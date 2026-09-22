/*
 ESP32-S3 Trainer Controller: BLE Central & Telemetry Bridge
 =========================================================================
  Core Architecture:
  - Dual-Core Execution: BLE/Radio on Core 1, Serial/PC Link on Core 0.
  - Thread Safety: Mutex-protected shared sensor and speed variables.
  - Memory: Optimized stack usage with static telemetry allocation.

  Logic flow:
  1. Sensors: Reads analog brake force (0-4095) and digital LKA toggle.
  2. BLE: Connects to TACX via MAC; initializes FTMS Control Point (0x2AD9).
  3. Control: Maps 0-200% brake sum to 0-1000 resistance units (OpCode 0x04).
  4. Telemetry: Streams 21-byte packed binary packets to PC at 50Hz.

  Serial Packet Structure:
  [0xAA][0xBB] + [21 Bytes Telemetry] + [0xCC]

  LED Status:
  - RED: Disconnected / Initializing
  - YELLOW: Searching for TACX MAC Address
  - BLUE: BLE Radio Link established (Service Discovery)
  - PURPLE: FTMS Handshake in progress (Requesting Control)
  - GREEN: Fully Synchronized & Control Active
 */


#include <Arduino.h>
#include <NimBLEDevice.h>

// === configuration ===
#define SERIAL_BAUDRATE         115200
#define BLE_TIMING_INTERVAL_MS  20      
#define PC_SEND_INTERVAL_MS     20      

// === TACX-II connection settings ===
const char* TARGET_MAC_ADDR = "d3:42:b3:d4:26:1A";
// 0 = Public, 1 = Random. This Trainer uses random for some reason.
uint8_t BLE_ADDR_TYPE = 1; 

// === esp32 sensor pins ===
#define FRONTBRAKE_PIN 1
#define REARBRAKE_PIN  2
#define LKA_SWITCH_PIN 5

// === led control ===
#define RGB_BUILTIN     48
#define RGB_BRIGHTNESS  255

// Data packet for PC communication (Packed to ensure no padding bytes)
struct __attribute__((packed)) TelemetryData {
    float speed;          
    float frontBrake;     
    float rearBrake;      
    uint16_t resistance;  
    uint8_t lkaSwitch;    
    uint32_t blePeriod;   
    uint32_t serialPeriod;
};

// === ble globals ===
const char* FTMS_SERVICE_UUID = "1826";           // Fitness Machine Service
const char* INDOOR_BIKE_DATA_CHAR_UUID = "2ad2";  // Speed/Power data
const char* CONTROL_POINT_CHAR_UUID  = "2ad9";    // Resistance control

static NimBLEClient* pClient = nullptr;
static NimBLERemoteCharacteristic* pIndoorBikeDataChar = nullptr;
static NimBLERemoteCharacteristic* pControlPointChar = nullptr;
volatile bool bleConnected = false;
volatile bool fCPinit = false;

// Shared data protected by Mutex
SemaphoreHandle_t xSensorMutex;
volatile float speedf = 0.0; 
volatile float frontBrakeForce = 0.0; 
volatile float rearBrakeForce  = 0.0; 
volatile bool  lkaSwitchState  = false; 
volatile uint16_t resistanceValue = 0; 
volatile uint32_t bleActualPeriodMs    = 0; 

// === sensor logic ===
void readSensors(float &frontBrake, float &rearBrake, bool &switchState) {
    // Convert 12-bit ADC (0-4095) to 0-100% scale
    frontBrake = map(analogRead(FRONTBRAKE_PIN), 0, 4095, 0, 100);
    rearBrake  = map(analogRead(REARBRAKE_PIN), 0, 4095, 0, 100);
    switchState = (digitalRead(LKA_SWITCH_PIN) == HIGH); 
}

uint16_t calculateResistance(float frontBrake, float rearBrake) {
    float totalBrake = frontBrake + rearBrake;
    // Map total brake force to FTMS resistance range (0-1000)
    return map(totalBrake, 0.0, 200.0, 0, 1000);
}

// === ble connection logic ===
void writeResistanceToTacx(uint16_t resistance) {
    if (!bleConnected || !fCPinit || !pControlPointChar) return;
    // Opcode 0x04 = Set Target Resistance, followed by uint16 little-endian
    uint8_t payload[3] = { 0x04, (uint8_t)(resistance & 0xFF), (uint8_t)((resistance >> 8) & 0xFF) };
    pControlPointChar->writeValue(payload, 3, false);
}

void initFitnessMachineControlPoint() {
    if (!pControlPointChar || fCPinit) return;
    rgbLedWrite(RGB_BUILTIN, RGB_BRIGHTNESS, 0, RGB_BRIGHTNESS); // Purple: Handshaking
    
    // Request Control
    uint8_t requestControl[] = {0x00};
    if (pControlPointChar->writeValue(requestControl, 1, true)) {
        vTaskDelay(pdMS_TO_TICKS(200)); 
    }

    // Start/Reset the trainer
    uint8_t startCmd[] = {0x07};
    if (pControlPointChar->writeValue(startCmd, 1, true)) {
        vTaskDelay(pdMS_TO_TICKS(200));
        writeResistanceToTacx(0);
        fCPinit = true;
        rgbLedWrite(RGB_BUILTIN, 0, RGB_BRIGHTNESS, 0); // Green: Fully Ready
    }
}

// Callback for incoming trainer data (speed)
static void notifyCallback(NimBLERemoteCharacteristic* pChar, uint8_t* pData, size_t len, bool isNotify) {
    if (len > 3) {
        // FTMS Speed is in 0.01 km/h increments
        uint16_t rawSpeed = (uint16_t)pData[2] | ((uint16_t)pData[3] << 8);
        if (xSemaphoreTake(xSensorMutex, portMAX_DELAY) == pdTRUE) {
            speedf = rawSpeed * 0.01f;
            xSemaphoreGive(xSensorMutex);
        }
    }
}

class ClientCallbacks : public NimBLEClientCallbacks {
    void onConnect(NimBLEClient* p) override { bleConnected = true; }
    void onDisconnect(NimBLEClient* p, int reason) override { 
        bleConnected = false; 
        fCPinit = false;
        pIndoorBikeDataChar = nullptr; // Clear characteristic pointers
        pControlPointChar = nullptr;   
        
        NimBLEDevice::deleteClient(p); // Wipe memory and free RAM
        pClient = nullptr;             // Reset global client pointer
        
        rgbLedWrite(RGB_BUILTIN, RGB_BRIGHTNESS, 0, 0); // Red: Disconnected
    }
};

void connectToTacx() {
    // Define address using user-specified type (Public/Random)
    NimBLEAddress targetAddr(TARGET_MAC_ADDR, BLE_ADDR_TYPE);
    
    rgbLedWrite(RGB_BUILTIN, RGB_BRIGHTNESS, 100, 0); // Yellow: Searching

    pClient = NimBLEDevice::createClient();
    pClient->setClientCallbacks(new ClientCallbacks());

    // Direct connection via MAC address (skips scanning)
    if (pClient->connect(targetAddr)) {
        rgbLedWrite(RGB_BUILTIN, 0, 0, RGB_BRIGHTNESS); // Blue: Radio linked
        
        NimBLERemoteService* pSvc = pClient->getService(FTMS_SERVICE_UUID);
        if (pSvc) {
            pIndoorBikeDataChar = pSvc->getCharacteristic(INDOOR_BIKE_DATA_CHAR_UUID);
            pControlPointChar = pSvc->getCharacteristic(CONTROL_POINT_CHAR_UUID);
            
            if (pIndoorBikeDataChar && pControlPointChar) {
                pIndoorBikeDataChar->subscribe(true, notifyCallback);
                initFitnessMachineControlPoint(); 
                return;
            }
        }
        pClient->disconnect();
    }
    
    // Clean up if connection or service discovery fails
    NimBLEDevice::deleteClient(pClient);
    pClient = nullptr;
    rgbLedWrite(RGB_BUILTIN, RGB_BRIGHTNESS, 0, 0);
}

// === tasks ===
void bluetoothTask(void *pvParameters) {
    uint32_t lastTime = millis();
    while (1) {
        uint32_t now = millis();
        if (!bleConnected) {
            connectToTacx(); 
        } else if (fCPinit) {
            float currentFront, currentRear; 
            bool currentSwitch;
            readSensors(currentFront, currentRear, currentSwitch);
            uint16_t newResistance = calculateResistance(currentFront, currentRear);

            if (xSemaphoreTake(xSensorMutex, portMAX_DELAY) == pdTRUE) {
                bleActualPeriodMs = now - lastTime;
                frontBrakeForce = currentFront;
                rearBrakeForce = currentRear;
                lkaSwitchState = currentSwitch;
                resistanceValue = newResistance;
                xSemaphoreGive(xSensorMutex); 
            }
            writeResistanceToTacx(newResistance); 
            lastTime = now;
        }
        vTaskDelay(pdMS_TO_TICKS(BLE_TIMING_INTERVAL_MS));
    }
}

void serialToPcTask(void *pvParameters) {
    uint32_t lastTime = millis();
    static TelemetryData data; // Static to reduce stack usage
    while (1) {
        uint32_t now = millis();
        uint32_t currentSerialPeriod = now - lastTime;
        lastTime = now;

        if (xSemaphoreTake(xSensorMutex, portMAX_DELAY) == pdTRUE) {
            data.speed = speedf;
            data.frontBrake = frontBrakeForce;
            data.rearBrake = rearBrakeForce;
            data.resistance = resistanceValue;
            data.lkaSwitch = lkaSwitchState ? 1 : 0;
            data.blePeriod = bleActualPeriodMs;
            data.serialPeriod = currentSerialPeriod;
            xSemaphoreGive(xSensorMutex); 
        }

        // Send binary packet with sync bytes
        Serial.write(0xAA); 
        Serial.write(0xBB);
        Serial.write((uint8_t*)&data, sizeof(data));
        Serial.write(0xCC);
        vTaskDelay(pdMS_TO_TICKS(PC_SEND_INTERVAL_MS)); 
    }
}

void setup() {
    Serial.begin(SERIAL_BAUDRATE);
    pinMode(FRONTBRAKE_PIN, INPUT);
    pinMode(REARBRAKE_PIN, INPUT);
    pinMode(LKA_SWITCH_PIN, INPUT_PULLDOWN);

    NimBLEDevice::init("");
    NimBLEDevice::setPower(ESP_PWR_LVL_P9); // Set max Bluetooth power

    xSensorMutex = xSemaphoreCreateMutex();

    // Core 1: Bluetooth/Radio management | Core 0: Serial/PC communication
    xTaskCreatePinnedToCore(bluetoothTask, "BLETask", 16*1024, NULL, 5, NULL, 1);
    xTaskCreatePinnedToCore(serialToPcTask, "SerialTask", 8*1024, NULL, 3, NULL, 0);
}

void loop() {
    // Empty: Everything is handled in FreeRTOS tasks
}
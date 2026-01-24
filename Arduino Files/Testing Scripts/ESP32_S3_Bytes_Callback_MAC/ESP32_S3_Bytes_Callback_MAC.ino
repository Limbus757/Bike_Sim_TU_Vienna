#include <Arduino.h>
#include <NimBLEDevice.h>

// === configuration ===
#define SERIAL_BAUDRATE         115200
#define BLE_TIMING_INTERVAL_MS  20      
#define PC_SEND_INTERVAL_MS     20      

// === TACX-II mac address ===
const char* TARGET_MAC_ADDR = "aa:bb:cc:dd:ee:ff";

// === esp32 sensor pins ===
#define FRONTBRAKE_PIN 1
#define REARBRAKE_PIN  2
#define LKA_SWITCH_PIN 5

// === led control ===
#define RGB_BUILTIN     48
#define RGB_BRIGHTNESS  255

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
const char* FTMS_SERVICE_UUID = "1826";
const char* INDOOR_BIKE_DATA_CHAR_UUID = "2ad2";
const char* CONTROL_POINT_CHAR_UUID  = "2ad9";

static NimBLEClient* pClient = nullptr;
static NimBLERemoteCharacteristic* pIndoorBikeDataChar = nullptr;
static NimBLERemoteCharacteristic* pControlPointChar = nullptr;
volatile bool bleConnected = false;
volatile bool fCPinit = false;

SemaphoreHandle_t xSensorMutex;
volatile float speedf = 0.0; 
volatile float frontBrakeForce = 0.0; 
volatile float rearBrakeForce  = 0.0; 
volatile bool  lkaSwitchState  = false; 
volatile uint16_t resistanceValue = 0; 
volatile uint32_t bleActualPeriodMs    = 0; 

// === sensor logic ===
void readSensors(float &frontBrake, float &rearBrake, bool &switchState) {
    frontBrake = map(analogRead(FRONTBRAKE_PIN), 0, 4095, 0, 100);
    rearBrake  = map(analogRead(REARBRAKE_PIN), 0, 4095, 0, 100);
    switchState = (digitalRead(LKA_SWITCH_PIN) == HIGH); 
}

uint16_t calculateResistance(float frontBrake, float rearBrake) {
    float totalBrake = frontBrake + rearBrake;
    return map(totalBrake, 0.0, 200.0, 0, 1000);
}

// === ble connection logic ===
void writeResistanceToTacx(uint16_t resistance) {
    if (!bleConnected || !fCPinit || !pControlPointChar) return;
    uint8_t payload[3] = { 0x04, (uint8_t)(resistance & 0xFF), (uint8_t)((resistance >> 8) & 0xFF) };
    pControlPointChar->writeValue(payload, 3, false);
}

void initFitnessMachineControlPoint() {
    if (!pControlPointChar || fCPinit) return;
    rgbLedWrite(RGB_BUILTIN, RGB_BRIGHTNESS, 0, RGB_BRIGHTNESS); // handshaking: purple
    
    uint8_t requestControl[] = {0x00};
    if (pControlPointChar->writeValue(requestControl, 1, true)) {
        vTaskDelay(pdMS_TO_TICKS(200)); 
    }

    uint8_t startCmd[] = {0x07};
    if (pControlPointChar->writeValue(startCmd, 1, true)) {
        vTaskDelay(pdMS_TO_TICKS(200));
        writeResistanceToTacx(0);
        fCPinit = true;
        rgbLedWrite(RGB_BUILTIN, 0, RGB_BRIGHTNESS, 0); // connected: green
    }
}

static void notifyCallback(NimBLERemoteCharacteristic* pChar, uint8_t* pData, size_t len, bool isNotify) {
    if (len > 3) {
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
        pIndoorBikeDataChar = nullptr;
        pControlPointChar = nullptr;
        NimBLEDevice::deleteClient(p);
        pClient = nullptr;
        rgbLedWrite(RGB_BUILTIN, RGB_BRIGHTNESS, 0, 0); // disconnected: red
    }
};

void connectToTacx() {
    NimBLEAddress targetAddr(TARGET_MAC_ADDR);
    
    // stage: red/yellow (searching specifically for mac)
    rgbLedWrite(RGB_BUILTIN, RGB_BRIGHTNESS, 100, 0); 

    pClient = NimBLEDevice::createClient();
    pClient->setClientCallbacks(new ClientCallbacks());

    // direct connection via address is much faster than scanning first
    if (pClient->connect(targetAddr)) {
        rgbLedWrite(RGB_BUILTIN, 0, 0, RGB_BRIGHTNESS); // radio link: blue
        
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
    
    // if we reach here, connection failed
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
    TelemetryData data; 
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
    NimBLEDevice::setPower(ESP_PWR_LVL_P9);

    xSensorMutex = xSemaphoreCreateMutex();

    xTaskCreatePinnedToCore(bluetoothTask, "BLETask", 16*1024, NULL, 5, NULL, 1);
    xTaskCreatePinnedToCore(serialToPcTask, "SerialTask", 8*1024, NULL, 3, NULL, 0);
}

void loop() {}
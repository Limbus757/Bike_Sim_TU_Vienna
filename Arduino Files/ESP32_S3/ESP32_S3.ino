/*
 * ESP32-S3 CODE: BLE Central, Brake Input, PC TX
 * * This sketch runs on the ESP32-S3.
 * Role: BLE I/O (Read Speed, Calculate/Write Resistance), reads brake sensors locally, 
 * and sends Speed/Brake/Switch Telemetry (TX ONLY) to PC.
 *
 * LED Status Integrated into bluetoothTask:
 * - Solid Red: Scanning/Disconnected (WAS: Flashing Red)
 * - Solid Blue: Connecting
 * - Solid Cyan: Subscribed to Data
 * - Solid Green: Fully Ready/Control Active
 *
 * NOTE: Brake sensors and the physical switch must now be wired directly to the ESP32 board.
 *
 * ESP32 Local Sensor Pins:
 * - Analog Input: FRONTBRAKE_PIN (e.g., GPIO 36)
 * - Analog Input: REARBRAKE_PIN (e.g., GPIO 39)
 * - Digital Input: LKA_SWITCH_PIN (e.g., GPIO 2, with INPUT_PULLUP)
 *
 * PC Communication (Serial0/USB):
 * - RX from PC: NONE (TX ONLY)
 * - TX to PC (Full Telemetry): "U,speed,frontBrake,rearBrake,switchState,resistance,blePeriod,serialPeriod" (ms)
 * (e.g., "U,15.20,50.0,20.5,1,3.5,50,50")
 */

#include <Arduino.h>
#include <NimBLEDevice.h>
#include <Arduino_FreeRTOS.h> 

// --- Configuration ---
#define SERIAL_BAUDRATE 115200          // Baud rate for PC (Serial0)
#define BLE_TIMING_INTERVAL_MS 50       // BLE control loop interval (20 Hz)
#define PC_SEND_INTERVAL_MS 50          // High speed update to PC (20 Hz)

// --- ESP32 Sensor Pins ---
#define FRONTBRAKE_PIN 36 
#define REARBRAKE_PIN 39  
#define LKA_SWITCH_PIN 2      

// --- LED Control Constants (Adjust GPIO and Brightness as needed) ---
#define RGB_BUILTIN 48          // Common GPIO for built-in RGB LED on ESP32-S3
#define RGB_BRIGHTNESS 255      // Max brightness (assuming 8-bit color)

// --- Connection Stage Definitions ---
const int STAGE_SCANNING = 0;       // Red Solid
const int STAGE_CONNECTING = 1;     // Blue Solid
const int STAGE_SUBSCRIBED = 2;     // Cyan Solid
const int STAGE_READY = 3;          // Green Solid

// --- BLE Constants ---
const char* TACX_NAME = "Tacx Flux-2 36688";
const char* FTMS_SERVICE_UUID = "1826";
const char* INDOOR_BIKE_DATA_CHAR_UUID = "2ad2"; // Speed/etc.
const char* CONTROL_POINT_CHAR_UUID = "2ad9"; // Resistance control

// --- BLE Globals (NimBLE) ---
static NimBLEClient* pClient = nullptr;
static NimBLERemoteService* pRemoteService = nullptr;
static NimBLERemoteCharacteristic* pIndoorBikeDataChar = nullptr;
static NimBLERemoteCharacteristic* pControlPointChar = nullptr;
static volatile bool bleConnected = false;
static volatile bool fCPinit = false; // Fitness Machine Control Point initialized state

// --- Shared Data Variables ---
volatile float speedf = 0.0; 
volatile float frontBrakeForce = 0.0;
volatile float rearBrakeForce = 0.0;
volatile bool lkaSwitchState = false;
volatile float resistance = 0.0; 
volatile uint32_t bleActualPeriodMs = 0;   // Measured period (ms) for BLE task
volatile uint32_t serialActualPeriodMs = 0; // Measured period (ms) for Serial task

// --- LED State Variable ---
volatile int connectionStage = STAGE_SCANNING; 

// --- FreeRTOS Objects ---
SemaphoreHandle_t xDataMutex; // Protects all shared variables

// --- NimBLE Callback Handlers ---
static void notifyCallback(NimBLERemoteCharacteristic* pBLERemoteCharacteristic, uint8_t* pData, size_t length, bool isNotify) {
    if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
        if (length > 3) {
            uint16_t rawSpeed = (uint16_t)pData[2] | ((uint16_t)pData[3] << 8);
            speedf = rawSpeed * 0.01;
        } else {
            Serial.println("[BLE RX ERR] Invalid Speed packet size.");
        }
        xSemaphoreGive(xDataMutex);
    }
}

class ClientCallbacks : public NimBLEClientCallbacks {
    void onConnect(NimBLEClient* pClient) {
        // This is handled by the main task loop, but we set the flag here
        if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
            bleConnected = true;
            // The connectionStage will be updated to STAGE_SUBSCRIBED or STAGE_CONNECTING 
            // once the characteristics are found and subscribed in connectToTACXII().
            xSemaphoreGive(xDataMutex);
        }
        Serial.println("[BLE] Connection successful.");
    }

    void onDisconnect(NimBLEClient* pClient) {
        if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
            bleConnected = false;
            fCPinit = false; // Reset control state
            connectionStage = STAGE_SCANNING; // Reset stage immediately
            xSemaphoreGive(xDataMutex);
        }
        Serial.println("[BLE] Disconnected. Restarting scan.");
    }
};

// Helper structure for local sensor reads
struct SensorReadings {
    float frontBrake;
    float rearBrake;
    bool lkaSwitch;
};

/**
 * @brief Reads the local brake sensor inputs and the physical switch state.
 * @return Struct containing the latest sensor readings.
 */
SensorReadings readSensorInputs() {
    SensorReadings readings;
    
    // Read analog values and map to 0-100% force
    // Assuming 12-bit ADC (0-4095) for ESP32
    float rawFB = (float)analogRead(FRONTBRAKE_PIN);
    float rawRB = (float)analogRead(REARBRAKE_PIN);
    
    // Scale analog reading (0-4095) to force percentage (0-100)
    readings.frontBrake = map(rawFB, 0, 4095, 0, 100); 
    readings.rearBrake = map(rawRB, 0, 4095, 0, 100); 

    // Read the digital switch (assuming INPUT_PULLUP, so LOW is active/true)
    readings.lkaSwitch = (digitalRead(LKA_SWITCH_PIN) == LOW); 
    
    return readings;
}


/**
 * @brief Calculates a resistance value (0.0 to 10.0) based purely on brake input.
 */
float calculateResistance(float speed, float f_brake, float r_brake) {
    float totalBrakeForce = f_brake + r_brake;
    float newResistance = (totalBrakeForce / 200.0f) * 10.0f; 
    return constrain(newResistance, 0.0f, 10.0f);
}

void initFitnessMachineControlPoint() {
    if (!pControlPointChar) return;

    bool init = false;
    if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
        init = fCPinit;
        xSemaphoreGive(xDataMutex);
    }
    if (init) return;

    Serial.println("[BLE] Initializing FMCP...");
    uint8_t FMCPCstart[] = {0x07}; 
    if (pControlPointChar->writeValue(FMCPCstart, 1, true)) { 
        Serial.println("[BLE] Requested FMCPC Control (0x07).");
    } else {
        Serial.println("[BLE ERR] Failed to request FMCPC Control.");
        return;
    }
    vTaskDelay(pdMS_TO_TICKS(100)); 
    writeResitancetoTACXII(0.0);
    
    // --- State Update 3: Ready ---
    if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
        fCPinit = true;
        connectionStage = STAGE_READY;
        rgbLedWrite(RGB_BUILTIN, 0, RGB_BRIGHTNESS, 0); // Green Solid
        xSemaphoreGive(xDataMutex);
    }

    Serial.println("[BLE] FMCP initialized and control established.");
}

void connectToTACXII() {
    bool connected = false;
    if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
        connected = bleConnected;
        xSemaphoreGive(xDataMutex);
    }
    if (connected) return;

    NimBLEScan* pScan = NimBLEDevice::getScan();
    pScan->setActiveScan(true);
    NimBLEScanResults results = pScan->start(5, false); 

    NimBLEAdvertisedDevice* advDevice = nullptr;
    for (int i = 0; i < results.getCount(); i++) {
        if (results.getDevice(i).getName() == TACX_NAME) {
            advDevice = new NimBLEAdvertisedDevice(results.getDevice(i));
            break;
        }
    }
    
    if (!advDevice) return;

    // --- State Update 1: Connecting ---
    if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
        connectionStage = STAGE_CONNECTING;
        rgbLedWrite(RGB_BUILTIN, 0, 0, RGB_BRIGHTNESS); // Blue Solid
        xSemaphoreGive(xDataMutex);
    }

    pClient = NimBLEDevice::createClient();
    pClient->setClientCallbacks(new ClientCallbacks());
    
    if (!pClient->connect(advDevice)) {
        Serial.println("[BLE ERR] Failed to connect.");
        delete advDevice;
        return;
    }

    pRemoteService = pClient->getService(FTMS_SERVICE_UUID);
    if (!pRemoteService) {
        Serial.println("[BLE ERR] FTMS Service not found. Disconnecting.");
        pClient->disconnect();
        return;
    }

    pIndoorBikeDataChar = pRemoteService->getCharacteristic(INDOOR_BIKE_DATA_CHAR_UUID);
    pControlPointChar = pRemoteService->getCharacteristic(CONTROL_POINT_CHAR_UUID);

    if (pIndoorBikeDataChar && pIndoorBikeDataChar->canNotify()) {
        if (pIndoorBikeDataChar->subscribe(true, notifyCallback)) {
            // --- State Update 2: Subscribed ---
            if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
                connectionStage = STAGE_SUBSCRIBED;
                rgbLedWrite(RGB_BUILTIN, 0, RGB_BRIGHTNESS, RGB_BRIGHTNESS); // Cyan Solid
                xSemaphoreGive(xDataMutex);
            }
            initFitnessMachineControlPoint();
        } else {
            Serial.println("[BLE ERR] Failed to subscribe to 2ad2. Disconnecting.");
            pClient->disconnect();
        }
    } else {
        Serial.println("[BLE ERR] 2ad2 Char missing or cannot notify. Disconnecting.");
        pClient->disconnect();
    }

    delete advDevice;
}

/**
 * @brief Writes the resistance value to the Tacx trainer.
 */
void writeResitancetoTACXII(float resistanceValue) {
    // Check connection status safely
    bool connected = false;
    bool init = false;
    if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
        connected = bleConnected;
        init = fCPinit;
        xSemaphoreGive(xDataMutex);
    }

    if (!connected || !init || !pControlPointChar || !pControlPointChar->canWrite()) {
        return;
    }
    // Resistance value scaled by 40 (0.0 -> 0, 10.0 -> 400)
    long resistanceSupportRaw = (long)(resistanceValue * 40.0);
    resistanceSupportRaw = constrain(resistanceSupportRaw, 0L, 65535L);

    uint8_t resistanceLSB = (uint8_t)(resistanceSupportRaw & 0xFF);
    uint8_t resistanceMSB = (uint8_t)((resistanceSupportRaw >> 8) & 0xFF);

    uint8_t payload[3] = {
        0x04, // op-code for setting the resistance (Target Resistance Level)
        resistanceLSB,
        resistanceMSB
    };

    pControlPointChar->writeValue(payload, 3, false); 
}

// -----------------------------------------------------------
// FREE RTOS TASKS 
// -----------------------------------------------------------

// Task: Handles BLE connection, LED status, and applies calculated resistance.
void bluetoothTask(void *pvParameters) {
    Serial.println("[Task] Bluetooth starting...");
    float currentSpeed = 0.0;
    float currentFrontBrakeForce = 0.0;
    float currentRearBrakeForce = 0.0;
    
    uint32_t lastExecutionTime = millis(); // Initial timestamp for period calculation

    while (1) {
        // --- Calculate Actual Period ---
        uint32_t currentTime = millis();
        uint32_t period = currentTime - lastExecutionTime;
        lastExecutionTime = currentTime;
        // -------------------------------
        
        int currentStage;
        bool connected;
        bool init;
        
        // Read shared state variables safely
        if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
            // Update the measured period
            bleActualPeriodMs = period;

            currentStage = connectionStage;
            connected = bleConnected;
            init = fCPinit;
            xSemaphoreGive(xDataMutex);
        } else {
            currentStage = STAGE_SCANNING; 
            connected = false;
            init = false;
        }

        // --- Stage 0: Scanning/Disconnected (SOLID RED) ---
        if (currentStage == STAGE_SCANNING) {
            // Solid Red for scanning/disconnection status
            rgbLedWrite(RGB_BUILTIN, RGB_BRIGHTNESS, 0, 0); // Red (Solid)
            connectToTACXII(); // Attempt connection
        } 
        
        // --- Stages 1, 2, 3: Connected/Subscribed/Ready ---
        // The LED color is set to Solid Blue, Cyan, or Green during the transition functions.
        else if (connected && init) {
            // Fully Ready State (Stage 3): Normal operation
            
            // Read local sensor inputs
            SensorReadings inputs = readSensorInputs();

            // Update shared sensor variables and calculate resistance safely
            if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
                currentSpeed = speedf; 
                
                frontBrakeForce = inputs.frontBrake;
                rearBrakeForce = inputs.rearBrake;
                lkaSwitchState = inputs.lkaSwitch;

                currentFrontBrakeForce = frontBrakeForce;
                currentRearBrakeForce = rearBrakeForce;

                resistance = calculateResistance(currentSpeed, currentFrontBrakeForce, currentRearBrakeForce);
                xSemaphoreGive(xDataMutex);
            }

            // Apply the calculated resistance.
            writeResitancetoTACXII(resistance);
        }
        
        vTaskDelay(pdMS_TO_TICKS(BLE_TIMING_INTERVAL_MS));
    }
}


// Task: Sends data to the PC (TX ONLY to PC), including speed, brake force, switch state, and RESISTANCE.
void serialToPcTask(void *pvParameters) {
    Serial.println("[Task] SerialToPc starting...");
    
    uint32_t lastExecutionTime = millis(); // Initial timestamp for period calculation
    
    while (1) {
        // --- Calculate Actual Period ---
        uint32_t currentTime = millis();
        uint32_t period = currentTime - lastExecutionTime;
        lastExecutionTime = currentTime;
        // -------------------------------

        char outputString[120]; // Increased buffer size for two extra uint32_t fields
        float currentSpeed = 0.0;
        float currentFrontBrakeForce = 0.0;
        float currentRearBrakeForce = 0.0;
        int currentSwitchState = 0; // 0 or 1
        float currentResistance = 0.0; 
        uint32_t currentBlePeriod = 0;
        uint32_t currentSerialPeriod = 0;

        // Acquire mutex to read the shared data and update period
        if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
            // Update the measured period
            serialActualPeriodMs = period;

            currentSpeed = speedf; 
            currentFrontBrakeForce = frontBrakeForce;
            currentRearBrakeForce = rearBrakeForce;
            currentResistance = resistance; 
            currentSwitchState = lkaSwitchState ? 1 : 0; 

            // Read the measured periods from both tasks
            currentBlePeriod = bleActualPeriodMs;
            currentSerialPeriod = serialActualPeriodMs;
            
            xSemaphoreGive(xDataMutex);
        } else {
            Serial.println("[TX ERR] Failed to acquire xDataMutex for PC send.");
        }

        // --- PC Protocol: "U,speed,frontBrake,rearBrake,switchState,resistance,blePeriod,serialPeriod" ---
        // Example: U,15.20,50.0,20.5,1,3.5,50,50
        snprintf(outputString, sizeof(outputString), "U,%.2f,%.1f,%.1f,%d,%.1f,%lu,%lu", // Use %lu for uint32_t
                 currentSpeed, currentFrontBrakeForce, currentRearBrakeForce, currentSwitchState, currentResistance,
                 currentBlePeriod, currentSerialPeriod);
                 
        Serial.println(outputString);

        vTaskDelay(pdMS_TO_TICKS(PC_SEND_INTERVAL_MS));
    }
}


void setup() {
    // Serial0 (USB) for PC communication and debugging
    Serial.begin(SERIAL_BAUDRATE);
    while (!Serial) { vTaskDelay(1); }
    
    // Initialize Analog pins for local brake sensor reading
    pinMode(FRONTBRAKE_PIN, INPUT);
    pinMode(REARBRAKE_PIN, INPUT);
    // Initialize Digital pin for switch reading
    pinMode(LKA_SWITCH_PIN, INPUT_PULLUP); // Assuming switch connects to GND

    Serial.println("[SYSTEM] Initializing NimBLE...");
    NimBLEDevice::init("");
    NimBLEDevice::setPower(ESP_PWR_LVL_P9);

    xDataMutex = xSemaphoreCreateMutex();

    if (!xDataMutex) {
        Serial.println("[ERROR] Failed to create FreeRTOS Mutex!");
        while (1);
    }

    // Start FreeRTOS Tasks
    xTaskCreate(bluetoothTask, "BLETask", 4096, NULL, 5, NULL); 
    xTaskCreate(serialToPcTask, "SerialToPcTask", 2048, NULL, 3, NULL);
    vTaskStartScheduler();
}

void loop() {
    // Empty on FreeRTOS systems
}

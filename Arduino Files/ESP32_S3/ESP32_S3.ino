/*
 * ESP32-S3 CODE: BLE Central, Brake Input, PC TX
 * 
 * Role: 
 * - Reads brake and switch sensors locally
 * - Calculates resistance and sends control over BLE
 * - Streams telemetry to PC at 50 Hz (TX only)
 * 
 * LED Status:
 * - Red: Scanning / Disconnected
 * - Blue: Connecting
 * - Green: Fully Ready (Control Active)
 */

#include <Arduino.h>
#include <NimBLEDevice.h>

// --- Configuration ---
#define SERIAL_BAUDRATE        115200
#define BLE_TIMING_INTERVAL_MS 20      // 50 Hz
#define PC_SEND_INTERVAL_MS    20      // 50 Hz

// --- ESP32 Sensor Pins ---
#define FRONTBRAKE_PIN 36
#define REARBRAKE_PIN  39
#define LKA_SWITCH_PIN 2

// --- LED Control ---
#define RGB_BUILTIN     48+--
#define RGB_BRIGHTNESS  255

// --- BLE Constants ---
const char* TACX_NAME = "Tacx Flux-2 36688";
const char* FTMS_SERVICE_UUID = "1826";
const char* INDOOR_BIKE_DATA_CHAR_UUID = "2ad2";
const char* CONTROL_POINT_CHAR_UUID  = "2ad9";

// --- BLE Globals ---
static NimBLEClient* pClient = nullptr;
static NimBLERemoteService* pRemoteService = nullptr;
static NimBLERemoteCharacteristic* pIndoorBikeDataChar = nullptr;
static NimBLERemoteCharacteristic* pControlPointChar = nullptr;
volatile bool bleConnected = false;
volatile bool fCPinit = false;

// --- Shared sensor globals ---
SemaphoreHandle_t xSensorMutex;

volatile float speedf = 0.0;
volatile float frontBrakeForce = 0.0;
volatile float rearBrakeForce  = 0.0;
volatile bool  lkaSwitchState  = false;
volatile float resistanceValue = 0.0;
volatile uint32_t bleActualPeriodMs    = 0;
volatile uint32_t serialActualPeriodMs = 0;

// -----------------------------------------------------------
// LOCAL SENSOR HANDLING
// -----------------------------------------------------------
struct SensorReadings {
    float frontBrake;
    float rearBrake;
    bool lkaSwitch;
};

SensorReadings readSensorInputs() {
    SensorReadings readings;

    float rawFB = (float)analogRead(FRONTBRAKE_PIN);
    float rawRB = (float)analogRead(REARBRAKE_PIN);

    readings.frontBrake = map(rawFB, 0, 4095, 0, 100);
    readings.rearBrake  = map(rawRB, 0, 4095, 0, 100);
    readings.lkaSwitch  = (digitalRead(LKA_SWITCH_PIN) == LOW);

    return readings;
}

float calculateResistance(float speed, float f_brake, float r_brake) {
    float totalBrake = f_brake + r_brake;
    float res = (totalBrake / 200.0f) * 10.0f;
    return constrain(res, 0.0f, 10.0f);
}

// -----------------------------------------------------------
// BLE CALLBACKS
// -----------------------------------------------------------
static void notifyCallback(NimBLERemoteCharacteristic* pBLERemoteCharacteristic, uint8_t* pData, size_t length, bool isNotify) {
    if (length > 3) {
        uint16_t rawSpeed = (uint16_t)pData[2] | ((uint16_t)pData[3] << 8);
        float speed = rawSpeed * 0.01f;

        if (xSemaphoreTake(xSensorMutex, portMAX_DELAY) == pdTRUE) {
            speedf = speed;
            xSemaphoreGive(xSensorMutex);
        }
    } else {
        Serial.println("[BLE RX ERR] Invalid speed packet.");
    }
}

class ClientCallbacks : public NimBLEClientCallbacks {
    void onConnect(NimBLEClient* pClient) override {
        bleConnected = true;
        rgbLedWrite(RGB_BUILTIN, 0, 0, RGB_BRIGHTNESS); // Blue
        Serial.println("[BLE] Connected.");
    }

    void onDisconnect(NimBLEClient* pClient, int reason) override {
        bleConnected = false;
        fCPinit = false;
        rgbLedWrite(RGB_BUILTIN, RGB_BRIGHTNESS, 0, 0); // Red
        Serial.println("[BLE] Disconnected. Restarting scan.");
    }
};

// -----------------------------------------------------------
// BLE CONTROL FUNCTIONS
// -----------------------------------------------------------
void writeResistanceToTacx(float res) {
    if (!bleConnected || !fCPinit || !pControlPointChar || !pControlPointChar->canWrite())
        return;

    long rawRes = (long)(res * 40.0f);
    rawRes = constrain(rawRes, 0L, 65535L);
    uint8_t payload[3] = { 0x04, (uint8_t)(rawRes & 0xFF), (uint8_t)((rawRes >> 8) & 0xFF) };
    pControlPointChar->writeValue(payload, 3, false);
}

void initFitnessMachineControlPoint() {
    if (!pControlPointChar || fCPinit) return;

    Serial.println("[BLE] Initializing FMCP...");
    uint8_t startCmd[] = {0x07};

    if (pControlPointChar->writeValue(startCmd, 1, true)) {
        vTaskDelay(pdMS_TO_TICKS(100));
        writeResistanceToTacx(0.0);
        fCPinit = true;
        rgbLedWrite(RGB_BUILTIN, 0, RGB_BRIGHTNESS, 0); // Green
        Serial.println("[BLE] FMCP initialized.");
    } else {
        Serial.println("[BLE ERR] FMCP init failed.");
    }
}

void connectToTacx() {
    if (bleConnected) return;

    NimBLEScan* pScan = NimBLEDevice::getScan();
    pScan->setActiveScan(true);

    Serial.println("[BLE] Scanning for Tacx device...");
    pScan->start(10, false);
    NimBLEScanResults results = pScan->getResults();

    NimBLEAdvertisedDevice* advDevice = nullptr;
    for (int i = 0; i < results.getCount(); i++) {
        const NimBLEAdvertisedDevice* dev = results.getDevice(i);
        if (dev->getName() == TACX_NAME) {
            advDevice = const_cast<NimBLEAdvertisedDevice*>(dev);
            break;
        }
    }

    if (!advDevice) {
        Serial.println("[BLE] Tacx device not found.");
        return;
    }

    rgbLedWrite(RGB_BUILTIN, 0, 0, RGB_BRIGHTNESS); // Blue
    pClient = NimBLEDevice::createClient();
    pClient->setClientCallbacks(new ClientCallbacks());

    if (!pClient->connect(advDevice)) {
        Serial.println("[BLE ERR] Connect failed.");
        return;
    }

    pRemoteService = pClient->getService(FTMS_SERVICE_UUID);
    if (!pRemoteService) {
        Serial.println("[BLE ERR] FTMS Service not found.");
        pClient->disconnect();
        return;
    }

    pIndoorBikeDataChar = pRemoteService->getCharacteristic(INDOOR_BIKE_DATA_CHAR_UUID);
    pControlPointChar   = pRemoteService->getCharacteristic(CONTROL_POINT_CHAR_UUID);

    if (pIndoorBikeDataChar && pIndoorBikeDataChar->canNotify()) {
        pIndoorBikeDataChar->subscribe(true, notifyCallback);
        initFitnessMachineControlPoint();
    } else {
        Serial.println("[BLE ERR] Missing 2ad2 characteristic.");
        pClient->disconnect();
    }
}

// -----------------------------------------------------------
// TASKS
// -----------------------------------------------------------
void bluetoothTask(void *pvParameters) {
    Serial.println("[Task] Bluetooth starting...");
    uint32_t lastTime = millis();

    while (1) {
        uint32_t now = millis();
        if (xSemaphoreTake(xSensorMutex, portMAX_DELAY) == pdTRUE) {
            bleActualPeriodMs = now - lastTime;
            xSemaphoreGive(xSensorMutex);
        }
        lastTime = now;

        if (!bleConnected) {
            rgbLedWrite(RGB_BUILTIN, RGB_BRIGHTNESS, 0, 0); // Red
            connectToTacx();
        } 
        else if (bleConnected && fCPinit) {
            rgbLedWrite(RGB_BUILTIN, 0, RGB_BRIGHTNESS, 0); // Green

            SensorReadings inputs = readSensorInputs();
            float localSpeed;

            if (xSemaphoreTake(xSensorMutex, portMAX_DELAY) == pdTRUE) {
                localSpeed = speedf;
                frontBrakeForce = inputs.frontBrake;
                rearBrakeForce  = inputs.rearBrake;
                lkaSwitchState  = inputs.lkaSwitch;
                xSemaphoreGive(xSensorMutex);
            }

            float newRes = calculateResistance(localSpeed, inputs.frontBrake, inputs.rearBrake);

            if (xSemaphoreTake(xSensorMutex, portMAX_DELAY) == pdTRUE) {
                resistanceValue = newRes;
                xSemaphoreGive(xSensorMutex);
            }

            writeResistanceToTacx(newRes);
        }

        vTaskDelay(pdMS_TO_TICKS(BLE_TIMING_INTERVAL_MS));
    }
}

void serialToPcTask(void *pvParameters) {
    Serial.println("[Task] SerialToPc starting...");
    uint32_t lastTime = millis();

    // Pre-allocate a buffer on the stack (128 bytes should be enough)
    char buf[128];

    while (1) {
        uint32_t now = millis();
        serialActualPeriodMs = now - lastTime;
        lastTime = now;

        float localSpeed, localFront, localRear, localRes;
        bool  localLKA;
        uint32_t localBlePeriod;

        // Take mutex to safely copy shared variables
        if (xSemaphoreTake(xSensorMutex, portMAX_DELAY) == pdTRUE) {
            localSpeed     = speedf;
            localFront     = frontBrakeForce;
            localRear      = rearBrakeForce;
            localLKA       = lkaSwitchState;
            localRes       = resistanceValue;
            localBlePeriod = bleActualPeriodMs;
            xSemaphoreGive(xSensorMutex);
        }


        int len = snprintf(buf, sizeof(buf),
            "U,%.2f,%.1f,%.1f,%d,%.1f,%lu,%lu\n",
            localSpeed,
            localFront,
            localRear,
            localLKA ? 1 : 0,
            localRes,
            localBlePeriod,
            serialActualPeriodMs
        );

        // Print buffer
        if (len > 0) Serial.print(buf);

        vTaskDelay(pdMS_TO_TICKS(PC_SEND_INTERVAL_MS));
    }
}

// -----------------------------------------------------------
// SETUP / LOOP
// -----------------------------------------------------------
void setup() {
    Serial.begin(SERIAL_BAUDRATE);
    while (!Serial) { vTaskDelay(1); }

    pinMode(FRONTBRAKE_PIN, INPUT);
    pinMode(REARBRAKE_PIN, INPUT);
    pinMode(LKA_SWITCH_PIN, INPUT_PULLUP);

    Serial.println("[SYSTEM] Initializing NimBLE...");
    NimBLEDevice::init("");
    NimBLEDevice::setPower(ESP_PWR_LVL_P9); // Max TX power

    xSensorMutex = xSemaphoreCreateMutex();
    if (!xSensorMutex) {
        Serial.println("[ERROR] Mutex create failed!");
        while (1);
    }

    // Tasks: BLE -> Core 1, Serial -> Core 0
    xTaskCreatePinnedToCore(bluetoothTask, "BLETask", 16*1024, NULL, 5, NULL, 1);
    xTaskCreatePinnedToCore(serialToPcTask, "SerialToPcTask", 8*1024, NULL, 3, NULL, 0);
}


void loop() {
    // Unused with FreeRTOS
}

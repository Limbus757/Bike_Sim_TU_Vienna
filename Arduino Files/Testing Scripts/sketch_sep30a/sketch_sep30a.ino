#include <Arduino_FreeRTOS.h>
#include <ArduinoBLE.h>
#include <Arduino_LED_Matrix.h>

// Pins
#define FRONTBRAKE_PIN A3
#define BACKBRAKE_PIN A4
#define PMW_PIN1 3 // placeholder
#define PWM_Pin2 5  // placeholder
#define SWITCH_PIN1 4 // placeholder

#define SERIAL_BAUDRATE 115200
#define SEND_TIMING_INTERVAL_MS 1000
#define RECEIVE_TIMING_INTERVAL_MS 1000
#define BLE_TIMING_INTERVAL_MS 1000

// BLE Constants
const char* TACX_NAME = "Tacx Flux-2 36688"; // Lab-Bike: "Tacx Flux-2 36688" || Forschungsfest-Bike: "Tacx Flux-2 18201"
const char* FTMS_SERVICE_UUID = "1826";
const char* INDOOR_BIKE_DATA_CHAR_UUID = "2ad2"; // Speed/etc.
const char* CONTROL_POINT_CHAR_UUID = "2ad9"; // Resistance control
bool FCPinit = false; // State for Fitness Machine Control Point initialization

// BLE Globals
BLEDevice tacxPeripheral;
BLECharacteristic indoorBikeDataCharacteristic;
BLECharacteristic FitnessMachineControlPointCharacteristic;

// System Status/Timing Variables (0=Inactive/Error, >0=Actual Sample Duration in ms)
volatile int serialSendActualMs = 0;
volatile int serialReceiveActualMs = 0;
volatile int bluetoothTaskActualMs = 0;

// Shared variables (to be sent over Serial)
volatile float speedf = 0.0;
volatile float steeringAngle = 0.0;
volatile float frontBrakeForce = 0.0;
volatile float rearBrakeForce = 0.0;
volatile float resistance = 0.0;
volatile int switchState = 0;

// Received placeholder values
volatile uint8_t handleMotorRightPMW = 0; 
volatile uint8_t handleMotorLeftPWM = 0;
volatile float placeholderValue3 = 0.0;
volatile float placeholderValue4 = 0.0;
volatile float placeholderValue5 = 0.0;

// Mutex for protecting shared variables
SemaphoreHandle_t xBikeDataMutex; 
SemaphoreHandle_t xCommandDataMutex;

// Stack overflow hook
extern "C" void vApplicationStackOverflowHook(TaskHandle_t xTask, char *pcTaskName) {
  Serial.print("[ERROR] Stack overflow in task: ");
  Serial.println(pcTaskName);
  while (1) { /* trap */ }
}

void readAnalogAndDigitalPins() {
  // Reads analog input and maps it to a 0-100 range for brake force
  frontBrakeForce = map(analogRead(FRONTBRAKE_PIN), 0, 1023, 0, 100);
  rearBrakeForce = map(analogRead(BACKBRAKE_PIN), 0, 1023, 0, 100);
  switchState = digitalRead(SWITCH_PIN1);
}

void calculateResitance() {
  resistance = map(((int)(frontBrakeForce + rearBrakeForce)), 0 , 200, 0, 10); 
}

void setupHandlebarVibrationMotors() {
  pinMode(PMW_PIN1, OUTPUT);
  pinMode(PWM_Pin2, OUTPUT);
}

void updateHandlebarVibrationMotor() {
  // Write the received 0-255 PWM values directly to the pins
  analogWrite(PMW_PIN1, handleMotorRightPMW);
  analogWrite(PWM_Pin2, handleMotorLeftPWM); 
}

void updateSteeringMotor() {
  // Placeholder for steering motor logic
}

// Attempts to establish a connection with the Unity application by sending an 'H' (Handshake)
// message and waiting indefinitely for an 'A' (Acknowledgment) reply.
void handshakeWithUnity() {
  Serial.println("[Handshake] Attempting to connect with Unity...");
  // until a successful acknowledgment is received.
  while (true) {
    // 1. Send Handshake Request 'H'
    Serial.println("H"); 

    // 2. Wait a moment for the reply
    delay(100); 

    // 3. Check for Acknowledgment 'A'
    if (Serial.available()) {
      String received = Serial.readStringUntil('\n');
      received.trim();
      if (received.startsWith("A")) {
        Serial.println("[Handshake] Acknowledgment received. Connection established.");
        return; // Handshake successful!
      }
    }
  }
}

void startSerial(){
  Serial.begin(SERIAL_BAUDRATE);
  while (!Serial) { ; }
  delay(500);
}

void createMutexes(){
  xBikeDataMutex = xSemaphoreCreateMutex();
  if (!xBikeDataMutex) {
    Serial.println("[ERROR] Failed to create xBikeDataMutex!");
    while (1);
  }

  xCommandDataMutex = xSemaphoreCreateMutex();
  if (!xCommandDataMutex) {
    Serial.println("[ERROR] Failed to create xCommandDataMutex!");
    while (1);
  }
}

//sets up BLE for scanning, traps in an infinite loop if failed
void initializeBLE() {
  if (!BLE.begin()) {
    Serial.println("[BLE ERROR] starting BLE failed! Trapped in infinite loop.");
    while (1);
  }
  Serial.println("[BLE] BLE initialized. Scanning...");
}

void connectToTACXII() {
  if (!tacxPeripheral.connected()) {
      Serial.println("[BLE] Looking for Tacx...");
      BLE.scan();
      
      BLEDevice peripheral = BLE.available();
      if (peripheral && peripheral.localName() == TACX_NAME) {
        BLE.stopScan();
        Serial.println("[BLE] Found Tacx: ");
        Serial.println("Adress" + peripheral.address());
        Serial.println("LocalName" + peripheral.localName());
        Serial.println("ServiceUUID" + peripheral.advertisedServiceUuid());

        Serial.println("[BLE] Connecting...");
        if (peripheral.connect()) {
          Serial.println("[BLE] Connected.");
          tacxPeripheral = peripheral;
          
          // Discover Service
          if (tacxPeripheral.discoverService(FTMS_SERVICE_UUID)) {
            // Get Characteristics
            indoorBikeDataCharacteristic = tacxPeripheral.characteristic(INDOOR_BIKE_DATA_CHAR_UUID);
            FitnessMachineControlPointCharacteristic = tacxPeripheral.characteristic(CONTROL_POINT_CHAR_UUID);

            if (indoorBikeDataCharacteristic.canSubscribe() && indoorBikeDataCharacteristic.subscribe()) {
              Serial.println("[BLE] Subscribed to Indoor Bike Data (2ad2).");
            } else {
              Serial.println("[BLE ERROR] Failed to subscribe to 2ad2.");
              tacxPeripheral.disconnect();
            }

            // Init Fitness Machine Control Point (2ad9)
            if (FitnessMachineControlPointCharacteristic.canWrite()) {
              uint8_t FMCPCreset = 0x00; // Reset Control Point (0x00)
              uint8_t FMCPCstart = 0x07; // Request Control (0x07)
              FitnessMachineControlPointCharacteristic.writeValue(FMCPCreset, 2);
              vTaskDelay(pdMS_TO_TICKS(100));

              if (FitnessMachineControlPointCharacteristic.valueUpdated()) {
                Serial.println("[BLE] Requested FMCPC Control");
              } else {
                Serial.println("[BLE] Couldn't Request FMCPC Control");
              }

              FitnessMachineControlPointCharacteristic.writeValue(FMCPCstart, 1);
              if (FitnessMachineControlPointCharacteristic.valueUpdated()) {
                Serial.println("[BLE] Started FMCPC Training");
              } else {
                Serial.println("[BLE] Couldn't Start FMCPC Training");
              }

              FCPinit = true;
            } else {
              Serial.println("[BLE ERROR] 2ad9 characteristic not writable.");
              tacxPeripheral.disconnect();
            }
          } else {
            Serial.println("[BLE ERROR] FTMS Service (1826) discovery failed.");
            tacxPeripheral.disconnect();
          }
        } else {
          Serial.println("[BLE ERROR] Failed to connect.");
        }
      }
    }
}

float readSpeedFromTACXII() {
  const byte* charValue = indoorBikeDataCharacteristic.value();
  size_t charValueSize = indoorBikeDataCharacteristic.valueSize();

  if (charValueSize > 3) {
      // The Speed value is an unsigned Integer 16-bit starting at byte index 2.
      // Byte 2 is LSB, Byte 3 is MSB
      uint16_t rawSpeed = (uint16_t)charValue[2] | ((uint16_t)charValue[3] << 8);
      return rawSpeed * 0.01;
  } else {
    Serial.println("[BLE ERROR] Failed to read Speed from TACX!");
    return 0.0;
  }
}

void writeResitancetoTACXII() {
  long resistanceSupportRaw = (long)(resistance * 40.0);
  resistanceSupportRaw = constrain(resistanceSupportRaw, 0L, 65535L);

  uint8_t resistanceLSB = (uint8_t)(resistanceSupportRaw & 0xFF); // Extract the lower 8 bits
  uint8_t resistanceMSB = (uint8_t)((resistanceSupportRaw >> 8) & 0xFF);  // Shift and extract the upper 8 bits

  uint8_t payload[3] = {
        0x04, //op-code for setting the resitance
        resistanceLSB,
        resistanceMSB
    };

  FitnessMachineControlPointCharacteristic.writeValue(payload, 3);
}

// Task: Handles BLE connection, data read, and resistance command write to Tacx
void bluetoothTask(void *pvParameters) {
  static uint32_t ulLastSendTime = 0; 
  bool FCPinit = false; 
  Serial.println("[Task] Bluetooth starting...");
  initializeBLE();

  uint8_t resistanceMSB = 0;
  uint8_t resistanceLSB = 0;
  int resistanceSupport = resistance * 40;
  
  
  while (1) {
    // discovery & connection logic
    connectToTACXII();
    // connected loop
    if (tacxPeripheral.connected() && FCPinit == 1) {
      // measure actual cycle time
      uint32_t ulCurrentTime = millis();
      uint32_t ulActualDuration = ulCurrentTime - ulLastSendTime;
      ulLastSendTime = ulCurrentTime;
      bluetoothTaskActualMs = (int)ulActualDuration; 

      if (xSemaphoreTake(xBikeDataMutex, portMAX_DELAY) == pdTRUE) {
        speedf = readSpeedFromTACXII();
        writeResitancetoTACXII();
        xSemaphoreGive(xBikeDataMutex);
      } else {
        Serial.println("[BLE ERROR] Failed to acquire xBikeDataMutex for control write.");
      }
    } else if (!tacxPeripheral.connected()) {
      // Reset initialization state if disconnected
      FCPinit = false;
    }
    
    // Check if the peripheral disconnected unexpectedly
    if (tacxPeripheral.connected() && !tacxPeripheral.localName()) {
        Serial.println("[BLE] Peripheral disconnected unexpectedly. Restarting scan.");
        tacxPeripheral.disconnect();
    }

    // Use a delay appropriate for the resistance control loop
    vTaskDelay(pdMS_TO_TICKS(BLE_TIMING_INTERVAL_MS));
  }
}

// Task: send shared variables over Serial
void serialSendTask(void *pvParameters) {
  char outputString[170]; 
  Serial.println("[Task] SerialSend starting...");
  
  // Static variable to store the time of the previous cycle's start
  static uint32_t ulLastSendTime = 0; 

  while (1) {
    if (xSemaphoreTake(xBikeDataMutex, portMAX_DELAY) == pdTRUE) {
      readAnalogAndDigitalPins();
      snprintf(outputString, sizeof(outputString),
               "U,%.2f;%.2f;%.2f;%.2f;%.2f,%d,%d,%d,%d", 
               speedf, steeringAngle, frontBrakeForce, rearBrakeForce, resistance, switchState,
               serialSendActualMs, serialReceiveActualMs, bluetoothTaskActualMs);
      xSemaphoreGive(xBikeDataMutex);
      Serial.println(outputString);

      // measure actualy cycle time
      uint32_t ulCurrentTime = millis();
      uint32_t ulActualDuration = ulCurrentTime - ulLastSendTime;
      ulLastSendTime = ulCurrentTime;
      serialSendActualMs = (int)ulActualDuration; 
    } else {
      Serial.println("[TX ERROR] Failed to acquire xBikeDataMutex.");
    }

    vTaskDelay(pdMS_TO_TICKS(SEND_TIMING_INTERVAL_MS));
  }
}

// Task: receive commands for motor controls over Serial
void serialReceiveTask(void *pvParameters) {
  Serial.println("[Task] SerialReceive starting...");
  char buffer[150]; 
  static uint32_t ulLastReceiveTime = 0; 

  while (1) {
    if (Serial.available()) {
      // Read the incoming message until a newline character
      String receivedString = Serial.readStringUntil('\n');
      receivedString.trim(); // Remove any leading/trailing whitespace

      // Copy String contents to the modifiable C-style char array for strtok
      receivedString.toCharArray(buffer, sizeof(buffer));

      char* token;
      int valueIndex = 0;
      float parsedValues[5]; // Array to temporarily hold 5 parsed float values

      // Start tokenizing the string using the semicolon ';' delimiter
      // Expected message format: "PWM1_Val;PWM2_Val;V3_Float;V4_Float;V5_Float"
      token = strtok(buffer, ";"); 

      while (token != NULL && valueIndex < 5) {
        // Convert the token string to a float and store it
        parsedValues[valueIndex] = atof(token);
        
        // Move to the next token
        token = strtok(NULL, ";");
        valueIndex++;
      }

      // --- Assignment Logic ---
      if (valueIndex == 5) {
        // Assign values to shared variables under mutex protection
        if (xSemaphoreTake(xCommandDataMutex, portMAX_DELAY) == pdTRUE) {
          
          // Assign first two values as uint8_t (0-255 PWM control)
          handleMotorRightPMW = (uint8_t)parsedValues[0]; 
          handleMotorLeftPWM = (uint8_t)parsedValues[1];
          
          // Assign remaining values as floats
          placeholderValue3 = parsedValues[2];
          placeholderValue4 = parsedValues[3];
          placeholderValue5 = parsedValues[4];
          
          xSemaphoreGive(xCommandDataMutex);

          // Update PWM pins immediately after receiving new commands
          updateHandlebarVibrationMotor(); 

          // Debugging output
          Serial.print("[RX] Received and assigned: ");
          Serial.print(handleMotorRightPMW); Serial.print(";");
          Serial.print(handleMotorLeftPWM); Serial.print(";");
          Serial.print(placeholderValue3); Serial.print(";");
          Serial.print(placeholderValue4); Serial.print(";");
          Serial.println(placeholderValue5);
          
        } else {
          Serial.println("[RX ERROR] Failed to acquire xCommandDataMutex.");
        }
      } else {
        Serial.print("[RX ERROR] Received incomplete message. Expected 5 values, got ");
        Serial.print(valueIndex);
        Serial.print(": '");
        Serial.print(receivedString);
        Serial.println("'");
      }

      // measure actualy cycle time
      uint32_t ulCurrentTime = millis();
      uint32_t ulActualDuration = ulCurrentTime - ulLastReceiveTime;
      ulLastReceiveTime = ulCurrentTime;
      serialReceiveActualMs = (int)ulActualDuration;
    }

    vTaskDelay(pdMS_TO_TICKS(RECEIVE_TIMING_INTERVAL_MS)); // Sleep to prevent blocking and allow other tasks to run
  }
}

void setup() {
  pinMode(FRONTBRAKE_PIN, INPUT);
  pinMode(BACKBRAKE_PIN, INPUT);
  pinMode(SWITCH_PIN1, INPUT_PULLUP); // Assuming a button/switch input

  setupHandlebarVibrationMotors();
  startSerial();
  handshakeWithUnity();
  createMutexes();

  // start FreeRTOS Tasks
  xTaskCreate(bluetoothTask, "BLETask", 1024, NULL, 1, NULL); 
  xTaskCreate(serialSendTask, "SerialSendTask", 512, NULL, 1, NULL); 
  xTaskCreate(serialReceiveTask, "SerialReceiveTask", 512, NULL, 1, NULL); 
  vTaskStartScheduler(); // starts FreeRTOS
}

void loop() {
  // empty; FreeRTOS tasks run
}

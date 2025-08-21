/*
  This version uses FreeRTOS to handle BLE, serial communication sending,
  and serial communication receiving on three separate tasks.
  This provides a more robust and responsive solution by preventing one process
  from blocking the other.

  The circuit:
  - Arduino Uno WiFi Rev2 board
*/

#include <ArduinoBLE.h>
#include <FreeRTOS_AVR.h> // Make sure this library is installed for your board
#include "Arduino_LED_Matrix.h"

//Pins
#define FRONTBRAKE_PIN A3
#define BACKBRAKE_PIN A4

// Data to be sent to Unity (volatile to ensure latest value is always read)
volatile float speedO = 0.0;
volatile float steeringAngle = 0.0;
volatile float frontBrakeForce = 0.0;
volatile float rearBrakeForce = 0.0;
volatile float resistance = 0.0;
volatile float pitch = 0.0;
volatile float roll = 0.0;

// BLE objects

"Tacx Flux-2 18201"
const char* deviceToConnect = "Tacx Flux-2 36688"
uint8_t speedLSB = 0x00;
uint8_t speedMSB = 0x00;
uint16_t speed = 0;
int FCPinit = 0;

// Fitness Machine Service Characteristics
BLECharacteristic indoorBikeDataCharacteristic;
BLECharacteristic fitnessMachineControlPointCharacteristic;

// Mutex to protect shared variables from simultaneous access by multiple tasks
SemaphoreHandle_t xDataMutex;

// --- Task Functions ---

/// <summary>
/// FreeRTOS Task for handling BLE communication with the bike.
/// </summary>
void bleMonitorTask(void *pvParameters) {
  // Task specific setup
  
  // Scan for peripheral and connect
  BLE.scan();

  // Task loop
  while (true) {
    BLEDevice peripheral = BLE.available();
    if (peripheral) {
      if (peripheral.localName() == deviceToConnect) {
        BLE.stopScan();
        monitorIndoorBikeData(peripheral);
        
        // Peripheral disconnected, start scanning again
        BLE.scan();
      }
    }
    // Yield to other tasks
    vTaskDelay(pdMS_TO_TICKS(50));
  }
}

/// <summary>
/// Subroutine for the BLE monitor task to handle connection and data reading.
/// </summary>
void monitorIndoorBikeData(BLEDevice peripheral) {
  if (!peripheral.connect()) {
    return;
  }

  // Discover services and characteristics
  if (!peripheral.discoverService("1826")) {
    peripheral.disconnect();
    return;
  }

  indoorBikeDataCharacteristic = peripheral.characteristic("2ad2");
  if (!indoorBikeDataCharacteristic.subscribe()) {
    peripheral.disconnect();
    return;
  }

  fitnessMachineControlPointCharacteristic = peripheral.characteristic("2ad9");
  if (!fitnessMachineControlPointCharacteristic) {
    peripheral.disconnect();
    return;
  }

  // Handle resistance setup
  // This logic is simplified to run only once after connection
  if (FCPinit == 0) {
    uint8_t fmcpReset = 0x00;
    uint8_t fmcpStart = 0x07;
    fitnessMachineControlPointCharacteristic.writeValue(fmcpReset);
    vTaskDelay(pdMS_TO_TICKS(10));
    fitnessMachineControlPointCharacteristic.writeValue(fmcpStart);
    FCPinit = 1;
  }

  // Keep reading data as long as the peripheral is connected
  while (peripheral.connected()) {
    if (indoorBikeDataCharacteristic.valueUpdated()) {
      int descriptorValueSize = indoorBikeDataCharacteristic.valueSize();
      if (descriptorValueSize >= 4) {
        byte descriptorValue[descriptorValueSize];
        indoorBikeDataCharacteristic.readValue(descriptorValue, descriptorValueSize);

        speedLSB = descriptorValue[2];
        speedMSB = descriptorValue[3];
        speed = speedMSB;
        speed <<= 8;
        speed = speed | speedLSB;
        
        // mutex to safely update the speed variable
        if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
          speedO = speed * 0.01;
          xSemaphoreGive(xDataMutex);
        }
      }
    }

    // Yield to allow other tasks to run
    vTaskDelay(pdMS_TO_TICKS(15));
  }
}

/// <summary>
/// FreeRTOS Task for handling serial communication sending to Unity.
/// </summary>
void serialSendTask(void *pvParameters) {
  // Task specific setup
  
  // Task loop
  while (true) {
    // Take mutex to safely read shared variables
    if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
      // Format and send the data string to Unity
      Serial.print(speedO, 2);
      Serial.print(";");
      Serial.print(steeringAngle);
      Serial.print(";");
      Serial.print(frontBrakeForce);
      Serial.print(";");
      Serial.print(rearBrakeForce);
      Serial.print(";");
      Serial.println(resistance);
      
      // Give back the mutex
      xSemaphoreGive(xDataMutex);
    }
    
    /// TODO get steering angle input from motor.
    // steeringAngle = map(analogRead(A2), 0, 1023, 90, -90);

    // Process brake forces (these can be updated here since they are independent)
    frontBrakeForce = map(analogRead(FRONTBRAKE_PIN), 0, 1023, 0, 100);
    rearBrakeForce = map(analogRead(BACKBRAKE_PIN), 0, 1023, 0, 100);
    
    // Delay this task to control the sending rate
    vTaskDelay(pdMS_TO_TICKS(15));
  }
}

/// <summary>
/// FreeRTOS Task for handling serial communication receiving from Unity.
/// </summary>
void serialReceiveTask(void *pvParameters) {
  // Task specific setup
  
  // Task loop
  while (true) {
    if (Serial.available() > 0) {
      String incomingCommand = Serial.readStringUntil('\n');
      incomingCommand.trim();

      // Safely update the resistance variable using a mutex
      if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
        if (incomingCommand.startsWith("RESISTANCE:")) {
          String valueString = incomingCommand.substring(11);
          int newResistance = valueString.toInt();
          resistance = newResistance;
          
          // Update bike resistance based on the value set by Unity
          if (fitnessMachineControlPointCharacteristic) {
            uint8_t opCode = 0x04;
            int resistanceSupport = (int)resistance * 40;
            uint8_t msb = resistanceSupport / 256;
            uint8_t lsb = resistanceSupport - (msb * 256);
            uint8_t potResistance[3] = {opCode, lsb, msb};
            fitnessMachineControlPointCharacteristic.writeValue(potResistance, 3);
          }
        }
        xSemaphoreGive(xDataMutex);
      }
    }
    // Yield to allow other tasks to run
    vTaskDelay(pdMS_TO_TICKS(10));
  }
}

// --- Main Setup and Loop ---

void setup() {
  Serial.begin(115200);
  while (!Serial);

  // Create the mutex before creating tasks
  xDataMutex = xSemaphoreCreateMutex();
  if (xDataMutex == NULL) {
    while (1);
  }

  // Create the FreeRTOS tasks
  xTaskCreate(bleMonitorTask, "BLEMonitor", configMINIMAL_STACK_SIZE * 2, NULL, 2, NULL);
  xTaskCreate(serialSendTask, "SerialSend", configMINIMAL_STACK_SIZE * 2, NULL, 1, NULL);
  xTaskCreate(serialReceiveTask, "SerialReceive", configMINIMAL_STACK_SIZE * 2, NULL, 1, NULL);

  // Start the scheduler. It will not return unless a task is deleted.
  vTaskStartScheduler();
}

void loop() {
  // This is intentionally left empty. FreeRTOS takes over.
}

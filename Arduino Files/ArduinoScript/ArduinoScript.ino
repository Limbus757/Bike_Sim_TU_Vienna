/*
  This version uses FreeRTOS to handle BLE, serial communication sending,
  and serial communication receiving on three separate tasks.

  Board:
  - Arduino Uno WiFi Rev 4 board
*/

#include <ArduinoBLE.h>
#include <FreeRTOS_AVR.h> 
#include "Arduino_LED_Matrix.h"

//Pins
#define FRONTBRAKE_PIN A3
#define BACKBRAKE_PIN A4

// TODO: Micheal: ADD PWM Functionality for handle motors
// #define VIBRATION_MOTOR_PIN1 A7
// #define VIBRATION_MOTOR_PIN2 A7

// TODO: Thomas: ADD Handlebar Motor Control Pins

// ##### LED MATRIX OBJECTS #####

// Matrix Object
ArduinoLEDMatrix matrix;

// Timestamps for LED matrix state changes
volatile unsigned long lastDataSentTime = 0;
volatile unsigned long lastDataReceivedTime = 0;

unsigned long bleConnected = 0x00000000;
unsigned long dataReceived = 0x00000000;
unsigned long dataSent = 0x00000000;

// State frames for LED matrix
unsigned long statusFrame[] = {
  bleConnected,
  dataReceived,
  dataSent
};

SemaphoreHandle_t xLedMutex;

// ##### UNITY DATA ##### (volatile to ensure latest value is always read)

//Data to be sent
volatile float speedO = 0.0;
volatile float steeringAngle = 0.0; //right now unused in Unity but still sent if needed for later upgrades.
volatile float frontBrakeForce = 0.0;
volatile float rearBrakeForce = 0.0;
volatile float resistance = 0.0; //unused in Unity only sent to be monitored

// Received Data
volatile float placeholderValue1 = 0.0;
volatile float placeholderValue2 = 0.0;
volatile float placeholderValue3 = 0.0;
volatile float placeholderValue4 = 0.0;
volatile float placeholderValue5 = 0.0;

// Mutex to protect shared variables from simultaneous access by multiple tasks
SemaphoreHandle_t xDataMutex;

// ##### BLE objects ##### 

// "Tacx Flux-2 18201" is the bikefest ID
// "Tacx Flux-2 36608" is the lab bike ID
const char* deviceToConnect = "Tacx Flux-2 36688"
uint8_t speedLSB = 0x00;
uint8_t speedMSB = 0x00;
uint16_t speed = 0;
int FCPinit = 0;

// Fitness Machine Service Characteristics
BLECharacteristic indoorBikeDataCharacteristic;
BLECharacteristic fitnessMachineControlPointCharacteristic;

// ##### Task Functions #####

// FreeRTOS Task for handling BLE communication with the bike.
void bleMonitorTask(void *pvParameters) {
  BLE.begin();
  BLE.scanforName(deviceToConnect);

  while (true) {
    BLEDevice peripheral = BLE.available();
    if (peripheral) {
      BLE.stopScan();
      monitorIndoorBikeData(peripheral);
      BLE.scanforName(deviceToConnect); // if the peripheral disconnected, start scanning again

      }
    }
    // Yield to other tasks
    vTaskDelay(pdMS_TO_TICKS(50));
  }
}

/// FreeRTOS Task for handling serial communication, sending to Unity
void serialSendTask(void *pvParameters) {
  while (true) {
    if (Serial.availableForWrite() > 0) {
      measureTimeElapsed(lastTimeDataSent, &dataSent);

      xSemaphoreTake(xDataMutex, portMAX_DELAY);
      String outputString = String(speedO, 2) 
          + String(steeringAngle) 
          + String(frontBrakeForce) 
          + String(rearBrakeForce)
          + String(resistance);
      lastDataSentTime = millis();
      xSemaphoreGive(xDataMutex);
            
      Serial.println(outputString);
    }
  // TODO: Implement read steering agnle form Motor if possible
  readBrakes();
  vTaskDelay(pdMS_TO_TICKS(15)); //sending rate : 15ms
  }
}

// Task for handling serial communication, received from Unity
void serialReceiveTask(void *pvParameters) {
  while (true) {
    if (Serial.available()) {
      measureTimeElapsed(lastTimeDataReceived, &dataReceived);

      String incomingCommand = Serial.readStringUntil('\n');
      incomingCommand.trim();

      if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
        if (incomingCommand.startsWith("Resistance:")) {
          String valueString = incomingCommand.substring(11);
          int newResistance = valueString.toInt();
          resistance = newResistance;
          
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
    vTaskDelay(pdMS_TO_TICKS(15)); //receiving rate : 15ms
  }
}

void serialReceiveTask(void *pvParameters) {
  while (true) {
    if (Serial.available()) {
      lastDataReceivedTime = millis();
      String incomingCommand = Serial.readStringUntil('\n');
      incomingCommand.trim();

      int lastIndex = 0;
      int semicolonIndex;
      int valueIndex = 0;
      float values[5];

      while ((semicolonIndex = incomingCommand.indexOf(';', lastIndex)) != -1 && valueIndex < 5) {
        String valueString = incomingCommand.substring(lastIndex, semicolonIndex);
        values[valueIndex] = valueString.toFloat();
        lastIndex = semicolonIndex + 1;
        valueIndex++;
      }
      
      // Check for the last value after the last semicolon
      if (lastIndex < incomingCommand.length() && valueIndex < 5) {
        String valueString = incomingCommand.substring(lastIndex);
        values[valueIndex] = valueString.toFloat();
        valueIndex++;
      }

      placeholderValue1 = values[0];
      placeholderValue2 = values[1];
      placeholderValue3 = values[2];
      placeholderValue4 = values[3];
      placeholderValue5 = values[4];

    }
    vTaskDelay(pdMS_TO_TICKS(15));
  }
}

void renderMatrixTask(void* parameter) {
  matrix.begin();
  while (true) {
    matrix.beginDraw();
    matrix.fillScreen(0x0000);
    matrix.renderBitmap(statusFrame, 8, 12);
    matrix.endDraw();
    vTaskDelay(pdMS_TO_TICKS(100));
  }
}

// ##### SUBROUTINES AND FUNCTIONS #####

void readBrakes(){
  if (xSemaphoreTake(xDataMutex, portMAX_DELAY) == pdTRUE) {
    frontBrakeForce = map(analogRead(FRONTBRAKE_PIN), 0, 1023, 0, 100);
    rearBrakeForce = map(analogRead(BACKBRAKE_PIN), 0, 1023, 0, 100);
  }
  xSemaphoreGive(xDataMutex);
}

// Checks if the time elapsed since the given timestamp is greater than 250ms.
// If it is, the LED frame part is set to 0x00000000; otherwise, it's set to 0x11110000.
void measureTimeElapsed(unsigned long timestamp, volatile unsigned long* ledFramePart){
  xSemaphoreTake(xLedMutex, portMAX_DELAY);
  if ((millis() - timestamp) > 250) {
    *ledFramePart = 0x00000000;
  } else {
    *ledFramePart = 0x11110000;
  }
  xSemaphoreGive(xLedMutex);
}

/// Subroutine for the BLE monitor task to handle connection and data reading.
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
    vTaskDelay(pdMS_TO_TICKS(15));
  }
}

// ##### Main Setup and Loop #####

void setup() {
  Serial.begin(115200);
  vTaskDelay(pdMS_TO_TICKS(100));

  // Create the mutex before creating tasks
  xDataMutex = xSemaphoreCreateMutex();
  vTaskDelay(pdMS_TO_TICKS(100));

  xLedMutex = xSemaphoreCreateMutex();
  vTaskDelay(pdMS_TO_TICKS(100));
 
  // Create the FreeRTOS tasks
  xTaskCreate(bleMonitorTask, "BLEMonitor", configMINIMAL_STACK_SIZE * 2, NULL, 2, NULL);
  xTaskCreate(serialSendTask, "SerialSend", configMINIMAL_STACK_SIZE * 2, NULL, 1, NULL);
  xTaskCreate(serialReceiveTask, "SerialReceive", configMINIMAL_STACK_SIZE * 2, NULL, 1, NULL);
  xTaskCreate(renderMatrixTask, "RenderMatrix", configMINIMAL_STACK_SIZE * 2, NULL, 3, NULL);

  // Start the scheduler. It will not return unless a task is deleted.
  vTaskStartScheduler();
}

void loop() {
  // This is intentionally left empty, FreeRTOS takes over.
}

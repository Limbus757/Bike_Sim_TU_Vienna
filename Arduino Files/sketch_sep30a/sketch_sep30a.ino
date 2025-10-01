#include <Arduino_FreeRTOS.h>
#include <ArduinoBLE.h>
#include <Arduino_LED_Matrix.h>

// Pins
#define FRONTBRAKE_PIN A3
#define BACKBRAKE_PIN A4

// Shared variables (to be sent over Serial)
volatile float speedO = 0.0;
volatile float steeringAngle = 0.0;
volatile float frontBrakeForce = 0.0;
volatile float rearBrakeForce = 0.0;
volatile float resistance = 0.0;

// Received placeholder values
volatile float placeholderValue1 = 0.0;
volatile float placeholderValue2 = 0.0;
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

// Task: send shared variables over Serial
void serialSendTask(void *pvParameters) {
  (void) pvParameters;
  char outputString[128];

  Serial.println("[Task] SerialSend starting...");

  for (;;) {
    if (xSemaphoreTake(xBikeDataMutex, portMAX_DELAY) == pdTRUE) {
      snprintf(outputString, sizeof(outputString),
               "%.2f;%.2f;%.2f;%.2f;%.2f",
               speedO, steeringAngle, frontBrakeForce, rearBrakeForce, resistance);
      Serial.println(outputString);
      readBrakes();
      xSemaphoreGive(xBikeDataMutex);
    }
    vTaskDelay(pdMS_TO_TICKS(1000));
  }
}

void readBrakes() {
    frontBrakeForce = map(analogRead(FRONTBRAKE_PIN), 0, 1023, 0, 100);
    rearBrakeForce  = map(analogRead(BACKBRAKE_PIN), 0, 1023, 0, 100);
}

void serialReceiveTask(void *pvParameters) {
  (void) pvParameters;
  Serial.println("[Task] SerialReceive starting...");

  for (;;) {
    if (Serial.available()) {
      String incomingCommand = Serial.readStringUntil('\n');
      incomingCommand.trim();
      Serial.print("[SerialReceive] Received: ");
      Serial.println(incomingCommand);

      int lastIndex = 0;
      int semicolonIndex;
      int valueIndex = 0;
      float values[5] = {0};

      while ((semicolonIndex = incomingCommand.indexOf(';', lastIndex)) != -1 && valueIndex < 5) {
        String valueString = incomingCommand.substring(lastIndex, semicolonIndex);
        values[valueIndex] = valueString.toFloat();
        Serial.print("[SerialReceive] Parsed value ");
        Serial.print(valueIndex);
        Serial.print(": ");
        Serial.println(values[valueIndex]);
        lastIndex = semicolonIndex + 1;
        valueIndex++;
      }

      if (lastIndex < incomingCommand.length() && valueIndex < 5) {
        String valueString = incomingCommand.substring(lastIndex);
        values[valueIndex] = valueString.toFloat();
        Serial.print("[SerialReceive] Parsed last value ");
        Serial.print(valueIndex);
        Serial.print(": ");
        Serial.println(values[valueIndex]);
      }

      if (xSemaphoreTake(xCommandDataMutex, portMAX_DELAY) == pdTRUE) {
        placeholderValue1 = values[0];
        placeholderValue2 = values[1];
        placeholderValue3 = values[2];
        placeholderValue4 = values[3];
        placeholderValue5 = values[4];
        xSemaphoreGive(xCommandDataMutex);

        Serial.print("[SerialReceive] Stored values: ");
        Serial.print(placeholderValue1); Serial.print(", ");
        Serial.print(placeholderValue2); Serial.print(", ");
        Serial.print(placeholderValue3); Serial.print(", ");
        Serial.print(placeholderValue4); Serial.print(", ");
        Serial.println(placeholderValue5);
      }
    }
    vTaskDelay(pdMS_TO_TICKS(15));
  }
}

void setup() {
  Serial.begin(115200);
  while (!Serial) { ; }  // wait for USB Serial to be ready
  delay(500);

  // Create mutex
  xCommandDataMutex = xSemaphoreCreateMutex();
  if (!xDataMutex) {
    Serial.println("[ERROR] Failed to create mutex!");
    while (1);
  }

  xCommandDataMutex = xSemaphoreCreateMutex();
  if (!xDataMutex) {
    Serial.println("[ERROR] Failed to create mutex!");
    while (1);
  }

  xTaskCreate(serialSendTask, "SerialSendTask", 1024, NULL, 1, NULL); // large stack for Serial
  xTaskCreate(serialReceiveTask, "SerialReceiveTask", 1024, NULL, 1, NULL); // large stack for Serial
  vTaskStartScheduler();// starts FreeRTOS
}

void loop() {
  // empty; FreeRTOS tasks run
}

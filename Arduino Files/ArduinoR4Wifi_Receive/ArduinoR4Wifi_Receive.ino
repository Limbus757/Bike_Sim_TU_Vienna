#include <Wire.h>
#include <Adafruit_DRV2605.h>

// --- PIN DEFINITIONS: STEERING MOTOR ---
const int STE_ENABLE_PIN = 10;    
const int STE_DIRECTION_PIN = 9; 
const int STE_PWM_PIN = 8;      

// --- PIN DEFINITIONS: HAPTIC DRIVERS ---
const int VIB_EN_LEFT = 6;       
const int VIB_EN_RIGHT = 7;      
const int VIB_PWM_LEFT = 5;      
const int VIB_PWM_RIGHT = 3;     

// --- PWM CONSTANTS ---
const int PWM_MIN_LIMIT = 25;    // 10% Duty Cycle (Zero Speed)
const int PWM_MAX_LIMIT = 230;   // 90% Duty Cycle (Max Speed)

// --- GLOBAL PIN STATES ---
uint8_t currentEnVal = 0;
uint8_t currentDirVal = 0;
uint8_t currentStePWM = PWM_MIN_LIMIT; // Start at 10% (Zero Speed)
uint8_t currentVibLPWM = 128;
uint8_t currentVibRPWM = 128;

// --- SAFETY TIMEOUT ---
unsigned long lastDataReceivedTime = 0;
const unsigned long TIMEOUT_THRESHOLD_MS = 200; 
bool firstDataReceived = false; // Flag to ensure timeout only starts after first input

Adafruit_DRV2605 drv;

// Configures DRV2605 chips individually by toggling Enable pins
bool configureDrv2605One(int enPin) {
  digitalWrite(VIB_EN_LEFT, LOW);
  digitalWrite(VIB_EN_RIGHT, LOW);
  delay(5);

  digitalWrite(enPin, HIGH);
  delay(10);
  
  if (!drv.begin()) {
    digitalWrite(enPin, LOW);
    return false;
  }

  drv.setMode(DRV2605_MODE_PWMANALOG);
  return true;
}

// Applies global variables to physical pins with safety constraints
void updateSignals() {
  digitalWrite(STE_ENABLE_PIN, currentEnVal ? HIGH : LOW);
  digitalWrite(STE_DIRECTION_PIN, currentDirVal ? HIGH : LOW);
  
  // Constrain Steering PWM: Floor is 10% (25), Ceiling is 90% (230)
  uint8_t clampedSteering = constrain(currentStePWM, PWM_MIN_LIMIT, PWM_MAX_LIMIT);
  analogWrite(STE_PWM_PIN, clampedSteering);
  
  analogWrite(VIB_PWM_LEFT, currentVibLPWM);
  analogWrite(VIB_PWM_RIGHT, currentVibRPWM);
}

// Reverts all motors to a safe/stopped state
void setSafeState() {
  currentEnVal = 0;
  currentDirVal = 0;
  currentStePWM = PWM_MIN_LIMIT; // 10% PWM is Zero Speed
  currentVibLPWM = 128;
  currentVibRPWM = 128;
  updateSignals();
}

void setPinmodes() {
  pinMode(STE_ENABLE_PIN, OUTPUT);
  pinMode(STE_DIRECTION_PIN, OUTPUT);
  pinMode(STE_PWM_PIN, OUTPUT);

  pinMode(VIB_EN_LEFT, OUTPUT);
  pinMode(VIB_EN_RIGHT, OUTPUT);
  pinMode(VIB_PWM_LEFT, OUTPUT);
  pinMode(VIB_PWM_RIGHT, OUTPUT);
}

void setup() {
  Serial.begin(115200);
  Wire.begin();

  setPinmodes();
  setSafeState(); // Ensure pins are in safe state on boot

  
  if (!configureDrv2605One(VIB_EN_LEFT)) {
    Serial.println("DRV2605 Left Error");
   // while (1); 
  }
 /*
 if (!configureDrv2605One(VIB_EN_RIGHT)) {
    Serial.println("DRV2605 Right Error");
    while (1);
  }
 */ 
 
  // Keep both haptic drivers enabled for PWM operation
  digitalWrite(VIB_EN_LEFT, HIGH);
  digitalWrite(VIB_EN_RIGHT, HIGH);

}

void loop() {
  // 1. Check if there is data
  if (Serial.available() > 0) {
    
    // read the line until the \n terminator
    String incomingLine = Serial.readStringUntil('\n');

    if (incomingLine.length() > 0) {
      int en, dir, ste, vibL, vibR;

      // sscanf returns the number of variables successfully filled
      // We expect 5 values based on your Unity string: "{0},{1},{2},{3},{4}\n"
      int parsedCount = sscanf(incomingLine.c_str(), "%d,%d,%d,%d,%d", 
                               &en, &dir, &ste, &vibL, &vibR);

      if (parsedCount == 5) {
        currentEnVal = (uint8_t)en;
        currentDirVal = (uint8_t)dir;
        currentStePWM = (uint8_t)ste;
        currentVibLPWM = (uint8_t)vibL;
        currentVibRPWM = (uint8_t)vibR;

        // reset safety watchdog
        if (!firstDataReceived) firstDataReceived = true;
        lastDataReceivedTime = millis();

        // push new values to the physical pins
        updateSignals();
      }
    }
  }

  // safety timeout logic
  if (firstDataReceived && (millis() - lastDataReceivedTime > TIMEOUT_THRESHOLD_MS)) {
    setSafeState();
  }
}
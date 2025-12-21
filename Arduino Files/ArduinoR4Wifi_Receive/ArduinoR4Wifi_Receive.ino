#include <Wire.h>
#include <Adafruit_DRV2605.h>

// --- PIN DEFINITIONS: STEERING MOTOR ---
const int STE_ENABLE_PIN = 8;    
const int STE_DIRECTION_PIN = 9; 
const int STE_PWM_PIN = 10;      

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
uint8_t currentVibLPWM = 0;
uint8_t currentVibRPWM = 0;

// --- SAFETY TIMEOUT ---
unsigned long lastDataReceivedTime = 0;
const unsigned long TIMEOUT_THRESHOLD_MS = 2000; 
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
  currentVibLPWM = 0;
  currentVibRPWM = 0;
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
    while (1); 
  }

  if (!configureDrv2605One(VIB_EN_RIGHT)) {
    Serial.println("DRV2605 Right Error");
    while (1);
  }

  // Keep both haptic drivers enabled for PWM operation
  digitalWrite(VIB_EN_LEFT, HIGH);
  digitalWrite(VIB_EN_RIGHT, HIGH);

}

void loop() {
  // 1. Process Serial Input
  if (Serial.available() > 0) {
    currentEnVal = Serial.parseInt();   
    currentDirVal = Serial.parseInt();  
    currentStePWM = Serial.parseInt();  
    currentVibLPWM = Serial.parseInt(); 
    currentVibRPWM = Serial.parseInt(); 

    // Flush any trailing newline/carriage return
    while (Serial.available() > 0 && (Serial.peek() == '\n' || Serial.peek() == '\r')) {
      Serial.read();
    }

    // Trigger the timeout logic only after the first message is received
    if (!firstDataReceived) {
      firstDataReceived = true;
    }

    lastDataReceivedTime = millis(); 
    updateSignals();
  }

  // 2. Safety Timeout Logic
  // Only activates if firstDataReceived is true AND time has elapsed
  if (firstDataReceived && (millis() - lastDataReceivedTime > TIMEOUT_THRESHOLD_MS)) {
    setSafeState();
  }
}
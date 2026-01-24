/* 
GENERAL FUNCTION:
- Receives binary control packets from Unity via Serial.
- Drives a Maxon ESCON 50/5 steering motor using 20kHz (12-bit PWM) 
- Controls two DRV2605 haptic drivers at 20kHz (12-bit PWM) 
- Includes a safety watchdog that stops all motors if Unity stops responding.
*/

#include <Wire.h>
#include <Adafruit_DRV2605.h>
#include "pwm.h" // Accesses R4's RA4M1 GPT timers for high-res PWM

// --- CONFIGURATION ---
#define SERIAL_BAUDRATE 115200
#define TIMEOUT_THRESHOLD_MS 75 

// --- PIN DEFINITIONS ---
const int SM_ENABLE_PIN = 10;    
const int SM_DIRECTION_PIN = 9;  
const int SM_PWM_PIN = 8;        // Steering PWM (GPT timer)

const int VM_EN_L_PIN = 6;       
const int VM_EN_R_PIN = 7;      
const int VM_PWM_L_PIN = 5;      
const int VM_PWM_R_PIN = 3;     

// --- 12-BIT PWM CONSTANTS (0-4095) ---
const uint16_t SM_MIN_LIMIT = 410;   // 10% floor (prevents ESCON fault)
const uint16_t SM_MAX_LIMIT = 3685;  // 90% ceiling
const uint16_t VM_IDLE_PWM  = 2048;  // Neutral vibration

// --- BINARY PACKET STRUCTURE ---
// Packed to ensure memory alignment matches Unity exactly (8 bytes payload)
struct __attribute__((packed)) ControlData {
    uint8_t  smEnable;       
    uint8_t  smDirection;    
    uint16_t smPwm;         // 2 bytes (0-4095)
    uint16_t vmLeftPwm;     // 2 bytes (0-4095)
    uint16_t vmRightPwm;    // 2 bytes (0-4095)
};

ControlData currentData = {0, 0, SM_MIN_LIMIT, VM_IDLE_PWM, VM_IDLE_PWM}; 
unsigned long lastDataTime = 0;
bool firstData = false;

Adafruit_DRV2605 drv;

// --- VM DRIVER CONFIG ---
// DRV2605s share an I2C address; we toggle EN pins to configure them individually.
bool configVMDriver(int enPin) {
    digitalWrite(VM_EN_L_PIN, LOW);
    digitalWrite(VM_EN_R_PIN, LOW);
    delay(10);
    digitalWrite(enPin, HIGH); // Wake only target chip
    delay(20);
    if (!drv.begin()) return false;
    drv.setMode(DRV2605_MODE_PWMANALOG); // Sample PWM to create analog vibration
    return true;
}



void setFrequencies() {
    // 20kHz: Higher than human hearing; stops mechanical motor "rattle."
    // 12-bit: Increases resolution from 256 steps to 4096 steps.
    PwmOut smPwm(SM_PWM_PIN);
    smPwm.begin(20000.0f, 0.0f, true, PWM_RESOLUTION_12_BIT); 

    PwmOut vmL(VM_PWM_L_PIN);
    PwmOut vmR(VM_PWM_R_PIN);
    vmL.begin(20000.0f, 0.0f, true, PWM_RESOLUTION_12_BIT);
    vmR.begin(20000.0f, 0.0f, true, PWM_RESOLUTION_12_BIT);
}

void applySignals() {
    digitalWrite(SM_ENABLE_PIN, currentData.smEnable ? HIGH : LOW);
    digitalWrite(SM_DIRECTION_PIN, currentData.smDirection ? HIGH : LOW);
    
    // Safety clamp for ESCON controller (avoids out-of-range faults)
    uint16_t clampedSM = constrain(currentData.smPwm, SM_MIN_LIMIT, SM_MAX_LIMIT); 
    analogWrite(SM_PWM_PIN, clampedSM); 
  
    analogWrite(VM_PWM_L_PIN, currentData.vmLeftPwm);
    analogWrite(VM_PWM_R_PIN, currentData.vmRightPwm);
}

void setSafeState() {
    currentData.smEnable = 0;
    currentData.smDirection = 0;
    currentData.smPwm = SM_MIN_LIMIT;
    currentData.vmLeftPwm = VM_IDLE_PWM; 
    currentData.vmRightPwm = VM_IDLE_PWM; 
    applySignals();
}

void setup() {
    Serial.begin(SERIAL_BAUDRATE);
    Wire.begin();

    pinMode(SM_ENABLE_PIN, OUTPUT);
    pinMode(SM_DIRECTION_PIN, OUTPUT);
    pinMode(SM_PWM_PIN, OUTPUT);
    pinMode(VM_EN_L_PIN, OUTPUT);
    pinMode(VM_EN_R_PIN, OUTPUT);
    pinMode(VM_PWM_L_PIN, OUTPUT);
    pinMode(VM_PWM_R_PIN, OUTPUT);

    setFrequencies(); 
    setSafeState();

    if (!configVMDriver(VM_EN_L_PIN)) Serial.println("VM_L Fail");
    if (!configVMDriver(VM_EN_R_PIN)) Serial.println("VM_R Fail");
    
    // Keep both EN high after config so they process the continuous PWM stream
    digitalWrite(VM_EN_L_PIN, HIGH);
    digitalWrite(VM_EN_R_PIN, HIGH);
}

void loop() {
    // Binary Packet sync: Header (0xAA 0xBB) + Payload (8b) + Footer (0xCC) = 11 bytes total
    if (Serial.available() >= (sizeof(ControlData) + 3)) { 
        if (Serial.read() == 0xAA && Serial.read() == 0xBB) {
            // "Cast" bytes directly into the struct memory address for zero-latency parsing
            Serial.readBytes((byte*)&currentData, sizeof(ControlData)); 
            if (Serial.read() == 0xCC) {
                firstData = true;
                lastDataTime = millis();
                applySignals();
            }
        }
    }

    // Safety watchdog: stops motors if Unity crashes or cable is unplugged
    if (firstData && (millis() - lastDataTime > TIMEOUT_THRESHOLD_MS)) {
        setSafeState(); 
    }
}
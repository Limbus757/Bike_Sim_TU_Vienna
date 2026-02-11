/* * FUNCTION:
 * - Receives 11-byte binary packets from Unity.
 * - Drives Steering Motor via GPT Timer (20kHz, 12-bit).
 * - Controls DRV2605 Haptic Drivers via GPT Timer (20kHz, 12-bit).
 * - Watchdog safety stops motors if Unity stops responding for > 75ms.
 */

#include <Wire.h>
#include <Adafruit_DRV2605.h>
#include "pwm.h" // Standard R4 Renesas Core PWM library

// --- CONFIGURATION ---
#define SERIAL_BAUDRATE 256000
#define TIMEOUT_THRESHOLD_MS 75 

// --- PIN DEFINITIONS ---
const int SM_ENABLE_PIN = 10;    
const int SM_DIRECTION_PIN = 9;  
const int SM_PWM_PIN = 8;        

const int VM_EN_L_PIN = 6;       
const int VM_EN_R_PIN = 7;      
const int VM_PWM_L_PIN = 5;      
const int VM_PWM_R_PIN = 3;     

// --- 12-BIT PWM CONSTANTS (0-4095) ---
const uint16_t SM_MIN_LIMIT = 410;   // 10% floor for ESCON
const uint16_t SM_MAX_LIMIT = 3685;  // 90% ceiling
const uint16_t VM_IDLE_PWM  = 2048;  // Neutral haptic value

// --- BINARY PACKET STRUCTURE ---
// Matches Unity [StructLayout(LayoutKind.Sequential, Pack = 1)]
struct __attribute__((packed)) ControlData {
    uint8_t  smEnable;       
    uint8_t  smDirection;    
    uint16_t smPwm;         
    uint16_t vmLeftPwm;     
    uint16_t vmRightPwm;    
};

ControlData currentData = {0, 0, SM_MIN_LIMIT, VM_IDLE_PWM, VM_IDLE_PWM}; 
unsigned long lastDataTime = 0;
bool firstData = false;

Adafruit_DRV2605 drv;

// Configure DRV2605 Drivers
bool configVMDriver(int enPin) {
    digitalWrite(VM_EN_L_PIN, LOW);
    digitalWrite(VM_EN_R_PIN, LOW);
    delay(10);
    digitalWrite(enPin, HIGH); 
    delay(20);
    if (!drv.begin()) return false;
    drv.setMode(DRV2605_MODE_PWMANALOG); 
    return true;
}

void setFrequencies() {
    
    uint32_t period_us = 1000; 

    PwmOut smPwm(SM_PWM_PIN);
    smPwm.begin(period_us, 0, false, TIMER_SOURCE_DIV_1); 

    PwmOut vmL(VM_PWM_L_PIN);
    vmL.begin(period_us, 0, false, TIMER_SOURCE_DIV_1);

    PwmOut vmR(VM_PWM_R_PIN);
    vmR.begin(period_us, 0, false, TIMER_SOURCE_DIV_1);
}

void applySignals() {
    digitalWrite(SM_ENABLE_PIN, currentData.smEnable ? HIGH : LOW);
    digitalWrite(SM_DIRECTION_PIN, currentData.smDirection ? HIGH : LOW);
    
    // Apply Steering with safety clamp
    uint16_t clampedSM = constrain(currentData.smPwm, SM_MIN_LIMIT, SM_MAX_LIMIT); 
    analogWrite(SM_PWM_PIN, clampedSM); 
  
    // Apply Haptics
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

    // Set 12-bit resolution for all analogWrite calls
    analogWriteResolution(12);

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
    
    digitalWrite(VM_EN_L_PIN, HIGH);
    digitalWrite(VM_EN_R_PIN, HIGH);
}

void loop() {
    while (Serial.available() >= 11) {
        // 2. Look for the start of the packet (0xAA 0xBB)
        if (Serial.read() == 0xAA) {
            if (Serial.peek() == 0xBB) { // Peek to confirm second header byte
                Serial.read(); // Consume the 0xBB

                // 3. Fast-read the payload directly into the struct memory
                byte* p = (byte*)&currentData;
                for (size_t i = 0; i < sizeof(ControlData); i++) {
                    p[i] = Serial.read();
                }


                if (Serial.read() == 0xCC) {
                    firstData = true;
                    lastDataTime = millis();
                    
                    applySignals();
                }
            }
        }
    }

    // --- Safety Watchdog ---
    if (firstData && (millis() - lastDataTime > TIMEOUT_THRESHOLD_MS)) {
        setSafeState(); 
    }
}
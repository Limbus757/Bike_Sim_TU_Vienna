// --- PIN DEFINITIONS ---
const int ENABLE_PIN = 8;    // Digital pin for Enable (D8)
const int DIRECTION_PIN = 9; // Digital pin for Direction (D9)
const int PWM_SET_VALUE_PIN = 10; // Digital pin for PWM Set Value (D10)

// --- PWM CONSTANTS for ESCON CONTROL ---
// These are the 8-bit analogWrite values (0-255) for the corresponding duty cycles.
const int PWM_MIN_REVERSE = 25;  // Approx. 10% Duty Cycle (Max Reverse Speed)
const int PWM_ZERO_SPEED = 127; // Approx. 50% Duty Cycle (Zero Speed)
const int PWM_MAX_FORWARD = 230; // Approx. 90% Duty Cycle (Max Forward Speed)

// The delay between each step in the ramp (controls ramp speed)
const int RAMP_DELAY_MS = 5; // A small delay for a fast, noticeable ramp (5ms per step)

void setup() {
  // Set pin modes
  pinMode(ENABLE_PIN, OUTPUT);
  pinMode(DIRECTION_PIN, OUTPUT);
  pinMode(PWM_SET_VALUE_PIN, OUTPUT);

  // 1. Set Enable Pin HIGH
  digitalWrite(ENABLE_PIN, HIGH);
  
  // Set PWM frequency if possible (See previous note - reliance on default is assumed)

  // Initialize the direction pin LOW
  digitalWrite(DIRECTION_PIN, LOW);
  
  // Start motor at zero speed (50% duty cycle)
  analogWrite(PWM_SET_VALUE_PIN, PWM_ZERO_SPEED);
}

// Function to ramp the speed from a start value to an end value
void rampSpeed(int startValue, int endValue) {
  // Determine if we are ramping up or down
  if (startValue < endValue) {
    // Ramp UP
    for (int duty = startValue; duty <= endValue; duty++) {
      analogWrite(PWM_SET_VALUE_PIN, duty);
      delay(RAMP_DELAY_MS);
    }
  } else {
    // Ramp DOWN
    for (int duty = startValue; duty >= endValue; duty--) {
      analogWrite(PWM_SET_VALUE_PIN, duty);
      delay(RAMP_DELAY_MS);
    }
  }
}

void loop() {
  // --- FORWARD MOTION (Ramp Up and Down) ---
  
  // Set Direction Pin HIGH (Often used for enable/disable in velocity mode)
  digitalWrite(DIRECTION_PIN, HIGH);
  delay(500); // Small pause for signal change

  // 1. Ramp UP from ZERO to MAX FORWARD (127 -> 230)
  rampSpeed(PWM_ZERO_SPEED, PWM_MAX_FORWARD);
  
  delay(1000); // Maintain max speed for 1 second

  // 2. Ramp DOWN from MAX FORWARD to ZERO (230 -> 127)
  rampSpeed(PWM_MAX_FORWARD, PWM_ZERO_SPEED);

  delay(1000); // Maintain zero speed for 1 second

  // --- REVERSE MOTION (Ramp Up and Down) ---  
  digitalWrite(DIRECTION_PIN, LOW);
  delay(500); // Small pause for signal change

  // 3. Ramp UP from ZERO to MAX REVERSE (127 -> 25)
  // Note: We are "ramping up" in magnitude, which means ramping the digital value DOWN
  rampSpeed(PWM_ZERO_SPEED, PWM_MIN_REVERSE); 

  delay(1000); // Maintain max reverse speed for 1 second

  // 4. Ramp DOWN from MAX REVERSE to ZERO (25 -> 127)
  rampSpeed(PWM_MIN_REVERSE, PWM_ZERO_SPEED);

  delay(1000); // Maintain zero speed before repeating cycle
}
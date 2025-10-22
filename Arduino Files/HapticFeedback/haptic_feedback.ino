#include <Wire.h>
#include <Adafruit_DRV2605.h>

// --- Pins anpassen ---
const int EN_LEFT  = 8;   // EN-Pin Treiber links
const int EN_RIGHT = 7;   // EN-Pin Treiber rechts

const int PWM_LEFT  = 9;  // PWM-Ausgang für links (an IN/TRIG links)
const int PWM_RIGHT = 10; // PWM-Ausgang für rechts (an IN/TRIG rechts)

Adafruit_DRV2605 drv;     // Ein Objekt genügt, da die Adresse identisch ist

// Hilfsfunktion: Einen einzelnen DRV2605 per EN-Pin einschalten & konfigurieren
bool configureDrv2605One(int enPin) {
  // Nur diesen Treiber einschalten, den anderen sicher ausschalten
  digitalWrite(EN_LEFT,  LOW);
  digitalWrite(EN_RIGHT, LOW);
  delay(5);

  digitalWrite(enPin, HIGH); // gewünschten Treiber aktivieren
  delay(10);

  if (!drv.begin()) {
    // Falls der Chip nicht antwortet, EN wieder aus und Fehler melden
    digitalWrite(enPin, LOW);
    return false;
  }

  // In PWM/Analog-Modus (IN/TRIG erwartet PWM-Pegel)
  drv.setMode(DRV2605_MODE_PWMANALOG);

  // Optional: weitere Grundkonfig (z.B. Library, Effekt ist im PWM-Mode egal)
  // drv.selectLibrary(1);

  // Treiber eingeschaltet lassen (für PWM-Betrieb benötigt)
  return true;
}

// Setzt beide PWM-Werte (0..255)
void setPWM(uint8_t left, uint8_t right) {
  analogWrite(PWM_LEFT,  left);
  analogWrite(PWM_RIGHT, right);
}

void setup() {
  Serial.begin(115200);

  // Pins vorbereiten
  pinMode(EN_LEFT,  OUTPUT);
  pinMode(EN_RIGHT, OUTPUT);
  digitalWrite(EN_LEFT,  LOW);
  digitalWrite(EN_RIGHT, LOW);

  pinMode(PWM_LEFT,  OUTPUT);
  pinMode(PWM_RIGHT, OUTPUT);

  // I2C starten
  Wire.begin();

  // Links konfigurieren
  if (!configureDrv2605One(EN_LEFT)) {
    Serial.println("DRV2605 (links) nicht gefunden!");
    while (1);
  }
  Serial.println("DRV2605 links im PWM-Modus.");

  // Rechts konfigurieren
  if (!configureDrv2605One(EN_RIGHT)) {
    Serial.println("DRV2605 (rechts) nicht gefunden!");
    while (1);
  }
  Serial.println("DRV2605 rechts im PWM-Modus.");

  // Jetzt beide aktiv schalten (I²C wird im Loop nicht mehr benötigt)
  digitalWrite(EN_LEFT,  HIGH);
  digitalWrite(EN_RIGHT, HIGH);

  // Startwerte
  setPWM(0, 0);
}

void loop() {
  // Demo: gleichzeitiger Sweep (du kannst stattdessen setPWM(L, R) beliebig aufrufen)
  for (int d = 0; d <= 255; d++) {
    setPWM(d, 255 - d);  // z.B. links hoch, rechts runter
    delay(10);
  }
  delay(200);

  for (int d = 255; d >= 0; d--) {
    setPWM(d, 255 - d);
    delay(10);
  }
  delay(200);
}

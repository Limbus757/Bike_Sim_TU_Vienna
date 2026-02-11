# Bike Simulator: Arduino Control System

This repository contains the firmware and hardware documentation for the Bike Simulator project developed at TU Vienna. The system utilizes a dual-microcontroller architecture to bridge the gap between physical hardware (Tacx Trainer, sensors, and motors) and the Unity simulation environment.

---

## Hardware Architecture

The system is split into two primary controllers to ensure real-time performance and safety:

### 1. ESP32-S3: Telemetry Bridge & Sensor Hub
Located in the ESP32-S3_SerialSender folder, this controller acts as the central communication hub.
* **Dual-Core Execution**: Utilizes FreeRTOS to run BLE/Radio management on Core 1 and Serial/PC communication on Core 0 to prevent timing jitter.
* **BLE Connectivity**: Connects to a TACX-II trainer via the Fitness Machine Service (FTMS) using a specific MAC address.
* **Sensor Integration**: Reads analog brake force from front and rear brakes (mapped from 12-bit ADC to a 0-100 scale) and monitors a digital Lane Keep Assist (LKA) toggle.
* **Dynamic Resistance**: Maps total braking force (0-200% sum) to FTMS resistance units (0-1000) and sends it to the trainer.
* **Telemetry Streaming**: Streams 21-byte packed binary packets to the PC at 50Hz (20ms intervals) via Serial.

### 2. Arduino R4 WiFi: Actuator & Haptics Controller
Located in the Arduino-R4-Wifi_SerialReceiver folder, this controller translates Unity commands into physical movement.
* **Steering Control**: Drives a steering motor via a GPT Timer at 20kHz with 12-bit resolution.
* **Haptic Feedback**: Controls DRV2605 haptic drivers via PWM to provide tactile feedback.
* **Safety Watchdog**: Automatically sets motors to a safe state if the Unity application stops responding for more than 75ms.
* **High-Speed Serial**: Operates at a baud rate of 256,000 to ensure low-latency control.

---

## Communication Protocols
Both controllers use a structured binary packet format to ensure efficient data transfer with minimal overhead.

### Telemetry Packet (ESP32 -> PC)
| **Sync Header** | 0xAA 0xBB |
| **Payload** | 21 Bytes: Speed, Front/Rear Brake, Resistance, LKA Switch, Timing |
| **Tail** | 0xCC |

### Control Packet (PC -> Arduino R4)
| **Sync Header** | 0xAA 0xBB |
| **Payload** | 11 Bytes: Motor Enable, Direction, Steering PWM, Haptic PWM |
| **Tail** | 0xCC |

---

### LED Status Indicators (ESP32-S3)
The built-in RGB LED on the ESP32 provides immediate visual feedback on the system state:
* **Red**: Disconnected or initialization failure.
* **Yellow**: Searching for the Tacx Trainer MAC address.
* **Blue**: BLE radio link established (Service Discovery).
* **Purple**: FTMS Handshake in progress (Requesting Control).
* **Green**: Fully Synchronized & Control Active.

---

### Setup & Installation
1.  **Dependencies**:
    * **ESP32**: Requires the NimBLE-Arduino library.
    * **Arduino R4**: Requires Adafruit_DRV2605.h and the included pwm.h.
2.  **Configuration**:
    * In ESP32-S3_SerialSender.ino, verify the TARGET_MAC_ADDR matches your trainer.
3.  **Deployment**:
    * Upload the respective .ino files to their target boards using the Arduino IDE.

## Mechanical & Technical Documentation

In addition to the firmware, this repository includes integrated design and technical files located in the Documents and 3D Prints folder.

### 3D Design Files
* **Format**: All mechanical parts are provided as STEP files, ensuring compatibility with most CAD software (Fusion 360, SolidWorks, etc.).
* **Components**: Includes custom-designed mounts for attaching the sensors, microcontrollers, and the steering motor assembly to the bike frame.

### Technical Datasheets
* **Reference Material**: Includes datasheets for the core electronic components, such as the DRV2605 haptic drivers and motor controllers, to assist with wiring and electrical troubleshooting.

---


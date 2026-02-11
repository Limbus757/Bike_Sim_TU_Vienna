# Bike_Sim_TU_Vienna
**Bachelor Thesis Project | TU Wien**

An immersive bicycle simulation environment developed at TU Wien, integrating Unity-based visualization with Arduino hardware for real-time interaction.

![Project Overview](./Bike_Sim_Shared_Overview.jpg)

---
## Project Structure

| Folder / File | Description |
| :--- | :--- |
| **`Unity Project/`** | The core simulation built in Unity. Includes physics, environments, and UI. |
| **`Arduino Files/`** | Microcontroller firmware for sensor data acquisition and haptic feedback. |
| **`Data Analysis/`** | Scripts and tools for processing simulation logs and performance metrics. |
| **`Documents and 3D Prints/`** | CAD files, datasheets for the elctronics |
---

## Getting Started

### 1. Hardware Setup
* **Microcontroller:** [e.g., Arduino Nano/Uno]
* **Sensors:** [e.g., Potentiometers for steering, Hall effect for speed]
* Upload the code found in `/Arduino Files/` using the Arduino IDE.

### 2. Unity Simulation
* **Version:** [Insert Unity Version, e.g., 2022.3 LTS]
* Open the `Unity Project` folder in Unity Hub.
* Ensure the COM port in the simulation settings matches your Arduino port.

### 3. Data Analysis
* Post-processing scripts are located in `/Data Analysis/`.
* [Optional: Mention if Python or MATLAB is required].

---
using UnityEngine;
using System.IO.Ports;
using System.Threading;
using System.Runtime.InteropServices;
using System;

/// <summary>
/// Handles serial communication with external microcontrollers.
/// Packs motor and haptic data into a binary packet and sends it via a background thread.
/// </summary>
public class SentSerialController : MonoBehaviour {

    /// <summary>
    /// binary structure for hardware control signals.
    /// uses Pack=1 to ensure no padding is added between fields for serial transmission.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ControlData {
        public byte smEnable;       // steering motor enable state
        public byte smDirection;    // steering motor direction
        public ushort smPwm;        // steering motor power (PWM)
        public ushort vmLeftPwm;    // left vibration motor power
        public ushort vmRightPwm;   // right vibration motor power
    }

    [Header("Data Sources")]
    [Tooltip("Configuration scriptable object containing safety limits.")]
    public LKAConfiguration config;
    [Tooltip("Reference to the LKA logic controller.")]
    public LaneKeepingAssistController laneKeepingAssist;
    [Tooltip("Reference to the haptic feedback system.")]
    public ML_LaneHapticsFromPercent haptics;

    [Header("Settings")]
    [Tooltip("The COM port name (e.g., COM3 on Windows or /dev/ttyUSB0 on Linux).")]
    public string portName = "COM3";
    [Tooltip("Communication speed. Must match the microcontroller's setup.")]
    public int baudRate = 256000;
    [Tooltip("Frequency of data transmission in milliseconds.")]
    public int sendIntervalMs = 5;
    private float uiUpdateRate = 0.5f;

    [Header("Live Debug")]
    [Tooltip("Mirrors of the binary data for real-time monitoring in the Inspector.")]
    public bool LKA_Engaged_Debug;
    public int MotorDirection_Debug;
    public int SteeringPWM_Debug;
    public int VibLeftPWM_Debug;
    public int VibRightPWM_Debug;

    private SerialPort serialPort;
    private Thread writeThread;
    private bool isWriting = false;
    private ControlData latestData;
    private readonly object _dataLock = new object(); // prevents thread-clashing when reading/writing data
    private float nextUiUpdateTime = 0f;

    /// <summary>
    /// gathers initial references and sets hardware to safe default values.
    /// </summary>
    void Awake() {
        // search scene for missing dependencies
        if (config == null) config = FindObjectOfType<LKAConfiguration>();
        if (laneKeepingAssist == null) laneKeepingAssist = FindObjectOfType<LaneKeepingAssistController>();
        if (haptics == null) haptics = FindObjectOfType<ML_LaneHapticsFromPercent>();

        // apply safety defaults from config if available
        if (config != null) {
            latestData = new ControlData {
                smEnable = 0,
                smDirection = 0,
                smPwm = (ushort)config.SteeringPwmMinLimit,
                vmLeftPwm = (ushort)config.VibrationPwmIdleValue,
                vmRightPwm = (ushort)config.VibrationPwmIdleValue
            };
        } else {
            Debug.LogError("[Serial Test] LKAConfiguration not found! Using hardcoded safety defaults.");
            latestData = new ControlData { smPwm = 410, vmLeftPwm = 2048, vmRightPwm = 2048 };
        }

        StartSerialThread();
    }

    /// <summary>
    /// updates the buffer with the latest game data every frame.
    /// </summary>
    void Update() {
        PrepareBinaryBuffer();
        UpdateDebugMirrors();
    }

    /// <summary>
    /// pulls data from LKA and Haptics scripts and locks it for the serial thread.
    /// </summary>
    private void PrepareBinaryBuffer() {
        if (laneKeepingAssist != null && haptics != null) {
            lock (_dataLock) {
                latestData.smEnable = (byte)(laneKeepingAssist.isEngaged ? 1 : 0);
                latestData.smDirection = (byte)(laneKeepingAssist.SteeringMotorDirection ? 1 : 0);
                latestData.smPwm = (ushort)laneKeepingAssist.SteeringMotorPWM;
                latestData.vmLeftPwm = (ushort)haptics.pwmLeft;
                latestData.vmRightPwm = (ushort)haptics.pwmRight;
            }
        }
    }

    /// <summary>
    /// updates the inspector debug variables at a throttled rate for performance.
    /// </summary>
    private void UpdateDebugMirrors() {
        if (Time.time >= nextUiUpdateTime) {
            LKA_Engaged_Debug = latestData.smEnable == 1;
            MotorDirection_Debug = latestData.smDirection;
            SteeringPWM_Debug = latestData.smPwm;
            VibLeftPWM_Debug = latestData.vmLeftPwm;
            VibRightPWM_Debug = latestData.vmRightPwm;

            nextUiUpdateTime = Time.time + uiUpdateRate;
        }
    }

    /// <summary>
    /// launches the dedicated background thread for serial port writing.
    /// </summary>
    private void StartSerialThread() {
        isWriting = true;
        writeThread = new Thread(WriteDataLoop);
        writeThread.Start();
    }

    /// <summary>
    /// background loop that manages the serial port and sends the binary packets.
    /// </summary>
    private void WriteDataLoop() {
        int structSize = Marshal.SizeOf(typeof(ControlData));
        int totalPacketSize = structSize + 3; // +3 for Start/End markers (AA, BB ... CC)
        byte[] packetBuffer = new byte[totalPacketSize];

        // set header and footer markers
        packetBuffer[0] = 0xAA;
        packetBuffer[1] = 0xBB;
        packetBuffer[totalPacketSize - 1] = 0xCC;

        try {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.Open();

            while (isWriting && serialPort.IsOpen) {
                ControlData dataToSend;

                // capture the current state of data safely
                lock (_dataLock) { dataToSend = latestData; }

                // convert struct to byte array using marshalling
                IntPtr ptr = Marshal.AllocHGlobal(structSize);
                try {
                    Marshal.StructureToPtr(dataToSend, ptr, false);
                    Marshal.Copy(ptr, packetBuffer, 2, structSize);
                } finally {
                    Marshal.FreeHGlobal(ptr);
                }

                // transmit the packet to the hardware
                serialPort.Write(packetBuffer, 0, totalPacketSize);
                Thread.Sleep(sendIntervalMs);
            }
        } catch (Exception e) {
            Debug.LogError($"[Serial TX Error]: {e.Message}");
        } finally {
            // ensure the port is closed and hardware is potentially reset
            if (serialPort != null && serialPort.IsOpen) {
                byte[] safePacket = new byte[totalPacketSize];
                Array.Clear(safePacket, 0, safePacket.Length);
                serialPort.Write(safePacket, 0, totalPacketSize);
                serialPort.Close();
            }
        }
    }

    // ensure threads are killed when Unity stops or object is destroyed
    void OnDestroy() => StopSerialThread();
    void OnApplicationQuit() => StopSerialThread();

    /// <summary>
    /// forces a hardware safety reset and stops the communication thread.
    /// </summary>
    public void ShutdownSerial() {
        isWriting = false;

        if (serialPort != null && serialPort.IsOpen) {
            lock (_dataLock) {
                // reset internal data to safe defaults
                latestData.smEnable = 0;
                latestData.smDirection = 0;
                latestData.smPwm = (ushort)(config != null ? config.SteeringPwmMinLimit : 410);
                latestData.vmLeftPwm = (ushort)(config != null ? config.VibrationPwmIdleValue : 2048);
                latestData.vmRightPwm = (ushort)(config != null ? config.VibrationPwmIdleValue : 2048);
            }

            // pack the safety data into a final transmission
            int structSize = Marshal.SizeOf(typeof(ControlData));
            int totalPacketSize = structSize + 3;
            byte[] safetyBuffer = new byte[totalPacketSize];
            safetyBuffer[0] = 0xAA;
            safetyBuffer[1] = 0xBB;
            safetyBuffer[totalPacketSize - 1] = 0xCC;

            IntPtr ptr = Marshal.AllocHGlobal(structSize);
            try {
                Marshal.StructureToPtr(latestData, ptr, false);
                Marshal.Copy(ptr, safetyBuffer, 2, structSize);
                serialPort.Write(safetyBuffer, 0, totalPacketSize);
            } finally {
                Marshal.FreeHGlobal(ptr);
            }

            serialPort.Close();
            Debug.Log("[SentSerial] Hardware set to SAFE state and port closed.");
        }

        // wait for the background thread to finish its last loop
        if (writeThread != null && writeThread.IsAlive) writeThread.Join(50);
    }

    /// <summary>
    /// signals the writing thread to stop and waits for it to exit.
    /// </summary>
    private void StopSerialThread() {
        isWriting = false;
        if (writeThread != null && writeThread.IsAlive) writeThread.Join(500);
    }
}
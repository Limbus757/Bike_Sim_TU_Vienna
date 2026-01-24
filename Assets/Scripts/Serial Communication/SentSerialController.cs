using UnityEngine;
using System.IO.Ports;
using System.Threading;
using System.Runtime.InteropServices;
using System;

public class SentSerialController : MonoBehaviour {

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ControlData {
        public byte smEnable;       // SM_ENABLE_PIN
        public byte smDirection;    // SM_DIRECTION_PIN
        public ushort smPwm;        // SM_PWM_PIN
        public ushort vmLeftPwm;    // VM_PWM_L_PIN 
        public ushort vmRightPwm;   // VM_PWM_R_PIN
    }

    [Header("Data Sources")]
    public LKAConfiguration config; // Added Configuration Source
    public LaneKeepingAssistController laneKeepingAssist;
    public ML_LaneHapticsFromPercent haptics;

    [Header("Settings")]
    public string portName = "COM3";
    public int baudRate = 256000;
    public int sendIntervalMs = 5;
    private float uiUpdateRate = 0.5f;

    [Header("Live Debug")]
    public bool LKA_Engaged_Debug;
    public int MotorDirection_Debug;
    public int SteeringPWM_Debug;
    public int VibLeftPWM_Debug;
    public int VibRightPWM_Debug;

    private SerialPort serialPort;
    private Thread writeThread;
    private bool isWriting = false;
    private ControlData latestData;
    private readonly object _dataLock = new object();
    private float nextUiUpdateTime = 0f;

    void Awake() {
        // Automatically try to find config if not set in inspector
        if (config == null) config = FindObjectOfType<LKAConfiguration>();
        if (laneKeepingAssist == null) laneKeepingAssist = FindObjectOfType<LaneKeepingAssistController>();
        if (haptics == null) haptics = FindObjectOfType<ML_LaneHapticsFromPercent>();

        if (config != null) {
            // Initialize with values defined in your LKAConfiguration
            latestData = new ControlData {
                smEnable = 0,
                smDirection = 0,
                smPwm = (ushort)config.SteeringPwmMinLimit,   // Start at safety floor
                vmLeftPwm = (ushort)config.VibrationPwmIdleValue,
                vmRightPwm = (ushort)config.VibrationPwmIdleValue
            };
        } else {
            Debug.LogError("[Serial Test] LKAConfiguration not found! Using hardcoded safety defaults.");
            latestData = new ControlData { smPwm = 410, vmLeftPwm = 2048, vmRightPwm = 2048 };
        }

        StartSerialThread();
    }

    void Update() {
        PrepareBinaryBuffer();
        UpdateDebugMirrors();
    }

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

    private void UpdateDebugMirrors() {
        if (Time.time >= nextUiUpdateTime) {
            // No lock used here as per your preference; slight risk of torn read 
            // is acceptable for visual debugging.
            LKA_Engaged_Debug = latestData.smEnable == 1;
            MotorDirection_Debug = latestData.smDirection;
            SteeringPWM_Debug = latestData.smPwm;
            VibLeftPWM_Debug = latestData.vmLeftPwm;
            VibRightPWM_Debug = latestData.vmRightPwm;

            nextUiUpdateTime = Time.time + uiUpdateRate;
        }
    }

    private void StartSerialThread() {
        isWriting = true;
        writeThread = new Thread(WriteDataLoop);
        writeThread.Start();
    }

    private void WriteDataLoop() {
        int structSize = Marshal.SizeOf(typeof(ControlData));
        int totalPacketSize = structSize + 3;
        byte[] packetBuffer = new byte[totalPacketSize];

        packetBuffer[0] = 0xAA;
        packetBuffer[1] = 0xBB;
        packetBuffer[totalPacketSize - 1] = 0xCC;

        try {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.Open();

            while (isWriting && serialPort.IsOpen) {
                ControlData dataToSend;
                lock (_dataLock) { dataToSend = latestData; }

                IntPtr ptr = Marshal.AllocHGlobal(structSize);
                try {
                    Marshal.StructureToPtr(dataToSend, ptr, false);
                    Marshal.Copy(ptr, packetBuffer, 2, structSize);
                } finally {
                    Marshal.FreeHGlobal(ptr);
                }

                serialPort.Write(packetBuffer, 0, totalPacketSize);
                Thread.Sleep(sendIntervalMs);
            }
        } catch (Exception e) {
            Debug.LogError($"[Serial TX Error]: {e.Message}");
        } finally {
            if (serialPort != null && serialPort.IsOpen) {
                // Safety: On close, reset to idle values from config if possible
                byte[] safePacket = new byte[totalPacketSize];
                Array.Clear(safePacket, 0, safePacket.Length);
                // You could manually set the idle bytes in safePacket here if needed
                serialPort.Write(safePacket, 0, totalPacketSize);
                serialPort.Close();
            }
        }
    }

    void OnDestroy() => StopSerialThread();
    void OnApplicationQuit() => StopSerialThread();

    public void ShutdownSerial() {
        isWriting = false; // Signal thread to stop loop

        if (serialPort != null && serialPort.IsOpen) {
            lock (_dataLock) {
                // Reset to safe defaults defined in your config
                latestData.smEnable = 0;
                latestData.smDirection = 0;
                latestData.smPwm = (ushort)(config != null ? config.SteeringPwmMinLimit : 410);
                latestData.vmLeftPwm = (ushort)(config != null ? config.VibrationPwmIdleValue : 2048);
                latestData.vmRightPwm = (ushort)(config != null ? config.VibrationPwmIdleValue : 2048);
            }

            // Prepare and send the final safety packet
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

        if (writeThread != null && writeThread.IsAlive) writeThread.Join(500);
    }

    private void StopSerialThread() {
        isWriting = false;
        if (writeThread != null && writeThread.IsAlive) writeThread.Join(500);
    }
}
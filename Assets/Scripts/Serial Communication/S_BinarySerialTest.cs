using UnityEngine;
using System.IO.Ports;
using System.Threading;
using System.Runtime.InteropServices;
using System;

/// <summary>
/// Handles high-speed binary serial communication (Write Only) to the Arduino R4.
/// Formats data into a packed struct for fast data transfer.
/// </summary>
public class S_BinarySerialTest : MonoBehaviour {
    
    // struct matches the Arduino memory layout exactly for bit-perfect data transfer
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ControlData {
        public byte smEnable;       // SM_ENABLE_PIN
        public byte smDirection;    // SM_DIRECTION_PIN
        public ushort smPwm;        // SM_PWM_PIN (12-bit: 0-4095)
        public ushort vmLeftPwm;    // VM_PWM_L_PIN (12-bit: 0-4095)
        public ushort vmRightPwm;   // VM_PWM_R_PIN (12-bit: 0-4095)
    }

    [Header("Data Sources")]
    public LaneKeepingAssistController laneKeepingAssist;
    public ML_LaneHapticsFromPercent haptics;

    [Header("Settings")]
    public string portName = "COM3";
    public int baudRate = 115200;
    public int sendIntervalMs = 20;

    [Header("Live Debug")]
    public int SteeringPWM_Debug;
    public int VibLeftPWM_Debug;
    public int VibRightPWM_Debug;

    private SerialPort serialPort;
    private Thread writeThread;
    private bool isWriting = false;
    private ControlData latestData;
    private readonly object _dataLock = new object();

    void Awake() {
        // Initialize with safe 12-bit defaults (Idle = 2048)
        latestData = new ControlData {
            smEnable = 0,
            smDirection = 0,
            smPwm = 410,
            vmLeftPwm = 2048,
            vmRightPwm = 2048
        };
        StartSerialThread();
    }

    void Update() {
        PrepareBinaryBuffer();
    }

    private void PrepareBinaryBuffer() {
        if (laneKeepingAssist != null && haptics != null) {
            lock (_dataLock) {
                // Map LKA and Haptic values to the 12-bit structure
                latestData.smEnable = (byte)(laneKeepingAssist.isEngaged ? 1 : 0);
                latestData.smDirection = (byte)(laneKeepingAssist.SteeringMotorDirection ? 1 : 0);

                // Unity values should be scaled to 0-4095 for the R4 12-bit PWM
                latestData.smPwm = (ushort)laneKeepingAssist.SteeringMotorPWM;
                //latestData.vmLeftPwm = (ushort)haptics.pwmLeft4095;
                //latestData.vmRightPwm = (ushort)haptics.pwmRight4095;

                // Update debug mirrors
                SteeringPWM_Debug = latestData.smPwm;
                VibLeftPWM_Debug = latestData.vmLeftPwm;
                VibRightPWM_Debug = latestData.vmRightPwm;
            }
        }
    }

    private void StartSerialThread() {
        isWriting = true;
        writeThread = new Thread(WriteDataLoop);
        writeThread.Start();
    }

    private void WriteDataLoop() {
        int structSize = Marshal.SizeOf(typeof(ControlData));
        int totalPacketSize = structSize + 3; // 2 Header bytes (0xAA, 0xBB) + Payload + 1 Footer (0xCC)
        byte[] packetBuffer = new byte[totalPacketSize];

        // Set constant headers/footers
        packetBuffer[0] = 0xAA;
        packetBuffer[1] = 0xBB;
        packetBuffer[totalPacketSize - 1] = 0xCC;

        try {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.Open();

            while (isWriting && serialPort.IsOpen) {
                ControlData dataToSend;
                lock (_dataLock) { dataToSend = latestData; }

                // Pin struct in memory to copy raw bytes into the packet buffer
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
                // Send safety shutdown packet on exit
                byte[] safePacket = new byte[totalPacketSize];
                Array.Clear(safePacket, 0, safePacket.Length);
                serialPort.Write(safePacket, 0, totalPacketSize);

                serialPort.Close();
                Debug.Log("[Serial TX] Port closed safely.");
            }
        }
    }

    void OnDestroy() => StopSerialThread();
    void OnApplicationQuit() => StopSerialThread();

    private void StopSerialThread() {
        isWriting = false;
        if (writeThread != null && writeThread.IsAlive) writeThread.Join(500);
    }
}
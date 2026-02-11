using UnityEngine;
using System.IO.Ports;
using System.Threading;
using System;
using System.Runtime.InteropServices;
using System.Diagnostics;

public class ReceivedSerialProvider: MonoBehaviour {

    // struct matches esp32 memory layout exactly for bit-perfect data transfer
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TelemetryData {
        public float speed;
        public float frontBrake;
        public float rearBrake;
        public ushort resistance;
        public byte lkaSwitch;
        public uint blePeriod;
        public uint serialPeriod;
    }

    // static globals allowing any script in the project to access data without references
    public static float SpeedKmh = 0.0f;
    public static float FrontBrakeForce = 0.0f;
    public static float RearBrakeForce = 0.0f;
    public static bool LkaSwitchState = false;
    public static float ResistanceValue = 0.0f;
    public static uint BleActualPeriodMs = 0;
    public static uint SerialActualPeriodMs = 0;

    [Header("Settings")]
    public string portName = "COM5";
    public int baudRate = 115200;
    public bool InvertLkaSwitch = true;

    [Header("Connection Status")]
    public bool IsSerialActive;
    public int ProcessingTimeUs;

    [Header("Live Telemetry Debug")]
    public float SpeedKmh_Debug;
    public float FrontBrakeForce_Debug;
    public float RearBrakeForce_Debug;
    public float ResistanceValue_Debug;
    public bool LkaSwitchState_Debug;
    public uint BlePeriod_Debug;
    public uint SerialPeriod_Debug;

    private SerialPort serialPort;
    private Thread readThread;
    private bool isReading = false;
    private TelemetryData latestData;
    private double internalProcTimeUs;
    private double lastPacketTimestamp;
    private readonly object _dataLock = new(); // prevents thread collisions

    private float debugUpdateTimer = 0f;
    private const float debugUpdateInterval = 0.2f;

    void Awake() {
        StartSerialThread();
    }

    void Update() {
        // use system stopwatch for thread-safe heartbeat check
        double currentTime = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        IsSerialActive = (currentTime - lastPacketTimestamp) < 0.5;

        // sync background thread data to main thread variables
        lock (_dataLock) {
            SpeedKmh = latestData.speed;
            FrontBrakeForce = latestData.frontBrake;
            RearBrakeForce = latestData.rearBrake;
            ResistanceValue = latestData.resistance;

            bool rawSwitch = latestData.lkaSwitch == 1;
            LkaSwitchState = InvertLkaSwitch ? !rawSwitch : rawSwitch;

            BleActualPeriodMs = latestData.blePeriod;
            SerialActualPeriodMs = latestData.serialPeriod;
        }

        // update inspector values at a lower frequency to save cpu
        debugUpdateTimer += Time.deltaTime;
        if (debugUpdateTimer >= debugUpdateInterval) {
            debugUpdateTimer = 0f;
            UpdateDebugMirrors();
        }
    }

    private void UpdateDebugMirrors() {
        // round floats to 2 decimals for a clean inspector view
        SpeedKmh_Debug = (float)Math.Round(SpeedKmh, 2);
        FrontBrakeForce_Debug = (float)Math.Round(FrontBrakeForce, 2);
        RearBrakeForce_Debug = (float)Math.Round(RearBrakeForce, 2);
        ResistanceValue_Debug = ResistanceValue;
        LkaSwitchState_Debug = LkaSwitchState;
        BlePeriod_Debug = BleActualPeriodMs;
        SerialPeriod_Debug = SerialActualPeriodMs;
        ProcessingTimeUs = (int)internalProcTimeUs; // integer for clarity
    }

    private void StartSerialThread() {
        isReading = true;
        readThread = new Thread(ReadDataLoop);
        readThread.Start();
    }

    private void ReadDataLoop() {
        Stopwatch sw = new();
        int packetSize = Marshal.SizeOf(typeof(TelemetryData));
        byte[] buffer = new byte[packetSize];

        while (isReading) {
            try {
                // handle initial connection and auto-reconnect
                if (serialPort == null || !serialPort.IsOpen) {
                    serialPort = new SerialPort(portName, baudRate) { ReadTimeout = 500 };
                    serialPort.Open();
                    UnityEngine.Debug.Log($"serial connected to {portName}");
                }

                while (isReading && serialPort.IsOpen) {
                    // search for the 2-byte header start sequence
                    if (serialPort.ReadByte() == 0xAA) {
                        if (serialPort.ReadByte() == 0xBB) {
                            sw.Restart();

                            // pull the raw binary payload into our buffer
                            int bytesRead = 0;
                            while (bytesRead < packetSize) {
                                int readCount = serialPort.Read(buffer, bytesRead, packetSize - bytesRead);
                                bytesRead += readCount;
                            }

                            // verify the packet ended where it should have
                            if (serialPort.ReadByte() == 0xCC) {
                                // pin buffer in memory to safely map raw bytes to struct
                                GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                                try {
                                    TelemetryData incoming = (TelemetryData)Marshal.PtrToStructure(
                                        handle.AddrOfPinnedObject(),
                                        typeof(TelemetryData)
                                    );

                                    sw.Stop();

                                    // update latest data and timestamp for the main thread
                                    lock (_dataLock) {
                                        latestData = incoming;
                                        lastPacketTimestamp = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
                                        internalProcTimeUs = (sw.ElapsedTicks / (double)Stopwatch.Frequency) * 1000000.0;
                                    }
                                } finally {
                                    handle.Free(); // always unpin to avoid memory leaks
                                }
                            }
                        }
                    }
                }
            } catch (Exception e) {
                UnityEngine.Debug.LogWarning($"serial error: {e.Message}");
                if (serialPort != null && serialPort.IsOpen) serialPort.Close();
                Thread.Sleep(2000); // cooldown prevents cpu spamming during disconnects
            }
        }
    }

    public void ShutdownSerial() {
        isReading = false; // Signal background thread to stop

        // zero out static globals to prevent other scripts from using stale data
        lock (_dataLock) {
            SpeedKmh = 0f;
            FrontBrakeForce = 0f;
            RearBrakeForce = 0f;
            LkaSwitchState = false;
            ResistanceValue = 0f;
            latestData = new TelemetryData(); // Clear internal struct
        }

        if (serialPort != null && serialPort.IsOpen) {
            serialPort.Close();
            UnityEngine.Debug.Log("[ReceivedSerial] Input stream closed and globals zeroed.");
        }

        if (readThread != null && readThread.IsAlive) readThread.Join(50);
    }

    void OnDestroy() {
        // signal thread to stop and wait for it to finish gracefully
        isReading = false;
        if (readThread != null && readThread.IsAlive) readThread.Join();
        if (serialPort != null && serialPort.IsOpen) serialPort.Close();
    }
}
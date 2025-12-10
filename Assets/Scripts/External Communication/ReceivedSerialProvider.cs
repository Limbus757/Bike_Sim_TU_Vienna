using UnityEngine;
using System.IO.Ports;
using System.Threading;
using System.Collections.Generic;
using System;
using System.Globalization;

/// <summary>
/// Handles one-way serial communication (Read Only) with the ESP32-S3.
/// This version makes static variables readable in the Inspector via a separate Debug class
/// and displays the last received string directly for sanity checking.
/// </summary>
public class ReceivedSerialProvider : MonoBehaviour {
   
    // --- Public Static Global Variables ---
   
    // Telemetry Data
    public static float SpeedKmh = 0.0f;
    public static float FrontBrakeForce = 0.0f;
    public static float RearBrakeForce = 0.0f;
    public static bool LkaSwitchState = false;
    public static float ResistanceValue = 0.0f;
    public static uint BleActualPeriodMs = 0;
    public static uint SerialActualPeriodMs = 0;

    // --- Inspector Display & Sanity Check Fields ---
    [Header("1. Settings")]
    [Tooltip("The name of the serial port (e.g., COM3 on Windows).")]
    public string portName = "COM3";

    [Tooltip("The communication speed in bits per second, must match microcontroller.")]
    public int baudRate = 115200;

    [Header("2. Live Debug Data")]
    [Tooltip("The last full line received from the serial port.")]
    public string lastReceivedString = "No Data Received Yet";

    [Tooltip("A quick check of the parsed values (Speed, F_Brake, R_Brake, Resistance)")]
    public string parsedValuesCheck = "0.0 | 0.0 | 0.0 | 0.0";

    // =========================================================================
    // --- Private Fields ---
    // =========================================================================
    private SerialPort serialPort;
    private Thread readThread;
    private bool isReading = false;
    private Queue<string> _receivedDataQueue = new Queue<string>();
    private object _queueLock = new object();

    void Awake() {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        StartSerialThread();
    }

    void Update() {
        ProcessReceivedData();
        UpdateInspectorDebugFields();
    }

    /// <summary>
    /// Updates the public non-static fields used for inspector display.
    /// </summary>
    private void UpdateInspectorDebugFields() {
        // Update the parsed values debug string
        parsedValuesCheck = string.Format(
            CultureInfo.InvariantCulture,
            "S:{0:F2} | FB:{1:F1} | RB:{2:F1} | R:{3:F1} | LKA:{4} | BLE:{5}ms",
            SpeedKmh,
            FrontBrakeForce,
            RearBrakeForce,
            ResistanceValue,
            LkaSwitchState ? "ON" : "OFF",
            BleActualPeriodMs
        );
    }

    /// <summary>
    /// Initializes and starts the background thread for serial communication.
    /// </summary>
    private void StartSerialThread() {
        isReading = true;
        readThread = new Thread(ReadData);
        readThread.Start();
    }

    /// <summary>
    /// The main loop for the reading thread.
    /// </summary>
    private void ReadData() {
        try {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.Open();

            while (isReading) {
                if (serialPort.IsOpen && serialPort.BytesToRead > 0) {
                    string data = serialPort.ReadLine();
                    lock (_queueLock) {
                        _receivedDataQueue.Enqueue(data);
                    }
                }
                Thread.Sleep(1);
            }
        } catch (Exception e) {
            Debug.LogError($"[Serial Error] Failed to open or read serial port: {e.Message}");
        } finally {
            if (serialPort != null && serialPort.IsOpen) {
                serialPort.Close();
            }
        }
    }

    /// <summary>
    /// Processes data from the queue and updates global variables if valid.
    /// </summary>
    private void ProcessReceivedData() {
        lock (_queueLock) {
            string dataToProcess = null;

            // Only take the latest message to prevent lag
            while (_receivedDataQueue.Count > 0) {
                dataToProcess = _receivedDataQueue.Dequeue().Trim();
            }

            if (dataToProcess != null) {
                // always update the Inspector string
                lastReceivedString = dataToProcess;

                // Only parse and update static globals if it starts with 'U,'
                if (dataToProcess.StartsWith("U,")) {
                    string dataPayload = dataToProcess.Substring(2);
                    string[] values = dataPayload.Split(',');

                    if (values.Length == 7) {
                        ParseAndSetGlobalData(values);
                    } else {
                        Debug.LogWarning($"[Serial Parser] Expected 7 values, received {values.Length} for: {dataToProcess}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Safely parses the string array and updates the public static global variables.
    /// </summary>
    private void ParseAndSetGlobalData(string[] values) {
        const NumberStyles style = NumberStyles.Float;
        var culture = CultureInfo.InvariantCulture;
        int tempInt;

        // --- Parse Floats ---
        float.TryParse(values[0], style, culture, out SpeedKmh);
        float.TryParse(values[1], style, culture, out FrontBrakeForce);
        float.TryParse(values[2], style, culture, out RearBrakeForce);
        float.TryParse(values[4], style, culture, out ResistanceValue);

        // --- Parse LkaSwitchState ---
        if (int.TryParse(values[3], out tempInt)) {
            LkaSwitchState = (tempInt == 1);
        }

        // --- Parse UInts ---
        uint.TryParse(values[5], out BleActualPeriodMs);
        uint.TryParse(values[6], out SerialActualPeriodMs);
    }

    // --- Cleanup Methods ---
    void OnDestroy() {
        StopSerialThread();
    }

    void OnApplicationQuit() {
        StopSerialThread();
    }

    private void StopSerialThread() {
        isReading = false;
        if (readThread != null && readThread.IsAlive) {
            readThread.Join();
        }
    }
}
using UnityEngine;
using System.IO.Ports;
using System.Threading;
using System.Collections.Generic;
using System;
using System.Globalization;

/// <summary>
/// Handles one-way serial communication (Read Only) with the ESP32-S3.
/// This version makes static variables reliably inspectable by using non-static fields
/// that are updated every frame.
/// </summary>
public class ReceivedSerialProvider : MonoBehaviour
{

    // =========================================================================
    // --- Public Static Global Variables (Data used by other scripts like SimulatorInputProviderHT) ---
    // =========================================================================
    public static float SpeedKmh = 0.0f;
    public static float FrontBrakeForce = 0.0f;
    public static float RearBrakeForce = 0.0f;
    public static bool LkaSwitchState = false;
    public static float ResistanceValue = 0.0f;
    public static uint BleActualPeriodMs = 0;
    public static uint SerialActualPeriodMs = 0;

    

    // =========================================================================
    // --- Inspector Display & Sanity Check Fields ---
    // =========================================================================
    [Header("1. Settings")]
    [Tooltip("The name of the serial port (e.g., COM3 on Windows).")]
    public string portName = "COM3";

    [Tooltip("The communication speed in bits per second, must match microcontroller.")]
    public int baudRate = 115200;

    [Header("2. Live Debug Data")]
    [Tooltip("The last full line received from the serial port.")]
    public string lastReceivedString = "No Data Received Yet";

    [Tooltip("A quick check of the parsed values (S, FB, RB, R)")]
    public string parsedValuesCheck = "0.0 | 0.0 | 0.0 | 0.0";

    // =========================================================================
    // --- Internal Static Exposer (FOR INSPECTOR DISPLAY ONLY) ---
    // =========================================================================
    // These non-static public fields mirror the static variables and will be
    // explicitly updated in Update() to guarantee Inspector visibility.

    [Header("3. Telemetry (Live Static Data)")]
    [Tooltip("Read-only mirror of the static SpeedKmh.")]
    public float Display_SpeedKmh = 0f;
    [Tooltip("Read-only mirror of the static FrontBrakeForce.")]
    public float Display_FrontBrakeForce = 0f;
    [Tooltip("Read-only mirror of the static RearBrakeForce.")]
    public float Display_RearBrakeForce = 0f;
    [Tooltip("Read-only mirror of the static ResistanceValue.")]
    public float Display_ResistanceValue = 0f;
    [Tooltip("Read-only mirror of the static LkaSwitchState.")]
    public bool Display_LkaSwitchState = false;
    [Tooltip("Read-only mirror of the static BleActualPeriodMs.")]
    public uint Display_BleActualPeriodMs = 0;
    [Tooltip("Read-only mirror of the static SerialActualPeriodMs.")]
    public uint Display_SerialActualPeriodMs = 0;


    // =========================================================================
    // --- Private Fields ---
    // =========================================================================
    private SerialPort serialPort;
    private Thread readThread;
    private bool isReading = false;
    private Queue<string> _receivedDataQueue = new Queue<string>();
    private object _queueLock = new object();

    // --- Fields for throttling UI updates ---
    private float _lastLogTime = 0f;
    private const float LogInterval = 1.0f; // Update raw log string in Inspector only once per second

    void Awake()
    {
        // Ensure that float parsing uses '.' as the decimal separator
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        StartSerialThread();
    }

    void Update()
    {
        ProcessReceivedData();
        UpdateInspectorDebugFields(); // Now also updates the live telemetry mirrors
    }

    /// <summary>
    /// Updates all public non-static fields used for inspector display, including
    /// the mirrors for the static telemetry data.
    /// </summary>
    private void UpdateInspectorDebugFields()
    {
        // 1. Update the non-static display mirrors from the static variables
        // We use a lock here to ensure safe read access to all static variables at once
        lock (_queueLock)
        {
            Display_SpeedKmh = SpeedKmh;
            Display_FrontBrakeForce = FrontBrakeForce;
            Display_RearBrakeForce = RearBrakeForce;
            Display_ResistanceValue = ResistanceValue;
            Display_LkaSwitchState = LkaSwitchState;
            Display_BleActualPeriodMs = BleActualPeriodMs;
            Display_SerialActualPeriodMs = SerialActualPeriodMs;
        }

        // 2. Update the combined parsed values debug string
        parsedValuesCheck = string.Format(
            CultureInfo.InvariantCulture,
            "S:{0:F2} | FB:{1:F1} | RB:{2:F1} | R:{3:F1} | LKA:{4} | BLE:{5}ms",
            Display_SpeedKmh, // Use the display fields for consistency
            Display_FrontBrakeForce,
            Display_RearBrakeForce,
            Display_ResistanceValue,
            Display_LkaSwitchState ? "ON" : "OFF",
            Display_BleActualPeriodMs
        );
    }

    /// <summary>
    /// Initializes and starts the background thread for serial communication.
    /// </summary>
    private void StartSerialThread()
    {
        isReading = true;
        readThread = new Thread(ReadData);
        readThread.Start();
    }

    /// <summary>
    /// The main loop for the reading thread.
    /// </summary>
    private void ReadData()
    {
        try
        {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.NewLine = "\n";
            serialPort.Open();

            while (isReading)
            {
                if (serialPort.IsOpen && serialPort.BytesToRead > 0)
                {
                    string data = serialPort.ReadLine();
                    lock (_queueLock)
                    {
                        _receivedDataQueue.Enqueue(data);
                    }
                }
                Thread.Sleep(1);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Serial Error] Failed to open or read serial port: {e.Message}");
        }
        finally
        {
            if (serialPort != null && serialPort.IsOpen)
            {
                serialPort.Close();
            }
        }
    }

    /// <summary>
    /// Processes data from the queue, handles concatenated strings, and updates global variables.
    /// </summary>
    private void ProcessReceivedData()
    {
        lock (_queueLock)
        {
            string dataToProcess = null;

            // Only take the latest message to prevent lag.
            while (_receivedDataQueue.Count > 0)
            {
                dataToProcess = _receivedDataQueue.Dequeue().Trim();
            }

            if (dataToProcess != null)
            {
                // Handle concatenated messages by splitting by "U," and taking the last line
                string[] lines = dataToProcess.Split(new string[] { "U," }, StringSplitOptions.RemoveEmptyEntries);

                string latestLine = dataToProcess;
                if (lines.Length > 0)
                {
                    latestLine = "U," + lines[lines.Length - 1].Trim();
                }

                bool isTelemetry = latestLine.StartsWith("U,");

                // Throttle Inspector raw string update
                if (!isTelemetry || Time.time > _lastLogTime + LogInterval)
                {
                    lastReceivedString = latestLine;
                    _lastLogTime = Time.time;
                }

                // Parsing Logic
                if (isTelemetry)
                {
                    string dataPayload = latestLine.Substring(2);
                    string[] values = dataPayload.Split(',');

                    if (values.Length == 7)
                    {
                        ParseAndSetGlobalData(values);
                    }
                    else
                    {
                        Debug.LogWarning($"[Serial Parser] Expected 7 values, received {values.Length} for: {latestLine}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Safely parses the string array and updates the public static global variables.
    /// </summary>
    private void ParseAndSetGlobalData(string[] values)
    {
        const NumberStyles style = NumberStyles.Float;
        var culture = CultureInfo.InvariantCulture;
        int tempInt;

        // Note: The static variables are updated here. The non-static Display_ fields are updated in Update().
        float.TryParse(values[0], style, culture, out SpeedKmh);
        float.TryParse(values[1], style, culture, out FrontBrakeForce);
        float.TryParse(values[2], style, culture, out RearBrakeForce);
        float.TryParse(values[4], style, culture, out ResistanceValue);

        if (int.TryParse(values[3], out tempInt))
        {
            LkaSwitchState = (tempInt == 1);
        }

        uint.TryParse(values[5], out BleActualPeriodMs);
        uint.TryParse(values[6], out SerialActualPeriodMs);
    }

    // --- Cleanup Methods ---
    void OnDestroy()
    {
        StopSerialThread();
    }

    void OnApplicationQuit()
    {
        StopSerialThread();
    }

    private void StopSerialThread()
    {
        isReading = false;
        if (readThread != null && readThread.IsAlive)
        {
            readThread.Join();
        }
    }
}
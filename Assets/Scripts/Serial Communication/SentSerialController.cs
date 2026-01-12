using UnityEngine;
using System.IO.Ports;
using System.Threading;
using System.Globalization;
using System;

/// <summary>
/// Handles one-way serial communication (Write Only) to an external device.
/// It collects motor control values from the LaneKeepingAssist script and sends them 
/// on a separate thread at a fixed configurable interval.
/// </summary>
public class SentSerialController : MonoBehaviour
{

    // --- Data Source ---
    [Header("0. Data Source")]
    [Tooltip("The LaneKeepingAssist script that determines the motor control values.")]
    public LaneKeepingAssist laneKeepingAssist;

    // --- Configuration & Debug Fields ---
    [Header("1. Settings")]
    [Tooltip("The name of the serial port (e.g., COM3 on Windows).")]
    public string portName = "COM3";

    [Tooltip("The communication speed in bits per second, must match microcontroller.")]
    public int baudRate = 115200;

    [Tooltip("The interval in milliseconds for sending data. 20ms = 50Hz.")]
    public int sendIntervalMs = 20;

    [Header("2. Live Debug Data")]
    [Tooltip("The last full line sent to the serial port.")]
    public string lastSentString = "No message sent yet.";

    [Tooltip("Actual period (in ms) of the sending thread.")]
    public uint actualSendPeriodMs = 0;

    // --- Control Outputs (Mapped to ESP32 Pins) ---
    [Header("3. Steering Control Outputs")]
    [Tooltip("Value for the Direction Pin (0=Left, 1=Right).")]
    public int SteeringDirPinValue = 0;

    [Tooltip("Value for the Enable Pin (0=OFF, 1=ON/Active Correction).")]
    public int SteeringENPinValue = 0;

    [Tooltip("PWM Value for the main speed control (MIN_PWM-MAX_PWM).")]
    public int SteeringPWMPinValue = 0;

    [Header("3. Vibration Control Outputs")]
    [Tooltip("PWM Value for a secondary control (0-255).")]
    public int VibrationPMWValueLeft = 0;

    [Tooltip("PWM Value for a third control (0-255).")]
    public int VibrationPMWValueRight = 0;

    // --- Private Fields ---
    private SerialPort serialPort;
    private Thread writeThread;
    private bool isWriting = false;
    private object _writeLock = new object();
    private string _latestMessageToSend = "";

    private const int DEFAULT_PWM_UNUSED = 0;

    void Awake()
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        StartSerialThread();
    }

    void Update()
    {
        PrepareMessageBuffer();
        lock (_writeLock)
        {
            lastSentString = _latestMessageToSend;
        }
    }

    /// <summary>
    /// Collects data from LaneKeepingAssist, updates local fields, and formats the serial command.
    /// </summary>
    private void PrepareMessageBuffer() {
        if (laneKeepingAssist != null) {
            // Read LKA values
            SteeringDirPinValue = laneKeepingAssist.motorDirection ? 1 : 0;
            SteeringENPinValue = laneKeepingAssist.isEngaged ? 1 : 0; // Enable = 1 ONLY when the LKA is actively correcting (PID output is outside dead zone)
            SteeringPWMPinValue = laneKeepingAssist.motorPWM;

            //Placeholder PWM pins for Handlebar vibration Motors
            VibrationPMWValueLeft = DEFAULT_PWM_UNUSED;
            VibrationPMWValueRight = DEFAULT_PWM_UNUSED;
        }

        // We use 'R' for Request/Control Header
        string message = string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},{3},{4}\n", // New Format: 5 comma-separated values
            SteeringDirPinValue,
            SteeringENPinValue,
            SteeringPWMPinValue,
            VibrationPMWValueLeft,
            VibrationPMWValueRight
            );

        lock (_writeLock)
        {
            _latestMessageToSend = message;
        }
    }

    /// <summary>
    /// Initializes and starts the background thread for serial communication.
    /// </summary>
    private void StartSerialThread() {
        isWriting = true;
        writeThread = new Thread(WriteData);
        writeThread.Start();
    }

    /// <summary>
    /// The main loop for the writing thread.
    /// </summary>
    private void WriteData() {
        try {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.NewLine = "\n";
            serialPort.Open();
            Debug.Log($"[Serial TX] Opened port {portName} at {baudRate}. Starting write loop.");

            uint lastTime = (uint)Environment.TickCount;

            while (isWriting)
            {
                if (serialPort.IsOpen)
                {
                    string message;
                    lock (_writeLock)
                    {
                        message = _latestMessageToSend;
                    }

                    // Use Write() as the message already contains the newline character.
                    serialPort.Write(message);

                    uint now = (uint)Environment.TickCount;
                    actualSendPeriodMs = now - lastTime;
                    lastTime = now;
                }

                Thread.Sleep(sendIntervalMs);
            }
        } catch (Exception e) {
            Debug.LogError($"[Serial TX Error] Write failed or port error: {e.Message}");
        } finally {
            if (serialPort != null && serialPort.IsOpen) {
                serialPort.Close();
                Debug.Log("[Serial TX] Port closed.");
            }
        }
    }

    // Cleanup Methods
    void OnDestroy() { StopSerialThread(); }

    void OnApplicationQuit() { StopSerialThread(); }

    private void StopSerialThread() {
        isWriting = false;
        if (writeThread != null && writeThread.IsAlive)
        {
            writeThread.Join(200);
        }
    }
}
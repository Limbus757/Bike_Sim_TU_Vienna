using UnityEngine;
using System.IO.Ports;
using System.Threading;
using System.Collections.Generic;
using System;
using System.Globalization;

/// <summary>
/// Handles one-way serial communication (Write Only) to an external device.
/// It sends 5 motor control values on a separate thread at a fixed interval (50 Hz).
/// </summary>
public class SentSerialController : MonoBehaviour {
    // =========================================================================
    // --- Public Control Data Inputs ---
    // --- These are the 5 values sent to the receiving Arduino.
    // --- OTHER SCRIPTS MUST WRITE TO THESE FIELDS to update the serial output.
    // =========================================================================

    [Header("3. Motor Control Outputs (Set by other Scripts)")]
    [Tooltip("Value for the Direction Pin (0 or 1).")]
    [Range(0, 1)]
    public int DirectionPinValue = 0; // R, Index 0 (Digital)

    [Tooltip("Value for the Enable Pin (0 or 1).")]
    [Range(0, 1)]
    public int EnablePinValue = 0;    // R, Index 1 (Digital)

    [Tooltip("PWM Value for the main speed control (0-255).")]
    [Range(0, 255)]
    public int PwmPin1Value = 0;      // R, Index 2 (Analog/PWM)

    [Tooltip("PWM Value for a secondary control (0-255).")]
    [Range(0, 255)]
    public int PwmPin2Value = 0;      // R, Index 3 (Analog/PWM)

    [Tooltip("PWM Value for a third control (0-255).")]
    [Range(0, 255)]
    public int PwmPin3Value = 0;      // R, Index 4 (Analog/PWM)


    // =========================================================================
    // --- Configuration & Debug Fields ---
    // =========================================================================

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


    // =========================================================================
    // --- Private Fields ---
    // =========================================================================
    private SerialPort serialPort;
    private Thread writeThread;
    private bool isWriting = false;
    private object _writeLock = new object();
    private string _latestMessageToSend = "";

    void Awake() {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        StartSerialThread();
    }

    void Update() {
        // 1. Read public fields and prepare the message string buffer.
        PrepareMessageBuffer();

        // 2. Update the inspector display for the user to see what is about to be sent.
        lock (_writeLock) {
            lastSentString = _latestMessageToSend;
        }

        // The WriteData thread handles the actual transmission at the timed interval.
    }

    /// <summary>
    /// Creates the comma-separated data string using current public inputs.
    /// Format: R,Dir,Enable,PWM1,PWM2,PWM3
    /// </summary>
    private void PrepareMessageBuffer() {
        string message = string.Format(
            CultureInfo.InvariantCulture,
            "R,{0},{1},{2},{3},{4}",
            DirectionPinValue,
            EnablePinValue,
            PwmPin1Value,
            PwmPin2Value,
            PwmPin3Value
        );

        lock (_writeLock) {
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
            serialPort.Open();
            Debug.Log($"[Serial TX] Opened port {portName} at {baudRate}. Starting write loop.");

            uint lastTime = (uint)Environment.TickCount;

            while (isWriting) {
                if (serialPort.IsOpen) {
                    string message;
                    lock (_writeLock) {
                        // Safely retrieve the latest prepared message from the main thread
                        message = _latestMessageToSend;
                    }

                    serialPort.WriteLine(message);

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

    // --- Cleanup Methods ---
    void OnDestroy() {
        StopSerialThread();
    }

    void OnApplicationQuit() {
        StopSerialThread();
    }

    private void StopSerialThread() {
        isWriting = false;
        if (writeThread != null && writeThread.IsAlive) {
            writeThread.Join(200);
        }
    }
}
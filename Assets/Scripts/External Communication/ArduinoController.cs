using UnityEngine;
using System.IO.Ports;
using System.Threading;
using System.Collections.Generic;
using System;

/// <summary>
/// A data structure to hold the parsed bike data.
/// This makes the data easier to access and use throughout your project.
/// </summary>
public struct BikeData {
    public float Speed;
    public float SteeringAngle;
    public float FrontBrakeForce;
    public float BackBrakeForce;
    public float Resistance;
    public float Pitch;
    public float Roll;
}

/// <summary>
/// Handles two-way serial communication with an external device (e.g., Arduino).
/// It uses a separate thread for reading data to prevent the main Unity thread from blocking.
/// </summary>
public class ArduinoSerialManager : MonoBehaviour {
    #region Public Fields
    // Use the Unity Inspector to set the correct port and baud rate.
    [Tooltip("The name of the serial port (e.g., COM3 on Windows, /dev/ttyACM0 on Linux).")]
    public string portName = "COM3";

    [Tooltip("The communication speed in bits per second.")]
    public int baudRate = 115200;
    #endregion

    #region Private Fields
    private SerialPort serialPort;
    private Thread readThread;
    private bool isReading = false;
    private Queue<string> _receivedDataQueue = new Queue<string>();
    private object _queueLock = new object();
    #endregion

    #region Events
    /// <summary>
    /// Event that fires when a complete line of data is received and parsed.
    /// It now passes a structured BikeData object.
    /// </summary>
    public event Action<BikeData> OnDataReceived;
    #endregion

    void Awake() {
        // Start the serial communication thread when the script awakes.
        StartSerialThread();
    }

    void Update() {
        // Check for new data in the queue on the main Unity thread.
        // This is a safe way to handle data received from a background thread.
        ProcessReceivedData();
        // Write();
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
    /// The main loop for the reading thread. It continuously tries to read from the serial port.
    /// </summary>
    private void ReadData() {
        try {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.Open();

            while (isReading) {
                if (serialPort.IsOpen && serialPort.BytesToRead > 0) {
                    // Read a line from the serial port and enqueue it.
                    string data = serialPort.ReadLine();
                    lock (_queueLock) {
                        _receivedDataQueue.Enqueue(data);
                    }
                }
                // Yield to prevent the thread from consuming too much CPU.
                Thread.Sleep(1);
            }
        } catch (Exception e) {
            Debug.LogError("Error in serial communication: " + e.Message);
        } finally {
            // Clean up the serial port when the thread is done.
            if (serialPort != null && serialPort.IsOpen) {
                serialPort.Close();
            }
        }
    }

    /// <summary>
    /// Processes data from the queue on the main Unity thread.
    /// It now parses the string into a structured BikeData object.
    /// </summary>
    private void ProcessReceivedData() {
        lock (_queueLock) {
            while (_receivedDataQueue.Count > 0) {
                string data = _receivedDataQueue.Dequeue();

                // Parse the received string.
                string[] values = data.Split(';');
                if (values.Length >= 6) // Assuming at least Speed, Steering, Pitch, Roll, FrontBrake, BackBrake.
                {
                    BikeData bikeData = new BikeData();

                    // Use TryParse for safe conversion without throwing exceptions.
                    float.TryParse(values[0], out bikeData.Speed);
                    float.TryParse(values[1], out bikeData.SteeringAngle);
                    float.TryParse(values[2], out bikeData.FrontBrakeForce);
                    float.TryParse(values[3], out bikeData.BackBrakeForce);
                    float.TryParse(values[4], out bikeData.Resistance);
                    float.TryParse(values[5], out bikeData.Pitch);
                    float.TryParse(values[6], out bikeData.Roll);

                    // Invoke the event with the structured data.
                    if (OnDataReceived != null) {
                        OnDataReceived.Invoke(bikeData);
                    }
                } else {
                    Debug.LogWarning("Received incomplete data string: " + data);
                }
            }
        }
    }

    /// <summary>
    /// Public method to send a string message to the serial device.
    /// This can be called from any other script.
    /// </summary>
    /// <param name="message">The string to send to the serial device.</param>
    public void Write(string message) {
        if (serialPort != null && serialPort.IsOpen) {
            try {
                serialPort.WriteLine(message);
            } catch (Exception e) {
                Debug.LogError("Error writing to serial port: " + e.Message);
            }
        }
    }


    void OnDestroy() {
        isReading = false;
        if (readThread != null && readThread.IsAlive) {
            readThread.Join();
        }
    }

    void OnApplicationQuit() {
        isReading = false;
        if (readThread != null && readThread.IsAlive) {
            readThread.Join();
        }
    }
}

using UnityEngine;
using UnityEngine.UI;
using System.IO.Ports;
using System;
using System.Linq;

public class SimpleSerialReader : MonoBehaviour {

    // Public variables for the Unity Inspector
    [Header("Serial Port Settings")]
    [Tooltip("The COM port to connect to. You must set this in the Inspector.")]
    public string comPort;

    [Tooltip("The baud rate must match the Serial.begin() in your Arduino sketch.")]
    public int baudRate = 9600;

    [Tooltip("The UI Text component to display the data.")]
    public Text dataText;

    private SerialPort stream;
    private bool isConnected = false;

    // Called when the script is first initialized
    void Start() {
        // Log all available ports to the console for debugging
        string[] availablePorts = SerialPort.GetPortNames();
        Debug.Log("Available COM ports: " + string.Join(", ", availablePorts));

        if (availablePorts.Length == 0) {
            Debug.LogError("No serial ports found. Make sure your device is connected and drivers are installed.");
            return;
        }

        // Try to initialize the connection
        InitializeSerialConnection();
    }

    // This method is called once per frame
    void FixedUpdate() {
        if (isConnected && stream.IsOpen) {
            try {
                // Check if there is any data to be read
                if (stream.BytesToRead > 0) {
                    string data = stream.ReadLine();

                    if (!string.IsNullOrEmpty(data)) {
                        Debug.Log("Raw data received: " + data);

                        float value;
                        if (float.TryParse(data.Trim(), out value)) {
                            // Only log the successfully parsed value
                            if (dataText != null) {
                                dataText.text = "Speed: " + value.ToString("F2");
                            }
                        } else {
                            Debug.LogError("Failed to parse data to float: " + data);
                        }
                    }
                }
            } catch (TimeoutException) {
                // This is expected and normal if there's a pause in data
            } catch (Exception ex) {
                Debug.LogError("Error reading from serial port: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Opens the serial port connection.
    /// </summary>
    private void InitializeSerialConnection() {
        if (isConnected) {
            Debug.LogWarning("Serial connection is already active.");
            return;
        }

        try {
            // Check if the specified COM port exists
            if (!SerialPort.GetPortNames().Contains(comPort)) {
                Debug.LogError("Specified COM port '" + comPort + "' does not exist. Please choose from the list above.");
                return;
            }

            stream = new SerialPort(comPort, baudRate);
            stream.DtrEnable = true;
            stream.ReadTimeout = 50; // Set a small timeout to prevent the game from freezing
            stream.Open();

            isConnected = true;
            Debug.Log("Serial connection established on port " + comPort + " at baud rate " + baudRate);
        } catch (Exception ex) {
            Debug.LogError("Failed to open serial port " + comPort + ": " + ex.Message);
            isConnected = false;
        }
    }

    /// <summary>
    /// Closes the serial port connection when the application is closing.
    /// </summary>
    void OnApplicationQuit() {
        if (stream != null && stream.IsOpen) {
            stream.Close();
            Debug.Log("Serial connection closed.");
        }
    }
}

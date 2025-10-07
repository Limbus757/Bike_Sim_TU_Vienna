using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Text;

/// <summary>
/// Logs relevant variables from BikeController and LaneKeepingAssist every FixedUpdate frame.
/// This version streams data directly to a CSV file to minimize memory usage for long runs.
/// Logging is controlled by explicit StartLogger() and StopLogger() calls.
/// </summary>
public class DataLogger : MonoBehaviour {
    // References to the controller scripts
    private BikeController bikeController;
    private LaneKeepingAssist lkaController;
    private GameController gameController;

    private string filePath;
    private bool isLogging = false;
    string header = "Time,BikeSpeed,SteeringAngle,FrontBrakeforce,BackBrakeforce,LkaDeviation,LkaHeadingError,LkaCurrentError";

    // Line to mark the end of a log session
    private const string END_OF_LOG_MARKER = "--- END OF LOG ---";

    void Awake() {
        // Get references to required scripts on this or other GameObjects.
        // This is done in Awake() because finding references is fast and should happen early.
        bikeController = GetComponent<BikeController>();
        lkaController = GetComponent<LaneKeepingAssist>();
        gameController = FindObjectOfType<GameController>();

        if (bikeController == null) Debug.LogError("DataLogger: BikeController not found on this GameObject.");
        if (lkaController == null) Debug.LogError("DataLogger: LaneKeepingAssist not found on this GameObject.");
        if (gameController == null) Debug.LogError("DataLogger: GameController not found in the scene.");
    }

    void Start() {
        // Prepare the file path here, in Start(), after all components have finished their Awake()
        string participantIDString = (gameController != null)
                               ? gameController.studyParticipantId.ToString() : "UNKNOWN_PARTICIPANT";

        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filename = $"{participantIDString}_Log_{timestamp}.csv";
        filePath = Path.Combine(Application.persistentDataPath, filename);
    }

    void FixedUpdate() {
        if (isLogging) {
            LogCurrentData();
        }
    }

    /// <summary>
    /// Initializes the CSV file with the metadata line and header, and starts the logging process.
    /// This should be called to begin data collection for a trial.
    /// </summary>
    public void StartLogger() {
        if (isLogging) {
            Debug.LogWarning("DataLogger: Logger is already running.");
            return;
        }

        string participantIDString = (gameController != null) ? gameController.studyParticipantId.ToString() : "UNKNOWN_PARTICIPANT";
        string metadata = $"{gameController.studyName},{participantIDString}\n";

        try {
            // 1. Create the file and write the METADATA line first
            File.WriteAllText(filePath, metadata);

            // 2. Append the HEADER row
            File.AppendAllText(filePath, header + "\n");

            isLogging = true;
            Debug.Log($"DataLogger: Logging started. New file created at: {filePath}");
        } catch (System.Exception e) {
            Debug.LogError($"DataLogger: Failed to start logger/write header. Error: {e.Message}");
            isLogging = false;
        }
    }

    /// <summary>
    /// Stops the logging process and appends a final marker line to the CSV file.
    /// This should be called at the end of a trial.
    /// </summary>
    public void StopLogger() {
        if (!isLogging) {
            Debug.LogWarning("DataLogger: Logger is not running.");
            return;
        }

        isLogging = false;

        try {
            File.AppendAllText(filePath, END_OF_LOG_MARKER + "\n");
            Debug.Log($"DataLogger: Logging stopped. End marker appended to: {filePath}");
        } catch (System.Exception e) {
            Debug.LogError($"DataLogger: Failed to append stop marker. Error: {e.Message}");
        }
    }

    /// <summary>
    /// Collects all variables and appends a single line of data to the CSV file.
    /// </summary>
    private void LogCurrentData() {
        // Check for null references and output an error if any script is missing.
        if (bikeController == null || lkaController == null || gameController == null) {
            string missing = "";
            if (bikeController == null) missing += "BikeController, ";
            if (lkaController == null) missing += "LaneKeepingAssist, ";
            if (gameController == null) missing += "GameController, ";

            missing = missing.TrimEnd(',', ' ');

            Debug.LogError($"DataLogger: LogCurrentData aborted. The following script references are missing: {missing}.");
            return;
        }

        StringBuilder line = new StringBuilder();
        line.Append(Time.time.ToString("F4")).Append(",");

        // BikeController Variables
        line.Append(bikeController.BikeSpeed.ToString("F4")).Append(",");
        line.Append(bikeController.SteeringAngle.ToString("F4")).Append(",");
        line.Append(bikeController.FrontBrakeforce.ToString("F4")).Append(",");
        line.Append(bikeController.BackBrakeforce.ToString("F4")).Append(",");

        // LaneKeepingAssist Variables
        line.Append(lkaController.Deviation.ToString("F4")).Append(",");
        line.Append(lkaController.HeadingError.ToString("F4")).Append(",");
        // The last field (LkaCurrentError) must not have a trailing comma
        line.Append(lkaController.CurrentError.ToString("F4"));

        try {
            // Append the new line to the file immediately
            File.AppendAllText(filePath, line.ToString() + "\n");
        } catch (System.Exception e) {
            // If logging fails mid-run, stop logging to prevent continuous error spam
            Debug.LogError($"DataLogger: Failed to append data to log file. Stopping logger. Error: {e.Message}");
            isLogging = false;
        }
    }

    /// <summary>
    /// Ensure logging stops if the application is quit without calling StopLogger() first.
    /// </summary>
    void OnApplicationQuit() {
        if (isLogging) {
            StopLogger();
        }
    }
}

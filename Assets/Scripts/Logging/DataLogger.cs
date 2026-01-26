using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Data Logger for research studies.
/// Uses a background thread to prevent disk I/O from causing frame drops.
/// Automatically generates CSV headers from the internal LogData struct.
/// </summary>
public class DataLogger : MonoBehaviour {
    [Header("Source Controllers")]
    public BikeController bike;
    public LaneKeepingAssistController lka;
    public GameController game;
    public MLClosedSplineFrenet frenet;
    public LKAConfiguration lkaConfig;
    public ML_LaneHapticsFromPercent haptics;
    public SecondaryTask secondaryTask;

    /// <summary>
    /// Defines the CSV columns and their order.
    /// </summary>
    private struct LogData {
        public float Timestamp;
        public float Trackposition;
        public float Speed_MS;
        public float SteerAngle;
        public float CTE_Norm;
        public float Haptic_L_Norm;
        public float Haptic_R_Norm;
        public float LkaPIDError;
        public float CTE_Meters;
        public float B_HeadingError_Deg;
        public float W_HeadingError_Deg;
        public int IsOnStraight;
        public float CurrentTrackCurvature;
        public float CurrentTrackRadius;
        public int LKA_Engaged;
        public int LKA_Switch;
        public int MotorDir;
        public int SecTaskNum;
        public int SecTaskPressed;
        public int MotorPWM;
        public int HapticLeftPWM;
        public int HapticRightPWM;
    }

    private static readonly string[] CsvHeaders =
    {
    "Timestamp",
    "Trackposition",
    "Speed_MS",
    "SteerAngle",
    "CTE_Norm",
    "Haptic_L_Norm",
    "Haptic_R_Norm",
    "LkaPIDError",
    "CTE_Meters",
    "B_HeadingError_Deg",
    "W_HeadingError_Deg",
    "IsOnStraight",
    "CurrentTrackCurvature",
    "CurrentTrackRadius",
    "LKA_Engaged",
    "LKA_Switch",
    "MotorDir",
    "SecTaskNum",
    "SecTaskPressed",
    "MotorPWM",
    "HapticLeftPWM",
    "HapticRightPWM"
    };

    private bool isLogging = false;
    private StreamWriter sw;
    private ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();

    void Awake() {
        bike = FindObjectOfType<BikeController>();
        lka = FindObjectOfType<LaneKeepingAssistController>();
        frenet = FindObjectOfType<MLClosedSplineFrenet>();
        game = FindObjectOfType<GameController>();
        haptics = FindObjectOfType<ML_LaneHapticsFromPercent>();
        lkaConfig = FindObjectOfType<LKAConfiguration>();
        secondaryTask = FindAnyObjectByType<SecondaryTask>();
    }

    void Start() {
        // Validate dependencies once to keep the logging loop clean
        if (!CheckDependencies()) {
            Debug.LogError("<color=red><b>Logger:</b> Missing dependencies. Script disabled.</color>");
            this.enabled = false;
            return;
        }

        PrepareDirectoryAndFile();
    }

    /// <summary>
    /// Verifies all required controllers are present in the scene.
    /// </summary>
    private bool CheckDependencies() {
        bool valid = true;
        if (bike == null) { Debug.LogWarning("Logger: BikeController missing"); valid = false; }
        if (lka == null) { Debug.LogWarning("Logger: LKAController missing"); valid = false; }
        if (frenet == null) { Debug.LogWarning("Logger: Frenet script missing"); valid = false; }
        if (game == null) { Debug.LogWarning("Logger: GameController missing"); valid = false; }
        if (lkaConfig == null) { Debug.LogWarning("Logger: LKAConfig missing"); valid = false; }
        if (haptics == null) { Debug.LogWarning("Logger: Haptics missing"); valid = false; }
        if (secondaryTask == null) { Debug.LogWarning("Logger: SecondaryTask missing"); valid = false; }
        return valid;
    }

    private void PrepareDirectoryAndFile() {
        try {
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string studyName = game.studyName;
            string folderPath = Path.Combine(documentsPath, "StudyData_BikeSim", studyName);

            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

            string pId = game.studyParticipantId.ToString();
            string cond = game.currentCondition.ToString();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filePath = Path.Combine(folderPath, $"{pId}_{cond}_{timestamp}.csv");

            sw = new StreamWriter(filePath, false) { AutoFlush = false };
            sw.WriteLine($"# Study: {studyName} | Participant: {pId} | Condition: {cond} | Init: {timestamp}");
            sw.WriteLine(string.Join(",", CsvHeaders));

            Debug.Log($"<color=cyan><b>Logger:</b> File created at {filePath}</color>");
        } catch (Exception e) {
            Debug.LogError("Logger failed to create file: " + e.Message);
        }
    }

    public void StartLogger() {
        if (isLogging || sw == null) return;
        isLogging = true;
        Task.Run(ProcessQueue);
        Debug.Log("<color=green><b>Logging Started</b></color>");
    }

    void FixedUpdate() {
        if (isLogging) {
            Debug.Log($"Haptic L PWM: {haptics.pwmLeft}, R PWM: {haptics.pwmRight}, L Norm: {haptics.normalizedLeftVibration}");
            LogCurrentData();
        }
    }

    private void LogCurrentData() {
        if (sw == null) return;

        LogData d = CaptureFrameData();

        string line =
            d.Timestamp.ToString("F4", CultureInfo.InvariantCulture) + "," +
            d.Trackposition.ToString("F4", CultureInfo.InvariantCulture) + "," +
            d.Speed_MS.ToString("F4", CultureInfo.InvariantCulture) + "," +
            d.SteerAngle.ToString("F4", CultureInfo.InvariantCulture) + "," +
            d.CTE_Norm.ToString("F4", CultureInfo.InvariantCulture) + "," +
            d.Haptic_L_Norm.ToString("F4", CultureInfo.InvariantCulture) + "," +
            d.Haptic_R_Norm.ToString("F4", CultureInfo.InvariantCulture) + "," +
            d.LkaPIDError.ToString("F4", CultureInfo.InvariantCulture) + "," +
            d.CTE_Meters.ToString("F4", CultureInfo.InvariantCulture) + "," +
            d.B_HeadingError_Deg.ToString("F4", CultureInfo.InvariantCulture) + "," +
            d.W_HeadingError_Deg.ToString("F4", CultureInfo.InvariantCulture) + "," +
            d.IsOnStraight + "," +
            d.CurrentTrackCurvature.ToString("F6", CultureInfo.InvariantCulture) + "," +
            d.CurrentTrackRadius.ToString("F4", CultureInfo.InvariantCulture) + "," +
            d.LKA_Engaged + "," +
            d.LKA_Switch + "," +
            d.MotorDir + "," +
            d.SecTaskNum + "," +
            d.SecTaskPressed + "," +
            d.MotorPWM + "," +
            d.HapticLeftPWM + "," +
            d.HapticRightPWM;

        _logQueue.Enqueue(line);
    }

    /// <summary>
    /// Maps controller values directly to the struct.
    /// Validation is handled in Start() to ensure these references are safe.
    /// </summary>
    private LogData CaptureFrameData() {
        return new LogData {
            Timestamp = Time.time,
            Trackposition = frenet.currentArcLengthS,
            Speed_MS = bike.BikeSpeedMS,
            SteerAngle = bike.SteeringAngle,
            CTE_Norm = lkaConfig.crossTrackErrorNormalized,
            Haptic_L_Norm = haptics.normalizedLeftVibration,
            Haptic_R_Norm = haptics.normalizedRightVibration,
            LkaPIDError = lka.CurrentError,
            CTE_Meters = frenet.crossTrackErrorMeters,
            B_HeadingError_Deg = frenet.bikeHeadingErrorDegrees,
            W_HeadingError_Deg = frenet.wheelHeadingErrorDegrees,
            IsOnStraight = frenet.isOnStraightTrack ? 1 : 0,
            CurrentTrackCurvature = frenet.currentCurvature,
            CurrentTrackRadius = frenet.currentRadius,
            LKA_Engaged = lka.isEngaged ? 1 : 0,
            LKA_Switch = lka.lkaSwitchActive ? 1 : 0,
            MotorDir = lka.SteeringMotorDirection ? 1 : 0,
            SecTaskNum = secondaryTask.currentNumber,
            SecTaskPressed = secondaryTask.buttonPressed ? 1 : 0,
            MotorPWM = lka.SteeringMotorPWM,
            HapticLeftPWM = haptics.pwmLeft,
            HapticRightPWM = haptics.pwmRight
        };
    }

    /// <summary>
    /// Background thread to process and write the log queue.
    /// </summary>
    private async Task ProcessQueue() {
        while (isLogging || !_logQueue.IsEmpty) {
            if (_logQueue.TryDequeue(out string line)) {
                if (sw != null) await sw.WriteLineAsync(line);
            } else {
                if (sw != null) await sw.FlushAsync();
                await Task.Delay(10);
            }
        }
        FinalizeFile();
    }

    public void StopLogger() {
        if (!isLogging) return;
        isLogging = false;
        Debug.Log("<color=yellow>Logging Stop requested. Draining queue...</color>");
    }

    private void FinalizeFile() {
        if (sw != null) {
            sw.WriteLine("--- END OF LOG ---");
            sw.Close();
            sw = null;
            Debug.Log("<color=orange><b>Logger:</b> File closed safely.</color>");
        }
    }

    private void OnApplicationQuit() {
        StopLogger();
    }
}
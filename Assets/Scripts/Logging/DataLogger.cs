using System;
using UnityEngine;
using System.IO;
using System.Text;

public class DataLogger : MonoBehaviour {
    private BikeController bikeController;
    private LaneKeepingAssistController lkaController;
    private GameController gameController;
    private MLClosedSplineFrenet frenetController;
    private LKAConfiguration lkaConfig;
    private ML_LaneHapticsFromPercent hapticsController;
    private SecondaryTask SecondaryTask;

    private string filePath;
    private bool isLogging = false;
    private StreamWriter sw;

    // Ordered: Telemetry -> ALL Normalized/Study Metrics -> Status -> Raw Hardware
    string header = "Time,LapNr,BikeSpeed,SteerAngle," +
                    "CTE_Norm,Haptic_L_Norm,Haptic_R_Norm,LkaPIDError," +
                    "CTE_Meters,HeadingError_Degrees,IsOnStraight,Curvature,LkaEngaged,LkaSwitchState,MotorDir,SecondaryTaskNumber,SecondaryTaskButtonPressed," +
                    "MotorPWM,HapticLeftPWM,HapticRightPWM";

    void Awake() {
        bikeController = FindObjectOfType<BikeController>();
        lkaController = FindObjectOfType<LaneKeepingAssistController>();
        frenetController = FindObjectOfType<MLClosedSplineFrenet>();
        gameController = FindObjectOfType<GameController>();
        hapticsController = FindObjectOfType<ML_LaneHapticsFromPercent>();
        lkaConfig = FindObjectOfType<LKAConfiguration>();
        SecondaryTask = FindAnyObjectByType<SecondaryTask>();
    }

    void Start() {
        // prepare the directory
        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string studyName = (gameController != null) ? gameController.studyName : "DefaultStudy";
        string folderPath = Path.Combine(documentsPath, "StudyData", studyName);

        if (!Directory.Exists(folderPath)) {
            Directory.CreateDirectory(folderPath);
        }

        // define the filename
        string pId = (gameController != null) ? gameController.studyParticipantId.ToString() : "0";
        string cond = (gameController != null) ? gameController.currentCondition.ToString() : "Test";
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        filePath = Path.Combine(folderPath, $"{pId}_{cond}_{timestamp}.csv");

        // create the file physically on the PC
        try {
            sw = new StreamWriter(filePath, false);
            sw.AutoFlush = true; // Ensures every write hits the disk immediately
            sw.WriteLine($"# Study: {studyName} | Participant: {pId} | Condition: {cond} | InitTime: {timestamp}");
            sw.WriteLine(header);
            Debug.Log($"<color=cyan><b>Logger:</b> File created and waiting for trigger at {filePath}</color>");
        } catch (Exception e) {
            Debug.LogError("Logger failed to create file in Start: " + e.Message);
        }
    }

    public void StartLogger() {
        if (isLogging) return;
        isLogging = true;
        Debug.Log("<color=green><b>Logging Started</b></color>");
    }

    void FixedUpdate() {
        if (isLogging) LogCurrentData();
    }

    void LogCurrentData() {
        if (bikeController == null || sw == null) return;

        StringBuilder line = new StringBuilder();

        // telemetry
        line.Append(Time.time.ToString("F3")).Append(",");
        line.Append(gameController != null ? gameController.currentLap : 0).Append(",");
        line.Append(bikeController.BikeSpeed.ToString("F2")).Append(",");
        line.Append(bikeController.SteeringAngle.ToString("F2")).Append(",");

        // normalized data grouped together for easy plotting/correlation
        line.Append(lkaConfig != null ? lkaConfig.crossTrackErrorNormalized.ToString("F4") : "0").Append(",");
        line.Append(hapticsController != null ? hapticsController.normalizedLeftVibration.ToString("F4") : "0").Append(",");
        line.Append(hapticsController != null ? hapticsController.normalizedRightVibration.ToString("F4") : "0").Append(",");
        line.Append(lkaController != null ? lkaController.CurrentError.ToString("F4") : "0").Append(",");

        // enviroment & system status
        line.Append(frenetController != null ? frenetController.crossTrackErrorMeters.ToString("F4") : "0").Append(",");
        line.Append(frenetController != null ? frenetController.wheelHeadingErrorDegrees.ToString("F4") : "0").Append(",");
        line.Append(frenetController != null ? (frenetController.isOnStraightTrack ? "1" : "0") : "0").Append(",");
        line.Append(frenetController != null ? frenetController.currentCurvature.ToString("F6") : "0").Append(",");
        line.Append(lkaController.isEngaged ? "1" : "0").Append(",");
        line.Append(lkaController.lkaSwitchActive ? "1" : "0").Append(",");
        line.Append(lkaController.SteeringMotorDirection ? "1" : "0").Append(",");
        line.Append(SecondaryTask.currentNumber).Append(",");
        line.Append(SecondaryTask.buttonPressed ? "1" : "0").Append(",");

        // hardware outputs
        line.Append(lkaController.SteeringMotorPWM).Append(",");
        line.Append(hapticsController != null ? hapticsController.pwmLeft : 0).Append(",");
        line.Append(hapticsController != null ? hapticsController.pwmRight : 0);

        sw.WriteLine(line.ToString());
    }

    public void StopLogger() {
        if (!isLogging || sw == null) return;
        isLogging = false;
        try {
            sw.WriteLine("--- END OF LOG ---");
            sw.Flush();
            sw.Close();
            sw.Dispose();
            sw = null;
            Debug.Log("<color=yellow>Logging Stopped.</color>");
        } catch (Exception e) {
            Debug.LogError("Error closing stream: " + e.Message);
        }
    }

    private void OnApplicationQuit() { StopLogger(); }
}
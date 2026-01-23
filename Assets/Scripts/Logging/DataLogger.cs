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

    private string filePath;
    private bool isLogging = false;
    private StreamWriter sw;

    // Ordered: Telemetry -> ALL Normalized/Study Metrics -> Status -> Raw Hardware
    string header = "Time,LapNr,BikeSpeed,SteerAngle," +
                    "CTE_Norm,Haptic_L_Norm,Haptic_R_Norm,LkaPIDError," +
                    "CTE_Meters,HeadingError_Degrees,IsOnStraight,Curvature,LkaEngaged,LkaSwitchState,MotorDir," +
                    "MotorPWM,HapticLeftPWM,HapticRightPWM";

    void Awake() {
        bikeController = FindObjectOfType<BikeController>();
        lkaController = FindObjectOfType<LaneKeepingAssistController>();
        frenetController = FindObjectOfType<MLClosedSplineFrenet>();
        gameController = FindObjectOfType<GameController>();
        hapticsController = FindObjectOfType<ML_LaneHapticsFromPercent>();
        lkaConfig = FindObjectOfType<LKAConfiguration>();
    }

    public void StartLogger() {
        if (isLogging) return;

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
        string pId = gameController != null ? gameController.studyParticipantId.ToString() : "0";
        string cond = gameController != null ? gameController.currentCondition.ToString() : "Unknown";
        float trackWidth = lkaConfig != null ? lkaConfig.trackWidthMeters : 0f;

        filePath = Path.Combine(Application.persistentDataPath, $"Study_{pId}_{cond}_{timestamp}.csv");

        //Metadata - Includes all constants so they don't repeat in data rows
        string metadata = $"Participant:{pId},Condition:{cond},TrackWidth:{trackWidth}m,Date:{timestamp}";

        try {
            sw = new StreamWriter(filePath, false);
            sw.WriteLine(metadata); // Row 1: Constants
            sw.WriteLine(header);   // Row 2: Labels
            isLogging = true;
            Debug.Log($"<color=green>Logging Started: {filePath}</color>");
        } catch (Exception e) {
            Debug.LogError("Logger failed to start: " + e.Message);
        }
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

        //
        // normalized data grouped together for easy plotting/correlation
        line.Append(lkaConfig != null ? lkaConfig.crossTrackErrorNormalized.ToString("F4") : "0").Append(",");
        line.Append(hapticsController != null ? hapticsController.pwmLeft.ToString("F4") : "0").Append(",");
        line.Append(hapticsController != null ? hapticsController.pwmRight.ToString("F4") : "0").Append(",");
        line.Append(lkaController != null ? lkaController.CurrentError.ToString("F4") : "0").Append(",");

        // enviroment & system status
        line.Append(frenetController != null ? frenetController.crossTrackErrorMeters.ToString("F4") : "0").Append(",");
        line.Append(frenetController != null ? frenetController.headingErrorDegrees.ToString("F4") : "0").Append(",");
        line.Append(frenetController != null ? (frenetController.isOnStraightTrack ? "1" : "0") : "0").Append(",");
        line.Append(frenetController != null ? frenetController.currentCurvature.ToString("F6") : "0").Append(",");
        line.Append(lkaController.isEngaged ? "1" : "0").Append(",");
        line.Append(lkaController.lkaSwitchActive ? "1" : "0").Append(",");
        line.Append(lkaController.motorDirection ? "1" : "0").Append(",");

        // hardware outputs
        line.Append(lkaController.motorPWM).Append(",");
        line.Append(hapticsController != null ? hapticsController.pwmLeft255 : 0).Append(",");
        line.Append(hapticsController != null ? hapticsController.pwmRight255 : 0);

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
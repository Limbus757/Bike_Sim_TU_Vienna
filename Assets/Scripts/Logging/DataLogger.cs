using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Text;

public class DataLogger : MonoBehaviour
{
    private BikeController bikeController;
    private LaneKeepingAssistController lkaController;
    private GameController gameController;
    private MLClosedSplineFrenet frenetController;
    private LKAConfiguration lkaConfig;
    private ML_LaneHapticsFromPercent hapticsController;

    private string filePath;
    private bool isLogging = false;
    private StreamWriter sw;

    string header = "Time,Lap,s_Pos,BikeSpeed,SteerAngle,FrontBrake,BackBrake," + // telemetry
                    "IsOnStraight,Curvature,CrossTrackError,HeadingError," + // environment
                    "LkaSwitchState,LkaEngaged,LkaNorm,MotorDir,MotorPWM,TotalPIDError," + // lka system
                    "HapticLeftNorm,HapticRightNorm,HapticLeftPWM,HapticRightPWM"; // haptic system
                   
    void Awake() {
        bikeController = FindObjectOfType<BikeController>();
        lkaController = FindObjectOfType<LaneKeepingAssistController>();
        frenetController = FindObjectOfType<MLClosedSplineFrenet>();
        gameController = FindObjectOfType<GameController>();
        hapticsController = FindObjectOfType<ML_LaneHapticsFromPercent>();
        lkaConfig = FindObjectOfType<LKAConfiguration>();

    }

    void Start() {
        // get the path to 'C:\Users\Name\Documents'
        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        // define the StudyData folder inside Documents
        string studyName = (gameController != null) ? gameController.studyName : "DefaultStudy";
        string studyFolderPath = Path.Combine(documentsPath, "StudyData", studyName);

        // create the directory if it doesn't exist
        if (!Directory.Exists(studyFolderPath))
        {
            Directory.CreateDirectory(studyFolderPath);
        }

        // setup filename
        string participantID = (gameController != null) ? gameController.studyParticipantId.ToString() : "0";
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"{participantID}_Log_{timestamp}.csv";

        filePath = Path.Combine(studyFolderPath, fileName);

        Debug.Log($"Logger path set to: {filePath}");
    }

    public void StartLogger() {
        if (isLogging) return;
        try {
            // open the file stream (append: false to create fresh file)
            sw = new StreamWriter(filePath, false);

            string metadata = $"{(gameController != null ? gameController.studyName : "Study")},{(gameController != null ? gameController.studyParticipantId.ToString() : "0")}";
            sw.WriteLine(metadata);
            sw.WriteLine(header);
            isLogging = true;
            Debug.Log("<color=green>Logging Started Successfully!</color>");
        }
        catch (Exception e)
        {
            Debug.LogError("Logger Start Error: " + e.Message);
        }
    }

    void FixedUpdate()
    {
        if (isLogging) LogCurrentData();
    }

    private void LogCurrentData() {
        if (bikeController == null || lkaController == null || sw == null) return;

        StringBuilder line = new StringBuilder();

        // --- telemetry ---
        line.Append(Time.time.ToString("F4")).Append(","); // time
        line.Append(gameController != null ? gameController.currentLap : 0).Append(","); // lap
        line.Append(frenetController != null ? frenetController.currentDistanceOnTrack.ToString("F3") : "0").Append(","); // s_pos
        line.Append(bikeController.BikeSpeed.ToString("F4")).Append(","); // speed
        line.Append(bikeController.SteeringAngle.ToString("F4")).Append(","); // steer angle
        line.Append(bikeController.FrontBrakeforce.ToString("F4")).Append(","); // front brake
        line.Append(bikeController.BackBrakeforce.ToString("F4")).Append(","); // back brake

        // --- environment and raw errors ---
        line.Append(frenetController != null ? (frenetController.isOnStraightTrack ? "1" : "0") : "0").Append(","); // straight check
        line.Append(frenetController != null ? frenetController.currentCurvature.ToString("F6") : "0").Append(","); // curvature
        line.Append(frenetController != null ? frenetController.crossTrackErrorMeters.ToString("F4") : "0").Append(","); // cte meters
        line.Append(frenetController != null ? frenetController.headingErrorDegrees.ToString("F4") : "0").Append(","); // heading error

        // --- lka system ---
        line.Append(lkaController.lkaSwitchActive ? "1" : "0").Append(","); // switch state
        line.Append(lkaController.isEngaged ? "1" : "0").Append(","); // engaged
        line.Append(lkaConfig != null ? lkaConfig.lkaCrossTrackErrorNormalized.ToString("F4") : "0").Append(","); // lka norm
        line.Append(lkaController.motorDirection ? "1" : "0").Append(","); // motor dir
        line.Append(lkaController.motorPWM).Append(","); // motor pwm
        line.Append(lkaController.CurrentError.ToString("F4")).Append(","); // total pid error

        // --- haptic system ---
        line.Append(hapticsController != null ? hapticsController.pwmLeft.ToString("F4") : "0").Append(","); // haptic left 0-1
        line.Append(hapticsController != null ? hapticsController.pwmRight.ToString("F4") : "0").Append(","); // haptic right 0-1
        line.Append(hapticsController != null ? hapticsController.pwmLeft255 : 0).Append(","); // haptic left pwm
        line.Append(hapticsController != null ? hapticsController.pwmRight255 : 0).Append(","); // haptic right pwm

        sw.WriteLine(line.ToString());
    }

    public void StopLogger()
    {
        if (!isLogging || sw == null) return;

        isLogging = false;
        try
        {
            sw.WriteLine("--- END OF LOG ---");
            sw.Flush();
            sw.Close();
            sw.Dispose();
            sw = null;
            Debug.Log("<color=yellow>Logging Stopped. File Saved.</color>");
        }
        catch (Exception e)
        {
            Debug.LogError("Logger Stop Error: " + e.Message);
        }
    }

    void OnApplicationQuit()
    {
        StopLogger();
    }
}
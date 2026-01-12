using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Text;

public class DataLogger : MonoBehaviour
{
    private BikeController bikeController;
    private LaneKeepingAssist lkaController;
    private GameController gameController;
    private MLClosedSplineFrenet frenetController;

    private string filePath;
    private bool isLogging = false;

    // Header updated to track the switch and engagement separately
    string header = "Time,BikeSpeed,SteerAngle,FrontBrake,BackBrake," +
                    "LkaSwitchState,LkaEngaged,MotorDir,MotorPWM," +
                    "IsOnStraight,Curvature,CrossTrackError,HeadingError";

    private const string END_OF_LOG_MARKER = "--- END OF LOG ---";

    void Awake()
    {
        bikeController = GetComponent<BikeController>();
        lkaController = GetComponent<LaneKeepingAssist>();
        frenetController = GetComponent<MLClosedSplineFrenet>();
        gameController = FindObjectOfType<GameController>();
    }

    void Start() {
        string basePath = Application.dataPath;
        
        string studyName = (gameController != null) ? gameController.studyName : "DefaultStudy";
        string participantID = (gameController != null) ? gameController.studyParticipantId.ToString() : "0";
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string studyFolderPath = Path.Combine(basePath, "Scripts", "Logging", studyName);

        if (!Directory.Exists(studyFolderPath)) {
            Directory.CreateDirectory(studyFolderPath);
        }

        string fileName = $"{participantID}_Log_{timestamp}.csv";
        filePath = Path.Combine(studyFolderPath, fileName);
    }

    void FixedUpdate() {
        if (isLogging) LogCurrentData();
    }

    public void StartLogger() {
        if (isLogging) return;
        try {
            if (!Directory.Exists(Path.GetDirectoryName(filePath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            }

            string metadata = $"{(gameController != null ? gameController.studyName : "Study")},{(gameController != null ? gameController.studyParticipantId.ToString() : "0")}\n";
            File.WriteAllText(filePath, metadata);
            File.AppendAllText(filePath, header + "\n");
            isLogging = true;
        }
        catch (System.Exception e) { Debug.LogError("Logger Start Error: " + e.Message); }
    }

    public void StopLogger()
    {
        if (!isLogging) return;
        isLogging = false;
        try { File.AppendAllText(filePath, END_OF_LOG_MARKER + "\n"); } catch (System.Exception e) { Debug.LogError("Logger Stop Error: " + e.Message); }
    }

    private void LogCurrentData()
    {
        if (bikeController == null || lkaController == null || frenetController == null) return;

        StringBuilder line = new StringBuilder();
        line.Append(Time.time.ToString("F4")).Append(",");
        line.Append(bikeController.BikeSpeed.ToString("F4")).Append(",");
        line.Append(bikeController.SteeringAngle.ToString("F4")).Append(",");
        line.Append(bikeController.FrontBrakeforce.ToString("F4")).Append(",");
        line.Append(bikeController.BackBrakeforce.ToString("F4")).Append(",");

        // LKA Hardware Switch State
        line.Append(lkaController.lkaSwitchActive ? "1" : "0").Append(",");

        // LKA Software Engagement (Active Steering)
        line.Append(lkaController.isEngaged ? "1" : "0").Append(",");
        line.Append(lkaController.motorDirection ? "1" : "0").Append(",");
        line.Append(lkaController.motorPWM).Append(",");

        line.Append(frenetController.isOnStraight ? "1" : "0").Append(",");
        line.Append(frenetController.curvatureAmount.ToString("F6")).Append(",");
        line.Append(frenetController.crossTrackError.ToString("F4")).Append(",");
        line.Append(frenetController.headingErrorDeg.ToString("F4"));

        try {File.AppendAllText(filePath, line.ToString() + "\n"); } catch (System.Exception) { isLogging = false; }
    }

    void OnApplicationQuit() { if (isLogging) StopLogger(); }
}
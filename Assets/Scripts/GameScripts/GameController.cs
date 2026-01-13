using UnityEngine;
using UnityEngine.Splines;
using TMPro;

public class GameController : MonoBehaviour {
    [Header("Study Parameters")]
    public string studyName = "DefaultStudy";
    public int studyParticipantId = 1;

    [Header("Study Logic")]
    public int totalRoundsToComplete = 3;
    public DataLogger dataLogger;
    public TextMeshProUGUI endStudyText;

    [Header("Auto-Trigger Settings")]
    public GameObject triggerCube;
    public float triggerZOffset = 5.0f;

    private int currentLap = 0;
    private bool studyStarted = false;
    private bool studyFinished = false;

    public enum Spawnpoint { Spawnpoint_0 = 0, Spawnpoint_1 = 1, Spawnpoint_2 = 2 }
    public Spawnpoint selectedSpawnpoint = Spawnpoint.Spawnpoint_0;
    public GameObject course;
    public GameObject bike;

    private Rigidbody bikeRigidbody;
    private SplineContainer splineContainer;
    private SplineSpawnpointData spawnpointData;

    void Start() {
        if (endStudyText != null) endStudyText.gameObject.SetActive(false);
        InitializeAndCheckSpawnVariables();
        SpawnBike();
    }

    private float lastTriggerTime = 0f;
    private float triggerCooldown = 3.0f; // Seconds to wait between triggers

    public void OnBikePassedTrigger()
    {
        if (studyFinished) return;
        if (Time.time - lastTriggerTime < triggerCooldown) return;
        lastTriggerTime = Time.time;

        if (!studyStarted)
        {
            studyStarted = true;
            currentLap = 1;
            Debug.Log("Study Started - Logger Called");
            if (dataLogger != null) dataLogger.StartLogger();
        }
        else
        {
            currentLap++;
            Debug.Log("Lap " + currentLap + " recorded");
            if (currentLap > totalRoundsToComplete) FinishStudy();
        }
    }

    private void FinishStudy() {
        studyFinished = true;
        if (dataLogger != null) dataLogger.StopLogger();
        if (endStudyText != null) {
            endStudyText.text = "Study is Over\nThank you for participating!";
            endStudyText.gameObject.SetActive(true);
        }
    }

    private void SpawnBike() {
        int spawnpointIndex = (int)selectedSpawnpoint;
        Spline selectedSpline = splineContainer[0];
        int knotIndex = spawnpointData.spawnpoints[spawnpointIndex].knotIndex;
        BezierKnot[] knotArray = selectedSpline.ToArray();

        Vector3 startPos = splineContainer.transform.TransformPoint(knotArray[knotIndex].Position);
        Quaternion startRot = splineContainer.transform.rotation * knotArray[knotIndex].Rotation;

        bikeRigidbody.MovePosition(startPos);
        bikeRigidbody.MoveRotation(startRot);

        if (triggerCube != null) {
            triggerCube.transform.position = startPos + (startRot * Vector3.forward * triggerZOffset);
            triggerCube.transform.rotation = startRot;
        }
    }

    private void InitializeAndCheckSpawnVariables() {
        bikeRigidbody = bike.GetComponent<Rigidbody>();
        splineContainer = course.GetComponentInChildren<SplineContainer>();
        spawnpointData = course.GetComponentInChildren<SplineSpawnpointData>();
    }
}
using UnityEngine;
using UnityEngine.Splines;
using TMPro;

public class GameController : MonoBehaviour {

    public enum StudyConditions {
        BaselineCW,
        BaselineCC,
        HapticsFixedCW,
        HapticsFixedCC,
        HapticsAdaptiveCW,
        HapticsAdaptiveCC,
    }

    [Header("Study Parameters")]
    public string studyName = "DefaultStudy";
    public int studyParticipantId = 1;
    public StudyConditions currentCondition = StudyConditions.BaselineCW;


    [Header("Study Logic")]
    public int totalRoundsToComplete = 1;
    public DataLogger dataLogger;
    public TextMeshProUGUI endStudyText;

    [Header("Spawn Settings")]
    public bool reverseDirection = false;

    [Header("Auto-Trigger Settings")]
    public GameObject triggerCube;
    public float triggerZOffset = 5.0f;

    public int currentLap = 0;
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
    private float triggerCooldown = 10.0f; // Seconds to wait between triggers

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
        // Example of how to use the condition in your logic or logging
        Debug.Log($"Finished study with condition: {currentCondition.ToString()}");

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

        // calculate rotation, flip 180 degrees if reverseDirection is true
        Quaternion baseRot = splineContainer.transform.rotation * knotArray[knotIndex].Rotation;
        Quaternion startRot = reverseDirection ? baseRot * Quaternion.Euler(0, 180, 0) : baseRot;

        bikeRigidbody.position = startPos;
        bikeRigidbody.rotation = startRot;

        if (triggerCube != null) {
            // trigger is always placed in front of the bike's current facing direction
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
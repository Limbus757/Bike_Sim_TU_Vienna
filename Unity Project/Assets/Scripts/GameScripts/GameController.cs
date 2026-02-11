using UnityEngine;
using UnityEngine.Splines;
using TMPro;

/// <summary>
/// Master controller for the research study. 
/// Manages participant data, lap tracking, bike spawning, and hardware safety shutdowns.
/// </summary>
public class GameController : MonoBehaviour {

    public enum StudyConditions {
        // CW = 0 (Forward)
        Training,
        BaselineCW,
        HapticsFixedCW,
        HapticsAdaptiveCW,

        // CC = 1 (Reverse)
        BaselineCC,
        HapticsFixedCC,
        HapticsAdaptiveCC
    }

    [Header("Study Parameters")]
    [Tooltip("Unique name for the current study trial.")]
    public string studyName = "DefaultStudy";
    [Tooltip("ID of the person currently participating.")]
    public int studyParticipantId = 1;
    [Tooltip("The condition currently being tested.")]
    public StudyConditions currentCondition = StudyConditions.BaselineCW;

    [Header("Logging and end procedure")]
    public DataLogger dataLogger;
    public TextMeshPro endStudyText;

    [Header("Spawn Settings")]
    [Tooltip("If true, flips the bike 180 degrees at the start line, including start and finish line spawns ")]
    public bool reverseDirection = false;

    [Header("Start and End Triggers")]
    public GameObject startTriggerObject;
    public GameObject finishTriggerObject;

    [Header("State Tracking")]
    public bool studyStarted = false;
    private bool studyFinished = false;

    public enum Spawnpoint { Spawnpoint_0 = 0, Spawnpoint_1 = 1, Spawnpoint_2 = 2 }

    public GameObject bike;
    public SplineContainer splineContainer;

    private Rigidbody bikeRigidbody;
    
    private SplineSpawnpointData spawnpointData;

    private SentSerialController sentSerial;

    /// <summary>
    /// Prepares the scene, identifies direction based on condition, and spawns objects.
    /// </summary>
    void Start() {
        if (endStudyText != null) {
            endStudyText.gameObject.SetActive(false);
        }

        if (sentSerial == null) sentSerial = FindObjectOfType<SentSerialController>();

        // sets the class-level boolean based on the enum integer flag
        switch (currentCondition) {
            case StudyConditions.BaselineCC:
            case StudyConditions.HapticsFixedCC:
            case StudyConditions.HapticsAdaptiveCC:
                reverseDirection = true;
                break;

            default: // Training + all CW conditions
                reverseDirection = false;
                break;
        }

        InitializeAndCheckSpawnVariables();
        SpawnBike();
        SpawnStartAndFinishTriggers();
    }

    /// <summary>
    /// Entry point for all trigger interactions.
    /// </summary>
    public void OnTriggerHit(StudyTrigger.TriggerType type) {
        if (studyFinished) return;

        // Only start if we haven't started yet
        if (type == StudyTrigger.TriggerType.StartLine && !studyStarted) {
            StartStudy();
        }
        // Only finish if we are currently mid-study
        else if (type == StudyTrigger.TriggerType.FinishLine && studyStarted) {
            FinishStudy();
        }
    }
    
    /// <summary>
    /// Positions the bike at Index 0 (Forward) or Index 1 (Reverse) using direct position assignment.
    /// </summary>
    private void SpawnBike() {
        int bikeIndex = reverseDirection ? 1 : 0;

        if (spawnpointData != null && spawnpointData.spawnpoints.Count > bikeIndex) {
            int knotIndex = spawnpointData.spawnpoints[bikeIndex].knotIndex;
            Spline selectedSpline = splineContainer[0];

            // Direct calculation of world position and rotation
            Vector3 startPos = splineContainer.transform.TransformPoint(selectedSpline[knotIndex].Position);
            Quaternion baseRot = splineContainer.transform.rotation * selectedSpline[knotIndex].Rotation;
            Quaternion startRot = reverseDirection ? baseRot * Quaternion.Euler(0, 180, 0) : baseRot;

            bikeRigidbody.position = startPos;
            bikeRigidbody.rotation = startRot;

            bikeRigidbody.WakeUp();
            Physics.SyncTransforms();
        }
    }

    /// <summary>
    /// Positions the triggers at Index 2 and 3. 
    /// Swaps their roles (Start vs Finish) if reverseDirection is true.
    /// </summary>
    private void SpawnStartAndFinishTriggers() {
        if (spawnpointData == null || spawnpointData.spawnpoints.Count < 4) {
            Debug.LogError("SplineSpawnpointData requires 4 points: [0]BikeF, [1]BikeR, [2]PointA, [3]PointB");
            return;
        }

        // Map indices: 2 is normally start, 3 is normally finish.
        int startKnotIdx = reverseDirection ? 3 : 2;
        int finishKnotIdx = reverseDirection ? 2 : 3;

        Spline spline = splineContainer[0];

        // Position Start Trigger (Direct Assignment)
        int sKnot = spawnpointData.spawnpoints[startKnotIdx].knotIndex;
        Vector3 sPos = splineContainer.transform.TransformPoint(spline[sKnot].Position);
        Quaternion sRot = splineContainer.transform.rotation * spline[sKnot].Rotation;
        if (reverseDirection) sRot *= Quaternion.Euler(0, 180, 0);

        startTriggerObject.transform.position = sPos;
        startTriggerObject.transform.rotation = sRot;
        startTriggerObject.GetComponent<StudyTrigger>().type = StudyTrigger.TriggerType.StartLine;

        // Position Finish Trigger (Direct Assignment)
        int fKnot = spawnpointData.spawnpoints[finishKnotIdx].knotIndex;
        Vector3 fPos = splineContainer.transform.TransformPoint(spline[fKnot].Position);
        Quaternion fRot = splineContainer.transform.rotation * spline[fKnot].Rotation;
        if (reverseDirection) fRot *= Quaternion.Euler(0, 180, 0);

        finishTriggerObject.transform.position = fPos;
        finishTriggerObject.transform.rotation = fRot;
        finishTriggerObject.GetComponent<StudyTrigger>().type = StudyTrigger.TriggerType.FinishLine;

        Physics.SyncTransforms();
    }

    /// <summary>
    /// Begins the study recording.
    /// </summary>
    private void StartStudy() {
        if (studyFinished || studyStarted) return;

        studyStarted = true;
        if (dataLogger != null) dataLogger.StartLogger();
    }

    /// <summary>
    /// Ends the study and secures hardware.
    /// </summary>
    private void FinishStudy() {
        if (studyFinished) return;

        studyFinished = true;

        if (dataLogger != null) dataLogger.StopLogger();

        if (endStudyText != null) {
            endStudyText.text = "<size=1.5><color=#00FFFF>Round Complete!</color></size>\n" +
                                "<size=1><color=white>You may now remove the headset.</color></size>";
            endStudyText.gameObject.SetActive(true);
        }

        if (sentSerial != null) {
            sentSerial.ShutdownSerial();
        }
    }

    /// <summary>
    /// caches components from the assigned course and bike objects.
    /// </summary>
    /// 
    private void InitializeAndCheckSpawnVariables() {
        bikeRigidbody = bike.GetComponent<Rigidbody>();
        spawnpointData = splineContainer.GetComponent<SplineSpawnpointData>();
    }
}
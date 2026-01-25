using UnityEngine;
using UnityEngine.Splines;
using TMPro;

/// <summary>
/// Master controller for the research study. 
/// Manages participant data, lap tracking, bike spawning, and hardware safety shutdowns.
/// </summary>
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
    [Tooltip("Unique name for the current study trial.")]
    public string studyName = "DefaultStudy";
    [Tooltip("ID of the person currently participating.")]
    public int studyParticipantId = 1;
    [Tooltip("The condition currently being tested.")]
    public StudyConditions currentCondition = StudyConditions.BaselineCW;

    [Header("Study Logic")]
    [Tooltip("How many laps the participant must complete before the study auto-ends.")]
    public int totalRoundsToComplete = 1;
    public DataLogger dataLogger;
    public TextMeshPro endStudyText;

    [Header("Spawn Settings")]
    [Tooltip("If true, flips the bike 180 degrees at the start line.")]
    public bool reverseDirection = false;

    [Header("Auto-Trigger Settings")]
    [Tooltip("The collider object that acts as the start/finish line.")]
    public GameObject triggerCube;
    [Tooltip("How far in front of the bike the trigger should be placed upon spawning.")]
    public float triggerZOffset = 6.0f;

    [Header("State Tracking")]
    public int currentLap = 0;
    private bool studyStarted = false;
    private bool studyFinished = false;

    public enum Spawnpoint { Spawnpoint_0 = 0, Spawnpoint_1 = 1, Spawnpoint_2 = 2 }

    [Tooltip("Select which knot on the spline the bike starts at.")]
    public Spawnpoint selectedSpawnpoint = Spawnpoint.Spawnpoint_0;
    public GameObject course;
    public GameObject bike;

    private Rigidbody bikeRigidbody;
    private SplineContainer splineContainer;
    private SplineSpawnpointData spawnpointData;

    private ReceivedSerialProvider receivedSerial;
    private SentSerialController sentSerial;

    private float lastTriggerTime = 0f;
    private float triggerCooldown = 5.0f; // seconds to wait between triggers to avoid double-counting

    /// <summary>
    /// prepares the scene, finds hardware controllers, and spawns the bike.
    /// </summary>
    void Start() {
        // hide completion text at the start
        if (endStudyText != null) endStudyText.gameObject.SetActive(false);

        // find hardware communication scripts
        if (sentSerial == null) sentSerial = FindObjectOfType<SentSerialController>();
        if (receivedSerial == null) receivedSerial = FindObjectOfType<ReceivedSerialProvider>();

        InitializeAndCheckSpawnVariables();
        SpawnBike();
    }

    /// <summary>
    /// logic executed when the StudyTrigger script detects the bike.
    /// handles study initialization on the first hit and lap counting thereafter.
    /// </summary>
    public void OnBikePassedTrigger() {
        if (studyFinished) return;

        if (studyStarted) {
            if (Time.time - lastTriggerTime < triggerCooldown) {  // ignore hits that happen too quickly
                return;
            }
        }

        lastTriggerTime = Time.time;

        if (!studyStarted) { // logic for the very first time the bike crosses the start line
            studyStarted = true;
            currentLap = 1;

            if (dataLogger != null) {
                dataLogger.StartLogger();
            }
            Debug.Log("<color=green>Study Started. Logger initialized.</color>");
        } else { // logic for subsequent laps
            currentLap++;
            Debug.Log($"<color=white><b>Lap {currentLap}</b> recorded.</color>");

            if (currentLap > totalRoundsToComplete) {
                FinishStudy();
            }
        }
    }

    /// <summary>
    /// cleans up the study session, stops data logging, and disables hardware for safety.
    /// </summary>
    private void FinishStudy() {
        studyFinished = true;

        // stop the logger immediately to ensure data integrity
        if (dataLogger != null) dataLogger.StopLogger();

        // show the completion message to the participant
        if (endStudyText != null) {
            endStudyText.text = "<size=2><color=#00FFFF>Round Completed!</color></size>\n" +
                                "<size=1><color=white>You may now remove the headset.</color></size>";
            endStudyText.gameObject.SetActive(true);
        }

        
        // hardware safety: tell serial controllers to kill power to motors
        if (sentSerial != null) {
            sentSerial.ShutdownSerial();
        } else {
            Debug.LogWarning("FinishStudy: SentSerialController not found. Hardware may still be active!");
        }

        if (receivedSerial != null) {
            receivedSerial.ShutdownSerial();

        } else {
            Debug.LogWarning("FinishStudy: ReceivedSerialProvider not found. Hardware may still be active!");
        }
        
    }

    /// <summary>
    /// positions the bike on the track based on spline data and selected spawnpoint.
    /// </summary>
    private void SpawnBike() {
        int spawnpointIndex = (int)selectedSpawnpoint;
        Spline selectedSpline = splineContainer[0];

        // get the specific knot (point on path) for spawning
        int knotIndex = spawnpointData.spawnpoints[spawnpointIndex].knotIndex;
        BezierKnot[] knotArray = selectedSpline.ToArray();

        // convert local spline position to world position
        Vector3 startPos = splineContainer.transform.TransformPoint(knotArray[knotIndex].Position);

        // calculate rotation and handle the reverse direction toggle
        Quaternion baseRot = splineContainer.transform.rotation * knotArray[knotIndex].Rotation;
        Quaternion startRot = reverseDirection ? baseRot * Quaternion.Euler(0, 180, 0) : baseRot;

        // apply to physics engine
        bikeRigidbody.position = startPos;
        bikeRigidbody.rotation = startRot;

        // force physics to update immediately to prevent "ghost" collisions
        bikeRigidbody.WakeUp();
        Physics.SyncTransforms();

        // place the start/finish trigger slightly ahead of the bike
        if (triggerCube != null) {
            triggerCube.transform.position = startPos + (startRot * Vector3.forward * triggerZOffset);
            triggerCube.transform.rotation = startRot;
        }
    }

    /// <summary>
    /// caches components from the assigned course and bike objects.
    /// </summary>
    private void InitializeAndCheckSpawnVariables() {
        bikeRigidbody = bike.GetComponent<Rigidbody>();
        splineContainer = course.GetComponentInChildren<SplineContainer>();
        spawnpointData = course.GetComponentInChildren<SplineSpawnpointData>();
    }
}
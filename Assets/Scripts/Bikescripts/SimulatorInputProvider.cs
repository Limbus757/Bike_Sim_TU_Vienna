using UnityEngine;

// Ensure this script is placed in the same project/namespace as ReceivedSerialProvider
// or that ReceivedSerialProvider is accessible.
public class SimulatorInputProvider : MonoBehaviour, IBikeInputProvider {
    [Header("Controller Assignments")]
    [Tooltip("The VR Controller GameObject used to capture steering input.")]
    public GameObject leftController;

    [Tooltip("The VR Controller used for the calibration button press.")]
    public GameObject rightController;

    public float SteeringAngle { get; private set; } = 0.0f;

    private bool steeringInitialized = false;
    private float steeringZeroYaw = 0f;

    // --- Interface Methods ---

    public float GetRearBrakeForce() => ReceivedSerialProvider.RearBrakeForce;
    public float GetFrontBrakeForce() => ReceivedSerialProvider.FrontBrakeForce;
    public float GetResistance() => ReceivedSerialProvider.ResistanceValue;
    public float GetSpeed() => ReceivedSerialProvider.SpeedKmh;
    public float GetSteeringAngle() => SteeringAngle;

    // --- Core Lifecycle ---

    void Awake() {
        InitializeSteering();
    }

    void Start() {
        // Auto-calibrate after a short delay to ensure VR tracking is active
        Invoke(nameof(RecalibrateSteering), 1.0f);
    }

    void Update() {
        // Calibration check
        if (Input.GetKeyDown(KeyCode.Space) || OVRInput.GetDown(OVRInput.Button.Two)) {
            RecalibrateSteering();
            Debug.Log("Steering Recalibrated.");
        }

        UpdateSteeringAngle();
    }

    // --- Steering Logic ---

    private void InitializeSteering() {
        if (leftController != null) {
            steeringInitialized = true;
        } else {
            Debug.LogError("SimulatorInputProvider: 'Left Controller' GameObject not assigned!");
        }
    }

    public void RecalibrateSteering() {
        if (!steeringInitialized || leftController == null) return;

        // Calculate how much the controller is offset from the BIKE'S current world rotation
        float currentWorldYaw = leftController.transform.eulerAngles.y;
        float bikeWorldYaw = transform.eulerAngles.y;

        // Store the difference so 'straight' is always relative to the bike's heading
        steeringZeroYaw = Mathf.DeltaAngle(bikeWorldYaw, currentWorldYaw);

        Debug.Log($"Steering Calibrated! Zero Offset relative to bike: {steeringZeroYaw:F2}");
    }

    private void UpdateSteeringAngle() {
        if (steeringInitialized && leftController != null) {
            // 1. Get current world yaw of controller and bike
            float currentWorldYaw = leftController.transform.eulerAngles.y;
            float bikeWorldYaw = transform.eulerAngles.y;

            // 2. Calculate the controller's yaw relative to the bike's current heading
            float relativeYaw = Mathf.DeltaAngle(bikeWorldYaw, currentWorldYaw);

            // 3. Subtract the calibration offset to find the actual steering input
            float deltaYaw = Mathf.DeltaAngle(steeringZeroYaw, relativeYaw);

            // 4. Clamp to your +/- 50 degree limits
            SteeringAngle = Mathf.Clamp(deltaYaw, -50f, 50f);
        }
    }
}
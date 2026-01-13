using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Ensure this script is placed in the same project/namespace as ReceivedSerialProvider
// or that ReceivedSerialProvider is accessible.
public class SimulatorInputProvider : MonoBehaviour, IBikeInputProvider
{

    [Header("Controller Assignments")]
    [Tooltip("The VR Controller GameObject used to capture steering input.")]
    public GameObject leftController;

    [Tooltip("The VR Controller used for the calibration button press.")]
    public GameObject rightController;

    public float SteeringAngle { get; private set; } = 0.0f;

    private Transform handlebar;
    // Stores the rotation set during calibration, used as the zero reference
    private Quaternion steeringZeroRotation = Quaternion.identity;
    private bool steeringInitialized = false;

    // --- Interface Methods (Reading Serial Data) ---

    public float GetRearBrakeForce()
    {
        return ReceivedSerialProvider.RearBrakeForce;
    }

    public float GetFrontBrakeForce()
    {
        return ReceivedSerialProvider.FrontBrakeForce;
    }

    public float GetResistance()
    {
        return ReceivedSerialProvider.ResistanceValue;
    }

    public float GetSpeed()
    {
        return ReceivedSerialProvider.SpeedKmh;
    }

    public float GetSteeringAngle()
    {
        UpdateSteeringAngle();
        return SteeringAngle;
    }

    // --- Core Lifecycle ---

    void Awake()
    {
        InitializeSteering();
    }

    void Start()
    {
        // Auto-calibrate after a short delay to ensure VR tracking is active
        Invoke(nameof(RecalibrateSteering), 1.0f);
    }

    void Update()
    {
        // NOTE: Ensure your separate CameraAndSteeringCalibration script handles 
        // the OVRInput button check and calls RecalibrateSteering().
        // Keeping the Spacebar check here for easy testing if that script is absent.
        if (Input.GetKeyDown(KeyCode.Space))
        {
            RecalibrateSteering();
            Debug.Log("Steering Recalibrated via calibration button.");
        }

        UpdateSteeringAngle();
    }

    // --- Steering Logic ---

    private void InitializeSteering()
    {
        handlebar = this.transform.Find("WheelHandleBar");

        if (handlebar == null)
        {
            Debug.LogError("SimulatorInputProvider: 'WheelHandleBar' child Transform not found! Check name.");
        }

        if (leftController == null)
        {
            Debug.LogError("SimulatorInputProvider: 'Left Controller' GameObject not assigned! Steering input will not work.");
        }

        if (leftController != null)
        {
            steeringInitialized = true;
            Debug.Log("SimulatorInputProvider: Steering setup complete. Waiting for calibration.");
        }
    }

    /// <summary>
    /// Captures the left controller's current rotation as the new straight-ahead zero-point.
    /// </summary>
    public void RecalibrateSteering()
    {
        if (!steeringInitialized || leftController == null)
        {
            Debug.LogError("Recalibration Failed: Steering system not initialized or controller missing.");
            return;
        }

        // Capture the current rotation of the left controller as the new zero-point.
        // Only capture the Y-axis rotation (Yaw) of the controller for a cleaner zero reference.
        steeringZeroRotation = Quaternion.Euler(0, leftController.transform.rotation.eulerAngles.y, 0);

        Debug.Log($"Steering Zero-Point Set to World Yaw: {steeringZeroRotation.eulerAngles.y:F2}");
    }

    /// <summary>
    /// Calculates the steering angle by isolating the World Y-axis (Yaw) rotation.
    /// This makes the steering less susceptible to controller roll and pitch.
    /// </summary>
    private void UpdateSteeringAngle()
    {
        if (steeringInitialized && leftController != null)
        {

            // isolate the World Y-axis rotation (Yaw) of the current controller rotation.
            Quaternion currentWorldYaw = Quaternion.Euler(
                0,
                leftController.transform.rotation.eulerAngles.y,
                0
            );

            // 2. Isolate the World Y-axis rotation (Yaw) of the zero-point.
            // Note: This is redundant if RecalibrateSteering() is correct, but safer here.
            Quaternion zeroWorldYaw = Quaternion.Euler(
                0,
                steeringZeroRotation.eulerAngles.y,
                0
            );

            // 3. Calculate the rotational difference (delta).
            // This is the rotation from the zero point to the current point.
            Quaternion currentDelta = Quaternion.Inverse(zeroWorldYaw) * currentWorldYaw;

            float tempSteeringAngle = 0.0f;

            // 4. Extract the Yaw from the delta.
            tempSteeringAngle = currentDelta.eulerAngles.y;

            // 5. Convert 0-360 range to the shortest signed angle (-180 to 180).
            tempSteeringAngle = Mathf.DeltaAngle(0, tempSteeringAngle);

            // 6. Clamp to bike steering limits and set the final angle.
            tempSteeringAngle = Mathf.Clamp(tempSteeringAngle, -90, 90);
            SteeringAngle = tempSteeringAngle;

        }
        else if (handlebar == null)
        {
            Debug.LogWarning("SteeringInputProvider: Initialization failed, cannot change steering angle!");
        }
    }
}
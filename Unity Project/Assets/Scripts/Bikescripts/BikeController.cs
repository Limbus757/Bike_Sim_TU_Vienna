using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MotionSystems;
using System;

/// <summary>
/// Central controller for the bike entity. 
/// Coordinates input retrieval, physics-based movement, and motion platform synchronization.
/// </summary>
public class BikeController : MonoBehaviour {

    #region Input-Control-Parameters
    public enum InputMode { Microcontroller, Gamepad }

    [Header("Input Mode Settings")]
    [Tooltip("Select the active input mode.")]
    public InputMode currentInputMode = InputMode.Microcontroller;

    public enum VisualTiltingMode { Disabled, Enabled }

    [Header("Camera Tilt Mode Settings")]
    [Tooltip("Select the camera tilt mode.")]
    public VisualTiltingMode currentVisualTiltingMode = VisualTiltingMode.Disabled;

    public enum RollAndPitchMode { Disabled, Enabled, NoTilt, NoPitch }

    [Header("Platform Mode Settings")]
    [Tooltip("Select the motion platform mode.")]
    public RollAndPitchMode currentRollAndPitchMode = RollAndPitchMode.Disabled;
    #endregion

    [Header("Bike Variables")]
    [Tooltip("Variables relevant to the handling of the bike.")]
    public float BikeSpeedKmh;
    public float SteeringAngle;
    public float TurnRadius;
    public float TiltAngle;
    public float RollPosition;
    public float PitchPosition;
    public float FrontBrakeforce;
    public float BackBrakeforce;
    public float BikeSpeedMS => BikeSpeedKmh / 3.6f;

    [Header("Tuning Constants")]
    [SerializeField] private float accelerationMultiplier = 2.5f;
    [SerializeField] private float brakeForceMultiplier = 3.0f;
    [SerializeField] private float tiltMultiplier = 0.5f;
    [SerializeField] private float pitchMultiplier = 50f;

    [Header("Camera Visuals")]
    [SerializeField] private float maxTiltAngle = 30.0f;
    public float visualTiltMultiplier = 1000f;
    public float visualTiltSpeed = 0.5f;

    [Header("Necessary Gameobjects")]
    [Tooltip("The visual handle bar mesh to be rotated.")]
    public Transform HandleBarTransform;
    [Tooltip("The camera that will tilt during turns.")]
    public GameObject Camera;

    [SerializeField] public IBikeInputProvider inputProvider;

    private MotionPlatformController motionPlatform;
    private Rigidbody bikeRigidBody;

    /// <summary>
    /// initializes references and validates the required components in the hierarchy.
    /// </summary>
    void Awake() {
        // get reference to the inputProvider based on selected mode
        FetchInputProvider();

        bikeRigidBody = GetComponent<Rigidbody>();
        if (bikeRigidBody == null) {
            Debug.LogError("BikeController: Rigidbody not found on this GameObject! Physics will not work.");
        }

        // find the handlebar child by name if not assigned
        if (HandleBarTransform == null) {
            HandleBarTransform = transform.Find("WheelHandleBar");
            if (HandleBarTransform == null) {
                Debug.LogError("BikeController: 'WheelHandleBar' child Transform not found! Please assign correctly or check name.");
            }
        }

        // initialize bike's internal state to zero
        BikeSpeedKmh = 0f;
        SteeringAngle = 0f;
        FrontBrakeforce = 0f;
        BackBrakeforce = 0f;
        PitchPosition = 0f;
        RollPosition = 0f;
    }

    /// <summary>
    /// assigns the correct input provider implementation based on the enum selection.
    /// </summary>
    private void FetchInputProvider() {
        switch (currentInputMode) {
            case InputMode.Microcontroller:
                inputProvider = GetComponent<SimulatorInputProvider>();
                break;
            case InputMode.Gamepad:
                inputProvider = GetComponent<GamepadInputProvider>();
                break;
            default:
                Debug.LogError("BikeController: Invalid InputMode selected.");
                break;
        }
    }

    /// <summary>
    /// handles physics-based updates and movement calculations.
    /// </summary>
    void FixedUpdate() {
        FetchControlInputs();
        ApplyBikeAcceleration(bikeRigidBody);
        MoveBikeAlongTurn();
        UpdateBikeTiltAndPitch();
        UpdateBikeVisuals();
    }

    /// <summary>
    /// pulls the latest data from the active input provider.
    /// </summary>
    private void FetchControlInputs() {
        SteeringAngle = inputProvider.GetSteeringAngle();
        BikeSpeedKmh = inputProvider.GetSpeed();
        FrontBrakeforce = inputProvider.GetFrontBrakeForce();
        BackBrakeforce = inputProvider.GetRearBrakeForce();
    }

    /// <summary>
    /// applies forward force or braking deceleration to the rigidbody.
    /// </summary>
    private void ApplyBikeAcceleration(Rigidbody rigidBody) {
        // convert speed from km/h to m/s
        float targetSpeed = BikeSpeedMS;
        float currentSpeed = rigidBody.velocity.magnitude;
        float brakeForce = BackBrakeforce + FrontBrakeforce;

        Vector3 forward = transform.forward;
        Vector3 currentDir = rigidBody.velocity.normalized;

        // handle speed gain if not braking
        if (targetSpeed > currentSpeed && brakeForce == 0f) {
            float speedGain = accelerationMultiplier * Time.fixedDeltaTime;
            float newSpeed = Mathf.Min(currentSpeed + speedGain, targetSpeed);
            rigidBody.velocity = forward * newSpeed;
        }
        // apply deceleration based on total brake force
        else if (brakeForce > 0f) {
            float deceleration = brakeForce * brakeForceMultiplier;
            float speedDrop = deceleration * Time.fixedDeltaTime;
            float newSpeed = Mathf.Max(currentSpeed - speedDrop, 0f);
            rigidBody.velocity = currentDir * newSpeed;
        }
        // maintain speed if no significant changes
        else {
            bikeRigidBody.velocity = forward * targetSpeed;
        }
    }

    /// <summary>
    /// calculates the turning radius and rotates the bike based on the steering angle.
    /// </summary>
    private void MoveBikeAlongTurn() {
        float speedInMS = BikeSpeedMS;
        if (BikeSpeedMS < 0.1f) return;

        float wheelbase = 1.5f; // standard bike wheelbase approximation

        float rotationStep = (speedInMS / wheelbase) * Mathf.Tan(SteeringAngle * Mathf.Deg2Rad) * Time.fixedDeltaTime; // calculate the angular change step based on steering geometry

        transform.Rotate(Vector3.up, rotationStep * Mathf.Rad2Deg); // rotate the transform on the vertical axis

        bikeRigidBody.MovePosition(transform.position + transform.forward * speedInMS * Time.fixedDeltaTime); // move the physics body forward in the new direction
    }


    // TODO: Implement tilting mode and more accurate calculations when motion platform is functional again.
    /// <summary>
    /// calculates roll and pitch values and sends them to the motion platform.
    /// </summary>
    private void UpdateBikeTiltAndPitch() {
        // placeholder variables for calculation results
        float tempTilt = 0;
        float tempPitch = 0;

        // determine tilt based on steering input
        float calculatedTilt = SteeringAngle * tiltMultiplier;

        // calculate pitch based on the bike's vertical angle relative to the horizon
        float pitchAngle = Vector3.Angle(transform.forward, Vector3.up);
        float calculatedPitch = (pitchAngle - 90) * 600f;

        // switch logic for different platform hardware modes
        switch (currentRollAndPitchMode) {
            case RollAndPitchMode.Enabled:
                tempTilt = calculatedTilt;
                tempPitch = calculatedPitch;
                break;
            case RollAndPitchMode.NoTilt:
                tempTilt = 0;
                tempPitch = calculatedPitch;
                break;
            case RollAndPitchMode.NoPitch:
                tempTilt = calculatedTilt;
                tempPitch = 0;
                break;
            case RollAndPitchMode.Disabled:
                tempTilt = 0;
                tempPitch = 0;
                break;
            default:
                tempTilt = 0;
                tempPitch = 0;
                break;
        }

        // update the external motion platform controller if enabled
        if (currentRollAndPitchMode != RollAndPitchMode.Disabled && motionPlatform != null) {
            RollPosition = tempTilt;
            PitchPosition = tempPitch;
            motionPlatform.UpdatePlatformPosition(PitchPosition, RollPosition);
        }
    }

    /// <summary>
    /// updates the visual transforms of the bike components like handlebars.
    /// </summary>
    private void UpdateBikeVisuals() {
        // rotate the handlebars around the local vertical axis
        HandleBarTransform.localEulerAngles = new Vector3(0.0f, SteeringAngle, 0.0f);
    }


    // TODO: Implement visual tilting with more accurate calculations.
    /// <summary>
    /// applies a procedural lean to the camera based on the bike's current tilt.
    /// </summary>
    private void ApplyCameraTilting() {
        // calculate target camera roll
        float visualTiltAngle = -(TiltAngle) * visualTiltMultiplier;
        visualTiltAngle = Mathf.Clamp(visualTiltAngle, -maxTiltAngle, maxTiltAngle);

        // smoothly interpolate to the target rotation
        Quaternion currentRot = Camera.transform.localRotation;
        Quaternion targetRot = Quaternion.Euler(0f, 0f, visualTiltAngle);
        Quaternion interpolatedRotation = Quaternion.Lerp(currentRot, targetRot, Time.deltaTime * visualTiltSpeed);

        Camera.transform.localRotation = interpolatedRotation;
    }
}
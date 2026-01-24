using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MotionSystems;
using System;

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
    [Tooltip("Select the platform mode.")]
    public RollAndPitchMode currentRollAndPitchMode = RollAndPitchMode.Disabled;

    #endregion

    [Header("Bike Variables")]
    [Tooltip("Variables relevant to the handling of the bike.")]
    public float BikeSpeed;
    public float SteeringAngle;
    public float TurnRadius;
    public float TiltAngle;
    public float RollPosition;
    public float PitchPosition;
    public float FrontBrakeforce;
    public float BackBrakeforce;

    // variables relevent to how the bike handles, can be tuned
    private float accelerationMultiplier = 2.5f;
    private float brakeForceMultiplier = 3.0f;
    private float tiltMultiplier = 0.5f;
    private float pitchMultiplier = 50f;

    // variables specific to Visual Tilting of the camera when turning
    private float maxTiltAngle = 30.0f;
    public float visualTiltMultiplier = 1000f;
    public float visualTiltSpeed = 0.5f;

    [Header("Necessary Gameobjects")]
    [Tooltip("Gameobjects relevant to the bike.")]
    public Transform HandleBarTransform;
    public GameObject Camera;

    [SerializeField] public IBikeInputProvider inputProvider;

    private MotionPlatformController motionPlatform;
    private Rigidbody bikeRigidBody;

    void Awake() {
        FetchInputProvider(); // get reference to the inputProvider

        bikeRigidBody = GetComponent<Rigidbody>();        
        if (bikeRigidBody == null) {
            Debug.LogError("BikeController: Rigidbody not found on this GameObject! Physics will not work.");
        }

        HandleBarTransform = transform.Find("WheelHandleBar");
        if (HandleBarTransform == null) {
            Debug.LogError("BikeController: 'WheelHandleBar' child Transform not found! Please assign correctly or check name.");
        }

        // initialize bike's internal state
        BikeSpeed = 0f;
        SteeringAngle = 0f;
        FrontBrakeforce = 0f;
        BackBrakeforce = 0f;
        PitchPosition = 0f;
        RollPosition = 0f;
    }

    private void FetchInputProvider() {
        switch (currentInputMode) {
            case InputMode.Microcontroller:
                inputProvider = GetComponent<SimulatorInputProvider>();
                break;
            case InputMode.Gamepad:
                inputProvider = GetComponent<GamepadInputProvider>();
                break;
            default:
                Debug.LogError("BikeController: Invalid InputMode selected. No input provider will be assigned.");
                break;
        }
    }

    void FixedUpdate() {
        FetchControlInputs();
        ApplyBikeAcceleration(bikeRigidBody);
        MoveBikeAlongTurn();
        UpdateBikeTiltAndPitch();
        UpdateBikeVisuals();
    }

    private void FetchControlInputs() {
        SteeringAngle = inputProvider.GetSteeringAngle();
        BikeSpeed = inputProvider.GetSpeed();
        FrontBrakeforce = inputProvider.GetFrontBrakeForce();
        BackBrakeforce = inputProvider.GetRearBrakeForce();
    }

    private void ApplyBikeAcceleration(Rigidbody rigidBody) {

        float targetSpeed = BikeSpeed / 3.6f; // km/h to m/s
        float currentSpeed = rigidBody.velocity.magnitude;
        float brakeForce = BackBrakeforce + FrontBrakeforce;

        Vector3 forward = transform.forward;
        Vector3 currentDir = rigidBody.velocity.normalized;

        // apply acceleration
        if (targetSpeed > currentSpeed && brakeForce == 0f) {
            float speedGain = accelerationMultiplier * Time.fixedDeltaTime;
            float newSpeed = Mathf.Min(currentSpeed + speedGain, targetSpeed);
            rigidBody.velocity = forward * newSpeed;
        } else if (brakeForce > 0f) {
            float deceleration = brakeForce * brakeForceMultiplier; 
            float speedDrop = deceleration * Time.fixedDeltaTime;
            float newSpeed = Mathf.Max(currentSpeed - speedDrop, 0f);
            rigidBody.velocity = currentDir * newSpeed;
        } else {
            bikeRigidBody.velocity = forward * targetSpeed;
        }
    }

    private void MoveBikeAlongTurn() {
        float speedInMS = BikeSpeed / 3.6f;
        if (speedInMS < 0.1f) return;

        float wheelbase = 1.5f;

        // 1. Calculate how much the bike SHOULD rotate based on steering
        // If SteeringAngle is 0, tan is 0, and rotation is 0. No "if" needed!
        float rotationStep = (speedInMS / wheelbase) * Mathf.Tan(SteeringAngle * Mathf.Deg2Rad) * Time.fixedDeltaTime;

        // 2. Apply the rotation
        transform.Rotate(Vector3.up, rotationStep * Mathf.Rad2Deg);

        // 3. Move the Rigidbody forward in its NEW direction
        bikeRigidBody.MovePosition(transform.position + transform.forward * speedInMS * Time.fixedDeltaTime);
    }

    // TODO: This function is currently not fully implemented, because the Motion Platform is not working
    private void UpdateBikeTiltAndPitch() {

        #warning  TODO REFINE CALULATIONS
        // Variables for calculations
        float tempTilt = 0;
        float tempPitch = 0;

        float calculatedTilt = SteeringAngle * tiltMultiplier;

        // Pitch calculation using the Approximated model logic, as a default
        float pitchAngle = Vector3.Angle(transform.forward, Vector3.up);
        float calculatedPitch = (pitchAngle - 90) * 600f;
        //calculatedPitch += tempBrakeForce * 500;

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
                Debug.LogWarning("BikeController: Invalid PitchAndTiltMode. No platform calculations will be performed.");
                tempTilt = 0;
                tempPitch = 0;
                break;
        }

        if (currentRollAndPitchMode != RollAndPitchMode.Disabled) {
            RollPosition = tempTilt;
            PitchPosition = tempPitch;
            motionPlatform.UpdatePlatformPosition(PitchPosition, RollPosition);
        }
    }

    private void UpdateBikeVisuals() {
        HandleBarTransform.localEulerAngles = new Vector3(0.0f, SteeringAngle, 0.0f);
    }


    // TODO: This function is currently not fully implemented, because the Motion Platform is not working

    private void ApplyCameraTilting() {
        float visualTiltAngle = -(TiltAngle) * visualTiltMultiplier;

        visualTiltAngle = Mathf.Clamp(visualTiltAngle, -maxTiltAngle, maxTiltAngle);

        Quaternion currentRot = Camera.transform.localRotation;
        Quaternion targetRot = Quaternion.Euler(0f, 0f, visualTiltAngle);
        Quaternion interpolatedRotation = Quaternion.Lerp(currentRot, targetRot, Time.deltaTime * visualTiltSpeed);
        Camera.transform.localRotation = interpolatedRotation;
    }
}


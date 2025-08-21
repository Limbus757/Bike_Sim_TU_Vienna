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

    public float bikeSpeed { get; private set; }
    public float steeringAngle { get; private set; }
    public float turnRadius { get; private set; }
    public float tiltAngle { get; private set; }
    public float rollPosition { get; private set; }
    public float pitchPosition { get; private set; }
    public float frontBrakeforce { get; private set; }
    public float backBrakeforce { get; private set; }

    // variables relevent to how the bike handles, need to be tuned
    private float accelerationMultiplier = 2.5f;
    private float brakeForceMultiplier = 3.0f;
    private float tiltMultiplier = 0.5f;
    private float pitchMultiplier = 50f;

    // references specific to BikeController
    public GameObject BikeBase;
    public GameObject HandleBar;
    public GameObject Camera;
    [SerializeField] private GameObject visualTiltTarget;

    private IBikeInputProvider inputProvider;

    private MotionPlatformController motionPlatform;
    private Rigidbody bikeRigidBody;

    private MotionPlatformController motionPlatformController;

    void Awake() {
        FetchInputProvider(); // get reference to the inputProvider

        bikeRigidBody = GetComponent<Rigidbody>();        
        if (bikeRigidBody == null) {
            Debug.LogError("BikeController: Rigidbody not found on this GameObject! Physics will not work.");
        }

        motionPlatformController = FindObjectOfType<MotionPlatformController>();
        if (motionPlatformController == null) {
            Debug.LogError("BikeController: MotionPlatformController not found in scene! Cannot send platform data.");
        }

        // initialize bike's internal state
        bikeSpeed = 0f;
        steeringAngle = 0f;
        frontBrakeforce = 0f;
        backBrakeforce = 0f;
        pitchPosition = 0f;
        rollPosition = 0f;
    }

    private void FetchInputProvider() {
        switch (currentInputMode) { 
        case InputMode.Microcontroller:
            inputProvider = FindObjectOfType<PhysicalBikeInputProvider>();
            break;
        case InputMode.Gamepad:
            inputProvider = FindObjectOfType<GamepadInputProvider>();
            break;
        default:
            inputProvider = null;
            Debug.LogError("BikeController: Inputmode not set or found, the bike will not be able to drive!");
            break;
        }
   
    }

    void FixedUpdate() {
        FetchControlInputs();
        ApplyBikeAcceleration(bikeRigidBody);
        MoveBikeAlongTurn();
        UpdateBikeTiltAndPitch();
    }

    private void FetchControlInputs() {
        steeringAngle = inputProvider.GetSteeringAngle();
        bikeSpeed = inputProvider.GetSpeed();
        frontBrakeforce = inputProvider.GetFrontBrakeForce();
        backBrakeforce = inputProvider.GetBackBrakeForce();
    }

    private void ApplyBikeAcceleration(Rigidbody rigidBody) {

        float targetSpeed = bikeSpeed / 3.6f; // km/h to m/s
        float currentSpeed = rigidBody.velocity.magnitude;
        float brakeForce = backBrakeforce + frontBrakeforce;

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
        float wheelbase = 1.5f;
        turnRadius = wheelbase / (Mathf.Sin(Mathf.Abs(steeringAngle) * Mathf.Deg2Rad));

        if (turnRadius > 85)
            turnRadius = Mathf.Infinity;

        Vector3 turningCenterCurve = (transform.position + (transform.right.normalized * turnRadius));
        int sign = 0;

        if (steeringAngle < 0) {
            Vector3 curDirection = turningCenterCurve - transform.position;
            turningCenterCurve = transform.position - curDirection;
            sign = -1;
        } else if (steeringAngle > 0) {
            sign = 1;
        }

        float speedInMS = bikeSpeed / 3.6f;

        if (steeringAngle != 0 && turnRadius != Mathf.Infinity) // curve
        {
            transform.RotateAround(turningCenterCurve, Vector3.up, sign * ((speedInMS * 1f) / (2f * Mathf.PI * turnRadius) * 360f) * Time.deltaTime);
        } else {
            transform.position = transform.position + transform.forward * Time.deltaTime * speedInMS;
        }
    }

    private void UpdateBikeTiltAndPitch() {

        #warning  TODO REFINE CALULATIONS
        // Variables for calculations
        float tempTilt = 0;
        float tempPitch = 0;

        float calculatedTilt = steeringAngle * tiltMultiplier;

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

        // Apply the calculated values to the bike's properties
        rollPosition = tempTilt;
        pitchPosition = tempPitch;
        motionPlatform.UpdatePlatformPosition(pitchPosition, rollPosition);
    }
}


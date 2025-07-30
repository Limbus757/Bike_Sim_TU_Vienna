using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MotionSystems;
using System;

public class BikeController : MonoBehaviour {
    public float bikeSpeed { get; private set; }
    public float steeringAngle { get; private set; }
    public float curveRadius { get; private set; }
    public float tiltAngle { get; private set; }
    public float rollPosition { get; private set; }
    public float pitchPosition { get; private set; }
    public float appliedFrontBrakeForce { get; private set; }
    public float appliedBackBrakeForce { get; private set; }

    // References specific to BikeController
    public GameObject BikeBase;
    public GameObject HandleBar;
    public GameObject Camera;
    [SerializeField] private GameObject visualTiltTarget;

    private SteeringInputProvider steeringInputProvider;
    private UdoinoInputProvider udoinoInputProvider;

    private GameControllerScript gameControllerScript;
    private HandleBarCollider handleBarColliderScript;
    private Bike bikeModel;

    public Quaternion initialBikeRotation;

    // Event for broadcasting bike state
    public class BikeStateChangedEventArgs : EventArgs {
        public float currentSpeed { get; set; }
        public float currentSteeringAngle { get; set; }
        public float currentICurveRadius { get; set; }
        public float currentITiltAngle { get; set; }
        public float currentRollPosition { get; set; }
        public float currentPitchPosition { get; set; }
        public float currentAppliedFrontBrakeForce { get; set; }
        public float currentAppliedBackBrakeForce { get; set; }
    }

    public static event EventHandler<BikeStateChangedEventArgs> OnBikeStateChanged;


    void Awake() {
        // Get reference to the SteeringInputProvider in the scene
        steeringInputProvider = FindObjectOfType<SteeringInputProvider>();
        udoinoInputProvider = FindObjectOfType <MicroContollerInputProvider>();

        if (steeringInputProvider == null) {
            Debug.LogError("BikeControllerScript: SteeringInputProvider not found! Bike will not receive steering input.");
        }

        if (udoinoInputProvider == null) {
            Debug.LogError("BikeControllerScript: UdoinoInputProvider not found! Bike will not receive accel/decel input.");
        }

        // Initialize bike's internal state variables
        bikeSpeed = 0f;
        steeringAngle = 0f;
        appliedFrontBrakeForce = 0f;
        appliedBackBrakeForce = 0f;
        pitchPosition = 0f;
        rollPosition = 0f;

        HandleBarCollider handleBarColliderScript = HandleBar.GetComponent<HandleBarCollider>();
        if (handleBarColliderScript == null) Debug.LogError("HandleBarCollider not found on HandleBar GameObject!");
    }

    void FixedUpdate() {
        if (BikeBase != null) {
            var rgb = this.GetComponent<Rigidbody>();
            ApplyBikeMotion(rgb);
            ApplyVisualTiltingCamera();
            MoveBikeAlongTurn();
        }
    }

    private void FetchControlInputs() {
        this.steeringAngle = steeringInputProvider.SteeringAngle;
        this.bikeSpeed = udoinoInputProvider.targetSpeed;
        this.brakeforce = udoinoInputProvider.
    }

    private void ApplyBikeMotion(Rigidbody rigidBody) {
        float targetSpeed = gameControllerScript.BikeSpeed / 3.6f; // km/h to m/s
        float currentSpeed = rigidBody.velocity.magnitude;
        float brakeForce = gameControllerScript.appliedBrakeForce;

        Vector3 forward = transform.forward;
        Vector3 currentDir = rigidBody.velocity.normalized;

        // --- Apply Acceleration ---
        if (targetSpeed > currentSpeed && brakeForce == 0f) {
            float accel = 2.5f; // Tune acceleration
            float speedGain = accel * Time.fixedDeltaTime;
            float newSpeed = Mathf.Min(currentSpeed + speedGain, targetSpeed);
            rigidBody.velocity = forward * newSpeed;
        }

        // --- Apply Braking ---
        else if (brakeForce > 0f) {
            float deceleration = brakeForce * 3f; // Tune this multiplier
            float speedDrop = deceleration * Time.fixedDeltaTime;
            float newSpeed = Mathf.Max(currentSpeed - speedDrop, 0f);
            rigidBody.velocity = currentDir * newSpeed;
        }

        // --- Maintain constant speed if no input ---
        else {
            rigidBody.velocity = forward * targetSpeed;
        }
    }

    private void ApplyVisualTiltingCamera() {
        float visualTiltAngle = -(gameControllerScript.ITiltAngle) * gameControllerScript.visualTiltMultiplier * 1000f;

        // --- Clamp the tilt angle to supported limits ---
        visualTiltAngle = Mathf.Clamp(
            visualTiltAngle,
            -gameControllerScript.supportedAngle,
            gameControllerScript.supportedAngle
        );

        // --- TEST MODE: Force 45� left/right tilt based on sign ---
        if (gameControllerScript.currentVisualTiltingMode == GameControllerScript.VisualTiltingMode.TestMode) {
            if (gameControllerScript.ITiltAngle > 0)
                visualTiltAngle = -gameControllerScript.supportedAngle;
            else if (gameControllerScript.ITiltAngle < 0)
                visualTiltAngle = gameControllerScript.supportedAngle;
            else
                visualTiltAngle = 0f;
        }

        // --- Apply visual tilt to assigned target ---
        if (gameControllerScript.currentVisualTiltingMode != GameControllerScript.VisualTiltingMode.Disabled && visualTiltTarget != null) {
            Quaternion currentRot = visualTiltTarget.transform.localRotation;
            Quaternion targetRot = Quaternion.Euler(0f, 0f, visualTiltAngle);
            visualTiltTarget.transform.localRotation = Quaternion.Lerp(currentRot, targetRot, Time.deltaTime * gameControllerScript.visualTiltSpeed);
        }

        if (gameControllerScript.activateCalculationLogging) {
            Debug.Log("[I] visualTiltAngle: " + visualTiltAngle);
        }
    }


    private void MoveBikeAlongTurn() {
        float wheelbase = 1.5f;
        float turnRadius = wheelbase / (Mathf.Sin(Mathf.Abs(steeringAngle) * Mathf.Deg2Rad));


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

        float speedInMS = gameControllerScript.BikeSpeed / 3.6f;

        if (steeringAngle != 0 && turnRadius != Mathf.Infinity) // curve
        {
            transform.RotateAround(turningCenterCurve, Vector3.up, sign * ((speedInMS * 1f) / (2f * Mathf.PI * turnRadius) * 360f) * Time.deltaTime);
        } else {
            transform.position = transform.position + transform.forward * Time.deltaTime * speedInMS;
        }
    }
}


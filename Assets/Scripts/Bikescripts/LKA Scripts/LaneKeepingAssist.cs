using UnityEngine;

public class LaneKeepingAssist : MonoBehaviour {
    [Header("Script References")]
    public MLClosedSplineFrenet frenetSource;
    public BikeController bikeController;
    public LKAConfiguration config;

    [Header("Visual Indicator")]
    public Renderer bulbRenderer;
    public Light handlebarLight;

    [Header("LKA Status")]
    public bool lkaSwitchActive;
    public bool isEngaged = false;

    [Header("LKA Motor Outputs")]
    [Tooltip("Connect this pin to the ESCON 'Enable' Digital Input.")]
    public bool motorEnablePin = false;
    public bool motorDirection = true;
    public int motorPWM = 26;

    [Header("PID Controller Gains")]
    public float Kp = 15.0f;
    public float Ki = 0.5f;
    public float Kd = 5.0f;

    [Header("LKA Parameters")]
    public float minSpeedToEngage = 2.0f;
    public float maxHeadingAngle = 90.0f;

    [Header("Error Weights (Sum = 1.0)")]
    [Range(0f, 1f)] public float weightCrosstrack = 0.7f;
    [Range(0f, 1f)] public float weightHeading = 0.3f;

    [Header("Live PID Debug")]
    public float CurrentError;
    public float NormalizedCrosstrack;
    public float NormalizedHeading;
    private float integralError = 0f;
    private float lastError = 0f;

    private const int MIN_MOTOR_PWM = 25; // maxon motor 10% PWM threshold
    private const int MAX_MOTOR_PWM = 228; // maxon motor 90% PWM cap
    private bool wasActiveLastFrame = false;

    void Awake() {
        if (bikeController == null) bikeController = GetComponent<BikeController>() ?? FindObjectOfType<BikeController>();
        if (frenetSource == null) frenetSource = GetComponent<MLClosedSplineFrenet>() ?? FindObjectOfType<MLClosedSplineFrenet>();
    }

    private void OnValidate() {
        // keeps a*x + b*y logic balanced where a + b = 1.0
        weightHeading = 1.0f - weightCrosstrack;
    }

    void FixedUpdate() {
        UpdateCorrection();
    }

    public void UpdateCorrection() {
        lkaSwitchActive = ReceivedSerialProvider.LkaSwitchState;
        float currentSpeed = ReceivedSerialProvider.SpeedKmh;

        float trackWidth = config.trackWidthMeters;
        float trackHalfWidth = trackWidth / 2f;
        float deadZoneMeters = trackWidth * config.deadZonePercentage;

        bool fullyReady = lkaSwitchActive && frenetSource != null && currentSpeed >= minSpeedToEngage;

        if (!fullyReady) {
            HandleDisengagement();
            return;
        }

        float deviation = frenetSource.crossTrackError;

        // deadzone check: if inside, motor must be disabled to prevent holding torque
        if (Mathf.Abs(deviation) < deadZoneMeters) {
            UpdateVisuals(Color.yellow, true);
            SetMotorIdle(); // motorEnablePin = false
            return;
        }

        // normalize heading error (-1 to 1)
        NormalizedHeading = Mathf.Clamp(frenetSource.headingErrorDeg / maxHeadingAngle, -1f, 1f);

        // normalize crosstrack error (-1 to 1)
        NormalizedCrosstrack = config.GetNormalizedLKASteeringCrosstrackerror(deviation);

        // weighted total error, opposite signs cancel out for a smooth return
        CurrentError = (NormalizedCrosstrack * weightCrosstrack) + (NormalizedHeading * weightHeading);

        // PID calc
        float p = Kp * CurrentError;
        integralError = Mathf.Clamp(integralError + (CurrentError * Time.fixedDeltaTime), -2f, 2f);
        float i = Ki * integralError;
        float d = Kd * ((CurrentError - lastError) / Time.fixedDeltaTime);
        lastError = CurrentError;

        // output mapping
        float rawOutput = p + i + d;
        float steeringEffort = Mathf.Clamp(rawOutput, -1f, 1f);

        // PWM mapping
        motorEnablePin = true;
        isEngaged = true;
        motorDirection = steeringEffort > 0;
        motorPWM = Mathf.RoundToInt(Mathf.Lerp(MIN_MOTOR_PWM, MAX_MOTOR_PWM, Mathf.Abs(steeringEffort)));

        UpdateVisuals(Color.green, true);
        wasActiveLastFrame = true;
    }

    private void SetMotorIdle() {
        isEngaged = false;
        motorEnablePin = false; // disables the ESCON power stage  to let off holding torque
        motorPWM = MIN_MOTOR_PWM; // sets RPM to 0
        
        // reset all PID variabled
        CurrentError = 0f;
        integralError = 0f;
        lastError = 0f;
        NormalizedCrosstrack = 0f;
        NormalizedHeading = 0f;
    }

    private void HandleDisengagement() {
        if (wasActiveLastFrame) Debug.LogWarning("[LKA] Disengaged.");
        UpdateVisuals(Color.red, false);
        SetMotorIdle();
        wasActiveLastFrame = false;
    }

    private void UpdateVisuals(Color col, bool active) {
        if (bulbRenderer != null) {
            bulbRenderer.material.color = col;
            bulbRenderer.material.SetColor("_EmissionColor", col * (active ? 3.0f : 0.05f));
        }
        if (handlebarLight != null) {
            handlebarLight.color = col;
            handlebarLight.intensity = active ? 2.5f : 0.5f;
        }
    }
}
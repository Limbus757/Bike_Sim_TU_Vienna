using UnityEngine;

public class LaneKeepingAssistController : MonoBehaviour {
    [Header("References")]
    public MLClosedSplineFrenet frenetSource;
    public LKAConfiguration config;
    public GameObject eternityBike;
    private BikeController bikeController;

    [Header("Visuals")]
    public Renderer bulbRenderer;
    public Light handlebarLight;
    public string bulbName = "LKAIndicator";
    public string lightName = "IndicatorLight";

    [Header("Status & Output")]
    public bool lkaSwitchActive;
    public bool isEngaged = false;
    public bool SteeringMotorDirection = true;
    public int SteeringMotorPWM = 0;

    [Header("PID Gains")]
    public float Kp = 0.8f;
    public float Ki = 0.0f; // Keep for friction compensation
    public float Kd = 0.4f;
    [Range(0.01f, 1f)] public float derivativeSmoothing = 0.1f;

    [Header("Strategy Weights")]
    [Tooltip("Strength of pull at the lane edge.")]
    [Range(0f, 1f)] public float maxCrosstrackWeight = 0.7f;
    [Tooltip("Strength of alignment at the center handover.")]
    [Range(0f, 1f)] public float maxHeadingWeight = 1.0f;

    [Header("Debug")]
    public float CurrentError;
    private float integralError = 0f;
    private float smoothedDerivative = 0f;
    private float lastWheelHeading = 0f;
    private bool wasActiveLastFrame = false;

    void Awake() {
        if (eternityBike == null) eternityBike = GameObject.Find("EternityBike");
        if (eternityBike != null) bikeController = eternityBike.GetComponent<BikeController>();
        if (frenetSource == null) frenetSource = FindObjectOfType<MLClosedSplineFrenet>();
        if (config == null) config = FindObjectOfType<LKAConfiguration>();

        SetupVisuals();
    }

    void FixedUpdate() => UpdateCorrection();

    public void UpdateCorrection() {
        lkaSwitchActive = ReceivedSerialProvider.LkaSwitchState;

        if (!CanEngageLKA()) {
            HandleHardwareNotReady();
            return;
        }

        // Always track the 'Wheel Heading' (Frame Angle + Steering Angle)
        float currentWheelHeading = frenetSource.dynamicHeadingErrorDegrees + bikeController.SteeringAngle;

        // 1. Deadzone Check (0 Correction / Full Rider Freedom)
        if (!config.isWithinActiveZone) {
            SetMotorDisabled();
            lastWheelHeading = currentWheelHeading;
            UpdateVisuals(Color.yellow, 0.5f, true);
            return;
        }

        // 2. Strategy: Calculate Blended Error (Flare Logic)
        // lkaCrossTrackErrorNormalized is 0 at deadzone and 1 at lane edge.
        CurrentError = CalculateBlendedError();

        // 3. PID: Including Integral and Wheel-Based Damping
        float steeringEffort = RunPID(CurrentError, currentWheelHeading);
        float effortMagnitude = Mathf.Abs(steeringEffort);

        // 4. Hardware Output
        ApplyHardwareOutput(steeringEffort, effortMagnitude);

        UpdateVisuals(Color.green, 1.0f + (effortMagnitude * 4.0f), true);
        wasActiveLastFrame = true;
    }

    private float CalculateBlendedError() {
        float absCTE = Mathf.Abs(config.lkaCrossTrackErrorNormalized);

        // Pull vs Flare:
        // Focuses 100% on heading alignment (FLARE) as we reach the deadzone edge.
        float currentCrosstrackWeight = absCTE * maxCrosstrackWeight;
        // float currentCrosstrackWeight = Mathf.Sqrt(absCTE) * maxCrosstrackWeight;
        float currentHeadingWeight = Mathf.Lerp(1.0f, maxHeadingWeight, absCTE);

        float currentHeadingError = frenetSource.dynamicHeadingErrorDegrees;
        float normHeading = Mathf.Clamp(currentHeadingError / 45f, -1f, 1f);

        float distanceEffort = config.lkaCrossTrackErrorNormalized * currentCrosstrackWeight;
        float headingEffort = normHeading * currentHeadingWeight;

        return distanceEffort + headingEffort;
    }

    private float RunPID(float error, float currentWheelHeading) {
        // Proportional
        float p = Kp * error;

        // Integral: Accumulated only while outside deadzone
        // Clamped to prevent wind-up which could slam the motor
        integralError = Mathf.Clamp(integralError + (error * Time.fixedDeltaTime), -0.5f, 0.5f);
        float i = Ki * integralError;

        // Derivative (Wheel-Based Damping)
        float wheelRate = (currentWheelHeading - lastWheelHeading) / Time.fixedDeltaTime;
        lastWheelHeading = currentWheelHeading;

        smoothedDerivative = Mathf.Lerp(smoothedDerivative, wheelRate / 100f, derivativeSmoothing);
        float d = Kd * smoothedDerivative;

        return Mathf.Clamp(p + i + d, -1f, 1f);
    }

    private void ApplyHardwareOutput(float effort, float magnitude) {
        isEngaged = true;
        SteeringMotorDirection = effort > 0;

        float targetPWM = Mathf.Lerp(config.SteeringPwmMinLimit, config.SteeringPwmMaxLimit, magnitude);

        // Slew Rate: Move toward target to protect gears
        SteeringMotorPWM = Mathf.RoundToInt(Mathf.MoveTowards(SteeringMotorPWM, targetPWM, 5000f * Time.fixedDeltaTime));
    }

    private void SetMotorDisabled() {
        isEngaged = false;
        float idleVal = (config != null) ? config.SteeringPwmMinLimit : 410;
        SteeringMotorPWM = Mathf.RoundToInt(idleVal);

        // Reset PID states so they don't jump when we exit the deadzone again
        integralError = 0f;
        smoothedDerivative = 0f;
        CurrentError = 0f;
    }

    // ... (CanEngageLKA, HandleHardwareNotReady, Visuals/Setup methods remain same)
    private bool CanEngageLKA() {
        if (!lkaSwitchActive) return false;
        if (bikeController != null && bikeController.BikeSpeed < config.minSpeedToEngage) return false;
        if (frenetSource == null || config == null) return false;
        return true;
    }

    private void HandleHardwareNotReady() {
        if (wasActiveLastFrame) Debug.LogWarning("LKA Disengaged.");
        UpdateVisuals(Color.red, 1.0f, true);
        SetMotorDisabled();
        wasActiveLastFrame = false;
    }

    private void UpdateVisuals(Color col, float intensity, bool active) {
        if (bulbRenderer != null) {
            bulbRenderer.material.color = col;
            bulbRenderer.material.SetColor("_EmissionColor", col * (active ? (1.5f + intensity) : 0.05f));
        }
        if (handlebarLight != null) {
            handlebarLight.color = col;
            handlebarLight.intensity = active ? (0.5f + intensity) : 0.0f;
        }
    }

    private void SetupVisuals() {
        if (bulbRenderer == null) {
            GameObject bulbObj = GameObject.Find(bulbName);
            if (bulbObj != null) bulbRenderer = bulbObj.GetComponent<Renderer>();
        }
        if (handlebarLight == null) {
            GameObject lightObj = GameObject.Find(lightName);
            if (lightObj != null) handlebarLight = lightObj.GetComponent<Light>();
        }
    }
}
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
    public float Kp = 0.6f;
    public float Ki = 0.0f;
    public float Kd = 0.2f;
    [Range(0.01f, 1f)] public float derivativeSmoothing = 0.01f;

    [Header("Advanced Blending (Linear Ramp)")]
    [Tooltip("Strength of pull at the lane edge.")]
    [Range(0f, 1f)] public float maxCrosstrackWeight = 0.6f;
    [Tooltip("Strength of alignment at the center. 1.0 is recommended.")]
    [Range(0f, 1f)] public float maxHeadingWeight = 0.9f;

    [Header("Debug")]
    public float CurrentError;
    private float integralError = 0f;
    private float smoothedDerivative = 0f;
    private float lastHeadingError = 0f;
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

        if (!config.isWithinActiveZone) {
            SetMotorDisabled();
            UpdateVisuals(Color.yellow, 0.5f, true);
            return;
        }

        // 1. Calculate Error using the Linear Ramp (No Window)
        CurrentError = CalculateBlendedError();

        // 2. Run PID with Derivative Smoothing but raw output
        float steeringEffort = RunPID(CurrentError);
        float effortMagnitude = Mathf.Abs(steeringEffort);

        // 3. Apply Direct Hardware Output (No PWM Slewing or Soft-Start)
        ApplyHardwareOutput(steeringEffort, effortMagnitude);

        UpdateVisuals(Color.green, 1.0f + (effortMagnitude * 4.0f), true);
        wasActiveLastFrame = true;
    }

    private float CalculateBlendedError() {
        float rawNormCTE = config.lkaCrossTrackErrorNormalized;
        float absCTE = Mathf.Abs(rawNormCTE);

        float currentHeadingError = frenetSource.dynamicHeadingErrorDegrees;
        float normHeading = Mathf.Clamp(currentHeadingError / 60f, -1f, 1f);

        // Linear Ramp: blendFactor is 0 at center, 1 at lane edge.
        float blendFactor = Mathf.Clamp01(absCTE);

        // Weights transition smoothly over the whole lane
        // This gives the 1:54 motor more time to rotate back to zero.
        float currentCrosstrackWeight = Mathf.Lerp(0f, maxCrosstrackWeight, blendFactor);
        float currentHeadingWeight = Mathf.Lerp(maxHeadingWeight, 1f - maxCrosstrackWeight, blendFactor);

        float distanceEffort = rawNormCTE * currentCrosstrackWeight;
        float headingEffort = normHeading * currentHeadingWeight;

        return distanceEffort + headingEffort;
    }

    private float RunPID(float error) {
        float p = Kp * error;

        integralError = Mathf.Clamp(integralError + (error * Time.fixedDeltaTime), -0.5f, 0.5f);

        float currentHeading = frenetSource.dynamicHeadingErrorDegrees;
        float rawHeadingRate = (currentHeading - lastHeadingError) / Time.fixedDeltaTime;
        lastHeadingError = currentHeading;

        // Smooth the derivative to prevent motor chatter, but keep it responsive
        smoothedDerivative = Mathf.Lerp(smoothedDerivative, rawHeadingRate / 100f, derivativeSmoothing);

        return Mathf.Clamp(p + (Ki * integralError) + (Kd * smoothedDerivative), -1f, 1f);
    }

    private void ApplyHardwareOutput(float effort, float magnitude) {
        isEngaged = true;
        SteeringMotorDirection = effort > 0;

        // Direct mapping to PWM. The 1:54 gears handle the physical smoothing.
        float targetPWM = Mathf.Lerp(config.SteeringPwmMinLimit, config.SteeringPwmMaxLimit, magnitude);

        SteeringMotorPWM = Mathf.RoundToInt(targetPWM);
    }

    private bool CanEngageLKA() {
        if (!lkaSwitchActive) return false;
        if (bikeController != null && bikeController.BikeSpeed < config.minSpeedToEngage) return false;
        if (frenetSource == null || config == null) return false;
        return true;
    }

    private void SetMotorDisabled() {
        isEngaged = false;
        float idleVal = (config != null) ? config.SteeringPwmMinLimit : 25f;
        SteeringMotorPWM = Mathf.RoundToInt(idleVal);
        integralError = 0f;
        smoothedDerivative = 0f;
        CurrentError = 0f;
        if (frenetSource != null) lastHeadingError = frenetSource.dynamicHeadingErrorDegrees;
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
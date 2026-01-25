using UnityEngine;

public class LaneKeepingAssistController : MonoBehaviour {
    [Header("Hardware Config")]
    [Tooltip("Toggle this if the motor turns left when it should turn right.")]
    public bool invertMotorDirection = false;

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
    public float Ki = 0.0f;
    public float Kd = 0.4f;
    [Range(0.01f, 1f)] public float derivativeSmoothing = 0.1f;

    [Header("Strategy Weights")]
    [Range(0f, 1f)] public float maxCrosstrackWeight = 0.7f;
    [Range(0f, 1f)] public float maxHeadingWeight = 1.0f;

    [Header("Debug")]
    public float CurrentError;
    private float integralError = 0f;
    private float smoothedDerivative = 0f;
    private float lastSteerAngle = 0f;
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
            lastSteerAngle = bikeController.SteeringAngle;
            UpdateVisuals(Color.yellow, 0.5f, true);
            return;
        }

        CurrentError = CalculateBlendedError();

        // 1. Run PID
        float steeringEffort = RunPID(CurrentError, bikeController.SteeringAngle);

        // 2. Apply Inversion Toggle
        if (invertMotorDirection) steeringEffort *= -1f;

        float effortMagnitude = Mathf.Abs(steeringEffort);

        // 3. Hardware Output
        ApplyHardwareOutput(steeringEffort, effortMagnitude);

        UpdateVisuals(Color.green, 1.0f + (effortMagnitude * 4.0f), true);
        wasActiveLastFrame = true;
    }

    private float CalculateBlendedError() {
        float absCTE = Mathf.Abs(config.lkaCrossTrackErrorNormalized);

        // Position Error: Left is -1.0
        float distanceError = config.lkaCrossTrackErrorNormalized;

        // Heading Error: Left is -Deg
        float headError = frenetSource.wheelHeadingErrorDegrees;

        float currentCrosstrackWeight = absCTE * maxCrosstrackWeight;
        float currentHeadingWeight = Mathf.Lerp(1.0f, maxHeadingWeight, absCTE);

        float normHeading = Mathf.Clamp(headError / 45f, -1f, 1f);

        return (distanceError * currentCrosstrackWeight) + (normHeading * currentHeadingWeight);
    }

    private float RunPID(float error, float currentSteerAngle) {
        float p = Kp * error;

        integralError = Mathf.Clamp(integralError + (error * Time.fixedDeltaTime), -0.5f, 0.5f);
        float i = Ki * integralError;

        float steerVelocity = (currentSteerAngle - lastSteerAngle) / Time.fixedDeltaTime;
        lastSteerAngle = currentSteerAngle;

        smoothedDerivative = Mathf.Lerp(smoothedDerivative, steerVelocity / 100f, derivativeSmoothing);

        // D-term damping: resists movement. 
        // Note: This must oppose the direction of motion relative to the motor's polarity.
        float d = -Kd * smoothedDerivative;

        return Mathf.Clamp(p + i + d, -1f, 1f);
    }

    private void ApplyHardwareOutput(float effort, float magnitude) {
        isEngaged = true;
        SteeringMotorDirection = effort > 0;

        float targetPWM = Mathf.Lerp(config.SteeringPwmMinLimit, config.SteeringPwmMaxLimit, magnitude);
        SteeringMotorPWM = Mathf.RoundToInt(Mathf.MoveTowards(SteeringMotorPWM, targetPWM, 5000f * Time.fixedDeltaTime));
    }

    private void SetMotorDisabled() {
        isEngaged = false;
        SteeringMotorPWM = Mathf.RoundToInt(config != null ? config.SteeringPwmMinLimit : 410);
        integralError = 0f;
        smoothedDerivative = 0f;
        CurrentError = 0f;
    }

    private bool CanEngageLKA() {
        if (!lkaSwitchActive) return false;
        if (bikeController != null && bikeController.BikeSpeedKmh < config.minSpeedToEngage) return false;
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
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
    public float Kp = 0.5f;
    public float Ki = 0.006f;
    public float Kd = 0.2f;
    [Range(0.01f, 1f)] public float derivativeSmoothing = 0.1f;

    [Header("Strategy Weights")]
    [Range(0f, 2f)] public float maxCrosstrackWeight = 0.6f;
    [Range(0f, 2f)] public float maxHeadingWeight = 1.2f;

    private float maxExpectedError = 1.8f;

    [Header("Debug")]
    public float CurrentError;
    public float CurrentEffort;
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
        CurrentEffort = RunPID(CurrentError, bikeController.SteeringAngle);

        // 2. Apply Inversion Toggle
        if (invertMotorDirection) CurrentEffort *= -1f;

        float effortMagnitude = Mathf.Abs(CurrentEffort);

        // 3. Hardware Output
        ApplyHardwareOutput(CurrentEffort, effortMagnitude);

        UpdateVisuals(Color.green, 1.0f + (effortMagnitude * 4.0f), true);
        wasActiveLastFrame = true;
    }

    private float CalculateBlendedError() {
        // distance from center (-1 to 1)
        float distanceError = config.lkaCrossTrackErrorNormalized;
        float absCTE = Mathf.Abs(distanceError);

        // wheel alignment error (degrees) from the handlebar probe
        float wheelError = frenetSource.wheelHeadingErrorDegrees;

        // normalize: 45 degrees of wheel offset = 1.0 error
        float normWheelHeading = Mathf.Clamp(wheelError / 90f, -1f, 1f);

        // dynamic Weighting, closer to the center, we care less about distance and more about the wheel being straight.
        float currentCrosstrackWeight = Mathf.Pow(absCTE, 1.1f) * maxCrosstrackWeight;
        float currentHeadingWeight = maxHeadingWeight; // Keep this high (e.g., 1.5 - 2.0)

        return (distanceError * currentCrosstrackWeight) + (normWheelHeading * currentHeadingWeight);
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
        SteeringMotorPWM = Mathf.RoundToInt(Mathf.MoveTowards(SteeringMotorPWM, targetPWM, 8000f * Time.fixedDeltaTime));
    }

    private void SetMotorDisabled() {
        isEngaged = false;
        SteeringMotorPWM = Mathf.RoundToInt(config != null ? config.SteeringPwmMinLimit : 410);
        integralError = 0f;
        smoothedDerivative = 0f;
        CurrentError = 0f;
        if (bikeController != null) lastSteerAngle = bikeController.SteeringAngle;
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
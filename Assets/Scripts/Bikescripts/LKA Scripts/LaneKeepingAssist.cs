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
    public float Kp = 0.2f;
    public float Ki = 0.01f;
    public float Kd = 0.05f;
    [Range(0.01f, 1f)] public float derivativeSmoothing = 0.1f; // 1.0 = no smoothing

    [Header("LKA Parameters")]
    public float minSpeedToEngage = 2.0f;
    public float maxHeadingAngle = 90.0f;

    [Header("Error Weights (Sum = 1.0)")]
    [Range(0f, 1f)] public float weightCrosstrack = 0.8f;
    [Range(0f, 1f)] public float weightHeading = 0.2f;

    [Header("Live PID Debug")]
    public float CurrentError;
    public float NormalizedCrosstrack;
    public float NormalizedHeading;
    private float integralError = 0f;
    private float lastHeadingError = 0f;
    private float smoothedDerivative = 0f;

    private const int MIN_MOTOR_PWM = 25; // maxon motor 10% PWM 
    private const int MAX_MOTOR_PWM = 228; // maxon motor 90% PWM
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

        // Check if system is ready
        bool fullyReady = lkaSwitchActive && frenetSource != null && currentSpeed >= minSpeedToEngage;
        if (!fullyReady) {
            HandleDisengagement();
            return;
        }

        // normalize heading & cross track error (-1 to 1)
        NormalizedCrosstrack = config.lkaCrossTrackErrorNormalized;
        NormalizedHeading = Mathf.Clamp(frenetSource.headingErrorDegrees / 90f, -1f, 1f);

        // weighted total error
        CurrentError = (NormalizedCrosstrack * weightCrosstrack) + (NormalizedHeading * weightHeading);

        float p = Kp * CurrentError;

        integralError = Mathf.Clamp(integralError + (CurrentError * Time.fixedDeltaTime), -1f, 1f);
        float i = Ki * integralError;

        // PID - Derivative (Calculated on heading change for dampening)
        float rawDerivative = (frenetSource.headingErrorDegrees - lastHeadingError) / Time.fixedDeltaTime;
        smoothedDerivative = Mathf.Lerp(smoothedDerivative, rawDerivative, derivativeSmoothing);
        float d = Kd * smoothedDerivative;
        lastHeadingError = frenetSource.headingErrorDegrees;

        // deadzone check - Disable motor drive but keep PID "warm"
        if (Mathf.Abs(NormalizedCrosstrack) == 0) {
            UpdateVisuals(Color.yellow, true);
            motorEnablePin = false; // releases torque
            motorPWM = MIN_MOTOR_PWM;
            isEngaged = false;
            return;
        }

        // output mapping (Subtracting D dampens the rotation speed)
        float rawOutput = p + i - d;
        float steeringEffort = Mathf.Clamp(rawOutput, -1f, 1f);

        // PWM mapping with Power Curve for smoother response in the air
        motorEnablePin = true;
        isEngaged = true;
        motorDirection = steeringEffort > 0;

        float effortMagnitude = Mathf.Abs(steeringEffort);
        float curvedEffort = effortMagnitude * effortMagnitude; // quadratic curve

        motorPWM = Mathf.RoundToInt(Mathf.Lerp(MIN_MOTOR_PWM, MAX_MOTOR_PWM, curvedEffort));

        UpdateVisuals(Color.green, true);
        wasActiveLastFrame = true;
    }

    private void SetMotorIdle() {
        isEngaged = false;
        motorEnablePin = false; // disables power stage
        motorPWM = MIN_MOTOR_PWM;

        // Full reset of PID memory for hard stops/disengagement
        CurrentError = 0f;
        integralError = 0f;
        lastHeadingError = 0f;
        smoothedDerivative = 0f;
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
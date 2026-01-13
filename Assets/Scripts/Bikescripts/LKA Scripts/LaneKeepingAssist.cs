using UnityEngine;

public class LaneKeepingAssist : MonoBehaviour {
    [Header("Script References")]
    public MLClosedSplineFrenet frenetSource;
    public BikeController bikeController;

    [Header("Visual Indicator")]
    public Renderer bulbRenderer;
    public Light handlebarLight;

    [Header("LKA Status")]
    public bool lkaSwitchActive;
    public bool isEngaged = false;

    [Header("Safety Limits")]
    [Tooltip("The maximum PWM allowed to protect the belt. Start low (e.g. 100).")]
    [Range(26, 255)]
    public int maxAllowedPWM = 120;

    [Header("LKA Motor Outputs")]
    public bool motorEnablePin = false;
    public bool motorDirection = true;
    public int motorPWM = 26;

    [Header("PID Controller Gains")]
    public float Kp = 50.0f;
    public float Ki = 0.5f;
    public float Kd = 5.0f;

    [Header("LKA Parameters")]
    public float headingErrorMultiplier = 1.0f;
    public float minSpeedToEngage = 2.0f;

    [Header("Live PID Debug")]
    public float CurrentError;
    private float integralError = 0f;
    private float lastError = 0f;

    private const int MIN_PWM = 26;
    private bool wasActiveLastFrame = false;

    void Awake() {
        if (bikeController == null) bikeController = GetComponent<BikeController>() ?? FindObjectOfType<BikeController>();
        if (frenetSource == null) frenetSource = GetComponent<MLClosedSplineFrenet>() ?? FindObjectOfType<MLClosedSplineFrenet>();
    }

    void FixedUpdate() {
        UpdateCorrection();
    }

    public void UpdateCorrection() {
        lkaSwitchActive = ReceivedSerialProvider.LkaSwitchState;
        float currentSpeed = ReceivedSerialProvider.SpeedKmh;

        float trackWidth = LKAConfiguration.TrackWidthMeters;
        float deadZoneMeters = trackWidth * LKAConfiguration.DeadZonePercentage;

        bool fullyReady = lkaSwitchActive &&
                          frenetSource != null &&
                          bikeController != null &&
                          currentSpeed >= minSpeedToEngage;

        float deviation = frenetSource.crossTrackError;

        if (!fullyReady) {
            if (wasActiveLastFrame) {
                string reason = !lkaSwitchActive ? "LKA Switch is OFF." :
                                (frenetSource == null || bikeController == null) ? "Critical reference is missing." :
                                $"Speed ({currentSpeed:F1} km/h) is below min threshold.";
                Debug.LogWarning($"[LKA] Disengaged: {reason}");
            }

            UpdateVisuals(Color.red, false);
            SetMotorIdle();
            wasActiveLastFrame = false;
            return;
        } else {
            wasActiveLastFrame = true;

            if (Mathf.Abs(deviation) < deadZoneMeters) {
                UpdateVisuals(Color.yellow, true);
                SetMotorIdle();
                return;
            } else {
                // Error calculation
                CurrentError = deviation + (frenetSource.headingErrorDeg * headingErrorMultiplier);

                // PID Logic
                float p = Kp * CurrentError;

                integralError += CurrentError * Time.fixedDeltaTime;
                // ANTI-WINDUP: Keep integral from building too much torque
                integralError = Mathf.Clamp(integralError, -2f, 2f);
                float i = Ki * integralError;

                float d = Kd * ((CurrentError - lastError) / Time.fixedDeltaTime);
                lastError = CurrentError;

                // TOTAL OUTPUT - Normalized mapping to prevent belt damage
                float rawOutput = p + i + d;
                float steeringEffort = Mathf.Clamp(rawOutput, -1f, 1f);

                motorEnablePin = true;
                isEngaged = true;
                motorDirection = steeringEffort > 0;

                // SAFELY MAP PWM
                motorPWM = Mathf.RoundToInt(Mathf.Lerp(MIN_PWM, maxAllowedPWM, Mathf.Abs(steeringEffort)));

                UpdateVisuals(Color.green, true);

                if (Time.frameCount % 10 == 0)
                    Debug.Log($"[LKA] ACTIVE: PWM={motorPWM}, Effort={steeringEffort:F2}, Error={CurrentError:F3}");
            }
        }
    }

    private void SetMotorIdle() {
        isEngaged = false;
        motorEnablePin = false;
        motorPWM = MIN_PWM;
        integralError = 0f;
        lastError = 0f;
    }

    private void UpdateVisuals(Color col, bool active) {
        if (bulbRenderer != null) {
            Material mat = bulbRenderer.material;
            mat.color = col;
            mat.SetColor("_EmissionColor", col * (active ? 3.0f : 0.05f));
            mat.EnableKeyword("_EMISSION");
        }
        if (handlebarLight != null) {
            handlebarLight.color = col;
            handlebarLight.intensity = active ? 2.5f : 0.5f;
        }
    }
}
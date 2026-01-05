using UnityEngine;

public class LaneKeepingAssist : MonoBehaviour {
    [Header("Script References")]
    public MLClosedSplineFrenet frenetSource;
    private BikeController bikeController;

    [Header("Visual Indicator (Handlebar Bulb)")]
    [Tooltip("The small sphere mesh on the handlebar.")]
    public Renderer bulbRenderer;
    [Tooltip("The Point Light inside the sphere.")]
    public Light handlebarLight;

    [Header("LKA Status (Public)")]
    public bool lkaSwitchActive;
    public bool isEngaged = false;

    [Header("LKA Motor Outputs")]
    public bool motorDirection = true; // true = Right, false = Left
    public int motorPWM = 26;

    [Header("PID Controller Gains")]
    public float Kp = 10.0f;
    public float Ki = 0.1f;
    public float Kd = 1.0f;

    [Header("LKA Parameters")]
    public float lkaDeadZone = 0.3f;
    public float headingErrorGain = 1.5f;
    public float minSpeedToEngage = 2.0f;

    [Header("Live PID Debug")]
    public float CurrentError;
    private float integralError = 0f;
    private float lastError = 0f;

    private const int MIN_PWM = 26;
    private const int MAX_PWM = 230;

    void Awake() {
        bikeController = GetComponent<BikeController>();
        if (frenetSource == null) frenetSource = GetComponent<MLClosedSplineFrenet>();
    }

    public void UpdateCorrection() {
        lkaSwitchActive = ReceivedSerialProvider.LkaSwitchState;

        // Condition check for engagement
        bool canEngage = lkaSwitchActive &&
                         frenetSource != null &&
                         bikeController != null &&
                         bikeController.BikeSpeed >= minSpeedToEngage;

        if (!canEngage) {
            UpdateVisuals(Color.red, false);
            StopMotor();
            return;
        }

        float deviation = frenetSource.crossTrackError;

        // Dead Zone check
        if (Mathf.Abs(deviation) < lkaDeadZone) {
            UpdateVisuals(Color.red, false);
            StopMotor();
            return;
        }

        // --- ACTIVE ENGAGEMENT ---
        isEngaged = true;
        UpdateVisuals(Color.green, true);

        CurrentError = deviation + (frenetSource.headingErrorDeg * (headingErrorGain * 0.01f));

        // PID Logic
        float p = Kp * CurrentError;
        integralError += CurrentError * Time.deltaTime;
        float i = Ki * integralError;
        float d = Kd * ((CurrentError - lastError) / Time.deltaTime);
        lastError = CurrentError;

        float rawOutput = p + i + d;
        motorDirection = rawOutput > 0;
        motorPWM = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(rawOutput)), MIN_PWM, MAX_PWM);
    }

    private void UpdateVisuals(Color col, bool active) {
        // Change the color of the sphere bulb
        if (bulbRenderer != null) {
            bulbRenderer.material.color = col;
            // Optional: make it "glow" using Emission if your material supports it
            bulbRenderer.material.SetColor("_EmissionColor", col * (active ? 2f : 0.5f));
        }

        // Set the light color and brightness
        if (handlebarLight != null) {
            handlebarLight.color = col;
            handlebarLight.intensity = active ? 2.0f : 0.5f; // Dimmer when red/inactive
        }
    }

    private void StopMotor() {
        isEngaged = false;
        motorPWM = MIN_PWM;
        integralError = 0;
        lastError = 0;
    }
}
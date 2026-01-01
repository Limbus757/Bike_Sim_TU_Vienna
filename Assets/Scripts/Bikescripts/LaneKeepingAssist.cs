using UnityEngine;

public class LaneKeepingAssist : MonoBehaviour {
    [Header("Script References")]
    public MLClosedSplineFrenet frenetSource;
    private BikeController bikeController;

    [Header("LKA Status (Public)")]
    [Tooltip("Reflects the physical switch state from the Serial Provider.")]
    public bool lkaSwitchActive;
    [Tooltip("True if the PID is actually sending a correction signal above the deadzone.")]
    public bool isEngaged = false;

    [Header("LKA Motor Outputs")]
    public bool motorDirection = true;
    public int motorPWM = 26; // Default to 10% (255 * 0.1)

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

    private const int MIN_PWM = 26;  // 10% of 255
    private const int MAX_PWM = 230; // 90% of 255

    void Awake() {
        bikeController = GetComponent<BikeController>();
        if (frenetSource == null) frenetSource = GetComponent<MLClosedSplineFrenet>();
    }

    public void UpdateCorrection() {
        // 1. Full Cutoff Switch Logic
        // Pull the static variable from the Serial Provider
        lkaSwitchActive = ReceivedSerialProvider.LkaSwitchState;

        if (!lkaSwitchActive) {
            StopMotor();
            return;
        }

        // 2. Safety Checks (Source & Speed)
        if (frenetSource == null || bikeController == null || bikeController.BikeSpeed < minSpeedToEngage) {
            StopMotor();
            return;
        }

        float deviation = frenetSource.crossTrackError;

        // 3. Dead Zone Check
        if (Mathf.Abs(deviation) < lkaDeadZone) {
            StopMotor();
            return;
        }

        // If we reached here, LKA is officially active and correcting
        isEngaged = true;

        CurrentError = deviation + (frenetSource.headingErrorDeg * (headingErrorGain * 0.01f));

        // PID Logic
        float p = Kp * CurrentError;
        integralError += CurrentError * Time.deltaTime;
        float i = Ki * integralError;
        float d = Kd * ((CurrentError - lastError) / Time.deltaTime);
        lastError = CurrentError;

        float rawOutput = p + i + d;

        motorDirection = rawOutput > 0;

        // Map and Clamp PWM
        int calculatedPWM = Mathf.RoundToInt(Mathf.Abs(rawOutput));
        motorPWM = Mathf.Clamp(calculatedPWM, MIN_PWM, MAX_PWM);
    }

    private void StopMotor() {
        isEngaged = false;
        motorPWM = MIN_PWM; // Idle at 10% PWM
        integralError = 0;
        lastError = 0;
    }
}
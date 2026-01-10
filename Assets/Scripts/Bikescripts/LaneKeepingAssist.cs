using UnityEngine;

public class LaneKeepingAssist : MonoBehaviour
{
    [Header("Script References")]
    // ** MUST be assigned in Inspector if not on this object **
    [Tooltip("Reference to the script that calculates track error.")]
    public MLClosedSplineFrenet frenetSource;

    // ** MUST be assigned in Inspector if not on this object **
    [Tooltip("Reference to the script that controls the bike.")]
    public BikeController bikeController;

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
    [Tooltip("Minimum speed (m/s) required for LKA to actively correct steering.")]
    public float minSpeedToEngage = 2.0f;

    [Header("Live PID Debug")]
    public float CurrentError;
    private float integralError = 0f;
    private float lastError = 0f;

    private const int MIN_PWM = 26;
    private const int MAX_PWM = 230;

    // A flag to prevent the Disengaged log from spamming the console
    private bool wasActiveLastFrame = false;

    void Awake()
    {
        // --- 1. Find BikeController ---
        if (bikeController == null)
        {
            bikeController = GetComponent<BikeController>() ?? FindObjectOfType<BikeController>();
            if (bikeController == null)
            {
                Debug.LogError("[LKA] CRITICAL FAILURE: BikeController reference is missing.");
            }
        }

        // --- 2. Find FrenetSource ---
        if (frenetSource == null)
        {
            frenetSource = GetComponent<MLClosedSplineFrenet>() ?? FindObjectOfType<MLClosedSplineFrenet>();
            if (frenetSource == null)
            {
                Debug.LogError("[LKA] CRITICAL FAILURE: MLClosedSplineFrenet reference is missing.");
            }
        }

        // --- Visual Debug Checks ---
        if (bulbRenderer == null)
            Debug.LogWarning("[LKA] Bulb Renderer is not assigned. Visual feedback is disabled.");
        if (handlebarLight == null)
            Debug.LogWarning("[LKA] Handlebar Light is not assigned. Light feedback is disabled.");
    }

    /// <summary>
    /// Unity physics loop: Calls the LKA correction logic at a fixed interval.
    /// This is the missing piece that prevents the script from running.
    /// </summary>
    void FixedUpdate()
    {
        // This is where your logic must be called to run every physics step.
        UpdateCorrection();
    }

    public void UpdateCorrection()
    {
        // 1. Read the static switch state from the serial provider
        lkaSwitchActive = ReceivedSerialProvider.LkaSwitchState;
        // Use the static speed from the serial provider, but fall back to 0 if the reference is missing.
        float currentSpeed = ReceivedSerialProvider.SpeedKmh;

        // 2. Comprehensive check for LKA conditions (Switch ON, References present, and CRITICAL Speed Check)
        bool fullyReady = lkaSwitchActive &&
                          frenetSource != null &&
                          bikeController != null &&
                          currentSpeed >= minSpeedToEngage;

        // --- DISENGAGEMENT / IDLE LOGIC ---
        if (!fullyReady)
        {
            // Only log if the state has just changed from active to inactive
            if (wasActiveLastFrame)
            {
                string reason = "Unknown Error";
                if (!lkaSwitchActive) reason = "LKA Switch is OFF.";
                else if (frenetSource == null || bikeController == null) reason = "Critical reference is missing.";
                else if (currentSpeed < minSpeedToEngage) reason = $"Speed ({currentSpeed:F1} km/h) is below min threshold (convert to km/h if minSpeedToEngage is m/s)."; // Note: Debug speed unit is often km/h from serial

                Debug.LogWarning($"[LKA] Disengaged: {reason}");
            }

            UpdateVisuals(Color.red, false);
            StopMotor();
            wasActiveLastFrame = false;
            return;
        }

        // LKA is ON and ready to check deviation
        wasActiveLastFrame = true;

        float deviation = frenetSource.crossTrackError;

        // --- DEAD ZONE LOGIC ---
        if (Mathf.Abs(deviation) < lkaDeadZone)
        {
            UpdateVisuals(Color.yellow, true); // Active, but idle
            StopMotor();
            return;
        }

        // --- ACTIVE ENGAGEMENT ---
        isEngaged = true; // LKA is actively correcting!

        UpdateVisuals(Color.green, true);

        // Error calculation
        CurrentError = deviation + (frenetSource.headingErrorDeg * (headingErrorGain * 0.01f));

        // PID Logic
        float p = Kp * CurrentError;

        // Integral term reset on disengage, accumulated during engagement
        integralError += CurrentError * Time.fixedDeltaTime; // Use Time.fixedDeltaTime in FixedUpdate
        float i = Ki * integralError;

        // Derivative term calculation
        float deltaTime = Time.fixedDeltaTime; // Use Time.fixedDeltaTime
        float d = Kd * ((CurrentError - lastError) / deltaTime);

        lastError = CurrentError;

        float rawOutput = p + i + d;
        motorDirection = rawOutput > 0;

        // Clamp PWM to the defined motor limits
        motorPWM = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(rawOutput)), MIN_PWM, MAX_PWM);

        Debug.Log($"[LKA] ACTIVE: PWM={motorPWM}, Dir={(motorDirection ? "Right" : "Left")}, Error={CurrentError:F3}");
    }

    private void UpdateVisuals(Color col, bool active)
    {
        if (bulbRenderer != null)
        {
            Material mat = bulbRenderer.material;
            mat.color = col;
            Color emissionColor = col * (active ? 3.0f : 0.05f);
            mat.SetColor("_EmissionColor", emissionColor);
            mat.EnableKeyword("_EMISSION");
        }

        if (handlebarLight != null)
        {
            handlebarLight.color = col;
            handlebarLight.intensity = active ? 2.5f : 0.5f;
        }
    }

    private void StopMotor()
    {
        isEngaged = false;
        motorPWM = MIN_PWM;

        // Essential: Reset PID integral and differential terms on disengage
        integralError = 0f;
        lastError = 0f;
    }
}
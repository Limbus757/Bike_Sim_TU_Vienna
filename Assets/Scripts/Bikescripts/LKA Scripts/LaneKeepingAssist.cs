using UnityEngine;

public class LaneKeepingAssist : MonoBehaviour
{
    [Header("Script References")]
    [Tooltip("Reference to the script that calculates track error.")]
    public MLClosedSplineFrenet frenetSource;
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

    [Header("LKA Motor Outputs (Public for External Access)")]
    public bool motorEnablePin = false; 
    public bool motorDirection = true;  
    public int motorPWM = 26;           

    [Header("PID Controller Gains")]
    public float Kp = 50.0f;
    public float Ki = 0.5f;
    public float Kd = 5.0f;

    [Header("LKA Parameters")]
    [Tooltip("Multiplier for the heading error component in the total error calculation.")]
    public float headingErrorMultiplier = 1.0f;
    [Tooltip("Minimum speed (m/s) required for LKA to actively correct steering.")]
    public float minSpeedToEngage = 2.0f;

    [Header("Live PID Debug")]
    public float CurrentError;
    private float integralError = 0f;
    private float lastError = 0f;

    private const int MIN_PWM = 26;
    private const int MAX_PWM = 230;

    // flag to prevent the Disengaged log from spamming the console
    private bool wasActiveLastFrame = false;

    void Awake() {
        // Find BikeController
        if (bikeController == null) {
            bikeController = GetComponent<BikeController>() ?? FindObjectOfType<BikeController>();
            if (bikeController == null) {
                Debug.LogError("[LKA] CRITICAL FAILURE: BikeController reference is missing.");
            }
        }

        // Find FrenetSource
        if (frenetSource == null) {
            frenetSource = GetComponent<MLClosedSplineFrenet>() ?? FindObjectOfType<MLClosedSplineFrenet>();
            if (frenetSource == null) {
                Debug.LogError("[LKA] CRITICAL FAILURE: MLClosedSplineFrenet reference is missing.");
            }
        }

        // Visual Debug Checks
        if (bulbRenderer == null)
            Debug.LogWarning("[LKA] Bulb Renderer is not assigned. Visual feedback is disabled.");
        if (handlebarLight == null)
            Debug.LogWarning("[LKA] Handlebar Light is not assigned. Light feedback is disabled.");
    }

    void FixedUpdate() {
        UpdateCorrection();
    }

    public void UpdateCorrection() {
        lkaSwitchActive = ReceivedSerialProvider.LkaSwitchState;
        float currentSpeed = ReceivedSerialProvider.SpeedKmh; // Assumed to be in km/h

        // Get dead zone based on global config
        float trackWidth = LKAConfiguration.TrackWidthMeters;
        float deadZoneMeters = trackWidth * LKAConfiguration.DeadZonePercentage;

        // Check for LKA conditions (Switch ON, References present, and CRITICAL Speed Check)
        bool fullyReady = lkaSwitchActive &&
                          frenetSource != null &&
                          bikeController != null &&
                          currentSpeed >= minSpeedToEngage;

        float deviation = frenetSource.crossTrackError;

        
        if (!fullyReady) { // system NOT Ready (switch OFF, missing references, or not enough biking speed)
            if (wasActiveLastFrame)
            {
                string reason = !lkaSwitchActive ? "LKA Switch is OFF." :
                                (frenetSource == null || bikeController == null) ? "Critical reference is missing." :
                                $"Speed ({currentSpeed:F1} km/h) is below min threshold ({minSpeedToEngage} m/s).";
                Debug.LogWarning($"[LKA] Disengaged: {reason}");
            }

            UpdateVisuals(Color.red, false);
            SetMotorIdle(); // Sets enable pin to FALSE
            wasActiveLastFrame = false;
            return;
        } else { // System ready
            wasActiveLastFrame = true;

            if (Mathf.Abs(deviation) < deadZoneMeters) { // inside deadzone
                UpdateVisuals(Color.yellow, true);
                SetMotorIdle();
                return;
            } else { // outside deadzone
                     // Error calculation
                CurrentError = deviation + (frenetSource.headingErrorDeg * headingErrorMultiplier);

                // PID Logic
                float p = Kp * CurrentError;

                integralError += CurrentError * Time.fixedDeltaTime;
                float i = Ki * integralError;

                float deltaTime = Time.fixedDeltaTime;
                float d = Kd * ((CurrentError - lastError) / deltaTime);
                lastError = CurrentError;

                float rawOutput = p + i + d;

                motorEnablePin = true; // Sets enable pin to TRUE
                isEngaged = true; // LKA is actively correcting
                motorDirection = rawOutput > 0;
                motorPWM = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(rawOutput)), MIN_PWM, MAX_PWM);
                UpdateVisuals(Color.green, true);


                Debug.Log($"[LKA] ACTIVE: EN={motorEnablePin}, PWM={motorPWM}, Dir={(motorDirection ? "Right" : "Left")}, Error={CurrentError:F3}");
                
            }
        }
    }

    /// <summary>
    /// Sets public motor outputs to their inactive state.
    /// </summary>
    private void SetMotorIdle()
    {
        isEngaged = false;
        motorEnablePin = false; // CRITICAL: Disable the motor pin
        motorPWM = MIN_PWM;

        // Reset PID integral and differential terms on disengage/idle
        integralError = 0f;
        lastError = 0f;
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
}
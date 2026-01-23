/*using UnityEngine;

public class LaneKeepingAssistController : MonoBehaviour {
    [Header("Script References")]
    public MLClosedSplineFrenet frenetSource;
    public LKAConfiguration config;
    public GameObject eternityBike;
    private BikeController bikeController;

    [Header("Visual Indicators")]
    public Renderer bulbRenderer;
    public Light handlebarLight;
    public string bulbName = "LKAIndicator";
    public string lightName = "IndicatorLight";

    [Header("LKA Status & Output")]
    public bool lkaSwitchActive;
    public bool isEngaged = false;
    public bool motorDirection = true;
    public int motorPWM = 25;

    [Header("Hardware Limits (ESCON 1:54 Gearbox)")]
    public int IDLE_PWM = 25;
    public int lowestEffectivePWM = 55;
    public int ESCON_MAX_PWM = 228;
    public float maxPwmChangePerSecond = 800f; // Increased for better responsiveness

    [Header("PID Controller Gains")]
    public float Kp = 0.6f;
    public float Ki = 0.2f; // Small amount to clear constant offsets
    public float Kd = 0.25f; // Dampens the "oversteer" wobble
    [Range(0.01f, 1f)] public float derivativeSmoothing = 0.1f;

    [Header("LKA Weights")]
    [Range(0f, 1f)] public float weightCrosstrack = 0.8f;
    private float weightHeading;

    [Header("Live PID Debug")]
    public float CurrentError;
    private float integralError = 0f;
    private float lastError = 0f;
    private float smoothedDerivative = 0f;
    private float currentSmoothPWM = 25f;
    private bool wasActiveLastFrame = false;

    void Awake() {
        if (eternityBike == null) eternityBike = GameObject.Find("EternityBike");
        if (eternityBike != null) bikeController = eternityBike.GetComponent<BikeController>();

        if (frenetSource == null) frenetSource = FindObjectOfType<MLClosedSplineFrenet>();
        if (config == null) config = FindObjectOfType<LKAConfiguration>();

        SetupVisuals();
        currentSmoothPWM = IDLE_PWM;
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

    private void OnValidate() {
        weightHeading = 1.0f - weightCrosstrack;
    }

    void FixedUpdate() {
        UpdateCorrection();
    }

    public bool IsSpeedValid() {
        if (bikeController == null || config == null) return false;
        return bikeController.BikeSpeed >= config.minSpeedToEngage;
    }

    public void UpdateCorrection() {
        lkaSwitchActive = ReceivedSerialProvider.LkaSwitchState;

        // 1. HARDWARE SWITCH CHECK (The Red Light Gate)
        // If the physical switch is flipped off, we want the RED indicator.
        if (!lkaSwitchActive) {
            HandleDisengagement(); // This sets lights to RED and idles motor
            return;
        }

        // 2. SAFETY & STATE CHECK (The Yellow Light Gate)
        // If speed is too low or we are inside the deadzone thresholds set in Config
        bool hardwareReady = IsSpeedValid() && frenetSource != null && config != null;
        bool activeZone = config.isWithinActiveZone;

        if (!hardwareReady || !activeZone) {
            SetMotorIdle();
            UpdateVisualFeedback(0f); // This will set the light to YELLOW (Standby)
            wasActiveLastFrame = true; // System is "ready," just not currently steering
            return;
        }

        // 3. ERROR DATA
        float normCTE = config.lkaCrossTrackErrorNormalized;
        float normHeading = Mathf.Clamp(frenetSource.headingErrorDegrees / 90f, -1f, 1f);

        float previousError = CurrentError;
        CurrentError = (normCTE * weightCrosstrack) + (normHeading * weightHeading);

        // 4. PID CALCULATIONS
        float p = Kp * CurrentError;
        integralError = Mathf.Clamp(integralError + (CurrentError * Time.fixedDeltaTime), -0.5f, 0.5f);
        float i = Ki * integralError;

        float errorRate = (CurrentError - previousError) / Time.fixedDeltaTime;
        smoothedDerivative = Mathf.Lerp(smoothedDerivative, errorRate, derivativeSmoothing);
        float d = Kd * smoothedDerivative;

        // 5. MOTOR OUTPUT
        float steeringEffort = Mathf.Clamp(p + i + d, -1f, 1f);
        float effortMagnitude = Mathf.Abs(steeringEffort);

        isEngaged = true;
        motorDirection = steeringEffort > 0;

        // Soft-start ramp for crossing the threshold
        float softStart = Mathf.Clamp01(Mathf.Abs(normCTE) / 0.05f);
        float targetPWM = Mathf.Lerp(lowestEffectivePWM, ESCON_MAX_PWM, effortMagnitude * softStart);

        currentSmoothPWM = Mathf.MoveTowards(currentSmoothPWM, targetPWM, maxPwmChangePerSecond * Time.fixedDeltaTime);
        motorPWM = Mathf.RoundToInt(currentSmoothPWM);

        // 6. VISUALS (Green/Cyan)
        UpdateVisualFeedback(effortMagnitude);
        wasActiveLastFrame = true;
        lastError = CurrentError;
    }

    private void UpdateVisualFeedback(float effort) {
        Color finalColor;
        float finalIntensity;

        if (!isEngaged) {
            finalColor = Color.yellow; // Standby
            finalIntensity = 0.5f;
        } else {
            finalColor = Color.green; // Active (No Cyan interpolation)
                                      // Intensity scales with steering effort (1.0 to 5.0 range)
            finalIntensity = 1.0f + (effort * 4.0f);
        }
        UpdateVisuals(finalColor, finalIntensity, true);
    }

    private void HandleDisengagement() {
        if (wasActiveLastFrame) Debug.LogWarning("[LKA] Disengaged.");
        UpdateVisuals(Color.red, 1.0f, true);
        SetMotorIdle();
        wasActiveLastFrame = false;
    }

    private void SetMotorIdle() {
        isEngaged = false;
        motorPWM = IDLE_PWM;
        currentSmoothPWM = IDLE_PWM;
        integralError = 0f;
        smoothedDerivative = 0f;
        CurrentError = 0f;
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
}
*/


using UnityEngine;

public class LaneKeepingAssistController : MonoBehaviour {
    [Header("Script References")]
    public MLClosedSplineFrenet frenetSource;
    public LKAConfiguration config;
    public GameObject eternityBike;
    private BikeController bikeController;

    [Header("Visual Indicators")]
    public Renderer bulbRenderer;
    public Light handlebarLight;
    public string bulbName = "LKAIndicator";
    public string lightName = "IndicatorLight";

    [Header("LKA Status & Output")]
    public bool lkaSwitchActive;
    public bool isEngaged = false;
    public bool motorDirection = true;
    public int motorPWM = 25;

    [Header("Hardware Limits (ESCON 1:54 Gearbox)")]
    public int IDLE_PWM = 25;
    public int lowestEffectivePWM = 55;
    public int ESCON_MAX_PWM = 228;
    public float maxPwmChangePerSecond = 1200f; // High for snappy motor response

    [Header("PID Controller Gains")]
    public float Kp = 0.7f;
    public float Ki = 0.05f;
    public float Kd = 0.3f; // Damping based on heading rate
    [Range(0.01f, 1f)] public float derivativeSmoothing = 0.05f;

    [Header("LKA Weights & Smoothing")]
    [Range(0f, 1f)] public float weightCrosstrack = 0.95f;
    [Tooltip("Size of the window to start straightening out. Lower = sharper approach.")]
    public float approachSmoothingWindow = 0.3f;

    [Header("Live PID Debug")]
    public float CurrentError;
    private float integralError = 0f;
    private float smoothedDerivative = 0f;
    private float lastHeadingError = 0f;
    private float currentSmoothPWM = 25f;
    private bool wasActiveLastFrame = false;

    void Awake() {
        if (eternityBike == null) eternityBike = GameObject.Find("EternityBike");
        if (eternityBike != null) bikeController = eternityBike.GetComponent<BikeController>();

        if (frenetSource == null) frenetSource = FindObjectOfType<MLClosedSplineFrenet>();
        if (config == null) config = FindObjectOfType<LKAConfiguration>();

        SetupVisuals();
        currentSmoothPWM = IDLE_PWM;
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

    void FixedUpdate() {
        UpdateCorrection();
    }

    public bool IsSpeedValid() {
        if (bikeController == null || config == null) return false;
        return bikeController.BikeSpeed >= config.minSpeedToEngage;
    }

    public void UpdateCorrection() {
        lkaSwitchActive = ReceivedSerialProvider.LkaSwitchState;

        // 1. MASTER SWITCH CHECK (Red Light)
        if (!lkaSwitchActive) {
            HandleDisengagement();
            return;
        }

        // 2. SAFETY & ZONE CHECK (Yellow Light)
        bool hardwareReady = IsSpeedValid() && frenetSource != null && config != null;
        bool activeZone = config.isWithinActiveZone;

        if (!hardwareReady || !activeZone) {
            SetMotorIdle();
            UpdateVisualFeedback(0f);
            wasActiveLastFrame = true;
            return;
        }

        // 3. DATA ACQUISITION
        float normCTE = config.lkaCrossTrackErrorNormalized;
        float absCTE = Mathf.Abs(normCTE);

        float currentHeadingError = frenetSource.headingErrorDegrees;
        float normHeading = Mathf.Clamp(currentHeadingError / 90f, -1f, 1f);

        // 4. CALCULATE HEADING RATE (Front Wheel Rotation Speed)
        float rawHeadingRate = (currentHeadingError - lastHeadingError) / Time.fixedDeltaTime;
        lastHeadingError = currentHeadingError;

        // 5. DYNAMIC WEIGHTING (The "Ease-in")
        float approachFactor = Mathf.Clamp01(absCTE / approachSmoothingWindow);
        float dynamicWeightCTE = weightCrosstrack * approachFactor;
        float dynamicWeightHeading = 1.0f - dynamicWeightCTE;

        float previousError = CurrentError;
        CurrentError = (normCTE * dynamicWeightCTE) + (normHeading * dynamicWeightHeading);

        // 6. PID CALCULATIONS
        float p = Kp * CurrentError;

        // Integral with Anti-Windup
        integralError = Mathf.Clamp(integralError + (CurrentError * Time.fixedDeltaTime), -0.5f, 0.5f);
        float i = Ki * integralError;

        // Derivative (Damping based on calculated Heading Rate)
        smoothedDerivative = Mathf.Lerp(smoothedDerivative, rawHeadingRate / 100f, derivativeSmoothing);
        float d = Kd * smoothedDerivative;

        // 7. MOTOR OUTPUT
        float steeringEffort = Mathf.Clamp(p + i + d, -1f, 1f);
        float effortMagnitude = Mathf.Abs(steeringEffort);

        isEngaged = true;
        motorDirection = steeringEffort > 0;

        // 8. PWM MAPPING
        float boundaryRamp = Mathf.Clamp01(absCTE / 0.01f); // Sharp engagement
        float targetPWM = Mathf.Lerp(lowestEffectivePWM, ESCON_MAX_PWM, effortMagnitude * boundaryRamp);

        currentSmoothPWM = Mathf.MoveTowards(currentSmoothPWM, targetPWM, maxPwmChangePerSecond * Time.fixedDeltaTime);
        motorPWM = Mathf.RoundToInt(currentSmoothPWM);

        UpdateVisualFeedback(effortMagnitude);
        wasActiveLastFrame = true;
    }

    private void UpdateVisualFeedback(float effort) {
        if (!isEngaged) {
            UpdateVisuals(Color.yellow, 0.5f, true);
        } else {
            UpdateVisuals(Color.green, 1.0f + (effort * 4.0f), true);
        }
    }

    private void HandleDisengagement() {
        if (wasActiveLastFrame) Debug.LogWarning("[LKA] Disengaged (Switch Off).");
        UpdateVisuals(Color.red, 1.0f, true);
        SetMotorIdle();
        wasActiveLastFrame = false;
    }

    private void SetMotorIdle() {
        isEngaged = false;
        motorPWM = IDLE_PWM;
        currentSmoothPWM = IDLE_PWM;
        integralError = 0f;
        smoothedDerivative = 0f;
        CurrentError = 0f;
        // Sync heading to prevent derivative spikes on re-entry
        if (frenetSource != null) lastHeadingError = frenetSource.headingErrorDegrees;
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
}
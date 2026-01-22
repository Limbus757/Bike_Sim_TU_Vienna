using UnityEngine;
/*
public class LaneKeepingAssistController : MonoBehaviour {
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
    public float Kp = 0.6f;
    public float Ki = 0.05f;
    public float Kd = 0.00f;
    [Range(0.01f, 1f)] public float derivativeSmoothing = 0.1f; // 1.0 = no smoothing

    [Header("LKA Parameters")]
    public float minSpeedToEngage = 8f;
    public float maxHeadingAngle = 90.0f;

    [Header("Error Weights (Sum = 1.0)")]
    [Range(0f, 1f)] public float weightCrosstrack = 0.9f;
    [Range(0f, 1f)] public float weightHeading = 0.1f;

    [Header("Live PID Debug")]
    public float CurrentError;
    public float NormalizedCrosstrack;
    public float NormalizedHeading;
    private float integralError = 0f;
    private float lastHeadingError = 0f;
    private float smoothedDerivative = 0f;

    public int MIN_MOTOR_PWM = 25; // maxon motor 10% PWM 
    public int MAX_MOTOR_PWM = 228; // maxon motor 90% PWM
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
        
        motorPWM = Mathf.RoundToInt(Mathf.Lerp(55, MAX_MOTOR_PWM, effortMagnitude));

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
*/

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
    public string lightName = "InidicatorLight";

    [Header("LKA Status & Output")]
    public bool lkaSwitchActive;
    public bool isEngaged = false; 
    public bool motorDirection = true;
    public int motorPWM = 25;

    [Header("Hardware Limits (ESCON 1:54 Gearbox)")]
    public int IDLE_PWM = 25;                  
    public int lowestEffectivePWM = 55;        
    public int ESCON_MAX_PWM = 228;            
    public float maxPwmChangePerSecond = 400f; 

    [Header("Thresholds")]
    public float engagementThreshold = 0.05f;  
    public float disengagementThreshold = 0.02f;
    public float directionSwitchThreshold = 0.12f;

    [Header("PID Controller Gains")]
    public float Kp = 0.6f; 
    public float Ki = 0.00f;
    public float Kd = 0.2f; 
    [Range(0.01f, 1f)] public float derivativeSmoothing = 0.15f;

    [Header("LKA Parameters")]
    [Range(0f, 1f)] public float weightCrosstrack = 0.9f; 
    private float weightHeading; 

    [Header("Live PID Debug")]
    public float CurrentError;
    private float integralError = 0f;
    private float lastHeading = 0f; 
    private float smoothedDerivative = 0f;
    private float currentSmoothPWM = 25f; 
    private bool wasActiveLastFrame = false;
    private bool currentSteerDirection = true; 

    void Awake() {
        if (eternityBike == null) eternityBike = GameObject.Find("Eternitybike");
        if (eternityBike != null) bikeController = eternityBike.GetComponent<BikeController>();
        
        if (frenetSource == null) frenetSource = FindObjectOfType<MLClosedSplineFrenet>();
        if (config == null) config = FindObjectOfType<LKAConfiguration>();
        
        if (bulbRenderer == null) {
            GameObject bulbObj = GameObject.Find(bulbName);
            if (bulbObj != null) bulbRenderer = bulbObj.GetComponent<Renderer>();
        }
        if (handlebarLight == null) {
            GameObject lightObj = GameObject.Find(lightName);
            if (lightObj != null) handlebarLight = lightObj.GetComponent<Light>();
        }

        if (eternityBike != null) lastHeading = eternityBike.transform.eulerAngles.y;
        currentSmoothPWM = IDLE_PWM;
    }

    private void OnValidate() {
        weightHeading = 1.0f - weightCrosstrack;
    }

    void FixedUpdate() {
        UpdateCorrection();
    }

    public bool IsSpeedValid() {
        // Validation check using threshold from config and speed from Eternitybike
        if (bikeController == null || config == null) return false;
        return bikeController.BikeSpeed >= config.minSpeedToEngage;
    }

    public void UpdateCorrection() {
        lkaSwitchActive = ReceivedSerialProvider.LkaSwitchState;

        // Master Safety Gate
        bool hardwareReady = lkaSwitchActive && IsSpeedValid() && config != null && eternityBike != null;
        
        if (!hardwareReady) {
            HandleDisengagement();
            return;
        }

        // 1. DATA INPUTS
        float normCTE = config.lkaCrossTrackErrorNormalized; 
        float rawHeading = Mathf.Clamp(frenetSource.headingErrorDegrees / 90f, -1f, 1f);
        float normHeading = (Mathf.Abs(rawHeading) < 0.05f) ? 0f : rawHeading;
        
        CurrentError = (normCTE * weightCrosstrack) + (normHeading * weightHeading);

        // 2. PID CALCULATIONS
        float p = Kp * CurrentError;
        integralError = Mathf.Clamp(integralError + (CurrentError * Time.fixedDeltaTime), -1f, 1f);
        float i = Ki * integralError;

        float currentHeading = eternityBike.transform.eulerAngles.y;
        float headingRate = Mathf.DeltaAngle(lastHeading, currentHeading) / Time.fixedDeltaTime;
        smoothedDerivative = Mathf.Lerp(smoothedDerivative, headingRate, derivativeSmoothing);
        float d = -Kd * smoothedDerivative; 
        lastHeading = currentHeading;

        float steeringEffort = Mathf.Clamp(p + i + d, -1f, 1f);
        float effortMagnitude = Mathf.Abs(steeringEffort);

        // 3. DIRECTIONAL HYSTERESIS
        if (currentSteerDirection && steeringEffort < -directionSwitchThreshold) currentSteerDirection = false;
        else if (!currentSteerDirection && steeringEffort > directionSwitchThreshold) currentSteerDirection = true;

        // 4. MASTER ENGAGEMENT LOGIC
        if (!isEngaged && effortMagnitude > engagementThreshold) isEngaged = true;
        else if (isEngaged && effortMagnitude < disengagementThreshold) isEngaged = false;

        // 5. POWER MAPPING & SLEW
        float targetPWM = isEngaged ? Mathf.Lerp(lowestEffectivePWM, ESCON_MAX_PWM, effortMagnitude) : IDLE_PWM;
        if (isEngaged && currentSmoothPWM < lowestEffectivePWM) currentSmoothPWM = lowestEffectivePWM;
        currentSmoothPWM = Mathf.MoveTowards(currentSmoothPWM, targetPWM, maxPwmChangePerSecond * Time.fixedDeltaTime);
        
        // 6. OUTPUTS
        motorPWM = Mathf.RoundToInt(currentSmoothPWM);
        motorDirection = currentSteerDirection; 

        // 7. VISUAL FEEDBACK
        UpdateVisualFeedback();
        wasActiveLastFrame = true;
    }

    private void UpdateVisualFeedback() {
        Color finalColor;
        float finalIntensity;

        if (!isEngaged) {
            finalColor = Color.yellow; // Standby
            finalIntensity = 0.5f; 
        } else {
            float effortPercent = Mathf.InverseLerp(lowestEffectivePWM, ESCON_MAX_PWM, motorPWM);
            finalColor = Color.Lerp(Color.green, Color.cyan, effortPercent);
            finalIntensity = 1.0f + (effortPercent * 4.0f);
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
        if (eternityBike != null) lastHeading = eternityBike.transform.eulerAngles.y;
    }

    private void UpdateVisuals(Color col, float intensity, bool active) {
        if (bulbRenderer != null) {
            bulbRenderer.material.color = col;
            float emissionPower = active ? (1.5f + intensity) : 0.05f;
            bulbRenderer.material.SetColor("_EmissionColor", col * emissionPower);
        }
        if (handlebarLight != null) {
            handlebarLight.color = col;
            handlebarLight.intensity = active ? (0.5f + intensity) : 0.0f;
        }
    }
}
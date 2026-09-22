using UnityEngine;

/// <summary>
/// Central configuration for Lane Keeping Assist (LKA) and Haptic Feedback. 
/// Manages track geometry, hardware PWM resolution limits, and signal normalization.
/// Handles the hysteresis logic for motor engagement and remaps raw errors into PID input values.
/// </summary>
public class LKAConfiguration : MonoBehaviour {
    [Header("Frenet Source")]
    public MLClosedSplineFrenet frenet;

    [Header("Condtion Source")]
    public GameController gameController;

    [Header("Lane Geometry")]
    public float trackWidthMeters = 2.5f; // Total width from left to right curb

    [Header("LKA Settings")]
    [Range(0f, 1f)]
    public float lkaDeadZonePercentage = 0.4f; // Inner area where LKA stays idle (eg. 0.4 = 40% of half-width)

    [Tooltip("The minimum speed for the lanekeeping to activate.")]
    public float minSpeedToEngage = 5f; // speed threshold for safety

    [Tooltip("How much further past the deadzone to ENGAGE.")]
    public float engageBuffer = 0f; // extra distance required to trigger LKA

    [Tooltip("How much inside the deadzone to DISENGAGE.")]
    public float disengageBuffer = 0.10f; // prevents LKA from flickering on/off at the boundary

    public enum HapticsMode { OFF, FIXED, ADAPTIVE }

    [Header("Haptics Settings")]
    public HapticsMode mode = HapticsMode.ADAPTIVE;
    [Range(0f, 1f)] public float hapticsDeadZonePercentage = 0.2f; // Haptics usually trigger before steering
    [Range(0f, 1f)] public float hapticsMaxVibPercentage = 0.4f;   // Max vibration intensity reach

    [Header("Calculated Hardware PWM Limits")]
    public int SteeringPwmMinLimit;     // Calculated 10% safety floor
    public int SteeringPwmMaxLimit;     // Calculated 90% safety ceiling
    public int VibrationPwmIdleValue;   // Midpoint of PWM range (50%)

    [Header("Live Status")]
    public bool isWithinActiveZone = false; // Is the LKA currently providing torque?

    [Range(-1f, 1f)]
    public float crossTrackErrorNormalized; // Raw position: -1 (Left edge) to 1 (Right edge)

    [Range(-1f, 1f)]
    public float lkaCrossTrackErrorNormalized; // Remapped error for the Lanekeeping, used specifically for PID input

    [Header("Steering PWM Resolution")]
    [Tooltip("Resolution for the ESCON controller. Set to 4095 for 12-bit.")]
    public int SteeringPWMRange = 4095;

    [Header("Vibration PWM Resolution")]
    [Tooltip("Resolution for DRV2605 drivers. Set to 4095 for 12-bit.")]
    public int VibrationPWMRange = 4095;

    // Helper to get distance from center to one curb
    public float RoadHalfWidthMeters => trackWidthMeters * 0.5f;

    // Safety margins to prevent overdriving the physical motors
    private const float steeringMinPwmPercent = 0.10f;
    private const float steeringMaxPwmPercent = 0.90f;

    private void Awake() {
        if (gameController == null) gameController = FindObjectOfType<GameController>();
        if (frenet == null) frenet = FindObjectOfType<MLClosedSplineFrenet>();
        UpdateBitRanges();
    }

    private void Start() {
        UpdateHapticsMode();
    }



    private void OnValidate() {
        UpdateBitRanges();
    }


    private void FixedUpdate() {
        if (frenet == null) return;

        // Calculate Raw Normalization
        // Maps meters to a -1 to 1 scale based on track width
        float currentError = (frenet.crossTrackErrorMeters / RoadHalfWidthMeters);
        crossTrackErrorNormalized = Mathf.Clamp(currentError, -1f, 1f);

        float absRaw = Mathf.Abs(crossTrackErrorNormalized);
        float engageLine = lkaDeadZonePercentage + engageBuffer;
        float disengageLine = lkaDeadZonePercentage - disengageBuffer;

        // LKA State Logic (Hysteresis)
        // This logic ensures that if the rider "wobbles" on the line, the motor doesn't chatter
        if (!isWithinActiveZone && absRaw > engageLine) {
            isWithinActiveZone = true; // Turn ON
        } else if (isWithinActiveZone && absRaw < disengageLine) {
            isWithinActiveZone = false; // Turn OFF
        }

        // Remapping LKA Output
        // Re-scales the error so that the edge of the deadzone is 0 and the curb is 1
        if (isWithinActiveZone) {
            float remapped = (absRaw - lkaDeadZonePercentage) / (1f - lkaDeadZonePercentage);
            lkaCrossTrackErrorNormalized = Mathf.Clamp01(remapped) * Mathf.Sign(crossTrackErrorNormalized);
        } else {
            lkaCrossTrackErrorNormalized = 0f;
        }
    }


    // Converts percentage safety limits into raw integer bits for the hardware controllers
    private void UpdateBitRanges() {
        SteeringPwmMinLimit = Mathf.RoundToInt(SteeringPWMRange * steeringMinPwmPercent);
        SteeringPwmMaxLimit = Mathf.RoundToInt(SteeringPWMRange * steeringMaxPwmPercent);
        VibrationPwmIdleValue = Mathf.RoundToInt(VibrationPWMRange * 0.50f);
    }

    private void UpdateHapticsMode() {
        switch (gameController.currentCondition) {
            case GameController.StudyConditions.BaselineCC:
            case GameController.StudyConditions.BaselineCW:
                mode = HapticsMode.OFF;
                break;
            case GameController.StudyConditions.HapticsFixedCC:
            case GameController.StudyConditions.HapticsFixedCW:
            case GameController.StudyConditions.Training:
                mode = HapticsMode.FIXED;
                break;
            case GameController.StudyConditions.HapticsAdaptiveCC:
            case GameController.StudyConditions.HapticsAdaptiveCW:
                mode = HapticsMode.ADAPTIVE;
                break;
        }
    }
}
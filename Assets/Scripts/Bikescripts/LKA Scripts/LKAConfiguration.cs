using UnityEngine;

/* * This script centralizes all Steering (SM) and Vibration (VM) configurations.
 * It handles bit-resolution mapping, lane geometry, and safety logic.
 */

public class LKAConfiguration : MonoBehaviour {
    [Header("Frenet Source")]
    public MLClosedSplineFrenet frenet;

    [Header("Lane Geometry")]
    public float trackWidthMeters = 4.0f;
    
    [Header("LKA Settings")]
    [Range(0f, 1f)] public float lkaDeadZonePercentage = 0.4f;
    [Tooltip("How much further past the deadzone to ENGAGE.")]
    public float engageBuffer = 0f;
    [Tooltip("How much inside the deadzone to DISENGAGE.")]
    public float disengageBuffer = 0.10f;

    public float minSpeedToEngage = 8f;

    public enum HapticsMode { OFF, FIXED, ADAPTIVE }

    [Header("Haptics Settings")]
    public HapticsMode mode = HapticsMode.ADAPTIVE;
    [Range(0f, 1f)] public float hapticsDeadZonePercentage = 0.2f;
    [Range(0f, 1f)] public float hapticsMaxVibPercentage = 0.4f;

    [Header("Calculated Hardware PWM Limits")]
    public int SteeringPwmMinLimit;
    public int SteeringPwmMaxLimit;
    public int VibrationPwmIdleValue;

    [Header("Live Status")]
    public bool isWithinActiveZone = false;
    [Range(-1f, 1f)] public float crossTrackErrorNormalized;
    [Range(-1f, 1f)] public float lkaCrossTrackErrorNormalized;

    [Header("Steering PWM Resolution")]
    [Tooltip("Resolution for the ESCON controller. Set to 4095 for 12-bit.")]
    public int SteeringPWMRange = 4095;

    [Header("Vibration PWM Resolution")]
    [Tooltip("Resolution for DRV2605 drivers. Set to 4095 for 12-bit.")]
    public int VibrationPWMRange = 4095;

    public float RoadHalfWidthMeters => trackWidthMeters * 0.5f;


    private const float SteeringSafetyFloorPercent = 0.10f; // 10%
    private const float SteeringSafetyCeilingPercent = 0.90f; // 90%

    private void Awake() {
        if (frenet == null) frenet = FindObjectOfType<MLClosedSplineFrenet>();
        UpdateBitRanges();
    }

    private void OnValidate() {
        UpdateBitRanges();
    }

    // Recalculates all hardware integer bounds based on specific SM/VM bit depths
    private void UpdateBitRanges() {
        // Steering Motor (SM) PWM calculations
        SteeringPwmMinLimit = Mathf.RoundToInt(SteeringPWMRange * SteeringSafetyFloorPercent);
        SteeringPwmMaxLimit = Mathf.RoundToInt(SteeringPWMRange * SteeringSafetyCeilingPercent);

        // Vibration Motor (VM) PWM calculations
        VibrationPwmIdleValue = Mathf.RoundToInt(VibrationPWMRange * 0.50f);
    }

    private void Update() {
        if (frenet == null) return;

        // 1. Calculate Raw Normalization (-1 to 1)
        float currentError = (frenet.crossTrackErrorMeters / RoadHalfWidthMeters);
        crossTrackErrorNormalized = Mathf.Clamp(currentError, -1f, 1f);

        float absRaw = Mathf.Abs(crossTrackErrorNormalized);
        float engageLine = lkaDeadZonePercentage + engageBuffer;
        float disengageLine = lkaDeadZonePercentage - disengageBuffer;

        // 2. LKA State Logic (Hysteresis)
        if (!isWithinActiveZone && absRaw > engageLine) {
            isWithinActiveZone = true;
        } else if (isWithinActiveZone && absRaw < disengageLine) {
            isWithinActiveZone = false;
        }

        // 3. Remapping LKA Output
        if (isWithinActiveZone) {
            // Pure linear remap: 0 at the deadzone edge, 1 at the lane boundary
            float remapped = (absRaw - lkaDeadZonePercentage) / (1f - lkaDeadZonePercentage);
            lkaCrossTrackErrorNormalized = Mathf.Clamp01(remapped) * Mathf.Sign(crossTrackErrorNormalized);
        } else {
            lkaCrossTrackErrorNormalized = 0f;
        }
    }
}

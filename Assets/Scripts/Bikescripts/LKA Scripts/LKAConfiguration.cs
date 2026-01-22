using UnityEngine;

/// <summary>
/// Central configuration for Lane Keeping Assist and Lane-based Haptics.
///
/// Lane convention:
///   0.0  = center
///  -1.0  = left edge
///  +1.0  = right edge
/// </summary>
public class LKAConfiguration : MonoBehaviour {
    [Header("Frenet Source")]
    public MLClosedSplineFrenet frenet;

    [Header("Lane Geometry")]
    [Tooltip("Total drivable lane width in meters.")]
    public float trackWidthMeters = 4.0f;

    [Tooltip("Percentage of total lane width considered LKA deadzone.")]
    [Range(0f, 1f)]
    public float lkaDeadZonePercentage = 0.5f;

    [Header("LKA Saftey Parameters")]
    public float minSpeedToEngage = 8f;

    public float maxHeadingAngle = 90.0f;
    public enum HapticsMode
    {
        OFF,
        FIXED,
        ADAPTIVE
    }

    [Header("Mode")]
    [SerializeField]
    public HapticsMode mode = HapticsMode.ADAPTIVE;

    [Header("Haptics Thresholds (Normalized Lane Units)")]
    [Range(0f, 1f)]
    public float hapticsDeadZonePercentage = 0.2f;

    [Range(0f, 1f)]
    public float hapticsMaxVibPercentage = 0.45f;

    [Header("Live Lane Position (Read-only)")]
    [Range(-1f, 1f)]
    public float crossTrackErrorNormalized;
    
    public float lkaCrossTrackErrorNormalized;

    public float RoadHalfWidthMeters => trackWidthMeters * 0.5f;

    private void Awake() {
        if (frenet == null)
            frenet = GetComponent<MLClosedSplineFrenet>();
    }

    // This ensures haptic thresholds are valid in the Inspector
    private void OnValidate() {
        // If they are exactly the same, push Max to the edge to avoid division by zero in InverseLerp
        if (Mathf.Approximately(hapticsMaxVibPercentage, hapticsDeadZonePercentage)) {
            hapticsMaxVibPercentage = 1.0f;
            Debug.LogWarning("[LKAConfig] Haptic Max and Deadzone were identical. Max has been set to 1.0 to ensure a valid range.");
        }

        // Flip the values if the max vibration point is closer to the center than the deadzone
        if (hapticsMaxVibPercentage < hapticsDeadZonePercentage) {
            float temp = hapticsMaxVibPercentage;
            hapticsMaxVibPercentage = hapticsDeadZonePercentage;
            hapticsDeadZonePercentage = temp;

            Debug.LogWarning("[LKAConfig] Haptic Max was lower than Deadzone. Values have been flipped to maintain logic.");
        }
    }

    private void Update() {
        if (frenet == null) return;

        crossTrackErrorNormalized = Mathf.Clamp((frenet.crossTrackErrorMeters / RoadHalfWidthMeters), -1f, 1f);

        lkaCrossTrackErrorNormalized = ApplyDeadzone(crossTrackErrorNormalized, lkaDeadZonePercentage);
    }

    private float ApplyDeadzone(float normalizedError, float deadzonePercent) {
        float absError = Mathf.Abs(normalizedError);

        if (absError <= deadzonePercent) return 0f;

        // Remap the remaining space (deadzone to 1.0) back to (0.0 to 1.0)
        float remapped = (absError - deadzonePercent) / (1f - deadzonePercent);
        return remapped * Mathf.Sign(normalizedError);
    }
}

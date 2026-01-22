using UnityEngine;

/// <summary>
/// Central configuration for Lane Keeping Assist and Lane-based Haptics.
/// </summary>
public class LKAConfiguration : MonoBehaviour {
    [Header("Frenet Source")]
    public MLClosedSplineFrenet frenet;

    [Header("Lane Geometry")]
    [Tooltip("Total drivable lane width in meters.")] 
    public float trackWidthMeters = 4.0f;
    [Range(0f, 1f)] public float lkaDeadZonePercentage = 0.5f;

    [Header("Safety Parameters")]
    [Tooltip("Minimum speed required from the bike for LKA to engage.")]
    public float minSpeedToEngage = 8f;

    public enum HapticsMode { OFF, FIXED, ADAPTIVE }

    [Header("Haptics Settings")]
    public HapticsMode mode = HapticsMode.ADAPTIVE;
    [Range(0f, 1f)] public float hapticsDeadZonePercentage = 0.2f;
    [Range(0f, 1f)] public float hapticsMaxVibPercentage = 0.45f;

    [Header("Live Lane Position (Read-only)")]
    [Range(-1f, 1f)] public float crossTrackErrorNormalized;
    [Range(-1f, 1f)] public float lkaCrossTrackErrorNormalized;

    public float RoadHalfWidthMeters => trackWidthMeters * 0.5f;

    private void Awake() {
        if (frenet == null) frenet = FindObjectOfType<MLClosedSplineFrenet>();
    }

    private void Update() {
        if (frenet == null) return;

        crossTrackErrorNormalized = Mathf.Clamp((frenet.crossTrackErrorMeters / RoadHalfWidthMeters), -1f, 1f);
        lkaCrossTrackErrorNormalized = ApplyDeadzone(crossTrackErrorNormalized, lkaDeadZonePercentage);
    }

    private float ApplyDeadzone(float normalizedError, float deadzonePercent) {
        float absError = Mathf.Abs(normalizedError);
        if (absError <= deadzonePercent) return 0f;

        float remapped = (absError - deadzonePercent) / (1f - deadzonePercent);
        return remapped * Mathf.Sign(normalizedError);
    }
}
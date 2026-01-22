using UnityEngine;

public class LKAConfiguration : MonoBehaviour {
    [Header("Frenet Source")]
    public MLClosedSplineFrenet frenet;

    [Header("Lane Geometry")]
    public float trackWidthMeters = 4.0f;
    [Range(0f, 1f)] public float lkaDeadZonePercentage = 0.4f;

    [Header("LKA Hysteresis Thresholds")]
    [Tooltip("How much further past the deadzone to ENGAGE.")]
    public float engageBuffer = 0f;
    [Tooltip("How much inside the deadzone to DISENGAGE.")]
    public float disengageBuffer = 0.5f;

    [Header("Safety Parameters")]
    public float minSpeedToEngage = 8f;

    public enum HapticsMode { OFF, FIXED, ADAPTIVE }

    [Header("Haptics Settings")]
    public HapticsMode mode = HapticsMode.ADAPTIVE;
    [Range(0f, 1f)] public float hapticsDeadZonePercentage = 0.2f;
    [Range(0f, 1f)] public float hapticsMaxVibPercentage = 0.4f;

    [Header("Live Status (Read-only)")]
    public bool isWithinActiveZone = false;
    [Range(-1f, 1f)] public float crossTrackErrorNormalized;
    [Range(-1f, 1f)] public float lkaCrossTrackErrorNormalized;

    public float RoadHalfWidthMeters => trackWidthMeters * 0.5f;

    private void Awake() {
        if (frenet == null) frenet = FindObjectOfType<MLClosedSplineFrenet>();
    }

    private void Update() {
        if (frenet == null) return;

        // 1. Calculate Raw Normalization (Used for Haptics)
        crossTrackErrorNormalized = Mathf.Clamp((frenet.crossTrackErrorMeters / RoadHalfWidthMeters), -1f, 1f);
        float absRaw = Mathf.Abs(crossTrackErrorNormalized);

        // 2. LKA State Logic (Hysteresis)
        float engageLine = lkaDeadZonePercentage + engageBuffer;
        float disengageLine = lkaDeadZonePercentage - disengageBuffer;

        if (!isWithinActiveZone && absRaw > engageLine) {
            isWithinActiveZone = true;
        } else if (isWithinActiveZone && absRaw < disengageLine) {
            isWithinActiveZone = false;
        }

        // 3. Calculate LKA Output Error (Remapped so 0 is the deadzone edge)
        if (isWithinActiveZone) {
            float remapped = (absRaw - lkaDeadZonePercentage) / (1f - lkaDeadZonePercentage);
            lkaCrossTrackErrorNormalized = Mathf.Clamp01(remapped) * Mathf.Sign(crossTrackErrorNormalized);
        } else {
            lkaCrossTrackErrorNormalized = 0f;
        }
    }
}
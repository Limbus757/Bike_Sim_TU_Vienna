using UnityEngine;

using UnityEngine;

/// <summary>
/// Central hub for Lane Keeping Assist geometry and live lane position data.
/// Attach this to a GameObject in your scene.
/// </summary>
public class LKAConfiguration : MonoBehaviour {
    [Header("Script References")]
    [Tooltip("The Frenet source providing cross-track error (ey).")]
    public MLClosedSplineFrenet frenet;

    [Header("Lane Geometry")]
    [Tooltip("The total width of the drivable track/lane in meters.")]
    public float trackWidthMeters = 3.0f;

    [Tooltip("The percentage of the track width in which the LKA does not engage.")]
    [Range(0f, 1f)]
    public float deadZonePercentage = 0.10f;

    [Header("Handlebar Vibration Thresholds (Percent)")]
    [Tooltip("Below this absolute lane % there is no vibration (deadzone). Example: 40 means start at |lanePercent| >= 40.")]
    [Range(0f, 100f)]
    public float vibrationStartPercent = 40f;

    [Tooltip("At this absolute lane % vibration reaches maximum (still before LKA). Example: 80.")]
    [Range(0f, 100f)]
    public float vibrationMaxPercent = 80f;

    [Header("Live Output (Read-Only)")]
    [Tooltip("-100 (Right Edge) to +100 (Left Edge). Used by Haptics.")]
    [Range(-100f, 100f)]
    public float lanePercent;

    // Public properties for other scripts to access
    public float RoadHalfWidthMeters => trackWidthMeters / 2.0f;
    public float DeadZoneMeters => trackWidthMeters * deadZonePercentage;

    private void Awake() {
        if (frenet == null) frenet = GetComponent<MLClosedSplineFrenet>();
    }

    private void Update() {
        if (frenet == null) return;

        // unified Calculation Logic: converts cross-track error to lane percentage (-100 to 100)
        float ey = frenet.crossTrackError;
        float hw = Mathf.Max(0.01f, RoadHalfWidthMeters);

        // Note: LKA uses ey (Left positive), Haptics use lanePercent (Right edge is -100)
        lanePercent = Mathf.Clamp((-ey / hw) * 100f, -100f, 100f);
    }

    /// <summary>
    /// Helper for LKA to calculate normalized error using the central config values.
    /// </summary>
    public float GetNormalizedLKASteeringCrosstrackerror(float deviation) {
        float sign = Mathf.Sign(deviation);
        float distancePastDeadzone = Mathf.Abs(deviation) - DeadZoneMeters;
        float usableLaneSpace = RoadHalfWidthMeters - DeadZoneMeters;

        float result = (usableLaneSpace > 0) ? (distancePastDeadzone / usableLaneSpace) * sign : 0f;
        return Mathf.Clamp(result, -1f, 1f);
    }
}
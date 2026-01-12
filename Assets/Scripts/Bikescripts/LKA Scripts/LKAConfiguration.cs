using UnityEngine;

/// <summary>
/// Static class to hold global Lane Keeping Assist configuration parameters.
/// This allows other scripts (e.g., visualizers, other safety systems) 
/// to access the fundamental geometry without needing a reference to the LKA MonoBehaviour.
/// </summary>
public static class LKAConfiguration
{
    // --- Lane Geometry ---
    [Tooltip("The total width of the drivable track/lane in meters.")]
    public const float TrackWidthMeters = 3.0f; // Example: 3 meters

    [Tooltip("The percentage of the track width in which the LKA does not engage.")]
    public const float DeadZonePercentage = 0.10f;

    // Calculated Dead Zone in meters
    public static float DeadZoneMeters => TrackWidthMeters * DeadZonePercentage;

    // --- Lane Half-Width Percentage ---
    // The cross-track error expressed as a percentage of the *half* track width. 
    // This is often used for visualizers or normalized systems.
    // A value of 1.0f means the bike is at the edge of the lane.
    // Note: The LKA script uses the raw DeadZoneMeters for its primary logic.
    public static float LaneHalfWidthPercentage(float crossTrackError)
    {
        float halfWidth = TrackWidthMeters / 2.0f;
        return Mathf.Clamp(crossTrackError / halfWidth, -1.0f, 1.0f);
    }

}

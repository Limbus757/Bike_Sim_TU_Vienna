using UnityEngine;

public class LanePercentFromCenter : MonoBehaviour
{
    [Header("Inputs")]
    public MLClosedSplineFrenet frenet;     // dein Script mit crossTrackError
    [Tooltip("Halbe Fahrbahnbreite in Metern (Spurbreite / 2).")]
    public float roadHalfWidthMeters = 1.5f;

    [Header("Output (read-only)")]
    [Range(-100f, 100f)]
    public float lanePercent;             // -100 .. +100

    void Update()
    {
        float ey = frenet.crossTrackError; // links positiv, rechts negativ
        float hw = Mathf.Max(0.01f, roadHalfWidthMeters);
        lanePercent = Mathf.Clamp((-ey / hw) * 100f, -100f, 100f);
    }
}
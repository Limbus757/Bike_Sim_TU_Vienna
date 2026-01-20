using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates left/right handlebar haptic intensities based on normalized lane position.
/// Vibrations ramp up as the rider approaches a lane edge, matching the side of the lane being approached..
/// </summary>
public class ML_LaneHapticsFromPercent : MonoBehaviour {

    [Header("Input")]
    [Tooltip("Central LKA configuration providing lane position and haptic thresholds.")]
    public LKAConfiguration config;

    [Header("Response Curve")]
    [Tooltip(
        "Maps normalized lane distance (0..1) to vibration intensity (0..1).\n" +
        "X axis: distance from center beyond haptic start\n" +
        "Y axis: vibration strength"
    )]
    public AnimationCurve intensityCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Scaling")]
    [Tooltip("Global multiplier for vibration strength.")]
    [Range(0f, 2f)]
    public float gain = 1f;

    [Header("Smoothing")]
    [Tooltip(
        "Temporal smoothing applied to each haptic channel.\n" +
        "0 = no smoothing (raw signal)\n" +
        "Higher values = slower, softer response\n" +
        "Recommended range: 5–15"
    )]
    [Range(0f, 30f)]
    public float smoothing = 10f;

    [Header("Outputs (Read-only)")]
    [Range(0f, 1f)] public float pwmLeft;
    [Range(0f, 1f)] public float pwmRight;
    [Range(0, 255)] public int pwmLeft255;
    [Range(0, 255)] public int pwmRight255;

    // Internal velocity terms used by SmoothDamp.
    // These MUST be separate per channel to avoid cross-coupling.
    private float _leftVel;
    private float _rightVel;

    private void Awake() {
        // Auto-bind configuration if not explicitly assigned
        if (config == null) config = GetComponent<LKAConfiguration>();
    }

    private void Update() {
        if (config == null) { // fail-safe
            pwmLeft = pwmRight = 0f;
            pwmLeft255 = pwmRight255 = 0;
            return;
        }

        // Lane position normalized to (-1;1)
        // -1 = left edge, 1 = right edge
        float lane = config.crossTrackErrorNormalized;

        // Distance from lane center, ignoring side
        // Used to determine vibration intensity only
        float absLane = Mathf.Abs(lane);

        // Convert absolute lane distance into a 0..1 parameter
        // using thresholds defined in LKAConfiguration.
        float t = Mathf.InverseLerp(
            config.hapticsDeadZonePercentage, 
            config.hapticsMaxVibPercentage, 
            absLane
            );

        // Apply response curve and global gain
        float intensity = Mathf.Clamp01(
            intensityCurve.Evaluate(t) * gain
            );

        // left/right channel assignment
        float targetLeft = (lane < 0f) ? intensity : 0f;
        float targetRight = (lane > 0f) ? intensity : 0f;

        if (smoothing <= 0f) {
            pwmLeft = targetLeft;
            pwmRight = targetRight;
        } else {
            float smoothTime = 1f / smoothing; // bigger smoothing -> smaller time constant
            pwmLeft = Mathf.SmoothDamp(pwmLeft, targetLeft, ref _leftVel, smoothTime);
            pwmRight = Mathf.SmoothDamp(pwmRight, targetRight, ref _rightVel, smoothTime);
        }

        pwmLeft255 = 123 - Mathf.Clamp(Mathf.RoundToInt(pwmLeft * 123f), 0, 123);
        pwmRight255 = 123 - Mathf.Clamp(Mathf.RoundToInt(pwmRight * 123f), 0, 123);
    }
}


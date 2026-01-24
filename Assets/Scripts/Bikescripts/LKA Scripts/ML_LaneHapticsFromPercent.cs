using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static LKAConfiguration;


/*
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
            pwmLeft = pwmRight = 128f;
            pwmLeft255 = pwmRight255 = 128;
            pwmLeft255 = pwmRight255 = 128;

            return;
        }

        // Lane position normalized to (-1;1)
        // -1 = left edge, 1 = right edge
        float lane = config.crossTrackErrorNormalized;
        if (config.mode == HapticsMode.OFF)
        {
            ApplyOutputs(0f, 0f);
            return;
        }
        // Distance from lane center, ignoring side
        // Used to determine vibration intensity only
        float absLane = Mathf.Abs(lane);
        if (config.mode == HapticsMode.FIXED)
        {
            bool outsideDeadzone = absLane >= config.hapticsDeadZonePercentage;

            float f_intensity = outsideDeadzone ? 128f  : 0f;

            float f_targetLeft = (lane < 0f) ? f_intensity : 0f;
            float f_targetRight = (lane > 0f) ? f_intensity : 0f;

            ApplyOutputs(f_targetLeft, f_targetRight);
            return;
        }
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

        pwmLeft255 = 128 - Mathf.Clamp(Mathf.RoundToInt(pwmLeft * 128f), 0, 128);
        pwmRight255 = 128 - Mathf.Clamp(Mathf.RoundToInt(pwmRight * 128f), 0, 128);
    }
    private void ApplyOutputs(float left, float right)
    {
        pwmLeft = left;
        pwmRight = right;
        pwmLeft255 = 128 - Mathf.Clamp(Mathf.RoundToInt(pwmLeft * 128f), 0, 128);
        pwmRight255 = 128 - Mathf.Clamp(Mathf.RoundToInt(pwmRight * 128f), 0, 128);
    }
}

*/

public class ML_LaneHapticsFromPercent : MonoBehaviour {

    [Header("Input")]
    public LKAConfiguration config;

    [Header("Response Curve (Adaptive Only)")]
    public AnimationCurve intensityCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Scaling")]
    [Range(0f, 2f)] public float gain = 1f;

    [Header("Smoothing")]
    [Range(0f, 30f)] public float smoothing = 10f;

    [Header("Normalized Strength (0.0 to 1.0)")]
    [Range(0f, 1f)] public float normalizedLeftVibration;
    [Range(0f, 1f)] public float normalizedRightVibration;

    [Header("Hardware PWM")]
    public int pwmLeft;
    public int pwmRight;


    private int idlePWM;
    private float _leftVel;
    private float _rightVel;

    private void Awake() {
        if (config == null) config = GetComponent<LKAConfiguration>();
        idlePWM = (config != null) ? config.VibrationPwmIdleValue : 128;
        pwmLeft = pwmRight = idlePWM;
    }

    private void Update() {
        UpdateHaptics();
    }

    private void UpdateHaptics() {
        // setup hardware baseline
        float targetL = 0f;
        float targetR = 0f;

        // calculate Intensity based on Mode
        if (config != null && config.mode != HapticsMode.OFF) {
            float normalizedCTE = config.crossTrackErrorNormalized;
            float absNormCTE = Mathf.Abs(normalizedCTE);
            float intensity = 0f;

            switch (config.mode) {
                case HapticsMode.FIXED:
                    intensity = (absNormCTE >= config.hapticsDeadZonePercentage) ? 1.0f : 0f;
                    break;
                case HapticsMode.ADAPTIVE:
                    float t = Mathf.InverseLerp(config.hapticsDeadZonePercentage, config.hapticsMaxVibPercentage, absNormCTE);
                    intensity = Mathf.Clamp01(intensityCurve.Evaluate(t) * gain);
                    break;
            }

            // assign side
            if (normalizedCTE < 0f) targetL = intensity;
            else if (normalizedCTE > 0f) targetR = intensity;
        }

        bool useSmooth = (config != null && config.mode == HapticsMode.ADAPTIVE && smoothing > 0f);

        ProcessChannel(targetL, ref normalizedLeftVibration, ref _leftVel, out pwmLeft, idlePWM, useSmooth);
        ProcessChannel(targetR, ref normalizedRightVibration, ref _rightVel, out pwmRight, idlePWM, useSmooth);
    }

    /// <summary>
    /// Unified pipeline for a single haptic channel.
    /// </summary>
    private void ProcessChannel(float target, ref float currentVal, ref float velocity, out int pwmOut, int idle, bool smooth) {
        if (target <= 0 && currentVal <= 0.001f) {
            currentVal = 0f;
            velocity = 0f;
        } else if (smooth) {
            float smoothTime = 1f / smoothing;
            currentVal = Mathf.SmoothDamp(currentVal, target, ref velocity, smoothTime);
        } else {
            currentVal = target;
            velocity = 0f;
        }
        currentVal = Mathf.Clamp01(currentVal);
        pwmOut = idle - Mathf.RoundToInt(currentVal * idle);
    }
}
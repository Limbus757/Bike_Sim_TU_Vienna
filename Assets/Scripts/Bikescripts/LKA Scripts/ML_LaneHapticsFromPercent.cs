using UnityEngine;
using static LKAConfiguration;

/// <summary>
/// Generates handlebar vibration intensities based on normalized lane position.
/// Vibrations ramp up as the rider approaches a lane edge, matching the side of the lane being approached.
/// 
/// This script manages the haptic feedback pipeline, including mode switching,
/// temporal smoothing, and hardware-specific pwm signal mapping.
/// 
/// OFF: no vibration is sent to the handlebars.
/// FIXED: a binary warning that triggers full vibration as soon as the rider crosses the deadzone.
/// ADAPTIVE: a graduated vibration that ramps up in intensity as the rider moves closer to the lane edge, can be mapped to a curve.
/// </summary>
public class ML_LaneHapticsFromPercent : MonoBehaviour {

    [Header("Input")]
    [Tooltip("Link to the lka configuration which provides normalized cross-track error and haptic settings.")]
    public LKAConfiguration config;

    [Header("Response Curve (Adaptive Only)")]
    [Tooltip("Defines the ramp-up of vibration intensity as the rider moves further from the lane center.")]
    public AnimationCurve intensityCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Scaling")]
    [Tooltip("Multiplier applied to the calculated vibration intensity.")]
    [Range(0f, 2f)] public float gain = 1f;

    [Header("Smoothing")]
    [Tooltip("Controls how quickly the vibration intensity changes. higher values result in faster response.")]
    [Range(0f, 30f)] public float smoothing = 10f;

    [Header("Normalized Strength (0.0 to 1.0)")]
    [Tooltip("Current intensity of the left vibration channel.")]
    [Range(0f, 1f)] public float normalizedLeftVibration;
    [Tooltip("Current intensity of the right vibration channel.")]
    [Range(0f, 1f)] public float normalizedRightVibration;

    [Header("Hardware PWM")]
    [Tooltip("The actual integer values sent to the motor hardware.")]
    public int pwmLeft;
    public int pwmRight;

    private int idlePWM;
    private float _leftVel;
    private float _rightVel;

    /// <summary>
    /// Initializes references and sets the hardware idle baseline value.
    /// </summary>
    private void Awake() {
        if (config == null) config = GetComponent<LKAConfiguration>();

        // fetch the neutral pwm value from configuration
        idlePWM = (config != null) ? config.VibrationPwmIdleValue : 128;
        pwmLeft = pwmRight = idlePWM;
    }

    /// <summary>
    /// Updates the haptic feedback state every frame.
    /// </summary>
    private void Update() {
        UpdateHaptics();
    }

    /// <summary>
    /// Calculates target intensities based on current lane position and processes them through the haptic pipeline.
    /// </summary>
    private void UpdateHaptics() {
        float targetL = 0f;
        float targetR = 0f;

        // calculate intensity based on the selected condition mode
        if (config != null && config.mode != LKAConfiguration.HapticsMode.OFF) {
            float normalizedCTE = config.crossTrackErrorNormalized;
            float absNormCTE = Mathf.Abs(normalizedCTE);
            float intensity = 0f;

            switch (config.mode) {
                case LKAConfiguration.HapticsMode.FIXED:
                    // binary vibration response based on deadzone
                    intensity = (absNormCTE >= config.hapticsDeadZonePercentage) ? 1.0f : 0f;
                    break;
                case LKAConfiguration.HapticsMode.ADAPTIVE:
                    // graduated vibration response based on distance from center
                    float t = Mathf.InverseLerp(config.hapticsDeadZonePercentage, config.hapticsMaxVibPercentage, absNormCTE);
                    intensity = Mathf.Clamp01(intensityCurve.Evaluate(t) * gain);
                    break;
            }

            // assign intensity to the appropriate side
            if (normalizedCTE < 0f) targetL = intensity;
            else if (normalizedCTE > 0f) targetR = intensity;
        }

        // smoothing is only active during adaptive mode to maintain fixed mode's urgency
        bool useSmooth = (config != null && config.mode == LKAConfiguration.HapticsMode.ADAPTIVE && smoothing > 0f);

        // process individual motor channels
        ProcessChannel(targetL, ref normalizedLeftVibration, ref _leftVel, out pwmLeft, idlePWM, useSmooth);
        ProcessChannel(targetR, ref normalizedRightVibration, ref _rightVel, out pwmRight, idlePWM, useSmooth);
    }

    /// <summary>
    /// Unified pipeline for a single haptic channel.
    /// Includes zero-crossing logic to prevent vibration bleed between handlebars.
    /// </summary>
    private void ProcessChannel(float target, ref float currentVal, ref float velocity, out int pwmOut, int idle, bool smooth) {

        // zero-crossing force: if the target is zero, we cut vibration immediately.
        // this prevents the smoothing algorithm from lagging while the rider is on the opposite side.
        if (target <= 0) {
            currentVal = 0f;
            velocity = 0f;
        } else if (smooth) {
            // apply temporal smoothing for a natural feel
            float smoothTime = 1f / smoothing;
            currentVal = Mathf.SmoothDamp(currentVal, target, ref velocity, smoothTime);
        } else {
            // immediate response for non-smoothed modes
            currentVal = target;
            velocity = 0f;
        }

        // map the normalized intensity to the inverted hardware pwm range
        currentVal = Mathf.Clamp01(currentVal);
        pwmOut = idle - Mathf.RoundToInt(currentVal * idle);
    }
}
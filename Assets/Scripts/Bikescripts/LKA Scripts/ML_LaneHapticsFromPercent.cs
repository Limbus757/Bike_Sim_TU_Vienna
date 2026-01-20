using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ML_LaneHapticsFromPercent : MonoBehaviour
{

    [Header("Input")]
    [Tooltip("Source providing lanePercent (-100..+100). If null, will try GetComponent.")]
    public LKAConfiguration config;

    [Header("Response Curve")]
    [Tooltip("Maps normalized distance (0..1) to intensity (0..1). X=0 at startPercent, X=1 at maxPercent.")]
    public AnimationCurve intensityCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Output Scaling")]
    [Tooltip("Master gain multiplier for vibration intensity.")]
    [Range(0f, 2f)]
    public float gain = 1f;

    [Tooltip("Minimum PWM once vibration has started (helps feeling the onset).")]
    [Range(0f, 1f)]
    public float minPwmWhenActive = 0f;

    [Header("Smoothing")]
    [Tooltip("0 = no smoothing. Higher values smooth more (good to avoid jitter).")]
    [Range(0f, 30f)]
    public float smoothing = 10f;

    [Header("Outputs (Read-only)")]
    [Range(0f, 1f)] public float pwmLeft;
    [Range(0f, 1f)] public float pwmRight;
    [Range(0, 255)] public int pwmLeft255;
    [Range(0, 255)] public int pwmRight255;

    private float _pwmLeftVel;
    private float _pwmRightVel;

    private void Awake() {
        if (config == null) config = GetComponent<LKAConfiguration>();
    }

    private void Update() {
        if (config == null) {
            pwmLeft = pwmRight = 0f;
            pwmLeft255 = pwmRight255 = 0;
            return;
        }

        float lanePercent = Mathf.Clamp(config.lanePercent, -100f, 100f);

        // How far from center in absolute percent (0..100)
        float absP = Mathf.Abs(lanePercent);

        // Normalize to 0..1 within [startPercent..maxPercent]
        float t = 0f;
        if (absP <= config.vibrationStartPercent) {
            t = 0f; // deadzone
        } else if (config.vibrationMaxPercent <= config.vibrationStartPercent + 0.0001f) {
            // Degenerate case: start == max -> step behavior
            t = 1f;
        } else {
            t = Mathf.InverseLerp(config.vibrationStartPercent, config.vibrationMaxPercent, absP);
        }

        // Map through curve
        float intensity = Mathf.Clamp01(intensityCurve.Evaluate(t)) * gain;

        // Optional minimum when active (only if t>0)
        if (t > 0f) intensity = Mathf.Max(intensity, minPwmWhenActive);

        intensity = Mathf.Clamp01(intensity);

        // Decide side:
        // lanePercent > 0 means you're left -> rumble LEFT side (you are near left edge)
        // lanePercent < 0 means you're right -> rumble RIGHT side
        float targetLeft = 0f;
        float targetRight = 0f;

        if (lanePercent > 0.01f) targetLeft = intensity;
        else if (lanePercent < -0.01f) targetRight = intensity;
        else { targetLeft = 0f; targetRight = 0f; }

        // Smoothing (exponential-ish using SmoothDamp)
        if (smoothing <= 0f)
        {
            pwmLeft = targetLeft;
            pwmRight = targetRight;
        }
        else
        {
            float smoothTime = 1f / smoothing; // bigger smoothing -> smaller time constant
            pwmLeft = Mathf.SmoothDamp(pwmLeft, targetLeft, ref _pwmLeftVel, smoothTime);
            pwmRight = Mathf.SmoothDamp(pwmRight, targetRight, ref _pwmRightVel, smoothTime);
        }
        pwmLeft255 = 123-Mathf.Clamp(Mathf.RoundToInt(pwmLeft * 123f), 0, 123);
        pwmRight255 = 123-Mathf.Clamp(Mathf.RoundToInt(pwmRight * 123f), 0, 123);
    }
}

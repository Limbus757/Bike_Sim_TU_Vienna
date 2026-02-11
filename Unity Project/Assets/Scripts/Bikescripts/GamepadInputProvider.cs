using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class GamepadInputProvider : MonoBehaviour, IBikeInputProvider
{
    [Header("Steering")]
    public float gamepadSteeringSensitivity = 2f;

    [Header("Dynamics")]
    [Tooltip("Maximale Beschleunigung durch Gas (in units/s^2).")]
    public float maxAcceleration = 8f;

    [Tooltip("Maximale Bremsverzögerung (in units/s^2).")]
    public float maxBraking = 14f;

    [Tooltip("Wie stark das Bike ohne Input langsamer wird (Roll-/Luftwiderstand) (in units/s^2).")]
    public float coastDeceleration = 2f;

    [Tooltip("Maximale Geschwindigkeit (in units/s).")]
    public float maxSpeed = 20f;

    [Header("Brake split")]
    [Range(0f, 1f)] public float frontBrakeRatio = 0.5f; // 0.5 = 50/50
    [Range(0f, 1f)] public float rearBrakeRatio = 0.5f;

    private float _speed;          // aktuelle Geschwindigkeit
    private float _acceleration;   // aktuelle Beschleunigung (signed)

    public float GetSteeringAngle()
    {
        // Wenn du komplett aufs neue InputSystem gehen willst,
        // könntest du auch leftStick.x nehmen. Ich lass es wie bei dir.
        float rawInput = Input.GetAxis("Horizontal");
        return Mathf.Clamp(rawInput * gamepadSteeringSensitivity, -90f, 90f);
    }

    private void Update()
    {
        var gp = Gamepad.current;

        float throttle = gp != null ? gp.rightTrigger.ReadValue() : 0f; // 0..1
        float brake = gp != null ? gp.leftTrigger.ReadValue() : 0f; // 0..1

        // Signed acceleration:
        // + durch Gas
        // - durch Bremse
        float a = (throttle * maxAcceleration) - (brake * maxBraking);

        // Wenn weder Gas noch Bremse: ausrollen
        if (throttle <= 0.001f && brake <= 0.001f)
        {
            // Coast wirkt immer entgegen der Fahrtrichtung (hier: Speed>=0 angenommen)
            a = -coastDeceleration;
        }

        _acceleration = a;

        // Integrate speed
        _speed += _acceleration * Time.deltaTime;

        // clamp speed: kein Rückwärtsfahren in diesem einfachen Modell
        _speed = Mathf.Clamp(_speed, 0f, maxSpeed);
    }

    // Jetzt ist das wirklich Geschwindigkeit (nicht "Gaswert")
    public float GetSpeed()
    {
        return _speed;
    }

    // Optional: falls du es irgendwo brauchst
    public float GetAcceleration()
    {
        return _acceleration;
    }

    // Brake jetzt genauso "richtig" wie Gas (InputSystem), gesplittet:
    public float GetFrontBrakeForce()
    {
        float brake = Gamepad.current != null ? Gamepad.current.leftTrigger.ReadValue() : 0f;
        return brake * maxBraking * frontBrakeRatio;
    }

    public float GetRearBrakeForce()
    {
        float brake = Gamepad.current != null ? Gamepad.current.leftTrigger.ReadValue() : 0f;
        return brake * maxBraking * rearBrakeRatio;
    }

    public float GetResistance()
    {
        return 0f;
    }
}
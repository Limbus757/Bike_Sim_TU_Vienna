using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GamepadInputProvider : MonoBehaviour, IBikeInputProvider
{
    public float gamepadSteeringSensitivity = 45f;
    public float gamepadAccelerationSensitivity = 1f;
    public float gamepadBrakeSensitivity = 1f;

    public float GetSteeringAngle()
    {
        float rawInput = Input.GetAxis("Horizontal");
        return Mathf.Clamp(rawInput * gamepadSteeringSensitivity, -90, 90);
    }

    public float GetSpeedInput()
    {
        return Input.GetAxis("RightTrigger") * gamepadAccelerationSensitivity;
    }

    public float GetBrakeForce()
    {
        return Input.GetAxis("LeftTrigger") * gamepadBrakeSensitivity;
    }
}
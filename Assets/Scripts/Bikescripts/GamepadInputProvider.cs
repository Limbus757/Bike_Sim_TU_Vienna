using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GamepadInputProvider : MonoBehaviour, IBikeInputProvider
{
    public float gamepadSteeringSensitivity = 2f;
    public float gamepadAccelerationSensitivity = 100f;
    public float gamepadBrakeSensitivity = 0.5f;

    public float GetSteeringAngle()
    {
        float rawInput = Input.GetAxis("Horizontal");
        return Mathf.Clamp(rawInput * gamepadSteeringSensitivity, -90, 90);
    }

    public float GetSpeed() {
        return Input.GetAxis("RightTrigger") * gamepadAccelerationSensitivity;
    }

    public float GetFrontBrakeForce() {
        return Input.GetAxis("LeftTrigger") * gamepadBrakeSensitivity /2;
    }

    public float GetBackBrakeForce() {
        return Input.GetAxis("LeftTrigger") * gamepadBrakeSensitivity /2;
    }

    public float GetResistance() {
        return 0f;
    }
}
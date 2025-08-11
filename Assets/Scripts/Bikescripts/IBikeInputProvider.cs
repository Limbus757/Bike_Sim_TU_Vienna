using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IBikeInputProvider
{
    // Gets the current steering angle input.
    float GetSteeringAngle();

    // Gets the current acceleration input.
    float GetSpeedInput();

    // Gets the current brake force input.
    float GetBrakeForce();

    // Gets the current resistance input.
    float GetResistance();
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IBikeInputProvider
{
    // Gets the current steering angle input.
    float GetSteeringAngle();

    // Gets the current acceleration input.
    float GetSpeed();

    // Gets the current front brake force input.
    float GetFrontBrakeForce();

    // Gets the current front brake force input.
    float GetRearBrakeForce();

    // Gets the current resistance input.
    float GetResistance();
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Ensure this script is placed in the same project/namespace as ReceivedSerialProvider
// or that ReceivedSerialProvider is accessible.
public class SimulatorInputProvider : MonoBehaviour, IBikeInputProvider {

    public GameObject leftController;
    private Transform handlebar;
    private Quaternion initialHandlebarRotation, initialControllerRotation;
    private bool initializationComplete = false;
    public float SteeringAngle { get; private set; } = 0.0f;
    private bool steeringInitialized = false;

    public float GetRearBrakeForce() {
        return ReceivedSerialProvider.RearBrakeForce;
    }

    public float GetFrontBrakeForce() {
        return ReceivedSerialProvider.FrontBrakeForce;
    }

    public float GetResistance() {
        return ReceivedSerialProvider.ResistanceValue;
    }

    public float GetSpeed() {
        return ReceivedSerialProvider.SpeedKmh;
    }

    public float GetSteeringAngle() {
        UpdateSteeringAngle();
        return SteeringAngle;
    }

    void Awake() {
        InitializeSteering();
    }

    private void InitializeSteering() {
        handlebar = this.transform.Find("WheelHandleBar");

        if (handlebar == null) {
            Debug.LogError("PhysicalBikeInputProvider: 'WheelHandleBar' child Transform not found! Please assign correctly or check name.");
        }

        if (leftController == null) {
            Debug.LogError("PhysicalBikeInputProvider: 'Left Controller' GameObject not assigned! Steering input will not work.");
        }

        if (!steeringInitialized && leftController != null) {
            initialHandlebarRotation = handlebar.rotation;
            initialControllerRotation = leftController.transform.rotation;
            steeringInitialized = true;
            Debug.Log("PhysicalBikeInputProvider: Steering Initialization complete.");
        }
    }

    private void UpdateSteeringAngle() {
        if (steeringInitialized) {
            float tempSteeringAngle = 0.0f;
            // calculate the steering angle based on the VR controller's rotation
            // NOTE: The steering calculation line (initialControllerRotation * leftController.transform.rotation * initialHandlebarRotation) 
            // seems unusual for calculating delta rotation, but is preserved as it was in the original script.
            Quaternion currentDelta = Quaternion.Inverse(initialControllerRotation) * leftController.transform.rotation;

            // Extract the yaw (Y-axis rotation) and adjust
            tempSteeringAngle = currentDelta.eulerAngles.y;

            // Handle the 0-360 to -180 to 180 conversion
            tempSteeringAngle = Mathf.DeltaAngle(0, tempSteeringAngle);

            // adjust Steering angle to always be between [-90;90] degrees 
            tempSteeringAngle = Mathf.Clamp(tempSteeringAngle, -90, 90);
            SteeringAngle = tempSteeringAngle;

        } else {
            // Check for handlebar missing in Update if not found in Awake
            if (handlebar == null) {
                Debug.LogWarning("SteeringInputProvider: Initialization failed, cannot change steering angle!");
            }
        }
    }
}
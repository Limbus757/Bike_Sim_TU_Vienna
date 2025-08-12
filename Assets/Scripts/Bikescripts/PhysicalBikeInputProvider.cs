using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PhysicalBikeInputProvider : MonoBehaviour, IBikeInputProvider {

    public GameObject leftController;
    private Transform handlebar;
    private Quaternion initialHandlebarRotation, initialControllerRotation;
    public bool initializationComplete = false;
    public float SteeringAngle { get; private set; } = 0.0f;
   private bool steeringInitialized = false;

    public float GetBackBrakeForce() {
        throw new System.NotImplementedException();
    }

    public float GetFrontBrakeForce() {
        throw new System.NotImplementedException();
    }

    public float GetResistance() {
        throw new System.NotImplementedException();
    }

    public float GetSpeed() {
        throw new System.NotImplementedException();
    }

    public float GetSteeringAngle() {
        return SteeringAngle;
    }

    void Awake() {
        if (leftController == null) {
            Debug.LogError("PhysicalBikeInputProvider: 'Left Controller' GameObject not assigned! Steering input will not work.");
        }
    }

    void Start() {
        if (!steeringInitialized && leftController != null) {
            initialHandlebarRotation = handlebar.rotation;
            initialControllerRotation = leftController.transform.rotation;
            steeringInitialized = true;
            Debug.Log("PhysicalBikeInputProvider: Steering Initialization complete.");
        }
    }

    void Update() {
        UpdateSteeringAngle();
    }

    private void UpdateSteeringAngle() {
        if (steeringInitialized) {
            float tempSteeringAngle = 0.0f;
            // calculate the steering angle based on the VR controller's rotation
            tempSteeringAngle = (initialControllerRotation * leftController.transform.rotation * initialHandlebarRotation).eulerAngles.y;
            // adjust Steering angle to alyways be between [-90;90] degrees 
            tempSteeringAngle = Mathf.Clamp(Mathf.DeltaAngle(0, tempSteeringAngle), -90, 90);
            // reveal calculated steering angle
            SteeringAngle = tempSteeringAngle;
            // apply the calculated steering angle to the visual handlebar's local rotation
            handlebar.localEulerAngles = new Vector3(0.0f, SteeringAngle, 0.0f);
        } else {
            Debug.Log("SteeringInputProvider: Initialization failed, cannot change steering angle!");
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SteeringInputProvider : MonoBehaviour
{
    public GameObject leftController;
    private Transform handlebar;
    private Quaternion initialHandlebarRotation, initialControllerRotation;
    public bool initializationComplete = false;
    public float SteeringAngle { get; private set; } = 0.0f;

    void Awake() {
        handlebar = this.transform.Find("WheelHandleBar");

        if (handlebar == null) {
            Debug.LogError("SteeringInputProvider: 'WheelHandleBar' child Transform not found! Please assign correctly or check name.");
        }

        if (leftController == null) {
            Debug.LogError("SteeringInputProvider: 'Left Controller' GameObject not assigned! Steering input will not work.");
        }
    }

    private void Start() {
        if (!initializationComplete && handlebar != null && leftController != null)
        {
            initialHandlebarRotation = handlebar.rotation;
            initialControllerRotation = leftController.transform.rotation;
            initializationComplete = true;
            Debug.Log("SteeringInputProvider: Initialization complete.");
        }
    }

    void Update() {
        if (initializationComplete) {
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

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


    [SerializeField] private UduinoController uduinoController;

    private float uduinoBikeSpeed;
    private float uduinoFrontBrakeForce;
    private float uduinoBackBrakeForce;
    private float uduinoResistance;
    private float uduinoCombinedBrakeForce;

    public float GetBackBrakeForce() {
        return uduinoBackBrakeForce;
    }

    public float GetFrontBrakeForce() {
        return uduinoFrontBrakeForce;
    }

    public float GetResistance() {
        return uduinoResistance;
    }

    public float GetSpeed() {
        return uduinoBikeSpeed;
    }

    public float GetSteeringAngle() {
        UpdateSteeringAngle();
        return SteeringAngle;
    }

    void Awake() {
        InitializeSteering();

        if (uduinoController != null) {
            uduinoController.OnUduinoDataReceived += OnUduinoDataReceived;
        } else {
            Debug.LogError("PhysicalBikeInputProvider: UduinoController reference not set! Input will not work.");
        }
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

    private void OnUduinoDataReceived(float speed, float steering, float f_brake, float b_brake, float combined_brake, float resistance) {
        // This method is called by the UduinoController's event.
        // It updates the internal state of this provider.
        uduinoBikeSpeed = speed;
        uduinoFrontBrakeForce = f_brake;
        uduinoBackBrakeForce = b_brake;
        uduinoCombinedBrakeForce = combined_brake;
        uduinoResistance = resistance;
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

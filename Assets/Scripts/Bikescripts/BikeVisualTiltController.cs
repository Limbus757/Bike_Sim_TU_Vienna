using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// In BikeCameraVisualTiltController.cs
public class BikeCameraVisualTiltController : MonoBehaviour
{

    public bool applyTilt;
    private float maxTiltAngle = 30.0f;
    public float visualTiltMultiplier = 1000f;
    public float visualTiltSpeed = 0.5f;

    [SerializeField] private GameObject visualTiltTarget;
    private BikeController bikeController;

    void Awake()
    {   
        bikeController = FindObjectOfType<BikeController>();
        if (bikeController == null) {
            Debug.LogError("BikeVisualTiltController: BikeController not found!");
        }
    }

    void LateUpdate() {   
        if (applyTilt && bikeController != null) {
            ApplyCameraTilting();
        }
    }

    private void ApplyCameraTilting() {
        float visualTiltAngle = -(bikeController.tiltAngle) * visualTiltMultiplier;

        visualTiltAngle = Mathf.Clamp(visualTiltAngle, -maxTiltAngle, maxTiltAngle);

        Quaternion currentRot = visualTiltTarget.transform.localRotation;
        Quaternion targetRot = Quaternion.Euler(0f, 0f, visualTiltAngle);
        Quaternion interpolatedRotation = Quaternion.Lerp(currentRot, targetRot, Time.deltaTime * visualTiltSpeed);
        visualTiltTarget.transform.localRotation = interpolatedRotation;
    }
}
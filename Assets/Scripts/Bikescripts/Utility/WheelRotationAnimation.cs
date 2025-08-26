using UnityEngine;

/// <summary>
/// This script handles the visual rotation of a wheel based on the bike's speed.
/// It should be attached to each wheel GameObject.
/// </summary>
public class WheelRotation : MonoBehaviour {
    // A reference to the BikeController script to get the current speed.
    private BikeController bikeController;

    // The radius of the wheel in meters. You need to set this in the Inspector.
    public float wheelRadius = 0.35f; // A common radius for a bike wheel in meters.

    /// <summary>
    /// Called when the script instance is being loaded.
    /// We find the BikeController here to ensure we have a reference.
    /// </summary>
    void Awake() {
        // We find the BikeController by searching for it in the scene.
        // It's a good practice to do this once on Awake to avoid repeated searches.
        bikeController = FindObjectOfType<BikeController>();
        if (bikeController == null) {
            Debug.LogError("WheelRotation: BikeController not found in the scene. Wheel rotation will not work.");
        }
    }

    /// <summary>
    /// Called every frame. We update the wheel's rotation here for a smooth visual effect.
    /// </summary>
    void Update() {
        if (bikeController != null) {
            float bikeSpeedKmh = bikeController.BikeSpeed;
            float bikeSpeedMs = bikeSpeedKmh / 3.6f;

            // Formula: angular speed = linear speed / radius
            float angularSpeedRadPerSec = 0f;
            if (wheelRadius > 0) {
                angularSpeedRadPerSec = bikeSpeedMs / wheelRadius;
            }

            // 1 radian is approx. 57.2958 degrees.
            float rotationDegreesPerSec = angularSpeedRadPerSec * Mathf.Rad2Deg;

            // rotation amount for this frame.
            float rotationAmount = rotationDegreesPerSec * Time.deltaTime;

            // rotate the wheel around its local X-axis.
            transform.Rotate(rotationAmount, 0, 0, Space.Self);
        }
    }
}

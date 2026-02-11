using UnityEngine;

/// <summary>
/// Controls the visual spinning of a wheel based on the parent bike's velocity.
/// Applies rotation around the local X-axis to simulate rolling movement.
/// </summary>
/// 

public class WheelRotation : MonoBehaviour {
    [Header("References")]
    [Tooltip("Reference to the main controller to pull speed data.")]
    private BikeController bikeController;

    [Header("Settings")]
    [Tooltip("The physical radius of the wheel mesh in meters. Used to calculate how fast the wheel should spin relative to movement.")]
    public float wheelRadius = 0.35f;

    /// <summary>
    /// Locates the BikeController in the scene. 
    /// </summary>
    void Awake() {
        // Search the scene for the BikeController component
        bikeController = FindObjectOfType<BikeController>();

        // Error handling to prevent 'NullReferenceException' during Update
        if (bikeController == null) {
            Debug.LogError($"WheelRotation on {gameObject.name}: BikeController not found! Ensure a BikeController exists in the hierarchy.");
        }
    }

    /// <summary>
    /// Calculates and applies the rotation every frame based on the bike's current speed.
    /// </summary>
    void Update() {
        if (bikeController != null) {
            // cnvert speed to meters per second (m/s)
            float bikeSpeedKmh = bikeController.BikeSpeedKmh;
            float bikeSpeedMs = bikeSpeedKmh / 3.6f;

            // calculate angular velocity (radians per second)
            // Physics Formula: v = r * ω  =>  ω = v / r
            float angularSpeedRadPerSec = 0f;
            if (wheelRadius > 0) {
                angularSpeedRadPerSec = bikeSpeedMs / wheelRadius;
            }

            // convert Radians to Degrees for Unity's Transform system
            float rotationDegreesPerSec = angularSpeedRadPerSec * Mathf.Rad2Deg;

            // calculate Frame-Independent rotation, multiplying by DeltaTime ensures the wheel spins at the same speed regardless of FPS
            float rotationAmount = rotationDegreesPerSec * Time.deltaTime;

            // apply rotation, uses Space.Self to ensure it rotates on its own axis, even if the bike is leaning
            transform.Rotate(rotationAmount, 0, 0, Space.Self);
        }
    }
}
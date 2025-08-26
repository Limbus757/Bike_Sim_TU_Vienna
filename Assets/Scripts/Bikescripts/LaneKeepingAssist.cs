using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// A PID-based Lane Keeping Assist system that calculates a steering correction
/// to keep an object aligned with a spline. This script must be attached
/// to the same GameObject as the BikeController.
/// </summary>
public class LaneKeepingAssist : MonoBehaviour {
    [Header("PID Controller Gains")]
    [Tooltip("Proportional gain. Controls the response to the current error.")]
    public float Kp = 2.0f;
    [Tooltip("Integral gain. Reduces steady-state error by accumulating past error.")]
    public float Ki = 0.5f;
    [Tooltip("Derivative gain. Dampens oscillations by predicting future error.")]
    public float Kd = 0.2f;

    [Header("LKA Parameters")]
    [Tooltip("The 'dead zone' distance from the spline center where no correction is applied.")]
    public float lkaDeadZone = 0.5f;
    [Tooltip("A multiplier that weights the heading error relative to the lateral error.")]
    public float headingErrorGain = 2.0f;
    [Tooltip("The maximum allowed correction angle to prevent over-steering.")]
    public float maxAngleCorrection = 30f;

    [Header("Inspectable Variables (Read-Only)")]
    [Tooltip("The current deviation distance from the spline center.")]
    public float Deviation;
    [Tooltip("The angle difference between the bike's forward vector and the spline's tangent.")]
    public float HeadingError;
    [Tooltip("The current calculated error for the PID controller.")]
    public float CurrentError;

    // Internal PID state variables
    private float integralError = 0f;
    private float lastError = 0f;

    private SplineContainer trackSpline;
    private GameController gameController;

    void Awake() {
        gameController = FindObjectOfType<GameController>();
        if (gameController == null) {
            Debug.LogError("LKA: GameController not found in the scene. LKA cannot be initialized.");
        }
    }

    public float UpdateCorrection(Transform bikeTransform) {
        if (gameController != null) {
            // trackSpline = gameController.course;
        }

        // 1. Find the closest point on the spline and calculate deviation.
        float normalizedT = 0;
        Vector3 closestPoint = new Vector3(0, 0, 0); //SplineUtility.GetNearestPoint(trackSpline.Spline, bikeTransform.position, out normalizedT);
        Vector3 splineTangent = SplineUtility.EvaluateTangent(trackSpline.Spline, normalizedT);

        Vector3 deviationVector = bikeTransform.position - closestPoint;
        deviationVector.y = 0; // Ignore vertical deviation

        Deviation = deviationVector.magnitude;

        // 2. Determine the direction of the error and calculate lateral error.
        Vector3 splineRight = Vector3.Cross(splineTangent, Vector3.up).normalized;
        float side = Vector3.Dot(deviationVector.normalized, splineRight);
        float lateralError = side * (Deviation - lkaDeadZone);

        // 3. Calculate heading error (angle between bike's forward and spline's tangent)
        Vector3 bikeForward = bikeTransform.forward;
        HeadingError = Vector3.SignedAngle(bikeForward, splineTangent, Vector3.up);

        // 4. Check for the dead zone. If inside, reset state and return 0.
        if (Deviation < lkaDeadZone) {
            integralError = 0f; // Reset integral term in the dead zone.
            lastError = 0f;
            CurrentError = 0f;
            return 0f;
        }

        // 5. Calculate the combined error for PID
        CurrentError = lateralError + (HeadingError * headingErrorGain);

        // 6. Calculate PID components.
        float proportionalTerm = Kp * CurrentError;

        integralError += CurrentError * Time.deltaTime;
        float integralTerm = Ki * integralError;

        float derivativeTerm = Kd * ((CurrentError - lastError) / Time.deltaTime);
        lastError = CurrentError;

        // 7. Calculate final correction.
        float totalCorrection = proportionalTerm + integralTerm + derivativeTerm;

        // 8. Clamp the correction to prevent extreme steering.
        return Mathf.Clamp(totalCorrection, -maxAngleCorrection, maxAngleCorrection);
    }
}

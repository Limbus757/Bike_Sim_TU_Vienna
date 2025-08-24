using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

public class GameController : MonoBehaviour {

    #region Course-Parameters

    // The Spawnpoint enum now encodes the course and knot index into a single value.
    // The format is: (Course Index * 100) + Knot Index
    // This allows the code to programmatically determine the spawn point without a switch statement.
    public enum Spawnpoint {
        Course1_Spawnpoint1 = 0,
        Course1_Spawnpoint2 = 5,
        Course1_Spawnpoint3 = 10,
    }

    [Header("Platform Mode Settings")]
    [Tooltip("Select the StartingPoint. This corresponds to a specific knot on a spline.")]
    public Spawnpoint currentSpawnpoint = Spawnpoint.Course1_Spawnpoint1;

    [Header("Course Reference")]
    [Tooltip("Add the parent GameObjects of your splines here. Index 0 is Course 1, Index 1 is Course 2, etc.")]
    public SplineContainer[] courses;

    [Header("Bike Reference")]
    [Tooltip("Add the bike Gameobject here.")]
    public GameObject bike;
    private Rigidbody bikeRigidbody;

    #endregion

    void Start() {
        InitializeCourseAndBikePosition();
    }

    void Update() {
        HandleInputs();
    }

    void FixedUpdate() {

    }

    /// <summary>
    /// Finds the correct spline and knot based on the selected spawn point and
    /// teleports the bike to that exact position and rotation.
    /// </summary>
    private void InitializeCourseAndBikePosition() {
        bikeRigidbody = bike.GetComponent<Rigidbody>();

        int spawnpointValue = (int)currentSpawnpoint;
        Debug.Log($"Selected Spawnpoint enum value: {spawnpointValue}");

        // Use integer division to extract the course index. // E.g., 107 / 100 = 1 (Course 2)
        int courseIndex = spawnpointValue / 100;

        // Use the modulo operator to extract the knot index. // E.g., 107 % 100 = 7 (Knot 7)
        int knotIndex = spawnpointValue % 100;
        Debug.Log($"Extracted Course Index: {courseIndex}, Extracted Knot Index: {knotIndex}");

        if (courseIndex < 0 || courseIndex >= courses.Length) {
            Debug.LogError($"Course index {courseIndex} is out of bounds. Please check the Spawnpoint enum.");
            return;
        }

        Spline selectedSpline = courses[courseIndex].Spline;
        Debug.Log($"Found Spline with {selectedSpline.Count} knots.");

        // Validate that the spline and bike references are assigned
        if (selectedSpline == null || this.bike == null) {
            Debug.LogError("Spline or Bike Transform not assigned. Please check the Inspector.");
            return;
        }

        // Check if the knot index is valid for the selected spline
        if (knotIndex < 0 || knotIndex >= selectedSpline.Count) {
            Debug.LogError($"Knot index {knotIndex} is out of bounds for the selected spline. " +
                           $"The spline has {selectedSpline.Count} knots.");
            return;
        }

        // Get the knot's position in local space
        BezierKnot[] knotArray = selectedSpline.ToArray();
        Vector3 knotLocalPosition = knotArray[knotIndex].Position;

        // Convert the local knot position to a world position
        Vector3 startPosition = courses[courseIndex].transform.TransformPoint(knotLocalPosition);

        // Get the knot's rotation in local space and convert it to a world rotation
        Quaternion knotLocalRotation = knotArray[knotIndex].Rotation;
        Quaternion startRotation = courses[courseIndex].transform.rotation * knotLocalRotation;

        Debug.Log($"Calculated start position: {startPosition} and start rotation: {startRotation.eulerAngles}");

        bikeRigidbody.MovePosition(startPosition);
        bikeRigidbody.MoveRotation(startRotation);

        Debug.Log($"Bike teleported to course {courseIndex}, knot {knotIndex}.");
    }

    private void HandleInputs() {

    }
}

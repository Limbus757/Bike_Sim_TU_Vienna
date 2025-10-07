using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

public class GameController : MonoBehaviour {

    [Header("Study Parameters")]
    [Tooltip("Name of the current study or trial batch.")]
    public string studyName = "DefaultStudy";
    [Tooltip("Numerical ID for the study participant. Use the plus/minus buttons to increment.")]
    public int studyParticipantId = 1;

    #region Course-Parameters
    public enum Spawnpoint {
        Spawnpoint_0 = 0,
        Spawnpoint_1 = 1,
        Spawnpoint_2 = 2,
    }

    [Header("Platform Mode Settings")]
    [Tooltip("Select the StartingPoint. This corresponds to a specific knot on a spline.")]
    public Spawnpoint selectedSpawnpoint = Spawnpoint.Spawnpoint_0;

    [Header("Course Reference")]
    [Tooltip("Select the current Course here")]
    public GameObject course;

    [Header("Bike Reference")]
    [Tooltip("Add the bike Gameobject here.")]
    public GameObject bike;

    #endregion

    private Rigidbody bikeRigidbody;
    private SplineContainer splineContainer;
    private SplineSpawnpointData spawnpointData;

    void Start() {
        InitializeAndCheckSpawnVariables();
        SpawnBike();
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
    private void SpawnBike() {
        int spawnpointIndex = (int)selectedSpawnpoint;
        Debug.Log($"Selected Spawnpoint: {spawnpointIndex}");

        Spline selectedSpline = splineContainer[0];
        Debug.Log($"Found Spline with {selectedSpline.Count} knots.");

        // Get the knot's position in local space
        int knotIndex = spawnpointData.spawnpoints[spawnpointIndex].knotIndex; 
        BezierKnot[] knotArray = selectedSpline.ToArray();
        Vector3 knotLocalPosition = knotArray[knotIndex].Position;

        // Convert the local knot position to a world position
        Vector3 startPosition = splineContainer.transform.TransformPoint(knotLocalPosition);

        // Get the knot's rotation in local space and convert it to a world rotation
        Quaternion knotLocalRotation = knotArray[knotIndex].Rotation;
        Quaternion startRotation = splineContainer.transform.rotation * knotLocalRotation;

        Debug.Log($"Calculated start position: {startPosition} and start rotation: {startRotation.eulerAngles}");

        bikeRigidbody.MovePosition(startPosition);
        bikeRigidbody.MoveRotation(startRotation);

        Debug.Log($"Bike teleported to course {course.name}, knot {knotIndex}.");
    }

    private void InitializeAndCheckSpawnVariables() {
        if (course == null) {
            Debug.LogError("GameController: 'Course Parent' is not assigned. Please assign a GameObject containing Spline and SpawnpointData components in the Inspector.");
            return;
        }

        if (bike == null) {
            Debug.LogError("GameController: Bike GameObject is not assigned. Cannot initialize bike position.");
            return;
        }

        bikeRigidbody = bike.GetComponent<Rigidbody>();

        splineContainer = course.GetComponentInChildren<SplineContainer>();
        spawnpointData = course.GetComponentInChildren<SplineSpawnpointData>();
    }

    private void HandleInputs() {

    }
}

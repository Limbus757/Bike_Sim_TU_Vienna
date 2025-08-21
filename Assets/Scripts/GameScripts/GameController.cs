using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameController : MonoBehaviour {

    #region Course-Parameters

    public enum Spawnpoint {Course1, None}

    [Header("Platform Mode Settings")]
    [Tooltip("Select the StartingPoint.")]
    public Spawnpoint currentSpawnpoint = Spawnpoint.Course1;

    #endregion

    void Start() {
       
    }
    void Update() {
        HandleInputs();
    }

    void FixedUpdate() {

    }

    private void HandleInputs() {
        
    }
}

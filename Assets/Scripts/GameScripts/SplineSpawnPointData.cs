using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;


/// <summary>
/// This script holds the spawn point data for a specific spline and is meant to be attached
/// to the same GameObject as a SplineContainer component.
/// </summary>
public class SplineSpawnpointData : MonoBehaviour {

    /// <summary>
    /// A serializable data structure to hold the name and knot index for a single spawn point.
    /// The [System.Serializable] attribute makes it visible and editable in the Unity Inspector.
    /// </summary>
    [System.Serializable]
    public struct SpawnpointData {
        [Tooltip("The index of the knot on the spline that this spawn point corresponds to.")]
        public int knotIndex;
    }

    /// <summary>
    /// INDEX MAP:
    /// [0] = Bike Spawn (Forward)
    /// [1] = Bike Spawn (Reverse)
    /// [2] = Start Line Knot
    /// [3] = Finish Line Knot
    /// 
    /// When Reverse is true, Trigger 2 becomes the Finish and Trigger 3 becomes the Start.
    [Tooltip("A list of all spawn points for this course. You can edit this list in the Inspector.")]
    public List<SpawnpointData> spawnpoints = new List<SpawnpointData>();
}

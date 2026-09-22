using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

public class SplineFromPoints : MonoBehaviour {
    [Header("Coordinate Data")]
    [TextArea(10, 20)]
    public string rawCoordinates = "0.00, 0.00\n12.50, 0.00\n25.00, 0.00\n37.50, 0.00\n50.00, 0.00\n62.50, 0.00\n75.00, 0.00\n87.50, 0.00\n100.00, 0.00\n112.50, 0.00\n125.00, 0.00\n137.50, 37.50\n137.50, 75.00\n125.00, 75.00\n112.50, 75.00\n100.75, 75.00\n87.50, 88.25\n87.50, 100.00\n87.50, 112.50\n87.50, 137.50\n62.50, 137.50\n37.50, 137.50\n37.50, 125.00\n37.50, 112.50\n37.50, 100.00\n37.50, 87.50\n37.50, 75.00\n37.50, 62.50\n37.50, 50.00\n37.50, 37.50\n37.50, 25.00\n37.50, 0.00\n37.50, -112.50\n12.50, -112.50\n-12.50, -112.50\n-12.50, -88.25\n-25.75, -75.00\n-62.50, -75.00\n-100.00, -75.00\n-137.50, -37.50\n-137.50, 0.00\n-100.00, 0.00\n0.00, 0.00";

    [Header("Generation Settings")]
    public string gameObjectName = "Generated_Track";
    public float scaleMultiplier = 1f;

    [ContextMenu("Generate and Save Spline")]
    public void CreateSpline() {
        // Find or create the track object
        GameObject trackObj = GameObject.Find(gameObjectName);
        if (trackObj == null) trackObj = new GameObject(gameObjectName);

        // Find or add the spline container
        SplineContainer container = trackObj.GetComponent<SplineContainer>();
        if (container == null) container = trackObj.AddComponent<SplineContainer>();

        Spline spline = container.Spline;
        spline.Clear();
        spline.Closed = true;

        // Parse and add points
        string[] lines = rawCoordinates.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines) {
            string[] axis = line.Split(',');
            if (axis.Length < 2) continue;

            if (float.TryParse(axis[0], out float x) && float.TryParse(axis[1], out float z)) {
                // Create knot with default rotation and zeroed tangents
                float3 position = new float3(x * scaleMultiplier, 0, z * scaleMultiplier);
                spline.Add(new BezierKnot(position));
            }
        }

        Debug.Log($"Generated spline with {spline.Count} knots at raw positions.");
    }
}
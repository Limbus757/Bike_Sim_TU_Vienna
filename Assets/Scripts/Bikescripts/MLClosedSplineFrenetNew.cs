using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// <summary>
/// This script tracks a bike's progress along a Unity Spline.
/// It converts world positions into "Frenet" coordinates (s = distance along track, ey = offset from center).
/// It also detects if the current track section is "Straight" or "Curved" based on curvature math.
/// </summary>
public class MLClosedSplineFrenetNew : MonoBehaviour {
    [Header("Input References")]
    [Tooltip("The Transform of the bike/player.")]
    public Transform bike;
    [Tooltip("The SplineContainer defining the track path.")]
    public SplineContainer centerLine;
    [Tooltip("The index of the spline in the container (usually 0).")]
    public int splineIndex = 0;

    [Header("Sampling Settings")]
    [Tooltip("Higher numbers increase precision but use more memory. 600 is usually plenty.")]
    public int samples = 600;
    [Tooltip("The number of nearby samples to check each frame. Keeps performance high.")]
    public int localWindow = 12;

    [Header("Curvature Detection")]
    [Tooltip("The 'sharpness' limit. If curvature is below this, the track is 'straight'. Try 0.01 to 0.05.")]
    public float straightThreshold = 0.02f;
    [Tooltip("If true, the bool won't flicker on/off instantly on bumpy track sections.")]
    public bool useSmoothing = true;
    [Tooltip("How many seconds the track must remain in a new state before the public bool flips.")]
    public float stateChangeDelay = 0.15f;

    [Header("Physics/Ground Settings")]
    [Tooltip("If true, the 'Up' direction is based on the ground normal (useful for banked turns).")]
    public bool useGroundNormal = false;
    public LayerMask groundMask = ~0;
    public Vector3 roadUp = Vector3.up;

    [Header("Outputs (Read-Only)")]
    [Tooltip("True if the bike is on a straight section, false if in a curve.")]
    public bool isOnStraight;
    [Tooltip("Numerical value of track sharpness (0 = dead straight).")]
    public float curvatureAmount;
    [Tooltip("Distance from the center line. Positive = Left, Negative = Right.")]
    public float crossTrackError;
    [Tooltip("Total distance traveled along the spline from the start.")]
    public float s;
    [Tooltip("The angle difference between the bike's nose and the track direction.")]
    public float headingErrorDeg;
    [Tooltip("The point on the spline center-line closest to the bike.")]
    public Vector3 nearestPoint;
    [Tooltip("The forward direction of the track at the nearest point.")]
    public Vector3 tangent;
    [Tooltip("The total measured length of the track.")]
    public float totalLength;

    // Internal Arrays (The "Baked" Track)
    private Vector3[] pts;        // World-space positions
    private Vector3[] tans;       // World-space tangents
    private float[] curvatures;   // Sharpness values
    private float[] cumLen;       // Total distance at each sample
    private int N;                // Total sample count
    private int lastBestK = 0;    // Remembers previous position for fast lookup

    // Smoothing Logic
    private bool internalState;
    private float stateTimer;

    void Awake() => Bake();

    void OnValidate() {
        samples = Mathf.Max(32, samples);
        localWindow = Mathf.Clamp(localWindow, 4, 64);
    }

    /// <summary>
    /// Pre-calculates the spline data into arrays. 
    /// This allows us to find the bike on the track without doing heavy math every frame.
    /// </summary>
    public void Bake() {
        if (centerLine == null) return;

        var spline = centerLine.Splines[splineIndex];
        var tf = centerLine.transform;

        N = samples;
        pts = new Vector3[N + 1];
        tans = new Vector3[N + 1];
        curvatures = new float[N + 1];
        cumLen = new float[N + 1];
        totalLength = 0f;

        for (int i = 0; i <= N; i++) {
            float t = (float)i / N; // Normalized time (0 to 1)

            // Convert Spline Local space to World space
            Vector3 localPos = (Vector3)spline.EvaluatePosition(t);
            Vector3 localTan = (Vector3)spline.EvaluateTangent(t);

            pts[i] = tf.TransformPoint(localPos);
            tans[i] = tf.TransformDirection(localTan).normalized;

            // Calculate track 'sharpness' at this point
            curvatures[i] = spline.EvaluateCurvature(t);

            // Record cumulative distance along the track
            if (i > 0) {
                float seg = Vector3.Distance(pts[i - 1], pts[i]);
                totalLength += seg;
                cumLen[i] = totalLength;
            } else cumLen[i] = 0f;
        }
        lastBestK = 0;
    }

    void Update() {
        if (centerLine == null || bike == null || pts == null || pts.Length == 0) return;

        // Determine which way is 'Up' for the bike (Standard Up or Ground Normal)
        roadUp = useGroundNormal ? SampleGroundUp(bike.position) : Vector3.up;

        // Perform the core tracking logic
        EvaluateAt(bike.position, roadUp, out crossTrackError, out s, out nearestPoint, out tangent, out headingErrorDeg);

        // Handle the straight/curved smoothing timer
        UpdateSmoothing();
    }

    /// <summary>
    /// Calculates all track-related data for a specific world position.
    /// </summary>
    void EvaluateAt(Vector3 pos, Vector3 up,
                    out float ey, out float sOut,
                    out Vector3 pStar, out Vector3 tHat, out float hdgErrDeg) {
        // 1) LOCAL SEARCH
        // Instead of checking all 600 points, we only check points near where we were last frame.
        int bestK = lastBestK;
        float bestDist2 = float.MaxValue;
        int half = Mathf.Min(localWindow, N / 2);

        for (int off = -half; off <= half; off++) {
            int k = Mod(bestK + off, N);
            float d2 = (pts[k] - pos).sqrMagnitude;
            if (d2 < bestDist2) {
                bestDist2 = d2;
                lastBestK = k;
            }
        }
        bestK = lastBestK;

        // 2) SEGMENT PROJECTION
        // Find the exact closest point on the line segment between two baked points.
        int aK = bestK;
        int bK = Mod(bestK + 1, N);
        Vector3 A = pts[aK];
        Vector3 B = pts[bK];
        Vector3 AB = B - A;
        float ab2 = Mathf.Max(AB.sqrMagnitude, 1e-6f);

        // tSeg is the 0-1 percentage of how far the bike is between point A and B
        float tSeg = Mathf.Clamp01(Vector3.Dot(pos - A, AB) / ab2);
        pStar = A + tSeg * AB;

        // 3) TANGENT (Track Direction)
        tHat = AB.normalized;
        if (tHat.sqrMagnitude < 0.5f) // Fallback for edge cases
            tHat = Vector3.Slerp(tans[aK], tans[bK], tSeg).normalized;

        // 4) CROSS TRACK ERROR (Lateral Offset)
        // Uses a cross product to find the 'Right' vector, then calculates distance from center.
        Vector3 nRight = Vector3.Cross(tHat, up).normalized;
        ey = Vector3.Dot(pos - pStar, -nRight);

        // 5) ARC LENGTH (Total distance along track)
        float segLen = Vector3.Distance(A, B);
        float sRaw = cumLen[aK] + tSeg * segLen;
        sOut = ModF(sRaw, totalLength);

        // 6) HEADING ERROR (Steering angle vs Track angle)
        Vector3 fwd = Vector3.ProjectOnPlane(bike.forward, up).normalized;
        Vector3 tPlanar = Vector3.ProjectOnPlane(tHat, up).normalized;
        hdgErrDeg = Vector3.SignedAngle(fwd, tPlanar, up);

        // 7) CURVATURE SAMPLING
        // Mix the curvature of the two closest points based on bike's progress between them.
        float curveA = curvatures[aK];
        float curveB = curvatures[bK];
        curvatureAmount = Mathf.Lerp(curveA, curveB, tSeg);

        // Determine the RAW state (before smoothing)
        internalState = curvatureAmount < straightThreshold;
    }

    /// <summary>
    /// Prevents the isOnStraight boolean from flickering if the track data is noisy.
    /// It requires the bike to stay in a state for 'stateChangeDelay' before switching.
    /// </summary>
    void UpdateSmoothing() {
        if (!useSmoothing) {
            isOnStraight = internalState;
            return;
        }

        if (internalState != isOnStraight) {
            stateTimer += Time.deltaTime;
            if (stateTimer >= stateChangeDelay) {
                isOnStraight = internalState;
                stateTimer = 0f;
            }
        } else {
            stateTimer = 0f;
        }
    }

    /// <summary>
    /// Shoots a raycast down to find the angle of the ground.
    /// </summary>
    Vector3 SampleGroundUp(Vector3 origin) {
        if (Physics.Raycast(origin + Vector3.up * 2f, Vector3.down, out var hit, 5f, groundMask))
            return hit.normal.normalized;
        return Vector3.up;
    }

    // Helper math functions for wrapping around closed loops (0 becomes totalLength)
    int Mod(int x, int m) { int r = x % m; return r < 0 ? r + m : r; }
    float ModF(float x, float m) { float r = x % m; return r < 0 ? r + m : r; }

#if UNITY_EDITOR
    /// <summary>
    /// Draws a sphere and line in the Scene View to visualize track tracking.
    /// Green = Straight, Red = Curved.
    /// </summary>
    void OnDrawGizmosSelected() {
        if (pts == null || pts.Length == 0) return;
        Gizmos.color = isOnStraight ? Color.green : Color.red;
        Gizmos.DrawWireSphere(nearestPoint, 0.5f);
        Gizmos.DrawLine(nearestPoint, nearestPoint + tangent * 2f);
    }
#endif
}
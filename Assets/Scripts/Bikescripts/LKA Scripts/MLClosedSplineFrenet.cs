using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// <summary>
/// This script tracks a bike's progress along a Unity Spline.
/// It converts world positions into "Frenet" coordinates (s = distance along track, ey = offset from center).
/// It also detects if the current track section is "Straight" or "Curved" based on curvature math.
/// </summary>
public class MLClosedSplineFrenet : MonoBehaviour
{
    // ... (All Header and Input/Output Fields remain the same) ...
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
    [Tooltip("The 'sharpness' limit. If curvature is below this, the track is 'straight'. Try lowering this (e.g., 0.005) if long turns are missed.")]
    public float straightThreshold = 0.015f;
    [Tooltip("If true, the bool won't flicker on/off instantly on bumpy track sections.")]
    public bool useSmoothing = true;
    [Tooltip("How many seconds the track must remain in a new state before the public bool flips.")]
    public float stateChangeDelay = 0.15f;

    // NEW: Small constant to prevent divide-by-zero errors.
    private const float MIN_CURVATURE_DENOMINATOR = 1e-6f;

    [Header("Physics/Ground Settings")]
    public bool useGroundNormal = false;
    public LayerMask groundMask = ~0;
    public Vector3 roadUp = Vector3.up;

    [Header("Outputs (Read-Only)")]
    public bool isOnStraight;
    public float curvatureAmount;
    public float crossTrackError;
    public float s;
    public float headingErrorDeg;
    public Vector3 nearestPoint;
    public Vector3 tangent;
    public float totalLength;

    // Internal Arrays (The "Baked" Track)
    private Vector3[] pts;
    private Vector3[] tans;
    private float[] curvatures;
    private float[] cumLen;
    private int N;
    private int lastBestK = 0;

    // Smoothing Logic
    private bool internalState;
    private float stateTimer;

    void Awake() => Bake();

    void OnValidate()
    {
        samples = Mathf.Max(32, samples);
        localWindow = Mathf.Clamp(localWindow, 4, 64);
    }

    /// <summary>
    /// Pre-calculates the spline data into arrays. 
    /// </summary>
    public void Bake()
    {
        if (centerLine == null) return;

        // ADDED NULL/Index CHECK
        if (centerLine.Splines.Count == 0 || splineIndex >= centerLine.Splines.Count)
        {
            Debug.LogError("[Frenet] Cannot bake: SplineContainer is empty or Spline Index is out of bounds.");
            return;
        }

        var spline = centerLine.Splines[splineIndex];
        var tf = centerLine.transform;

        N = samples;
        pts = new Vector3[N + 1];
        tans = new Vector3[N + 1];
        curvatures = new float[N + 1];
        cumLen = new float[N + 1];
        totalLength = 0f;

        for (int i = 0; i <= N; i++)
        {
            float t = (float)i / N; // Normalized time (0 to 1)

            // Convert Spline Local space to World space
            Vector3 localPos = (Vector3)spline.EvaluatePosition(t);
            Vector3 localTan = (Vector3)spline.EvaluateTangent(t);

            pts[i] = tf.TransformPoint(localPos);
            tans[i] = tf.TransformDirection(localTan).normalized;

            // Calculate track 'sharpness' at this point
            float rawCurvature = spline.EvaluateCurvature(t);

            // --- CURVATURE FIX ---
            if (float.IsNaN(rawCurvature) || float.IsInfinity(rawCurvature))
            {
                // If the curvature evaluation results in NaN (division by zero), 
                // we treat it as perfectly straight (zero curvature).
                curvatures[i] = 0f;
                if (float.IsNaN(rawCurvature))
                {
                    Debug.LogWarning("[Frenet] NaN Curvature detected during bake at index " + i + ". Treating as 0.");
                }
            }
            else
            {
                // Curvature is always a magnitude, so we use its absolute value.
                curvatures[i] = Mathf.Abs(rawCurvature);
            }
            // --- END CURVATURE FIX ---

            // Record cumulative distance along the track
            if (i > 0)
            {
                float seg = Vector3.Distance(pts[i - 1], pts[i]);
                totalLength += seg;
                cumLen[i] = totalLength;
            }
            else cumLen[i] = 0f;
        }
        lastBestK = 0;
        Debug.Log($"[Frenet] Spline Baked successfully. Total Length: {totalLength:F2}m.");
    }

    void Update()
    {
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
                    out Vector3 pStar, out Vector3 tHat, out float hdgErrDeg)
    {

        // 1) LOCAL SEARCH (Unchanged - uses robust logic from previous task if implemented)
        int bestK = lastBestK;
        float bestDist2 = float.MaxValue;
        int half = Mathf.Min(localWindow, N / 2);

        for (int off = -half; off <= half; off++)
        {
            int k = Mod(bestK + off, N);
            float d2 = (pts[k] - pos).sqrMagnitude;
            if (d2 < bestDist2)
            {
                bestDist2 = d2;
                lastBestK = k;
            }
        }
        bestK = lastBestK;
        float dist = Mathf.Sqrt(bestDist2); 
        if (dist > 30f) 
        {
            int best = 0;
            float bestD2 = float.MaxValue;
            for (int i = 0; i < N; i++)
            {
                float d2 = (pts[i] - pos).sqrMagnitude;
                if (d2 < bestD2) { bestD2 = d2; best = i; }
            }
            lastBestK = best;

            bestDist2 = bestD2;
        }
        // 2) SEGMENT PROJECTION (Unchanged)
        int aK = bestK;
        int bK = Mod(bestK + 1, N);
        Vector3 A = pts[aK];
        Vector3 B = pts[bK];
        Vector3 AB = B - A;
        float ab2 = Mathf.Max(AB.sqrMagnitude, MIN_CURVATURE_DENOMINATOR); // Added robustness here too

        // tSeg is the 0-1 percentage of how far the bike is between point A and B
        float tSeg = Mathf.Clamp01(Vector3.Dot(pos - A, AB) / ab2);
        pStar = A + tSeg * AB;

        // 3) TANGENT (Track Direction) (Unchanged)
        tHat = AB.normalized;
        if (tHat.sqrMagnitude < 0.5f)
            tHat = Vector3.Slerp(tans[aK], tans[bK], tSeg).normalized;

        // 4) CROSS TRACK ERROR (Lateral Offset) (Unchanged)
        Vector3 nRight = Vector3.Cross(tHat, up).normalized;
        ey = Vector3.Dot(pos - pStar, -nRight);

        // 5) ARC LENGTH (Total distance along track) (Unchanged)
        float segLen = Vector3.Distance(A, B);
        float sRaw = cumLen[aK] + tSeg * segLen;
        sOut = ModF(sRaw, totalLength);

        // 6) HEADING ERROR (Steering angle vs Track angle) (Unchanged)
        Vector3 fwd = Vector3.ProjectOnPlane(bike.forward, up).normalized;
        Vector3 tPlanar = Vector3.ProjectOnPlane(tHat, up).normalized;
        hdgErrDeg = Vector3.SignedAngle(fwd, tPlanar, up);

        // 7) CURVATURE SAMPLING (Unchanged)
        float curveA = curvatures[aK];
        float curveB = curvatures[bK];
        curvatureAmount = Mathf.Lerp(curveA, curveB, tSeg);

        // Determine the RAW state (before smoothing)
        internalState = curvatureAmount < straightThreshold;
    }

    // ... (UpdateSmoothing, SampleGroundUp, Mod, ModF, OnDrawGizmosSelected remain the same) ...

    /// <summary>
        /// Prevents the isOnStraight boolean from flickering if the track data is noisy.
        /// It requires the bike to stay in a state for 'stateChangeDelay' before switching.
        /// </summary>
    void UpdateSmoothing()
    {
        if (!useSmoothing)
        {
            isOnStraight = internalState;
            return;
        }

        if (internalState != isOnStraight)
        {
            stateTimer += Time.deltaTime;
            if (stateTimer >= stateChangeDelay)
            {
                isOnStraight = internalState;
                stateTimer = 0f;
            }
        }
        else
        {
            stateTimer = 0f;
        }
    }

    /// <summary>
    /// Shoots a raycast down to find the angle of the ground.
    /// </summary>
    Vector3 SampleGroundUp(Vector3 origin)
    {
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
    void OnDrawGizmosSelected()
    {
        if (pts == null || pts.Length == 0) return;
        Gizmos.color = isOnStraight ? Color.green : Color.red;
        Gizmos.DrawWireSphere(nearestPoint, 0.5f);
        Gizmos.DrawLine(nearestPoint, nearestPoint + tangent * 2f);
    }
#endif
}
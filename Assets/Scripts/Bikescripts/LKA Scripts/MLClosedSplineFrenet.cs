using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;




/// <summary>
/// This script tracks a bike's progress along a Unity spline using Frenet coordinates.
/// It calculates lateral offset (distance from center) and longitudinal progress (distance along track).
/// </summary>
/// 

/*
public class MLClosedSplineFrenet : MonoBehaviour {
    [Header("Input References")]
    public Transform bikeTransform;
    public SplineContainer trackSplineContainer;
    public int splineIndex = 0;

    [Header("Sampling Settings")]
    public int resolutionSamples = 600;
    public int localSearchWindow = 12;

    [Header("Curvature Detection")]
    public float straightLineThreshold = 0.015f;
    public bool useSmoothing = true;
    public float stateChangeDelay = 0.15f;

    [Header("Physics/Ground Settings")]
    public bool useGroundNormal = false;
    public LayerMask groundLayerMask = ~0;
    public Vector3 currentSurfaceNormal = Vector3.up;

    [Header("Outputs (Read-Only)")]
    public bool isOnStraightTrack;
    public float currentCurvature;
    public float crossTrackErrorMeters; // Was 'ey'
    public float currentDistanceOnTrack;  // Was 's'
    public float headingErrorDegrees;
    public Vector3 closestPointOnSpline;
    public Vector3 trackTangentDirection;
    public float lapTotalLength;

    private GameController _gameController;
    private const float EPSILON = 1e-6f;

    // Baked track data
    private Vector3[] _samplePoints;
    private Vector3[] _sampleTangents;
    private float[] _sampleCurvatures;
    private float[] _cumulativeDistances;
    private int _lastClosestSampleIndex = 0;

    // Smoothing logic
    private bool _rawIsStraightState;
    private float _stateTransitionTimer;

    void Awake() {
        _gameController = FindObjectOfType<GameController>();
        BakeSplineData();
    }

    public void BakeSplineData() {
        if (trackSplineContainer == null || trackSplineContainer.Splines.Count <= splineIndex) return;

        var spline = trackSplineContainer.Splines[splineIndex];
        var worldTransform = trackSplineContainer.transform;

        _samplePoints = new Vector3[resolutionSamples + 1];
        _sampleTangents = new Vector3[resolutionSamples + 1];
        _sampleCurvatures = new float[resolutionSamples + 1];
        _cumulativeDistances = new float[resolutionSamples + 1];
        lapTotalLength = 0f;

        for (int i = 0; i <= resolutionSamples; i++) {
            float normalizedTime = (float)i / resolutionSamples;

            Vector3 localPos = (Vector3)spline.EvaluatePosition(normalizedTime);
            Vector3 localTan = (Vector3)spline.EvaluateTangent(normalizedTime);

            _samplePoints[i] = worldTransform.TransformPoint(localPos);
            _sampleTangents[i] = worldTransform.TransformDirection(localTan).normalized;

            float rawCurve = spline.EvaluateCurvature(normalizedTime);
            _sampleCurvatures[i] = (float.IsNaN(rawCurve) || float.IsInfinity(rawCurve)) ? 0f : Mathf.Abs(rawCurve);

            if (i > 0) {
                lapTotalLength += Vector3.Distance(_samplePoints[i - 1], _samplePoints[i]);
                _cumulativeDistances[i] = lapTotalLength;
            }
        }
    }

    void Update() {
        if (trackSplineContainer == null || bikeTransform == null || _samplePoints == null) return;

        currentSurfaceNormal = useGroundNormal ? CalculateGroundNormal(bikeTransform.position) : Vector3.up;

        CalculateFrenetCoordinates(
            bikeTransform.position,
            currentSurfaceNormal,
            out crossTrackErrorMeters,
            out currentDistanceOnTrack,
            out closestPointOnSpline,
            out trackTangentDirection,
            out headingErrorDegrees
        );

        UpdateStraightStateSmoothing();
    }

    void CalculateFrenetCoordinates(Vector3 position, Vector3 upVector,
                                    out float lateralError, out float progressDist,
                                    out Vector3 splinePoint, out Vector3 tangent, out float headingError) {

        // 1. Find the closest baked sample (Local Search)
        int bestIndex = _lastClosestSampleIndex;
        float minSqrDist = float.MaxValue;
        int searchRange = Mathf.Min(localSearchWindow, resolutionSamples / 2);

        for (int offset = -searchRange; offset <= searchRange; offset++) {
            int index = WrapIndex(bestIndex + offset, resolutionSamples);
            float sqrDist = (_samplePoints[index] - position).sqrMagnitude;
            if (sqrDist < minSqrDist) {
                minSqrDist = sqrDist;
                _lastClosestSampleIndex = index;
            }
        }

        // Global search fallback if bike teleports
        if (Mathf.Sqrt(minSqrDist) > 30f) {
            for (int i = 0; i < resolutionSamples; i++) {
                float sqrDist = (_samplePoints[i] - position).sqrMagnitude;
                if (sqrDist < minSqrDist) { minSqrDist = sqrDist; _lastClosestSampleIndex = i; }
            }
        }

        // 2. Project position onto the segment between samples
        int indexA = _lastClosestSampleIndex;
        int indexB = WrapIndex(_lastClosestSampleIndex + 1, resolutionSamples);

        Vector3 pointA = _samplePoints[indexA];
        Vector3 pointB = _samplePoints[indexB];
        Vector3 segmentVec = pointB - pointA;
        float segmentLengthSqr = Mathf.Max(segmentVec.sqrMagnitude, EPSILON);

        float tSegment = Mathf.Clamp01(Vector3.Dot(position - pointA, segmentVec) / segmentLengthSqr);
        splinePoint = pointA + tSegment * segmentVec;

        // 3. Determine Orientations
        tangent = segmentVec.normalized;

        // Handle Reversal Logic
        if (_gameController != null && _gameController.reverseDirection) {
            tangent = -tangent;
        }

        // 4. Calculate Lateral Error (Distance from center)
        Vector3 trackRight = Vector3.Cross(tangent, upVector).normalized;
        lateralError = Vector3.Dot(position - splinePoint, -trackRight);

        // 5. Calculate Progress Distance
        float segmentRealLength = Vector3.Distance(pointA, pointB);
        float rawTotalDist = _cumulativeDistances[indexA] + (tSegment * segmentRealLength);
        progressDist = Mathf.Repeat(rawTotalDist, lapTotalLength);

        // 6. Heading Error
        Vector3 bikeForwardPlanar = Vector3.ProjectOnPlane(bikeTransform.forward, upVector).normalized;
        Vector3 trackForwardPlanar = Vector3.ProjectOnPlane(tangent, upVector).normalized;
        headingError = Vector3.SignedAngle(bikeForwardPlanar, trackForwardPlanar, upVector);

        // 7. Curvature Smoothing
        currentCurvature = Mathf.Lerp(_sampleCurvatures[indexA], _sampleCurvatures[indexB], tSegment);
        _rawIsStraightState = currentCurvature < straightLineThreshold;
    }

    private void UpdateStraightStateSmoothing() {
        if (!useSmoothing) {
            isOnStraightTrack = _rawIsStraightState;
            return;
        }

        if (_rawIsStraightState != isOnStraightTrack) {
            _stateTransitionTimer += Time.deltaTime;
            if (_stateTransitionTimer >= stateChangeDelay) {
                isOnStraightTrack = _rawIsStraightState;
                _stateTransitionTimer = 0f;
            }
        } else {
            _stateTransitionTimer = 0f;
        }
    }

    private Vector3 CalculateGroundNormal(Vector3 origin) {
        if (Physics.Raycast(origin + Vector3.up * 2f, Vector3.down, out var hit, 5f, groundLayerMask))
            return hit.normal.normalized;
        return Vector3.up;
    }

    private int WrapIndex(int x, int m) { int r = x % m; return r < 0 ? r + m : r; }

#if UNITY_EDITOR
    void OnDrawGizmosSelected() {
        if (_samplePoints == null) return;
        Gizmos.color = isOnStraightTrack ? Color.green : Color.red;
        Gizmos.DrawWireSphere(closestPointOnSpline, 0.5f);
        Gizmos.DrawLine(closestPointOnSpline, closestPointOnSpline + trackTangentDirection * 2f);
    }
#endif
}
*/

public class MLClosedSplineFrenet : MonoBehaviour {
    [Header("Probe transforms")]
    public Transform crosstrackProbe;
    public Transform headingErrorProbe;

    [Header("Spline settings")]
    public SplineContainer trackSplineContainer;
    public int splineIndex = 0;

    [Header("Sampling")]
    public int resolutionSamples = 800;
    public int localSearchWindow = 20;

    [Header("Dynamic Lookahead")]
    public bool useDynamicLookahead = true;
    public float minLookahead = 0.5f;
    public float maxLookahead = 1.5f;
    public float speedForMaxLookahead = 20f; // km/h

    [Header("Curvature Detection")]
    public float straightLineThreshold = 0.015f;
    public bool useSmoothing = true;
    public float stateChangeDelay = 0.15f;

    [Header("outputs (read-only)")]
    public bool isOnStraightTrack; 
    public float crossTrackErrorMeters;
    public float wheelHeadingErrorDegrees; // Log this!
    public float dynamicHeadingErrorDegrees; // This is what the LKA uses
    public float currentCurvature;
    public Vector3 closestPointOnSpline;
    public Vector3 trackTangentDirection;

    private Vector3[] _samplePoints;
    private Vector3[] _sampleTangents;
    private float[] _sampleCurvatures;
    private int _lastClosestSampleIndex = 0;
    private GameController _gameController;
    private BikeController _bikeController;

    // Smoothing logic variables
    private bool _rawIsStraightState;
    private float _stateTransitionTimer;


    [Header("VR Debug Visuals")]
    public bool showVrDebug = true;
    private LineRenderer _debugLine;
    private GameObject _wheelSphere;   // The "Truth"
    private GameObject _probeSphere;   // The "Intent"

    void Awake() {
        _gameController = FindObjectOfType<GameController>();
        _bikeController = FindObjectOfType<BikeController>(); // Direct reference
        BakeSplineData();
    }

    void Start() {
        if (showVrDebug) {
            // 1. Create Line
            GameObject lineObj = new GameObject("VR_LKA_Line");
            _debugLine = lineObj.AddComponent<LineRenderer>();
            _debugLine.material = new Material(Shader.Find("Unlit/Color"));
            _debugLine.startWidth = 0.03f;
            _debugLine.endWidth = 0.01f; // Tapers toward the lookahead
            _debugLine.positionCount = 2;

            // 2. Create Wheel Sphere (White)
            _wheelSphere = CreateDebugSphere("Wheel_Sphere", Color.white, 0.1f);

            // 3. Create Probe Sphere (Cyan)
            _probeSphere = CreateDebugSphere("Probe_Sphere", Color.cyan, 0.15f);
        }
    }

    public void BakeSplineData() {
        if (trackSplineContainer == null) return;
        var spline = trackSplineContainer.Splines[splineIndex];
        var worldTransform = trackSplineContainer.transform;
        float worldScale = worldTransform.lossyScale.x;

        _samplePoints = new Vector3[resolutionSamples + 1];
        _sampleTangents = new Vector3[resolutionSamples + 1];
        _sampleCurvatures = new float[resolutionSamples + 1];

        for (int i = 0; i <= resolutionSamples; i++) {
            float t = (float)i / resolutionSamples;
            _samplePoints[i] = worldTransform.TransformPoint((Vector3)spline.EvaluatePosition(t));
            _sampleTangents[i] = worldTransform.TransformDirection((Vector3)spline.EvaluateTangent(t)).normalized;

            float rawCurve = spline.EvaluateCurvature(t);
            _sampleCurvatures[i] = (float.IsNaN(rawCurve) || float.IsInfinity(rawCurve)) ? 0f : Mathf.Abs(rawCurve) / worldScale;
        }
    }

    void Update() {
        if (crosstrackProbe == null || headingErrorProbe == null || _samplePoints == null) return;

        // --- Dynamic Lookahead Logic ---
        if (useDynamicLookahead && _bikeController != null) {
            float speed = _bikeController.BikeSpeed;
            float t = Mathf.Clamp01(speed / speedForMaxLookahead);
            float targetZ = Mathf.Lerp(minLookahead, maxLookahead, t);

            // adjust the probe's local position (assuming Z is forward)
            headingErrorProbe.localPosition = new Vector3(0, 0, targetZ);
        }

        // find closest point for the crosstrack probe
        int bestIndex = _lastClosestSampleIndex;
        float minSqrDist = float.MaxValue;
        for (int offset = -localSearchWindow; offset <= localSearchWindow; offset++) {
            int index = WrapIndex(bestIndex + offset, resolutionSamples);
            float sqrDist = (_samplePoints[index] - crosstrackProbe.position).sqrMagnitude;
            if (sqrDist < minSqrDist) {
                minSqrDist = sqrDist;
                _lastClosestSampleIndex = index;
            }
        }

        // project crosstrack probe onto spline
        int indexA = _lastClosestSampleIndex;
        int indexB = WrapIndex(_lastClosestSampleIndex + 1, resolutionSamples);
        Vector3 segment = _samplePoints[indexB] - _samplePoints[indexA];
        float tSeg = Mathf.Clamp01(Vector3.Dot(crosstrackProbe.position - _samplePoints[indexA], segment) / Mathf.Max(segment.sqrMagnitude, 0.0001f));

        closestPointOnSpline = _samplePoints[indexA] + tSeg * segment;
        trackTangentDirection = Vector3.Slerp(_sampleTangents[indexA], _sampleTangents[indexB], tSeg).normalized;
        if (_gameController != null && _gameController.reverseDirection) trackTangentDirection *= -1;

        // calculate lateral error
        Vector3 flatTangent = Vector3.ProjectOnPlane(trackTangentDirection, Vector3.up).normalized;
        Vector3 flatRight = Vector3.Cross(flatTangent, Vector3.up).normalized;
        crossTrackErrorMeters = Vector3.Dot(crosstrackProbe.position - closestPointOnSpline, -flatRight);

       
        CalculateHeadingForProbe(crosstrackProbe, out wheelHeadingErrorDegrees);
        CalculateHeadingForProbe(headingErrorProbe, out dynamicHeadingErrorDegrees);

        // curvature & Straight State Logic
        currentCurvature = Mathf.Lerp(_sampleCurvatures[indexA], _sampleCurvatures[indexB], tSeg);
        _rawIsStraightState = currentCurvature < straightLineThreshold;
        UpdateStraightStateSmoothing();
    }

    // Helper to keep code clean
    private GameObject CreateDebugSphere(string name, Color col, float scale) {
        GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = name;
        s.transform.localScale = Vector3.one * scale;
        Destroy(s.GetComponent<SphereCollider>());
        s.GetComponent<MeshRenderer>().material = new Material(Shader.Find("Unlit/Color"));
        s.GetComponent<MeshRenderer>().material.color = col;
        return s;
    }

    void LateUpdate() {
        if (showVrDebug && _debugLine != null) {
            _debugLine.SetPosition(0, crosstrackProbe.position);
            _debugLine.SetPosition(1, headingErrorProbe.position);

            _wheelSphere.transform.position = crosstrackProbe.position;
            _probeSphere.transform.position = headingErrorProbe.position;

            // Optional: Change color based on if lookahead is active
            _probeSphere.GetComponent<MeshRenderer>().material.color =
                (Mathf.Abs(crossTrackErrorMeters) > 0.5f) ? Color.red : Color.cyan;
        }
    }

    private void UpdateStraightStateSmoothing() {
        if (!useSmoothing) {
            isOnStraightTrack = _rawIsStraightState;
            return;
        }

        if (_rawIsStraightState != isOnStraightTrack) {
            _stateTransitionTimer += Time.deltaTime;
            if (_stateTransitionTimer >= stateChangeDelay) {
                isOnStraightTrack = _rawIsStraightState;
                _stateTransitionTimer = 0f;
            }
        } else {
            _stateTransitionTimer = 0f;
        }
    }

    private void CalculateHeadingForProbe(Transform probe, out float error) {
        float minSqr = float.MaxValue;
        int bestIdx = _lastClosestSampleIndex;

        // Search neighborhood around current position
        for (int i = 0; i <= localSearchWindow * 2; i++) {
            int idx = WrapIndex(_lastClosestSampleIndex + i, resolutionSamples);
            float d = (_samplePoints[idx] - probe.position).sqrMagnitude;
            if (d < minSqr) {
                minSqr = d;
                bestIdx = idx;
            }
        }

        int nextIdx = WrapIndex(bestIdx + 1, resolutionSamples);
        Vector3 seg = _samplePoints[nextIdx] - _samplePoints[bestIdx];
        float t = Mathf.Clamp01(Vector3.Dot(probe.position - _samplePoints[bestIdx], seg) / Mathf.Max(seg.sqrMagnitude, 0.0001f));

        Vector3 probeTangent = Vector3.Slerp(_sampleTangents[bestIdx], _sampleTangents[nextIdx], t).normalized;
        if (_gameController != null && _gameController.reverseDirection) probeTangent *= -1;

        Vector3 probeForwardPlanar = Vector3.ProjectOnPlane(probe.forward, Vector3.up).normalized;
        Vector3 trackForwardPlanar = Vector3.ProjectOnPlane(probeTangent, Vector3.up).normalized;
        error = Vector3.SignedAngle(probeForwardPlanar, trackForwardPlanar, Vector3.up);
    }

    private int WrapIndex(int x, int m) { int r = x % m; return r < 0 ? r + m : r; }

#if UNITY_EDITOR
    void OnDrawGizmos() {
        if (crosstrackProbe != null) {
            Gizmos.color = isOnStraightTrack ? Color.green : Color.red;
            Gizmos.DrawWireSphere(crosstrackProbe.position, 0.2f);
        }
        if (headingErrorProbe != null) {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(headingErrorProbe.position, 0.2f);
        }
    }
#endif
}


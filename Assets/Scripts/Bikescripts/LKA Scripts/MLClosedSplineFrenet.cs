using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

/// <summary>
/// This script manages the translation of the bike's world position into Frenet coordinates (s, d).
/// Optimized with Global Search for spawns and Velocity Gating to prevent CSV jitters.
/// </summary>
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

    [Header("Outputs (read-only)")]
    public bool isOnStraightTrack;
    public float crossTrackErrorMeters;
    public float bikeHeadingErrorDegrees;
    public float wheelHeadingErrorDegrees;
    public float currentCurvature;
    public float currentRadius;
    public Vector3 closestPointOnSpline;
    public Vector3 trackTangentDirection;

    private Vector3[] _samplePoints;
    private Vector3[] _sampleTangents;
    private float[] _sampleCurvatures;
    private int _lastClosestSampleIndex = 0;
    private GameController _gameController;
    private BikeController _bikeController;

    private bool _rawIsStraightState;
    private float _stateTransitionTimer;

    [Header("VR Debug Visuals")]
    public bool showVrDebug = true;
    private LineRenderer _debugLine;
    private GameObject _wheelSphere;
    private GameObject _probeSphere;

    void Awake() {
        _gameController = FindObjectOfType<GameController>();
        _bikeController = FindObjectOfType<BikeController>();
        BakeSplineData();
    }

    void Start() {
        if (showVrDebug) {
            GameObject lineObj = new GameObject("VR_LKA_Line");
            _debugLine = lineObj.AddComponent<LineRenderer>();
            _debugLine.material = new Material(Shader.Find("Unlit/Color"));
            _debugLine.startWidth = 0.03f;
            _debugLine.endWidth = 0.01f;
            _debugLine.positionCount = 2;

            _wheelSphere = CreateDebugSphere("Wheel_Sphere", Color.white, 0.1f);
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

    void FixedUpdate() {
        if (crosstrackProbe == null || headingErrorProbe == null || _samplePoints == null) return;

        // FIX 1: AUTO-RESET SEARCH WINDOW IF TELEPORTED
        // If the bike is far from the last tracked sample, check the whole track (800 samples)
        float distToLast = Vector3.Distance(crosstrackProbe.position, _samplePoints[_lastClosestSampleIndex]);
        int searchRange = (distToLast > 5f) ? (resolutionSamples / 2) : localSearchWindow;

        int bestIndex = _lastClosestSampleIndex;
        float minSqrDist = float.MaxValue;
        for (int offset = -searchRange; offset <= searchRange; offset++) {
            int index = WrapIndex(bestIndex + offset, resolutionSamples);
            float sqrDist = (_samplePoints[index] - crosstrackProbe.position).sqrMagnitude;
            if (sqrDist < minSqrDist) {
                minSqrDist = sqrDist;
                _lastClosestSampleIndex = index;
            }
        }

        // Interpolation
        int indexA = _lastClosestSampleIndex;
        int indexB = WrapIndex(_lastClosestSampleIndex + 1, resolutionSamples);
        Vector3 segment = _samplePoints[indexB] - _samplePoints[indexA];
        float tSeg = Mathf.Clamp01(Vector3.Dot(crosstrackProbe.position - _samplePoints[indexA], segment) / Mathf.Max(segment.sqrMagnitude, 0.0001f));

        closestPointOnSpline = _samplePoints[indexA] + tSeg * segment;
        trackTangentDirection = Vector3.Slerp(_sampleTangents[indexA], _sampleTangents[indexB], tSeg).normalized;
        if (_gameController != null && _gameController.reverseDirection) trackTangentDirection *= -1;

        // Cross Track Error
        Vector3 flatTangent = Vector3.ProjectOnPlane(trackTangentDirection, Vector3.up).normalized;
        Vector3 flatRight = Vector3.Cross(flatTangent, Vector3.up).normalized;
        crossTrackErrorMeters = Vector3.Dot(crosstrackProbe.position - closestPointOnSpline, -flatRight);

        // Heading Error Calculations
        CalculateHeadingForProbe(crosstrackProbe, out bikeHeadingErrorDegrees);
        CalculateHeadingForProbe(headingErrorProbe, out wheelHeadingErrorDegrees);

        // Curvature Logic
        currentCurvature = Mathf.Lerp(_sampleCurvatures[indexA], _sampleCurvatures[indexB], tSeg);
        currentRadius = (currentCurvature >= straightLineThreshold) ? (1f / currentCurvature) : 0f;

        _rawIsStraightState = currentCurvature < straightLineThreshold;
        UpdateStraightStateSmoothing();
    }

    private void CalculateHeadingForProbe(Transform probe, out float error) {
        // FIX 2: VELOCITY GATING
        // If the bike is barely moving, don't calculate an error. This stops the start-line jitters.
        if (_bikeController != null && _bikeController.BikeSpeedKmh < 0.2f) {
            error = 0f;
            return;
        }

        // Finding local tangent for this specific probe position
        float minSqr = float.MaxValue;
        int bestIdx = _lastClosestSampleIndex;
        for (int i = -localSearchWindow; i <= localSearchWindow; i++) {
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

        // FIX 3: THE WRAPPER
        // Vector3.SignedAngle handles the -180 to 180 wrap automatically.
        error = -Vector3.SignedAngle(probeForwardPlanar, trackForwardPlanar, Vector3.up);
    }

    private int WrapIndex(int x, int m) { int r = x % m; return r < 0 ? r + m : r; }

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
            _probeSphere.GetComponent<MeshRenderer>().material.color = (Mathf.Abs(crossTrackErrorMeters) > 0.5f) ? Color.red : Color.cyan;
        }
    }

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
using UnityEngine;
using UnityEngine.Splines;
using System;

/// <summary>
/// Frenet-Estimator für geschlossene Center-Splines:
/// - crossTrackError (links +)
/// - s (0..totalLength)
/// - headingErrorDeg
/// </summary>
public class MLClosedSplineFrenet_old: MonoBehaviour
{
    [Header("Input")]
    public Transform bike;
    public SplineContainer centerLine;
    public int splineIndex = 0;          // falls mehrere Splines im Container

    [Header("Sampling")]
    [Tooltip("Mehr Samples = genauer. 400–1000 ist gut.")]
    public int samples = 600;
    [Tooltip("Wie viele Nachbarsegmente beim lokalen Search betrachtet werden.")]
    public int localWindow = 12;

    [Header("Up/Neigung")]
    public bool useGroundNormal = false;
    public LayerMask groundMask = ~0;
    public Vector3 roadUp = Vector3.up;

    [Header("Outputs (read-only)")]
    public float crossTrackError;    // e_y (links +)
    public float s;                  // Bogenlänge entlang der Spline [0, totalLength)
    public float headingErrorDeg;    // Grad
    public Vector3 nearestPoint;     // P*
    public Vector3 tangent;          // Tangente an P*
    public float totalLength;

    // cache
    Vector3[] pts;       // positions
    Vector3[] tans;      // tangents (unit)
    float[] cumLen;      // cumulative arc length (cumLen[0]=0, cumLen[N]=total)
    int N;               // samples
    int lastBestK = 0;   // seed für lokale Suche

    void Awake() => Bake();
    void OnValidate() { samples = Mathf.Max(32, samples); localWindow = Mathf.Clamp(localWindow, 4, 64); }

    /*void Bake()
    {
        if (centerLine == null) return;

        var spline = centerLine.Splines[splineIndex];
        N = samples;
        pts = new Vector3[N + 1];
        tans = new Vector3[N + 1];
        cumLen = new float[N + 1];
        totalLength = 0f;

        // gleichmäßiges Param-Sampling (t von 0..1), N Segmente, letzter Punkt = erster Punkt (closed)
        for (int i = 0; i <= N; i++)
        {
            float t = (float)i / N;
            pts[i] = spline.EvaluatePosition(t);
            //var tan = spline.EvaluateTangent(t);
            Vector3 tan = (Vector3)spline.EvaluateTangent(t);
            tan.y = tan.y; // bleibt wie ist; normalisieren später
            tans[i] = tan.normalized;

            if (i > 0)
            {
                float seg = Vector3.Distance(pts[i - 1], pts[i]);
                totalLength += seg;
                cumLen[i] = totalLength;
            }
            else cumLen[i] = 0f;
        }

        // forciere "geschlossen": verbinde N->0 in Längenlogik implizit über Wrap bei Projektion
        lastBestK = 0;
    }*/
    void Bake() {
        if (centerLine == null) return;

        var spline = centerLine.Splines[splineIndex];
        var tf = centerLine.transform; // ← Transform holen!

        N = samples;
        pts = new Vector3[N + 1];
        tans = new Vector3[N + 1];
        cumLen = new float[N + 1];
        totalLength = 0f;

        for (int i = 0; i <= N; i++)
        {
            float t = (float)i / N;

            // --- Hier korrigiert: Lokale Punkte → Weltpunkte ---
            Vector3 localPos = (Vector3)spline.EvaluatePosition(t);
            Vector3 localTan = (Vector3)spline.EvaluateTangent(t);

            pts[i] = tf.TransformPoint(localPos);             // → Weltposition
            tans[i] = tf.TransformDirection(localTan).normalized; // → Welt-Tangente

            if (i > 0)
            {
                float seg = Vector3.Distance(pts[i - 1], pts[i]);
                totalLength += seg;
                cumLen[i] = totalLength;
            }
            else
                cumLen[i] = 0f;
        }

        lastBestK = 0;
    }

    void Update() {
        if (centerLine == null || bike == null || pts == null || pts.Length == 0) return;

        if (useGroundNormal)
            roadUp = SampleGroundUp(bike.position);
        else
            roadUp = Vector3.up;

        EvaluateAt(bike.position, roadUp, out crossTrackError, out s, out nearestPoint, out tangent, out headingErrorDeg);
    }

    void EvaluateAt(Vector3 pos, Vector3 up, out float ey, out float sOut, out Vector3 pStar, out Vector3 tHat, out float hdgErrDeg) {
        // 1) Lokale Suche um lastBestK mit Wrap
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

        // 2) Kandidaten-Segment [k, k+1] (mit Wrap)
        int aK = bestK;
        int bK = Mod(bestK + 1, N);
        Vector3 A = pts[aK];
        Vector3 B = pts[bK];
        Vector3 AB = B - A;
        float ab2 = Mathf.Max(AB.sqrMagnitude, 1e-6f);

        float tSeg = Mathf.Clamp01(Vector3.Dot(pos - A, AB) / ab2);
        pStar = A + tSeg * AB;

        // 3) Tangente: linear zwischen Sample-Tangenten nähern (oder aus AB)
        // (AB ist in Uniform-t Sampling meist ruhiger)
        tHat = AB.normalized;
        if (tHat.sqrMagnitude < 0.5f) // falls sehr kurz (selten), fallback
            tHat = Vector3.Slerp(tans[aK], tans[bK], tSeg).normalized;

        // 4) signierter Querfehler: links positiv
        Vector3 nRight = Vector3.Cross(tHat, up).normalized; // zeigt "rechts"
        ey = Vector3.Dot(pos - pStar, -nRight);               // invertieren → links positiv

        // 5) Bogenlänge s (0..totalLength) am Projektionspunkt
        float segLen = Vector3.Distance(A, B);
        float baseLen = cumLen[aK];
        float sRaw = baseLen + tSeg * segLen;
        // Wrap in [0, totalLength)
        sOut = (sRaw >= totalLength) ? (sRaw - totalLength) : ((sRaw < 0f) ? (sRaw + totalLength) : sRaw);

        // 6) Heading-Fehler (Yaw auf der Ebene)
        Vector3 fwd = Vector3.ProjectOnPlane(bike.forward, up).normalized;
        Vector3 tPlanar = Vector3.ProjectOnPlane(tHat, up).normalized;
        hdgErrDeg = Vector3.SignedAngle(fwd, tPlanar, up);
    }


    Vector3 SampleGroundUp(Vector3 origin) {
        if (Physics.Raycast(origin + Vector3.up * 2f, Vector3.down, out var hit, 5f, groundMask))
            return hit.normal.normalized;
        return Vector3.up;
    }

    int Mod(int x, int m) { int r = x % m; return r < 0 ? r + m : r; }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(nearestPoint, 0.08f);
        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(nearestPoint, nearestPoint + tangent * 0.8f);
    }
#endif
}
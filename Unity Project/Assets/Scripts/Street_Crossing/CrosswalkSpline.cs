using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;   // für float3

public class RandomSplineWalker : MonoBehaviour
{
    [Header("Routen")]
    public SplineContainer[] routes;  // alle möglichen Splines
    public bool pickNewRouteAtEnd = false; // neue Route wählen, wenn er am Ende ist?
    public bool destroyAtEnd = false;      // NPC am Ende despawnen?
    public bool loopSameRoute = true;      // gleiche Route immer wieder?

    [Header("Bewegung")]
    public float speed = 2f;        // m/s entlang der Kurve

    private SplineContainer currentRoute;
    private float currentLength;
    private float t = 0f;           // 0..1 Position auf der aktuellen Route

    void Start()
    {
        PickRandomRoute();
    }

    void PickRandomRoute()
    {
        if (routes == null || routes.Length == 0)
        {
            Debug.LogWarning("RandomSplineWalker: Keine Routes gesetzt!", this);
            currentRoute = null;
            return;
        }

        currentRoute = routes[UnityEngine.Random.Range(0, routes.Length)];
        if (currentRoute == null)
        {
            Debug.LogWarning("RandomSplineWalker: Eine Route ist null!", this);
            return;
        }

        currentLength = currentRoute.CalculateLength();
        if (currentLength <= 0f)
        {
            Debug.LogWarning("RandomSplineWalker: Route hat Länge 0!", this);
        }

        t = 0f; // immer am Start der neuen Route beginnen
    }

    void Update()
    {
        if (currentRoute == null || currentLength <= 0f)
            return;

        // physikalisch sinnvolle Geschwindigkeit: Meter pro Sekunde
        float deltaT = (speed * Time.deltaTime) / currentLength;
        t += deltaT;

        if (t > 1f)
        {
            // wir sind am Ende der Route angekommen
            if (pickNewRouteAtEnd)
            {
                PickRandomRoute();
            }
            else if (loopSameRoute)
            {
                t = 0f;
            }
            else if (destroyAtEnd)
            {
                Destroy(gameObject);
                return;
            }
            else
            {
                t = 1f; // stehen bleiben
            }
        }

        // Position & Richtung auf der Spline auswerten
        if (currentRoute.Evaluate(t, out float3 pos, out float3 tangent, out float3 up))
        {
            Vector3 worldPos = (Vector3)pos;
            Vector3 dir = (Vector3)tangent;

            transform.position = worldPos;

            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion rot = Quaternion.LookRotation(dir, (Vector3)up);
                transform.rotation = rot;
            }
        }
    }
}

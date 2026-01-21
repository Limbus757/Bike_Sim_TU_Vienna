using UnityEngine;

public class WaypointWalker : MonoBehaviour
{
    public Transform[] waypoints;
    public float speed = 2f;
    public bool loop = true;
    public Animator animator; // optional

    private int currentIndex = 0;

    void Start()
    {
        // Falls keine Waypoints gesetzt sind, lieber nichts tun
        if (waypoints == null || waypoints.Length == 0) return;

        // NPC auf den ersten Waypoint setzen (optional)
        transform.position = waypoints[0].position;
    }

    void Update()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        Transform target = waypoints[currentIndex];
        Vector3 dir = target.position - transform.position;
        float distance = dir.magnitude;

        // Sind wir nah genug am aktuellen Wegpunkt?
        if (distance < 0.1f)
        {
            currentIndex++;

            if (currentIndex >= waypoints.Length)
            {
                if (loop)
                {
                    currentIndex = 0; // wieder von vorne
                }
                else
                {
                    // am Ende der Route angekommen
                    if (animator != null)
                        animator.SetBool("IsWalking", false);

                    return;
                }
            }

            return;
        }

        // Bewegen
        Vector3 moveDir = dir.normalized;
        transform.position += moveDir * speed * Time.deltaTime;

        // In Laufrichtung drehen
        if (moveDir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 10f * Time.deltaTime);
        }

        // Animation (optional)
        if (animator != null)
            animator.SetBool("Walking", true);
    }
}
using UnityEngine;

/// <summary>
/// Detects when a bike enters specific study milestones (Start or Finish) 
/// and notifies the GameController to update the study state.
/// </summary>
public class StudyTrigger : MonoBehaviour {

    /// <summary>
    /// Categorizes the trigger's purpose within the track layout.
    /// </summary>
    public enum TriggerType { StartLine, FinishLine }

    [Header("Trigger Configuration")]
    [Tooltip("Designate whether this object acts as the starting point or the lap/finish point.")]
    public TriggerType type;

    [Header("References")]
    [Tooltip("The main game controller that handles the logic for when a trigger is hit.")]
    private GameController gameController;

    /// <summary>
    /// Finds the GameController in the scene at the start of the session.
    /// </summary>
    void Start() {
        gameController = FindObjectOfType<GameController>();

        if (gameController == null) {
            Debug.LogWarning($"StudyTrigger on {gameObject.name}: No GameController found in scene!");
        }
    }

    /// <summary>
    /// Triggered when another collider enters this object's trigger zone.
    /// Identifies if the object is a bike and notifies the controller of the specific milestone reached.
    /// </summary>
    /// <param name="other">The collider that entered the trigger zone.</param>
    private void OnTriggerEnter(Collider other) {
        // Look for the BikeController anywhere in the object that hit us (or its parents)
        var bike = other.GetComponentInParent<BikeController>();

        // If the object that entered is indeed a bike, notify the controller of the specific event
        if (bike != null) {
            if (gameController != null) {
                // Fire the event to record the pass based on this trigger's specific type
                gameController.OnTriggerHit(type);

                // Optional debug to confirm which type of trigger was activated
                Debug.Log($"StudyTrigger: Bike {bike.name} detected at {type}.");
            }
        }
    }
}
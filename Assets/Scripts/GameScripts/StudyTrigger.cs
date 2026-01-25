using UnityEngine;

/// <summary>
/// Detects when a bike enters a specific area and notifies the GameController.
/// Used for lap timing and ending a study session.
/// </summary>
public class StudyTrigger : MonoBehaviour {

    [Header("References")]
    [Tooltip("The main game controller that handles the logic for when a trigger is hit.")]
    private GameController gameController;

    /// <summary>
    /// finds the GameController in the scene at the start of the session.
    /// </summary>
    void Start() {
        gameController = FindObjectOfType<GameController>(); // locate the game controller to send events to it later

        if (gameController == null) {
            Debug.LogWarning($"StudyTrigger on {gameObject.name}: No GameController found in scene!");
        }
    }

    /// <summary>
    /// triggered when another collider enters this object's trigger zone.
    /// </summary>
    /// <param name="other">the collider that entered the trigger.</param>
    private void OnTriggerEnter(Collider other) {
        // look for the BikeController anywhere in the object that hit us (or its parents)
        // this ensures that hitting the wheel, frame, or handlebar still triggers the event
        var bike = other.GetComponentInParent<BikeController>();

        // if the object that entered is indeed a bike, notify the controller
        if (bike != null) {
            if (gameController != null) {
                // fire the event to record the pass
                gameController.OnBikePassedTrigger();

                // optional debug to confirm the trigger worked in the console
                Debug.Log($"StudyTrigger: Bike {bike.name} detected.");
            }
        }
    }
}
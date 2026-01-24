using UnityEngine;

public class StudyTrigger : MonoBehaviour {
    private GameController gameController;

    void Start() {
        gameController = FindObjectOfType<GameController>();
    }

    private void OnTriggerEnter(Collider other) {
        // Look for the BikeController anywhere in the object that hit us
        var bike = other.GetComponentInParent<BikeController>();

        if (bike != null) {
            if (gameController != null) {
                gameController.OnBikePassedTrigger();
            }
        }
    }
}
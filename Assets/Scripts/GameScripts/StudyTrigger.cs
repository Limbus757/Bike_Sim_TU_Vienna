using UnityEngine;

public class StudyTrigger : MonoBehaviour {
    private GameController gameController;

    void Start() {
        gameController = FindObjectOfType<GameController>();
    }

    private void OnTriggerEnter(Collider other) {
        // Triggers when the bike (tagged "Player") passes through
        if (other.CompareTag("Player") || other.transform.root.CompareTag("Player")) {
            if (gameController != null) {
                gameController.OnBikePassedTrigger();
            }
        }
    }
}
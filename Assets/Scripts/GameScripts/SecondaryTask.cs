using TMPro;
using UnityEngine;

/// <summary>
/// This script manages a visual 'Oddball' or 'Detection' task.
/// It displays random numbers at a set interval and tracks if the user identifies a 'target'.
/// </summary>
public class SecondaryTask : MonoBehaviour {
    [Header("Display Element")]
    public TMP_Text numberText;

    [Header("Range of numbers generated (inclusive)")]
    public int minNumber = 0;
    public int maxNumber = 9;
    public float intervalSeconds = 2.0f; // how long each number stays on screen

    [Header("Target Number (optional)")] 
    public int targetNumber = 7;
    public bool useTarget = true;

    [Header("Public state")]
    public int currentNumber = -1; // the number currently shown on screen
    public bool isTarget = false; // true if the current number matches the targetNumber
    public bool buttonPressed = false; // true while the participant is holding the button

    // internal timer to track the next change
    private float nextTime;

    void Start() {
        // Set the timer for the first interval
        nextTime = Time.time + intervalSeconds;

        // Show the first number immediately on start
        GenerateNewNumber();
    }

    void Update() {
        // Check if the timer has expired
        if (Time.time >= nextTime) {
            GenerateNewNumber();
            nextTime = Time.time + intervalSeconds;
        }

        // Monitors the raw input state, this uses .Get(), meaning it is only true while the button is physically held down.
        buttonPressed = OVRInput.Get(OVRInput.RawButton.X, OVRInput.Controller.LTouch);
    }

    /// <summary>
    /// Picks a random number and updates the UI and target status.
    /// </summary>
    private void GenerateNewNumber() {
        currentNumber = Random.Range(minNumber, maxNumber + 1);
        isTarget = useTarget && (currentNumber == targetNumber);

        // update display
        if (numberText != null) {
            numberText.text = currentNumber.ToString();
        }
    }

    /// <summary>
    /// This allows the DataLogger to manually reset the button state after it has recorded a '1' in the CSV.
    /// Useful for turning an "input hold" into a single "input event" in data analysis.
    /// </summary>
    public void ConsumeButtonPress() {
        buttonPressed = false;
    }
}
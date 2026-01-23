using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class SecondaryTask : MonoBehaviour
{
    [Header("Display")]
    public TMP_Text numberText; // <- hier Display zuweisen

    [Header("Number Generation")]
    public int minNumber = 0;
    public int maxNumber = 9;
    public float intervalSeconds = 2.0f;

    [Header("Target (optional)")]
    public int targetNumber = 7;
    public bool useTarget = true;

    [Header("Public state (read by Logger)")]
    public int currentNumber = -1;
    public bool isTarget = false;
    public bool buttonPressed = false;


    private float nextTime;

    void Start()
    {
        nextTime = Time.time + intervalSeconds;
        GenerateNewNumber(); // direkt am Start eine Zahl anzeigen
    }

    void Update()
    {
        // Generate new number after interval
        if (Time.time >= nextTime)
        {
            GenerateNewNumber();
            nextTime = Time.time + intervalSeconds;
        }

        // Input (press stays true until consumed)
        bool yLeft = OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch);

        if (yLeft)
        {
            buttonPressed = true;
        }
    }

    private void GenerateNewNumber()
    {
        currentNumber = Random.Range(minNumber, maxNumber + 1);
        isTarget = useTarget && (currentNumber == targetNumber);

        // Update UI display
        if (numberText != null)
        {
            numberText.text = currentNumber.ToString();
        }
    }

    // Logger kann das nach dem Auslesen aufrufen (optional)
    public void ConsumeButtonPress()
    {
        buttonPressed = false;
    }
}

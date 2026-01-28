using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;

/// <summary>
/// Displays a sequence either as pure random or as an N-Back task.
/// Target detection: buttonPressed is true while X is held.
/// </summary>
public class SecondaryTask : MonoBehaviour
{

    [Header("Display Element")]
    public TMP_Text numberText;

    [Header("Timing")]
    public float intervalSeconds = 1.0f; // how long each number stays on screen

    [Header("Public state")]
    public int currentNumber = -1;
    public bool isTarget = false;
    public bool buttonPressed = false;

    private float nextTime = 0f;

    private GameController gameController;

    // counts NON-targets since last target
    private int nonTargetStreak = 999;
    private int[] a = null;//= new int[120];
    private int[] a1 = { 8, 9, 6, 7, 5, 4, 9, 3, 0, 1, 0, 8, 1, 3, 2, 0, 3, 6, 7, 1, 8, 7, 4, 3, 1, 9, 0, 6, 0, 7, 5, 1, 2, 3, 6, 1, 4, 0, 3, 1, 9, 5, 3, 0, 2, 3, 1, 3, 2, 5, 7, 6, 2, 0, 2, 3, 7, 1, 8, 9, 3, 0, 7, 8, 0, 3, 1, 4, 6, 8, 1, 3, 9, 4, 1, 3, 4, 5, 6, 9, 3, 0, 1, 2, 7, 0, 3, 7, 2, 6, 0, 4, 0, 5, 9, 7, 4, 1, 2, 0, 4, 7, 5, 4, 7, 6, 0, 8, 4, 9, 4, 5, 7, 0, 2, 1, 6, 2, 3, 7 };
    private int[] a2 = { 0, 1, 0, 7, 6, 5, 1, 8, 6, 5, 2, 6, 0, 2, 5, 7, 6, 9, 7, 3, 2, 7, 3, 1, 4, 8, 5, 2, 0, 8, 7, 6, 8, 7, 2, 5, 6, 2, 4, 3, 8, 2, 1, 4, 7, 3, 7, 8, 6, 5, 7, 8, 9, 7, 3, 8, 1, 9, 4, 3, 0, 1, 5, 4, 5, 2, 1, 8, 0, 9, 7, 1, 6, 4, 8, 3, 6, 5, 1, 3, 7, 5, 4, 3, 9, 2, 8, 4, 1, 4, 0, 5, 3, 1, 2, 6, 4, 7, 9, 8, 2, 7, 4, 3, 5, 4, 9, 4, 7, 8, 2, 4, 1, 3, 2, 8, 7, 9, 2, 5 };
    private int[] a3 = { 4, 9, 2, 3, 8, 0, 9, 2, 7, 5, 6, 9, 7, 2, 6, 4, 3, 6, 9, 1, 2, 0, 1, 0, 9, 4, 0, 8, 1, 7, 9, 1, 5, 9, 7, 5, 0, 2, 1, 6, 9, 3, 5, 8, 4, 7, 8, 4, 2, 8, 4, 2, 1, 0, 3, 5, 6, 3, 7, 3, 6, 5, 1, 8, 4, 2, 7, 5, 6, 7, 2, 5, 6, 3, 4, 6, 8, 7, 1, 9, 4, 6, 2, 5, 0, 9, 3, 7, 2, 3, 0, 6, 9, 6, 5, 8, 7, 3, 0, 5, 4, 3, 6, 5, 2, 5, 6, 3, 9, 5, 4, 7, 3, 8, 6, 2, 1, 6, 8, 2 };
    private int[] a4 = { 7, 1, 5, 3, 8, 3, 9, 4, 6, 0, 5, 4, 1, 7, 9, 1, 8, 2, 9, 5, 6, 3, 9, 8, 4, 7, 9, 4, 1, 3, 5, 8, 3, 2, 1, 0, 7, 1, 3, 1, 0, 5, 3, 7, 1, 0, 6, 5, 9, 3, 5, 4, 8, 6, 8, 2, 1, 3, 4, 1, 7, 6, 8, 7, 0, 5, 6, 8, 6, 2, 4, 5, 3, 1, 5, 0, 2, 5, 3, 6, 1, 2, 5, 8, 6, 4, 1, 6, 9, 3, 0, 4, 0, 7, 9, 8, 1, 0, 3, 6, 4, 8, 9, 3, 2, 9, 5, 2, 0, 1, 2, 6, 2, 5, 3, 7, 5, 2, 6, 0 };
    private int[] a5 = { 3, 5, 6, 2, 9, 0, 1, 6, 1, 0, 4, 7, 0, 3, 6, 4, 3, 9, 5, 4, 0, 1, 4, 2, 8, 0, 6, 3, 2, 9, 7, 8, 6, 3, 8, 5, 2, 0, 3, 2, 4, 9, 2, 0, 7, 9, 5, 4, 2, 8, 5, 9, 4, 3, 8, 3, 9, 8, 4, 5, 9, 3, 4, 1, 5, 8, 2, 6, 0, 5, 8, 9, 7, 8, 7, 0, 3, 9, 4, 6, 7, 4, 8, 5, 4, 6, 7, 2, 4, 9, 7, 8, 5, 3, 8, 0, 6, 2, 6, 9, 1, 7, 8, 3, 4, 2, 3, 6, 0, 7, 4, 3, 7, 8, 5, 4, 7, 5, 3, 4 };
    private int[] a6 = { 4, 3, 5, 7, 6, 1, 2, 5, 8, 3, 6, 7, 2, 0, 6, 1, 3, 1, 7, 6, 5, 7, 6, 5, 8, 0, 9, 6, 4, 7, 2, 3, 8, 5, 0, 6, 0, 1, 5, 4, 1, 6, 4, 0, 7, 1, 2, 4, 6, 5, 4, 7, 0, 8, 0, 3, 9, 5, 6, 4, 8, 9, 8, 7, 1, 3, 8, 2, 9, 3, 1, 6, 5, 9, 2, 1, 3, 4, 8, 1, 3, 8, 9, 4, 1, 2, 0, 8, 1, 8, 7, 6, 8, 5, 1, 7, 2, 1, 3, 4, 5, 7, 3, 9, 4, 3, 2, 0, 6, 4, 6, 2, 4, 3, 1, 2, 8, 5, 4, 9 };
    private int[] a7 = { 4, 5, 2, 0, 6, 5, 4, 2, 1, 8, 8, 2, 7, 4, 3, 0, 1, 0, 9, 4, 5, 2, 9, 6, 3, 8, 0, 7, 0, 1, 4, 0, 6, 2, 5, 6, 2, 5, 4, 3, 8, 9, 5, 1, 7, 1, 0, 3, 4, 1, 2, 8, 0, 3, 2, 1, 4, 3, 5, 2, 7, 5, 2, 5, 6, 4, 2, 3, 8, 4, 2, 8, 5, 4, 3, 8, 0, 7, 6, 8, 4, 0, 6, 3, 0, 3, 5, 0, 2, 9, 8, 6, 5, 2, 0, 7, 2, 4, 0, 1, 3, 4, 2, 5, 3, 7, 9, 0, 4, 0, 7, 9, 5, 8, 2, 3, 4, 7, 6, 8 };
    private List<int[]> ints = new List<int[]>();
    private int index = 0;
    void Start()
    {
        ints.Add(a1);
        ints.Add(a2);
        ints.Add(a3);
        ints.Add(a4);
        ints.Add(a5);
        ints.Add(a6);
        ints.Add(a7);
        gameController = FindObjectOfType<GameController>();

    }

    void Update() 
    {
        if (Time.time >= nextTime && gameController.studyStarted)
        {
            if(a == null)
            {
                a = ints[((int)gameController.currentCondition)];
            }
            currentNumber = get_next_number();
            numberText.text = currentNumber.ToString();
            isTarget = Check2Back(currentNumber);
            nextTime = Time.time + intervalSeconds;
        }

        // Held input state
        buttonPressed = OVRInput.Get(OVRInput.RawButton.X, OVRInput.Controller.LTouch);
    }
    int get_next_number()
    {
        index = (index >=120)? 0 : index;
        int n = a[index];
        index++;
        return n;

    }
    static Queue<int> buffer = new Queue<int>();

    static bool Check2Back(int value)
    {
        if (buffer.Count < 2)
        {
            buffer.Enqueue(value);
            return false;
        }

        bool match = buffer.Peek() == value;
        buffer.Dequeue();
        buffer.Enqueue(value);

        return match;
    }

}

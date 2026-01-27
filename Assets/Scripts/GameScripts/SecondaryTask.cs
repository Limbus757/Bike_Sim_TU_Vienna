using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Displays a sequence either as pure random or as an N-Back task.
/// Target detection: buttonPressed is true while X is held.
/// </summary>
public class SecondaryTask : MonoBehaviour
{
    public enum TaskMode
    {
        Random,
        NBackClassic,        // targets occur when number == n-back value (probability-controlled)
        NBackFixedRemember   // forces patterns like 7, (n-1 random), 7 (remember number can be fixed or random at start)
    }

    [Header("Display Element")]
    public TMP_Text numberText;

    [Header("Range of numbers generated (inclusive)")]
    public int minNumber = 0;
    public int maxNumber = 9;

    [Header("Timing")]
    public float intervalSeconds = 2.0f; // how long each number stays on screen

    [Header("Mode Switch")]
    public TaskMode mode = TaskMode.Random;

    [Header("N-Back Settings")]
    [Min(1)] public int nBack = 2;

    [Tooltip("In NBackClassic: probability that the next item will be a target (if history is available).")]
    [Range(0f, 1f)] public float targetProbability = 0.25f;

    [Header("Fixed Remember Variant")]
    [Tooltip("If true, rememberNumber is chosen randomly at Start(). If false, the value below is used.")]
    public bool randomRememberNumberAtStart = false;

    [Tooltip("Used when randomRememberNumberAtStart is false.")]
    public int rememberNumber = 7;

    [Tooltip("In NBackFixedRemember: probability to start a forced pattern block (remember, fillers, remember).")]
    [Range(0f, 1f)] public float startPatternProbability = 0.25f;

    [Header("Target pacing")]
    [Tooltip("Minimum number of NON-target stimuli between targets.")]
    [Min(0)] public int minNonTargetsBetweenTargets = 3;

    [Header("Public state")]
    public int currentNumber = -1;
    public bool isTarget = false;
    public bool buttonPressed = false;

    private float nextTime;

    // N-back history (stores last nBack numbers)
    private readonly Queue<int> history = new Queue<int>();

    // State for fixed-remember pattern: remember, (nBack-1 fillers), remember
    private bool inFixedPattern = false;
    private int fixedPatternStep = 0; // 0 => first remember, 1..nBack-1 fillers, nBack => second remember

    // counts NON-targets since last target
    private int nonTargetStreak = 999;

    void Start()
    {
        if (randomRememberNumberAtStart)
            rememberNumber = Random.Range(minNumber, maxNumber + 1);

        nextTime = Time.time + intervalSeconds;
        GenerateNext();
    }

    void Update()
    {
        if (Time.time >= nextTime)
        {
            GenerateNext();
            nextTime = Time.time + intervalSeconds;
        }

        // Held input state
        buttonPressed = OVRInput.Get(OVRInput.RawButton.X, OVRInput.Controller.LTouch);
    }

    private void GenerateNext()
    {
        int next;

        switch (mode)
        {
            case TaskMode.Random:
                next = Random.Range(minNumber, maxNumber + 1);
                break;

            case TaskMode.NBackClassic:
                next = GenerateNBackClassic();
                break;

            case TaskMode.NBackFixedRemember:
                next = GenerateNBackFixedRemember();
                break;

            default:
                next = Random.Range(minNumber, maxNumber + 1);
                break;
        }

        // Determine target: in N-back modes, target means "matches n-back value"
        int? nBackValue = GetNBackValue();
        bool wouldBeTarget = (mode != TaskMode.Random) && nBackValue.HasValue && (next == nBackValue.Value);

        // Enforce pacing: if target would occur too soon, force a non-target
        if (wouldBeTarget && nonTargetStreak < minNonTargetsBetweenTargets && nBackValue.HasValue)
        {
            // avoid the n-back value so it becomes non-target
            next = RandomExcluding(nBackValue.Value);
            wouldBeTarget = false;

            // IMPORTANT: if we were in a fixed pattern and we "killed" the target step,
            // we should also cancel the pattern to avoid weird partial blocks.
            if (mode == TaskMode.NBackFixedRemember && inFixedPattern)
            {
                inFixedPattern = false;
                fixedPatternStep = 0;
            }
        }

        isTarget = wouldBeTarget;

        // Commit
        currentNumber = next;
        PushHistory(next);

        // update pacing counter
        if (mode == TaskMode.Random)
        {
            // Random mode: doesn't matter, but keep it sane
            nonTargetStreak++;
        }
        else
        {
            if (isTarget) nonTargetStreak = 0;
            else nonTargetStreak++;
        }

        if (numberText != null)
            numberText.text = currentNumber.ToString();
    }

    private bool CooldownAllowsTarget()
    {
        return nonTargetStreak >= minNonTargetsBetweenTargets;
    }

    private int GenerateNBackClassic()
    {
        int? nBackValue = GetNBackValue();

        // If we don't yet have enough history, just output random
        if (!nBackValue.HasValue)
            return Random.Range(minNumber, maxNumber + 1);

        // If we're in cooldown, we MUST avoid targets
        if (!CooldownAllowsTarget())
            return RandomExcluding(nBackValue.Value);

        // Decide if we want a target
        bool makeTarget = (Random.value < targetProbability);

        if (makeTarget)
        {
            return nBackValue.Value; // force target
        }
        else
        {
            // pick a number that is NOT the n-back value to avoid accidental target
            return RandomExcluding(nBackValue.Value);
        }
    }

    private int GenerateNBackFixedRemember()
    {
        // Start a new forced pattern block occasionally,
        // BUT only if cooldown allows targets (because the block will end with a target).
        if (!inFixedPattern && CooldownAllowsTarget() && Random.value < startPatternProbability)
        {
            inFixedPattern = true;
            fixedPatternStep = 0;
        }

        if (!inFixedPattern)
        {
            // normal random stream, but avoid accidentally creating n-back targets too often:
            int? nBackValue = GetNBackValue();
            if (nBackValue.HasValue)
                return RandomExcluding(nBackValue.Value);

            return Random.Range(minNumber, maxNumber + 1);
        }

        // We are in the forced pattern:
        // step 0 => rememberNumber
        // steps 1..nBack-1 => fillers
        // step nBack => rememberNumber again (this should create an n-back target)
        int result;

        if (fixedPatternStep == 0)
        {
            // First remember number (NOT necessarily a target, depends on history)
            result = rememberNumber;
        }
        else if (fixedPatternStep < nBack)
        {
            // filler: avoid rememberNumber AND avoid n-back value to reduce accidental targets
            int? nBackValue = GetNBackValue();
            result = RandomExcludingMultiple(rememberNumber, nBackValue);
        }
        else
        {
            // This is the "target" step of the block.
            // If cooldown doesn't allow, we abort block and output a safe non-target instead.
            int? nBackValue = GetNBackValue();
            if (nBackValue.HasValue && !CooldownAllowsTarget())
            {
                inFixedPattern = false;
                fixedPatternStep = 0;
                return RandomExcluding(nBackValue.Value);
            }

            result = rememberNumber;

            // end block
            inFixedPattern = false;
            fixedPatternStep = 0;
            return result;
        }

        fixedPatternStep++;
        return result;
    }

    private int? GetNBackValue()
    {
        if (history.Count < nBack) return null;

        // Queue iteration: the oldest element in the queue is the n-back value (because we keep size == nBack)
        foreach (var v in history)
            return v;

        return null;
    }

    private void PushHistory(int value)
    {
        history.Enqueue(value);
        while (history.Count > nBack)
            history.Dequeue();
    }

    private int RandomExcluding(int excluded)
    {
        if (minNumber == maxNumber) return minNumber;

        int v;
        int safety = 100;
        do
        {
            v = Random.Range(minNumber, maxNumber + 1);
            safety--;
        } while (v == excluded && safety > 0);

        return v;
    }

    private int RandomExcludingMultiple(int excludedA, int? excludedB)
    {
        if (minNumber == maxNumber) return minNumber;

        int v;
        int safety = 200;
        do
        {
            v = Random.Range(minNumber, maxNumber + 1);
            safety--;
        } while (
            (v == excludedA || (excludedB.HasValue && v == excludedB.Value)) &&
            safety > 0
        );

        return v;
    }

    /// <summary>
    /// Allows a logger to turn a "hold" into a single "event" in analysis.
    /// </summary>
    public void ConsumeButtonPress()
    {
        buttonPressed = false;
    }
}

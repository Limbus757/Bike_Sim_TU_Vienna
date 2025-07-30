using System;
using System.Runtime.InteropServices;
using MotionSystems;
using UnityEngine;

public class FSMIController : MonoBehaviour
{
    // Public Constants related to FSMI
    public const int PLATFORM_POSITION_LOGIC_MIN = -32767;
    public const int PLATFORM_POSITION_LOGIC_MAX = 32767;
    public const float DRAWING_HEAVE_MAX = 1.0f;
    private const float DRAWING_HEAVE_STEP = 0.05f;
    public const float DRAWING_PITCH_MAX = 2;
    private const float DRAWING_PITCH_STEP = 0.1f;
    public const float DRAWING_ROLL_MAX = 2;
    private const float DRAWING_ROLL_STEP = 0.1f;

    // Private FSMI-related variables
    private GameObject m_shaft = null;
    private GameObject m_board = null;
    private Vector3 m_originPosition;
    private Vector3 m_originRotation;
    private float m_heave = 0;
    private float m_pitch = 0;
    private float m_roll = 0;
    private ForceSeatMI m_fsmi;
    private FSMI_TopTablePositionLogical m_platformPosition = new FSMI_TopTablePositionLogical();

    // Public properties to receive calculated values from GameControllerScript
    public float CurrentRollPosition { get; set; }
    public float CurrentPitchPosition { get; set; }
    public float RollMultiplier { get; set; } // Set by GameControllerScript based on realism support level

    void Start()
    {
        // Find platform's components. Ensure "Shaft" and "Board" GameObjects exist in the scene.
        m_shaft = GameObject.Find("Shaft");
        m_board = GameObject.Find("Board");

        if (m_shaft == null || m_board == null)
        {
            Debug.LogError("Shaft or Board GameObjects not found for FSMI initialization in FSMIController. Please ensure they exist in the scene.");
            enabled = false; // Disable script if critical GameObjects are missing
            return;
        }

        InitializeFSMI();
    }

    private void InitializeFSMI()
    {
        m_fsmi = new ForceSeatMI();

        if (m_fsmi.IsLoaded())
        {
            SaveOriginPosition();
            SaveOriginRotation();

            m_platformPosition.mask = 0;
            m_platformPosition.structSize = (byte)Marshal.SizeOf(m_platformPosition);
            m_platformPosition.state = FSMI_State.NO_PAUSE;
            m_platformPosition.mask = FSMI_POS_BIT.STATE | FSMI_POS_BIT.POSITION;

            m_fsmi.SetAppID("");
            m_fsmi.ActivateProfile("SDK - Positioning");
            m_fsmi.BeginMotionControl();

            SendDataToFSMIPlatform(); // Initial send
            Debug.Log("ForceSeatMI initialized successfully.");
        }
        else
        {
            Debug.LogError("ForceSeatMI library has not been found! Please install ForceSeatPM.");
            enabled = false; // Disable script if library not loaded
        }
    }

    public void UpdatePlatformVisualsAndSendFSMI(float steeringAngleInputForRollVisual, float verticalInputForPitchVisual, float spaceInputForHeaveVisual, float calculatedPitchPosition, float calculatedRollPosition, float rollMultiplier)
    {
        if (m_fsmi != null && m_fsmi.IsLoaded() && enabled)
        {
            // Update visual platform components based on direct inputs (used for m_board.transform.Rotate)
            UpdateValue(ref m_pitch, verticalInputForPitchVisual, DRAWING_PITCH_STEP, -DRAWING_PITCH_MAX, DRAWING_PITCH_MAX);
            UpdateValue(ref m_roll, steeringAngleInputForRollVisual, DRAWING_ROLL_STEP, -DRAWING_ROLL_MAX, DRAWING_ROLL_MAX);
            UpdateValue(ref m_heave, spaceInputForHeaveVisual, DRAWING_HEAVE_STEP, 0, DRAWING_HEAVE_MAX);

            // Apply visual changes to the platform game objects
            m_shaft.transform.position = m_originPosition;
            m_shaft.transform.Translate(0, m_heave, 0);

            m_board.transform.eulerAngles = m_originRotation;
            m_board.transform.Rotate(m_pitch, 0, -m_roll);

            // Now, set the values that will be sent to the actual FSMI platform.
            // These come from the calculation models, passed from GameControllerScript.
            CurrentPitchPosition = calculatedPitchPosition;
            CurrentRollPosition = calculatedRollPosition;
            RollMultiplier = rollMultiplier; // Ensure this is always up-to-date from GameControllerScript

            SendDataToFSMIPlatform();
        }
    }

    private void UpdateValue(ref float value, float input, float step, float min, float max)
    {
        if (input != 0) {
            // If there's input, move the value in the direction of the input
            value = Mathf.Clamp(value + (Mathf.Sign(input) * step), min, max);
        } else {
            // If no input, bring the value closer to 0
            if (value > 0) {
                value = Mathf.Clamp(value - step, 0, max);
            } else if (value < 0) {
                value = Mathf.Clamp(value + step, min, 0);
            }
        }
    }

    private void SaveOriginPosition() {
        var x = m_shaft.transform.position.x;
        var y = m_shaft.transform.position.y;
        var z = m_shaft.transform.position.z;
        m_originPosition = new Vector3(x, y, z);
    }

    private void SaveOriginRotation() {
        var x = m_board.transform.eulerAngles.x;
        var y = m_board.transform.eulerAngles.y;
        var z = m_board.transform.eulerAngles.z;
        m_originRotation = new Vector3(x, y, z);
    }

    private void SendDataToFSMIPlatform() {
        // Convert parameters to logical units
        m_platformPosition.state = FSMI_State.NO_PAUSE;

        m_platformPosition.pitch = (short)Mathf.Clamp(CurrentPitchPosition, PLATFORM_POSITION_LOGIC_MIN, PLATFORM_POSITION_LOGIC_MAX);
        m_platformPosition.roll = (short)Mathf.Clamp(CurrentRollPosition * RollMultiplier, PLATFORM_POSITION_LOGIC_MIN, PLATFORM_POSITION_LOGIC_MAX);
        m_platformPosition.heave = (short)Mathf.Clamp(m_heave / DRAWING_HEAVE_MAX * PLATFORM_POSITION_LOGIC_MAX, PLATFORM_POSITION_LOGIC_MIN, PLATFORM_POSITION_LOGIC_MAX);

        // Send data to platform
        m_fsmi.SendTopTablePosLog(ref m_platformPosition);
    }


    // Cleanup 
    void OnApplicationQuit() { 
        CleanupFSMI();
    }

    void OnDestroy() {
        CleanupFSMI();
    }

    private void CleanupFSMI()
    {
        if (m_fsmi != null && m_fsmi.IsLoaded())
        {
            m_fsmi.EndMotionControl();
            m_fsmi.Dispose();
            Debug.Log("ForceSeatMI cleaned up.");
        }
    }
}
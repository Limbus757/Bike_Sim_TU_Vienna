using System;
using System.Runtime.InteropServices;
using MotionSystems;
using UnityEngine;

public class MotionPlatformController : MonoBehaviour {
    // These properties are public for inspection in the Unity editor.
    public float pitchPosition = 0;
    public float rollPosition = 0;

    // Platform-related parameters, moved from the original GameControllerScript
    private const int PLATFORM_POSITION_LOGIC_MIN = -32767;
    private const int PLATFORM_POSITION_LOGIC_MAX = 32767;
    private const float DRAWING_PITCH_MAX = 2;
    private const float DRAWING_ROLL_MAX = 2;
    public float RollMultiplier = 1f;

    // FSMI api
    private ForceSeatMI m_fsmi;
    private FSMI_TopTablePositionLogical m_platformPosition;

    // GameObjects for visual representation of the platform
    private GameObject m_shaft = null;
    private GameObject m_board = null;
    private Vector3 m_originPosition;
    private Vector3 m_originRotation;

    void Awake() {
        InitializeFSMI();
    }

    void OnDestroy() {
        if (m_fsmi.IsLoaded()) {
            m_fsmi.EndMotionControl();
            m_fsmi.Dispose();
        }
    }

    /// <summary>
    /// Initializes the ForceSeatMI library and finds the visual platform components.
    /// </summary>
    private void InitializeFSMI() {
        m_fsmi = new ForceSeatMI();

        if (m_fsmi.IsLoaded()) {
            // Find platform's components
            m_shaft = GameObject.Find("Shaft");
            m_board = GameObject.Find("Board");

            if (m_shaft == null || m_board == null) {
                Debug.LogError("MotionPlatformController: Shaft or Board GameObject not found! Visuals will not work.");
            } else {
                SaveOriginPosition();
                SaveOriginRotation();
            }

            // Prepare data structure by clearing it and setting correct size
            m_platformPosition.mask = 0;
            m_platformPosition.structSize = (byte)Marshal.SizeOf(m_platformPosition);
            m_platformPosition.state = FSMI_State.NO_PAUSE;
            m_platformPosition.mask = FSMI_POS_BIT.STATE | FSMI_POS_BIT.POSITION;

            m_fsmi.SetAppID("");
            m_fsmi.ActivateProfile("SDK - Positioning");
            m_fsmi.BeginMotionControl();
        } else {
            Debug.LogError("MotionPlatformController: ForceSeatMI library has not been found! Please install ForceSeatPM.");
        }
    }

    /// <summary>
    /// Sends the current pitch and roll data to the physical platform via FSMI.
    /// </summary>
    private void SendDataToFSMIPlatform() {
        if (m_fsmi != null && m_fsmi.IsLoaded()) {
            // Convert parameters to logical units
            m_platformPosition.state = FSMI_State.NO_PAUSE;
            m_platformPosition.pitch = (short)Mathf.Clamp(pitchPosition, PLATFORM_POSITION_LOGIC_MIN, PLATFORM_POSITION_LOGIC_MAX);
            m_platformPosition.roll = (short)Mathf.Clamp(rollPosition * RollMultiplier, PLATFORM_POSITION_LOGIC_MIN, PLATFORM_POSITION_LOGIC_MAX);
            m_platformPosition.heave = 0; // Heave is not used in this new setup

            // Send data to platform
            m_fsmi.SendTopTablePosLog(ref m_platformPosition);
        }
    }

    /// <summary>
    /// Updates the visual representation of the platform.
    /// </summary>
    private void UpdatePlatformVisuals() {
        if (m_shaft != null && m_board != null) {
            // Set back origin position and then modify it
            m_shaft.transform.position = m_originPosition;
            // No heave in this version, so no translation.

            // Set back origin rotation and then modify it
            m_board.transform.eulerAngles = m_originRotation;
            m_board.transform.Rotate(pitchPosition, 0, -rollPosition);
        }
    }

    /// <summary>
    /// Public method to receive new pitch and roll data and update the platform.
    /// This replaces the event subscription model.
    /// </summary>
    /// <param name="newPitch">The new pitch value to apply to the platform.</param>
    /// <param name="newRoll">The new roll value to apply to the platform.</param>
    public void UpdatePlatformPosition(float newPitch, float newRoll) {
        // Update this controller's pitch and roll with the new values
        this.pitchPosition = newPitch;
        this.rollPosition = newRoll;

        // Apply these new positions to the physical and visual platforms
        UpdatePlatformVisuals();
        SendDataToFSMIPlatform();
    }

    /// <summary>
    /// Saves the initial position of the platform's shaft.
    /// </summary>
    private void SaveOriginPosition() {
        if (m_shaft != null) {
            m_originPosition = m_shaft.transform.position;
        }
    }

    /// <summary>
    /// Saves the initial rotation of the platform's board.
    /// </summary>
    private void SaveOriginRotation() {
        if (m_board != null) {
            m_originRotation = m_board.transform.eulerAngles;
        }
    }
}
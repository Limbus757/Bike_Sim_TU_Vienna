using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ResetPosition : MonoBehaviour {

    [SerializeField] Transform resetTransform;
    [SerializeField] GameObject player;
    [SerializeField] Camera playerHead;

    public void Update() {
        bool bRight = OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch);
        if (Input.GetKeyDown("v") || OVRInput.Get(OVRInput.RawButton.B)) {
            ResetViewPosition();
        }
    }

    [ContextMenu("Reset Camera Pos")]
    public void ResetViewPosition() {

        var rotationAngleY = resetTransform.rotation.eulerAngles.y - playerHead.transform.rotation.eulerAngles.y;
        player.transform.Rotate(0, rotationAngleY, 0);

        var distanceDiff = resetTransform.position - playerHead.transform.position;
        player.transform.position += distanceDiff;

    }
}

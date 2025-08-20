using UnityEngine;
using Uduino;
using System;

public class UduinoController : MonoBehaviour {
    // Event that other classes can subscribe to.
    public event Action<float, float, float, float, float, float> OnUduinoDataReceived;

    void Awake() {
        // subscribe to Uduino's native event to get data.
        UduinoManager.Instance.OnDataReceived += OnDataReceived;
    }

    private void OnDataReceived(string data, UduinoDevice device) {
        if (device.name.Equals("IndoorBikeData")) {
            string[] values = data.Split(',');

            if (values.Length > 5) {

                float parsedSpeed = 0;
                float parsedSteeringAngle = 0;
                float parsedFrontBrakeForce = 0;
                float parsedRearBrakeForce = 0;
                float parsedCombinedBrakeForce = 0;
                float parsedResistance = 0;

                float.TryParse(values[0], out parsedSpeed);

                float.TryParse(values[1], out parsedSteeringAngle);

                float.TryParse(values[2], out parsedFrontBrakeForce);
                
                float.TryParse(values[3], out parsedRearBrakeForce);
                
                float.TryParse(values[4], out parsedCombinedBrakeForce);
               
                float.TryParse(values[5], out parsedResistance);

                // Invoke the event, passing the parsed data to all subscribers.
                OnUduinoDataReceived?.Invoke(
                    parsedSpeed,
                    parsedSteeringAngle,
                    parsedFrontBrakeForce,
                    parsedRearBrakeForce,
                    parsedCombinedBrakeForce,
                    parsedResistance);
            }
        }
    }

    void OnDestroy() {
        // Unsubscribe from the event
        UduinoManager.Instance.OnDataReceived -= OnDataReceived;
    }
}
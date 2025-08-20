using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using MotionSystems;
using Uduino;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameController : MonoBehaviour {

    #region Course-Parameters

    public enum Spawnpoint {Course1, None}

    [Header("Platform Mode Settings")]
    [Tooltip("Select the StartingPoint.")]
    public Spawnpoint currentSpawnpoint = Spawnpoint.Course1;

    #endregion

    void Start() {
       
    }
    void Update() {
        HandleInputs();
    }

    void FixedUpdate() {

    }

    private void HandleInputs() {
        
    }

    private void UpdateValue(ref float value, float input, float step, float min, float max) {
        if (0 < input) {
            value = Mathf.Clamp(value + step, min, max);
        } else if (0 > input) {
            value = Mathf.Clamp(value - step, min, max);
        } else if (value > 0) {
            value = Mathf.Clamp(value - step, 0, max);
        } else if (value < 0) {
            value = Mathf.Clamp(value + step, min, 0);
        }
    }

    void UpdateEternityBikeData(string data, UduinoDevice device) {
        if (device.name.Equals("IndoorBikeData")) {
            Bicycle = GameObject.Find("EternityBike");
            string[] values = data.Split(','); // [Speed0, processedSteeringAngle, ForntBreakForche, RearBrakeForce, CombineBreakForce, Resistance]

            //READ AND VALIDATE INPUT
            float Velocity = 0;
            if (controller_mode) {
                float vertical = Input.GetAxis("Vertical");
                Velocity = vertical * 20;
            } else {
                //Test from sonja 09.01.22
                int i;
                if (int.TryParse(values[0].Substring(0, 1), out i)) {
                    Velocity = float.Parse(values[0]);      // Velocity refers to the speed of the tacx
                }

            }

            BikeSpeed = Velocity;

            float iSteeringAngle = 0;
            if (controller_mode) {
                float vertical = Input.GetAxis("Horizontal");
                iSteeringAngle = Math.Max(Math.Min(vertical, 1.0f), -1.0f) * 180;
            } else {
                iSteeringAngle = BikeController.steeringAngle; // -90, 0, +90
            }

            if (values.Length > 1) {

                if (iSteeringAngle < 0) {

                    Sign = -1;

                } else {
                    Sign = 1;
                }

                // -90 to 90
                ISteeringAngle = iSteeringAngle;

                // -1 to 1
                SteeringAngle = iSteeringAngle.Remap(90, -90, 1.0f, -1.0f);
                SteeringAngle = (float)Math.Round(SteeringAngle * 100f) / 100f;

                //Debug.Log("Steering ANGLE == " + SteeringAngle + " IST " + ISteeringAngle);
                //Debug.Log("Velocity: " + Velocity + " SteeringAngle: " + iSteeringAngle + " SteeringAngle: " + SteeringAngle);

                //TODO FIX BRAKES
                float FrontBrakeForce = float.Parse(values[2]);
                float RearBrakeForce = float.Parse(values[3]);
                ;
                float CombinedBrakeForce;
                if (controller_mode) {
                    var hor2 = Input.GetAxis("Horizontal2");
                    CombinedBrakeForce = hor2 * 200;
                    if (CombinedBrakeForce < 0) CombinedBrakeForce *= -1;
                } else {
                    CombinedBrakeForce = float.Parse(values[4]);
                }

                float Resistance = float.Parse(values[5]);

                //APPLY PARSED DATA

                var activeCalculationModel = calculationModelRegistry[activeCalculationModelIndex];
                appliedBrakeForce = activeCalculationModel.calculateBreakForce(BikeSpeed, CombinedBrakeForce);
                PitchPosition = activeCalculationModel.calculatePitch(Bicycle.transform.forward, appliedBrakeForce);
                RollPosition = activeCalculationModel.calculateTilt(Velocity, SteeringAngle);

                //Debug.LogError("Velocity: " + Velocity + " SteeringAngle: " + iSteeringAngle + " SteeringAngle: " + SteeringAngle + " FrontBrakeForce: " + FrontBrakeForce + " RearBrakeForce: " + RearBrakeForce + " CombinedBrakeForce: " + CombinedBrakeForce + " Resistance: " + Resistance);

                if (logger.isActive() && elapsed > logger.frequency) {
                    logger.log(this, elapsed, Velocity, PitchPosition, RollPosition, appliedBrakeForce, Resistance);
                    elapsed = 0;
                }
            }
        }
    }
}

public abstract class AbstractPlatformCalculationModel {
    private readonly float minTilt;
    private readonly float maxTilt;

    public readonly float minPitch;
    public readonly float maxPitch;

    private readonly float minBrakeForce;
    private readonly float maxBrakeForce;

    public bool logCalculations = false;

    public AbstractPlatformCalculationModel(
        float minTilt,
        float maxTilt,
        float minPitch,
        float maxPitch,
        float minBrakeForce,
        float maxBrakeForce
        ) {
        this.minTilt = minTilt;
        this.maxTilt = maxTilt;
        this.minPitch = minPitch;
        this.maxPitch = maxPitch;
        this.minBrakeForce = minBrakeForce;
        this.maxBrakeForce = maxBrakeForce;
    }

    public abstract String getLabel();

    public void setLogCalculations(bool active) {
        this.logCalculations = active;
    }

    public delegate float Calculation();

    public static float getResultWithinRange(float min, float max, Calculation calculation) {
        float ret = calculation.Invoke();

        ret = Math.Min(ret, max);
        ret = Math.Max(ret, min);

        return ret;
    }

    public float calculateTilt(float velocity, float steeringAngle) {
        float ret = getResultWithinRange(minTilt, maxTilt, () => calculateTilt2(velocity, steeringAngle));
        if (logCalculations) {
            Debug.Log("[I] Calculated Tilt: " + ret);
        }
        return ret;
    }

    protected abstract float calculateTilt2(float velocity, float steeringAngle);

    public float calculatePitch(Vector3 bikeForward, float brakeForce) {
        var ret = getResultWithinRange(minPitch, maxPitch, () => calculatePitch2(bikeForward, brakeForce));
        if (logCalculations) {
            Debug.Log("[I] Calculated Pitch: " + ret);
        }
        return ret;
    }

    protected abstract float calculatePitch2(Vector3 bikeForward, float brakeForce);

    public float calculateBreakForce(float bikeSpeed, float combinedBrakeForce) {
        var ret = getResultWithinRange(minBrakeForce, maxBrakeForce, () => calculateBreakForce2(bikeSpeed, combinedBrakeForce));
        if (logCalculations) {
            Debug.LogWarning("[I] Calculated Brakeforce: " + ret);
        }
        return ret;
    }
    protected abstract float calculateBreakForce2(float bikeSpeed, float combinedBrakeForce);
}

public class RealismPlatformCalculationModel : ApproximatedPlatformCalculationModel {
    GameController platform;

    public RealismPlatformCalculationModel(float minTilt,
        float maxTilt,
        float minPitch,
        float maxPitch,
        float minBrakeForce,
        float maxBrakeForce
        ) : base(minTilt, maxTilt, minPitch, maxPitch, minBrakeForce, maxBrakeForce) {
        this.platform = null;
    }

    public void setPlatform(GameController platform) {
        this.platform = platform;
    }

    public override string getLabel() {
        return "Realism";
    }

    /**
     * TODO: @levent fix ugly references to platform...
     */
    protected override float calculateTilt2(float velocity, float steeringAngle) {
        float Range = GameController.PLATFORM_POSITION_LOGIC_MAX / (this.platform.supportedAngle * 1000);

        float speedInMS = this.platform.BikeSpeed / 3.6f;
        double iCalc = Mathf.Atan((float)Math.Pow(this.platform.speedCalculationMultiplier * speedInMS, this.platform.speedCalculationExponent) / (GameController.GRAVITATIONAL_ACCELERATION * this.platform.ICurveRadius)) * (180 / Math.PI);
        this.platform.calculatedTiltAngle = (float)iCalc;

        this.platform.supportFactor = 90 / this.platform.supportedAngle;

        if (this.platform.calculatedTiltAngle >= this.platform.supportedAngle) {
            this.platform.calculatedTiltAngle = this.platform.supportedAngle;
        }

        float MultipliedTiltAngle = this.platform.calculatedTiltAngle * 1000;


        float RollPosition = Range * MultipliedTiltAngle * this.platform.Sign;
        //Debug.Log("Calculate 2 RollPosition (Range, MultTiltAnge, Sign: " + RollPosition + " ("+ Range+ ", "+ MultipliedTiltAngle+ "," + platform.Sign + ")");

        //ITiltAngle = realisticITiltAngleFactor * RollPosition / PLATFORM_POSITION_LOGIC_MAX;
        //ITiltAngle = calculatedTiltAngle;
        if (platform.activateCalculationLogging) {
            Debug.Log("[I] ITiltAngle before Support: " + this.platform.ITiltAngle);
        }


        this.platform.ITiltAngleMax = this.platform.ITiltAngle;

        if (this.platform.currentRealismSupportLevel == GameController.RealismSupportLevel.OptimizedSupport) {
            this.platform.ITiltAngle = this.platform.calculatedTiltAngle * this.platform.Sign / this.platform.optimizedITiltAngleFactor;
        } else if (this.platform.currentRealismSupportLevel == GameController.RealismSupportLevel.FullSupport) {
            this.platform.ITiltAngle = (float)Math.Truncate(this.platform.calculatedTiltAngle * this.platform.Sign);
        } else if (this.platform.currentRealismSupportLevel == GameController.RealismSupportLevel.NoSupport) {
            this.platform.ITiltAngle = 0;
        }

        if (platform.activateCalculationLogging) {
            Debug.Log("[I] ITiltAngle after Support: " + this.platform.ITiltAngle);
        }

        return RollPosition * this.platform.custom_rollMultipliers[this.platform.custom_rollMultiplierInd];
    }
}

public class ApproximatedPlatformCalculationModel : AbstractPlatformCalculationModel {
    public ApproximatedPlatformCalculationModel(float minTilt,
        float maxTilt,
        float minPitch,
        float maxPitch,
        float minBrakeForce,
        float maxBrakeForce
        ) : base(minTilt, maxTilt, minPitch, maxPitch, minBrakeForce, maxBrakeForce) {
    }

    public override string getLabel() {
        return "Approximated";
    }

    private const float tiltMultiplicator = 4000;

    protected override float calculateTilt2(float velocity, float steeringAngle) {
        float toApply = velocity * steeringAngle * tiltMultiplicator;

        if (velocity > 10) {
            //alles gut
        } else if (velocity > 6) {
            toApply *= 0.6f;
        } else if (velocity > 4) {
            toApply *= 0.4f;
        } else if (velocity > 2) {
            toApply *= 0.2f;
        } else {
            toApply = 0f;
        }
        return toApply;
    }

    protected override float calculateBreakForce2(float bikeSpeed, float combinedBrakeForce) {
        float applyBrakeForce = 0;
        if (combinedBrakeForce < 2) {
            if (logCalculations) {
                Debug.Log("[I] Ignore BrakeForce: " + combinedBrakeForce);
            }
        } else {
            applyBrakeForce = combinedBrakeForce;
            //Break Force should never be negative, but just in case
            if (applyBrakeForce < 0) {
                applyBrakeForce = 0;
            }
        }

        return applyBrakeForce;
    }

    private const float pitchDeadzone = 1.0f;
    private const float pitchMultiplier = 600.0f;

    protected override float calculatePitch2(Vector3 bikeForward, float brakeForce) {
        float angle = Vector3.Angle(bikeForward, Vector3.up);

        //Debug.Log("pitch angle: " + angle);
        /*
        if (angle >= 90 - pitchDeadzone && angle <= 90 + pitchDeadzone)
        {
            return 0;
        }
        */
        //TODO PITCH ÄNDERUNGEN


        float pitchToApply = (angle - 90) * pitchMultiplier;
        //    Debug.Log("Pitch To Apply Before " + pitchToApply);
        pitchToApply += brakeForce * 500;
        //    Debug.Log("Pitch To Apply After " + pitchToApply);

        return pitchToApply;
    }
}

public class NoTiltPlatformCalculationModel : ApproximatedPlatformCalculationModel {
    public NoTiltPlatformCalculationModel(float minTilt,
        float maxTilt,
        float minPitch,
        float maxPitch,
        float minBrakeForce,
        float maxBrakeForce
        ) : base(minTilt, maxTilt, minPitch, maxPitch, minBrakeForce, maxBrakeForce) {
    }

    public override string getLabel() {
        return "No Tilt";
    }

    protected override float calculateTilt2(float velocity, float steeringAngle) {
        return 0;
    }
}

public class NoTiltAndNoPitchPlatformCalculationModel : NoTiltPlatformCalculationModel {
    public NoTiltAndNoPitchPlatformCalculationModel(float minTilt,
        float maxTilt,
        float minPitch,
        float maxPitch,
        float minBrakeForce,
        float maxBrakeForce
        ) : base(minTilt, maxTilt, minPitch, maxPitch, minBrakeForce, maxBrakeForce) {
    }

    public override string getLabel() {
        return "No Tilt & No Pitch";
    }

    protected override float calculatePitch2(Vector3 bikeForward, float brakeForce) {
        return 0;
    }
}

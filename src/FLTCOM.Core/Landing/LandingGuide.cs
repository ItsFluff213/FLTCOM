// Ported from MechJeb2, https://github.com/MuMech/MechJeb2
// Source path: MechJeb2/LandingAutopilot/CourseCorrection.cs, CoastToDeceleration.cs, DecelerationBurn.cs,
// KillHorizontalVelocity.cs, FinalDescent.cs, modified for FLTCOM: one pure state machine with no KSP types, the
// horizontal kill folded into the final descent, which also steers onto the target.
// Original notice: Copyright (C) the MechJeb2 contributors. Licensed under the GNU General Public License version 3.
using System;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Landing
{
    public enum LandingPhase
    {
        Correct,
        Coast,
        Brake,
        Final,
        Landed,
    }

    public enum LandingStatus
    {
        Running,
        Done,
        Failed,
    }

    /// <summary>One tick of the vessel. Inertial core frame, SI units. Forward is where the engines push now.</summary>
    public sealed class LandingInput
    {
        public LandingInput(double ut, Vector3d position, Vector3d velocity, double mass, double maxThrust, Vector3d forward, double groundAltitude, bool landed, bool chutesOpen)
        {
            Ut = ut;
            Position = position;
            Velocity = velocity;
            Mass = mass;
            MaxThrust = maxThrust;
            Forward = forward;
            GroundAltitude = groundAltitude;
            Landed = landed;
            ChutesOpen = chutesOpen;
        }

        public double Ut { get; }

        public Vector3d Position { get; }

        public Vector3d Velocity { get; }

        public double Mass { get; }

        public double MaxThrust { get; }

        public Vector3d Forward { get; }

        /// <summary>Terrain height above sea level under the vessel.</summary>
        public double GroundAltitude { get; }

        public bool Landed { get; }

        public bool ChutesOpen { get; }
    }

    public readonly struct LandingCommand
    {
        public LandingCommand(double throttle, Vector3d direction, bool armChutes, LandingStatus status)
        {
            Throttle = throttle;
            Direction = direction;
            ArmChutes = armChutes;
            Status = status;
        }

        public double Throttle { get; }

        /// <summary>Unit vector, inertial, where the engines should push.</summary>
        public Vector3d Direction { get; }

        public bool ArmChutes { get; }

        public LandingStatus Status { get; }
    }

    /// <summary>
    /// Flies the descent after the deorbit burn: course correction, coast, braking burn on the speed policy, final descent
    /// onto the target, touchdown. Re-predicts the landing about once a second.
    /// </summary>
    public sealed class LandingGuide
    {
        public const double CorrectedMiss = 150;
        public const double RecorrectMiss = 1000;
        public const double TouchdownSpeed = 1;
        public const double ChuteArmSpeed = 250;
        private const double CorrectionTimeConstant = 2;
        private const double BrakeTimeConstant = 0.3;
        private const double MaxCorrectionDrag = 0.1;
        private const double SteerTimeConstant = 5;
        private const double MaxSteer = 0.2;
        private const double RestingPush = 0.25;

        private readonly LandingBody _body;
        private readonly LandingTarget _target;
        private readonly double _targetAltitude;
        private readonly double _dragArea;
        private readonly double _chuteArea;
        private readonly double _chuteDeployAltitude;
        private readonly double _correctionBudget;
        private double _lastPrediction = double.NegativeInfinity;
        private double _lastUt = double.NaN;
        private Vector3d _correction = new Vector3d(0, 0, 0);
        private bool _chutesArmed;
        private double _lastThrottle;

        public LandingGuide(LandingBody body, LandingTarget target, double targetAltitude, double dragArea, double chuteArea, double chuteDeployAltitude, double correctionBudget)
        {
            _body = body;
            _target = target;
            _targetAltitude = targetAltitude;
            _dragArea = dragArea;
            _chuteArea = chuteArea;
            _chuteDeployAltitude = chuteDeployAltitude;
            _correctionBudget = correctionBudget;
        }

        public LandingPhase Phase { get; private set; } = LandingPhase.Correct;

        public LandingPrediction? Prediction { get; private set; }

        /// <summary>Distance from the predicted landing to the target, metres; infinite while the descent does not reach the ground.</summary>
        public double Miss { get; private set; } = double.PositiveInfinity;

        public double CorrectionSpent { get; private set; }

        public string? Note { get; private set; }

        public double PredictionInterval { get; set; } = 1;

        public LandingCommand Step(LandingInput input)
        {
            double dt = double.IsNaN(_lastUt) ? 0 : Math.Max(0, input.Ut - _lastUt);
            _lastUt = input.Ut;
            var craft = new LandingCraft(input.Mass, _dragArea, input.MaxThrust, _chuteArea, _chuteDeployAltitude);
            Vector3d up = input.Position.Normalized;
            Vector3d air = _body.AirVelocity(input.Position, input.Velocity);
            double altitude = input.Position.Magnitude - _body.Radius;
            double agl = altitude - Math.Max(input.GroundAltitude, _targetAltitude - 50);

            if (input.Landed || Phase == LandingPhase.Landed)
            {
                Phase = LandingPhase.Landed;
                return new LandingCommand(0, up, _chutesArmed, LandingStatus.Done);
            }
            if (craft.HasChutes && _body.AtmosphereHeight > 0 && air.Magnitude < ChuteArmSpeed && altitude < 0.5 * _body.AtmosphereHeight)
            {
                _chutesArmed = true;
            }
            if (dt > 0 && _correction.Magnitude > 1e-6)
            {
                Vector3d aim = _correction.Normalized;
                double applied = Math.Min(_correction.Magnitude, Math.Max(0, Vector3d.Dot(input.Forward, aim) * _lastThrottle * craft.MaxAccel * dt));
                _correction -= aim * applied;
                if (Phase == LandingPhase.Correct)
                {
                    CorrectionSpent += _lastThrottle * craft.MaxAccel * dt;
                }
            }
            if (Phase != LandingPhase.Final && input.Ut - _lastPrediction >= (Phase == LandingPhase.Correct ? PredictionInterval / 2 : PredictionInterval))
            {
                Predict(input, craft);
            }
            if (Phase is LandingPhase.Correct or LandingPhase.Coast && Prediction is { Outcome: LandingOutcome.NoReentry })
            {
                Note = "the descent does not come down: the vessel stays in orbit";
                return Fail(up);
            }

            IDescentSpeedPolicy? policy = DescentRules.Policy(_body, craft, _targetAltitude);
            double decelEnd = DescentRules.DecelerationEndAltitude(_body, craft, _targetAltitude);
            double drag = 0.5 * _body.Density(altitude) * air.Magnitude * air.Magnitude * _dragArea / Math.Max(1, input.Mass);
            bool canCorrect = craft.MaxAccel > 0 && drag < MaxCorrectionDrag && !input.ChutesOpen && CorrectionSpent < _correctionBudget;

            if (Phase is LandingPhase.Correct or LandingPhase.Coast && policy != null
                && (altitude < decelEnd + 5 || air.Magnitude > 0.9 * policy.MaxAllowedSpeed(input.Position, air)))
            {
                Phase = LandingPhase.Brake;
            }
            if (Phase == LandingPhase.Coast && canCorrect && Miss > RecorrectMiss && Prediction is { Landed: true })
            {
                Phase = LandingPhase.Correct;
            }
            if (Phase == LandingPhase.Correct && (!canCorrect || Miss < CorrectedMiss || Prediction is not { Landed: true }))
            {
                Phase = LandingPhase.Coast;
            }
            if (Phase == LandingPhase.Brake && (policy == null || altitude < decelEnd + 5))
            {
                Phase = LandingPhase.Final;
            }
            if (Phase == LandingPhase.Coast && policy == null && agl < 2 * _chuteDeployAltitude)
            {
                Phase = LandingPhase.Final;
            }

            return Phase switch
            {
                LandingPhase.Correct => Correct(input, craft),
                LandingPhase.Brake => Brake(input, craft, policy!, air, up),
                LandingPhase.Final => Final(input, craft, air, up, agl),
                _ => Hold(air, up),
            };
        }

        private void Predict(LandingInput input, LandingCraft craft)
        {
            _lastPrediction = input.Ut;
            var state = new StateVector(input.Position, input.Velocity);
            ReentrySettings settings = ReentrySettings.For(_body, craft, _targetAltitude, tolerance: 0.05);
            Prediction = ReentryPredictor.Predict(_body, craft, state, input.Ut, settings);
            if (!Prediction.Landed)
            {
                Miss = double.PositiveInfinity;
                _correction = new Vector3d(0, 0, 0);
                return;
            }
            Vector3d miss = LandingAim.Miss(_body, Prediction, _target, _targetAltitude, input.Ut);
            Miss = miss.Magnitude;
            _correction = LandingAim.CourseCorrection(_body, craft, settings, state, input.Ut, miss, _target, _targetAltitude);
        }

        private LandingCommand Correct(LandingInput input, LandingCraft craft)
        {
            double size = _correction.Magnitude;
            if (size < 1e-6)
            {
                return Command(0, input.Forward);
            }
            Vector3d direction = _correction / size;
            double throttle = Vector3d.Dot(input.Forward, direction) > 0.97 ? Clamp(size / (craft.MaxAccel * CorrectionTimeConstant)) : 0;
            return Command(throttle, direction);
        }

        private LandingCommand Brake(LandingInput input, LandingCraft craft, IDescentSpeedPolicy policy, Vector3d air, Vector3d up)
        {
            Vector3d retro = air.Magnitude > 1e-3 ? air.Normalized * -1 : up;
            if (Vector3d.Dot(air, up) > 0)
            {
                return Command(0, retro);
            }
            double g = Gravity(input.Position);
            double sink = Math.Abs(Vector3d.Dot(air.Normalized, up));
            double speedError = air.Magnitude - policy.MaxAllowedSpeed(input.Position, air);
            double minAccel = -g * sink;
            double maxAccel = craft.MaxAccel - g * sink;
            double braking = maxAccel - minAccel > 0 ? Clamp((speedError / BrakeTimeConstant - minAccel) / (maxAccel - minAccel)) * craft.MaxAccel : 0;
            Vector3d side = LandingAim.Horizontal(_correction, up) / SteerTimeConstant;
            if (side.Magnitude > MaxSteer * craft.MaxAccel)
            {
                side = side.Normalized * (MaxSteer * craft.MaxAccel);
            }
            Vector3d push = retro * braking + side;
            Vector3d direction = Lean(retro, push, craft);
            double throttle = Vector3d.Dot(input.Forward, direction) < 0.75 ? 0 : Clamp(Vector3d.Dot(push, direction) / craft.MaxAccel);
            return Command(throttle, direction);
        }

        private LandingCommand Final(LandingInput input, LandingCraft craft, Vector3d air, Vector3d up, double agl)
        {
            if (craft.MaxAccel <= 0)
            {
                return Hold(air, up);
            }
            double g = Gravity(input.Position);
            double spare = Math.Max(0.1, craft.MaxAccel - g);
            Vector3d horizontal = LandingAim.Horizontal(air, up);
            Vector3d offset = LandingAim.Horizontal(_body.Surface(_target.Latitude, _target.Longitude, _targetAltitude, input.Ut) - input.Position, up);
            Vector3d wanted = offset.Magnitude < 2000 ? offset / 8 : new Vector3d(0, 0, 0);
            double cap = Math.Min(10, 0.3 * Math.Sqrt(Math.Max(0, agl)) + 0.5);
            if (wanted.Magnitude > cap)
            {
                wanted = wanted.Normalized * cap;
            }
            Vector3d sideways = (wanted - horizontal) / 2;
            double sideCap = 0.5 * craft.MaxAccel;
            if (sideways.Magnitude > sideCap)
            {
                sideways = sideways.Normalized * sideCap;
            }

            double sinkRate = Math.Max(TouchdownSpeed, Math.Min(Math.Max(0, agl) / 6, 0.6 * Math.Sqrt(2 * spare * Math.Max(0, agl))));
            if (horizontal.Magnitude > 3 && agl > 30)
            {
                sinkRate = Math.Min(sinkRate, 2);
            }
            double climb = Vector3d.Dot(air, up);
            double lift = Math.Max(0, (-sinkRate - climb) / 0.5 + g);
            Vector3d push = up * lift + sideways;
            Vector3d direction = Lean(up, push, craft);
            double aligned = Clamp((Vector3d.Dot(input.Forward, direction) - 0.5) / 0.4);
            return Command(Clamp(Vector3d.Dot(push, direction) / craft.MaxAccel) * aligned, direction);
        }

        /// <summary>
        /// Where to point for <paramref name="push"/>: the nose leaves <paramref name="rest"/> only as far as the push
        /// outweighs <see cref="RestingPush"/> of full thrust. Pointing along the bare push swings the vessel round
        /// whenever the push is near zero, because its direction is then the sign of a correction for a miss of a metre
        /// or two, which flips from one prediction to the next.
        /// </summary>
        private static Vector3d Lean(Vector3d rest, Vector3d push, LandingCraft craft) => (rest * (RestingPush * craft.MaxAccel) + push).Normalized;

        private LandingCommand Hold(Vector3d air, Vector3d up) =>
            Command(0, air.Magnitude > 1 ? air.Normalized * -1 : up);

        private LandingCommand Command(double throttle, Vector3d direction)
        {
            _lastThrottle = throttle;
            return new LandingCommand(throttle, direction, _chutesArmed, LandingStatus.Running);
        }

        private LandingCommand Fail(Vector3d up)
        {
            _lastThrottle = 0;
            return new LandingCommand(0, up, _chutesArmed, LandingStatus.Failed);
        }

        private double Gravity(Vector3d position) => _body.Mu / Vector3d.Dot(position, position);

        private static double Clamp(double x) => Math.Max(0, Math.Min(1, x));
    }
}

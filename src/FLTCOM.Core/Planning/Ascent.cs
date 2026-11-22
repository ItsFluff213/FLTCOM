// The gravity turn (vertical rise, shaped pitch-over, coast to the edge of the atmosphere), the apoapsis-matching
// throttle and the angle-of-attack limiter are ported from MechJeb2, https://github.com/MuMech/MechJeb2
// Source path: MechJeb2/MechJebModuleAscentClassicAutopilot.cs
// Original notice: Copyright the MechJeb contributors; MechJeb2 is licensed GPL-3.0, as is FLTCOM.
using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    /// <summary>The orbit a launch should reach. Altitudes above sea level in metres, angles in radians.</summary>
    public sealed class LaunchTarget
    {
        public LaunchTarget(double apoapsis, double periapsis, double? inclination = null, double? lan = null)
        {
            Apoapsis = Math.Max(apoapsis, periapsis);
            Periapsis = Math.Min(apoapsis, periapsis);
            Inclination = inclination;
            Lan = lan;
        }

        public double Apoapsis { get; }

        public double Periapsis { get; }

        /// <summary>Null flies due east into the lowest inclination the site allows.</summary>
        public double? Inclination { get; }

        /// <summary>Null launches without waiting for a plane.</summary>
        public double? Lan { get; }

        public static LaunchTarget Circular(double altitude, double? inclination = null, double? lan = null) => new LaunchTarget(altitude, altitude, inclination, lan);
    }

    /// <summary>A rough delta-v budget for a launch, split into the ascent and the burns that finish the orbit.</summary>
    public readonly struct AscentBudget
    {
        /// <summary>Gravity and drag losses of a Kerbin-like ascent, scaled by surface gravity on other bodies with air.</summary>
        public const double AtmosphereLoss = 1000;

        /// <summary>A gravity turn reaches its apoapsis with about this share of the circular speed still to add.</summary>
        public const double CircularizeShare = 0.25;

        private AscentBudget(double ascent, double finish)
        {
            Ascent = ascent;
            Finish = finish;
        }

        public double Ascent { get; }

        public double Finish { get; }

        public double Total => Ascent + Finish;

        public static AscentBudget Estimate(CentralBody body, LaunchSite site, LaunchPlane plane, LaunchTarget target)
        {
            double mu = body.GravParameter;
            double r0 = body.Radius + Math.Max(0, site.Altitude);
            double ra = body.Radius + target.Apoapsis;
            double rp = body.Radius + target.Periapsis;
            double v0 = Math.Sqrt(mu * (2 / r0 - 2 / (r0 + ra)));
            double atApoapsis = v0 * r0 / ra;
            double raisePeriapsis = Math.Max(0, Math.Sqrt(mu * (2 / ra - 2 / (ra + rp))) - atApoapsis);
            double spin = site.RotationRate * r0 * Math.Cos(site.Latitude) * Math.Sin(plane.Heading(site));
            double gravity = mu / (body.Radius * body.Radius);
            double losses = body.AtmosphereHeight > 0 ? AtmosphereLoss * gravity / DeltaVG0 : 0.05 * v0;
            double finish = body.AtmosphereHeight > 0 ? Math.Max(raisePeriapsis, CircularizeShare * Math.Sqrt(mu / ra)) : raisePeriapsis;
            double total = v0 - spin + losses + raisePeriapsis;
            return new AscentBudget(Math.Max(0, total - finish), finish);
        }

        private const double DeltaVG0 = Propulsion.DeltaVEstimator.G0;
    }

    public enum AscentPhase
    {
        Vertical,
        Turn,
        Coast,
        Done,
    }

    /// <summary>What the vessel reads this frame. Position and velocity are inertial, from the body centre.</summary>
    public sealed class AscentInput
    {
        public AscentInput(double ut, double altitude, double dynamicPressure, StateVector state)
        {
            Ut = ut;
            Altitude = altitude;
            DynamicPressure = dynamicPressure;
            State = state;
        }

        public double Ut { get; }

        /// <summary>Above sea level, in metres.</summary>
        public double Altitude { get; }

        /// <summary>In pascals.</summary>
        public double DynamicPressure { get; }

        public StateVector State { get; }

        /// <summary>Apoapsis radius of the orbit through this state; infinite when it escapes.</summary>
        public static double ApoapsisRadius(double mu, StateVector state)
        {
            double r = state.Position.Magnitude;
            double energy = Vector3d.Dot(state.Velocity, state.Velocity) / 2 - mu / r;
            if (energy >= 0)
            {
                return double.PositiveInfinity;
            }
            double a = -mu / (2 * energy);
            double h = Vector3d.Cross(state.Position, state.Velocity).Magnitude;
            double e = Math.Sqrt(Math.Max(0, 1 - h * h / (mu * a)));
            return a * (1 + e);
        }
    }

    /// <summary>What to do this frame. Direction is a unit vector in local east, north, up components.</summary>
    public readonly struct AscentCommand
    {
        public AscentCommand(AscentPhase phase, double throttle, Vector3d direction)
        {
            Phase = phase;
            Throttle = throttle;
            Direction = direction;
        }

        public AscentPhase Phase { get; }

        public double Throttle { get; }

        public Vector3d Direction { get; }
    }

    /// <summary>
    /// Flies the classic gravity turn up to the target apoapsis and coasts out of the atmosphere, holding the apoapsis
    /// with small throttle pulses. Steering is closed loop: the heading aims the velocity into the target plane.
    /// The burns that finish the orbit are planned afterwards from the orbit actually reached.
    /// </summary>
    public sealed class AscentGuide
    {
        public const double TurnStartFraction = 0.05;
        public const double TurnStartSpeedFactor = 18.5;
        public const double TurnEndFraction = 0.85;
        public const double TurnShapeExponent = 0.4;
        public const double MaxAngleOfAttack = 5;
        public const double AngleOfAttackFadePressure = 2500;
        private const double MinTurnShapeExponent = 0.1;
        private const double ApoapsisSlack = 5;

        private readonly CentralBody _body;
        private readonly LaunchTarget _target;
        private readonly Vector3d _planeNormal;
        private readonly double _rotationRate;
        private readonly double _launchAltitude;
        private double _actualTurnStart;
        private Vector3d _lastDirection = new Vector3d(0, 0, 1);

        private double _lastThrottle;
        private double _lastApoapsisRadius;
        private double _lastUt = double.NegativeInfinity;
        private readonly Queue<double> _ratePerThrottle = new Queue<double>();

        public AscentGuide(CentralBody body, LaunchTarget target, LaunchPlane plane, double rotationRate, double launchAltitude)
        {
            _body = body;
            _target = target;
            _planeNormal = plane.Normal;
            _rotationRate = rotationRate;
            _launchAltitude = Math.Max(launchAltitude, 0);
            _actualTurnStart = _launchAltitude;
        }

        public AscentPhase Phase { get; private set; } = AscentPhase.Vertical;

        private double TargetApoapsisRadius => _body.Radius + _target.Apoapsis;

        private bool HasAir => _body.AtmosphereHeight > 0;

        public double TurnStartAltitude => HasAir ? _body.AtmosphereHeight * TurnStartFraction : _launchAltitude + 25;

        public double TurnStartSpeed => HasAir ? TurnStartSpeedFactor * TurnStartSpeedFactor * TurnStartSpeedFactor * 0.015625 : double.PositiveInfinity;

        public double TurnEndAltitude => HasAir
            ? Math.Min(_body.AtmosphereHeight * TurnEndFraction, _target.Apoapsis)
            : Math.Min(30000, _target.Apoapsis * TurnEndFraction);

        public AscentCommand Step(AscentInput input)
        {
            StateVector state = input.State;
            Enu enu = Enu.At(state.Position);
            Vector3d surface = state.Velocity - Vector3d.Cross(new Vector3d(0, 0, _rotationRate), state.Position);
            double surfaceSpeed = surface.Magnitude;
            double apoapsisRadius = AscentInput.ApoapsisRadius(_body.GravParameter, state);

            switch (Phase)
            {
                case AscentPhase.Vertical:
                    if (!IsVertical(input.Altitude, surfaceSpeed))
                    {
                        Phase = AscentPhase.Turn;
                    }
                    if (apoapsisRadius > TargetApoapsisRadius)
                    {
                        Phase = AscentPhase.Coast;
                    }
                    _lastDirection = new Vector3d(0, 0, 1);
                    return new AscentCommand(AscentPhase.Vertical, 1, _lastDirection);

                case AscentPhase.Turn:
                    if (apoapsisRadius > TargetApoapsisRadius)
                    {
                        Phase = AscentPhase.Coast;
                        return Coast(input, state, enu, surface, apoapsisRadius);
                    }
                    if (IsVertical(input.Altitude, surfaceSpeed))
                    {
                        Phase = AscentPhase.Vertical;
                        return new AscentCommand(AscentPhase.Vertical, 1, new Vector3d(0, 0, 1));
                    }
                    double throttle = ThrottleToRaiseApoapsis(input.Ut, apoapsisRadius, TargetApoapsisRadius);
                    if (throttle >= 1)
                    {
                        double pitch = Angles.ToRadians(FlightPathAngle(input.Altitude, surfaceSpeed));
                        double heading = Heading(state, enu);
                        _lastDirection = new Vector3d(Math.Sin(heading) * Math.Cos(pitch), Math.Cos(heading) * Math.Cos(pitch), Math.Sin(pitch));
                    }
                    return new AscentCommand(AscentPhase.Turn, throttle, LimitAngleOfAttack(_lastDirection, enu.ToLocal(surface), input));

                case AscentPhase.Coast:
                    return Coast(input, state, enu, surface, apoapsisRadius);

                default:
                    return new AscentCommand(AscentPhase.Done, 0, enu.ToLocal(state.Velocity).Normalized);
            }
        }

        private AscentCommand Coast(AscentInput input, StateVector state, Enu enu, Vector3d surface, double apoapsisRadius)
        {
            Vector3d prograde = enu.ToLocal(state.Velocity).Normalized;
            if (input.Altitude > _body.AtmosphereHeight)
            {
                Phase = AscentPhase.Done;
                return new AscentCommand(AscentPhase.Done, 0, prograde);
            }
            double throttle = apoapsisRadius < TargetApoapsisRadius ? ThrottleToRaiseApoapsis(input.Ut, apoapsisRadius, TargetApoapsisRadius) : 0;
            return new AscentCommand(AscentPhase.Coast, throttle, LimitAngleOfAttack(prograde, enu.ToLocal(surface), input));
        }

        /// <summary>The planned flight path angle in degrees above the horizon at <paramref name="altitude"/>.</summary>
        public double FlightPathAngle(double altitude, double surfaceSpeed)
        {
            if (IsVertical(altitude, surfaceSpeed))
            {
                return 90;
            }
            double turnEnd = TurnEndAltitude;
            if (altitude > turnEnd)
            {
                return 0;
            }
            double fraction = altitude > _actualTurnStart ? (altitude - _actualTurnStart) / (turnEnd - _actualTurnStart) : 0;
            double shape = Math.Max(TurnShapeExponent, MinTurnShapeExponent);
            return Math.Max(0.01, Math.Min(89.99, 90 - Math.Pow(fraction, shape) * 90));
        }

        private bool IsVertical(double altitude, double surfaceSpeed)
        {
            _actualTurnStart = Math.Min(_actualTurnStart, Math.Max(TurnStartAltitude, _launchAltitude));
            if (altitude < TurnStartAltitude && surfaceSpeed < TurnStartSpeed)
            {
                _actualTurnStart = Math.Max(_actualTurnStart, altitude);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Heading in radians clockwise from north: the horizontal part of the velocity still missing to fly along the
        /// target plane at circular speed. Low and slow it is the launch azimuth corrected for the body's spin; later it
        /// steers out drift away from the plane.
        /// </summary>
        private double Heading(StateVector state, Enu enu)
        {
            Vector3d along = Vector3d.Cross(_planeNormal, state.Position);
            along = along.Magnitude < 1e-9 ? enu.East : along.Normalized;
            Vector3d wanted = along * Math.Sqrt(_body.GravParameter / TargetApoapsisRadius);
            Vector3d missing = enu.ToLocal(wanted - state.Velocity);
            return Math.Atan2(missing.X, missing.Y);
        }

        /// <summary>A throttle that eases off as the apoapsis nears its target so it lands on it rather than past it.</summary>
        private double ThrottleToRaiseApoapsis(double ut, double apoapsisRadius, double targetRadius)
        {
            double throttle;
            if (apoapsisRadius > targetRadius + ApoapsisSlack)
            {
                throttle = 0;
            }
            else if (_lastUt > ut - 1 && ut > _lastUt && _lastThrottle > 0)
            {
                double rate = Math.Max(1, (apoapsisRadius - _lastApoapsisRadius) / ((ut - _lastUt) * _lastThrottle));
                _ratePerThrottle.Enqueue(rate);
                while (_ratePerThrottle.Count > 3)
                {
                    _ratePerThrottle.Dequeue();
                }
                double wantedRate = targetRadius - apoapsisRadius;
                throttle = Math.Max(0.05, Math.Min(1, wantedRate / _ratePerThrottle.Average()));
            }
            else
            {
                throttle = 1;
            }
            _lastThrottle = throttle;
            _lastApoapsisRadius = apoapsisRadius;
            _lastUt = ut;
            return throttle;
        }

        /// <summary>Inside the atmosphere, keeps the nose within a few degrees of the airflow; the limit widens as the air thins.</summary>
        private Vector3d LimitAngleOfAttack(Vector3d wanted, Vector3d surface, AscentInput input)
        {
            if (input.Altitude >= _body.AtmosphereHeight || surface.Magnitude < 1e-6)
            {
                return wanted;
            }
            double fade = input.DynamicPressure < AngleOfAttackFadePressure ? AngleOfAttackFadePressure / input.DynamicPressure : 1;
            double limit = Angles.ToRadians(double.IsInfinity(fade) || double.IsNaN(fade) ? 180 : Math.Min(fade * MaxAngleOfAttack, 180));
            Vector3d airflow = surface.Normalized;
            double angle = Math.Acos(Math.Max(-1, Math.Min(1, Vector3d.Dot(airflow, wanted.Normalized))));
            if (angle <= limit)
            {
                return wanted;
            }
            Vector3d axis = Vector3d.Cross(airflow, wanted);
            if (axis.Magnitude < 1e-9)
            {
                return airflow;
            }
            return Vector3d.Rotate(airflow, axis.Normalized, limit);
        }
    }
}

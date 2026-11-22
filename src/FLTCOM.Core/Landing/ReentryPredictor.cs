// Ported from MechJeb2, https://github.com/MuMech/MechJeb2
// Source path: MechJeb2/ReentrySimulation.cs (RunSimulation, AdvanceToFreefallEnd, FreefallEnded, BS34Step, LimitSpeed,
// TotalAccel), modified for FLTCOM: no KSP or Unity types, drag from one drag area instead of drag cubes, the parachutes
// open fully at their deploy height, no lift, no trajectory pooling.
// Original notice: Copyright (C) the MechJeb2 contributors. Licensed under the GNU General Public License version 3.
using System;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Landing
{
    public enum LandingOutcome
    {
        Landed,
        NoReentry,
        Aerobraked,
        TimedOut,
    }

    /// <summary>Where and when the descent meets the ground, in the inertial frame and on the surface.</summary>
    public sealed class LandingPrediction
    {
        public LandingPrediction(LandingOutcome outcome, double ut, Vector3d position, double latitude, double longitude, double deltaVExpended, double maxDragG, double freefallEndUt, double touchdownSpeed = double.NaN)
        {
            TouchdownSpeed = touchdownSpeed;
            Outcome = outcome;
            Ut = ut;
            Position = position;
            Latitude = latitude;
            Longitude = longitude;
            DeltaVExpended = deltaVExpended;
            MaxDragG = maxDragG;
            FreefallEndUt = freefallEndUt;
        }

        public LandingOutcome Outcome { get; }

        public bool Landed => Outcome == LandingOutcome.Landed;

        public double Ut { get; }

        public Vector3d Position { get; }

        public double Latitude { get; }

        public double Longitude { get; }

        /// <summary>Delta-v the braking burn spends on the way down.</summary>
        public double DeltaVExpended { get; }

        public double MaxDragG { get; }

        /// <summary>When the Kepler coast ends: the vessel enters the air or starts braking.</summary>
        public double FreefallEndUt { get; }

        /// <summary>Speed against the ground at touchdown, m/s, after the braking the guide will fly.</summary>
        public double TouchdownSpeed { get; }
    }

    /// <summary>Settings of one prediction. Tolerance is the BS3(4) step error in m/s; larger is faster and coarser.</summary>
    public sealed class ReentrySettings
    {
        public ReentrySettings(double landingAltitude, IDescentSpeedPolicy? policy, double decelerationEndAltitude, bool chutes, double tolerance = 0.01)
        {
            LandingAltitude = landingAltitude;
            Policy = policy;
            DecelerationEndAltitude = decelerationEndAltitude;
            Chutes = chutes;
            Tolerance = tolerance;
        }

        public double LandingAltitude { get; }

        public IDescentSpeedPolicy? Policy { get; }

        public double DecelerationEndAltitude { get; }

        /// <summary>The parachutes open at their deploy height.</summary>
        public bool Chutes { get; }

        public double Tolerance { get; }

        public static ReentrySettings For(LandingBody body, LandingCraft craft, double landingAltitude, double tolerance = 0.01) =>
            new ReentrySettings(landingAltitude, DescentRules.Policy(body, craft, landingAltitude), DescentRules.DecelerationEndAltitude(body, craft, landingAltitude), craft.HasChutes && body.AtmosphereHeight > 0, tolerance);
    }

    public static class ReentryPredictor
    {
        private const double MinDt = 0.05;
        private const double MaxDt = 10;
        private const int MaxSteps = 50000;
        private const double MaxOrbits = 1;

        public static LandingPrediction Predict(LandingBody body, LandingCraft craft, StateVector state, double ut, ReentrySettings settings)
        {
            var sim = new Simulation(body, craft, settings);
            return sim.Run(state, ut);
        }

        private sealed class Simulation
        {
            private readonly LandingBody _body;
            private readonly LandingCraft _craft;
            private readonly ReentrySettings _settings;
            private readonly double _decelRadius;
            private readonly double _aerobrakedRadius;
            private readonly double _landingRadius;
            private Vector3d _x;
            private Vector3d _v;
            private Vector3d _startX;
            private double _t;
            private double _dt = 1;
            private double _deltaV;
            private double _maxDragG;
            private int _steps;

            public Simulation(LandingBody body, LandingCraft craft, ReentrySettings settings)
            {
                _body = body;
                _craft = craft;
                _settings = settings;
                _decelRadius = body.Radius + settings.DecelerationEndAltitude;
                _aerobrakedRadius = body.Radius + body.AtmosphereHeight;
                _landingRadius = body.Radius + settings.LandingAltitude;
            }

            public LandingPrediction Run(StateVector state, double ut)
            {
                _x = state.Position;
                _v = state.Velocity;
                _t = ut;
                double energy = Vector3d.Dot(_v, _v) / 2 - _body.Mu / _x.Magnitude;
                OrbitElements? orbit = energy < 0 ? OrbitElements.FromState(_body.Mu, state, ut) : null;
                if (orbit != null && orbit.PeriapsisRadius > Math.Max(_decelRadius, _aerobrakedRadius) && orbit.PeriapsisRadius > _landingRadius)
                {
                    return Result(LandingOutcome.NoReentry);
                }
                if (orbit != null)
                {
                    AdvanceToFreefallEnd(orbit);
                }
                double freefallEnd = _t;
                _startX = _x;
                double maxT = _t + MaxOrbits * Angles.TwoPi * Math.Sqrt(Math.Pow(_x.Magnitude, 3) / _body.Mu);
                bool aerobrake = false;
                while (true)
                {
                    if (_x.Magnitude < _landingRadius)
                    {
                        return Result(LandingOutcome.Landed, freefallEnd);
                    }
                    if (_body.AtmosphereHeight > 0 && _x.Magnitude > _aerobrakedRadius && Vector3d.Dot(_x, _v) > 0 && _t > freefallEnd)
                    {
                        aerobrake = true;
                    }
                    if (_t > maxT || Escaping() || _steps > MaxSteps || aerobrake)
                    {
                        return Result(aerobrake ? LandingOutcome.Aerobraked : LandingOutcome.TimedOut, freefallEnd);
                    }
                    Vector3d before = _x;
                    BS34Step();
                    LimitSpeed();
                    if (_x.Magnitude < _landingRadius)
                    {
                        double over = before.Magnitude - _x.Magnitude;
                        double f = over > 0 ? (before.Magnitude - _landingRadius) / over : 1;
                        double back = (1 - f) * _dtTaken;
                        _x = before + (_x - before) * f;
                        _t -= back;
                    }
                }
            }

            private double _dtTaken;

            private LandingPrediction Result(LandingOutcome outcome, double freefallEnd = double.NaN) =>
                new LandingPrediction(outcome, _t, _x, _body.Latitude(_x), _body.Longitude(_x, _t), _deltaV, _maxDragG, freefallEnd, _body.AirVelocity(_x, _v).Magnitude);

            private bool Escaping()
            {
                double escape = Math.Sqrt(2 * _body.Mu / _x.Magnitude);
                return _body.AtmosphereHeight > 0 && _v.Magnitude > escape && Vector3d.Dot(_x, _v) > 0;
            }

            private void AdvanceToFreefallEnd(OrbitElements orbit)
            {
                if (FreefallEnded(orbit, _t))
                {
                    return;
                }
                double lower = _t;
                double upper = orbit.NextTimeAtTrueAnomaly(0, _t);
                if (upper <= lower)
                {
                    upper += orbit.Period;
                }
                while (upper - lower > 0.5)
                {
                    double mid = (upper + lower) / 2;
                    if (FreefallEnded(orbit, mid))
                    {
                        upper = mid;
                    }
                    else
                    {
                        lower = mid;
                    }
                }
                _t = (upper + lower) / 2;
                StateVector s = orbit.StateAt(_t);
                _x = s.Position;
                _v = s.Velocity;
            }

            private bool FreefallEnded(OrbitElements orbit, double ut)
            {
                StateVector s = orbit.StateAt(ut);
                Vector3d air = _body.AirVelocity(s.Position, s.Velocity);
                double r = s.Position.Magnitude;
                if (r < _aerobrakedRadius || r < _landingRadius)
                {
                    return true;
                }
                if (Vector3d.Dot(air, s.Position) > 0)
                {
                    return false;
                }
                if (r < _decelRadius)
                {
                    return true;
                }
                return _settings.Policy != null && air.Magnitude > _settings.Policy.MaxAllowedSpeed(s.Position, air);
            }

            private void BS34Step()
            {
                const double D9 = 1d / 9d;
                const double D24 = 1d / 24d;
                double tol = _settings.Tolerance;
                while (true)
                {
                    _steps++;
                    Vector3d dv1 = _dt * Accel(_x, _v);
                    Vector3d dx1 = _dt * _v;
                    Vector3d dv2 = _dt * Accel(_x + 0.5 * dx1, _v + 0.5 * dv1);
                    Vector3d dx2 = _dt * (_v + 0.5 * dv1);
                    Vector3d dv3 = _dt * Accel(_x + 0.75 * dx2, _v + 0.75 * dv2);
                    Vector3d dx3 = _dt * (_v + 0.75 * dv2);
                    Vector3d dv4 = _dt * Accel(_x + 2 * D9 * dx1 + 3 * D9 * dx2 + 4 * D9 * dx3, _v + 2 * D9 * dv1 + 3 * D9 * dv2 + 4 * D9 * dv3);
                    Vector3d dx = (2 * dx1 + 3 * dx2 + 4 * dx3) * D9;
                    Vector3d dv = (2 * dv1 + 3 * dv2 + 4 * dv3) * D9;
                    Vector3d zv = (7 * dv1 + 6 * dv2 + 8 * dv3 + 3 * dv4) * D24;
                    double error = Math.Max((zv - dv).Magnitude, 1e-5);

                    double next = _dt * 0.9 * Math.Pow(tol / error, 1 / 3d);
                    if (double.IsNaN(next))
                    {
                        next = MinDt;
                    }
                    next = Math.Min(Math.Max(next, MinDt), MaxDt);
                    double fromStart = (_x - _startX).Magnitude;
                    if (fromStart < 1000)
                    {
                        next = Math.Min(next, 0.5);
                    }
                    else if (fromStart < 10000)
                    {
                        next = Math.Min(next, 2);
                    }
                    double agl = _x.Magnitude - _landingRadius;
                    next = Math.Min(next, Math.Max(MinDt, agl / Math.Max(1, _v.Magnitude) / 4));

                    if (error > tol && _dt > MinDt)
                    {
                        _dt = Math.Max(MinDt, Math.Min(next, _dt * 0.5));
                        continue;
                    }
                    _maxDragG = Math.Max(_maxDragG, Drag(_x, _v).Magnitude / 9.81);
                    _x += dx;
                    _v += dv;
                    _t += _dt;
                    _dtTaken = _dt;
                    _dt = next;
                    return;
                }
            }

            private void LimitSpeed()
            {
                if (_settings.Policy == null || _craft.MaxAccel <= 0)
                {
                    return;
                }
                Vector3d air = _body.AirVelocity(_x, _v);
                double max = _settings.Policy.MaxAllowedSpeed(_x, air);
                if (air.Magnitude > max)
                {
                    double dv = Math.Min(air.Magnitude - max, _dtTaken * _craft.MaxAccel);
                    air -= dv * air.Normalized;
                    _deltaV += dv;
                    _v = air + Vector3d.Cross(_body.Spin, _x);
                }
            }

            private Vector3d Accel(Vector3d x, Vector3d v)
            {
                double r = x.Magnitude;
                return x * (-_body.Mu / (r * r * r)) + Drag(x, v);
            }

            private Vector3d Drag(Vector3d x, Vector3d v)
            {
                double altitude = x.Magnitude - _body.Radius;
                double density = _body.Density(altitude);
                if (density <= 0 || _craft.Mass <= 0)
                {
                    return new Vector3d(0, 0, 0);
                }
                Vector3d air = _body.AirVelocity(x, v);
                double area = _craft.DragArea;
                if (_settings.Chutes && altitude - _settings.LandingAltitude < _craft.ChuteDeployAltitude)
                {
                    area += _craft.ChuteArea;
                }
                return air * (-0.5 * density * air.Magnitude * area / _craft.Mass);
            }
        }
    }
}

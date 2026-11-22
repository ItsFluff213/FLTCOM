// Ported from MechJeb2, https://github.com/MuMech/MechJeb2
// Source path: MechJeb2/LandingAutopilot/DeorbitBurn.cs, modified for FLTCOM: instead of steering the burn live, the
// burn time and its normal component are solved ahead of time against the full re-entry prediction, so the deorbit can
// be briefed and flown by the node executor.
// Original notice: Copyright (C) the MechJeb2 contributors. Licensed under the GNU General Public License version 3.
using System;
using System.Collections.Generic;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Landing
{
    public sealed class LandingPlan
    {
        private LandingPlan(string? error, double burnUt, double prograde, double normal, double radial, LandingPrediction? prediction, double miss)
        {
            Error = error;
            BurnUt = burnUt;
            Prograde = prograde;
            Normal = normal;
            Radial = radial;
            Prediction = prediction;
            Miss = miss;
        }

        public bool Ok => Error == null;

        public string? Error { get; }

        public double BurnUt { get; }

        public double Prograde { get; }

        public double Normal { get; }

        public double Radial { get; }

        public double DeorbitDeltaV => Math.Sqrt(Prograde * Prograde + Normal * Normal + Radial * Radial);

        public LandingPrediction? Prediction { get; }

        /// <summary>Distance from the predicted landing to the target after the deorbit burn, metres.</summary>
        public double Miss { get; }

        internal static LandingPlan Fail(string error) => new LandingPlan(error, 0, 0, 0, 0, null, double.PositiveInfinity);

        internal static LandingPlan Burn(double ut, double prograde, double normal, double radial, LandingPrediction prediction, double miss) =>
            new LandingPlan(null, ut, prograde, normal, radial, prediction, miss);
    }

    /// <summary>Times and shapes the deorbit burn so the predicted landing falls on the target.</summary>
    public static class LandingPlanner
    {
        public const int SearchOrbits = 4;
        private const int StepsPerOrbit = 36;
        private const double ScanTolerance = 0.5;
        private const double FineTolerance = 0.05;

        /// <summary>
        /// Periapsis after the deorbit burn: deep in the air on a body with an atmosphere, under the surface on one
        /// without, as MechJeb's deorbit does, so the descent comes down steeply enough to aim.
        /// </summary>
        public static double DeorbitPeriapsis(LandingBody body) =>
            body.AtmosphereHeight > 0 ? body.Radius + 0.3 * body.AtmosphereHeight : 0.9 * body.Radius;

        public static LandingPlan Plan(LandingBody body, LandingCraft craft, OrbitElements orbit, LandingTarget target, double targetAltitude, double earliestUt)
        {
            if (orbit.Eccentricity >= 1)
            {
                return LandingPlan.Fail("the vessel is on an escape path; settle into orbit first");
            }
            double floor = body.Radius + Math.Max(body.AtmosphereHeight, targetAltitude);
            if (orbit.PeriapsisRadius <= floor)
            {
                return LandingPlan.Fail("the vessel is not in a stable orbit; the landing starts from orbit");
            }
            var solver = new Solver(body, craft, orbit, target, targetAltitude);
            double step = orbit.Period / StepsPerOrbit;
            var crossings = new List<double>();
            double previousUt = earliestUt;
            double? previous = solver.AlongError(earliestUt, 0, ScanTolerance);
            for (int i = 1; i <= SearchOrbits * StepsPerOrbit; i++)
            {
                double ut = earliestUt + i * step;
                double? along = solver.AlongError(ut, 0, ScanTolerance);
                if (previous is double a && along is double b && a < 0 && b >= 0 && b - a < 1)
                {
                    crossings.Add(previousUt + step * (-a / (b - a)));
                }
                previous = along;
                previousUt = ut;
            }
            LandingPlan? best = null;
            foreach (double guess in crossings)
            {
                LandingPlan plan = solver.Refine(Math.Max(earliestUt, guess), earliestUt);
                if (plan.Ok && (best == null || plan.DeorbitDeltaV < best.DeorbitDeltaV))
                {
                    best = plan;
                }
            }
            return best ?? LandingPlan.Fail("no deorbit within " + SearchOrbits + " orbits brings the vessel down on the target");
        }

        private sealed class Solver
        {
            private const double MaxMiss = 2000;
            private readonly LandingBody _body;
            private readonly LandingCraft _craft;
            private readonly OrbitElements _orbit;
            private readonly LandingTarget _target;
            private readonly double _targetAltitude;
            private readonly double _periapsis;

            public Solver(LandingBody body, LandingCraft craft, OrbitElements orbit, LandingTarget target, double targetAltitude)
            {
                _body = body;
                _craft = craft;
                _orbit = orbit;
                _target = target;
                _targetAltitude = targetAltitude;
                _periapsis = DeorbitPeriapsis(body);
            }

            public double? AlongError(double ut, double normal, double tolerance) =>
                Errors(ut, normal, tolerance) is (double along, _, _) ? along : null;

            public LandingPlan Refine(double ut, double earliestUt)
            {
                double normal = 0;
                for (int i = 0; i < 8; i++)
                {
                    if (Errors(ut, normal, FineTolerance) is not (double along, double cross, _))
                    {
                        return LandingPlan.Fail("the deorbit misses the ground");
                    }
                    double dt = 2;
                    if (Errors(ut + dt, normal, FineTolerance) is not (double alongLater, _, _) || Math.Abs(alongLater - along) < 1e-12)
                    {
                        return LandingPlan.Fail("the deorbit does not converge");
                    }
                    ut = Math.Max(earliestUt, ut - along * dt / (alongLater - along));
                    if (Errors(ut, normal, FineTolerance) is not (_, double crossNow, _)
                        || Errors(ut, normal + 1, FineTolerance) is not (_, double crossTilted, _)
                        || Math.Abs(crossTilted - crossNow) < 1e-12)
                    {
                        return LandingPlan.Fail("the deorbit does not converge");
                    }
                    normal -= crossNow / (crossTilted - crossNow);
                    if (Math.Abs(along) * _body.Radius < 20 && Math.Abs(cross) * _body.Radius < 20)
                    {
                        break;
                    }
                }
                if (Errors(ut, normal, FineTolerance) is not (_, _, LandingPrediction prediction))
                {
                    return LandingPlan.Fail("the deorbit misses the ground");
                }
                double miss = LandingAim.Miss(_body, prediction, _target, _targetAltitude, prediction.Ut).Magnitude;
                if (miss > MaxMiss)
                {
                    return LandingPlan.Fail("the deorbit misses the target by " + (miss / 1000).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km");
                }
                StateVector s = _orbit.StateAt(ut);
                Vector3d burn = Burn(s, normal);
                return LandingPlan.Burn(ut, Vector3d.Dot(burn, s.Prograde), Vector3d.Dot(burn, s.Normal), Vector3d.Dot(burn, s.RadialOut), prediction, miss);
            }

            /// <summary>Along-track and cross-track miss in radians of the body, positive when the landing overshoots.</summary>
            private (double Along, double Cross, LandingPrediction Prediction)? Errors(double ut, double normal, double tolerance)
            {
                StateVector s = _orbit.StateAt(ut);
                Vector3d burn = Burn(s, normal);
                var after = new StateVector(s.Position, s.Velocity + burn);
                LandingCraft craft = _craft.AfterBurn(burn.Magnitude);
                LandingPrediction p = ReentryPredictor.Predict(_body, craft, after, ut, ReentrySettings.For(_body, craft, _targetAltitude, tolerance));
                if (!p.Landed)
                {
                    return null;
                }
                Vector3d plane = after.Normal;
                Vector3d target = _body.Surface(_target.Latitude, _target.Longitude, _targetAltitude, p.Ut);
                double along = Vector3d.AngleAbout(target, p.Position, plane);
                if (along > Math.PI)
                {
                    along -= Angles.TwoPi;
                }
                double cross = Math.Asin(Vector3d.Dot(p.Position.Normalized, plane)) - Math.Asin(Vector3d.Dot(target.Normalized, plane));
                return (along, cross, p);
            }

            private Vector3d Burn(StateVector s, double normal)
            {
                Vector3d up = s.Position.Normalized;
                Vector3d n = s.Normal;
                Vector3d along = Vector3d.Cross(n, up);
                double r = s.Position.Magnitude;
                double vr = Vector3d.Dot(s.Velocity, up);
                double vt = Vector3d.Dot(s.Velocity, along);
                double ratio = r * r / (_periapsis * _periapsis) - 1;
                double wanted = Math.Sqrt(Math.Max(0, (vr * vr + 2 * _body.Mu * (1 / _periapsis - 1 / r)) / ratio));
                return along * (wanted - vt) + n * normal;
            }
        }
    }
}

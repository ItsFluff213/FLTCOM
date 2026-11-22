using System;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    /// <summary>Where a hyperbolic pass aims: the miss vector in the B-plane, split along T (the body's equator) and R.</summary>
    public readonly struct BPlane
    {
        public BPlane(double t, double r, double excessSpeedSquared, double declination = 0)
        {
            T = t;
            R = r;
            ExcessSpeedSquared = excessSpeedSquared;
            Declination = declination;
        }

        /// <summary>How far the incoming asymptote points out of the body's equator, in radians.</summary>
        public double Declination { get; }

        /// <summary>Miss along the equator side; positive passes go round the body prograde.</summary>
        public double T { get; }

        public double R { get; }

        public double ExcessSpeedSquared { get; }

        public double Magnitude => Math.Sqrt(T * T + R * R);

        /// <summary>
        /// The B-plane of the conic through <paramref name="s"/> around a body of <paramref name="mu"/>; null if the conic is closed.
        /// Vallado, Fundamentals of Astrodynamics and Applications, the B-plane targeting section.
        /// </summary>
        public static BPlane? Of(double mu, StateVector s)
        {
            double r = s.Position.Magnitude;
            double vInf2 = Vector3d.Dot(s.Velocity, s.Velocity) - 2 * mu / r;
            Vector3d eVec = Kepler.EccentricityVector(mu, s);
            double e = eVec.Magnitude;
            if (vInf2 <= 0 || e <= 1)
            {
                return null;
            }
            Vector3d h = Vector3d.Cross(s.Position, s.Velocity);
            Vector3d hHat = h.Normalized;
            Vector3d eHat = eVec / e;
            Vector3d incoming = eHat / e + Vector3d.Cross(hHat, eHat) * (Math.Sqrt(e * e - 1) / e);
            Vector3d b = Vector3d.Cross(incoming, hHat) * (h.Magnitude / Math.Sqrt(vInf2));
            Vector3d pole = new Vector3d(0, 0, 1);
            Vector3d tAxis = Vector3d.Cross(incoming, pole);
            if (tAxis.Magnitude < 1e-9)
            {
                tAxis = Vector3d.Cross(incoming, Vector3d.UnitX);
            }
            tAxis = tAxis.Normalized;
            Vector3d rAxis = Vector3d.Cross(incoming, tAxis);
            return new BPlane(Vector3d.Dot(b, tAxis), Vector3d.Dot(b, rAxis), vInf2, Math.Asin(Math.Max(-1, Math.Min(1, incoming.Z))));
        }

        /// <summary>The miss distance that brings a pass of excess speed squared <paramref name="vInf2"/> to periapsis radius <paramref name="rp"/>.</summary>
        public static double ImpactParameter(double mu, double rp, double vInf2) => rp * Math.Sqrt(1 + 2 * mu / (rp * vInf2));
    }

    /// <summary>Burns that put an encounter's periapsis where the operator wants it.</summary>
    public static class Targeting
    {
        private const double Probe = 0.1;
        private const int Iterations = 12;

        public static StateVector Apply(StateVector s, double prograde, double normal, double radial) =>
            new StateVector(s.Position, s.Velocity + s.Prograde * prograde + s.Normal * normal + s.RadialOut * radial);

        /// <summary>The encounter with <paramref name="target"/> after burning (prograde, normal, radial) from <paramref name="at"/>.</summary>
        public static Encounter? After(SolarSystem system, Patch at, double prograde, double normal, double radial, string target, double horizon) =>
            PatchedConics.Find(system, new Patch(at.Body, Apply(at.State, prograde, normal, radial), at.Ut), target, horizon);

        /// <summary>
        /// The B-plane aim for a periapsis radius <paramref name="rp"/>. Without an <paramref name="inclination"/> it keeps the pass's own side,
        /// turned no more than 60 degrees from prograde. With one it turns the aim so cos i = cos(declination) cos(angle), on the side nearer
        /// the pass; an inclination the asymptote cannot give is clamped to the nearest it can.
        /// </summary>
        public static (double T, double R) Aim(double mu, BPlane now, double rp, double? inclination = null)
        {
            double angle = Math.Atan2(now.R, now.T);
            if (inclination is double i)
            {
                double turn = Math.Acos(Math.Max(-1, Math.Min(1, Math.Cos(i) / Math.Cos(now.Declination))));
                angle = angle >= 0 ? turn : -turn;
            }
            else
            {
                double limit = Math.PI / 3;
                angle = Math.Max(-limit, Math.Min(limit, angle));
            }
            double b = BPlane.ImpactParameter(mu, rp, now.ExcessSpeedSquared);
            return (b * Math.Cos(angle), b * Math.Sin(angle));
        }

        /// <summary>
        /// The smallest burn at <paramref name="at"/> that brings the encounter with <paramref name="target"/> to periapsis radius
        /// <paramref name="rp"/>: Gauss-Newton on the B-plane miss, finite-difference Jacobian, minimum-norm steps. Null if no burn finds the body.
        /// </summary>
        public static Vector3d? Correct(SolarSystem system, Patch at, string target, double rp, double horizon, double maxDeltaV, double? inclination = null)
        {
            double mu = system[target].Body.GravParameter;
            BPlane? start = Miss(system, at, new Vector3d(0, 0, 0), target, horizon);
            if (start == null)
            {
                return null;
            }
            (double aimT, double aimR) = Aim(mu, start.Value, rp, inclination);
            double tolerance = Math.Max(100, 0.002 * BPlane.ImpactParameter(mu, rp, start.Value.ExcessSpeedSquared));
            var x = new Vector3d(0, 0, 0);
            BPlane current = start.Value;
            for (int i = 0; i < Iterations; i++)
            {
                double f1 = current.T - aimT;
                double f2 = current.R - aimR;
                double error = Math.Sqrt(f1 * f1 + f2 * f2);
                if (error < tolerance)
                {
                    return x;
                }
                var columns = new Vector3d[3];
                for (int k = 0; k < 3; k++)
                {
                    Vector3d probe = x + Axis(k) * Probe;
                    BPlane? moved = Miss(system, at, probe, target, horizon);
                    if (moved == null)
                    {
                        return null;
                    }
                    columns[k] = new Vector3d((moved.Value.T - current.T) / Probe, (moved.Value.R - current.R) / Probe, 0);
                }
                // J is 2x3; the minimum-norm step is -J^T (J J^T)^-1 f.
                double a = Row(columns, 0, 0), b = Row(columns, 0, 1), d = Row(columns, 1, 1);
                double det = a * d - b * b;
                if (Math.Abs(det) < 1e-12)
                {
                    return null;
                }
                double y1 = (d * f1 - b * f2) / det;
                double y2 = (-b * f1 + a * f2) / det;
                var step = new Vector3d(
                    -(columns[0].X * y1 + columns[0].Y * y2),
                    -(columns[1].X * y1 + columns[1].Y * y2),
                    -(columns[2].X * y1 + columns[2].Y * y2));
                bool accepted = false;
                for (int halving = 0; halving < 8 && !accepted; halving++)
                {
                    Vector3d trial = x + step;
                    if (trial.Magnitude <= maxDeltaV)
                    {
                        BPlane? moved = Miss(system, at, trial, target, horizon);
                        if (moved != null && Distance(moved.Value, aimT, aimR) < error)
                        {
                            x = trial;
                            current = moved.Value;
                            accepted = true;
                        }
                    }
                    step /= 2;
                }
                if (!accepted)
                {
                    return null;
                }
            }
            return Distance(current, aimT, aimR) < 10 * tolerance ? x : (Vector3d?)null;
        }

        private static BPlane? Miss(SolarSystem system, Patch at, Vector3d burn, string target, double horizon)
        {
            Encounter? e = After(system, at, burn.X, burn.Y, burn.Z, target, horizon);
            return e == null ? null : BPlane.Of(system[target].Body.GravParameter, e.Value.Entry.State);
        }

        private static Vector3d Axis(int k) => k == 0 ? new Vector3d(1, 0, 0) : k == 1 ? new Vector3d(0, 1, 0) : new Vector3d(0, 0, 1);

        private static double Row(Vector3d[] columns, int i, int j)
        {
            double sum = 0;
            foreach (Vector3d c in columns)
            {
                sum += (i == 0 ? c.X : c.Y) * (j == 0 ? c.X : c.Y);
            }
            return sum;
        }

        private static double Distance(BPlane b, double t, double r) => Math.Sqrt((b.T - t) * (b.T - t) + (b.R - r) * (b.R - r));
    }
}

/*
 * Copyright Lamont Granquist, Sebastien Gaggini and the MechJeb contributors
 * SPDX-License-Identifier: LicenseRef-PD-hp OR Unlicense OR CC0-1.0 OR 0BSD OR MIT-0 OR MIT OR LGPL-2.1+
 * Source path: MechJeb2 MechJebLib/TwoBody/Shepperd.cs (Solve only, on FLTCOM's Vector3d; modified for FLTCOM)
 */
using System;

namespace FLTCOM.Core.TwoBody
{
    /// <summary>
    /// Kepler propagation for any conic, closed or not. Propagate is Shepperd's universal method:
    /// Shepperd SW. Universal Keplerian state transition matrix. Celestial Mechanics. 1985;35(2):129-44.
    /// </summary>
    public static class Kepler
    {
        private const int MaxFractionTerms = 1000000;

        /// <summary>The state <paramref name="dt"/> seconds after <paramref name="state"/>, negative going back.</summary>
        public static StateVector Propagate(double mu, StateVector state, double dt)
        {
            if (dt == 0)
            {
                return state;
            }
            Vector3d ri = state.Position;
            Vector3d vi = state.Velocity;
            double tolerance = 1.0e-12;
            double u = 0;
            double umax = double.MaxValue;
            double umin = double.MinValue;
            double orbits = 0;
            double threshold = tolerance * Math.Abs(dt);
            double r0 = ri.Magnitude;
            double n0 = Vector3d.Dot(ri, vi);
            double beta = 2.0 * (mu / r0) - Vector3d.Dot(vi, vi);

            if (beta != 0.0)
            {
                umax = 1.0 / Math.Sqrt(Math.Abs(beta));
                umin = -1.0 / Math.Sqrt(Math.Abs(beta));
            }
            if (beta > 0.0)
            {
                orbits = beta * dt - 2 * n0;
                orbits = 1 + orbits * Math.Sqrt(beta) / (Math.PI * mu);
                orbits = Math.Floor(orbits / 2);
            }

            double uold = double.MinValue;
            double dtold = double.MinValue;
            double u1 = 0.0;
            double u2 = 0.0;
            double r1 = 0.0;
            for (int i = 1; i < 50; i++)
            {
                double q = beta * u * u;
                q /= 1.0 + q;
                double gcf = ContinuedFraction(q);
                double h0 = 1 - 2 * q;
                double h1 = 2 * u * (1 - q);
                double u0 = 2 * h0 * h0 - 1;
                u1 = 2 * h0 * h1;
                u2 = 2 * h1 * h1;
                double u3 = 2 * h1 * u2 * gcf / 3;
                if (orbits != 0)
                {
                    u3 += 2 * Math.PI * orbits / (beta * Math.Sqrt(beta));
                }
                r1 = r0 * u0 + n0 * u1 + mu * u2;
                double t = r0 * u1 + n0 * u2 + mu * u3;
                double slope = 4 * r1 / (1 + beta * u * u);
                double terror = dt - t;
                if (Math.Abs(terror) < threshold || (i > 1 && (u == uold || t == dtold)))
                {
                    break;
                }
                uold = u;
                dtold = t;
                double ustep = terror / slope;
                if (ustep > 0)
                {
                    umin = u;
                    u += ustep;
                    if (u > umax)
                    {
                        u = (umin + umax) / 2;
                    }
                }
                else
                {
                    umax = u;
                    u += ustep;
                    if (u < umin)
                    {
                        u = (umin + umax) / 2;
                    }
                }
            }

            double f = 1.0 - mu / r0 * u2;
            double gg = 1.0 - mu / r1 * u2;
            double g = r0 * u1 + n0 * u2;
            double ff = -mu * u1 / (r0 * r1);
            return new StateVector(ri * f + vi * g, ri * ff + vi * gg);
        }

        /// <summary>
        /// The state at <paramref name="ut"/> from Keplerian elements with the mean anomaly at <paramref name="epoch"/>.
        /// Hyperbolic orbits (e above 1, a negative) take the hyperbolic mean anomaly, as KSP stores it.
        /// </summary>
        public static StateVector FromElements(double mu, double a, double e, double inclination, double lan, double argPe, double meanAnomalyAtEpoch, double epoch, double ut)
        {
            if (e < 1)
            {
                return new OrbitElements(mu, a, e, inclination, lan, argPe, meanAnomalyAtEpoch, epoch).StateAt(ut);
            }
            double n = Math.Sqrt(mu / (-a * -a * -a));
            double m = meanAnomalyAtEpoch + n * (ut - epoch);
            double h = HyperbolicFromMean(m, e);
            double nu = 2 * Math.Atan(Math.Sqrt((e + 1) / (e - 1)) * Math.Tanh(h / 2));
            double p = a * (1 - e * e);
            double r = p / (1 + e * Math.Cos(nu));
            double vScale = Math.Sqrt(mu / p);
            (Vector3d pAxis, Vector3d qAxis) = Perifocal(inclination, lan, argPe);
            return new StateVector(
                pAxis * (r * Math.Cos(nu)) + qAxis * (r * Math.Sin(nu)),
                pAxis * (-vScale * Math.Sin(nu)) + qAxis * (vScale * (e + Math.Cos(nu))));
        }

        public static double SemiMajorAxis(double mu, StateVector s) => mu / (2 * mu / s.Position.Magnitude - Vector3d.Dot(s.Velocity, s.Velocity));

        public static Vector3d EccentricityVector(double mu, StateVector s) =>
            Vector3d.Cross(s.Velocity, Vector3d.Cross(s.Position, s.Velocity)) / mu - s.Position.Normalized;

        public static double PeriapsisRadius(double mu, StateVector s)
        {
            double e = EccentricityVector(mu, s).Magnitude;
            double h = Vector3d.Cross(s.Position, s.Velocity).Magnitude;
            return h * h / mu / (1 + e);
        }

        /// <summary>Seconds from <paramref name="s"/> to the next periapsis; negative on a hyperbola already past it.</summary>
        public static double TimeToPeriapsis(double mu, StateVector s)
        {
            Vector3d eVec = EccentricityVector(mu, s);
            double e = eVec.Magnitude;
            double a = SemiMajorAxis(mu, s);
            Vector3d hHat = Vector3d.Cross(s.Position, s.Velocity).Normalized;
            double nu = e < 1e-9 ? 0 : Math.Atan2(Vector3d.Dot(hHat, Vector3d.Cross(eVec, s.Position)), Vector3d.Dot(eVec, s.Position));
            if (e < 1)
            {
                double ecc = 2 * Math.Atan2(Math.Sqrt(1 - e) * Math.Sin(nu / 2), Math.Sqrt(1 + e) * Math.Cos(nu / 2));
                double mean = Angles.Wrap(ecc - e * Math.Sin(ecc));
                return (Angles.TwoPi - mean) / Math.Sqrt(mu / (a * a * a));
            }
            double f = 2 * Atanh(Math.Sqrt((e - 1) / (e + 1)) * Math.Tan(nu / 2));
            double m = e * Math.Sinh(f) - f;
            return -m / Math.Sqrt(mu / (-a * -a * -a));
        }

        /// <summary>Speed at periapsis: no point of the conic after now moves faster.</summary>
        public static double MaxSpeed(double mu, StateVector s)
        {
            double rp = Math.Max(PeriapsisRadius(mu, s), 1);
            double a = SemiMajorAxis(mu, s);
            return Math.Sqrt(Math.Max(0, mu * (2 / rp - 1 / a)));
        }

        public static (Vector3d P, Vector3d Q) Perifocal(double inclination, double lan, double argPe)
        {
            double cO = Math.Cos(lan), sO = Math.Sin(lan);
            double cw = Math.Cos(argPe), sw = Math.Sin(argPe);
            double ci = Math.Cos(inclination), si = Math.Sin(inclination);
            return (new Vector3d(cO * cw - sO * sw * ci, sO * cw + cO * sw * ci, sw * si),
                new Vector3d(-cO * sw - sO * cw * ci, -sO * sw + cO * cw * ci, cw * si));
        }

        private static double HyperbolicFromMean(double m, double e)
        {
            double h = Math.Log(2 * Math.Abs(m) / e + 1.8) * Math.Sign(m);
            for (int i = 0; i < 100; i++)
            {
                double step = (e * Math.Sinh(h) - h - m) / (e * Math.Cosh(h) - 1);
                h -= step;
                if (Math.Abs(step) < 1e-13 * Math.Max(1, Math.Abs(h)))
                {
                    break;
                }
            }
            return h;
        }

        private static double Atanh(double x) => 0.5 * Math.Log((1 + x) / (1 - x));

        // The continued fraction G(3, 0, 3/2, q) that gives U3 directly.
        private static double ContinuedFraction(double q)
        {
            double l = 1, d = 3, k = -5;
            double n = 0;
            double r = 1;
            double s = 1;
            double gcf = 1;
            double gold = 0;
            for (int i = 0; gcf != gold; i++)
            {
                if (double.IsNaN(gcf) || i == MaxFractionTerms)
                {
                    throw new InvalidOperationException("Kepler: the continued fraction did not converge for q=" + q);
                }
                k = -k;
                l += 2;
                d += 4 * l;
                n += (1 + k) * l;
                r = d / (d - n * r * q);
                s = (r - 1) * s;
                gold = gcf;
                gcf = gold + s;
            }
            return gcf;
        }
    }
}

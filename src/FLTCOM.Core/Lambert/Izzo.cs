/*
 * Copyright Lamont Granquist, Sebastien Gaggini and the MechJeb contributors
 * SPDX-License-Identifier: LicenseRef-PD-hp OR Unlicense OR CC0-1.0 OR 0BSD OR MIT-0 OR MIT OR LGPL-2.1+
 *
 * Source path: MechJebLib/Lambert/Izzo.cs (MechJeb2), ported to the FLTCOM Vector3d; the dual-number overload is left out.
 */

using System;
using FLTCOM.Core.TwoBody;

/*
 * Izzo's algorithm for solving Lambert's problem.
 *
 * Ported to C# from poliastro (MIT License), poliastro/core/iod.py
 * (commit 21fd7719e89a7d22b4eac63141a60a7f1b01768c), which itself implements:
 *
 *   Izzo, D. "Revisiting Lambert's problem." Celestial Mechanics and
 *   Dynamical Astronomy 121.1 (2015): 1-15.
 */
namespace FLTCOM.Core.Lambert
{
    /// <summary>Which of the two arcs between two positions a Lambert solution takes.</summary>
    public enum TransferGeometry
    {
        ShortWay,
        LongWay,

        /// <summary>The arc whose angular momentum points along the given axis.</summary>
        Prograde,

        /// <summary>The arc whose angular momentum points against the given axis.</summary>
        Retrograde,
    }

    /// <summary>Solves Lambert's problem using Izzo's algorithm (Householder iteration on the non-dimensional time-of-flight equation).</summary>
    public static class Izzo
    {
        /// <summary>The velocities at <paramref name="r1"/> and <paramref name="r2"/> of the conic that joins them in <paramref name="tof"/> seconds.</summary>
        /// <param name="nrev">Number of full revolutions (+ long-period, - short-period for nrev != 0).</param>
        /// <param name="h">Axis for <see cref="TransferGeometry.Prograde"/> and <see cref="TransferGeometry.Retrograde"/>; need not be normalized.</param>
        public static (Vector3d V1, Vector3d V2) Solve(double mu, Vector3d r1, Vector3d r2, double tof,
            TransferGeometry direction = TransferGeometry.ShortWay, int nrev = 0, Vector3d? h = null,
            int numiter = 35, double rtol = 1e-8)
        {
            if (!(tof > 0) || double.IsInfinity(tof) || !(mu > 0) || double.IsInfinity(mu))
            {
                throw new ArgumentException("time of flight and mu must be positive and finite");
            }
            if (Vector3d.Cross(r1, r2).Magnitude == 0)
            {
                throw new ArgumentException("Lambert solution cannot be computed for collinear vectors");
            }

            // negative directions flip the path of the multi-revolution case
            bool lowpath = nrev >= 0;
            nrev = Math.Abs(nrev);

            Vector3d c = r2 - r1;
            double cNorm = c.Magnitude;
            double r1Norm = r1.Magnitude;
            double r2Norm = r2.Magnitude;

            // Semiperimeter
            double s = (r1Norm + r2Norm + cNorm) * 0.5;

            Vector3d iR1 = r1 / r1Norm;
            Vector3d iR2 = r2 / r2Norm;
            Vector3d iH = Vector3d.Cross(iR1, iR2).Normalized;

            double ll = Math.Sqrt(1 - Math.Min(1.0, cNorm / s));

            bool flip = direction == TransferGeometry.LongWay || direction == TransferGeometry.Retrograde;
            if (direction == TransferGeometry.Prograde || direction == TransferGeometry.Retrograde)
            {
                if (h == null)
                {
                    throw new ArgumentException("Prograde or Retrograde directions require a normal vector", nameof(h));
                }
                flip ^= Vector3d.Dot(iH, h.Value) < 0;
            }

            Vector3d iT1, iT2;
            if (flip)
            {
                ll = -ll;
                iT1 = Vector3d.Cross(iR1, iH);
                iT2 = Vector3d.Cross(iR2, iH);
            }
            else
            {
                iT1 = Vector3d.Cross(iH, iR1);
                iT2 = Vector3d.Cross(iH, iR2);
            }

            // Non-dimensional time of flight
            double t = Math.Sqrt(2 * mu / (s * s * s)) * tof;

            (double x, double y) = FindXY(ll, t, nrev, numiter, lowpath, rtol);

            double gamma = Math.Sqrt(mu * s / 2);
            double rho = (r1Norm - r2Norm) / cNorm;
            double sigma = Math.Sqrt(1 - rho * rho);

            (double vr1, double vr2, double vt1, double vt2) = Reconstruct(x, y, r1Norm, r2Norm, ll, gamma, rho, sigma);

            Vector3d v1 = vr1 * iR1 + vt1 * iT1;
            Vector3d v2 = vr2 * iR2 + vt2 * iT2;
            return (v1, v2);
        }

        private static (double Vr1, double Vr2, double Vt1, double Vt2) Reconstruct(double x, double y, double r1, double r2, double ll, double gamma, double rho, double sigma)
        {
            double vr1 = gamma * (ll * y - x - rho * (ll * y + x)) / r1;
            double vr2 = -gamma * (ll * y - x + rho * (ll * y + x)) / r2;
            double vt1 = gamma * sigma * (y + ll * x) / r1;
            double vt2 = gamma * sigma * (y + ll * x) / r2;
            return (vr1, vr2, vt1, vt2);
        }

        private static (double X, double Y) FindXY(double ll, double t, int m, int numiter, bool lowpath, double rtol)
        {
            // NOTE: we dropped the determination of mMax and prefer to just let
            // the householder iteration fail to converge if the caller asks for
            // something infeasible.
            double x0 = InitialGuess(t, ll, m, lowpath);
            double x = Householder(x0, t, ll, m, rtol, numiter);
            double y = Math.Sqrt(1 - ll * ll * (1 - x * x));
            return (x, y);
        }

        /// <summary>The auxiliary angle psi (Eq. 17), via the appropriate inverse function.</summary>
        private static double ComputePsi(double x, double y, double ll)
        {
            if (x >= -1 && x < 1)
            {
                // Elliptic motion. Use arc cosine to avoid numerical errors.
                return SafeAcos(x * y + ll * (1 - x * x));
            }
            if (x > 1)
            {
                // Hyperbolic motion. The hyperbolic sine is bijective.
                double z = (y - x * ll) * Math.Sqrt(x * x - 1);
                return Math.Log(z + Math.Sqrt(z * z + 1));
            }
            // Parabolic motion
            return 0.0;
        }

        private static double TofEquationY(double x, double y, double t0, double ll, int m)
        {
            double t;
            if (m == 0 && Math.Sqrt(0.6) < x && x < Math.Sqrt(1.4))
            {
                double eta = y - ll * x;
                double s1 = (1 - ll - x * eta) * 0.5;
                double q = 4.0 / 3.0 * Hyp2F1B(s1);
                t = (eta * eta * eta * q + 4 * ll * eta) * 0.5;
            }
            else
            {
                double psi = ComputePsi(x, y, ll);
                t = ((psi + m * Math.PI) / Math.Sqrt(Math.Abs(1 - x * x)) - x + ll * y) / (1 - x * x);
            }
            return t - t0;
        }

        private static double InitialGuess(double t, double ll, int m, bool lowpath)
        {
            // NOTE: this is the m == 0 initial guess algorithm from pykep, not from the paper, or from poliastro.
            if (m == 0)
            {
                double ll2 = ll * ll;
                double ll3 = ll * ll2;
                double t0 = SafeAcos(ll) + ll * Math.Sqrt(1 - ll2); // Equation 19
                double t1 = 2 * (1 - ll3) / 3.0; // Equation 21
                if (t >= t0)
                {
                    return -(t - t0) / (t - t0 + 4);
                }
                if (t < t1)
                {
                    return t1 * (t1 - t) / (2.0 / 5.0 * (1 - ll2 * ll3) * t) + 1;
                }
                // constant is just Log(2)
                return Math.Pow(t / t0, 0.69314718055994529 / Math.Log(t1 / t0)) - 1.0;
            }
            // we change the meaning of lowpath compared to poliastro here out of smoothness concerns
            if (lowpath)
            {
                double r = Math.Pow(8 * t / (m * Math.PI), 2.0 / 3.0);
                return (r - 1) / (r + 1);
            }
            double l = Math.Pow((m * Math.PI + Math.PI) / (8 * t), 2.0 / 3.0);
            return (l - 1) / (l + 1);
        }

        /// <summary>A zero of the time-of-flight equation by the Householder method.</summary>
        private static double Householder(double p0, double t0, double ll, int m, double tol, int maxiter)
        {
            for (int ii = 0; ii < maxiter; ii++)
            {
                double y = Math.Sqrt(1 - ll * ll * (1 - p0 * p0));
                double fval = TofEquationY(p0, y, t0, ll, m);
                double t = fval + t0;
                double fder = (3 * t * p0 - 2 + 2 * ll * ll * ll * p0 / y) / (1 - p0 * p0);
                double fder2 = (3 * t + 5 * p0 * fder + 2 * (1 - ll * ll) * ll * ll * ll / (y * y * y)) / (1 - p0 * p0);
                double fder3 = (7 * p0 * fder2 + 8 * fder - 6 * (1 - ll * ll) * Math.Pow(ll, 5) * p0 / Math.Pow(y, 5)) / (1 - p0 * p0);

                // Householder step (quartic)
                double p = p0 - fval * ((fder * fder - fval * fder2 / 2)
                    / (fder * (fder * fder - fval * fder2) + fder3 * fval * fval / 6));

                if (Math.Abs(p - p0) < tol)
                {
                    return p;
                }
                p0 = p;
            }
            throw new InvalidOperationException("Lambert solution failed to converge");
        }

        /// <summary>Hypergeometric function 2F1(3, 1; 5/2; x), see Battin. Ported from poliastro/_math/special.py.</summary>
        private static double Hyp2F1B(double x)
        {
            if (x >= 1.0)
            {
                return double.PositiveInfinity;
            }
            double res = 1.0;
            double term = 1.0;
            int ii = 0;
            while (true)
            {
                term = term * (3 + ii) * (1 + ii) / (2.5 + ii) * x / (ii + 1);
                double resOld = res;
                res += term;
                if (resOld == res)
                {
                    return res;
                }
                ii += 1;
            }
        }

        private static double SafeAcos(double x) => Math.Acos(Math.Max(-1, Math.Min(1, x)));
    }
}

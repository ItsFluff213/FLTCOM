// Attribution: two-body orbit math written for FLTCOM from public textbook formulas:
// Vallado, "Fundamentals of Astrodynamics and Applications", 4th ed., algorithms 2 (Kepler's equation),
// 9 (RV2COE) and 10 (COE2RV); Curtis, "Orbital Mechanics for Engineering Students", chapters 3 and 4.
// Not ported from MechJebLib: MechJeb is GPL-3.0 and FLTCOM is MIT.
using System;

namespace FLTCOM.Core.TwoBody
{
    /// <summary>A closed Keplerian orbit. Angles in radians, distances in metres, times in seconds of universal time.</summary>
    public sealed class OrbitElements
    {
        private const double Tiny = 1e-9;

        public OrbitElements(double mu, double semiMajorAxis, double eccentricity, double inclination, double longitudeOfAscendingNode, double argumentOfPeriapsis, double meanAnomalyAtEpoch, double epoch)
        {
            if (!(mu > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(mu), mu, "must be positive");
            }
            if (!(semiMajorAxis > 0) || !(eccentricity >= 0 && eccentricity < 1))
            {
                throw new ArgumentOutOfRangeException(nameof(eccentricity), eccentricity, "only closed orbits are supported");
            }
            Mu = mu;
            SemiMajorAxis = semiMajorAxis;
            Eccentricity = eccentricity;
            Inclination = inclination;
            LongitudeOfAscendingNode = Angles.Wrap(longitudeOfAscendingNode);
            ArgumentOfPeriapsis = Angles.Wrap(argumentOfPeriapsis);
            MeanAnomalyAtEpoch = Angles.Wrap(meanAnomalyAtEpoch);
            Epoch = epoch;
        }

        public double Mu { get; }

        public double SemiMajorAxis { get; }

        public double Eccentricity { get; }

        public double Inclination { get; }

        public double LongitudeOfAscendingNode { get; }

        public double ArgumentOfPeriapsis { get; }

        public double MeanAnomalyAtEpoch { get; }

        public double Epoch { get; }

        public double PeriapsisRadius => SemiMajorAxis * (1 - Eccentricity);

        public double ApoapsisRadius => SemiMajorAxis * (1 + Eccentricity);

        public double MeanMotion => Math.Sqrt(Mu / (SemiMajorAxis * SemiMajorAxis * SemiMajorAxis));

        public double Period => Angles.TwoPi / MeanMotion;

        /// <summary>Circular orbit at <paramref name="radius"/> with the given plane, at true anomaly 0 at <paramref name="epoch"/>.</summary>
        public static OrbitElements Circular(double mu, double radius, double inclination, double epoch) =>
            new OrbitElements(mu, radius, 0, inclination, 0, 0, 0, epoch);

        public static OrbitElements FromApsides(double mu, double periapsisRadius, double apoapsisRadius, double inclination, double epoch)
        {
            double a = (periapsisRadius + apoapsisRadius) / 2;
            double e = (apoapsisRadius - periapsisRadius) / (apoapsisRadius + periapsisRadius);
            return new OrbitElements(mu, a, e, inclination, 0, 0, 0, epoch);
        }

        public double SpeedAtRadius(double radius) => Math.Sqrt(Mu * (2 / radius - 1 / SemiMajorAxis));

        /// <summary>The same orbit with the vessel <paramref name="seconds"/> behind: where it was, it now is that much later.</summary>
        public OrbitElements Later(double seconds) =>
            new OrbitElements(Mu, SemiMajorAxis, Eccentricity, Inclination, LongitudeOfAscendingNode, ArgumentOfPeriapsis, MeanAnomalyAtEpoch, Epoch + seconds);

        /// <summary>Longitude of ascending node plus argument of periapsis plus true anomaly; compares positions on coplanar orbits.</summary>
        public double TrueLongitudeAt(double ut) => Angles.Wrap(LongitudeOfAscendingNode + ArgumentOfPeriapsis + TrueAnomalyAt(ut));

        public double MeanAnomalyAt(double ut) => Angles.Wrap(MeanAnomalyAtEpoch + MeanMotion * (ut - Epoch));

        public double TrueAnomalyAt(double ut) => TrueFromEccentric(EccentricFromMean(MeanAnomalyAt(ut), Eccentricity), Eccentricity);

        /// <summary>The first universal time strictly after <paramref name="afterUt"/> at which the orbit reaches <paramref name="trueAnomaly"/>.</summary>
        public double NextTimeAtTrueAnomaly(double trueAnomaly, double afterUt)
        {
            double target = MeanFromTrue(trueAnomaly, Eccentricity);
            double delta = Angles.Wrap(target - MeanAnomalyAt(afterUt));
            if (delta < Tiny)
            {
                delta += Angles.TwoPi;
            }
            return afterUt + delta / MeanMotion;
        }

        public StateVector StateAt(double ut) => StateAtTrueAnomaly(TrueAnomalyAt(ut));

        public StateVector StateAtTrueAnomaly(double trueAnomaly)
        {
            double p = SemiMajorAxis * (1 - Eccentricity * Eccentricity);
            double cosNu = Math.Cos(trueAnomaly);
            double sinNu = Math.Sin(trueAnomaly);
            double r = p / (1 + Eccentricity * cosNu);
            double vScale = Math.Sqrt(Mu / p);
            Vector3d pAxis = PerifocalP();
            Vector3d qAxis = PerifocalQ();
            Vector3d position = pAxis * (r * cosNu) + qAxis * (r * sinNu);
            Vector3d velocity = pAxis * (-vScale * sinNu) + qAxis * (vScale * (Eccentricity + cosNu));
            return new StateVector(position, velocity);
        }

        /// <summary>True anomaly of the ascending node. On an equatorial orbit the node is the reference x axis.</summary>
        public double AscendingNodeTrueAnomaly => Angles.Wrap(-ArgumentOfPeriapsis);

        public static OrbitElements FromState(double mu, StateVector state, double ut)
        {
            Vector3d r = state.Position;
            Vector3d v = state.Velocity;
            double rMag = r.Magnitude;
            Vector3d h = Vector3d.Cross(r, v);
            Vector3d hHat = h.Normalized;
            var node = new Vector3d(-h.Y, h.X, 0);
            Vector3d eVec = (r * (Vector3d.Dot(v, v) - mu / rMag) - v * Vector3d.Dot(r, v)) / mu;
            double e = eVec.Magnitude;
            double energy = Vector3d.Dot(v, v) / 2 - mu / rMag;
            double a = -mu / (2 * energy);
            double inclination = Math.Acos(Math.Max(-1, Math.Min(1, hHat.Z)));

            bool equatorial = node.Magnitude < Tiny * h.Magnitude;
            Vector3d nodeDir = equatorial ? Vector3d.UnitX : node.Normalized;
            double lan = equatorial ? 0 : Math.Atan2(node.Y, node.X);
            Vector3d periDir = e < Tiny ? nodeDir : eVec / e;
            double argPe = Vector3d.AngleAbout(nodeDir, periDir, hHat);
            double nu = Vector3d.AngleAbout(periDir, r / rMag, hHat);
            if (e < Tiny)
            {
                e = 0;
            }
            return new OrbitElements(mu, a, e, inclination, lan, argPe, MeanFromTrue(nu, e), ut);
        }

        public static double EccentricFromMean(double meanAnomaly, double e)
        {
            double m = Angles.Wrap(meanAnomaly);
            double ecc = e < 0.8 ? m : Math.PI;
            for (int i = 0; i < 50; i++)
            {
                double step = (ecc - e * Math.Sin(ecc) - m) / (1 - e * Math.Cos(ecc));
                ecc -= step;
                if (Math.Abs(step) < 1e-13)
                {
                    break;
                }
            }
            return ecc;
        }

        public static double TrueFromEccentric(double eccentricAnomaly, double e) =>
            Angles.Wrap(2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(eccentricAnomaly / 2), Math.Sqrt(1 - e) * Math.Cos(eccentricAnomaly / 2)));

        public static double MeanFromTrue(double trueAnomaly, double e)
        {
            double ecc = 2 * Math.Atan2(Math.Sqrt(1 - e) * Math.Sin(trueAnomaly / 2), Math.Sqrt(1 + e) * Math.Cos(trueAnomaly / 2));
            return Angles.Wrap(ecc - e * Math.Sin(ecc));
        }

        private Vector3d PerifocalP()
        {
            double cO = Math.Cos(LongitudeOfAscendingNode), sO = Math.Sin(LongitudeOfAscendingNode);
            double cw = Math.Cos(ArgumentOfPeriapsis), sw = Math.Sin(ArgumentOfPeriapsis);
            double ci = Math.Cos(Inclination), si = Math.Sin(Inclination);
            return new Vector3d(cO * cw - sO * sw * ci, sO * cw + cO * sw * ci, sw * si);
        }

        private Vector3d PerifocalQ()
        {
            double cO = Math.Cos(LongitudeOfAscendingNode), sO = Math.Sin(LongitudeOfAscendingNode);
            double cw = Math.Cos(ArgumentOfPeriapsis), sw = Math.Sin(ArgumentOfPeriapsis);
            double ci = Math.Cos(Inclination), si = Math.Sin(Inclination);
            return new Vector3d(-cO * sw - sO * cw * ci, -sO * sw + cO * cw * ci, cw * si);
        }
    }

    public readonly struct StateVector
    {
        public StateVector(Vector3d position, Vector3d velocity)
        {
            Position = position;
            Velocity = velocity;
        }

        public Vector3d Position { get; }

        public Vector3d Velocity { get; }

        public Vector3d Prograde => Velocity.Normalized;

        public Vector3d Normal => Vector3d.Cross(Position, Velocity).Normalized;

        public Vector3d RadialOut => Vector3d.Cross(Prograde, Normal);
    }
}

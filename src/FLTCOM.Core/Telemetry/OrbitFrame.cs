using System;
using FLTCOM.Core.Protocol;

namespace FLTCOM.Core.Telemetry
{
    /// <summary>
    /// KSP keeps orbit angles in degrees and the mean anomaly at the orbit's own epoch. The protocol wants radians
    /// and every mean anomaly at the snapshot's universal time.
    /// </summary>
    public static class OrbitFrame
    {
        private const double DegreesToRadians = Math.PI / 180;
        private const double FullTurn = 2 * Math.PI;

        public static Orbit AtTime(
            string referenceBody,
            double gravParameter,
            double semiMajorAxis,
            double eccentricity,
            double inclinationDegrees,
            double longitudeOfAscendingNodeDegrees,
            double argumentOfPeriapsisDegrees,
            double meanAnomalyAtEpoch,
            double epoch,
            double ut)
        {
            double meanMotion = Math.Sqrt(gravParameter / Math.Pow(Math.Abs(semiMajorAxis), 3));
            double meanAnomaly = meanAnomalyAtEpoch + meanMotion * (ut - epoch);
            if (eccentricity < 1)
            {
                meanAnomaly = Wrap(meanAnomaly);
            }
            return new Orbit(
                referenceBody,
                semiMajorAxis,
                eccentricity,
                inclinationDegrees * DegreesToRadians,
                Wrap(longitudeOfAscendingNodeDegrees * DegreesToRadians),
                Wrap(argumentOfPeriapsisDegrees * DegreesToRadians),
                meanAnomaly);
        }

        /// <summary>An angle in degrees, folded into [0, 2π) radians.</summary>
        public static double Radians(double degrees) => Wrap(degrees * DegreesToRadians);

        private static double Wrap(double radians)
        {
            double wrapped = radians % FullTurn;
            return wrapped < 0 ? wrapped + FullTurn : wrapped;
        }
    }
}

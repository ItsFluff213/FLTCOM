using System;
using FLTCOM.Core.Telemetry;
using FLTCOM.Core.Protocol;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class OrbitFrameTests
    {
        private const double KerbinMu = 3.5316e12;
        private const double Lko = 700_000;

        private static double HalfPeriod(double sma) => Math.PI * Math.Sqrt(sma * sma * sma / KerbinMu);

        [Fact]
        public void DegreesBecomeRadiansAndTheMeanAnomalyMovesToTheSnapshotTime()
        {
            double epoch = 1000;
            Orbit orbit = OrbitFrame.AtTime("Kerbin", KerbinMu, Lko, 0.01, 90, 270, -90, 0.5, epoch, epoch + HalfPeriod(Lko));

            Assert.Equal("Kerbin", orbit.ReferenceBody);
            Assert.Equal(Math.PI / 2, orbit.Inclination, 12);
            Assert.Equal(3 * Math.PI / 2, orbit.LongitudeOfAscendingNode, 12);
            Assert.Equal(3 * Math.PI / 2, orbit.ArgumentOfPeriapsis, 12);
            Assert.Equal(0.5 + Math.PI, orbit.MeanAnomaly, 9);
        }

        [Fact]
        public void EllipticMeanAnomalyWrapsIntoOneTurn()
        {
            double epoch = 0;
            Orbit orbit = OrbitFrame.AtTime("Kerbin", KerbinMu, Lko, 0, 0, 0, 0, 5, epoch, epoch + 3 * HalfPeriod(Lko));

            Assert.Equal(5 + 3 * Math.PI - 4 * Math.PI, orbit.MeanAnomaly, 9);
        }

        [Fact]
        public void HyperbolicMeanAnomalyIsNotWrapped()
        {
            double sma = -2_000_000;
            double meanMotion = Math.Sqrt(KerbinMu / Math.Pow(-sma, 3));
            Orbit orbit = OrbitFrame.AtTime("Kerbin", KerbinMu, sma, 1.5, 0, 0, 0, 1, 0, 20 * Math.PI / meanMotion);

            Assert.Equal(1 + 20 * Math.PI, orbit.MeanAnomaly, 9);
        }
    }
}

using System;
using System.Collections.Generic;
using FLTCOM.Core.Lambert;
using FLTCOM.Core.TwoBody;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class LambertTests
    {
        private const double EarthMu = 398_600.4418;

        public static IEnumerable<object[]> Cases()
        {
            // Earth units (km, s) and Sun-scale metres; short and long way, hyperbolic, and multi-revolution arcs.
            yield return new object[] { EarthMu, new Vector3d(15945.34, 0, 0), new Vector3d(12214.83399, 10249.46731, 0), 76 * 60.0, TransferGeometry.ShortWay, 0 };
            yield return new object[] { EarthMu, new Vector3d(5000, 10000, 2100), new Vector3d(-14600, 2500, 7000), 3600.0, TransferGeometry.ShortWay, 0 };
            yield return new object[] { EarthMu, new Vector3d(273378, 0, 0), new Vector3d(145820, 12758, 0), 13.5 * 3600, TransferGeometry.ShortWay, 0 };
            yield return new object[] { EarthMu, new Vector3d(15945.34, 0, 0), new Vector3d(12214.83399, 10249.46731, 0), 76 * 60.0, TransferGeometry.LongWay, 0 };
            yield return new object[] { EarthMu, new Vector3d(7000, 0, 0), new Vector3d(0, 7500, 1000), 600.0, TransferGeometry.ShortWay, 0 };
            yield return new object[] { EarthMu, new Vector3d(22592.145603, -1599.915239, -19783.950506), new Vector3d(1922.067697, 4054.157051, -8925.727465), 36_000.0, TransferGeometry.ShortWay, 1 };
            yield return new object[] { EarthMu, new Vector3d(22592.145603, -1599.915239, -19783.950506), new Vector3d(1922.067697, 4054.157051, -8925.727465), 36_000.0, TransferGeometry.ShortWay, -1 };
            yield return new object[] { StockSystem.SunMu, new Vector3d(13_599_840_256, 0, 0), new Vector3d(-14_000_000_000, 15_000_000_000, 300_000_000), 3.0e6 * 2.2, TransferGeometry.Prograde, 0 };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void TheSolutionArrivesAtTheTargetWithinOnePartIn1e8(double mu, Vector3d r1, Vector3d r2, double tof, TransferGeometry way, int nrev)
        {
            (Vector3d v1, Vector3d v2) = Izzo.Solve(mu, r1, r2, tof, way, nrev, new Vector3d(0, 0, 1));

            StateVector end = Kepler.Propagate(mu, new StateVector(r1, v1), tof);

            Assert.InRange((end.Position - r2).Magnitude / r2.Magnitude, 0, 1e-8);
            Assert.InRange((end.Velocity - v2).Magnitude / v2.Magnitude, 0, 1e-8);
        }

        [Fact]
        public void ValladoExample75MatchesThePrintedVelocities()
        {
            (Vector3d v1, Vector3d v2) = Izzo.Solve(EarthMu, new Vector3d(15945.34, 0, 0), new Vector3d(12214.83399, 10249.46731, 0), 76 * 60.0);

            Close(new Vector3d(2.058925, 2.915956, 0), v1, 1e-5);
            Close(new Vector3d(-3.451569, 0.910301, 0), v2, 1e-5);
        }

        [Fact]
        public void CurtisExample52MatchesThePrintedVelocities()
        {
            (Vector3d v1, Vector3d v2) = Izzo.Solve(EarthMu, new Vector3d(5000, 10000, 2100), new Vector3d(-14600, 2500, 7000), 3600.0);

            Close(new Vector3d(-5.9925, 1.9254, 3.2456), v1, 1e-4);
            Close(new Vector3d(-3.3125, -4.1966, -0.38529), v2, 1e-4);
        }

        [Fact]
        public void ProgradeFollowsTheAxisAndRetrogradeGoesTheOtherWay()
        {
            var r1 = new Vector3d(7000, 0, 0);
            var r2 = new Vector3d(-5000, -5000, 0);
            var up = new Vector3d(0, 0, 1);

            (Vector3d pro, _) = Izzo.Solve(EarthMu, r1, r2, 3000, TransferGeometry.Prograde, h: up);
            (Vector3d retro, _) = Izzo.Solve(EarthMu, r1, r2, 3000, TransferGeometry.Retrograde, h: up);

            Assert.True(Vector3d.Cross(r1, pro).Z > 0);
            Assert.True(Vector3d.Cross(r1, retro).Z < 0);
        }

        [Fact]
        public void CollinearPositionsAreRefused()
        {
            Assert.Throws<ArgumentException>(() => Izzo.Solve(EarthMu, new Vector3d(7000, 0, 0), new Vector3d(14000, 0, 0), 3000));
        }

        private static void Close(Vector3d expected, Vector3d actual, double relative) =>
            Assert.InRange((actual - expected).Magnitude / expected.Magnitude, 0, relative);
    }
}

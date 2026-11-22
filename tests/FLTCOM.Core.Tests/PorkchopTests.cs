using System;
using System.Linq;
using FLTCOM.Core.Planning;
using FLTCOM.Core.TwoBody;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class PorkchopTests
    {
        private const double Day = 21_600;
        private const double Parking = 700_000;
        private const double Capture = 420_000;

        /// <summary>
        /// The published first Kerbin to Duna window, Year 1 Day 236, as alexmoon's launch window planner gives it (quoted on the KSP forum:
        /// "the regular window opens up around year 1 day 236"; "Depart Year 1 Day 236"). Day 1 starts at UT 0.
        /// </summary>
        private const double PublishedDay = 236;

        private static readonly Vector3d Equator = new Vector3d(0, 0, 1);

        private static double UtOfDay(double day) => (day - 1) * Day;

        [Fact]
        public void TheSearchFindsTheDunaWindowWithinTwoDaysOfABruteForceOptimum()
        {
            SystemBody kerbin = StockSystem.System["Kerbin"];
            SystemBody duna = StockSystem.System["Duna"];
            double bruteUt = 0;
            double bruteCost = double.PositiveInfinity;
            for (double day = 200; day <= 260; day += 0.5)
            {
                for (double flight = 200; flight <= 340; flight += 1)
                {
                    double cost = Porkchop.Cost(kerbin, duna, UtOfDay(day), flight * Day, Parking, Capture, Equator);
                    if (cost < bruteCost)
                    {
                        bruteCost = cost;
                        bruteUt = UtOfDay(day);
                    }
                }
            }

            TransferWindow best = Porkchop.Search(StockSystem.System, "Kerbin", "Duna", 0, Parking, Capture).Best;

            Assert.InRange(best.DepartureUt - bruteUt, -2 * Day, 2 * Day);
            Assert.True(best.DeltaV <= bruteCost + 0.5);
        }

        [Fact]
        public void TheDunaWindowSitsInThePublishedWindowsValley()
        {
            SystemBody kerbin = StockSystem.System["Kerbin"];
            SystemBody duna = StockSystem.System["Duna"];
            TransferWindow best = Porkchop.Search(StockSystem.System, "Kerbin", "Duna", 0, Parking, Capture).Best;
            double atPublished = Enumerable.Range(200, 141).Min(flight => Porkchop.Cost(kerbin, duna, UtOfDay(PublishedDay), flight * Day, Parking, Capture, Equator));

            // The minimum is flat: departing on the published day costs at most 0.25 % more than the optimum found, a few days away.
            Assert.InRange(best.DepartureUt / Day + 1, PublishedDay - 6, PublishedDay + 2);
            Assert.InRange(atPublished, best.DeltaV, best.DeltaV * 1.0025);
        }

        [Fact]
        public void TheEphemerisPutsTheHohmannGeometryOnThePublishedDay()
        {
            // A half-orbit transfer to the point opposite Kerbin, at Duna's own radius there (Duna is eccentric), timed so that Duna reaches
            // that point with the transfer: no Lambert involved, only the two orbits, so this checks the body data and the epoch.
            SystemBody kerbin = StockSystem.System["Kerbin"];
            OrbitElements duna = StockSystem.System["Duna"].Orbit!;
            double periapsisLongitude = duna.LongitudeOfAscendingNode + duna.ArgumentOfPeriapsis;
            double Off(double t)
            {
                Vector3d k = kerbin.Orbit!.StateAt(t).Position;
                double opposite = Math.Atan2(k.Y, k.X) + Math.PI;
                double r2 = duna.SemiMajorAxis * (1 - duna.Eccentricity * duna.Eccentricity) / (1 + duna.Eccentricity * Math.Cos(opposite - periapsisLongitude));
                double flight = Math.PI * Math.Sqrt(Math.Pow((k.Magnitude + r2) / 2, 3) / StockSystem.SunMu);
                Vector3d d = duna.StateAt(t + flight).Position;
                return Angles.Wrap(Math.Atan2(d.Y, d.X) - opposite + Math.PI) - Math.PI;
            }
            double lo = UtOfDay(200);
            double hi = UtOfDay(270);
            Assert.True(Off(lo) > 0 && Off(hi) < 0);
            while (hi - lo > 1)
            {
                double mid = (lo + hi) / 2;
                if (Off(mid) > 0)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            Assert.InRange(lo / Day + 1, PublishedDay - 1, PublishedDay + 1);
        }

        [Fact]
        public void TheDunaWindowCostsWhatTheDeltaVMapsSay()
        {
            TransferWindow w = Porkchop.Search(StockSystem.System, "Kerbin", "Duna", 0, Parking, Capture).Best;

            // Community delta-v maps: about 1,060 m/s from low Kerbin orbit to a Duna intercept, and about 610 m/s more into low Duna orbit.
            Assert.InRange(Porkchop.Ejection(StockSystem.System["Kerbin"].Body, Parking, w.DepartureExcess, Equator), 1_000, 1_120);
            Assert.InRange(w.DeltaV, 1_550, 1_800);
            Assert.InRange(w.FlightTime / Day, 200, 330);
            Assert.InRange(w.PlaneChange, 0, 30);
        }

        [Fact]
        public void TheGridCoversTheWindowAndNoCellBeatsTheRefinedWindow()
        {
            PorkchopGrid grid = Porkchop.Search(StockSystem.System, "Kerbin", "Duna", 1_000_000, Parking, Capture);

            Assert.Equal(Porkchop.Columns * Porkchop.Rows, grid.DeltaV.Count);
            Assert.True(grid.Best.DepartureUt >= 1_000_000);
            Assert.True(grid.DeltaV.Where(v => !double.IsInfinity(v)).Min() >= grid.Best.DeltaV - 1e-6);
        }

        [Fact]
        public void AMoonIsNotAPlanetOfTheSameStar()
        {
            Assert.Throws<ArgumentException>(() => Porkchop.Search(StockSystem.System, "Kerbin", "Mun", 0, Parking, 250_000));
        }

        [Fact]
        public void AnEscapeOutOfTheParkingPlaneCostsMore()
        {
            CentralBody kerbin = StockSystem.System["Kerbin"].Body;
            double flat = Porkchop.Ejection(kerbin, Parking, new Vector3d(1_000, 0, 0), Equator);
            double tilted = Porkchop.Ejection(kerbin, Parking, new Vector3d(Math.Cos(0.3) * 1_000, 0, Math.Sin(0.3) * 1_000), Equator);
            double straightUp = Porkchop.Ejection(kerbin, Parking, new Vector3d(0, 0, 1_000), Equator);

            Assert.True(tilted > flat + 50);
            Assert.True(double.IsPositiveInfinity(straightUp));
        }
    }
}

using System;
using System.Diagnostics;
using System.Linq;
using FLTCOM.Core.Planning;
using FLTCOM.Core.TwoBody;
using Xunit;
using Xunit.Abstractions;

namespace FLTCOM.Core.Tests
{
    public class PlanetTransferTests
    {
        private const double Day = 21_600;
        private readonly ITestOutputHelper _out;

        public PlanetTransferTests(ITestOutputHelper output)
        {
            _out = output;
        }

        [Theory]
        [InlineData("Duna", 100_000, 0)]
        [InlineData("Eve", 150_000, 0)]
        [InlineData("Duna", 100_000, 300 * Day)]
        public void AFlightOnThePlannedBurnsEndsInTheTargetOrbit(string planet, double altitude, double now)
        {
            Patch parking = StockSystem.KerbinOrbit(100_000, now);
            var clock = Stopwatch.StartNew();
            TransferPlan plan = TransferPlanner.Plan(StockSystem.System, parking, planet, altitude, now + 60);
            clock.Stop();

            Assert.True(plan.Plan.Ok, plan.Plan.Error);
            _out.WriteLine("planned in " + clock.ElapsedMilliseconds + " ms");
            foreach (string note in plan.Notes)
            {
                _out.WriteLine("note: " + note);
            }
            foreach (ImpulsiveBurn b in plan.Plan.Burns)
            {
                _out.WriteLine(b.Label + " at " + KerbinTime.Format(b.NodeUt) + ": " + b.DeltaV.ToString("0.0") + " m/s");
            }
            Assert.NotNull(plan.Porkchop);
            Assert.StartsWith("leave for " + planet, plan.Plan.Burns[0].Label);
            Assert.Equal("Sun", plan.Plan.Burns[0].Body);
            Assert.InRange(plan.Plan.Burns[0].NodeUt - plan.Porkchop!.Best.DepartureUt, -Day, Day);

            // The ejection flown should cost within 5 % of the porkchop's optimum ejection.
            double optimum = Porkchop.Ejection(StockSystem.System["Kerbin"].Body, 700_000, plan.Porkchop.Best.DepartureExcess, new Vector3d(0, 0, 1));
            Assert.InRange(plan.Plan.Burns[0].DeltaV, optimum * 0.95, optimum * 1.05);
            Assert.True(plan.Plan.TotalDeltaV <= plan.Porkchop.Best.DeltaV * 1.15, "total " + plan.Plan.TotalDeltaV + " against the porkchop's " + plan.Porkchop.Best.DeltaV);

            Patch p = parking;
            foreach (ImpulsiveBurn b in plan.Plan.Burns)
            {
                p = TransferPlanner.AfterBurn(StockSystem.System, p, b);
            }
            Patch flown = PatchedConics.Coast(StockSystem.System, p, p.Ut + 3_600);
            double mu = StockSystem.System[planet].Body.GravParameter;
            double radius = StockSystem.System[planet].Body.Radius;
            Assert.Equal(planet, flown.Body);
            Assert.InRange(Kepler.PeriapsisRadius(mu, flown.State) - radius, altitude * 0.9 - 2_000, altitude * 1.1 + 2_000);
            Assert.InRange(Kepler.EccentricityVector(mu, flown.State).Magnitude, 0, 0.02);
        }

        [Fact]
        public void TheEjectionAloneReachesDunasSphereBeforeAnyCorrection()
        {
            Patch parking = StockSystem.KerbinOrbit(100_000, 0);
            TransferPlan plan = TransferPlanner.Plan(StockSystem.System, parking, "Duna", 100_000, 60);

            Assert.True(plan.Plan.Ok, plan.Plan.Error);
            Patch gone = TransferPlanner.AfterBurn(StockSystem.System, parking, plan.Plan.Burns[0]);
            Assert.NotNull(PatchedConics.Find(StockSystem.System, gone, "Duna", plan.Porkchop!.Best.ArrivalUt + 100 * Day));
        }

        [Fact]
        public void APolarDunaOrbitIsAimedOnTheWay()
        {
            Patch parking = StockSystem.KerbinOrbit(100_000, 0);
            double want = Angles.ToRadians(90);
            TransferPlan plan = TransferPlanner.Plan(StockSystem.System, parking, "Duna", 100_000, 60, want);

            Assert.True(plan.Plan.Ok, plan.Plan.Error);
            Patch p = parking;
            foreach (ImpulsiveBurn b in plan.Plan.Burns.TakeWhile(b => !b.Label.StartsWith("capture", StringComparison.Ordinal)))
            {
                p = TransferPlanner.AfterBurn(StockSystem.System, p, b);
            }
            Encounter? pass = PatchedConics.Find(StockSystem.System, p, "Duna", plan.Porkchop!.Best.ArrivalUt + 100 * Day);
            Assert.NotNull(pass);
            Assert.InRange(Angles.ToDegrees(pass!.Value.Inclination), 88, 92);
        }

        [Fact]
        public void ReplanningAroundTheSunKeepsAPassAndRefusesToStartOverWithoutOne()
        {
            Patch parking = StockSystem.KerbinOrbit(100_000, 0);
            TransferPlan plan = TransferPlanner.Plan(StockSystem.System, parking, "Duna", 100_000, 60);
            Assert.True(plan.Plan.Ok, plan.Plan.Error);
            Patch gone = TransferPlanner.AfterBurn(StockSystem.System, parking, plan.Plan.Burns[0]);
            Patch around = gone;
            while (around.Body != "Sun")
            {
                around = PatchedConics.Step(StockSystem.System, around, gone.Ut + 100 * Day, out bool changed);
                Assert.True(changed);
            }

            TransferPlan rest = TransferPlanner.Plan(StockSystem.System, around, "Duna", 100_000, around.Ut + 60);
            Assert.True(rest.Plan.Ok, rest.Plan.Error);
            Assert.StartsWith("capture at Duna", rest.Plan.Burns.First(b => b.Label.StartsWith("capture", StringComparison.Ordinal)).Label);
            Assert.DoesNotContain(rest.Plan.Burns, b => b.Label.StartsWith("leave", StringComparison.Ordinal));

            StateVector s = around.State;
            var astray = new Patch("Sun", new StateVector(s.Position, s.Velocity + s.Normal * 300), around.Ut);
            TransferPlan lost = TransferPlanner.Plan(StockSystem.System, astray, "Duna", 100_000, around.Ut + 60);
            Assert.False(lost.Plan.Ok);
            Assert.Contains("no pass", lost.Plan.Error);
        }

        [Fact]
        public void AMoonOfAnotherPlanetIsRefused()
        {
            TransferPlan plan = TransferPlanner.Plan(StockSystem.System, StockSystem.KerbinOrbit(100_000, 0), "Sun", 100_000, 60);

            Assert.False(plan.Plan.Ok);
        }
    }
}

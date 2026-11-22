using System.Linq;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.TwoBody;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class FlightSafetyTests
    {
        private const double Now = 5_184_000;

        private static readonly CentralBody Mun = StockSystem.System["Mun"].Body;

        private static Patch MunOrbit(double periapsis, double apoapsis)
        {
            OrbitElements orbit = OrbitElements.FromApsides(Mun.GravParameter, Mun.Radius + periapsis, Mun.Radius + apoapsis, 0, Now);
            return new Patch("Mun", orbit.StateAt(Now), Now);
        }

        private static PlannedBurn Planned(ImpulsiveBurn b) =>
            new PlannedBurn("ship-1", b.Label, b.NodeUt, b.NodeUt - 30, 60, b.Prograde, b.Normal, b.Radial, null, b.OrbitAfter, b.Body);

        private static VesselTask Lander(double periapsis, double apoapsis, double trueAnomaly)
        {
            OrbitElements orbit = OrbitElements.FromApsides(Mun.GravParameter, Mun.Radius + periapsis, Mun.Radius + apoapsis, 0, Now);
            StateVector state = orbit.StateAtTrueAnomaly(trueAnomaly);
            var stage = new StageDeltaV(0, 10, 5, 60, 1_500, 200);
            return new VesselTask("ship-1", "Moon Lander 1", Mun, OrbitElements.FromState(Mun.GravParameter, state, Now), new[] { stage }, system: StockSystem.System, trajectory: new Patch("Mun", state, Now));
        }

        [Fact]
        public void ABriefingWhoseFinalOrbitIsUnderTheFloorCannotBeApproved()
        {
            Briefing briefing = BriefingBuilder.Build("Low Pass", OrbitChange.Periapsis(3_000), new[] { Lander(50_000, 50_000, 0) }, Now);

            Assert.False(briefing.CanApprove);
            Assert.Contains(briefing.Contingencies, c => c.Severity == Severity.Red && c.Text.Contains("under Mun's safe floor of 7.1 km"));
        }

        [Fact]
        public void RaisingThePeriapsisAtApoapsisClearsAnImpactCourseOnTheWayUp()
        {
            Briefing briefing = BriefingBuilder.Build("Recovery", OrbitChange.Circularize(), new[] { Lander(-56_678, 200_000, 2.5) }, Now);

            Assert.True(briefing.CanApprove, string.Join("; ", briefing.Contingencies.Select(c => c.Text)));
        }

        [Fact]
        public void ABriefingIsRedWhenTheVesselHitsBeforeItsFirstBurn()
        {
            Briefing briefing = BriefingBuilder.Build("Too Late", OrbitChange.Circularize(), new[] { Lander(-56_678, 200_000, -2.5) }, Now);

            Assert.False(briefing.CanApprove);
            Assert.Contains(briefing.Contingencies, c => c.Severity == Severity.Red && c.Text.StartsWith("on impact course with Mun", System.StringComparison.Ordinal));
        }

        [Fact]
        public void APeriapsisUnderTheSurfaceIsAnImpactCourse()
        {
            ImpactCourse? impact = FlightSafety.Impact(StockSystem.System, MunOrbit(-56_678, 200_000));

            Assert.NotNull(impact);
            Assert.Equal("Mun", impact!.Body);
            Assert.Equal("on impact course with Mun, periapsis -56.7 km", impact.Describe());
            Assert.Equal(Mun.SafeFloor, impact.Floor);
            Assert.InRange(impact.Ut, Now, Now + 3 * 3_600);
        }

        [Fact]
        public void AnOrbitClearOfTheTerrainIsSafe()
        {
            Assert.Null(FlightSafety.Impact(StockSystem.System, MunOrbit(50_000, 200_000)));
        }

        /// <summary>
        /// Moon Lander 1: the launch's trim burns flew after the ejection. The capture, planned before
        /// them, is stale: flown as planned it ends under the Mun. Re-planned from the real path it captures safely.
        /// </summary>
        [Fact]
        public void AStaleCaptureThatWouldImpactIsReplannedFromTheRealPath()
        {
            Patch parking = StockSystem.KerbinOrbit(100_000, Now);
            TransferPlan plan = TransferPlanner.Plan(StockSystem.System, parking, "Mun", 50_000, Now + 60);
            Assert.True(plan.Plan.Ok, plan.Plan.Error);
            var burns = plan.Plan.Burns;
            ImpulsiveBurn capture = burns.Last();
            Patch p = TransferPlanner.AfterBurn(StockSystem.System, parking, burns[0]);
            p = TransferPlanner.AfterBurn(StockSystem.System, p, new ImpulsiveBurn("trim", p.Ut + 600, -5, 0, 0));
            foreach (ImpulsiveBurn b in burns.Skip(1).Take(burns.Count - 2))
            {
                p = TransferPlanner.AfterBurn(StockSystem.System, p, b);
            }
            Assert.NotNull(FlightSafety.Impact(StockSystem.System, TransferPlanner.AfterBurn(StockSystem.System, p, capture)));

            string? stale = FlightSafety.Stale(StockSystem.System, p, Planned(capture), double.PositiveInfinity);

            Assert.NotNull(stale);
            Assert.Contains("impact course with Mun", stale);

            TransferPlan replan = TransferPlanner.Plan(StockSystem.System, p, "Mun", 50_000, p.Ut + 60);
            Assert.True(replan.Plan.Ok, replan.Plan.Error);
            Assert.Null(FlightSafety.Stale(StockSystem.System, p, Planned(replan.Plan.Burns[0]), double.PositiveInfinity));
            Patch flown = replan.Plan.Burns.Aggregate(p, (at, b) => TransferPlanner.AfterBurn(StockSystem.System, at, b));
            Assert.Equal("Mun", flown.Body);
            Assert.Null(FlightSafety.Impact(StockSystem.System, flown));
        }

        [Fact]
        public void ABurnThatStillMatchesItsPlannedOrbitIsNotStale()
        {
            Patch parking = StockSystem.KerbinOrbit(100_000, Now);
            TransferPlan plan = TransferPlanner.Plan(StockSystem.System, parking, "Mun", 50_000, Now + 60);
            Patch p = plan.Plan.Burns.Take(plan.Plan.Burns.Count - 1).Aggregate(parking, (at, b) => TransferPlanner.AfterBurn(StockSystem.System, at, b));

            Assert.Null(FlightSafety.Stale(StockSystem.System, p, Planned(plan.Plan.Burns.Last()), double.PositiveInfinity));
        }
    }
}

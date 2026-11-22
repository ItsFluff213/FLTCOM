using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Execution;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.TwoBody;
using Xunit;
using Xunit.Abstractions;

namespace FLTCOM.Core.Tests
{
    public class MoonTransferTests
    {
        private const double Now = 5_184_000;
        private readonly ITestOutputHelper _out;

        public MoonTransferTests(ITestOutputHelper output)
        {
            _out = output;
        }

        // Reference: the closed-form Hohmann lead angle, pi - n2 * pi * sqrt(((r1 + r2) / 2)^3 / mu), from a 100 km
        // circular Kerbin orbit with the stock moon orbits (Mun 12 000 km, Minmus 47 000 km): 110.7 and 114.9 degrees.
        [Theory]
        [InlineData(12_000_000, 110.7)]
        [InlineData(47_000_000, 114.9)]
        public void TheLeadAngleMatchesTheHohmannValue(double moonRadius, double degrees)
        {
            double lead = TransferPlanner.PhaseAngle(StockSystem.KerbinMu, 700_000, moonRadius);
            Assert.InRange(Angles.ToDegrees(lead), degrees - 0.5, degrees + 0.5);
        }

        [Theory]
        [InlineData("Mun", 50_000)]
        [InlineData("Minmus", 30_000)]
        public void AFlightOnThePlannedBurnsEndsInTheTargetOrbit(string moon, double altitude)
        {
            Patch parking = StockSystem.KerbinOrbit(100_000, Now);
            TransferPlan plan = TransferPlanner.Plan(StockSystem.System, parking, moon, altitude, Now + 60);

            Assert.True(plan.Plan.Ok, plan.Plan.Error);
            foreach (string note in plan.Notes)
            {
                _out.WriteLine("note: " + note);
            }
            foreach (ImpulsiveBurn b in plan.Plan.Burns)
            {
                _out.WriteLine(b.Label + " at " + b.NodeUt.ToString("0") + ": " + b.DeltaV.ToString("0.0") + " m/s");
            }
            Assert.StartsWith("leave for " + moon, plan.Plan.Burns[0].Label);
            Assert.StartsWith("capture at " + moon, plan.Plan.Burns.Last(b => b.Label.StartsWith("capture", StringComparison.Ordinal)).Label);
            Assert.InRange(plan.Plan.Burns[0].DeltaV, 800, 960);
            Assert.InRange(plan.Plan.TotalDeltaV, 900, 1_400);

            Patch flown = Fly(parking, plan);
            double mu = StockSystem.System[moon].Body.GravParameter;
            double radius = StockSystem.System[moon].Body.Radius;
            Assert.Equal(moon, flown.Body);
            Assert.InRange(Kepler.PeriapsisRadius(mu, flown.State) - radius, altitude * 0.9 - 2_000, altitude * 1.1 + 2_000);
            Assert.InRange(Kepler.EccentricityVector(mu, flown.State).Magnitude, 0, 0.02);
        }

        [Fact]
        public void AnInclinedTransferArrivesInANearPolarMunOrbit()
        {
            Patch parking = StockSystem.KerbinOrbit(100_000, Now);
            double want = Angles.ToRadians(80);
            TransferPlan plan = TransferPlanner.Plan(StockSystem.System, parking, "Mun", 50_000, Now + 60, want);

            Assert.True(plan.Plan.Ok, plan.Plan.Error);
            foreach (string note in plan.Notes)
            {
                _out.WriteLine("note: " + note);
            }
            Assert.Empty(plan.Notes);
            Assert.Contains(plan.Plan.Burns, b => b.Label == TransferPlanner.CorrectionLabel);
            foreach (ImpulsiveBurn b in plan.Plan.Burns)
            {
                _out.WriteLine(b.Label + ": " + b.DeltaV.ToString("0.0") + " m/s");
            }
            Patch flown = Fly(parking, plan);
            Assert.Equal("Mun", flown.Body);
            Vector3d h = Vector3d.Cross(flown.State.Position, flown.State.Velocity);
            Assert.InRange(Angles.ToDegrees(Math.Acos(h.Z / h.Magnitude)), 78, 82);
            Assert.InRange(plan.Plan.TotalDeltaV, 900, 1_500);
        }

        [Fact]
        public void TheDepartureLeadsTheMunByAboutTheHohmannAngle()
        {
            Patch parking = StockSystem.KerbinOrbit(100_000, Now);
            TransferPlan plan = TransferPlanner.Plan(StockSystem.System, parking, "Mun", 50_000, Now + 60);
            double ut = plan.Plan.Burns[0].NodeUt;
            Vector3d vessel = PatchedConics.Coast(StockSystem.System, parking, ut).State.Position;
            Vector3d mun = StockSystem.System.StateOf("Mun", ut).Position;
            double lead = Angles.ToDegrees(Vector3d.AngleAbout(vessel, mun, new Vector3d(0, 0, 1)));
            _out.WriteLine("lead " + lead.ToString("0.00"));
            Assert.InRange(lead, 105, 130);
            Assert.True(ut >= Now + 60);
        }

        [Fact]
        public void PlanningAgainAfterTheEjectionLeavesOnlyTheCapture()
        {
            Patch parking = StockSystem.KerbinOrbit(100_000, Now);
            TransferPlan plan = TransferPlanner.Plan(StockSystem.System, parking, "Mun", 50_000, Now + 60);
            ImpulsiveBurn ejection = plan.Plan.Burns[0];
            Patch coasting = TransferPlanner.AfterBurn(StockSystem.System, parking, ejection);

            TransferPlan again = TransferPlanner.Plan(StockSystem.System, coasting, "Mun", 50_000, coasting.Ut + 60);

            Assert.True(again.Plan.Ok, again.Plan.Error);
            Assert.DoesNotContain(again.Plan.Burns, b => b.Label.StartsWith("leave", StringComparison.Ordinal));
            Assert.StartsWith("capture at Mun", again.Plan.Burns[0].Label);
        }

        [Fact]
        public void AnOverburnIsCaughtByASmallCourseCorrection()
        {
            Patch parking = StockSystem.KerbinOrbit(100_000, Now);
            TransferPlan plan = TransferPlanner.Plan(StockSystem.System, parking, "Mun", 50_000, Now + 60);
            ImpulsiveBurn ejection = plan.Plan.Burns[0];
            var sloppy = new ImpulsiveBurn(ejection.Label, ejection.NodeUt, ejection.Prograde + 4, 1.5, 0);
            Patch coasting = TransferPlanner.AfterBurn(StockSystem.System, parking, sloppy);

            TransferPlan again = TransferPlanner.Plan(StockSystem.System, coasting, "Mun", 50_000, coasting.Ut + 60);

            Assert.True(again.Plan.Ok, again.Plan.Error);
            ImpulsiveBurn correction = again.Plan.Burns[0];
            _out.WriteLine(correction.Label + " " + correction.DeltaV.ToString("0.00"));
            Assert.Equal("course correction", correction.Label);
            Assert.InRange(correction.DeltaV, 0.1, 15);
            Patch flown = Fly(coasting, again);
            Assert.Equal("Mun", flown.Body);
            Assert.InRange(Kepler.PeriapsisRadius(6.5138398e10, flown.State) - 200_000, 43_000, 57_000);
        }

        [Fact]
        public void ABriefingTimesTheTransferOnTheVesselsStages()
        {
            Patch parking = StockSystem.KerbinOrbit(100_000, Now);
            var task = new VesselTask("7", "Probe", StockSystem.System["Kerbin"].Body, OrbitElements.FromState(StockSystem.KerbinMu, parking.State, Now),
                new[] { new StageDeltaV(0, 4, 2, 60, 2_200, 120) }, system: StockSystem.System, trajectory: parking);

            Briefing briefing = BriefingBuilder.Build("Grey Heron", OrbitChange.TransferTo("Mun", 50_000), new[] { task }, Now);

            Assert.True(briefing.CanApprove, string.Join("; ", briefing.Contingencies.Select(c => c.Text)));
            Assert.Equal("transfer to Mun, orbit at 50 km", briefing.Change);
            Assert.Equal("Mun", briefing.Burns.Last().Body);
            Assert.Equal("Kerbin", briefing.Burns[0].Body);
            Assert.NotNull(briefing.Burns.Last().OrbitAfter);
        }

        [Fact]
        public void AnAltitudeInsideTheAtmosphereOrTerrainIsRefused()
        {
            TransferPlan plan = TransferPlanner.Plan(StockSystem.System, StockSystem.KerbinOrbit(100_000, Now), "Mun", 5_000, Now + 60);
            Assert.False(plan.Plan.Ok);
        }

        [Fact]
        public void ATransferOperationSurvivesTheMissionJson()
        {
            var burns = new[]
            {
                new PlannedBurn("v1", "leave for Mun", 100, 90, 20, 840, 0, 0, null, new OrbitElements(StockSystem.KerbinMu, 6_400_000, 0.89, 0, 0, 0, 0, 100), "Kerbin"),
                new PlannedBurn("v1", "capture at Mun", 20_000, 19_990, 20, -280, 0, 0, null, new OrbitElements(6.5138398e10, 250_000, 0, 0, 0, 0, 0, 20_000), "Mun"),
            };
            var op = new Operation("op-1", "Grey Heron", OrbitChange.TransferTo("Mun", 50_000), new[] { "v1" }, burns, StepState.Briefed, null);
            var mission = new Mission("m-1", "Mun Shot", new[] { op });

            Mission back = MissionJson.Read(MissionJson.Write(mission));

            Assert.Equal(MissionJson.Write(mission), MissionJson.Write(back));
            Assert.Equal("Mun", back.Operations[0].Change.Destination);
            Assert.Equal("Mun", back.Operations[0].Burns[1].Body);
        }

        [Fact]
        public void RevisingKeepsTheStateAndReplacesTheRest()
        {
            var runner = new MissionRunner();
            var burns = new[] { new PlannedBurn("v1", "leave for Mun", 100, 90, 20, 840, 0, 0), new PlannedBurn("v1", "capture at Mun", 20_000, 19_990, 20, -280, 0, 0) };
            runner.AddBriefed("op-1", "Grey Heron", OrbitChange.TransferTo("Mun", 50_000), new Briefing("Grey Heron", "transfer", burns, 1_120, 2_000, 0.7, new List<BriefedStage>(), new List<Contingency>()));
            StepState before = runner.Find("op-1")!.State;

            runner.Revise("op-1", new[] { new PlannedBurn("v1", "capture at Mun", 20_004, 19_994, 20, -279, 0, 0, null, null, "Mun") }, "re-timed");

            Operation after = runner.Find("op-1")!;
            Assert.Equal(before, after.State);
            Assert.Single(after.Burns);
            Assert.Equal(20_004, after.Burns[0].NodeUt);
        }

        private static Patch Fly(Patch from, TransferPlan plan)
        {
            Patch p = from;
            foreach (ImpulsiveBurn b in plan.Plan.Burns)
            {
                p = TransferPlanner.AfterBurn(StockSystem.System, p, b);
            }
            return PatchedConics.Coast(StockSystem.System, p, p.Ut + 3_600);
        }
    }
}

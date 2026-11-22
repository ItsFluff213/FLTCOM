using System;
using System.Linq;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Propulsion;
using FLTCOM.PlanCli;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class BriefingTests
    {
        [Fact]
        public void CircularizeBriefingIsReadyWithTimedBurns()
        {
            Briefing briefing = BriefingBuilder.Build("Amber Falcon", OrbitChange.Circularize(), new[] { Fixtures.Load("lko-elliptic") }, Fixtures.Epoch);

            PlannedBurn burn = Assert.Single(briefing.Burns);
            Assert.True(briefing.CanApprove);
            Assert.Empty(briefing.Contingencies);
            Assert.True(burn.StartUt < burn.NodeUt && burn.StartUt > Fixtures.Epoch);
            Assert.True(burn.NodeUt - burn.StartUt > burn.Duration / 2);
            Assert.Contains("OPERATION AMBER FALCON", briefing.Format(Fixtures.Epoch));
            Assert.Contains("READY FOR APPROVAL", briefing.Format(Fixtures.Epoch));
        }

        [Fact]
        public void EveryBurnShowsItsOrbitButOnlyTheLastIsTheRunnersCheck()
        {
            Briefing briefing = BriefingBuilder.Build("Amber Falcon", OrbitChange.CircularAt(250_000), new[] { Fixtures.Load("lko-elliptic") }, Fixtures.Epoch);

            Assert.Equal(2, briefing.Burns.Count);
            Assert.All(briefing.Burns, burn => Assert.NotNull(burn.Expected));
            Assert.Null(briefing.Burns[0].OrbitAfter);
            Assert.Same(briefing.Burns[1].OrbitAfter, briefing.Burns[1].Expected);
            Assert.InRange(briefing.Burns[0].Expected!.ApoapsisRadius, 600_000 + 245_000, 600_000 + 255_000);
        }

        [Fact]
        public void ARaiseBeyondTheDeltaVBudgetIsRedWithNoApprovePath()
        {
            Briefing briefing = BriefingBuilder.Build("Iron Tern", OrbitChange.CircularAt(2_000_000), new[] { Fixtures.Load("lko-short") }, Fixtures.Epoch);

            Assert.False(briefing.CanApprove);
            Assert.Contains(briefing.Contingencies, c => c.Severity == Severity.Red && c.Text.Contains("short of delta-v"));
            Assert.Contains(briefing.Contingencies, c => c.Severity == Severity.Red && c.Text.Contains("margin"));
            Assert.Contains("CANNOT APPROVE", briefing.Format(Fixtures.Epoch));
        }

        [Fact]
        public void AMarginUnderTenPercentIsRed()
        {
            VesselTask task = Fixtures.Load("lko-elliptic");
            double need = OrbitPlanner.Plan(OrbitChange.Circularize(), task.Orbit, task.Body, Fixtures.Epoch + BriefingBuilder.Lead).TotalDeltaV;
            var stage = new StageDeltaV(0, 1, 0.98, 20, need * 1.05, 10);
            var tight = new VesselTask(task.VesselId, task.VesselName, task.Body, task.Orbit, new[] { stage });

            Briefing briefing = BriefingBuilder.Build("Polar Heron", OrbitChange.Circularize(), new[] { tight }, Fixtures.Epoch);

            Assert.False(briefing.CanApprove);
            Contingency red = Assert.Single(briefing.Contingencies);
            Assert.Contains("under 10 %", red.Text);
        }

        [Fact]
        public void APinnedStageThatCannotCoverItsBurnTurnsRed()
        {
            VesselTask task = Fixtures.Load("lko-short");
            var pinned = new VesselTask(task.VesselId, task.VesselName, task.Body, task.Orbit, task.Stages, new int?[] { 1, 1 });

            Briefing briefing = BriefingBuilder.Build("Swift Tern", OrbitChange.CircularAt(400_000), new[] { pinned }, Fixtures.Epoch);

            Assert.False(briefing.CanApprove);
            Assert.Contains(briefing.Stages, s => s.Stage == 1 && s.Short);
            Assert.Contains(briefing.Contingencies, c => c.Text.Contains("stage 1 cannot cover"));
        }

        [Fact]
        public void AnOperationCanSpanSeveralVessels()
        {
            VesselTask a = Fixtures.Load("lko-elliptic");
            var b = new VesselTask("twin", "Twin", a.Body, a.Orbit, a.Stages);

            Briefing briefing = BriefingBuilder.Build("Silver Comet", OrbitChange.Circularize(), new[] { a, b }, Fixtures.Epoch);

            Assert.Equal(2, briefing.Burns.Count);
            Assert.Equal(new[] { "lko-elliptic", "twin" }, briefing.Burns.Select(x => x.VesselId).OrderBy(x => x));
            Assert.True(briefing.CanApprove);
            Assert.Equal(4, briefing.Stages.Count);
        }

        [Fact]
        public void TwoBurnsOfOneVesselAtOnceAreListedAsAConflict()
        {
            VesselTask task = Fixtures.Load("lko-elliptic");
            var slow = new StageDeltaV(0, 10, 2, 1, 5000, 50_000);
            var crawler = new VesselTask(task.VesselId, task.VesselName, task.Body, task.Orbit, new[] { slow });

            Briefing briefing = BriefingBuilder.Build("Quiet Raven", OrbitChange.CircularAt(250_000), new[] { crawler }, Fixtures.Epoch);

            Assert.Contains(briefing.Contingencies, c => c.Severity == Severity.Amber && c.Text.Contains("two burns at once"));
            Assert.Contains(briefing.Contingencies, c => c.Severity == Severity.Amber && c.Text.Contains("long burn"));
        }

        [Fact]
        public void EveryMissionStateSurvivesTheJsonRoundTrip()
        {
            var burns = new[]
            {
                new PlannedBurn("v1", "apoapsis to 250 km", 100.5, 98.25, 4.5, 68.2, 0.001, -0.002, 1),
                new PlannedBurn("v2", "circularize", 2000, 1997, 6, 116.6, 0, 0),
            };
            var operations = Enum.GetValues(typeof(StepState)).Cast<StepState>()
                .Select((s, i) => new Operation("op-" + i, Codename.Generate((ulong)i), OrbitChange.CircularAt(250_000), new[] { "v1", "v2" }, burns, s, s == StepState.Executing ? new BurnProgress(1, 33.25) : null))
                .ToList();
            var mission = new Mission("m-1", "Relay Raise", operations);

            Mission back = MissionJson.Read(MissionJson.Write(mission));

            Assert.Equal(MissionJson.Write(mission), MissionJson.Write(back));
            Assert.Equal(operations.Select(o => o.State), back.Operations.Select(o => o.State));
            Operation executing = back.Operations.Single(o => o.State == StepState.Executing);
            Assert.Equal(33.25, executing.Progress!.DeltaVDone);
            Assert.Equal(1, back.Operations[0].Burns[0].PinnedStage);
            Assert.Null(back.Operations[0].Burns[1].PinnedStage);
            Assert.Equal(OrbitChangeKind.CircularOrbit, back.Operations[0].Change.Kind);
        }

        [Fact]
        public void ABriefingForAFourOperationMissionTakesUnder200Ms()
        {
            VesselTask task = Fixtures.Load("lko-elliptic");
            OrbitChange[] changes = { OrbitChange.Circularize(), OrbitChange.CircularAt(250_000), OrbitChange.Inclination(0.2), OrbitChange.StandardLow() };
            BriefingBuilder.Build("warm", changes[0], new[] { task }, Fixtures.Epoch);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (OrbitChange change in changes)
            {
                BriefingBuilder.Build("bench", change, new[] { task }, Fixtures.Epoch);
            }
            watch.Stop();

            Assert.True(watch.Elapsed.TotalMilliseconds < 200, watch.Elapsed.TotalMilliseconds + " ms");
        }
    }
}

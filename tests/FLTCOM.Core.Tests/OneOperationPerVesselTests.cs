using System.Collections.Generic;
using FLTCOM.Core.Execution;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Scheduling;
using Xunit;

namespace FLTCOM.Core.Tests
{
    /// <summary>
    /// The Moon Lander 1 incident: the launch (Bright Warden) added a trim after its last finishing burn and still owned the
    /// vessel when the transfer (Lunar Heron) was approved, so the two flew interleaved burns on one ship and the capture
    /// burn, planned before the trim, dropped the periapsis under the Mun.
    /// </summary>
    public class OneOperationPerVesselTests
    {
        private const string Ship = "ship-1";

        private static readonly ActiveView Flying = new ActiveView(GameScene.Flight, Ship, ActiveVesselCondition.StableOrbit, false);

        private static Briefing Briefing(string label, double startUt) =>
            new Briefing("Op", label, new[] { new PlannedBurn(Ship, label, startUt + 10, startUt, 20, 50, 0, 0) }, 50, 500, 450, new List<BriefedStage>(), new List<Contingency>());

        private static MissionRunner LaunchWaitingForItsTrim()
        {
            var runner = new MissionRunner();
            runner.AddBriefed("op-1", "Bright Warden", OrbitChange.Circularize(), Briefing("circularize", 1000));
            Assert.Null(runner.Approve("op-1", "Bright Warden", uplink: true));
            Assert.Equal(RunnerActionKind.Fly, runner.Next(1000.5, Flying).Kind);
            runner.AddCorrection("op-1", new[] { new PlannedBurn(Ship, "trim", 3010, 3000, 5, 2, 0, 0) }, "trim burn of 2.0 m/s added");
            Assert.Equal(StepState.Waiting, runner.Find("op-1")!.State);
            return runner;
        }

        [Fact]
        public void ASecondOperationOnAVesselStillFlyingAnotherIsRefused()
        {
            MissionRunner runner = LaunchWaitingForItsTrim();
            runner.AddBriefed("op-2", "Lunar Heron", OrbitChange.Circularize(), Briefing("leave for Mun", 2000));

            string? error = runner.Approve("op-2", "Lunar Heron", uplink: true);

            Assert.NotNull(error);
            Assert.Contains("Bright Warden", error);
            Assert.Equal(StepState.Briefed, runner.Find("op-2")!.State);
            Assert.Equal("op-1", runner.Next(3000.5, Flying).Operation!.Id);
        }

        [Fact]
        public void AnOperationOnHoldIsSupersededByTheNextOneApproved()
        {
            MissionRunner runner = LaunchWaitingForItsTrim();
            runner.Hold("op-1", "game loaded: holding until resumed");
            runner.AddBriefed("op-2", "Lunar Heron", OrbitChange.Circularize(), Briefing("leave for Mun", 4000));

            Assert.Null(runner.Approve("op-2", "Lunar Heron", uplink: true));

            Assert.Equal(StepState.Aborted, runner.Find("op-1")!.State);
            Assert.Equal("superseded by Lunar Heron", runner.NoteOf("op-1"));
            Assert.Equal(StepState.Waiting, runner.Find("op-2")!.State);
        }

        [Fact]
        public void AnImpactCourseHoldsTheOperationUntilTheOperatorResumesIt()
        {
            MissionRunner runner = LaunchWaitingForItsTrim();
            FlightAlarm impact = new ImpactCourse("Mun", -56_678, 7_061, 2_500).Alarm();

            runner.Alarm("op-1", impact);

            Assert.Equal(StepState.Hold, runner.Find("op-1")!.State);
            Assert.Same(impact, runner.AlarmOf("op-1"));
            Assert.Equal("IMPACT: on impact course with Mun, periapsis -56.7 km", runner.NoteOf("op-1"));
            Assert.Equal(RunnerActionKind.Idle, runner.Next(3000.5, Flying).Kind);

            Assert.Null(runner.Resume("op-1"));
            Assert.Null(runner.AlarmOf("op-1"));
        }

        [Fact]
        public void AnImpactRightAfterABurnHoldsBeforeTheNextBurn()
        {
            var runner = new MissionRunner();
            var burns = new[] { new PlannedBurn(Ship, "capture", 1010, 1000, 20, -50, 0, 0), new PlannedBurn(Ship, "circularize", 3010, 3000, 5, -5, 0, 0) };
            runner.AddBriefed("op-1", "Lunar Heron", OrbitChange.Circularize(), new Briefing("Op", "capture", burns, 55, 500, 445, new List<BriefedStage>(), new List<Contingency>()));
            runner.Approve("op-1", "Lunar Heron", uplink: true);
            Assert.Equal(RunnerActionKind.Fly, runner.Next(1000.5, Flying).Kind);

            runner.Alarm("op-1", new ImpactCourse("Mun", -56_678, 7_061, 2_500).Alarm());

            Operation held = runner.Find("op-1")!;
            Assert.Equal(StepState.Hold, held.State);
            Assert.Equal(1, MissionRunner.BurnIndex(held));
        }
    }
}

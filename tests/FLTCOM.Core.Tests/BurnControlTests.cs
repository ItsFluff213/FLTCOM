using System.Collections.Generic;
using FLTCOM.Core.AutoSystems;
using FLTCOM.Core.Execution;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Scheduling;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class BurnControlTests
    {
        private const string Ship = "ship-1";
        private const double Frame = 0.02;

        private static ActiveView Flying(double turnLead = IgnitionTiming.TurnFloor) =>
            new ActiveView(GameScene.Flight, Ship, ActiveVesselCondition.StableOrbit, false, turnLead);

        private static string? Feed(ReleaseRule rule, double from, double seconds, double input, bool abort = false, double spin = 0)
        {
            string? why = null;
            for (double t = from; t < from + seconds && why == null; t += Frame)
            {
                why = rule.Why(new ControlSample(t, input, abort, spin));
            }
            return why;
        }

        [Fact]
        public void AKeyTapNeverLetsGo()
        {
            var rule = new ReleaseRule();

            Assert.Null(Feed(rule, 0, 0.3, 1));
            Assert.Null(Feed(rule, 0.3, 0.5, 0));
            Assert.Null(Feed(rule, 0.8, 0.3, -1));
            Assert.Null(Feed(rule, 1.1, 5, 0.04));
        }

        [Fact]
        public void TapsInARowDoNotAddUpToAHold()
        {
            var rule = new ReleaseRule();

            for (int tap = 0; tap < 10; tap++)
            {
                Assert.Null(Feed(rule, tap, 0.6, 1));
                Assert.Null(Feed(rule, tap + 0.6, 0.4, 0));
            }
        }

        [Fact]
        public void InputHeldForASecondLetsGo()
        {
            var rule = new ReleaseRule();

            Assert.Null(Feed(rule, 0, 0.9, 0.5));
            Assert.Contains("pilot input held", Feed(rule, 0.9, 0.3, 0.5));
        }

        [Fact]
        public void AbortLetsGoAtOnce()
        {
            Assert.Equal("abort", new ReleaseRule().Why(new ControlSample(0, 0, abort: true, spin: 0)));
        }

        [Fact]
        public void OneFrameOfSpinDoesNotLetGo()
        {
            var rule = new ReleaseRule();
            Assert.Null(Feed(rule, 0, 1, 0, spin: 5));
            Assert.Null(Feed(rule, 1, Frame, 0, spin: 400));
            Assert.Null(Feed(rule, 1 + Frame, 3, 0, spin: 30));
        }

        [Fact]
        public void ASustainedTumbleLetsGoAfterHalfASecond()
        {
            var rule = new ReleaseRule();
            Assert.Null(Feed(rule, 0, ReleaseRule.SpinHoldSeconds - 2 * Frame, 0, spin: 75));
            Assert.Contains("spinning at 75 deg/s", Feed(rule, ReleaseRule.SpinHoldSeconds - 2 * Frame, 4 * Frame, 0, spin: 75));
        }

        [Fact]
        public void ANimbleCraftTurnsInTheFloorTimeAndASlowOneStartsEarlier()
        {
            Assert.Equal(IgnitionTiming.TurnFloor, IgnitionTiming.TurnLead(2.0));
            Assert.Equal(IgnitionTiming.TurnFloor, IgnitionTiming.TurnLead(null));

            double slow = IgnitionTiming.TurnLead(0.002);
            Assert.InRange(slow, IgnitionTiming.TurnFloor + 1, IgnitionTiming.TurnCap);
            Assert.True(IgnitionTiming.TurnLead(0.001) > slow);
            Assert.Equal(IgnitionTiming.TurnCap, IgnitionTiming.TurnLead(0));
        }

        [Fact]
        public void ASlowTurningShipWarpsLessAndStartsTurningBeforeItsTurnTime()
        {
            MissionRunner runner = Uploaded(startUt: 5000, duration: 60);
            ActiveView slow = Flying(turnLead: 150);

            Assert.Equal(RunnerActionKind.Coast, runner.Next(1000, slow).Kind);
            runner.Answer(Assert.Single(runner.Prompts).Id, accept: true);
            RunnerAction warp = runner.Next(1001, slow);

            Assert.Equal(RunnerActionKind.Warp, warp.Kind);
            Assert.Equal(5000 - 150, warp.UntilUt);
            Assert.Equal(RunnerActionKind.Align, runner.Next(5000 - 140, slow).Kind);
            Assert.Equal(RunnerActionKind.Coast, runner.Next(5000 - 140, Flying()).Kind);
        }

        [Fact]
        public void ACaptureBurnStillTurning20SecondsLateIsReplannedNotFlownStale()
        {
            MissionRunner runner = Uploaded(startUt: 1000, duration: 60);
            Assert.Equal(RunnerActionKind.Fly, runner.Next(1000.1, Flying()).Kind);

            Assert.False(runner.Late("op-1", 1005));
            Assert.Equal(StepState.Executing, runner.Find("op-1")!.State);

            Assert.True(runner.Late("op-1", 1020));
            Assert.Equal(StepState.Missed, runner.Find("op-1")!.State);
            Assert.Equal(RunnerActionKind.Idle, runner.Next(1020.1, Flying()).Kind);

            var aimed = new PlannedBurn(Ship, "capture", 1120, 1090, 60, -310, 0, 0);
            runner.Replanned("op-1", new[] { aimed }, "pointing took 20 s; re-planned");

            Operation replanned = runner.Find("op-1")!;
            Assert.Equal(StepState.Waiting, replanned.State);
            Assert.Equal(1090, replanned.Burns[0].StartUt);
            Assert.Equal(RunnerActionKind.Align, runner.Next(1030, Flying()).Kind);
        }

        [Fact]
        public void ALongBurnToleratesALateStartInProportion()
        {
            Assert.Equal(IgnitionTiming.LateFloor, IgnitionTiming.LateTolerance(20));
            Assert.Equal(30, IgnitionTiming.LateTolerance(300));

            MissionRunner runner = Uploaded(startUt: 1000, duration: 300);
            runner.Next(1000.1, Flying());

            Assert.False(runner.Late("op-1", 1025));
            Assert.True(runner.Late("op-1", 1031));
        }

        [Fact]
        public void ALaunchsFinishingBurnIsFlownLateRatherThanLeftFallingBack()
        {
            var runner = new MissionRunner();
            runner.AddBriefed("op-1", "Quiet Heron", OrbitChange.Launch(new LaunchTarget(80_000, 50_000)), Briefing(Ship, 1000));
            Assert.Null(runner.Approve("op-1", "Quiet Heron", uplink: true));
            runner.Next(1000.1, Flying());

            Assert.False(runner.Late("op-1", 1030));
            Assert.Equal(StepState.Executing, runner.Find("op-1")!.State);
        }

        [Fact]
        public void NoDashboardCommandButItsOwnScrubTouchesARunningBurn()
        {
            MissionRunner runner = Uploaded(startUt: 1000, duration: 60);
            runner.AddBriefed("op-2", "Far Kestrel", OrbitChange.Circularize(), Briefing("ship-2", 3000));
            runner.AddBriefed("op-3", "Near Kestrel", OrbitChange.Circularize(), Briefing("ship-3", 4000));
            Assert.Equal(RunnerActionKind.Fly, runner.Next(1000.1, Flying()).Kind);

            Assert.Null(runner.Approve("op-2", "Far Kestrel", uplink: true));
            Assert.Null(runner.Scrub("op-3"));
            Assert.Null(runner.Scrub("op-3"));
            Assert.NotNull(runner.Resume("op-1"));
            Assert.NotNull(runner.Answer(99, accept: true));
            Assert.NotNull(runner.Approve("op-1", "Quiet Heron", uplink: true));
            runner.AddBriefed("op-4", "Late Kestrel", OrbitChange.Circularize(), Briefing(Ship, 9000));

            Assert.Equal(StepState.Executing, runner.Find("op-1")!.State);
            Assert.Equal(RunnerActionKind.Fly, runner.Next(1001, Flying()).Kind);

            Assert.Null(runner.Scrub("op-1"));
            Assert.Equal(StepState.Aborted, runner.Find("op-1")!.State);
        }

        [Fact]
        public void AScrubSentTwiceIsNotAnError()
        {
            MissionRunner runner = Uploaded(startUt: 1000, duration: 60);

            Assert.Null(runner.Scrub("op-1"));
            int revision = runner.Revision;

            Assert.Null(runner.Scrub("op-1"));
            Assert.Equal(StepState.Scrubbed, runner.Find("op-1")!.State);
            Assert.Equal(revision, runner.Revision);
        }

        [Fact]
        public void AFairingIsJettisonedOnceAndItsBaseNeverCountsAgain()
        {
            Assert.True(FairingRule.Closed(deployable: true, sections: 3));
            Assert.False(FairingRule.Closed(deployable: false, sections: 0));
            Assert.False(FairingRule.Closed(deployable: true, sections: 0));
            Assert.False(FairingRule.Closed(deployable: false, sections: 3));
        }

        private static Briefing Briefing(string vesselId, double startUt, double duration = 20) =>
            new Briefing("Op", "circularize", new[] { new PlannedBurn(vesselId, "circularize", startUt + duration / 2, startUt, duration, 50, 0, 0) }, 50, 500, 450, new List<BriefedStage>(), new List<Contingency>());

        private static MissionRunner Uploaded(double startUt, double duration)
        {
            var runner = new MissionRunner();
            runner.AddBriefed("op-1", "Quiet Heron", OrbitChange.Circularize(), Briefing(Ship, startUt, duration));
            Assert.Null(runner.Approve("op-1", "Quiet Heron", uplink: true));
            return runner;
        }
    }
}

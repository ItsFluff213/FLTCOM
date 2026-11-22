using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Execution;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Scheduling;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class RunnerTests
    {
        private const string Ship = "ship-1";

        private static readonly ActiveView Flying = new ActiveView(GameScene.Flight, Ship, ActiveVesselCondition.StableOrbit, false);

        private static MissionRunner Briefed(double startUt = 1000)
        {
            var runner = new MissionRunner();
            var burn = new PlannedBurn(Ship, "circularize", startUt + 10, startUt, 20, 50, 0, 0);
            var briefing = new Briefing("Op", "circularize", new[] { burn }, 50, 500, 450, new List<BriefedStage>(), new List<Contingency>());
            runner.AddBriefed("op-1", "Quiet Heron", OrbitChange.Circularize(), briefing);
            return runner;
        }

        [Fact]
        public void UploadWithoutALinkIsRefusedAndNamesTheLink()
        {
            MissionRunner runner = Briefed();

            string? error = runner.Approve("op-1", "Quiet Heron", uplink: false);

            Assert.Contains("link", error);
            Assert.Equal(StepState.Briefed, runner.Find("op-1")!.State);
        }

        [Fact]
        public void UploadedBurnFliesWithoutALink()
        {
            MissionRunner runner = Briefed(startUt: 1000);
            Assert.Null(runner.Approve("op-1", "Quiet Heron", uplink: true));

            RunnerAction action = runner.Next(1000.5, Flying);

            Assert.Equal(RunnerActionKind.Fly, action.Kind);
            Assert.Equal(StepState.Executing, runner.Find("op-1")!.State);
        }

        [Fact]
        public void EveryRunningOperationHoldsAfterLoadAndNothingFires()
        {
            MissionRunner runner = Briefed(startUt: 1000);
            runner.Approve("op-1", "Quiet Heron", uplink: true);
            runner.Next(1000.5, Flying);
            var saved = runner.Operations.ToList();

            var loaded = new MissionRunner();
            loaded.Load(saved);

            Assert.Equal(StepState.Hold, loaded.Find("op-1")!.State);
            Assert.Equal(RunnerActionKind.Idle, loaded.Next(1001, Flying).Kind);
        }

        [Fact]
        public void ResumeAfterReloadWaitsForTheBurnAgain()
        {
            MissionRunner runner = Briefed(startUt: 1000);
            runner.Approve("op-1", "Quiet Heron", uplink: true);
            var loaded = new MissionRunner();
            loaded.Load(runner.Operations.ToList());

            Assert.Null(loaded.Resume("op-1"));

            Assert.Equal(StepState.Waiting, loaded.Find("op-1")!.State);
            Assert.Equal(RunnerActionKind.Align, loaded.Next(990, Flying).Kind);
        }

        [Fact]
        public void WarpOnlyAfterAYes()
        {
            MissionRunner runner = Briefed(startUt: 5000);
            runner.Approve("op-1", "Quiet Heron", uplink: true);

            Assert.Equal(RunnerActionKind.Coast, runner.Next(1000, Flying).Kind);
            Prompt prompt = Assert.Single(runner.Prompts);
            Assert.Equal(PromptKind.Warp, prompt.Kind);
            Assert.Equal(RunnerActionKind.Coast, runner.Next(1001, Flying).Kind);

            runner.Answer(prompt.Id, accept: true);
            RunnerAction warp = runner.Next(1002, Flying);

            Assert.Equal(RunnerActionKind.Warp, warp.Kind);
            Assert.Equal(5000 - MissionRunner.WarpLead, warp.UntilUt);
        }

        [Fact]
        public void MissedWindowAsksForAReplan()
        {
            MissionRunner runner = Briefed(startUt: 1000);
            runner.Approve("op-1", "Quiet Heron", uplink: true);

            RunnerAction action = runner.Next(1000 + MissionRunner.MissGrace + 1, Flying);

            Assert.Equal(RunnerActionKind.Replan, action.Kind);
            Assert.Equal(StepState.Missed, runner.Find("op-1")!.State);
        }

        private static readonly ActiveView AtBase = new ActiveView(GameScene.SpaceCenter, null, ActiveVesselCondition.Unstable, false);

        [Fact]
        public void AFarBurnIsWaitedForAtBaseAfterAskingAndOnlyThenSwitches()
        {
            const double start = 100 * KerbinTime.Day;
            MissionRunner runner = Briefed(startUt: start);
            runner.Approve("op-1", "Quiet Heron", uplink: true);

            Assert.Equal(RunnerActionKind.Idle, runner.Next(0, AtBase).Kind);
            Prompt prompt = Assert.Single(runner.Prompts);
            Assert.Equal(PromptKind.Warp, prompt.Kind);
            Assert.Equal(start - MissionRunner.SwitchLead, prompt.UntilUt);
            Assert.Contains("then switch", prompt.Text);
            Assert.Equal(RunnerActionKind.Idle, runner.Next(1, AtBase).Kind);

            runner.Answer(prompt.Id, accept: true);
            RunnerAction warp = runner.Next(2, AtBase);
            Assert.Equal(RunnerActionKind.Warp, warp.Kind);
            Assert.Equal(start - MissionRunner.SwitchLead, warp.UntilUt);
            Assert.Equal(RunnerActionKind.Idle, runner.Next(50 * KerbinTime.Day, AtBase).Kind);
            Assert.Empty(runner.Prompts);

            Assert.Equal(RunnerActionKind.Switch, runner.Next(start - MissionRunner.SwitchLead, AtBase).Kind);

            // In flight the warp to ignition is asked again, not taken from the answer at base.
            Assert.Equal(RunnerActionKind.Coast, runner.Next(start - MissionRunner.SwitchLead + 1, Flying).Kind);
            Assert.Equal(PromptKind.Warp, Assert.Single(runner.Prompts).Kind);
        }

        [Fact]
        public void ANoToTheWaitAtBaseSwitchesNowAndIsNotAskedAgain()
        {
            MissionRunner runner = Briefed(startUt: 100 * KerbinTime.Day);
            runner.Approve("op-1", "Quiet Heron", uplink: true);
            runner.Next(0, AtBase);

            runner.Answer(Assert.Single(runner.Prompts).Id, accept: false);

            Assert.Equal(RunnerActionKind.Switch, runner.Next(1, AtBase).Kind);
            Assert.Equal(RunnerActionKind.Switch, runner.Next(2, AtBase).Kind);
            Assert.Empty(runner.Prompts);
        }

        [Fact]
        public void ABurnWithinADaySwitchesFromBaseAtOnce()
        {
            MissionRunner runner = Briefed(startUt: 10_000);
            runner.Approve("op-1", "Quiet Heron", uplink: true);

            Assert.Equal(RunnerActionKind.Switch, runner.Next(0, AtBase).Kind);
            Assert.Empty(runner.Prompts);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Execution;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Scheduling;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class GroupRunnerTests
    {
        private static ActiveView Flying(string vessel) => new ActiveView(GameScene.Flight, vessel, ActiveVesselCondition.StableOrbit, false);

        private static readonly ActiveView Elsewhere = Flying("station");

        private static MissionRunner Approved()
        {
            var runner = new MissionRunner();
            var burns = new[]
            {
                new PlannedBurn("a", "phase a", 10_010, 10_000, 20, 10, 0, 0),
                new PlannedBurn("b", "phase b", 20_010, 20_000, 20, 10, 0, 0),
                new PlannedBurn("a", "circularize a", 30_010, 30_000, 20, -10, 0, 0),
                new PlannedBurn("b", "circularize b", 40_010, 40_000, 20, -10, 0, 0),
            };
            runner.AddBriefed("op-1", "Even Ring", OrbitChange.Spread(700_000), new Briefing("Op", "spread", burns, 40, 500, 0.9, new List<BriefedStage>(), new List<Contingency>()));
            Assert.Null(runner.Approve("op-1", "Even Ring", uplink: true));
            return runner;
        }

        private static void FlyBackground(MissionRunner runner, double ut)
        {
            RunnerAction ignite = runner.Next(ut, Elsewhere);
            Assert.Equal(RunnerActionKind.Background, ignite.Kind);
            Assert.Equal(StepState.Executing, runner.Find("op-1")!.State);
            runner.BurnDone("op-1", "done");
        }

        [Fact]
        public void TheGroupAsksOnceToWarpThroughEveryBurn()
        {
            MissionRunner runner = Approved();

            Assert.Equal(RunnerActionKind.Background, runner.Next(1000, Elsewhere).Kind);
            Prompt prompt = Assert.Single(runner.Prompts);
            Assert.Equal(PromptKind.Warp, prompt.Kind);
            Assert.StartsWith("2 ships, 4 burns over", prompt.Text);
            runner.Answer(prompt.Id, accept: true);

            Assert.Equal(RunnerActionKind.Warp, runner.Next(1001, Elsewhere).Kind);
            FlyBackground(runner, 10_000);
            RunnerAction second = runner.Next(10_100, Elsewhere);

            Assert.Equal(RunnerActionKind.Warp, second.Kind);
            Assert.Equal(20_000 - MissionRunner.BackgroundWarpLead, second.UntilUt);
            Assert.Empty(runner.Prompts);
        }

        [Fact]
        public void AnInterruptStopsTheWarpAndAsksAgain()
        {
            MissionRunner runner = Approved();
            runner.Next(1000, Elsewhere);
            runner.Answer(runner.Prompts[0].Id, accept: true);
            runner.Next(1001, Elsewhere);

            runner.Interrupt("op-1", "lost signal to b");

            Assert.False(runner.WarpsThrough("op-1"));
            Assert.Equal("lost signal to b", runner.NoteOf("op-1"));
            Assert.Equal(RunnerActionKind.Background, runner.Next(1002, Elsewhere).Kind);
            Assert.Equal(PromptKind.Warp, Assert.Single(runner.Prompts).Kind);
        }

        [Fact]
        public void ADueBurnOfAnotherVesselBurnsInTheBackgroundWithoutSwitching()
        {
            MissionRunner runner = Approved();

            RunnerAction action = runner.Next(10_000.5, Elsewhere);

            Assert.Equal(RunnerActionKind.Background, action.Kind);
            Assert.Equal("a", action.Burn!.VesselId);
            Assert.Equal(StepState.Executing, runner.Find("op-1")!.State);
        }

        [Fact]
        public void ThePlayersOwnVesselWaitsForTheirClick()
        {
            MissionRunner runner = Approved();

            Assert.Equal(RunnerActionKind.Await, runner.Next(9_000, Flying("a")).Kind);
            Assert.DoesNotContain(runner.Prompts, p => p.Kind == PromptKind.Fly);
            Assert.Equal(RunnerActionKind.Await, runner.Next(10_000 - MissionRunner.FlyAsk + 1, Flying("a")).Kind);
            Prompt fly = Assert.Single(runner.Prompts, p => p.Kind == PromptKind.Fly);
            Assert.Equal(RunnerActionKind.Await, runner.Next(10_001, Flying("a")).Kind);
            Assert.Equal(StepState.Waiting, runner.Find("op-1")!.State);

            runner.Answer(fly.Id, accept: true);

            Assert.Equal(RunnerActionKind.Fly, runner.Next(10_002, Flying("a")).Kind);
            Assert.Equal(StepState.Executing, runner.Find("op-1")!.State);
        }

        [Fact]
        public void WithoutAClickTheBurnIsMissedAndMovesWholeOrbitsLater()
        {
            MissionRunner runner = Approved();

            RunnerAction missed = runner.Next(10_000 + MissionRunner.MissGrace + 1, Flying("a"));
            Assert.Equal(RunnerActionKind.Replan, missed.Kind);
            Assert.Equal(StepState.Missed, runner.Find("op-1")!.State);

            runner.Delay("op-1", "a", 25_000, "re-planned one orbit later");

            Operation op = runner.Find("op-1")!;
            Assert.Equal(StepState.Waiting, op.State);
            Assert.Equal(new[] { "phase b", "phase a", "circularize b", "circularize a" }, op.Burns.Select(b => b.Label));
            Assert.Equal(35_000, op.Burns.Single(b => b.Label == "phase a").StartUt);
        }

        [Fact]
        public void AMissedBurnFlownByHandCountsAsFlown()
        {
            MissionRunner runner = Approved();
            runner.Next(10_000 + MissionRunner.MissGrace + 1, Flying("a"));

            runner.FlownByHand("op-1", "flown by hand");

            Operation op = runner.Find("op-1")!;
            Assert.Equal(StepState.Waiting, op.State);
            Assert.Equal(1, MissionRunner.BurnIndex(op));
        }

        [Fact]
        public void SwitchingToAVesselMidBurnLetsGoOfIt()
        {
            MissionRunner runner = Approved();
            runner.Next(10_000.5, Elsewhere);

            Assert.Equal(RunnerActionKind.Idle, runner.Next(10_001, Flying("a")).Kind);

            Assert.Equal(StepState.Hold, runner.Find("op-1")!.State);
            Assert.Contains("let go", runner.NoteOf("op-1"));
        }

        [Fact]
        public void TheLastBackgroundBurnCompletesTheOperation()
        {
            MissionRunner runner = Approved();
            foreach (double ut in new[] { 10_000.5, 20_000.5, 30_000.5, 40_000.5 })
            {
                FlyBackground(runner, ut);
            }

            Assert.Equal(StepState.Done, runner.Find("op-1")!.State);
        }
    }
}

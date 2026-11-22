using System;
using System.Collections.Generic;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Scheduling;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class ScheduleTests
    {
        private static ScheduledBurn Burn(string op, string vessel, double start, double duration) =>
            new ScheduledBurn(op, 0, new PlannedBurn(vessel, op + " burn", start + duration / 2, start, duration, 10, 0, 0));

        private static readonly ActiveView InStableOrbit = new ActiveView(GameScene.Flight, "a", ActiveVesselCondition.StableOrbit, false);

        [Fact]
        public void TwoOverlappingBurnsOfOneVesselConflict()
        {
            ScheduleTick tick = Schedule.Tick(new[] { Burn("op-2", "a", 1050, 100), Burn("op-1", "a", 1000, 100) }, 0, InStableOrbit);

            BurnConflict conflict = Assert.Single(tick.Conflicts);
            Assert.Equal("op-1", conflict.First.OperationId);
            Assert.Equal("op-2", conflict.Second.OperationId);
            Assert.Equal(new[] { "op-1", "op-2" }, new[] { tick.Ordered[0].OperationId, tick.Ordered[1].OperationId });
        }

        [Fact]
        public void ManyVesselsBurningAtOnceIsNotAConflict()
        {
            var burns = new List<ScheduledBurn>();
            for (int i = 0; i < 20; i++)
            {
                burns.Add(Burn("op-" + i, "vessel-" + i, 1000, 100));
            }

            Assert.Empty(Schedule.Tick(burns, 0, InStableOrbit).Conflicts);
        }

        [Fact]
        public void BackToBackBurnsOfOneVesselDoNotConflict()
        {
            Assert.Empty(Schedule.Tick(new[] { Burn("op-1", "a", 1000, 100), Burn("op-2", "a", 1100, 50) }, 0, InStableOrbit).Conflicts);
        }

        [Theory]
        [InlineData(GameScene.TrackingStation, "a", ActiveVesselCondition.Unstable, false, SwitchDecision.Switch)]
        [InlineData(GameScene.SpaceCenter, null, ActiveVesselCondition.Unstable, false, SwitchDecision.Switch)]
        [InlineData(GameScene.Flight, "a", ActiveVesselCondition.StableOrbit, false, SwitchDecision.Switch)]
        [InlineData(GameScene.Flight, "a", ActiveVesselCondition.Landed, false, SwitchDecision.Switch)]
        [InlineData(GameScene.Flight, "a", ActiveVesselCondition.Unstable, false, SwitchDecision.Ask)]
        [InlineData(GameScene.Flight, "a", ActiveVesselCondition.StableOrbit, true, SwitchDecision.Wait)]
        [InlineData(GameScene.Flight, "b", ActiveVesselCondition.Unstable, false, SwitchDecision.Stay)]
        [InlineData(GameScene.Other, "a", ActiveVesselCondition.StableOrbit, false, SwitchDecision.Ask)]
        public void SwitchPolicyDecidesForEachViewState(GameScene scene, string? active, ActiveVesselCondition condition, bool burning, SwitchDecision expected)
        {
            var view = new ActiveView(scene, active, condition, burning);

            ScheduleTick tick = Schedule.Tick(new[] { Burn("op-1", "b", 1000, 60) }, 950, view);

            Assert.Equal(expected, tick.Decision);
        }

        [Fact]
        public void NoSwitchUntilTheNextBurnIsNear()
        {
            ScheduleTick tick = Schedule.Tick(new[] { Burn("op-1", "b", 10_000, 60) }, 0, InStableOrbit);

            Assert.Equal(SwitchDecision.Stay, tick.Decision);
            Assert.Equal("op-1", tick.Next!.OperationId);
        }

        [Fact]
        public void CollectTakesUploadedBurnsAndSkipsFlownOnes()
        {
            var burns = new[]
            {
                new PlannedBurn("a", "one", 10, 9, 2, 1, 0, 0),
                new PlannedBurn("a", "two", 20, 19, 2, 1, 0, 0),
            };
            var executing = new Operation("op-1", "Amber Falcon", OrbitChange.Circularize(), new[] { "a" }, burns, StepState.Executing, new BurnProgress(1, 0));
            var draft = new Operation("op-2", "Iron Tern", OrbitChange.Circularize(), new[] { "a" }, burns);

            ScheduledBurn only = Assert.Single(Schedule.Collect(new[] { executing, draft }));
            Assert.Equal("two", only.Burn.Label);
        }

        [Fact]
        public void ATickOver200VesselsTakesUnderAMillisecond()
        {
            var burns = new List<ScheduledBurn>();
            var random = new Random(7);
            for (int i = 0; i < 200; i++)
            {
                burns.Add(Burn("op-" + i, "vessel-" + i, random.Next(0, 100_000), 60));
            }
            for (int i = 0; i < 100; i++)
            {
                Schedule.Tick(burns, 500, InStableOrbit);
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            const int runs = 1000;
            for (int i = 0; i < runs; i++)
            {
                Schedule.Tick(burns, 500, InStableOrbit);
            }
            watch.Stop();

            Assert.True(watch.Elapsed.TotalMilliseconds / runs < 1, watch.Elapsed.TotalMilliseconds / runs + " ms per tick");
        }
    }
}

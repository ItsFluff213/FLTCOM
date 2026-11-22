using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Propulsion;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class FlowSimulatorTests
    {
        private const int AllVessel = 1;
        private const int StagePriority = 3;
        private static readonly FlowResource[] None = new FlowResource[0];

        private static FlowResource[] Fuel(double units) =>
            new[] { new FlowResource("LiquidFuel", units, units, 0.005), new FlowResource("Oxidizer", units * 1.222, units * 1.222, 0.005) };

        private static FlowEngine Engine(double thrust, double isp) =>
            new FlowEngine(thrust, 0, isp, 9.80665, false, new[] { new FlowPropellant("LiquidFuel", 0.9, false, StagePriority), new FlowPropellant("Oxidizer", 1.1, false, StagePriority) });

        [Fact]
        public void OneEngineOnOneTankMatchesTheRocketEquation()
        {
            var pod = new FlowPart(1, "pod", -1, 1.0, None) { IsRoot = true };
            var tank = new FlowPart(2, "tank", -1, 0.5, Fuel(200));
            var engine = new FlowPart(3, "engine", 0, 1.0, None) { Engine = Engine(200, 340) };
            pod.Links.Add(2); tank.Links.AddRange(new uint[] { 1, 3 }); engine.Links.Add(2);
            tank.CrossFeed.AddRange(new uint[] { 1, 2, 3 }); engine.CrossFeed.AddRange(new uint[] { 1, 2, 3 }); pod.CrossFeed.AddRange(new uint[] { 1, 2, 3 });

            IReadOnlyList<StageDeltaV> stages = FlowSimulator.Estimate(new FlowVessel(1, new[] { pod, tank, engine }));

            double fuelMass = 200 * 0.005 + 200 * 1.222 * 0.005;
            double start = 1.0 + 0.5 + 1.0 + fuelMass;
            double expected = 340 * 9.80665 * Math.Log(start / (start - fuelMass));
            StageDeltaV stage = Assert.Single(stages);
            Assert.Equal(0, stage.Stage);
            Assert.Equal(expected, stage.DeltaV, 0);
        }

        [Fact]
        public void ASpentStageIsDroppedBeforeTheNextOneBurns()
        {
            var pod = new FlowPart(1, "pod", -1, 1.0, None) { IsRoot = true };
            var upperTank = new FlowPart(2, "upper", -1, 0.5, Fuel(100));
            var upperEngine = new FlowPart(3, "upper-engine", 0, 0.5, None) { Engine = Engine(60, 345) };
            var decoupler = new FlowPart(4, "decoupler", 0, 0.2, None) { IsDecoupler = true, DecouplesId = 5 };
            var lowerTank = new FlowPart(5, "lower", 1, 1.0, Fuel(400));
            var lowerEngine = new FlowPart(6, "lower-engine", 1, 1.5, None) { Engine = Engine(200, 320) };
            Chain(pod, upperTank, upperEngine, decoupler, lowerTank, lowerEngine);
            foreach (FlowPart p in new[] { pod, upperTank, upperEngine, decoupler }) { p.CrossFeed.RemoveAll(id => id >= 5); }
            foreach (FlowPart p in new[] { lowerTank, lowerEngine }) { p.CrossFeed.RemoveAll(id => id <= 4); }

            IReadOnlyList<StageDeltaV> stages = FlowSimulator.Estimate(new FlowVessel(2, new[] { pod, upperTank, upperEngine, decoupler, lowerTank, lowerEngine }));
            Assert.Equal(new[] { 1, 0 }, stages.Select(s => s.Stage).ToArray());
            Assert.All(stages, s => Assert.True(s.DeltaV > 500));
            Assert.True(stages[0].StartMass > stages[1].StartMass);
        }

        private static void Chain(params FlowPart[] parts)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) { parts[i].Links.Add(parts[i - 1].Id); }
                if (i < parts.Length - 1) { parts[i].Links.Add(parts[i + 1].Id); }
                parts[i].CrossFeed.AddRange(parts.Select(p => p.Id));
            }
        }

        [Fact]
        public void CrossfeedLetsAnEngineDrainATankItIsNotAttachedTo()
        {
            var core = new FlowPart(1, "core", -1, 2.0, Fuel(50)) { IsRoot = true };
            var engine = new FlowPart(2, "engine", 0, 1.0, None) { Engine = Engine(200, 340) };
            var booster = new FlowPart(3, "booster-tank", 0, 0.5, Fuel(300));
            core.Links.AddRange(new uint[] { 2, 3 }); engine.Links.Add(1); booster.Links.Add(1);
            engine.CrossFeed.AddRange(new uint[] { 1, 2 });
            var isolated = FlowSimulator.Estimate(new FlowVessel(1, new[] { core, engine, booster })).Sum(s => s.DeltaV);

            engine.CrossFeed.Add(3);
            var fed = FlowSimulator.Estimate(new FlowVessel(1, new[] { core, engine, booster })).Sum(s => s.DeltaV);

            Assert.True(fed > isolated * 1.5, $"{fed} vs {isolated}");
        }

        [Fact]
        public void AVesselWithoutEnginesHasNoStages()
        {
            var pod = new FlowPart(1, "pod", -1, 1.0, Fuel(10)) { IsRoot = true };
            Assert.Empty(FlowSimulator.Estimate(new FlowVessel(0, new[] { pod })));
        }
    }
}

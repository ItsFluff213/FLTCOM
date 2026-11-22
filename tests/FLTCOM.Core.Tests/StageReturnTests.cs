using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Constellation;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.TwoBody;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class StageReturnTests
    {
        private const string Name = "Relay Carrier";
        private static readonly CentralBody Kerbin = new CentralBody("Kerbin", 3.5316e12, 600_000, 70_000, 6_767, 84_159_286, 21_549.425);
        private static readonly CentralBody Mun = new CentralBody("Mun", 6.5138398e10, 200_000, 0, 7_061, 2_429_559.1, 138_984.38);

        /// <summary>
        /// The transfer stage (probe core 1, tank 2, engine 3) carries three satellites on separators 10, 20, 30; below its
        /// engine a decoupler 100 holds the launch stage: probe core 101, tank 102, engine 103, parachute 104.
        /// </summary>
        private static List<CarrierPart> Stack(bool launchCore = true, bool launchChutes = true, double launchTank = 4)
        {
            var parts = new List<CarrierPart>
            {
                new CarrierPart(1, null, "probe core", CarrierPartKind.Command, 0.1, 0.1),
                new CarrierPart(2, 1, "tank", CarrierPartKind.None, 8, 1),
                new CarrierPart(3, 2, "engine", CarrierPartKind.Engine, 0.5, 0.5, 60, 345),
                new CarrierPart(100, 3, "decoupler", CarrierPartKind.Separator, 0.05, 0.05),
                new CarrierPart(102, launchCore ? 101u : 100u, "launch tank", CarrierPartKind.None, launchTank, 0.5),
                new CarrierPart(103, 102, "launch engine", CarrierPartKind.Engine, 1.5, 1.5, 200, 310),
            };
            if (launchCore)
            {
                parts.Add(new CarrierPart(101, 100, "launch core", CarrierPartKind.Command, 0.1, 0.1));
            }
            if (launchChutes)
            {
                parts.Add(new CarrierPart(104, 102, "launch parachute", CarrierPartKind.Parachute, 0.1, 0.1));
            }
            for (uint k = 1; k <= 3; k++)
            {
                uint id = 10 * k;
                parts.Add(new CarrierPart(id, 2, "separator", CarrierPartKind.Separator, 0.05, 0.05));
                parts.Add(new CarrierPart(id + 1, id, "sat core", CarrierPartKind.Command, 0.1, 0.1));
                parts.Add(new CarrierPart(id + 2, id + 1, "sat tank", CarrierPartKind.None, 0.225, 0.025));
                parts.Add(new CarrierPart(id + 3, id + 2, "sat engine", CarrierPartKind.Engine, 0.02, 0.02, 2, 315));
            }
            return parts;
        }

        private static OrbitElements Circular(CentralBody body, double altitude) =>
            new OrbitElements(body.GravParameter, body.Radius + altitude, 0, 0, 0, 0, 0, 0);

        private static Briefing Brief(List<CarrierPart> parts, EndOfLife end = EndOfLife.Auto, CentralBody? body = null)
        {
            CentralBody at = body ?? Kerbin;
            double altitude = at == Kerbin ? ConstellationPlanner.SynchronousAltitude(at)!.Value : 500_000;
            var task = new VesselTask("100", Name, at, Circular(at, at == Kerbin ? 100_000 : 30_000), Array.Empty<StageDeltaV>(), carrier: parts);
            return BriefingBuilder.Build("Ring", OrbitChange.Deploy(altitude, null, end), new[] { task }, 0);
        }

        [Fact]
        public void FindsTheLaunchStageBelowTheDecoupler()
        {
            LaunchStage stage = LaunchStage.Find(Stack())!;

            Assert.Equal(101u, stage.CoreId);
            Assert.Equal(100u, stage.SeparatorId);
            Assert.Equal(new uint[] { 101, 102, 103, 104 }, stage.Parts.Select(p => p.Id).OrderBy(id => id));
            Assert.Contains(stage.Upper, p => p.Id == 100);
            Assert.Equal(3, PayloadDetector.Scan(stage.Upper).Payloads.Count);
        }

        [Theory]
        [InlineData(0, 0, true)]
        [InlineData(1, 1, true)]
        [InlineData(1, 0, false)]
        [InlineData(2, 1, false)]
        [InlineData(null, 3, false)]
        public void AProbeCoreOrAPodWithItsPilotAboardCanFlyAnEmptyPodCannot(int? minimumCrew, int crew, bool flies)
        {
            Assert.Equal(flies, CommandPart.Controls(minimumCrew, crew));
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(0, false)]
        public void ALaunchStageFlownByAPilotComesHomeAnEmptyPodDoesNot(int pilots, bool splits)
        {
            List<CarrierPart> parts = Stack()
                .Select(p => p.Id == 101 ? new CarrierPart(101, 100, "pod", CommandPart.Controls(1, pilots) ? CarrierPartKind.Command : CarrierPartKind.None, 0.8, 0.8) : p)
                .ToList();

            Assert.Equal(splits, LaunchStage.Find(parts) != null);
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void NoSplitWithoutACoreOrParachutesAndTheBriefingSaysWhy(bool core, bool chutes)
        {
            Assert.Null(LaunchStage.Find(Stack(launchCore: core, launchChutes: chutes)));

            Briefing b = Brief(Stack(launchCore: core, launchChutes: chutes));

            Assert.False(b.Deployment!.LaunchStage.ComesHome);
            Assert.Equal(LaunchStage.Needs, b.Deployment.LaunchStage.Text);
            Assert.DoesNotContain(b.Burns, x => ConstellationPlanner.IsRelease(x) && !ConstellationPlanner.IsSatelliteRelease(x));
        }

        [Fact]
        public void ALaunchStageWithoutDeltaVStaysAttachedAndSaysWhy()
        {
            Briefing b = Brief(Stack(launchTank: 0.5));

            Assert.NotNull(b.Deployment!.LaunchStage.Stage);
            Assert.False(b.Deployment.LaunchStage.ComesHome);
            Assert.Contains(b.Contingencies, c => c.Severity == Severity.Amber && c.Text.StartsWith("the launch stage stays attached: it has 0 m/s"));
        }

        [Fact]
        public void TheLaunchStageSplitsOffBeforeAnythingElse()
        {
            Briefing b = Brief(Stack());

            Assert.True(b.CanApprove, string.Join("; ", b.Contingencies.Select(c => c.Text)));
            LaunchStagePlan launch = b.Deployment!.LaunchStage;
            Assert.True(launch.ComesHome);
            Assert.Equal(Name + " Launch Stage", launch.Name);
            Assert.InRange(launch.DeltaV, 1, launch.Available / CarrierReturn.Reserve);
            PlannedBurn split = b.Burns[0];
            Assert.Equal("release " + Name + " Launch Stage", split.Label);
            Assert.Equal(101u, split.ReleasePart);
            Assert.False(ConstellationPlanner.IsSatelliteRelease(split));
            Assert.All(b.Burns.Skip(1), x => Assert.True(x.StartUt >= split.EndUt + ConstellationPlanner.SplitLead - 1, x.Label));
            Assert.Equal(new uint[] { 11, 21, 31 }, b.Burns.Where(ConstellationPlanner.IsSatelliteRelease).Select(x => x.ReleasePart!.Value).OrderBy(id => id));
        }

        [Fact]
        public void TheLaunchStageReturnWaitsForTheLastStepOfTheDeployment()
        {
            Briefing b = Brief(Stack());

            PlannedBurn step = b.Burns.Last();
            Assert.True(ConstellationPlanner.IsStep(step));
            Assert.False(ConstellationPlanner.IsRelease(step));
            Assert.All(b.Burns.Take(b.Burns.Count - 1), x => Assert.True(x.EndUt < step.StartUt, x.Label));
            Assert.DoesNotContain(b.Burns, x => x.VesselId == "101");
        }

        [Fact]
        public void TheTransferStageBurnsUpByDefaultOverKerbin()
        {
            Briefing b = Brief(Stack());

            ReturnPlan back = b.Deployment!.Return;
            Assert.Equal(ReturnKind.BurnUp, back.Kind);
            Assert.False(back.Fallback);
            PlannedBurn burnUp = Assert.Single(b.Burns, ConstellationPlanner.IsBurnUp);
            Assert.InRange(burnUp.Expected!.PeriapsisRadius - Kerbin.Radius, 0, Kerbin.AtmosphereHeight * 0.5);
            Assert.InRange(back.DeltaV, 1, back.Available);
            Assert.Equal(ConstellationPlanner.SpacingLabel, b.Burns.Last().Label);
            Assert.True(b.Burns.Last().StartUt > burnUp.EndUt);
        }

        [Fact]
        public void BurnUpCountsInTheRequiredDeltaV()
        {
            Briefing burnUp = Brief(Stack());
            Briefing park = Brief(Stack(), EndOfLife.Park);

            Assert.Equal(ReturnKind.Disposal, park.Deployment!.Return.Kind);
            Assert.Equal(burnUp.RequiredDeltaV - burnUp.Deployment!.Return.DeltaV, park.RequiredDeltaV - park.Deployment.Return.DeltaV, 3);
            Assert.True(burnUp.RequiredDeltaV > burnUp.Deployment.Return.DeltaV);
        }

        [Fact]
        public void WithoutAirTheDefaultParksAndAChosenBurnUpWarns()
        {
            OrbitElements orbit = Circular(Mun, 30_000);
            double ring = Mun.Radius + 500_000;

            ReturnPlan auto = CarrierReturn.Plan(Mun, null, orbit, 2_000, false, ring, true, 0);
            ReturnPlan chosen = CarrierReturn.Plan(Mun, null, orbit, 2_000, false, ring, true, 0, EndOfLife.BurnUp);
            Briefing briefed = Brief(Stack(), EndOfLife.BurnUp, Mun);

            Assert.Equal(ReturnKind.Disposal, auto.Kind);
            Assert.False(auto.Fallback);
            Assert.Equal(ReturnKind.Disposal, chosen.Kind);
            Assert.True(chosen.Fallback);
            Assert.StartsWith("Mun has no atmosphere to burn up in", chosen.Text);
            Assert.Contains(briefed.Contingencies, c => c.Severity == Severity.Amber && c.Text.StartsWith("Mun has no atmosphere to burn up in"));
            Assert.False(briefed.Deployment!.LaunchStage.ComesHome);
            Assert.Contains("comes home only from an orbit around Kerbin", briefed.Deployment.LaunchStage.Text);
        }

        [Fact]
        public void ShortOfDeltaVTheBurnUpFallsBackToParking()
        {
            OrbitElements orbit = Circular(Kerbin, 2_000_000);

            ReturnPlan plan = CarrierReturn.Plan(Kerbin, null, orbit, 200, false, Kerbin.Radius + 2_000_000, true, 0);

            Assert.NotEqual(ReturnKind.BurnUp, plan.Kind);
            Assert.StartsWith("the transfer stage has 200 m/s left of the", plan.Text);
            Assert.Equal(ReturnKind.BurnUp, CarrierReturn.Plan(Kerbin, null, orbit, 2_000, false, Kerbin.Radius + 2_000_000, true, 0).Kind);
        }

        [Fact]
        public void TheEndOfLifeChoiceSurvivesASave()
        {
            Briefing b = Brief(Stack(), EndOfLife.Park);
            var op = new Operation("op-1", "Ring", OrbitChange.Deploy(3_000_000, null, EndOfLife.Park), new[] { "100" }, b.Burns);

            Operation reloaded = MissionJson.Read(MissionJson.Write(new Mission("m-1", "Ring", new[] { op }))).Operations[0];

            Assert.Equal(EndOfLife.Park, reloaded.Change.EndOfLife);
            Assert.Equal(b.Burns[0].ReleasePart, reloaded.Burns[0].ReleasePart);
        }
    }
}

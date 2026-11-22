using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.AutoSystems;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class AutoSystemsTests
    {
        private const double KerbinAir = 70_000;

        private static StagePart Part(string name, PartRole roles, int stage, int decoupledIn = -1, EngineState engine = EngineState.None, bool fuelLeft = true) =>
            new StagePart(name, roles, stage, decoupledIn, engine, fuelLeft);

        /// <summary>Pad stage 5. Stage 4 lights the core and two boosters on clamps, 3 drops the boosters, 2 drops the core, 1 lights the lander, 0 holds the chutes.</summary>
        private static List<StagePart> BoosterCoreLander() => new List<StagePart>
        {
            Part("probeCore", PartRole.Command, -1),
            Part("chute", PartRole.Parachute, 0),
            Part("landerEngine", PartRole.Engine, 1),
            Part("landerTank", PartRole.Tank, -1),
            Part("coreDecoupler", PartRole.Decoupler, 2, 2),
            Part("coreTank", PartRole.Tank, -1, 2),
            Part("coreEngine", PartRole.Engine, 4, 2),
            Part("boosterDecouplerA", PartRole.Decoupler, 3, 3),
            Part("boosterDecouplerB", PartRole.Decoupler, 3, 3),
            Part("boosterA", PartRole.Engine | PartRole.Tank, 4, 3),
            Part("boosterB", PartRole.Engine | PartRole.Tank, 4, 3),
            Part("clamp", PartRole.Clamp, 4),
        };

        private static readonly StageFigure[] LaunchFigures =
        {
            new StageFigure(4, 1500, 60),
            new StageFigure(3, 1400, 72),
            new StageFigure(1, 900, 110),
            new StageFigure(0, 0, 0),
        };

        [Fact]
        public void BoosterCoreLanderStackReadsOneLinePerStage()
        {
            var plan = StagePlanner.Plan(BoosterCoreLander(), 5, new StagePlanContext(OrbitChangeKind.Launch, KerbinAir, LaunchFigures));

            Assert.Equal(new[]
            {
                "stage 4: light 3 engines, release the clamps, at liftoff",
                "stage 3: drop 2 boosters, at 1:00",
                "stage 2: drop the spent stage, at 2:12",
                "stage 1: light 1 engine, at 2:12",
                "stage 0: parachutes, kept",
            }, plan.Select(s => s.Describe()));
            Assert.All(plan, s => Assert.Null(s.Hazard));
            Assert.Equal(new[] { true, true, true, true, false }, plan.Select(s => s.Fires));
        }

        [Fact]
        public void ALandingKeepsTheChutesForTheDescent()
        {
            var figures = new[] { new StageFigure(1, 300, 20), new StageFigure(0, 0, 0) };
            var plan = StagePlanner.Plan(BoosterCoreLander().Where(p => p.Stage <= 1 && p.DecoupledIn < 2).ToList(), 2, new StagePlanContext(OrbitChangeKind.Land, KerbinAir, figures));

            Assert.Equal("stage 0: parachutes, kept for landing", plan.Last().Describe());
            Assert.Null(plan.Last().Hazard);
        }

        [Fact]
        public void AFairingStageBelowTheBurnsGoesNearTheTopOfTheAir()
        {
            var parts = new List<StagePart>
            {
                Part("probeCore", PartRole.Command, -1),
                Part("payloadDecoupler", PartRole.Decoupler, 0, 0),
                Part("payload", PartRole.None, -1, 0),
                Part("fairing", PartRole.Fairing, 1),
                Part("upperEngine", PartRole.Engine, 2),
                Part("stageDecoupler", PartRole.Decoupler, 2, 2),
                Part("lowerEngine", PartRole.Engine | PartRole.Tank, 3, 2),
                Part("clamp", PartRole.Clamp, 3),
            };
            var figures = new[] { new StageFigure(3, 2400, 90), new StageFigure(2, 1100, 80) };

            var plan = StagePlanner.Plan(parts, 4, new StagePlanContext(OrbitChangeKind.Launch, KerbinAir, figures));

            Assert.Equal("stage 2: drop the spent stage, light 1 engine, at 1:30", plan[1].Describe());
            Assert.Equal("stage 1: jettison the fairing, above 63 km", plan[2].Describe());
            Assert.Equal("stage 0: separate, kept", plan[3].Describe());
            Assert.All(plan, s => Assert.Null(s.Hazard));
            Assert.False(FairingRule.ShouldJettison(KerbinAir, 62_000));
            Assert.True(FairingRule.ShouldJettison(KerbinAir, 63_500));
            Assert.True(FairingRule.ShouldJettison(0, 10));
            Assert.Equal("before the first burn", FairingRule.When(0));
        }

        [Fact]
        public void AnUnsafeStackIsRedWhereTheStagerMustPassAndAmberBelow()
        {
            var parts = new List<StagePart>
            {
                Part("tankRoot", PartRole.Tank, -1),
                Part("chute", PartRole.Parachute, 2),
                Part("engine", PartRole.Engine, 1),
                Part("coreDecoupler", PartRole.Decoupler, 0, 0),
                Part("probeCore", PartRole.Command, -1, 0),
            };
            var figures = new[] { new StageFigure(1, 400, 30) };

            var plan = StagePlanner.Plan(parts, 3, new StagePlanContext(OrbitChangeKind.Circularize, 0, figures));

            Assert.Equal((Severity?)Severity.Red, plan[0].Hazard);
            Assert.Equal("opens parachutes in vacuum", plan[0].Warning);
            Assert.Null(plan[1].Hazard);
            Assert.Equal((Severity?)Severity.Amber, plan[2].Hazard);
            Assert.Equal("drops the probe core or command pod", plan[2].Warning);
        }

        [Fact]
        public void DroppingAnEngineBeforeItFiresIsRedWhenTheStageFires()
        {
            var parts = new List<StagePart>
            {
                Part("probeCore", PartRole.Command, -1),
                Part("mainEngine", PartRole.Engine, 2),
                Part("decoupler", PartRole.Decoupler, 1, 1),
                Part("strandedEngine", PartRole.Engine, 0, 1),
            };

            var plan = StagePlanner.Plan(parts, 3, new StagePlanContext(OrbitChangeKind.ChangeApoapsis, 0, new[] { new StageFigure(2, 100, 10), new StageFigure(0, 300, 30) }));

            Assert.Equal((Severity?)Severity.Red, plan[1].Hazard);
            Assert.Equal("drops an engine before it fires", plan[1].Warning);
        }

        [Fact]
        public void TheLiveStagerHoldsChutesUntilArmedAndNeverDropsTheProbeCore()
        {
            var flamedOut = new List<StagePart>
            {
                Part("probeCore", PartRole.Command, -1),
                Part("engine", PartRole.Engine, 2, engine: EngineState.Spent),
                Part("chute", PartRole.Parachute, 1),
                Part("lander", PartRole.Engine, 0),
            };
            StagingView chutes = StagingView.Of(flamedOut, 2);
            Assert.True(chutes.FiresChutes);
            Assert.False(StagingRule.ShouldStage(chutes));
            Assert.True(StagingRule.ShouldStage(chutes, chutesArmed: true));

            var core = new List<StagePart>
            {
                Part("tankRoot", PartRole.Tank, -1),
                Part("engine", PartRole.Engine, 2, engine: EngineState.Spent),
                Part("decoupler", PartRole.Decoupler, 1, 1),
                Part("probeCore", PartRole.Command, -1, 1),
                Part("lander", PartRole.Engine, 0),
            };
            StagingView drop = StagingView.Of(core, 2);
            Assert.True(drop.DropsCommand);
            Assert.False(StagingRule.ShouldStage(drop, chutesArmed: true));
            Assert.NotNull(StagingRule.Held(drop));
        }

        [Fact]
        public void TheLiveViewDropsSpentBoostersAndEmptyTanksButNotABurningEngine()
        {
            var boosters = BoosterCoreLander()
                .Select(p => p.Is(PartRole.Engine) && p.Stage == 4 ? new StagePart(p.Name, p.Roles, p.Stage, p.DecoupledIn, p.Name.StartsWith("booster", StringComparison.Ordinal) ? EngineState.Spent : EngineState.Burning) : p)
                .Where(p => !p.Is(PartRole.Clamp))
                .ToList();
            StagingView view = StagingView.Of(boosters, 4);
            Assert.Equal(1, view.BurningEngines);
            Assert.Equal(2, view.DroppedSpent);
            Assert.True(StagingRule.ShouldStage(view));

            var stillBurning = boosters.Select(p => p.Engine == EngineState.Spent ? new StagePart(p.Name, p.Roles, p.Stage, p.DecoupledIn, EngineState.Burning) : p).ToList();
            Assert.False(StagingRule.ShouldStage(StagingView.Of(stillBurning, 4)));

            var tanks = new List<StagePart>
            {
                Part("probeCore", PartRole.Command, -1),
                Part("engine", PartRole.Engine, 2, engine: EngineState.Burning),
                Part("dropTank", PartRole.Tank, -1, 1, fuelLeft: false),
            };
            Assert.True(StagingRule.ShouldStage(StagingView.Of(tanks, 2)));
            Assert.False(StagingRule.ShouldStage(StagingView.Of(new List<StagePart> { tanks[0], tanks[1], Part("dropTank", PartRole.Tank, -1, 1) }, 2)));
        }

        [Theory]
        [InlineData(AutoPhase.Descent, 2000, -50, false, GearState.Untouched, GearCommand.None)]
        [InlineData(AutoPhase.Descent, 1400, -100, false, GearState.Untouched, GearCommand.Deploy)]
        [InlineData(AutoPhase.Descent, 990, -5, false, GearState.Untouched, GearCommand.Deploy)]
        [InlineData(AutoPhase.Descent, 500, -5, false, GearState.Deployed, GearCommand.None)]
        [InlineData(AutoPhase.Descent, 900, 5, false, GearState.Untouched, GearCommand.None)]
        [InlineData(AutoPhase.Descent, 0, 0, true, GearState.Deployed, GearCommand.Brake)]
        [InlineData(AutoPhase.Descent, 0, 0, true, GearState.Braked, GearCommand.None)]
        [InlineData(AutoPhase.Ascent, 0, 0, true, GearState.Untouched, GearCommand.None)]
        [InlineData(AutoPhase.Ascent, 300, 50, false, GearState.Untouched, GearCommand.None)]
        [InlineData(AutoPhase.Ascent, 600, 80, false, GearState.Untouched, GearCommand.Retract)]
        [InlineData(AutoPhase.Ascent, 200, 150, false, GearState.Untouched, GearCommand.Retract)]
        [InlineData(AutoPhase.Ascent, 5000, 300, false, GearState.Retracted, GearCommand.None)]
        [InlineData(AutoPhase.Burn, 100, -50, false, GearState.Untouched, GearCommand.None)]
        public void GearComesDownFifteenSecondsOrAKilometreUpAndGoesUpAfterLaunch(AutoPhase phase, double height, double verticalSpeed, bool landed, GearState state, GearCommand expected)
        {
            Assert.Equal(expected, GearRule.Decide(phase, height, verticalSpeed, landed, state));
        }

        [Theory]
        [InlineData(AutoPhase.Ascent, true, true, DeployCommand.Retract)]
        [InlineData(AutoPhase.Descent, true, true, DeployCommand.Retract)]
        [InlineData(AutoPhase.Ascent, true, false, DeployCommand.Hold)]
        [InlineData(AutoPhase.Ascent, false, true, DeployCommand.Hold)]
        [InlineData(AutoPhase.Orbit, true, true, DeployCommand.Extend)]
        [InlineData(AutoPhase.Orbit, false, true, DeployCommand.Hold)]
        [InlineData(AutoPhase.Burn, true, true, DeployCommand.Hold)]
        public void PanelsFoldForTheAirAndOpenInOrbitButOneShotsAreLeftAlone(AutoPhase phase, bool retractable, bool breakable, DeployCommand expected)
        {
            Assert.Equal(expected, DeployableRule.Decide(phase, retractable, breakable));
        }

        [Fact]
        public void TheBriefingListsEveryAutomaticActionAndOffListsNone()
        {
            var kit = new VesselKit(gear: 3, fairings: 1, retractable: 2, oneShot: 1);

            var launch = AutoPlan.Actions(OrbitChangeKind.Launch, kit, KerbinAir, on: true);
            Assert.Contains("jettison 1 fairing above 63 km", launch);
            Assert.Contains("retract landing gear above 500 m or when climbing fast", launch);
            Assert.Contains("retract 2 panels, antennas and radiators before liftoff, extend them in a stable orbit", launch);
            Assert.Contains("leave 1 panel, antenna or radiator that cannot fold back alone", launch);

            var land = AutoPlan.Actions(OrbitChangeKind.Land, kit, KerbinAir, on: true);
            Assert.Contains("lower landing gear 15 s or 1000 m above the ground, brakes on at touchdown", land);

            Assert.Single(AutoPlan.Actions(OrbitChangeKind.Launch, kit, KerbinAir, on: false));
            Assert.True(DeployableRule.StableOrbit(75_000, KerbinAir));
            Assert.False(DeployableRule.StableOrbit(-200_000, KerbinAir));
        }

        [Fact]
        public void TheSwitchSurvivesASaveAndDefaultsOn()
        {
            var off = new Operation("op-1", "Quiet Heron", OrbitChange.Circularize().WithAutoSystems(false), new[] { "1" }, Array.Empty<PlannedBurn>());
            var on = new Operation("op-2", "Loud Heron", OrbitChange.Apoapsis(90_000), new[] { "1" }, Array.Empty<PlannedBurn>());

            Mission back = MissionJson.Read(MissionJson.Write(new Mission("m-1", "Save", new[] { off, on })));

            Assert.False(back.Operations[0].Change.AutoSystems);
            Assert.True(back.Operations[1].Change.AutoSystems);
            Assert.Equal(90_000, back.Operations[1].Change.Target);
        }
    }
}

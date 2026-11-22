using System;
using System.Linq;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.TwoBody;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class LaunchBriefingTests
    {
        private static readonly CentralBody Kerbin = new CentralBody("Kerbin", 3.5316e12, 600_000, 70_000, 6_767);
        private const double KerbinDay = 21_549.425;
        private const double Now = 10_000;

        [Fact]
        public void APadRocketWithEnoughDeltaVIsReadyAndLaunchesAfterTheLead()
        {
            Briefing briefing = BriefingBuilder.Build("Bright Kite", OrbitChange.Launch(LaunchTarget.Circular(80_000)), new[] { OnPad(4_500) }, Now);

            Assert.True(briefing.CanApprove, string.Join("; ", briefing.Contingencies.Select(c => c.Text)));
            Assert.Equal(2, briefing.Burns.Count);
            Assert.True(LaunchBriefing.IsAscent(briefing.Burns[0]));
            Assert.Equal(Now + BriefingBuilder.Lead, briefing.Burns[0].StartUt);
            Assert.InRange(briefing.RequiredDeltaV, 3_000, 3_600);
            Assert.Equal("launch to 80 km circular", briefing.Change);
        }

        [Fact]
        public void ARocketShortOfDeltaVGetsARedContingency()
        {
            Briefing briefing = BriefingBuilder.Build("Dim Kite", OrbitChange.Launch(LaunchTarget.Circular(80_000)), new[] { OnPad(2_400) }, Now);

            Assert.False(briefing.CanApprove);
            Assert.Contains(briefing.Contingencies, c => c.Severity == Severity.Red && c.Text.StartsWith("short of delta-v"));
        }

        [Fact]
        public void APeriapsisInsideTheAtmosphereIsRefused()
        {
            Briefing briefing = BriefingBuilder.Build("Low Kite", OrbitChange.Launch(new LaunchTarget(80_000, 50_000)), new[] { OnPad(4_500) }, Now);

            Assert.False(briefing.CanApprove);
            Assert.Empty(briefing.Burns);
        }

        [Fact]
        public void AnInclinationAndLanWaitForTheWindow()
        {
            var change = OrbitChange.Launch(LaunchTarget.Circular(80_000, Angles.ToRadians(45), Angles.ToRadians(90)));
            Briefing briefing = BriefingBuilder.Build("Tilted Kite", change, new[] { OnPad(4_500) }, Now);

            Assert.True(briefing.CanApprove);
            Assert.InRange(briefing.Burns[0].StartUt, Now + BriefingBuilder.Lead, Now + BriefingBuilder.Lead + KerbinDay);
            Assert.Equal("launch to 80 km circular, inclination 45 deg, LAN 90 deg", briefing.Change);
        }

        [Fact]
        public void AfterTheAscentTheFinishingBurnStartsNoEarlierThanNow()
        {
            double r = Kerbin.Radius + 72_000;
            var state = new StateVector(new Vector3d(r, 0, 0), new Vector3d(60, 2_150, 0));
            var orbit = OrbitElements.FromState(Kerbin.GravParameter, state, Now);
            var task = new VesselTask("v1", "Kerbal X", Kerbin, orbit, Stages(1_500));

            Briefing briefing = BriefingBuilder.Build("Bright Kite", OrbitChange.Launch(LaunchTarget.Circular(80_000)), new[] { task }, Now);

            Assert.True(briefing.CanApprove, string.Join("; ", briefing.Contingencies.Select(c => c.Text)));
            PlannedBurn burn = briefing.Burns[0];
            Assert.True(burn.StartUt >= Now);
            Assert.True(burn.NodeUt < Now + orbit.Period / 2);
            OrbitElements final = briefing.Burns[briefing.Burns.Count - 1].OrbitAfter!;
            Assert.InRange(final.PeriapsisRadius - Kerbin.Radius, 79_000, 81_000);
            Assert.InRange(final.ApoapsisRadius - Kerbin.Radius, 79_000, 81_000);
        }

        [Fact]
        public void ALaunchOperationSurvivesTheSave()
        {
            var change = OrbitChange.Launch(new LaunchTarget(150_000, 120_000, Angles.ToRadians(45), null));
            var op = new Operation("op-1", "Bright Kite", change, new[] { "v1" }, new[] { new PlannedBurn("v1", LaunchBriefing.AscentLabel, 100, 100, 150, 2_000, 0, 0) }, StepState.Briefed, null);

            Operation back = MissionJson.Read(MissionJson.Write(new Mission("m-1", "Launch", new[] { op }))).Operations[0];

            Assert.Equal(OrbitChangeKind.Launch, back.Change.Kind);
            Assert.Equal(150_000, back.Change.LaunchTarget!.Apoapsis);
            Assert.Equal(120_000, back.Change.LaunchTarget.Periapsis);
            Assert.Equal(Angles.ToRadians(45), back.Change.LaunchTarget.Inclination!.Value, 12);
            Assert.Null(back.Change.LaunchTarget.Lan);
        }

        [Fact]
        public void ALaunchShowsTheOrbitItAimsForWithoutGivingTheRunnerOne()
        {
            Briefing briefing = BriefingBuilder.Build("Bright Kite", OrbitChange.Launch(new LaunchTarget(120_000, 80_000)), new[] { OnPad(4_500) }, Now);

            OrbitElements aimed = briefing.Burns[1].Expected!;
            Assert.Null(briefing.Burns[1].OrbitAfter);
            Assert.Null(briefing.Burns[0].Expected);
            Assert.Equal(Kerbin.Radius + 80_000, aimed.PeriapsisRadius, 3);
            Assert.Equal(Kerbin.Radius + 120_000, aimed.ApoapsisRadius, 3);
        }

        private static VesselTask OnPad(double deltaV)
        {
            var site = new LaunchSite(Angles.ToRadians(-0.0972), 0, 70, KerbinDay, Now);
            Vector3d r = site.Position(Kerbin.Radius);
            var orbit = OrbitElements.FromState(Kerbin.GravParameter, new StateVector(r, site.Velocity(Kerbin.Radius)), Now);
            return new VesselTask("v1", "Kerbal X", Kerbin, orbit, Stages(deltaV), null, site);
        }

        /// <summary>Three stages sharing <paramref name="deltaV"/>, the last one weakest.</summary>
        private static StageDeltaV[] Stages(double deltaV) => new[]
        {
            new StageDeltaV(2, 130_000, 66_000, 2_300_000, deltaV * 0.4, 80),
            new StageDeltaV(1, 62_000, 26_000, 1_500_000, deltaV * 0.4, 70),
            new StageDeltaV(0, 22_000, 13_000, 250_000, deltaV * 0.2, 120),
        };
    }
}

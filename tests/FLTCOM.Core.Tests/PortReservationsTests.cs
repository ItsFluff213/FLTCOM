using System.Collections.Generic;
using FLTCOM.Core.Execution;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class PortReservationsTests
    {
        private static Briefing Brief(string vesselId) =>
            new Briefing("Op", "rendezvous", new[] { new PlannedBurn(vesselId, "transfer to the target", 2000, 1990, 20, 30, 0, 0) }, 30, 500, 15, new List<BriefedStage>(), new List<Contingency>());

        private static OrbitChange To(uint? port) => OrbitChange.RendezvousWith(new RendezvousTarget("station", "Kerbin Station", port));

        [Fact]
        public void ASecondRendezvousToTheSamePortIsRefusedAndNamesTheHolder()
        {
            var runner = new MissionRunner();
            runner.AddBriefed("op-1", "Quiet Heron", To(7), Brief("ship-1"));
            runner.AddBriefed("op-2", "Amber Lynx", To(7), Brief("ship-2"));
            Assert.Null(runner.Approve("op-1", "Quiet Heron", uplink: true));

            string? error = runner.Approve("op-2", "Amber Lynx", uplink: true);

            Assert.Contains("Quiet Heron", error);
            Assert.Equal(StepState.Briefed, runner.Find("op-2")!.State);
        }

        [Fact]
        public void ADifferentPortOnTheSameVesselIsFree()
        {
            var runner = new MissionRunner();
            runner.AddBriefed("op-1", "Quiet Heron", To(7), Brief("ship-1"));
            runner.AddBriefed("op-2", "Amber Lynx", To(8), Brief("ship-2"));
            runner.Approve("op-1", "Quiet Heron", uplink: true);

            Assert.Null(runner.Approve("op-2", "Amber Lynx", uplink: true));
        }

        [Fact]
        public void ARendezvousWithoutAPortHoldsTheWholeVessel()
        {
            var runner = new MissionRunner();
            runner.AddBriefed("op-1", "Quiet Heron", To(null), Brief("ship-1"));
            runner.Approve("op-1", "Quiet Heron", uplink: true);

            Assert.Equal("Quiet Heron", PortReservations.Holder(runner.Operations, new RendezvousTarget("station", "Kerbin Station", 8))!.Codename);
        }

        [Fact]
        public void AScrubbedRendezvousReleasesThePort()
        {
            var runner = new MissionRunner();
            runner.AddBriefed("op-1", "Quiet Heron", To(7), Brief("ship-1"));
            runner.Approve("op-1", "Quiet Heron", uplink: true);
            runner.Scrub("op-1");

            Assert.Null(PortReservations.Holder(runner.Operations, new RendezvousTarget("station", "Kerbin Station", 7)));
        }

        [Fact]
        public void ABriefedRendezvousHoldsNothingYet()
        {
            var runner = new MissionRunner();
            runner.AddBriefed("op-1", "Quiet Heron", To(7), Brief("ship-1"));

            Assert.Null(PortReservations.Holder(runner.Operations, new RendezvousTarget("station", "Kerbin Station", 7)));
        }
    }
}

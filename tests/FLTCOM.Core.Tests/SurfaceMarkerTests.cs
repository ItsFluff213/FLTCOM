using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Protocol;
using FLTCOM.Core.Telemetry;
using Xunit;
using LandingTarget = FLTCOM.Core.Landing.LandingTarget;

namespace FLTCOM.Core.Tests
{
    public class SurfaceMarkerTests
    {
        private const double Deg = Math.PI / 180;

        private static List<SurfaceMarker> Collect(string? home = "Kerbin", WaypointSighting[]? waypoints = null, GroundedVessel[]? vessels = null) =>
            SurfaceMarkers.Collect(home, waypoints ?? Array.Empty<WaypointSighting>(), vessels ?? Array.Empty<GroundedVessel>());

        [Fact]
        public void TheHomePlanetCarriesThePadAndTheRunwayAtTheLandingPlannersCoordinates()
        {
            List<SurfaceMarker> markers = Collect();

            Assert.Equal(new[] { SurfaceMarkers.LaunchPadId, SurfaceMarkers.RunwayId }, markers.Select(m => m.Id));
            Assert.All(markers, m => Assert.Equal(SurfaceMarkerKind.Site, m.Kind));
            Assert.All(markers, m => Assert.Equal("Kerbin", m.Body));
            Assert.Equal(LandingTarget.LaunchPad.Longitude, markers[0].Longitude, 6);
            Assert.Equal(LandingTarget.Runway.Latitude, markers[1].Latitude, 6);
            Assert.Empty(Collect(home: null));
        }

        [Fact]
        public void WaypointsAreCustomOrContractAndTurnToEastPositiveRadians()
        {
            List<SurfaceMarker> markers = Collect(null, new[]
            {
                new WaypointSighting("a1", "Crater Rim", "Mun", 3.5, 270, isCustom: true),
                new WaypointSighting("b2", "Site Alpha", "Mun", -12, -40, isCustom: false),
            });

            SurfaceMarker custom = markers.Single(m => m.Id == "waypoint:a1");
            Assert.Equal(SurfaceMarkerKind.Waypoint, custom.Kind);
            Assert.Equal(3.5 * Deg, custom.Latitude, 6);
            Assert.Equal(-90 * Deg, custom.Longitude, 6);
            SurfaceMarker contract = markers.Single(m => m.Id == "contract:b2");
            Assert.Equal(SurfaceMarkerKind.Contract, contract.Kind);
            Assert.Equal(-40 * Deg, contract.Longitude, 6);
        }

        [Fact]
        public void FlagsAndLandedCraftAreMarkedButDebrisAndScienceGearAreNot()
        {
            List<SurfaceMarker> markers = Collect(null, vessels: new[]
            {
                new GroundedVessel(7, "Mun Flag", VesselType.Flag, "Mun", 1, 2),
                new GroundedVessel(8, "Mun Lander 2", VesselType.Lander, "Mun", 3.2, -51.7),
                new GroundedVessel(9, "Spent Stage", VesselType.Debris, "Mun", 0, 0),
                new GroundedVessel(10, "Seismometer", VesselType.DeployedSciencePart, "Mun", 0, 0),
            });

            Assert.Equal(new[] { ("flag:7", SurfaceMarkerKind.Flag), ("vessel:8", SurfaceMarkerKind.Vessel) }, markers.Select(m => (m.Id, m.Kind)));
        }

        [Fact]
        public void TheOrderIsStableAndADuplicateKeyIsDropped()
        {
            WaypointSighting[] waypoints =
            {
                new WaypointSighting("z", "Zulu", "Mun", 0, 0, true),
                new WaypointSighting("a", "Alpha", "Mun", 0, 0, true),
                new WaypointSighting("a", "Alpha again", "Mun", 1, 1, true),
            };
            GroundedVessel[] vessels = { new GroundedVessel(1, "Flag", VesselType.Flag, "Mun", 0, 0) };

            List<SurfaceMarker> first = Collect("Kerbin", waypoints, vessels);
            List<SurfaceMarker> again = Collect("Kerbin", waypoints.Reverse().ToArray(), vessels);

            Assert.Equal(new[] { "site:launchPad", "site:runway", "waypoint:a", "waypoint:z", "flag:1" }, first.Select(m => m.Id));
            Assert.Equal("Alpha", first[2].Name);
            Assert.Equal(first.Select(m => m.Id), again.Select(m => m.Id));
        }

        [Fact]
        public void AResitingVesselsJitterIsNotAChange()
        {
            var feed = new MarkerFeed();
            GroundedVessel At(double lon) => new GroundedVessel(8, "Lander", VesselType.Lander, "Mun", 3.2, lon);

            Assert.NotNull(feed.Next(Collect(null, vessels: new[] { At(-51.7) })));
            Assert.Null(feed.Next(Collect(null, vessels: new[] { At(-51.7 + 1e-9) })));
            Assert.NotNull(feed.Next(Collect(null, vessels: new[] { At(-51.6) })));
        }

        [Fact]
        public void TheFeedSendsOnChangeAndRefreshesForLateDisplays()
        {
            var feed = new MarkerFeed();
            List<SurfaceMarker> markers = Collect();

            Assert.NotNull(feed.Next(markers));
            for (int i = 1; i < MarkerFeed.RefreshEvery; i++)
            {
                Assert.Null(feed.Next(markers));
            }
            Assert.NotNull(feed.Next(markers));
            Assert.NotNull(feed.Next(Collect(home: null)));
        }

        private static Operation Landing(string id, StepState state, LandingTarget target) =>
            new Operation(id, "Ember", OrbitChange.Land(target), new[] { "v1" }, Array.Empty<PlannedBurn>(), state);

        [Fact]
        public void OnlyApprovedUnfinishedLandingsWantAWaypoint()
        {
            var ops = new[]
            {
                Landing("briefed", StepState.Briefed, LandingTarget.At(0.1, 0.2)),
                Landing("flying", StepState.Executing, LandingTarget.At(0.1, 0.2)),
                Landing("waiting", StepState.Waiting, LandingTarget.LaunchPad),
                Landing("done", StepState.Done, LandingTarget.At(0.1, 0.2)),
                Landing("scrubbed", StepState.Scrubbed, LandingTarget.At(0.1, 0.2)),
                new Operation("orbit", "Ash", OrbitChange.Circularize(), new[] { "v1" }, Array.Empty<PlannedBurn>(), StepState.Executing),
            };

            List<LandingMark> wanted = LandingWaypointPlan.Wanted(ops, "Kerbin", vesselId => "Mun");

            Assert.Equal(new[] { ("flying", "Mun"), ("waiting", "Kerbin") }, wanted.Select(m => (m.OperationId, m.Body)));
            Assert.Equal("FLTCOM Ember landing", wanted[0].Name);
            Assert.Equal(0.2, wanted[0].Longitude);
        }

        [Fact]
        public void TheDiffPlacesNewMarksAndTakesAwayOnlyItsOwn()
        {
            var keep = new LandingMark("a", "FLTCOM A landing", "Mun", 0.1, 0.2);
            var fresh = new LandingMark("b", "FLTCOM B landing", "Mun", 0.3, 0.4);
            var finished = new LandingMark("c", "FLTCOM C landing", "Mun", 0.5, 0.6);

            var (add, remove) = LandingWaypointPlan.Diff(new[] { keep, fresh }, new[] { new LandingMark("a", "FLTCOM A landing", "Mun", 0.1, 0.2), finished });

            Assert.Equal(new[] { fresh }, add);
            Assert.Equal(new[] { finished }, remove);
            Assert.Empty(LandingWaypointPlan.Diff(Array.Empty<LandingMark>(), Array.Empty<LandingMark>()).Remove);
        }
    }
}

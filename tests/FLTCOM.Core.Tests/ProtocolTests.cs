using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FLTCOM.Core.Protocol;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class ProtocolTests
    {
        private static string RepoFile(params string[] parts)
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FLTCOM.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            return File.ReadAllText(Path.Combine(new[] { dir ?? throw new DirectoryNotFoundException("FLTCOM.sln") }.Concat(parts).ToArray()));
        }

        private static string Fixture(string name) => RepoFile("tests", "fixtures", "telemetry", name + ".json");

        private static readonly Dictionary<string, (string Text, string Expect)> Corpus = JsonDocument
            .Parse(RepoFile("tests", "fixtures", "protocol", "parse-corpus.json"))
            .RootElement.EnumerateArray()
            .ToDictionary(item => item.GetProperty("name").GetString()!, item => (item.GetProperty("text").GetString()!, item.GetProperty("expect").GetString()!));

        public static IEnumerable<object[]> CorpusNames() => Corpus.Keys.Select(name => new object[] { name });

        private static Body Sun() => new Body("Sun", null, 261600000, 1.1723328e18, null, 600000, 432000, 0, 0, "#ffd84a", null);

        private static Vessel Probe(uint id) => new Vessel(id, "FLT-0001-PRB", "Probe", VesselType.Probe, new SurfaceLocation(SurfaceSituation.Landed, "Sun", 0, 0, 0), false, true,
            CommsState.Off, null, 0, Array.Empty<CrewMember>(), Array.Empty<Resource>(), 100, null, null, null, null);

        [Theory]
        [MemberData(nameof(CorpusNames))]
        public void ParsesTheSharedCorpusLikeTheDisplay(string name)
        {
            (string text, string expect) = Corpus[name];
            string verdict;
            try
            {
                ProtocolJson.ReadServerMessage(text);
                verdict = "accept";
            }
            catch (ProtocolVersionException)
            {
                verdict = "versionMismatch";
            }
            catch (ProtocolException)
            {
                verdict = "malformed";
            }

            Assert.Equal(expect, verdict);
        }

        [Fact]
        public void RejectsDeepNestingWithoutOverflowingTheStack()
        {
            string deep = new string('[', 10000) + new string(']', 10000);
            string message = "{\"protocolVersion\":14,\"type\":\"pong\",\"nonce\":" + deep + "}";

            var error = Assert.Throws<ProtocolException>(() => ProtocolJson.ReadServerMessage(message));

            Assert.Contains("nested deeper than 64", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void RefusesToBuildAFleetWithTwoVesselsOfOneId()
        {
            var error = Assert.Throws<ArgumentException>(() => new Fleet(0, 0, 1, null, new[] { Sun() }, new[] { Probe(7), Probe(8), Probe(7) }, null));

            Assert.Equal("vessels", error.ParamName);
        }

        [Fact]
        public void RejectsAFleetMessageWithTwoVesselsOfOneId()
        {
            string json = ProtocolJson.Write(new Fleet(0, 0, 1, null, new[] { Sun() }, new[] { Probe(7), Probe(8) }, null));
            string duplicated = json.Replace("\"id\":8", "\"id\":7");

            var error = Assert.Throws<ProtocolException>(() => ProtocolJson.ReadServerMessage(duplicated));

            Assert.Contains("$.vessels[1].id: expected unique value", error.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(0.1)]
        [InlineData(1.0 / 3)]
        [InlineData(0.84551240822557006)]
        [InlineData(5e-324)]
        [InlineData(1.7976931348623157e308)]
        [InlineData(-2.2250738585072014e-308)]
        [InlineData(5184000.123456789)]
        [InlineData(-0.0)]
        public void WritesDoublesThatReadBackBitForBit(double value)
        {
            var fleet = (Fleet)ProtocolJson.ReadServerMessage(ProtocolJson.Write(new Fleet(0, value, 1, null, Array.Empty<Body>(), Array.Empty<Vessel>(), null)));

            Assert.Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(fleet.Ut));
        }

        [Fact]
        public void WritesAFleetOf200VesselsUnder256Kilobytes()
        {
            string json = ProtocolJson.Write(ProtocolJson.ReadServerMessage(Fixture("fleet-200")));

            Assert.InRange(Encoding.UTF8.GetByteCount(json), 1, 256 * 1024);
        }

        private static T RoundTrip<T>(T message)
            where T : ServerMessage
        {
            string json = ProtocolJson.Write(message);
            ServerMessage read = ProtocolJson.ReadServerMessage(json);
            Assert.Equal(json, ProtocolJson.Write(read));
            return Assert.IsType<T>(read);
        }

        private static T RoundTripClient<T>(T message)
            where T : ClientMessage
        {
            string json = ProtocolJson.Write(message);
            ClientMessage read = ProtocolJson.ReadClientMessage(json);
            Assert.Equal(json, ProtocolJson.Write(read));
            return Assert.IsType<T>(read);
        }

        [Fact]
        public void ServerHelloRoundTrips()
        {
            ServerHello hello = RoundTrip(new ServerHello("FLTCOM 0.1.0", null, 0.5));

            Assert.Equal("FLTCOM 0.1.0", hello.Server);
            Assert.Null(hello.SaveName);
            Assert.Equal(0.5, hello.SnapshotInterval);
            Assert.Contains("\"protocolVersion\":" + ProtocolInfo.Version, ProtocolJson.Write(hello), StringComparison.Ordinal);
        }

        [Fact]
        public void FleetRoundTripsEveryFixture()
        {
            foreach (string name in new[] { "kerbin-small", "fleet-200", "comms-lost" })
            {
                var fleet = (Fleet)ProtocolJson.ReadServerMessage(Fixture(name));
                Fleet again = RoundTrip(fleet);

                Assert.Equal(fleet.Vessels.Count, again.Vessels.Count);
                Assert.Equal(17, again.Bodies.Count);
            }
        }

        [Fact]
        public void FleetKeepsVesselDetail()
        {
            var surface = new SurfaceLocation(SurfaceSituation.Landed, "Mun", 0.05, -0.9, 1840.5);
            var orbit = new OrbitalLocation(OrbitalSituation.Orbiting, new Orbit("Kerbin", 850000.25, 0.0012, 0.1, 1.2, 2.3, 6.2831));
            var lander = new Vessel(42u, "FLT-0042-LDR", "Lander \"Two\"", VesselType.Lander, surface, true, true, CommsState.Los, null, 2,
                new[] { new CrewMember("Valentina Kerman", "Pilot", 4) }, new[] { new Resource("LiquidFuel", 120.5, 360) }, 6400, new DeltaV(1210, DeltaVSource.Estimate, new[] { new StageDeltaV(0, 1210, 1.2, 131) }), new Quat(0, 0, 0.7071, 0.7071), new Docking(new[] { new DockingPortInfo(4201, "Clamp-O-Tron Docking Port", "size1", true) }), null);
            var relay = new Vessel(uint.MaxValue, "FLT-9999-RLY", "Relay", VesselType.Relay, orbit, false, false, CommsState.Link, 0.82, 0,
                Array.Empty<CrewMember>(), Array.Empty<Resource>(), 1250, null, null, null, null);
            var comet = new Vessel(7u, "FLT-0007-AST", "C/2026 K1", VesselType.SpaceObject, orbit, false, false, CommsState.None, null, 0,
                Array.Empty<CrewMember>(), Array.Empty<Resource>(), 150000, null, null, null, new SpaceObject(SpaceObjectKind.Comet, SizeClass.D));
            var mun = new Body("Mun", new Orbit("Kerbin", 12000000, 0, 0, 0, 0, 1.7), 200000, 65138398000, 2429559, 0, 138984.38, 4.25, 7061, "#9a9a9a", "/art/body/Mun.png");

            Fleet fleet = RoundTrip(new Fleet(7, 5184000.5, 1, 42u, new[] { Sun(), mun }, new[] { lander, relay, comet }, new MarkerList(new[] { new SurfaceMarker("flag:9", "Mun Flag", SurfaceMarkerKind.Flag, "Mun", 0.05, -0.9) })));

            Assert.Equal(42u, fleet.ActiveVesselId);
            Assert.Equal(SurfaceMarkerKind.Flag, fleet.Markers!.Items[0].Kind);
            Assert.Equal(-0.9, fleet.Markers.Items[0].Longitude);
            Assert.Equal("size1", fleet.Vessels[0].Docking!.Ports[0].Size);
            Vessel readLander = fleet.Vessels[0];
            Assert.True(readLander.Loaded);
            Assert.True(readLander.Controllable);
            Assert.False(fleet.Vessels[1].Loaded);
            Assert.False(fleet.Vessels[1].Controllable);
            Assert.Equal(4.25, fleet.Bodies[1].RotationAngle);
            Assert.Equal(7061, fleet.Bodies[1].HighestTerrainAltitude);
            Assert.Equal("Lander \"Two\"", readLander.Name);
            Assert.Equal(CommsState.Los, readLander.Comms);
            string body = readLander.Location.Match(orbitLocation => orbitLocation.Orbit.ReferenceBody, surfaceLocation => surfaceLocation.Body);
            Assert.Equal("Mun", body);
            Assert.Equal(DeltaVSource.Estimate, readLander.DeltaV!.Source);
            Assert.Equal(uint.MaxValue, fleet.Vessels[1].Id);
            Assert.Equal(0.82, fleet.Vessels[1].SignalStrength);
            Assert.Equal(6.2831, ((OrbitalLocation)fleet.Vessels[1].Location).Orbit.MeanAnomaly);
            Assert.Null(fleet.Bodies[0].Orbit);
            Assert.Null(fleet.Vessels[1].SpaceObject);
            Assert.Equal(SpaceObjectKind.Comet, fleet.Vessels[2].SpaceObject!.Kind);
            Assert.Equal(SizeClass.D, fleet.Vessels[2].SpaceObject!.SizeClass);
        }

        [Fact]
        public void PongRoundTrips()
        {
            Assert.Equal(12345u, RoundTrip(new Pong(12345u)).Nonce);
        }

        [Fact]
        public void ByeRoundTripsEveryReason()
        {
            foreach (ByeReason reason in Enum.GetValues(typeof(ByeReason)))
            {
                Assert.Equal(reason, RoundTrip(new Bye(reason)).Reason);
            }
        }

        [Fact]
        public void ClientHelloRoundTrips()
        {
            Assert.Equal("fltcom-display", RoundTripClient(new ClientHello("fltcom-display")).Client);
        }

        [Fact]
        public void PingRoundTrips()
        {
            Assert.Equal(7u, RoundTripClient(new Ping(7u)).Nonce);
        }

        [Fact]
        public void ReadsWhatTheDisplaySends()
        {
            ClientMessage message = ProtocolJson.ReadClientMessage("{\"protocolVersion\":" + ProtocolInfo.Version + ",\"type\":\"ping\",\"nonce\":9}");

            Assert.Equal(9u, message.Match(hello => 0u, ping => ping.Nonce, brief => 0u, approve => 0u, scrub => 0u, resume => 0u, answer => 0u, groupEdit => 0u));
        }

        [Fact]
        public void RejectsAnotherProtocolVersionBeforeTheShape()
        {
            var error = Assert.Throws<ProtocolVersionException>(() => ProtocolJson.ReadClientMessage("{\"protocolVersion\":999,\"type\":\"teleport\"}"));

            Assert.Equal(999, error.Received);
        }

        [Theory]
        [InlineData("{\"protocolVersion\":14,\"type\":\"ping\"}", "$.nonce: missing")]
        [InlineData("{\"protocolVersion\":14,\"type\":\"ping\",\"nonce\":-1}", "$.nonce: expected uint32")]
        [InlineData("{\"protocolVersion\":14,\"type\":\"ping\",\"nonce\":1,\"extra\":true}", "$.extra: no such field")]
        [InlineData("{\"protocolVersion\":14,\"type\":\"teleport\"}", "$.type: unknown value 'teleport'")]
        [InlineData("{\"protocolVersion\":14,\"type\":\"ping\",\"nonce\":1", "invalid JSON")]
        public void RejectsMalformedMessagesWithAPath(string json, string expected)
        {
            var error = Assert.Throws<ProtocolException>(() => ProtocolJson.ReadClientMessage(json));

            Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void RefusesToBuildANonFiniteOrbit()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Orbit("Kerbin", double.NaN, 0, 0, 0, 0, 0));
        }
    }
}

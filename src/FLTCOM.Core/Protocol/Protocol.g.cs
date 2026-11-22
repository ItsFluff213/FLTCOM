#nullable enable
using System;
using System.Collections.Generic;

namespace FLTCOM.Core.Protocol
{

    public static class ProtocolInfo
    {
        public const int Version = 14;
        public const int MaxDepth = 64;
    }

    /// <summary>Messages the server sends to the display.</summary>
    public abstract class ServerMessage
    {
        private protected ServerMessage()
        {
        }

        public abstract T Match<T>(Func<ServerHello, T> hello, Func<Fleet, T> fleet, Func<CraftLayout, T> layout, Func<Pong, T> pong, Func<Bye, T> bye, Func<BriefingMessage, T> briefing, Func<Missions, T> missions, Func<GroupRoster, T> groups);

        internal abstract void Write(JsonWriter w);

        internal static ServerMessage Read(JsonNode node, string path)
        {
            string tag = node.Field(path, "type").AsString(path + ".type");
            switch (tag)
            {
                case "hello":
                    return ServerHello.Read(node, path);
                case "fleet":
                    return Fleet.Read(node, path);
                case "layout":
                    return CraftLayout.Read(node, path);
                case "pong":
                    return Pong.Read(node, path);
                case "bye":
                    return Bye.Read(node, path);
                case "briefing":
                    return BriefingMessage.Read(node, path);
                case "missions":
                    return Missions.Read(node, path);
                case "groups":
                    return GroupRoster.Read(node, path);
                default:
                    throw new ProtocolException(path + ".type: unknown value '" + tag + "', expected one of hello, fleet, layout, pong, bye, briefing, missions, groups");
            }
        }
    }

    /// <summary>Messages the display sends to the server.</summary>
    public abstract class ClientMessage
    {
        private protected ClientMessage()
        {
        }

        public abstract T Match<T>(Func<ClientHello, T> hello, Func<Ping, T> ping, Func<BriefRequest, T> brief, Func<Approve, T> approve, Func<Scrub, T> scrub, Func<Resume, T> resume, Func<Answer, T> answer, Func<GroupEdit, T> groupEdit);

        internal abstract void Write(JsonWriter w);

        internal static ClientMessage Read(JsonNode node, string path)
        {
            string tag = node.Field(path, "type").AsString(path + ".type");
            switch (tag)
            {
                case "hello":
                    return ClientHello.Read(node, path);
                case "ping":
                    return Ping.Read(node, path);
                case "brief":
                    return BriefRequest.Read(node, path);
                case "approve":
                    return Approve.Read(node, path);
                case "scrub":
                    return Scrub.Read(node, path);
                case "resume":
                    return Resume.Read(node, path);
                case "answer":
                    return Answer.Read(node, path);
                case "groupEdit":
                    return GroupEdit.Read(node, path);
                default:
                    throw new ProtocolException(path + ".type: unknown value '" + tag + "', expected one of hello, ping, brief, approve, scrub, resume, answer, groupEdit");
            }
        }
    }

    /// <summary>First message on every connection.</summary>
    public sealed class ServerHello : ServerMessage
    {
        private static readonly string[] Fields = { "protocolVersion", "type", "server", "saveName", "snapshotInterval" };

        public ServerHello(string server, string? saveName, double snapshotInterval)
        {
            Server = server ?? throw new ArgumentNullException("server");
            SaveName = saveName;
            SnapshotInterval = Require.Finite(snapshotInterval, "snapshotInterval");
        }

        /// <summary>Server name and version, such as FLTCOM 0.1.0.</summary>
        public string Server { get; }

        /// <summary>The loaded save, null at the main menu.</summary>
        public string? SaveName { get; }

        /// <summary>Real time between fleet snapshots while a save is loaded. The display calls its fleet stale when several intervals pass without one. Unit: s.</summary>
        public double SnapshotInterval { get; }

        public override T Match<T>(Func<ServerHello, T> hello, Func<Fleet, T> fleet, Func<CraftLayout, T> layout, Func<Pong, T> pong, Func<Bye, T> bye, Func<BriefingMessage, T> briefing, Func<Missions, T> missions, Func<GroupRoster, T> groups) => (hello ?? throw new ArgumentNullException(nameof(hello)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);
            w.Name("type"); w.String("hello");
            w.Name("server"); w.String(Server);
            w.Name("saveName"); if (SaveName is null) { w.Null(); } else { w.String(SaveName); }
            w.Name("snapshotInterval"); w.Number(SnapshotInterval);
            w.EndObject();
        }

        internal static new ServerHello Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new ServerHello(
                node.Field(path, "server").AsString(path + ".server"),
                node.Field(path, "saveName").Kind == JsonKind.Null ? null : node.Field(path, "saveName").AsString(path + ".saveName"),
                node.Field(path, "snapshotInterval").AsNumber(path + ".snapshotInterval"));
        }
    }

    /// <summary>A complete snapshot of the game at one universal time. The display replaces its state with each one; there are no deltas.</summary>
    public sealed class Fleet : ServerMessage
    {
        private static readonly string[] Fields = { "protocolVersion", "type", "seq", "ut", "warpRate", "activeVesselId", "bodies", "vessels", "markers" };

        public Fleet(uint seq, double ut, double warpRate, uint? activeVesselId, IReadOnlyList<Body> bodies, IReadOnlyList<Vessel> vessels, MarkerList? markers)
        {
            Seq = seq;
            Ut = Require.Finite(ut, "ut");
            WarpRate = Require.Finite(warpRate, "warpRate");
            ActiveVesselId = activeVesselId;
            Bodies = Require.UniqueBy(Require.Items(bodies, "bodies"), item => item.Name, "bodies");
            Vessels = Require.UniqueBy(Require.Items(vessels, "vessels"), item => item.Id, "vessels");
            Markers = markers;
        }

        /// <summary>Counts up by one per snapshot for the life of the server.</summary>
        public uint Seq { get; }

        /// <summary>Universal time of the snapshot. Every orbit's mean anomaly is given at this time. Unit: s.</summary>
        public double Ut { get; }

        /// <summary>Time warp multiplier, 1 at normal speed.</summary>
        public double WarpRate { get; }

        /// <summary>The vessel the player flies, null outside flight.</summary>
        public uint? ActiveVesselId { get; }

        public IReadOnlyList<Body> Bodies { get; }

        public IReadOnlyList<Vessel> Vessels { get; }

        /// <summary>The game's own surface markers on every body. Sent when they change and again every few seconds; null in between, when the display keeps the last list it got.</summary>
        public MarkerList? Markers { get; }

        public override T Match<T>(Func<ServerHello, T> hello, Func<Fleet, T> fleet, Func<CraftLayout, T> layout, Func<Pong, T> pong, Func<Bye, T> bye, Func<BriefingMessage, T> briefing, Func<Missions, T> missions, Func<GroupRoster, T> groups) => (fleet ?? throw new ArgumentNullException(nameof(fleet)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);
            w.Name("type"); w.String("fleet");
            w.Name("seq"); w.Number((long)Seq);
            w.Name("ut"); w.Number(Ut);
            w.Name("warpRate"); w.Number(WarpRate);
            w.Name("activeVesselId"); if (ActiveVesselId is null) { w.Null(); } else { w.Number((long)ActiveVesselId.Value); }
            w.Name("bodies"); w.BeginArray(); foreach (Body item0 in Bodies) { item0.Write(w); } w.EndArray();
            w.Name("vessels"); w.BeginArray(); foreach (Vessel item0 in Vessels) { item0.Write(w); } w.EndArray();
            w.Name("markers"); if (Markers is null) { w.Null(); } else { Markers.Write(w); }
            w.EndObject();
        }

        internal static new Fleet Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Fleet(
                node.Field(path, "seq").AsUInt(path + ".seq"),
                node.Field(path, "ut").AsNumber(path + ".ut"),
                node.Field(path, "warpRate").AsNumber(path + ".warpRate"),
                node.Field(path, "activeVesselId").Kind == JsonKind.Null ? null : (uint?)node.Field(path, "activeVesselId").AsUInt(path + ".activeVesselId"),
                JsonNode.UniqueBy(node.Field(path, "bodies").AsArray(path + ".bodies", (n0, i0) => Body.Read(n0, path + ".bodies" + "[" + i0 + "]")), item => item.Name, path + ".bodies", "name"),
                JsonNode.UniqueBy(node.Field(path, "vessels").AsArray(path + ".vessels", (n0, i0) => Vessel.Read(n0, path + ".vessels" + "[" + i0 + "]")), item => item.Id, path + ".vessels", "id"),
                node.Field(path, "markers").Kind == JsonKind.Null ? null : MarkerList.Read(node.Field(path, "markers"), path + ".markers"));
        }
    }

    /// <summary>Answer to a ping, echoing its nonce.</summary>
    public sealed class Pong : ServerMessage
    {
        private static readonly string[] Fields = { "protocolVersion", "type", "nonce" };

        public Pong(uint nonce)
        {
            Nonce = nonce;
        }

        public uint Nonce { get; }

        public override T Match<T>(Func<ServerHello, T> hello, Func<Fleet, T> fleet, Func<CraftLayout, T> layout, Func<Pong, T> pong, Func<Bye, T> bye, Func<BriefingMessage, T> briefing, Func<Missions, T> missions, Func<GroupRoster, T> groups) => (pong ?? throw new ArgumentNullException(nameof(pong)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);
            w.Name("type"); w.String("pong");
            w.Name("nonce"); w.Number((long)Nonce);
            w.EndObject();
        }

        internal static new Pong Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Pong(
                node.Field(path, "nonce").AsUInt(path + ".nonce"));
        }
    }

    /// <summary>Sent before the server closes the connection. The display owns the words it shows for each reason.</summary>
    public sealed class Bye : ServerMessage
    {
        private static readonly string[] Fields = { "protocolVersion", "type", "reason" };

        public Bye(ByeReason reason)
        {
            Reason = Require.Defined(reason, "reason");
        }

        public ByeReason Reason { get; }

        public override T Match<T>(Func<ServerHello, T> hello, Func<Fleet, T> fleet, Func<CraftLayout, T> layout, Func<Pong, T> pong, Func<Bye, T> bye, Func<BriefingMessage, T> briefing, Func<Missions, T> missions, Func<GroupRoster, T> groups) => (bye ?? throw new ArgumentNullException(nameof(bye)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);
            w.Name("type"); w.String("bye");
            w.Name("reason"); w.String(Wire.Name(Reason));
            w.EndObject();
        }

        internal static new Bye Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Bye(
                Wire.ReadByeReason(node.Field(path, "reason").AsString(path + ".reason"), path + ".reason"));
        }
    }

    /// <summary>First message the display sends on every connection.</summary>
    public sealed class ClientHello : ClientMessage
    {
        private static readonly string[] Fields = { "protocolVersion", "type", "client" };

        public ClientHello(string client)
        {
            Client = client ?? throw new ArgumentNullException("client");
        }

        /// <summary>Client name, such as fltcom-display.</summary>
        public string Client { get; }

        public override T Match<T>(Func<ClientHello, T> hello, Func<Ping, T> ping, Func<BriefRequest, T> brief, Func<Approve, T> approve, Func<Scrub, T> scrub, Func<Resume, T> resume, Func<Answer, T> answer, Func<GroupEdit, T> groupEdit) => (hello ?? throw new ArgumentNullException(nameof(hello)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);
            w.Name("type"); w.String("hello");
            w.Name("client"); w.String(Client);
            w.EndObject();
        }

        internal static new ClientHello Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new ClientHello(
                node.Field(path, "client").AsString(path + ".client"));
        }
    }

    public sealed class Ping : ClientMessage
    {
        private static readonly string[] Fields = { "protocolVersion", "type", "nonce" };

        public Ping(uint nonce)
        {
            Nonce = nonce;
        }

        public uint Nonce { get; }

        public override T Match<T>(Func<ClientHello, T> hello, Func<Ping, T> ping, Func<BriefRequest, T> brief, Func<Approve, T> approve, Func<Scrub, T> scrub, Func<Resume, T> resume, Func<Answer, T> answer, Func<GroupEdit, T> groupEdit) => (ping ?? throw new ArgumentNullException(nameof(ping)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);
            w.Name("type"); w.String("ping");
            w.Name("nonce"); w.Number((long)Nonce);
            w.EndObject();
        }

        internal static new Ping Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Ping(
                node.Field(path, "nonce").AsUInt(path + ".nonce"));
        }
    }

    /// <summary>shutdown: the game is closing or the save unloads. protocolMismatch: the client spoke another protocol version. The rest refuse the link because FLTCOM cannot run in that game.</summary>
    public enum ByeReason
    {
        Shutdown,
        ProtocolMismatch,
        MultiplayerSession,
        UnsupportedPhysics,
        UnsupportedScale,
    }

    /// <summary>A celestial body. Its parent is orbit.referenceBody; the root star has no orbit.</summary>
    public sealed class Body
    {
        private static readonly string[] Fields = { "name", "orbit", "radius", "gravParameter", "sphereOfInfluence", "atmosphereHeight", "rotationPeriod", "rotationAngle", "highestTerrainAltitude", "color", "texture" };

        public Body(string name, Orbit? orbit, double radius, double gravParameter, double? sphereOfInfluence, double atmosphereHeight, double rotationPeriod, double rotationAngle, double highestTerrainAltitude, string color, string? texture)
        {
            Name = name ?? throw new ArgumentNullException("name");
            Orbit = orbit;
            Radius = Require.Finite(radius, "radius");
            GravParameter = Require.Finite(gravParameter, "gravParameter");
            SphereOfInfluence = sphereOfInfluence is double sphereOfInfluenceValue ? Require.Finite(sphereOfInfluenceValue, "sphereOfInfluence") : (double?)null;
            AtmosphereHeight = Require.Finite(atmosphereHeight, "atmosphereHeight");
            RotationPeriod = Require.Finite(rotationPeriod, "rotationPeriod");
            RotationAngle = Require.Finite(rotationAngle, "rotationAngle");
            HighestTerrainAltitude = Require.Finite(highestTerrainAltitude, "highestTerrainAltitude");
            Color = color ?? throw new ArgumentNullException("color");
            Texture = texture;
        }

        /// <summary>Unique body name, the key other messages refer to.</summary>
        public string Name { get; }

        public Orbit? Orbit { get; }

        /// <summary>Unit: m.</summary>
        public double Radius { get; }

        /// <summary>Unit: m3/s2.</summary>
        public double GravParameter { get; }

        /// <summary>Null for the root star, whose sphere is unbounded. Unit: m.</summary>
        public double? SphereOfInfluence { get; }

        /// <summary>0 when the body has no atmosphere. Unit: m.</summary>
        public double AtmosphereHeight { get; }

        /// <summary>Unit: s.</summary>
        public double RotationPeriod { get; }

        /// <summary>How far the body has turned at the snapshot's ut: the angle from the reference direction to longitude 0, in [0, 2π). Unit: rad.</summary>
        public double RotationAngle { get; }

        /// <summary>Highest terrain above sea level, 0 for a body without solid terrain. Unit: m.</summary>
        public double HighestTerrainAltitude { get; }

        /// <summary>Orbit color as #rrggbb.</summary>
        public string Color { get; }

        /// <summary>Server path of an equirectangular PNG of the body's surface captured from the running game, such as /art/body/Kerbin.png. Null until captured or when the capture failed; the display then draws a grey sphere.</summary>
        public string? Texture { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("name"); w.String(Name);
            w.Name("orbit"); if (Orbit is null) { w.Null(); } else { Orbit.Write(w); }
            w.Name("radius"); w.Number(Radius);
            w.Name("gravParameter"); w.Number(GravParameter);
            w.Name("sphereOfInfluence"); if (SphereOfInfluence is null) { w.Null(); } else { w.Number(SphereOfInfluence.Value); }
            w.Name("atmosphereHeight"); w.Number(AtmosphereHeight);
            w.Name("rotationPeriod"); w.Number(RotationPeriod);
            w.Name("rotationAngle"); w.Number(RotationAngle);
            w.Name("highestTerrainAltitude"); w.Number(HighestTerrainAltitude);
            w.Name("color"); w.String(Color);
            w.Name("texture"); if (Texture is null) { w.Null(); } else { w.String(Texture); }
            w.EndObject();
        }

        internal static Body Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Body(
                node.Field(path, "name").AsString(path + ".name"),
                node.Field(path, "orbit").Kind == JsonKind.Null ? null : Orbit.Read(node.Field(path, "orbit"), path + ".orbit"),
                node.Field(path, "radius").AsNumber(path + ".radius"),
                node.Field(path, "gravParameter").AsNumber(path + ".gravParameter"),
                node.Field(path, "sphereOfInfluence").Kind == JsonKind.Null ? null : (double?)node.Field(path, "sphereOfInfluence").AsNumber(path + ".sphereOfInfluence"),
                node.Field(path, "atmosphereHeight").AsNumber(path + ".atmosphereHeight"),
                node.Field(path, "rotationPeriod").AsNumber(path + ".rotationPeriod"),
                node.Field(path, "rotationAngle").AsNumber(path + ".rotationAngle"),
                node.Field(path, "highestTerrainAltitude").AsNumber(path + ".highestTerrainAltitude"),
                node.Field(path, "color").AsString(path + ".color"),
                node.Field(path, "texture").Kind == JsonKind.Null ? null : node.Field(path, "texture").AsString(path + ".texture"));
        }
    }

    /// <summary>Keplerian elements at the snapshot's universal time. Apoapsis, periapsis and period derive from these and the reference body.</summary>
    public sealed class Orbit
    {
        private static readonly string[] Fields = { "referenceBody", "semiMajorAxis", "eccentricity", "inclination", "longitudeOfAscendingNode", "argumentOfPeriapsis", "meanAnomaly" };

        public Orbit(string referenceBody, double semiMajorAxis, double eccentricity, double inclination, double longitudeOfAscendingNode, double argumentOfPeriapsis, double meanAnomaly)
        {
            ReferenceBody = referenceBody ?? throw new ArgumentNullException("referenceBody");
            SemiMajorAxis = Require.Finite(semiMajorAxis, "semiMajorAxis");
            Eccentricity = Require.Finite(eccentricity, "eccentricity");
            Inclination = Require.Finite(inclination, "inclination");
            LongitudeOfAscendingNode = Require.Finite(longitudeOfAscendingNode, "longitudeOfAscendingNode");
            ArgumentOfPeriapsis = Require.Finite(argumentOfPeriapsis, "argumentOfPeriapsis");
            MeanAnomaly = Require.Finite(meanAnomaly, "meanAnomaly");
        }

        /// <summary>Name of the body this orbit is around.</summary>
        public string ReferenceBody { get; }

        /// <summary>Negative on a hyperbolic orbit. Unit: m.</summary>
        public double SemiMajorAxis { get; }

        public double Eccentricity { get; }

        /// <summary>Unit: rad.</summary>
        public double Inclination { get; }

        /// <summary>Unit: rad.</summary>
        public double LongitudeOfAscendingNode { get; }

        /// <summary>Unit: rad.</summary>
        public double ArgumentOfPeriapsis { get; }

        /// <summary>Unit: rad.</summary>
        public double MeanAnomaly { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("referenceBody"); w.String(ReferenceBody);
            w.Name("semiMajorAxis"); w.Number(SemiMajorAxis);
            w.Name("eccentricity"); w.Number(Eccentricity);
            w.Name("inclination"); w.Number(Inclination);
            w.Name("longitudeOfAscendingNode"); w.Number(LongitudeOfAscendingNode);
            w.Name("argumentOfPeriapsis"); w.Number(ArgumentOfPeriapsis);
            w.Name("meanAnomaly"); w.Number(MeanAnomaly);
            w.EndObject();
        }

        internal static Orbit Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Orbit(
                node.Field(path, "referenceBody").AsString(path + ".referenceBody"),
                node.Field(path, "semiMajorAxis").AsNumber(path + ".semiMajorAxis"),
                node.Field(path, "eccentricity").AsNumber(path + ".eccentricity"),
                node.Field(path, "inclination").AsNumber(path + ".inclination"),
                node.Field(path, "longitudeOfAscendingNode").AsNumber(path + ".longitudeOfAscendingNode"),
                node.Field(path, "argumentOfPeriapsis").AsNumber(path + ".argumentOfPeriapsis"),
                node.Field(path, "meanAnomaly").AsNumber(path + ".meanAnomaly"));
        }
    }

    public sealed class Vessel
    {
        private static readonly string[] Fields = { "id", "registryCode", "name", "vesselType", "location", "loaded", "controllable", "comms", "signalStrength", "crewCapacity", "crew", "resources", "mass", "deltaV", "attitude", "docking", "spaceObject" };

        public Vessel(uint id, string registryCode, string name, VesselType vesselType, Location location, bool loaded, bool controllable, CommsState comms, double? signalStrength, int crewCapacity, IReadOnlyList<CrewMember> crew, IReadOnlyList<Resource> resources, double mass, DeltaV? deltaV, Quat? attitude, Docking? docking, SpaceObject? spaceObject)
        {
            Id = id;
            RegistryCode = registryCode ?? throw new ArgumentNullException("registryCode");
            Name = name ?? throw new ArgumentNullException("name");
            VesselType = Require.Defined(vesselType, "vesselType");
            Location = location ?? throw new ArgumentNullException("location");
            Loaded = loaded;
            Controllable = controllable;
            Comms = Require.Defined(comms, "comms");
            SignalStrength = signalStrength is double signalStrengthValue ? Require.Finite(signalStrengthValue, "signalStrength") : (double?)null;
            CrewCapacity = crewCapacity;
            Crew = Require.Items(crew, "crew");
            Resources = Require.Items(resources, "resources");
            Mass = Require.Finite(mass, "mass");
            DeltaV = deltaV;
            Attitude = attitude;
            Docking = docking;
            SpaceObject = spaceObject;
        }

        /// <summary>KSP persistentId, unique within a fleet snapshot.</summary>
        public uint Id { get; }

        /// <summary>Fleet registry code, such as FLT-0042-PRB.</summary>
        public string RegistryCode { get; }

        public string Name { get; }

        public VesselType VesselType { get; }

        public Location Location { get; }

        /// <summary>KSP Vessel.loaded: its parts are in the scene. A loaded vessel can still be on rails.</summary>
        public bool Loaded { get; }

        /// <summary>KSP Vessel.IsControllable: it accepts control input now.</summary>
        public bool Controllable { get; }

        public CommsState Comms { get; }

        /// <summary>0 to 1 while comms is link, otherwise null.</summary>
        public double? SignalStrength { get; }

        public int CrewCapacity { get; }

        public IReadOnlyList<CrewMember> Crew { get; }

        /// <summary>Vessel totals per resource.</summary>
        public IReadOnlyList<Resource> Resources { get; }

        /// <summary>Unit: kg.</summary>
        public double Mass { get; }

        /// <summary>Null when the vessel has no engines or no estimate exists.</summary>
        public DeltaV? DeltaV { get; }

        /// <summary>Rotation from the craft frame to the orbit frame. Craft frame: CraftPart coordinates with y and z swapped (x right, y forward, z up the craft's axis). Orbit frame: the frame of the orbital elements, z toward the north pole, x where a body with rotationAngle 0 has longitude 0. Null when unknown.</summary>
        public Quat? Attitude { get; }

        /// <summary>The vessel's docking ports. Null when it has none or they are unknown.</summary>
        public Docking? Docking { get; }

        /// <summary>What kind of rock an asteroid or comet is. Null for every vessel that is not a spaceObject.</summary>
        public SpaceObject? SpaceObject { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("id"); w.Number((long)Id);
            w.Name("registryCode"); w.String(RegistryCode);
            w.Name("name"); w.String(Name);
            w.Name("vesselType"); w.String(Wire.Name(VesselType));
            w.Name("location"); Location.Write(w);
            w.Name("loaded"); w.Bool(Loaded);
            w.Name("controllable"); w.Bool(Controllable);
            w.Name("comms"); w.String(Wire.Name(Comms));
            w.Name("signalStrength"); if (SignalStrength is null) { w.Null(); } else { w.Number(SignalStrength.Value); }
            w.Name("crewCapacity"); w.Number((long)CrewCapacity);
            w.Name("crew"); w.BeginArray(); foreach (CrewMember item0 in Crew) { item0.Write(w); } w.EndArray();
            w.Name("resources"); w.BeginArray(); foreach (Resource item0 in Resources) { item0.Write(w); } w.EndArray();
            w.Name("mass"); w.Number(Mass);
            w.Name("deltaV"); if (DeltaV is null) { w.Null(); } else { DeltaV.Write(w); }
            w.Name("attitude"); if (Attitude is null) { w.Null(); } else { Attitude.Write(w); }
            w.Name("docking"); if (Docking is null) { w.Null(); } else { Docking.Write(w); }
            w.Name("spaceObject"); if (SpaceObject is null) { w.Null(); } else { SpaceObject.Write(w); }
            w.EndObject();
        }

        internal static Vessel Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Vessel(
                node.Field(path, "id").AsUInt(path + ".id"),
                node.Field(path, "registryCode").AsString(path + ".registryCode"),
                node.Field(path, "name").AsString(path + ".name"),
                Wire.ReadVesselType(node.Field(path, "vesselType").AsString(path + ".vesselType"), path + ".vesselType"),
                Location.Read(node.Field(path, "location"), path + ".location"),
                node.Field(path, "loaded").AsBool(path + ".loaded"),
                node.Field(path, "controllable").AsBool(path + ".controllable"),
                Wire.ReadCommsState(node.Field(path, "comms").AsString(path + ".comms"), path + ".comms"),
                node.Field(path, "signalStrength").Kind == JsonKind.Null ? null : (double?)node.Field(path, "signalStrength").AsNumber(path + ".signalStrength"),
                node.Field(path, "crewCapacity").AsInt(path + ".crewCapacity"),
                node.Field(path, "crew").AsArray(path + ".crew", (n0, i0) => CrewMember.Read(n0, path + ".crew" + "[" + i0 + "]")),
                node.Field(path, "resources").AsArray(path + ".resources", (n0, i0) => Resource.Read(n0, path + ".resources" + "[" + i0 + "]")),
                node.Field(path, "mass").AsNumber(path + ".mass"),
                node.Field(path, "deltaV").Kind == JsonKind.Null ? null : DeltaV.Read(node.Field(path, "deltaV"), path + ".deltaV"),
                node.Field(path, "attitude").Kind == JsonKind.Null ? null : Quat.Read(node.Field(path, "attitude"), path + ".attitude"),
                node.Field(path, "docking").Kind == JsonKind.Null ? null : Docking.Read(node.Field(path, "docking"), path + ".docking"),
                node.Field(path, "spaceObject").Kind == JsonKind.Null ? null : SpaceObject.Read(node.Field(path, "spaceObject"), path + ".spaceObject"));
        }
    }

    public sealed class SpaceObject
    {
        private static readonly string[] Fields = { "kind", "sizeClass" };

        public SpaceObject(SpaceObjectKind kind, SizeClass? sizeClass)
        {
            Kind = Require.Defined(kind, "kind");
            SizeClass = sizeClass;
        }

        public SpaceObjectKind Kind { get; }

        /// <summary>Null when the save does not say.</summary>
        public SizeClass? SizeClass { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("kind"); w.String(Wire.Name(Kind));
            w.Name("sizeClass"); if (SizeClass is null) { w.Null(); } else { w.String(Wire.Name(SizeClass.Value)); }
            w.EndObject();
        }

        internal static SpaceObject Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new SpaceObject(
                Wire.ReadSpaceObjectKind(node.Field(path, "kind").AsString(path + ".kind"), path + ".kind"),
                node.Field(path, "sizeClass").Kind == JsonKind.Null ? null : (SizeClass?)Wire.ReadSizeClass(node.Field(path, "sizeClass").AsString(path + ".sizeClass"), path + ".sizeClass"));
        }
    }

    /// <summary>comet: the rock carries KSP's ModuleComet. asteroid: any other space object.</summary>
    public enum SpaceObjectKind
    {
        Asteroid,
        Comet,
    }

    /// <summary>KSP's size class of an asteroid or comet (DiscoveryInfo.objectSize), lower case on the wire, a the smallest. Asteroids come as a to e; comets draw from the same letters on their own range.</summary>
    public enum SizeClass
    {
        A,
        B,
        C,
        D,
        E,
        F,
        G,
        H,
        I,
    }

    /// <summary>The stock KSP vessel types.</summary>
    public enum VesselType
    {
        Debris,
        SpaceObject,
        Unknown,
        Probe,
        Relay,
        Rover,
        Lander,
        Ship,
        Plane,
        Station,
        Base,
        Eva,
        Flag,
        DeployedScienceController,
        DeployedSciencePart,
        DroppedPart,
        DeployedGroundPart,
    }

    /// <summary>Where a vessel is: on an orbit, or resting on a surface.</summary>
    public abstract class Location
    {
        private protected Location()
        {
        }

        public abstract T Match<T>(Func<OrbitalLocation, T> orbit, Func<SurfaceLocation, T> surface);

        internal abstract void Write(JsonWriter w);

        internal static Location Read(JsonNode node, string path)
        {
            string tag = node.Field(path, "kind").AsString(path + ".kind");
            switch (tag)
            {
                case "orbit":
                    return OrbitalLocation.Read(node, path);
                case "surface":
                    return SurfaceLocation.Read(node, path);
                default:
                    throw new ProtocolException(path + ".kind: unknown value '" + tag + "', expected one of orbit, surface");
            }
        }
    }

    public sealed class OrbitalLocation : Location
    {
        private static readonly string[] Fields = { "kind", "situation", "orbit" };

        public OrbitalLocation(OrbitalSituation situation, Orbit orbit)
        {
            Situation = Require.Defined(situation, "situation");
            Orbit = orbit ?? throw new ArgumentNullException("orbit");
        }

        public OrbitalSituation Situation { get; }

        public Orbit Orbit { get; }

        public override T Match<T>(Func<OrbitalLocation, T> orbit, Func<SurfaceLocation, T> surface) => (orbit ?? throw new ArgumentNullException(nameof(orbit)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("kind"); w.String("orbit");
            w.Name("situation"); w.String(Wire.Name(Situation));
            w.Name("orbit"); Orbit.Write(w);
            w.EndObject();
        }

        internal static new OrbitalLocation Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new OrbitalLocation(
                Wire.ReadOrbitalSituation(node.Field(path, "situation").AsString(path + ".situation"), path + ".situation"),
                Orbit.Read(node.Field(path, "orbit"), path + ".orbit"));
        }
    }

    public sealed class SurfaceLocation : Location
    {
        private static readonly string[] Fields = { "kind", "situation", "body", "latitude", "longitude", "altitude" };

        public SurfaceLocation(SurfaceSituation situation, string body, double latitude, double longitude, double altitude)
        {
            Situation = Require.Defined(situation, "situation");
            Body = body ?? throw new ArgumentNullException("body");
            Latitude = Require.Finite(latitude, "latitude");
            Longitude = Require.Finite(longitude, "longitude");
            Altitude = Require.Finite(altitude, "altitude");
        }

        public SurfaceSituation Situation { get; }

        /// <summary>Name of the body the vessel rests on.</summary>
        public string Body { get; }

        /// <summary>Unit: rad.</summary>
        public double Latitude { get; }

        /// <summary>Unit: rad.</summary>
        public double Longitude { get; }

        /// <summary>Above sea level. Unit: m.</summary>
        public double Altitude { get; }

        public override T Match<T>(Func<OrbitalLocation, T> orbit, Func<SurfaceLocation, T> surface) => (surface ?? throw new ArgumentNullException(nameof(surface)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("kind"); w.String("surface");
            w.Name("situation"); w.String(Wire.Name(Situation));
            w.Name("body"); w.String(Body);
            w.Name("latitude"); w.Number(Latitude);
            w.Name("longitude"); w.Number(Longitude);
            w.Name("altitude"); w.Number(Altitude);
            w.EndObject();
        }

        internal static new SurfaceLocation Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new SurfaceLocation(
                Wire.ReadSurfaceSituation(node.Field(path, "situation").AsString(path + ".situation"), path + ".situation"),
                node.Field(path, "body").AsString(path + ".body"),
                node.Field(path, "latitude").AsNumber(path + ".latitude"),
                node.Field(path, "longitude").AsNumber(path + ".longitude"),
                node.Field(path, "altitude").AsNumber(path + ".altitude"));
        }
    }

    public enum OrbitalSituation
    {
        Flying,
        SubOrbital,
        Orbiting,
        Escaping,
    }

    public enum SurfaceSituation
    {
        Prelaunch,
        Landed,
        Splashed,
    }

    /// <summary>link: connected to the network. los: CommNet is on and the vessel has no path home. none: the vessel cannot communicate (debris, flags). off: CommNet is disabled in this save.</summary>
    public enum CommsState
    {
        Link,
        Los,
        None,
        Off,
    }

    public sealed class Docking
    {
        private static readonly string[] Fields = { "ports" };

        public Docking(IReadOnlyList<DockingPortInfo> ports)
        {
            Ports = Require.Items(ports, "ports");
        }

        public IReadOnlyList<DockingPortInfo> Ports { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("ports"); w.BeginArray(); foreach (DockingPortInfo item0 in Ports) { item0.Write(w); } w.EndArray();
            w.EndObject();
        }

        internal static Docking Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Docking(
                node.Field(path, "ports").AsArray(path + ".ports", (n0, i0) => DockingPortInfo.Read(n0, path + ".ports" + "[" + i0 + "]")));
        }
    }

    public sealed class DockingPortInfo
    {
        private static readonly string[] Fields = { "id", "name", "size", "free" };

        public DockingPortInfo(uint id, string name, string size, bool free)
        {
            Id = id;
            Name = name ?? throw new ArgumentNullException("name");
            Size = size ?? throw new ArgumentNullException("size");
            Free = free;
        }

        /// <summary>The port part's persistentId.</summary>
        public uint Id { get; }

        /// <summary>The part's title, such as Clamp-O-Tron Docking Port.</summary>
        public string Name { get; }

        /// <summary>The stock node type, such as size1. Only equal sizes dock.</summary>
        public string Size { get; }

        /// <summary>Ready to dock: not docked, not disabled, not behind a closed shield.</summary>
        public bool Free { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("id"); w.Number((long)Id);
            w.Name("name"); w.String(Name);
            w.Name("size"); w.String(Size);
            w.Name("free"); w.Bool(Free);
            w.EndObject();
        }

        internal static DockingPortInfo Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new DockingPortInfo(
                node.Field(path, "id").AsUInt(path + ".id"),
                node.Field(path, "name").AsString(path + ".name"),
                node.Field(path, "size").AsString(path + ".size"),
                node.Field(path, "free").AsBool(path + ".free"));
        }
    }

    public sealed class CrewMember
    {
        private static readonly string[] Fields = { "name", "trait", "level" };

        public CrewMember(string name, string trait, int level)
        {
            Name = name ?? throw new ArgumentNullException("name");
            Trait = trait ?? throw new ArgumentNullException("trait");
            Level = level;
        }

        public string Name { get; }

        /// <summary>Pilot, Engineer, Scientist, Tourist, or a trait a mod adds.</summary>
        public string Trait { get; }

        public int Level { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("name"); w.String(Name);
            w.Name("trait"); w.String(Trait);
            w.Name("level"); w.Number((long)Level);
            w.EndObject();
        }

        internal static CrewMember Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new CrewMember(
                node.Field(path, "name").AsString(path + ".name"),
                node.Field(path, "trait").AsString(path + ".trait"),
                node.Field(path, "level").AsInt(path + ".level"));
        }
    }

    public sealed class Resource
    {
        private static readonly string[] Fields = { "name", "amount", "capacity" };

        public Resource(string name, double amount, double capacity)
        {
            Name = name ?? throw new ArgumentNullException("name");
            Amount = Require.Finite(amount, "amount");
            Capacity = Require.Finite(capacity, "capacity");
        }

        public string Name { get; }

        /// <summary>Unit: u.</summary>
        public double Amount { get; }

        /// <summary>Unit: u.</summary>
        public double Capacity { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("name"); w.String(Name);
            w.Name("amount"); w.Number(Amount);
            w.Name("capacity"); w.Number(Capacity);
            w.EndObject();
        }

        internal static Resource Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Resource(
                node.Field(path, "name").AsString(path + ".name"),
                node.Field(path, "amount").AsNumber(path + ".amount"),
                node.Field(path, "capacity").AsNumber(path + ".capacity"));
        }
    }

    public sealed class DeltaV
    {
        private static readonly string[] Fields = { "total", "source", "stages" };

        public DeltaV(double total, DeltaVSource source, IReadOnlyList<StageDeltaV> stages)
        {
            Total = Require.Finite(total, "total");
            Source = Require.Defined(source, "source");
            Stages = Require.Items(stages, "stages");
        }

        /// <summary>Vacuum delta-v of all stages. Unit: m/s.</summary>
        public double Total { get; }

        public DeltaVSource Source { get; }

        /// <summary>Per-stage figures, highest stage index first (the stage that fires first). Empty when no per-stage estimate exists.</summary>
        public IReadOnlyList<StageDeltaV> Stages { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("total"); w.Number(Total);
            w.Name("source"); w.String(Wire.Name(Source));
            w.Name("stages"); w.BeginArray(); foreach (StageDeltaV item0 in Stages) { item0.Write(w); } w.EndArray();
            w.EndObject();
        }

        internal static DeltaV Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new DeltaV(
                node.Field(path, "total").AsNumber(path + ".total"),
                Wire.ReadDeltaVSource(node.Field(path, "source").AsString(path + ".source"), path + ".source"),
                node.Field(path, "stages").AsArray(path + ".stages", (n0, i0) => StageDeltaV.Read(n0, path + ".stages" + "[" + i0 + "]")));
        }
    }

    public sealed class StageDeltaV
    {
        private static readonly string[] Fields = { "stage", "deltaV", "twr", "burnTime" };

        public StageDeltaV(int stage, double deltaV, double? twr, double? burnTime)
        {
            Stage = stage;
            DeltaV = Require.Finite(deltaV, "deltaV");
            Twr = twr is double twrValue ? Require.Finite(twrValue, "twr") : (double?)null;
            BurnTime = burnTime is double burnTimeValue ? Require.Finite(burnTimeValue, "burnTime") : (double?)null;
        }

        /// <summary>KSP stage index, matching CraftPart.stage.</summary>
        public int Stage { get; }

        /// <summary>Vacuum delta-v of this stage. Unit: m/s.</summary>
        public double DeltaV { get; }

        /// <summary>Thrust to weight at ignition against the body the vessel is at, null when the stage has no engine.</summary>
        public double? Twr { get; }

        /// <summary>Full-throttle burn time, null when the stage has no engine. Unit: s.</summary>
        public double? BurnTime { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("stage"); w.Number((long)Stage);
            w.Name("deltaV"); w.Number(DeltaV);
            w.Name("twr"); if (Twr is null) { w.Null(); } else { w.Number(Twr.Value); }
            w.Name("burnTime"); if (BurnTime is null) { w.Null(); } else { w.Number(BurnTime.Value); }
            w.EndObject();
        }

        internal static StageDeltaV Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new StageDeltaV(
                node.Field(path, "stage").AsInt(path + ".stage"),
                node.Field(path, "deltaV").AsNumber(path + ".deltaV"),
                node.Field(path, "twr").Kind == JsonKind.Null ? null : (double?)node.Field(path, "twr").AsNumber(path + ".twr"),
                node.Field(path, "burnTime").Kind == JsonKind.Null ? null : (double?)node.Field(path, "burnTime").AsNumber(path + ".burnTime"));
        }
    }

    /// <summary>A vessel's part layout. The server sends it when it first links a vessel and whenever the parts change (staging, docking, decoupling), not with every fleet snapshot, and resends every layout after a new connection's hello. A newer revision for the same vesselId replaces the older one.</summary>
    public sealed class CraftLayout : ServerMessage
    {
        private static readonly string[] Fields = { "protocolVersion", "type", "vesselId", "revision", "parts", "model" };

        public CraftLayout(uint vesselId, uint revision, IReadOnlyList<CraftPart> parts, string? model)
        {
            VesselId = vesselId;
            Revision = revision;
            Parts = Require.Items(parts, "parts");
            Model = model;
        }

        public uint VesselId { get; }

        /// <summary>Counts up by one each time this vessel's layout changes.</summary>
        public uint Revision { get; }

        public IReadOnlyList<CraftPart> Parts { get; }

        /// <summary>Server path of the simplified 3D model of this design, such as /art/craft/1a2b3c4d.json: a CraftModel document. Vessels built alike share one model. Null until exported.</summary>
        public string? Model { get; }

        public override T Match<T>(Func<ServerHello, T> hello, Func<Fleet, T> fleet, Func<CraftLayout, T> layout, Func<Pong, T> pong, Func<Bye, T> bye, Func<BriefingMessage, T> briefing, Func<Missions, T> missions, Func<GroupRoster, T> groups) => (layout ?? throw new ArgumentNullException(nameof(layout)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);
            w.Name("type"); w.String("layout");
            w.Name("vesselId"); w.Number((long)VesselId);
            w.Name("revision"); w.Number((long)Revision);
            w.Name("parts"); w.BeginArray(); foreach (CraftPart item0 in Parts) { item0.Write(w); } w.EndArray();
            w.Name("model"); if (Model is null) { w.Null(); } else { w.String(Model); }
            w.EndObject();
        }

        internal static new CraftLayout Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new CraftLayout(
                node.Field(path, "vesselId").AsUInt(path + ".vesselId"),
                node.Field(path, "revision").AsUInt(path + ".revision"),
                node.Field(path, "parts").AsArray(path + ".parts", (n0, i0) => CraftPart.Read(n0, path + ".parts" + "[" + i0 + "]")),
                node.Field(path, "model").Kind == JsonKind.Null ? null : node.Field(path, "model").AsString(path + ".model"));
        }
    }

    /// <summary>One part, in the root part's frame: x right, y up the craft's axis, z forward. Positions are part origins.</summary>
    public sealed class CraftPart
    {
        private static readonly string[] Fields = { "id", "name", "position", "rotation", "size", "category", "stage" };

        public CraftPart(uint id, string name, Vec3 position, Quat rotation, Vec3 size, PartCategory category, int stage)
        {
            Id = id;
            Name = name ?? throw new ArgumentNullException("name");
            Position = position ?? throw new ArgumentNullException("position");
            Rotation = rotation ?? throw new ArgumentNullException("rotation");
            Size = size ?? throw new ArgumentNullException("size");
            Category = Require.Defined(category, "category");
            Stage = stage;
        }

        /// <summary>KSP persistentId of the part.</summary>
        public uint Id { get; }

        /// <summary>Part name, such as liquidEngine.</summary>
        public string Name { get; }

        public Vec3 Position { get; }

        public Quat Rotation { get; }

        /// <summary>Full extents of the part's bounds along its own axes.</summary>
        public Vec3 Size { get; }

        public PartCategory Category { get; }

        /// <summary>KSP stage index (the part's inverseStage), -1 for a part that is never staged.</summary>
        public int Stage { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("id"); w.Number((long)Id);
            w.Name("name"); w.String(Name);
            w.Name("position"); Position.Write(w);
            w.Name("rotation"); Rotation.Write(w);
            w.Name("size"); Size.Write(w);
            w.Name("category"); w.String(Wire.Name(Category));
            w.Name("stage"); w.Number((long)Stage);
            w.EndObject();
        }

        internal static CraftPart Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new CraftPart(
                node.Field(path, "id").AsUInt(path + ".id"),
                node.Field(path, "name").AsString(path + ".name"),
                Vec3.Read(node.Field(path, "position"), path + ".position"),
                Quat.Read(node.Field(path, "rotation"), path + ".rotation"),
                Vec3.Read(node.Field(path, "size"), path + ".size"),
                Wire.ReadPartCategory(node.Field(path, "category").AsString(path + ".category"), path + ".category"),
                node.Field(path, "stage").AsInt(path + ".stage"));
        }
    }

    public enum PartCategory
    {
        Engine,
        Tank,
        Pod,
        Decoupler,
        Other,
    }

    public sealed class Vec3
    {
        private static readonly string[] Fields = { "x", "y", "z" };

        public Vec3(double x, double y, double z)
        {
            X = Require.Finite(x, "x");
            Y = Require.Finite(y, "y");
            Z = Require.Finite(z, "z");
        }

        /// <summary>Unit: m.</summary>
        public double X { get; }

        /// <summary>Unit: m.</summary>
        public double Y { get; }

        /// <summary>Unit: m.</summary>
        public double Z { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("x"); w.Number(X);
            w.Name("y"); w.Number(Y);
            w.Name("z"); w.Number(Z);
            w.EndObject();
        }

        internal static Vec3 Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Vec3(
                node.Field(path, "x").AsNumber(path + ".x"),
                node.Field(path, "y").AsNumber(path + ".y"),
                node.Field(path, "z").AsNumber(path + ".z"));
        }
    }

    public sealed class Quat
    {
        private static readonly string[] Fields = { "x", "y", "z", "w" };

        public Quat(double x, double y, double z, double w)
        {
            X = Require.Finite(x, "x");
            Y = Require.Finite(y, "y");
            Z = Require.Finite(z, "z");
            W = Require.Finite(w, "w");
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        public double W { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("x"); w.Number(X);
            w.Name("y"); w.Number(Y);
            w.Name("z"); w.Number(Z);
            w.Name("w"); w.Number(W);
            w.EndObject();
        }

        internal static Quat Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Quat(
                node.Field(path, "x").AsNumber(path + ".x"),
                node.Field(path, "y").AsNumber(path + ".y"),
                node.Field(path, "z").AsNumber(path + ".z"),
                node.Field(path, "w").AsNumber(path + ".w"));
        }
    }

    /// <summary>stock: KSP's own calculation for a loaded vessel. estimate: FLTCOM's estimate from the saved parts of an unloaded vessel.</summary>
    public enum DeltaVSource
    {
        Stock,
        Estimate,
    }

    /// <summary>Ask the server to plan an orbit change for one vessel or one group and answer with a briefing.</summary>
    public sealed class BriefRequest : ClientMessage
    {
        private static readonly string[] Fields = { "protocolVersion", "type", "requestId", "vesselId", "groupId", "change", "codename", "pinnedStage" };

        public BriefRequest(uint requestId, uint vesselId, string? groupId, OrbitChangeRequest change, string? codename, int? pinnedStage)
        {
            RequestId = requestId;
            VesselId = vesselId;
            GroupId = groupId;
            Change = change ?? throw new ArgumentNullException("change");
            Codename = codename;
            PinnedStage = pinnedStage;
        }

        /// <summary>Chosen by the display; the briefing echoes it.</summary>
        public uint RequestId { get; }

        /// <summary>Vessel.id of the vessel to fly; ignored when groupId is set.</summary>
        public uint VesselId { get; }

        /// <summary>Plan for every member of this group in one operation; null plans for vesselId alone.</summary>
        public string? GroupId { get; }

        public OrbitChangeRequest Change { get; }

        /// <summary>Operator's codename; null lets the server generate one.</summary>
        public string? Codename { get; }

        /// <summary>Stage that flies every burn; null picks automatically, lowest stage first.</summary>
        public int? PinnedStage { get; }

        public override T Match<T>(Func<ClientHello, T> hello, Func<Ping, T> ping, Func<BriefRequest, T> brief, Func<Approve, T> approve, Func<Scrub, T> scrub, Func<Resume, T> resume, Func<Answer, T> answer, Func<GroupEdit, T> groupEdit) => (brief ?? throw new ArgumentNullException(nameof(brief)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);
            w.Name("type"); w.String("brief");
            w.Name("requestId"); w.Number((long)RequestId);
            w.Name("vesselId"); w.Number((long)VesselId);
            w.Name("groupId"); if (GroupId is null) { w.Null(); } else { w.String(GroupId); }
            w.Name("change"); Change.Write(w);
            w.Name("codename"); if (Codename is null) { w.Null(); } else { w.String(Codename); }
            w.Name("pinnedStage"); if (PinnedStage is null) { w.Null(); } else { w.Number((long)PinnedStage.Value); }
            w.EndObject();
        }

        internal static new BriefRequest Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new BriefRequest(
                node.Field(path, "requestId").AsUInt(path + ".requestId"),
                node.Field(path, "vesselId").AsUInt(path + ".vesselId"),
                node.Field(path, "groupId").Kind == JsonKind.Null ? null : node.Field(path, "groupId").AsString(path + ".groupId"),
                OrbitChangeRequest.Read(node.Field(path, "change"), path + ".change"),
                node.Field(path, "codename").Kind == JsonKind.Null ? null : node.Field(path, "codename").AsString(path + ".codename"),
                node.Field(path, "pinnedStage").Kind == JsonKind.Null ? null : (int?)node.Field(path, "pinnedStage").AsInt(path + ".pinnedStage"));
        }
    }

    /// <summary>Approve a briefed operation. The server uploads it and flies it when its burns come due.</summary>
    public sealed class Approve : ClientMessage
    {
        private static readonly string[] Fields = { "protocolVersion", "type", "operationId", "codename" };

        public Approve(string operationId, string codename)
        {
            OperationId = operationId ?? throw new ArgumentNullException("operationId");
            Codename = codename ?? throw new ArgumentNullException("codename");
        }

        public string OperationId { get; }

        /// <summary>Final codename, possibly edited after the briefing.</summary>
        public string Codename { get; }

        public override T Match<T>(Func<ClientHello, T> hello, Func<Ping, T> ping, Func<BriefRequest, T> brief, Func<Approve, T> approve, Func<Scrub, T> scrub, Func<Resume, T> resume, Func<Answer, T> answer, Func<GroupEdit, T> groupEdit) => (approve ?? throw new ArgumentNullException(nameof(approve)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);
            w.Name("type"); w.String("approve");
            w.Name("operationId"); w.String(OperationId);
            w.Name("codename"); w.String(Codename);
            w.EndObject();
        }

        internal static new Approve Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Approve(
                node.Field(path, "operationId").AsString(path + ".operationId"),
                node.Field(path, "codename").AsString(path + ".codename"));
        }
    }

    /// <summary>Cancel an operation. Before ignition it scrubs; during a burn it aborts and releases control.</summary>
    public sealed class Scrub : ClientMessage
    {
        private static readonly string[] Fields = { "protocolVersion", "type", "operationId" };

        public Scrub(string operationId)
        {
            OperationId = operationId ?? throw new ArgumentNullException("operationId");
        }

        public string OperationId { get; }

        public override T Match<T>(Func<ClientHello, T> hello, Func<Ping, T> ping, Func<BriefRequest, T> brief, Func<Approve, T> approve, Func<Scrub, T> scrub, Func<Resume, T> resume, Func<Answer, T> answer, Func<GroupEdit, T> groupEdit) => (scrub ?? throw new ArgumentNullException(nameof(scrub)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);
            w.Name("type"); w.String("scrub");
            w.Name("operationId"); w.String(OperationId);
            w.EndObject();
        }

        internal static new Scrub Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Scrub(
                node.Field(path, "operationId").AsString(path + ".operationId"));
        }
    }

    /// <summary>Resume an operation in Hold.</summary>
    public sealed class Resume : ClientMessage
    {
        private static readonly string[] Fields = { "protocolVersion", "type", "operationId" };

        public Resume(string operationId)
        {
            OperationId = operationId ?? throw new ArgumentNullException("operationId");
        }

        public string OperationId { get; }

        public override T Match<T>(Func<ClientHello, T> hello, Func<Ping, T> ping, Func<BriefRequest, T> brief, Func<Approve, T> approve, Func<Scrub, T> scrub, Func<Resume, T> resume, Func<Answer, T> answer, Func<GroupEdit, T> groupEdit) => (resume ?? throw new ArgumentNullException(nameof(resume)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);
            w.Name("type"); w.String("resume");
            w.Name("operationId"); w.String(OperationId);
            w.EndObject();
        }

        internal static new Resume Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Resume(
                node.Field(path, "operationId").AsString(path + ".operationId"));
        }
    }

    /// <summary>The operator's answer to an open prompt.</summary>
    public sealed class Answer : ClientMessage
    {
        private static readonly string[] Fields = { "protocolVersion", "type", "promptId", "accept" };

        public Answer(uint promptId, bool accept)
        {
            PromptId = promptId;
            Accept = accept;
        }

        public uint PromptId { get; }

        public bool Accept { get; }

        public override T Match<T>(Func<ClientHello, T> hello, Func<Ping, T> ping, Func<BriefRequest, T> brief, Func<Approve, T> approve, Func<Scrub, T> scrub, Func<Resume, T> resume, Func<Answer, T> answer, Func<GroupEdit, T> groupEdit) => (answer ?? throw new ArgumentNullException(nameof(answer)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);
            w.Name("type"); w.String("answer");
            w.Name("promptId"); w.Number((long)PromptId);
            w.Name("accept"); w.Bool(Accept);
            w.EndObject();
        }

        internal static new Answer Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Answer(
                node.Field(path, "promptId").AsUInt(path + ".promptId"),
                node.Field(path, "accept").AsBool(path + ".accept"));
        }
    }

    /// <summary>Answer to a brief request: the planned burns, the delta-v budget and the contingencies.</summary>
    public sealed class BriefingMessage : ServerMessage
    {
        private static readonly string[] Fields = { "protocolVersion", "type", "requestId", "operationId", "codename", "vesselId", "groupId", "change", "description", "burns", "requiredDeltaV", "availableDeltaV", "margin", "stages", "contingencies", "canApprove", "error", "porkchop", "stagePlan", "autoActions", "deployment" };

        public BriefingMessage(uint requestId, string? operationId, string codename, uint vesselId, string? groupId, OrbitChangeRequest change, string description, IReadOnlyList<BurnPlan> burns, double requiredDeltaV, double availableDeltaV, double? margin, IReadOnlyList<StageLine> stages, IReadOnlyList<ContingencyNote> contingencies, bool canApprove, string? error, PorkchopPlot? porkchop, IReadOnlyList<StagePlanLine> stagePlan, IReadOnlyList<AutoAction> autoActions, Deployment? deployment)
        {
            RequestId = requestId;
            OperationId = operationId;
            Codename = codename ?? throw new ArgumentNullException("codename");
            VesselId = vesselId;
            GroupId = groupId;
            Change = change ?? throw new ArgumentNullException("change");
            Description = description ?? throw new ArgumentNullException("description");
            Burns = Require.Items(burns, "burns");
            RequiredDeltaV = Require.Finite(requiredDeltaV, "requiredDeltaV");
            AvailableDeltaV = Require.Finite(availableDeltaV, "availableDeltaV");
            Margin = margin is double marginValue ? Require.Finite(marginValue, "margin") : (double?)null;
            Stages = Require.Items(stages, "stages");
            Contingencies = Require.Items(contingencies, "contingencies");
            CanApprove = canApprove;
            Error = error;
            Porkchop = porkchop;
            StagePlan = Require.Items(stagePlan, "stagePlan");
            AutoActions = Require.Items(autoActions, "autoActions");
            Deployment = deployment;
        }

        public uint RequestId { get; }

        /// <summary>Null when planning failed.</summary>
        public string? OperationId { get; }

        public string Codename { get; }

        public uint VesselId { get; }

        /// <summary>The group the briefing covers, null for one vessel.</summary>
        public string? GroupId { get; }

        public OrbitChangeRequest Change { get; }

        /// <summary>Readable summary of the change, such as circularize at apoapsis.</summary>
        public string Description { get; }

        public IReadOnlyList<BurnPlan> Burns { get; }

        /// <summary>Unit: m/s.</summary>
        public double RequiredDeltaV { get; }

        /// <summary>Unit: m/s.</summary>
        public double AvailableDeltaV { get; }

        /// <summary>Spare delta-v as a share of required; null when nothing is required.</summary>
        public double? Margin { get; }

        public IReadOnlyList<StageLine> Stages { get; }

        public IReadOnlyList<ContingencyNote> Contingencies { get; }

        /// <summary>False while any contingency is red.</summary>
        public bool CanApprove { get; }

        /// <summary>Why no plan exists, null on success.</summary>
        public string? Error { get; }

        /// <summary>The transfer windows searched for a transfer between planets, null otherwise.</summary>
        public PorkchopPlot? Porkchop { get; }

        /// <summary>What each stage of each vessel does and when it fires in this operation, next stage first.</summary>
        public IReadOnlyList<StagePlanLine> StagePlan { get; }

        /// <summary>Every automatic action the operation will take: staging, landing gear, fairings, panels.</summary>
        public IReadOnlyList<AutoAction> AutoActions { get; }

        /// <summary>The payloads, releases and carrier return of a constellation deployment, null otherwise.</summary>
        public Deployment? Deployment { get; }

        public override T Match<T>(Func<ServerHello, T> hello, Func<Fleet, T> fleet, Func<CraftLayout, T> layout, Func<Pong, T> pong, Func<Bye, T> bye, Func<BriefingMessage, T> briefing, Func<Missions, T> missions, Func<GroupRoster, T> groups) => (briefing ?? throw new ArgumentNullException(nameof(briefing)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);
            w.Name("type"); w.String("briefing");
            w.Name("requestId"); w.Number((long)RequestId);
            w.Name("operationId"); if (OperationId is null) { w.Null(); } else { w.String(OperationId); }
            w.Name("codename"); w.String(Codename);
            w.Name("vesselId"); w.Number((long)VesselId);
            w.Name("groupId"); if (GroupId is null) { w.Null(); } else { w.String(GroupId); }
            w.Name("change"); Change.Write(w);
            w.Name("description"); w.String(Description);
            w.Name("burns"); w.BeginArray(); foreach (BurnPlan item0 in Burns) { item0.Write(w); } w.EndArray();
            w.Name("requiredDeltaV"); w.Number(RequiredDeltaV);
            w.Name("availableDeltaV"); w.Number(AvailableDeltaV);
            w.Name("margin"); if (Margin is null) { w.Null(); } else { w.Number(Margin.Value); }
            w.Name("stages"); w.BeginArray(); foreach (StageLine item0 in Stages) { item0.Write(w); } w.EndArray();
            w.Name("contingencies"); w.BeginArray(); foreach (ContingencyNote item0 in Contingencies) { item0.Write(w); } w.EndArray();
            w.Name("canApprove"); w.Bool(CanApprove);
            w.Name("error"); if (Error is null) { w.Null(); } else { w.String(Error); }
            w.Name("porkchop"); if (Porkchop is null) { w.Null(); } else { Porkchop.Write(w); }
            w.Name("stagePlan"); w.BeginArray(); foreach (StagePlanLine item0 in StagePlan) { item0.Write(w); } w.EndArray();
            w.Name("autoActions"); w.BeginArray(); foreach (AutoAction item0 in AutoActions) { item0.Write(w); } w.EndArray();
            w.Name("deployment"); if (Deployment is null) { w.Null(); } else { Deployment.Write(w); }
            w.EndObject();
        }

        internal static new BriefingMessage Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new BriefingMessage(
                node.Field(path, "requestId").AsUInt(path + ".requestId"),
                node.Field(path, "operationId").Kind == JsonKind.Null ? null : node.Field(path, "operationId").AsString(path + ".operationId"),
                node.Field(path, "codename").AsString(path + ".codename"),
                node.Field(path, "vesselId").AsUInt(path + ".vesselId"),
                node.Field(path, "groupId").Kind == JsonKind.Null ? null : node.Field(path, "groupId").AsString(path + ".groupId"),
                OrbitChangeRequest.Read(node.Field(path, "change"), path + ".change"),
                node.Field(path, "description").AsString(path + ".description"),
                node.Field(path, "burns").AsArray(path + ".burns", (n0, i0) => BurnPlan.Read(n0, path + ".burns" + "[" + i0 + "]")),
                node.Field(path, "requiredDeltaV").AsNumber(path + ".requiredDeltaV"),
                node.Field(path, "availableDeltaV").AsNumber(path + ".availableDeltaV"),
                node.Field(path, "margin").Kind == JsonKind.Null ? null : (double?)node.Field(path, "margin").AsNumber(path + ".margin"),
                node.Field(path, "stages").AsArray(path + ".stages", (n0, i0) => StageLine.Read(n0, path + ".stages" + "[" + i0 + "]")),
                node.Field(path, "contingencies").AsArray(path + ".contingencies", (n0, i0) => ContingencyNote.Read(n0, path + ".contingencies" + "[" + i0 + "]")),
                node.Field(path, "canApprove").AsBool(path + ".canApprove"),
                node.Field(path, "error").Kind == JsonKind.Null ? null : node.Field(path, "error").AsString(path + ".error"),
                node.Field(path, "porkchop").Kind == JsonKind.Null ? null : PorkchopPlot.Read(node.Field(path, "porkchop"), path + ".porkchop"),
                node.Field(path, "stagePlan").AsArray(path + ".stagePlan", (n0, i0) => StagePlanLine.Read(n0, path + ".stagePlan" + "[" + i0 + "]")),
                node.Field(path, "autoActions").AsArray(path + ".autoActions", (n0, i0) => AutoAction.Read(n0, path + ".autoActions" + "[" + i0 + "]")),
                node.Field(path, "deployment").Kind == JsonKind.Null ? null : Deployment.Read(node.Field(path, "deployment"), path + ".deployment"));
        }
    }

    /// <summary>Total delta-v of a transfer between planets over a grid of departure times and flight times, and the best window found.</summary>
    public sealed class PorkchopPlot
    {
        private static readonly string[] Fields = { "departureStart", "departureStep", "flightStart", "flightStep", "columns", "rows", "deltaV", "bestDeparture", "bestFlight", "bestDeltaV" };

        public PorkchopPlot(double departureStart, double departureStep, double flightStart, double flightStep, int columns, int rows, IReadOnlyList<double> deltaV, double bestDeparture, double bestFlight, double bestDeltaV)
        {
            DepartureStart = Require.Finite(departureStart, "departureStart");
            DepartureStep = Require.Finite(departureStep, "departureStep");
            FlightStart = Require.Finite(flightStart, "flightStart");
            FlightStep = Require.Finite(flightStep, "flightStep");
            Columns = columns;
            Rows = rows;
            DeltaV = Require.Values(deltaV, "deltaV");
            BestDeparture = Require.Finite(bestDeparture, "bestDeparture");
            BestFlight = Require.Finite(bestFlight, "bestFlight");
            BestDeltaV = Require.Finite(bestDeltaV, "bestDeltaV");
        }

        /// <summary>Departure time of the first column. Unit: s.</summary>
        public double DepartureStart { get; }

        /// <summary>Unit: s.</summary>
        public double DepartureStep { get; }

        /// <summary>Flight time of the first row. Unit: s.</summary>
        public double FlightStart { get; }

        /// <summary>Unit: s.</summary>
        public double FlightStep { get; }

        public int Columns { get; }

        public int Rows { get; }

        /// <summary>Row by row, rows of flight time and columns of departure time; 0 where no transfer exists.</summary>
        public IReadOnlyList<double> DeltaV { get; }

        /// <summary>Unit: s.</summary>
        public double BestDeparture { get; }

        /// <summary>Unit: s.</summary>
        public double BestFlight { get; }

        /// <summary>Unit: m/s.</summary>
        public double BestDeltaV { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("departureStart"); w.Number(DepartureStart);
            w.Name("departureStep"); w.Number(DepartureStep);
            w.Name("flightStart"); w.Number(FlightStart);
            w.Name("flightStep"); w.Number(FlightStep);
            w.Name("columns"); w.Number((long)Columns);
            w.Name("rows"); w.Number((long)Rows);
            w.Name("deltaV"); w.BeginArray(); foreach (double item0 in DeltaV) { w.Number(item0); } w.EndArray();
            w.Name("bestDeparture"); w.Number(BestDeparture);
            w.Name("bestFlight"); w.Number(BestFlight);
            w.Name("bestDeltaV"); w.Number(BestDeltaV);
            w.EndObject();
        }

        internal static PorkchopPlot Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new PorkchopPlot(
                node.Field(path, "departureStart").AsNumber(path + ".departureStart"),
                node.Field(path, "departureStep").AsNumber(path + ".departureStep"),
                node.Field(path, "flightStart").AsNumber(path + ".flightStart"),
                node.Field(path, "flightStep").AsNumber(path + ".flightStep"),
                node.Field(path, "columns").AsInt(path + ".columns"),
                node.Field(path, "rows").AsInt(path + ".rows"),
                node.Field(path, "deltaV").AsArray(path + ".deltaV", (n0, i0) => n0.AsNumber(path + ".deltaV" + "[" + i0 + "]")),
                node.Field(path, "bestDeparture").AsNumber(path + ".bestDeparture"),
                node.Field(path, "bestFlight").AsNumber(path + ".bestFlight"),
                node.Field(path, "bestDeltaV").AsNumber(path + ".bestDeltaV"));
        }
    }

    /// <summary>Every operation the server knows and every open prompt. Sent on connect and on each change; the display replaces its state with each one.</summary>
    public sealed class Missions : ServerMessage
    {
        private static readonly string[] Fields = { "protocolVersion", "type", "ut", "operations", "prompts" };

        public Missions(double ut, IReadOnlyList<OperationStatus> operations, IReadOnlyList<Prompt> prompts)
        {
            Ut = Require.Finite(ut, "ut");
            Operations = Require.UniqueBy(Require.Items(operations, "operations"), item => item.Id, "operations");
            Prompts = Require.UniqueBy(Require.Items(prompts, "prompts"), item => item.Id, "prompts");
        }

        /// <summary>Unit: s.</summary>
        public double Ut { get; }

        public IReadOnlyList<OperationStatus> Operations { get; }

        public IReadOnlyList<Prompt> Prompts { get; }

        public override T Match<T>(Func<ServerHello, T> hello, Func<Fleet, T> fleet, Func<CraftLayout, T> layout, Func<Pong, T> pong, Func<Bye, T> bye, Func<BriefingMessage, T> briefing, Func<Missions, T> missions, Func<GroupRoster, T> groups) => (missions ?? throw new ArgumentNullException(nameof(missions)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);
            w.Name("type"); w.String("missions");
            w.Name("ut"); w.Number(Ut);
            w.Name("operations"); w.BeginArray(); foreach (OperationStatus item0 in Operations) { item0.Write(w); } w.EndArray();
            w.Name("prompts"); w.BeginArray(); foreach (Prompt item0 in Prompts) { item0.Write(w); } w.EndArray();
            w.EndObject();
        }

        internal static new Missions Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Missions(
                node.Field(path, "ut").AsNumber(path + ".ut"),
                JsonNode.UniqueBy(node.Field(path, "operations").AsArray(path + ".operations", (n0, i0) => OperationStatus.Read(n0, path + ".operations" + "[" + i0 + "]")), item => item.Id, path + ".operations", "id"),
                JsonNode.UniqueBy(node.Field(path, "prompts").AsArray(path + ".prompts", (n0, i0) => Prompt.Read(n0, path + ".prompts" + "[" + i0 + "]")), item => item.Id, path + ".prompts", "id"));
        }
    }

    /// <summary>What the orbit should become.</summary>
    public sealed class OrbitChangeRequest
    {
        private static readonly string[] Fields = { "kind", "altitude", "periapsis", "inclination", "lan", "body", "targetVesselId", "targetPortId", "landing", "autoSystems", "endOfLife" };

        public OrbitChangeRequest(ChangeKind kind, double? altitude, double? periapsis, double? inclination, double? lan, string? body, uint? targetVesselId, uint? targetPortId, LandingTarget? landing, bool? autoSystems, EndOfLife? endOfLife)
        {
            Kind = Require.Defined(kind, "kind");
            Altitude = altitude is double altitudeValue ? Require.Finite(altitudeValue, "altitude") : (double?)null;
            Periapsis = periapsis is double periapsisValue ? Require.Finite(periapsisValue, "periapsis") : (double?)null;
            Inclination = inclination is double inclinationValue ? Require.Finite(inclinationValue, "inclination") : (double?)null;
            Lan = lan is double lanValue ? Require.Finite(lanValue, "lan") : (double?)null;
            Body = body;
            TargetVesselId = targetVesselId;
            TargetPortId = targetPortId;
            Landing = landing;
            AutoSystems = autoSystems;
            EndOfLife = endOfLife;
        }

        public ChangeKind Kind { get; }

        /// <summary>Target altitude above sea level for changeApoapsis, changePeriapsis, circularOrbit and spreadEvenly; the rise for raiseApoapsis; the apoapsis for launch, where null means the standard low orbit; otherwise null. Unit: m.</summary>
        public double? Altitude { get; }

        /// <summary>Target periapsis above sea level for launch, null for a circular orbit at altitude; otherwise null. Unit: m.</summary>
        public double? Periapsis { get; }

        /// <summary>Target inclination for changeInclination and launch, where null flies due east; for deployConstellation, the ring's inclination from 0 to 180 degrees, where null or 0 is the equatorial ring; otherwise null. Unit: rad.</summary>
        public double? Inclination { get; }

        /// <summary>Longitude of the ascending node for launch: the launch then waits for the site to pass under that plane. For deployConstellation, the ring's ascending node, null meaning 0. Needs an inclination. Otherwise null. Unit: rad.</summary>
        public double? Lan { get; }

        /// <summary>The body a transfer flies to, by name; the orbit there is circular at altitude, inclined by inclination when that is set. Otherwise null.</summary>
        public string? Body { get; }

        /// <summary>The vessel a rendezvous meets, by persistentId; otherwise null.</summary>
        public uint? TargetVesselId { get; }

        /// <summary>The docking port on the target that a rendezvous reserves, by part persistentId; null reserves the whole target vessel. Otherwise null.</summary>
        public uint? TargetPortId { get; }

        /// <summary>Where land touches down; null for every other kind.</summary>
        public LandingTarget? Landing { get; }

        /// <summary>true or null: FLTCOM stages in every burn and works landing gear, fairings, solar panels, antennas and radiators. false: it stages only where it did before (ascent, landing, a flamed-out node burn) and touches nothing else.</summary>
        public bool? AutoSystems { get; }

        /// <summary>What a deployConstellation's transfer stage does once its satellites are out; null picks burnUp where the body has air and the delta-v suffices, otherwise park. Null for every other kind.</summary>
        public EndOfLife? EndOfLife { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("kind"); w.String(Wire.Name(Kind));
            w.Name("altitude"); if (Altitude is null) { w.Null(); } else { w.Number(Altitude.Value); }
            w.Name("periapsis"); if (Periapsis is null) { w.Null(); } else { w.Number(Periapsis.Value); }
            w.Name("inclination"); if (Inclination is null) { w.Null(); } else { w.Number(Inclination.Value); }
            w.Name("lan"); if (Lan is null) { w.Null(); } else { w.Number(Lan.Value); }
            w.Name("body"); if (Body is null) { w.Null(); } else { w.String(Body); }
            w.Name("targetVesselId"); if (TargetVesselId is null) { w.Null(); } else { w.Number((long)TargetVesselId.Value); }
            w.Name("targetPortId"); if (TargetPortId is null) { w.Null(); } else { w.Number((long)TargetPortId.Value); }
            w.Name("landing"); if (Landing is null) { w.Null(); } else { Landing.Write(w); }
            w.Name("autoSystems"); if (AutoSystems is null) { w.Null(); } else { w.Bool(AutoSystems.Value); }
            w.Name("endOfLife"); if (EndOfLife is null) { w.Null(); } else { w.String(Wire.Name(EndOfLife.Value)); }
            w.EndObject();
        }

        internal static OrbitChangeRequest Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new OrbitChangeRequest(
                Wire.ReadChangeKind(node.Field(path, "kind").AsString(path + ".kind"), path + ".kind"),
                node.Field(path, "altitude").Kind == JsonKind.Null ? null : (double?)node.Field(path, "altitude").AsNumber(path + ".altitude"),
                node.Field(path, "periapsis").Kind == JsonKind.Null ? null : (double?)node.Field(path, "periapsis").AsNumber(path + ".periapsis"),
                node.Field(path, "inclination").Kind == JsonKind.Null ? null : (double?)node.Field(path, "inclination").AsNumber(path + ".inclination"),
                node.Field(path, "lan").Kind == JsonKind.Null ? null : (double?)node.Field(path, "lan").AsNumber(path + ".lan"),
                node.Field(path, "body").Kind == JsonKind.Null ? null : node.Field(path, "body").AsString(path + ".body"),
                node.Field(path, "targetVesselId").Kind == JsonKind.Null ? null : (uint?)node.Field(path, "targetVesselId").AsUInt(path + ".targetVesselId"),
                node.Field(path, "targetPortId").Kind == JsonKind.Null ? null : (uint?)node.Field(path, "targetPortId").AsUInt(path + ".targetPortId"),
                node.Field(path, "landing").Kind == JsonKind.Null ? null : LandingTarget.Read(node.Field(path, "landing"), path + ".landing"),
                node.Field(path, "autoSystems").Kind == JsonKind.Null ? null : (bool?)node.Field(path, "autoSystems").AsBool(path + ".autoSystems"),
                node.Field(path, "endOfLife").Kind == JsonKind.Null ? null : (EndOfLife?)Wire.ReadEndOfLife(node.Field(path, "endOfLife").AsString(path + ".endOfLife"), path + ".endOfLife"));
        }
    }

    /// <summary>raiseApoapsis: raise every apoapsis by altitude metres. spreadEvenly: one circular orbit at altitude with the vessels equally spaced around it. launch: fly a vessel on the launch pad to orbit. transfer: fly one vessel to a circular orbit at altitude around body, at inclination there when set. rendezvous: close on targetVesselId and hold within 50 m. dock: rendezvous, then dock with RCS to targetPortId, or the best free port when it is null. land: deorbit and touch down at landing. deployConstellation: release every payload of the carrier onto one circle at altitude around the body it orbits, in the plane set by inclination and lan (equatorial when inclination is null), evenly spaced, then bring the carrier home.</summary>
    public enum ChangeKind
    {
        Circularize,
        ChangeApoapsis,
        ChangePeriapsis,
        ChangeInclination,
        CircularOrbit,
        StandardLowOrbit,
        RaiseApoapsis,
        SpreadEvenly,
        Launch,
        Transfer,
        Rendezvous,
        Dock,
        Land,
        DeployConstellation,
    }

    /// <summary>Where a landing touches down. launchPad and runway are the KSC spots on the home planet, and their coordinates are fixed by the server; custom uses latitude and longitude on the vessel's body.</summary>
    public sealed class LandingTarget
    {
        private static readonly string[] Fields = { "site", "latitude", "longitude" };

        public LandingTarget(LandingSite site, double latitude, double longitude)
        {
            Site = Require.Defined(site, "site");
            Latitude = Require.Finite(latitude, "latitude");
            Longitude = Require.Finite(longitude, "longitude");
        }

        public LandingSite Site { get; }

        /// <summary>Unit: rad.</summary>
        public double Latitude { get; }

        /// <summary>East-positive. Unit: rad.</summary>
        public double Longitude { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("site"); w.String(Wire.Name(Site));
            w.Name("latitude"); w.Number(Latitude);
            w.Name("longitude"); w.Number(Longitude);
            w.EndObject();
        }

        internal static LandingTarget Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new LandingTarget(
                Wire.ReadLandingSite(node.Field(path, "site").AsString(path + ".site"), path + ".site"),
                node.Field(path, "latitude").AsNumber(path + ".latitude"),
                node.Field(path, "longitude").AsNumber(path + ".longitude"));
        }
    }

    public sealed class MarkerList
    {
        private static readonly string[] Fields = { "items" };

        public MarkerList(IReadOnlyList<SurfaceMarker> items)
        {
            Items = Require.UniqueBy(Require.Items(items, "items"), item => item.Id, "items");
        }

        public IReadOnlyList<SurfaceMarker> Items { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("items"); w.BeginArray(); foreach (SurfaceMarker item0 in Items) { item0.Write(w); } w.EndArray();
            w.EndObject();
        }

        internal static MarkerList Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new MarkerList(
                JsonNode.UniqueBy(node.Field(path, "items").AsArray(path + ".items", (n0, i0) => SurfaceMarker.Read(n0, path + ".items" + "[" + i0 + "]")), item => item.Id, path + ".items", "id"));
        }
    }

    /// <summary>A spot the game marks on a body's surface, offered as a landing target.</summary>
    public sealed class SurfaceMarker
    {
        private static readonly string[] Fields = { "id", "name", "kind", "body", "latitude", "longitude" };

        public SurfaceMarker(string id, string name, SurfaceMarkerKind kind, string body, double latitude, double longitude)
        {
            Id = id ?? throw new ArgumentNullException("id");
            Name = name ?? throw new ArgumentNullException("name");
            Kind = Require.Defined(kind, "kind");
            Body = body ?? throw new ArgumentNullException("body");
            Latitude = Require.Finite(latitude, "latitude");
            Longitude = Require.Finite(longitude, "longitude");
        }

        /// <summary>Stable key, such as site:launchPad, flag:1234 or waypoint:&lt;guid&gt;.</summary>
        public string Id { get; }

        public string Name { get; }

        public SurfaceMarkerKind Kind { get; }

        /// <summary>Name of the body the marker is on.</summary>
        public string Body { get; }

        /// <summary>Unit: rad.</summary>
        public double Latitude { get; }

        /// <summary>East-positive, in (-π, π]. Unit: rad.</summary>
        public double Longitude { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("id"); w.String(Id);
            w.Name("name"); w.String(Name);
            w.Name("kind"); w.String(Wire.Name(Kind));
            w.Name("body"); w.String(Body);
            w.Name("latitude"); w.Number(Latitude);
            w.Name("longitude"); w.Number(Longitude);
            w.EndObject();
        }

        internal static SurfaceMarker Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new SurfaceMarker(
                node.Field(path, "id").AsString(path + ".id"),
                node.Field(path, "name").AsString(path + ".name"),
                Wire.ReadSurfaceMarkerKind(node.Field(path, "kind").AsString(path + ".kind"), path + ".kind"),
                node.Field(path, "body").AsString(path + ".body"),
                node.Field(path, "latitude").AsNumber(path + ".latitude"),
                node.Field(path, "longitude").AsNumber(path + ".longitude"));
        }
    }

    /// <summary>waypoint: a custom waypoint the player placed. contract: a contract's waypoint. flag: a planted flag. vessel: a landed or splashed vessel. site: the launch pad or the runway on the home planet, with id site:launchPad or site:runway.</summary>
    public enum SurfaceMarkerKind
    {
        Waypoint,
        Contract,
        Flag,
        Vessel,
        Site,
    }

    public enum LandingSite
    {
        LaunchPad,
        Runway,
        Custom,
    }

    /// <summary>Where and when the descent is predicted to touch down.</summary>
    public sealed class LandingPrediction
    {
        private static readonly string[] Fields = { "latitude", "longitude", "ut" };

        public LandingPrediction(double latitude, double longitude, double ut)
        {
            Latitude = Require.Finite(latitude, "latitude");
            Longitude = Require.Finite(longitude, "longitude");
            Ut = Require.Finite(ut, "ut");
        }

        /// <summary>Unit: rad.</summary>
        public double Latitude { get; }

        /// <summary>East-positive. Unit: rad.</summary>
        public double Longitude { get; }

        /// <summary>Unit: s.</summary>
        public double Ut { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("latitude"); w.Number(Latitude);
            w.Name("longitude"); w.Number(Longitude);
            w.Name("ut"); w.Number(Ut);
            w.EndObject();
        }

        internal static LandingPrediction Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new LandingPrediction(
                node.Field(path, "latitude").AsNumber(path + ".latitude"),
                node.Field(path, "longitude").AsNumber(path + ".longitude"),
                node.Field(path, "ut").AsNumber(path + ".ut"));
        }
    }

    /// <summary>One planned burn. Delta-v components are in m/s along prograde, orbit normal and radial-out at the node.</summary>
    public sealed class BurnPlan
    {
        private static readonly string[] Fields = { "vesselId", "label", "nodeUt", "startUt", "duration", "prograde", "normal", "radial", "deltaV", "orbitAfter" };

        public BurnPlan(uint vesselId, string label, double nodeUt, double startUt, double duration, double prograde, double normal, double radial, double deltaV, Orbit? orbitAfter)
        {
            VesselId = vesselId;
            Label = label ?? throw new ArgumentNullException("label");
            NodeUt = Require.Finite(nodeUt, "nodeUt");
            StartUt = Require.Finite(startUt, "startUt");
            Duration = Require.Finite(duration, "duration");
            Prograde = Require.Finite(prograde, "prograde");
            Normal = Require.Finite(normal, "normal");
            Radial = Require.Finite(radial, "radial");
            DeltaV = Require.Finite(deltaV, "deltaV");
            OrbitAfter = orbitAfter;
        }

        public uint VesselId { get; }

        public string Label { get; }

        /// <summary>Universal time of the impulsive node. Unit: s.</summary>
        public double NodeUt { get; }

        /// <summary>Ignition, so the burn is centred on the node in delta-v terms. Unit: s.</summary>
        public double StartUt { get; }

        /// <summary>Burn time from the vessel's thrust and mass. Unit: s.</summary>
        public double Duration { get; }

        /// <summary>Unit: m/s.</summary>
        public double Prograde { get; }

        /// <summary>Unit: m/s.</summary>
        public double Normal { get; }

        /// <summary>Unit: m/s.</summary>
        public double Radial { get; }

        /// <summary>Unit: m/s.</summary>
        public double DeltaV { get; }

        /// <summary>The planned orbit after this burn, with meanAnomaly at nodeUt, for drawing the plan. Set on every burn whose result the planner knows: each orbit change and transfer burn, a landing's deorbit, the orbit a launch aims for. Null on an ascent, a descent, and the earlier burns of an operation reloaded from a save.</summary>
        public Orbit? OrbitAfter { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("vesselId"); w.Number((long)VesselId);
            w.Name("label"); w.String(Label);
            w.Name("nodeUt"); w.Number(NodeUt);
            w.Name("startUt"); w.Number(StartUt);
            w.Name("duration"); w.Number(Duration);
            w.Name("prograde"); w.Number(Prograde);
            w.Name("normal"); w.Number(Normal);
            w.Name("radial"); w.Number(Radial);
            w.Name("deltaV"); w.Number(DeltaV);
            w.Name("orbitAfter"); if (OrbitAfter is null) { w.Null(); } else { OrbitAfter.Write(w); }
            w.EndObject();
        }

        internal static BurnPlan Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new BurnPlan(
                node.Field(path, "vesselId").AsUInt(path + ".vesselId"),
                node.Field(path, "label").AsString(path + ".label"),
                node.Field(path, "nodeUt").AsNumber(path + ".nodeUt"),
                node.Field(path, "startUt").AsNumber(path + ".startUt"),
                node.Field(path, "duration").AsNumber(path + ".duration"),
                node.Field(path, "prograde").AsNumber(path + ".prograde"),
                node.Field(path, "normal").AsNumber(path + ".normal"),
                node.Field(path, "radial").AsNumber(path + ".radial"),
                node.Field(path, "deltaV").AsNumber(path + ".deltaV"),
                node.Field(path, "orbitAfter").Kind == JsonKind.Null ? null : Orbit.Read(node.Field(path, "orbitAfter"), path + ".orbitAfter"));
        }
    }

    public sealed class StageLine
    {
        private static readonly string[] Fields = { "vesselId", "stage", "deltaV", "assigned", "twr", "burnTime", "insufficient" };

        public StageLine(uint vesselId, int stage, double deltaV, double assigned, double twr, double burnTime, bool insufficient)
        {
            VesselId = vesselId;
            Stage = stage;
            DeltaV = Require.Finite(deltaV, "deltaV");
            Assigned = Require.Finite(assigned, "assigned");
            Twr = Require.Finite(twr, "twr");
            BurnTime = Require.Finite(burnTime, "burnTime");
            Insufficient = insufficient;
        }

        public uint VesselId { get; }

        public int Stage { get; }

        /// <summary>Unit: m/s.</summary>
        public double DeltaV { get; }

        /// <summary>Delta-v of the planned burns this stage flies. Unit: m/s.</summary>
        public double Assigned { get; }

        /// <summary>Vacuum thrust-to-weight at the body's surface gravity.</summary>
        public double Twr { get; }

        /// <summary>Unit: s.</summary>
        public double BurnTime { get; }

        /// <summary>The stage cannot cover its assigned burns.</summary>
        public bool Insufficient { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("vesselId"); w.Number((long)VesselId);
            w.Name("stage"); w.Number((long)Stage);
            w.Name("deltaV"); w.Number(DeltaV);
            w.Name("assigned"); w.Number(Assigned);
            w.Name("twr"); w.Number(Twr);
            w.Name("burnTime"); w.Number(BurnTime);
            w.Name("insufficient"); w.Bool(Insufficient);
            w.EndObject();
        }

        internal static StageLine Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new StageLine(
                node.Field(path, "vesselId").AsUInt(path + ".vesselId"),
                node.Field(path, "stage").AsInt(path + ".stage"),
                node.Field(path, "deltaV").AsNumber(path + ".deltaV"),
                node.Field(path, "assigned").AsNumber(path + ".assigned"),
                node.Field(path, "twr").AsNumber(path + ".twr"),
                node.Field(path, "burnTime").AsNumber(path + ".burnTime"),
                node.Field(path, "insufficient").AsBool(path + ".insufficient"));
        }
    }

    public sealed class StagePlanLine
    {
        private static readonly string[] Fields = { "vesselId", "stage", "does", "when", "fires", "severity", "warning" };

        public StagePlanLine(uint vesselId, int stage, string does, string when, bool fires, Alert? severity, string? warning)
        {
            VesselId = vesselId;
            Stage = stage;
            Does = does ?? throw new ArgumentNullException("does");
            When = when ?? throw new ArgumentNullException("when");
            Fires = fires;
            Severity = severity;
            Warning = warning;
        }

        public uint VesselId { get; }

        /// <summary>KSP stage index, matching CraftPart.stage.</summary>
        public int Stage { get; }

        /// <summary>What firing the stage does, such as drop 2 boosters, light 1 engine.</summary>
        public string Does { get; }

        /// <summary>When it fires in this operation, such as at 1:12, kept, kept for landing.</summary>
        public string When { get; }

        /// <summary>The operation fires this stage to reach its burns.</summary>
        public bool Fires { get; }

        /// <summary>red: the stager will hold at this unsafe stage; amber: unsafe but never reached; null when safe.</summary>
        public Alert? Severity { get; }

        /// <summary>Why the stage is unsafe, such as opens parachutes in vacuum; null when safe.</summary>
        public string? Warning { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("vesselId"); w.Number((long)VesselId);
            w.Name("stage"); w.Number((long)Stage);
            w.Name("does"); w.String(Does);
            w.Name("when"); w.String(When);
            w.Name("fires"); w.Bool(Fires);
            w.Name("severity"); if (Severity is null) { w.Null(); } else { w.String(Wire.Name(Severity.Value)); }
            w.Name("warning"); if (Warning is null) { w.Null(); } else { w.String(Warning); }
            w.EndObject();
        }

        internal static StagePlanLine Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new StagePlanLine(
                node.Field(path, "vesselId").AsUInt(path + ".vesselId"),
                node.Field(path, "stage").AsInt(path + ".stage"),
                node.Field(path, "does").AsString(path + ".does"),
                node.Field(path, "when").AsString(path + ".when"),
                node.Field(path, "fires").AsBool(path + ".fires"),
                node.Field(path, "severity").Kind == JsonKind.Null ? null : (Alert?)Wire.ReadAlert(node.Field(path, "severity").AsString(path + ".severity"), path + ".severity"),
                node.Field(path, "warning").Kind == JsonKind.Null ? null : node.Field(path, "warning").AsString(path + ".warning"));
        }
    }

    /// <summary>A constellation deployment: the carrier's working orbit, each payload, each release and the carrier's return.</summary>
    public sealed class Deployment
    {
        private static readonly string[] Fields = { "altitude", "synchronousAltitude", "suggestedAltitude", "method", "workingOrbit", "payloads", "releases", "returnPlan", "launchStage" };

        public Deployment(double altitude, double? synchronousAltitude, double? suggestedAltitude, DeployMethod method, Orbit? workingOrbit, IReadOnlyList<DeployPayload> payloads, IReadOnlyList<DeployRelease> releases, CarrierReturnPlan returnPlan, LaunchStagePlan launchStage)
        {
            Altitude = Require.Finite(altitude, "altitude");
            SynchronousAltitude = synchronousAltitude is double synchronousAltitudeValue ? Require.Finite(synchronousAltitudeValue, "synchronousAltitude") : (double?)null;
            SuggestedAltitude = suggestedAltitude is double suggestedAltitudeValue ? Require.Finite(suggestedAltitudeValue, "suggestedAltitude") : (double?)null;
            Method = Require.Defined(method, "method");
            WorkingOrbit = workingOrbit;
            Payloads = Require.Items(payloads, "payloads");
            Releases = Require.Items(releases, "releases");
            ReturnPlan = returnPlan ?? throw new ArgumentNullException("returnPlan");
            LaunchStage = launchStage ?? throw new ArgumentNullException("launchStage");
        }

        /// <summary>The circle the payloads end on; the requested altitude when it was refused. Unit: m.</summary>
        public double Altitude { get; }

        /// <summary>Where an orbit lasts one day of the body, null when its day is unknown. Unit: m.</summary>
        public double? SynchronousAltitude { get; }

        /// <summary>When the altitude is refused: the highest one that works. Unit: m.</summary>
        public double? SuggestedAltitude { get; }

        public DeployMethod Method { get; }

        /// <summary>The carrier's orbit while it releases, meanAnomaly at the first release; null when refused.</summary>
        public Orbit? WorkingOrbit { get; }

        public IReadOnlyList<DeployPayload> Payloads { get; }

        /// <summary>In release order.</summary>
        public IReadOnlyList<DeployRelease> Releases { get; }

        public CarrierReturnPlan ReturnPlan { get; }

        public LaunchStagePlan LaunchStage { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("altitude"); w.Number(Altitude);
            w.Name("synchronousAltitude"); if (SynchronousAltitude is null) { w.Null(); } else { w.Number(SynchronousAltitude.Value); }
            w.Name("suggestedAltitude"); if (SuggestedAltitude is null) { w.Null(); } else { w.Number(SuggestedAltitude.Value); }
            w.Name("method"); w.String(Wire.Name(Method));
            w.Name("workingOrbit"); if (WorkingOrbit is null) { w.Null(); } else { WorkingOrbit.Write(w); }
            w.Name("payloads"); w.BeginArray(); foreach (DeployPayload item0 in Payloads) { item0.Write(w); } w.EndArray();
            w.Name("releases"); w.BeginArray(); foreach (DeployRelease item0 in Releases) { item0.Write(w); } w.EndArray();
            w.Name("returnPlan"); ReturnPlan.Write(w);
            w.Name("launchStage"); LaunchStage.Write(w);
            w.EndObject();
        }

        internal static Deployment Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Deployment(
                node.Field(path, "altitude").AsNumber(path + ".altitude"),
                node.Field(path, "synchronousAltitude").Kind == JsonKind.Null ? null : (double?)node.Field(path, "synchronousAltitude").AsNumber(path + ".synchronousAltitude"),
                node.Field(path, "suggestedAltitude").Kind == JsonKind.Null ? null : (double?)node.Field(path, "suggestedAltitude").AsNumber(path + ".suggestedAltitude"),
                Wire.ReadDeployMethod(node.Field(path, "method").AsString(path + ".method"), path + ".method"),
                node.Field(path, "workingOrbit").Kind == JsonKind.Null ? null : Orbit.Read(node.Field(path, "workingOrbit"), path + ".workingOrbit"),
                node.Field(path, "payloads").AsArray(path + ".payloads", (n0, i0) => DeployPayload.Read(n0, path + ".payloads" + "[" + i0 + "]")),
                node.Field(path, "releases").AsArray(path + ".releases", (n0, i0) => DeployRelease.Read(n0, path + ".releases" + "[" + i0 + "]")),
                CarrierReturnPlan.Read(node.Field(path, "returnPlan"), path + ".returnPlan"),
                LaunchStagePlan.Read(node.Field(path, "launchStage"), path + ".launchStage"));
        }
    }

    /// <summary>resonant: the carrier flies an orbit whose period is (n-1)/n or (n+1)/n of the circle's and each payload circularizes itself. carrierPhasing: payloads without engines; the carrier circularizes and phases between releases.</summary>
    public enum DeployMethod
    {
        Resonant,
        CarrierPhasing,
    }

    public sealed class DeployPayload
    {
        private static readonly string[] Fields = { "id", "name", "mass", "deltaV", "circularizeDeltaV" };

        public DeployPayload(uint id, string name, double mass, double deltaV, double circularizeDeltaV)
        {
            Id = id;
            Name = name ?? throw new ArgumentNullException("name");
            Mass = Require.Finite(mass, "mass");
            DeltaV = Require.Finite(deltaV, "deltaV");
            CircularizeDeltaV = Require.Finite(circularizeDeltaV, "circularizeDeltaV");
        }

        /// <summary>The payload's command part: its burns use this id until it is released and becomes a vessel.</summary>
        public uint Id { get; }

        /// <summary>The name it gets once released, such as Relay Carrier Sat 1.</summary>
        public string Name { get; }

        /// <summary>Unit: kg.</summary>
        public double Mass { get; }

        /// <summary>Vacuum delta-v of its own engines and propellant. Unit: m/s.</summary>
        public double DeltaV { get; }

        /// <summary>What its own circularization needs; 0 when the carrier places it. Unit: m/s.</summary>
        public double CircularizeDeltaV { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("id"); w.Number((long)Id);
            w.Name("name"); w.String(Name);
            w.Name("mass"); w.Number(Mass);
            w.Name("deltaV"); w.Number(DeltaV);
            w.Name("circularizeDeltaV"); w.Number(CircularizeDeltaV);
            w.EndObject();
        }

        internal static DeployPayload Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new DeployPayload(
                node.Field(path, "id").AsUInt(path + ".id"),
                node.Field(path, "name").AsString(path + ".name"),
                node.Field(path, "mass").AsNumber(path + ".mass"),
                node.Field(path, "deltaV").AsNumber(path + ".deltaV"),
                node.Field(path, "circularizeDeltaV").AsNumber(path + ".circularizeDeltaV"));
        }
    }

    public sealed class DeployRelease
    {
        private static readonly string[] Fields = { "payloadId", "name", "pass", "ut", "nodeUt", "apsis", "spacingError", "finalOrbit" };

        public DeployRelease(uint payloadId, string name, int pass, double ut, double nodeUt, string apsis, double spacingError, Orbit finalOrbit)
        {
            PayloadId = payloadId;
            Name = name ?? throw new ArgumentNullException("name");
            Pass = pass;
            Ut = Require.Finite(ut, "ut");
            NodeUt = Require.Finite(nodeUt, "nodeUt");
            Apsis = apsis ?? throw new ArgumentNullException("apsis");
            SpacingError = Require.Finite(spacingError, "spacingError");
            FinalOrbit = finalOrbit ?? throw new ArgumentNullException("finalOrbit");
        }

        public uint PayloadId { get; }

        public string Name { get; }

        /// <summary>Which pass of the working orbit, from 1.</summary>
        public int Pass { get; }

        /// <summary>When the carrier lets go. Unit: s.</summary>
        public double Ut { get; }

        /// <summary>Where the payload reaches the circle: its circularization node, or the release on the circle. Unit: s.</summary>
        public double NodeUt { get; }

        /// <summary>apoapsis, periapsis, or circle.</summary>
        public string Apsis { get; }

        /// <summary>Signed angle from its even slot once every payload is out. Unit: rad.</summary>
        public double SpacingError { get; }

        public Orbit FinalOrbit { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("payloadId"); w.Number((long)PayloadId);
            w.Name("name"); w.String(Name);
            w.Name("pass"); w.Number((long)Pass);
            w.Name("ut"); w.Number(Ut);
            w.Name("nodeUt"); w.Number(NodeUt);
            w.Name("apsis"); w.String(Apsis);
            w.Name("spacingError"); w.Number(SpacingError);
            w.Name("finalOrbit"); FinalOrbit.Write(w);
            w.EndObject();
        }

        internal static DeployRelease Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new DeployRelease(
                node.Field(path, "payloadId").AsUInt(path + ".payloadId"),
                node.Field(path, "name").AsString(path + ".name"),
                node.Field(path, "pass").AsInt(path + ".pass"),
                node.Field(path, "ut").AsNumber(path + ".ut"),
                node.Field(path, "nodeUt").AsNumber(path + ".nodeUt"),
                node.Field(path, "apsis").AsString(path + ".apsis"),
                node.Field(path, "spacingError").AsNumber(path + ".spacingError"),
                Orbit.Read(node.Field(path, "finalOrbit"), path + ".finalOrbit"));
        }
    }

    public sealed class CarrierReturnPlan
    {
        private static readonly string[] Fields = { "kind", "deltaV", "available", "text", "fallback" };

        public CarrierReturnPlan(CarrierReturnKind kind, double deltaV, double available, string text, bool fallback)
        {
            Kind = Require.Defined(kind, "kind");
            DeltaV = Require.Finite(deltaV, "deltaV");
            Available = Require.Finite(available, "available");
            Text = text ?? throw new ArgumentNullException("text");
            Fallback = fallback;
        }

        public CarrierReturnKind Kind { get; }

        /// <summary>Estimated delta-v of the return or disposal. Unit: m/s.</summary>
        public double DeltaV { get; }

        /// <summary>Delta-v the carrier has left once the payloads are out. Unit: m/s.</summary>
        public double Available { get; }

        public string Text { get; }

        /// <summary>The carrier cannot come home and this is what it does instead.</summary>
        public bool Fallback { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("kind"); w.String(Wire.Name(Kind));
            w.Name("deltaV"); w.Number(DeltaV);
            w.Name("available"); w.Number(Available);
            w.Name("text"); w.String(Text);
            w.Name("fallback"); w.Bool(Fallback);
            w.EndObject();
        }

        internal static CarrierReturnPlan Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new CarrierReturnPlan(
                Wire.ReadCarrierReturnKind(node.Field(path, "kind").AsString(path + ".kind"), path + ".kind"),
                node.Field(path, "deltaV").AsNumber(path + ".deltaV"),
                node.Field(path, "available").AsNumber(path + ".available"),
                node.Field(path, "text").AsString(path + ".text"),
                node.Field(path, "fallback").AsBool(path + ".fallback"));
        }
    }

    /// <summary>land: deorbit at home and come down on parachutes. transferAndLand: transfer home first. disposal: park on an orbit clear of the constellation. stay: stay on the release orbit. burnUp: lower the periapsis deep into the atmosphere so it burns up.</summary>
    public enum CarrierReturnKind
    {
        Land,
        TransferAndLand,
        Disposal,
        Stay,
        BurnUp,
    }

    /// <summary>The operator's pick for a transfer stage once its satellites are out. burnUp: lower the periapsis into the atmosphere. landHome: deorbit, or transfer home, and land on parachutes. park: a disposal orbit clear of the constellation.</summary>
    public enum EndOfLife
    {
        BurnUp,
        LandHome,
        Park,
    }

    /// <summary>The stage that lifted the stack: whether it splits off in the parking orbit and lands at the launch pad.</summary>
    public sealed class LaunchStagePlan
    {
        private static readonly string[] Fields = { "comesHome", "name", "deltaV", "available", "text" };

        public LaunchStagePlan(bool comesHome, string? name, double deltaV, double available, string text)
        {
            ComesHome = comesHome;
            Name = name;
            DeltaV = Require.Finite(deltaV, "deltaV");
            Available = Require.Finite(available, "available");
            Text = text ?? throw new ArgumentNullException("text");
        }

        /// <summary>It splits off first and gets its own return operation once the satellites are out.</summary>
        public bool ComesHome { get; }

        /// <summary>Its vessel name once split, such as Relay Carrier Launch Stage; null when none was found.</summary>
        public string? Name { get; }

        /// <summary>Estimated deorbit delta-v. Unit: m/s.</summary>
        public double DeltaV { get; }

        /// <summary>Its own delta-v. Unit: m/s.</summary>
        public double Available { get; }

        /// <summary>What it does, or why it stays attached.</summary>
        public string Text { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("comesHome"); w.Bool(ComesHome);
            w.Name("name"); if (Name is null) { w.Null(); } else { w.String(Name); }
            w.Name("deltaV"); w.Number(DeltaV);
            w.Name("available"); w.Number(Available);
            w.Name("text"); w.String(Text);
            w.EndObject();
        }

        internal static LaunchStagePlan Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new LaunchStagePlan(
                node.Field(path, "comesHome").AsBool(path + ".comesHome"),
                node.Field(path, "name").Kind == JsonKind.Null ? null : node.Field(path, "name").AsString(path + ".name"),
                node.Field(path, "deltaV").AsNumber(path + ".deltaV"),
                node.Field(path, "available").AsNumber(path + ".available"),
                node.Field(path, "text").AsString(path + ".text"));
        }
    }

    public sealed class AutoAction
    {
        private static readonly string[] Fields = { "vesselId", "text" };

        public AutoAction(uint vesselId, string text)
        {
            VesselId = vesselId;
            Text = text ?? throw new ArgumentNullException("text");
        }

        public uint VesselId { get; }

        /// <summary>One automatic action, such as jettison 1 fairing above 63 km.</summary>
        public string Text { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("vesselId"); w.Number((long)VesselId);
            w.Name("text"); w.String(Text);
            w.EndObject();
        }

        internal static AutoAction Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new AutoAction(
                node.Field(path, "vesselId").AsUInt(path + ".vesselId"),
                node.Field(path, "text").AsString(path + ".text"));
        }
    }

    public sealed class ContingencyNote
    {
        private static readonly string[] Fields = { "severity", "vesselId", "text" };

        public ContingencyNote(Alert severity, uint vesselId, string text)
        {
            Severity = Require.Defined(severity, "severity");
            VesselId = vesselId;
            Text = text ?? throw new ArgumentNullException("text");
        }

        public Alert Severity { get; }

        public uint VesselId { get; }

        public string Text { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("severity"); w.String(Wire.Name(Severity));
            w.Name("vesselId"); w.Number((long)VesselId);
            w.Name("text"); w.String(Text);
            w.EndObject();
        }

        internal static ContingencyNote Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new ContingencyNote(
                Wire.ReadAlert(node.Field(path, "severity").AsString(path + ".severity"), path + ".severity"),
                node.Field(path, "vesselId").AsUInt(path + ".vesselId"),
                node.Field(path, "text").AsString(path + ".text"));
        }
    }

    /// <summary>red blocks approval; amber warns.</summary>
    public enum Alert
    {
        Amber,
        Red,
    }

    /// <summary>One operation and where it stands.</summary>
    public sealed class OperationStatus
    {
        private static readonly string[] Fields = { "id", "codename", "change", "description", "state", "burns", "burnIndex", "deltaVDone", "note", "landingPrediction", "alarm", "descent" };

        public OperationStatus(string id, string codename, OrbitChangeRequest change, string description, OperationState state, IReadOnlyList<BurnPlan> burns, int burnIndex, double deltaVDone, string? note, LandingPrediction? landingPrediction, FlightAlarm? alarm, DescentStatus? descent)
        {
            Id = id ?? throw new ArgumentNullException("id");
            Codename = codename ?? throw new ArgumentNullException("codename");
            Change = change ?? throw new ArgumentNullException("change");
            Description = description ?? throw new ArgumentNullException("description");
            State = Require.Defined(state, "state");
            Burns = Require.Items(burns, "burns");
            BurnIndex = burnIndex;
            DeltaVDone = Require.Finite(deltaVDone, "deltaVDone");
            Note = note;
            LandingPrediction = landingPrediction;
            Alarm = alarm;
            Descent = descent;
        }

        public string Id { get; }

        public string Codename { get; }

        public OrbitChangeRequest Change { get; }

        public string Description { get; }

        public OperationState State { get; }

        public IReadOnlyList<BurnPlan> Burns { get; }

        /// <summary>Index into burns of the burn now due or flying.</summary>
        public int BurnIndex { get; }

        /// <summary>Delta-v flown in the current burn. Unit: m/s.</summary>
        public double DeltaVDone { get; }

        /// <summary>Latest timeline note, such as a re-plan, a released control or a missed window.</summary>
        public string? Note { get; }

        /// <summary>For land: the latest predicted touchdown point; otherwise null.</summary>
        public LandingPrediction? LandingPrediction { get; }

        /// <summary>A red alert: the vessel is headed into the ground, or its landing would come down too hard; otherwise null.</summary>
        public FlightAlarm? Alarm { get; }

        /// <summary>For land: the descent as last flown, once descent guidance has taken over; otherwise null.</summary>
        public DescentStatus? Descent { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("id"); w.String(Id);
            w.Name("codename"); w.String(Codename);
            w.Name("change"); Change.Write(w);
            w.Name("description"); w.String(Description);
            w.Name("state"); w.String(Wire.Name(State));
            w.Name("burns"); w.BeginArray(); foreach (BurnPlan item0 in Burns) { item0.Write(w); } w.EndArray();
            w.Name("burnIndex"); w.Number((long)BurnIndex);
            w.Name("deltaVDone"); w.Number(DeltaVDone);
            w.Name("note"); if (Note is null) { w.Null(); } else { w.String(Note); }
            w.Name("landingPrediction"); if (LandingPrediction is null) { w.Null(); } else { LandingPrediction.Write(w); }
            w.Name("alarm"); if (Alarm is null) { w.Null(); } else { Alarm.Write(w); }
            w.Name("descent"); if (Descent is null) { w.Null(); } else { Descent.Write(w); }
            w.EndObject();
        }

        internal static OperationStatus Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new OperationStatus(
                node.Field(path, "id").AsString(path + ".id"),
                node.Field(path, "codename").AsString(path + ".codename"),
                OrbitChangeRequest.Read(node.Field(path, "change"), path + ".change"),
                node.Field(path, "description").AsString(path + ".description"),
                Wire.ReadOperationState(node.Field(path, "state").AsString(path + ".state"), path + ".state"),
                node.Field(path, "burns").AsArray(path + ".burns", (n0, i0) => BurnPlan.Read(n0, path + ".burns" + "[" + i0 + "]")),
                node.Field(path, "burnIndex").AsInt(path + ".burnIndex"),
                node.Field(path, "deltaVDone").AsNumber(path + ".deltaVDone"),
                node.Field(path, "note").Kind == JsonKind.Null ? null : node.Field(path, "note").AsString(path + ".note"),
                node.Field(path, "landingPrediction").Kind == JsonKind.Null ? null : LandingPrediction.Read(node.Field(path, "landingPrediction"), path + ".landingPrediction"),
                node.Field(path, "alarm").Kind == JsonKind.Null ? null : FlightAlarm.Read(node.Field(path, "alarm"), path + ".alarm"),
                node.Field(path, "descent").Kind == JsonKind.Null ? null : DescentStatus.Read(node.Field(path, "descent"), path + ".descent"));
        }
    }

    /// <summary>Where a descent stands: the guidance phase, height over the ground under the vessel, vertical speed and the predicted miss.</summary>
    public sealed class DescentStatus
    {
        private static readonly string[] Fields = { "phase", "altitude", "verticalSpeed", "miss" };

        public DescentStatus(DescentPhase phase, double altitude, double verticalSpeed, double? miss)
        {
            Phase = Require.Defined(phase, "phase");
            Altitude = Require.Finite(altitude, "altitude");
            VerticalSpeed = Require.Finite(verticalSpeed, "verticalSpeed");
            Miss = miss is double missValue ? Require.Finite(missValue, "miss") : (double?)null;
        }

        public DescentPhase Phase { get; }

        /// <summary>Unit: m.</summary>
        public double Altitude { get; }

        /// <summary>Negative while coming down. Unit: m/s.</summary>
        public double VerticalSpeed { get; }

        /// <summary>Predicted touchdown to target; null before the first prediction reaches the ground. Unit: m.</summary>
        public double? Miss { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("phase"); w.String(Wire.Name(Phase));
            w.Name("altitude"); w.Number(Altitude);
            w.Name("verticalSpeed"); w.Number(VerticalSpeed);
            w.Name("miss"); if (Miss is null) { w.Null(); } else { w.Number(Miss.Value); }
            w.EndObject();
        }

        internal static DescentStatus Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new DescentStatus(
                Wire.ReadDescentPhase(node.Field(path, "phase").AsString(path + ".phase"), path + ".phase"),
                node.Field(path, "altitude").AsNumber(path + ".altitude"),
                node.Field(path, "verticalSpeed").AsNumber(path + ".verticalSpeed"),
                node.Field(path, "miss").Kind == JsonKind.Null ? null : (double?)node.Field(path, "miss").AsNumber(path + ".miss"));
        }
    }

    /// <summary>correct: small burns to put the predicted touchdown on the target. coast: engines off. brake: the braking burn. final: the vertical touchdown. landed: on the ground.</summary>
    public enum DescentPhase
    {
        Correct,
        Coast,
        Brake,
        Final,
        Landed,
    }

    /// <summary>A red alert on an operation. impact: the coast comes down under the body's safe floor (the top of its air or its highest terrain) where no landing is planned; the operation holds. landing: a planned landing would touch down too hard, cannot brake, or can no longer be flown before its deorbit.</summary>
    public sealed class FlightAlarm
    {
        private static readonly string[] Fields = { "kind", "body", "text" };

        public FlightAlarm(AlarmKind kind, string body, string text)
        {
            Kind = Require.Defined(kind, "kind");
            Body = body ?? throw new ArgumentNullException("body");
            Text = text ?? throw new ArgumentNullException("text");
        }

        public AlarmKind Kind { get; }

        public string Body { get; }

        /// <summary>What is wrong, such as "on impact course with Mun, periapsis -56.7 km".</summary>
        public string Text { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("kind"); w.String(Wire.Name(Kind));
            w.Name("body"); w.String(Body);
            w.Name("text"); w.String(Text);
            w.EndObject();
        }

        internal static FlightAlarm Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new FlightAlarm(
                Wire.ReadAlarmKind(node.Field(path, "kind").AsString(path + ".kind"), path + ".kind"),
                node.Field(path, "body").AsString(path + ".body"),
                node.Field(path, "text").AsString(path + ".text"));
        }
    }

    public enum AlarmKind
    {
        Impact,
        Landing,
    }

    /// <summary>Where an operation stands. hold: paused after a game load or a released control, waiting for resume.</summary>
    public enum OperationState
    {
        Draft,
        Briefed,
        Approved,
        Uploaded,
        Waiting,
        Executing,
        Hold,
        Done,
        Missed,
        Scrubbed,
        Aborted,
    }

    /// <summary>A question the server will not act on without the operator's answer. No answer means no action.</summary>
    public sealed class Prompt
    {
        private static readonly string[] Fields = { "id", "kind", "operationId", "vesselId", "text", "untilUt" };

        public Prompt(uint id, PromptKind kind, string operationId, uint vesselId, string text, double? untilUt)
        {
            Id = id;
            Kind = Require.Defined(kind, "kind");
            OperationId = operationId ?? throw new ArgumentNullException("operationId");
            VesselId = vesselId;
            Text = text ?? throw new ArgumentNullException("text");
            UntilUt = untilUt is double untilUtValue ? Require.Finite(untilUtValue, "untilUt") : (double?)null;
        }

        public uint Id { get; }

        public PromptKind Kind { get; }

        public string OperationId { get; }

        public uint VesselId { get; }

        public string Text { get; }

        /// <summary>For warp: the universal time the warp would stop at. Unit: s.</summary>
        public double? UntilUt { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("id"); w.Number((long)Id);
            w.Name("kind"); w.String(Wire.Name(Kind));
            w.Name("operationId"); w.String(OperationId);
            w.Name("vesselId"); w.Number((long)VesselId);
            w.Name("text"); w.String(Text);
            w.Name("untilUt"); if (UntilUt is null) { w.Null(); } else { w.Number(UntilUt.Value); }
            w.EndObject();
        }

        internal static Prompt Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Prompt(
                node.Field(path, "id").AsUInt(path + ".id"),
                Wire.ReadPromptKind(node.Field(path, "kind").AsString(path + ".kind"), path + ".kind"),
                node.Field(path, "operationId").AsString(path + ".operationId"),
                node.Field(path, "vesselId").AsUInt(path + ".vesselId"),
                node.Field(path, "text").AsString(path + ".text"),
                node.Field(path, "untilUt").Kind == JsonKind.Null ? null : (double?)node.Field(path, "untilUt").AsNumber(path + ".untilUt"));
        }
    }

    /// <summary>warp: time warp to the next burn, or through every burn of a group operation. switch: switch to the vessel that burns next. fly: let FLTCOM fly the burn of the vessel the player is flying.</summary>
    public enum PromptKind
    {
        Warp,
        Switch,
        Fly,
    }

    /// <summary>A saved, named group of vessels that plans and flies as one unit.</summary>
    public sealed class Group
    {
        private static readonly string[] Fields = { "id", "name", "vesselIds" };

        public Group(string id, string name, IReadOnlyList<uint> vesselIds)
        {
            Id = id ?? throw new ArgumentNullException("id");
            Name = name ?? throw new ArgumentNullException("name");
            VesselIds = Require.Values(vesselIds, "vesselIds");
        }

        public string Id { get; }

        public string Name { get; }

        public IReadOnlyList<uint> VesselIds { get; }

        internal void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("id"); w.String(Id);
            w.Name("name"); w.String(Name);
            w.Name("vesselIds"); w.BeginArray(); foreach (uint item0 in VesselIds) { w.Number((long)item0); } w.EndArray();
            w.EndObject();
        }

        internal static Group Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new Group(
                node.Field(path, "id").AsString(path + ".id"),
                node.Field(path, "name").AsString(path + ".name"),
                node.Field(path, "vesselIds").AsArray(path + ".vesselIds", (n0, i0) => n0.AsUInt(path + ".vesselIds" + "[" + i0 + "]")));
        }
    }

    /// <summary>Every saved group. Sent on connect and on each change; the display replaces its groups with each one.</summary>
    public sealed class GroupRoster : ServerMessage
    {
        private static readonly string[] Fields = { "protocolVersion", "type", "groups" };

        public GroupRoster(IReadOnlyList<Group> groups)
        {
            Groups = Require.UniqueBy(Require.Items(groups, "groups"), item => item.Id, "groups");
        }

        public IReadOnlyList<Group> Groups { get; }

        public override T Match<T>(Func<ServerHello, T> hello, Func<Fleet, T> fleet, Func<CraftLayout, T> layout, Func<Pong, T> pong, Func<Bye, T> bye, Func<BriefingMessage, T> briefing, Func<Missions, T> missions, Func<GroupRoster, T> groups) => (groups ?? throw new ArgumentNullException(nameof(groups)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);
            w.Name("type"); w.String("groups");
            w.Name("groups"); w.BeginArray(); foreach (Group item0 in Groups) { item0.Write(w); } w.EndArray();
            w.EndObject();
        }

        internal static new GroupRoster Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new GroupRoster(
                JsonNode.UniqueBy(node.Field(path, "groups").AsArray(path + ".groups", (n0, i0) => Group.Read(n0, path + ".groups" + "[" + i0 + "]")), item => item.Id, path + ".groups", "id"));
        }
    }

    public enum GroupAction
    {
        Create,
        Rename,
        Delete,
        Add,
        Remove,
    }

    /// <summary>Create, rename or delete a group, or add or remove members. create uses name and vesselIds; rename uses groupId and name; add and remove use groupId and vesselIds.</summary>
    public sealed class GroupEdit : ClientMessage
    {
        private static readonly string[] Fields = { "protocolVersion", "type", "action", "groupId", "name", "vesselIds" };

        public GroupEdit(GroupAction action, string? groupId, string? name, IReadOnlyList<uint> vesselIds)
        {
            Action = Require.Defined(action, "action");
            GroupId = groupId;
            Name = name;
            VesselIds = Require.Values(vesselIds, "vesselIds");
        }

        public GroupAction Action { get; }

        public string? GroupId { get; }

        public string? Name { get; }

        public IReadOnlyList<uint> VesselIds { get; }

        public override T Match<T>(Func<ClientHello, T> hello, Func<Ping, T> ping, Func<BriefRequest, T> brief, Func<Approve, T> approve, Func<Scrub, T> scrub, Func<Resume, T> resume, Func<Answer, T> answer, Func<GroupEdit, T> groupEdit) => (groupEdit ?? throw new ArgumentNullException(nameof(groupEdit)))(this);

        internal override void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("protocolVersion"); w.Number((long)ProtocolInfo.Version);
            w.Name("type"); w.String("groupEdit");
            w.Name("action"); w.String(Wire.Name(Action));
            w.Name("groupId"); if (GroupId is null) { w.Null(); } else { w.String(GroupId); }
            w.Name("name"); if (Name is null) { w.Null(); } else { w.String(Name); }
            w.Name("vesselIds"); w.BeginArray(); foreach (uint item0 in VesselIds) { w.Number((long)item0); } w.EndArray();
            w.EndObject();
        }

        internal static new GroupEdit Read(JsonNode node, string path)
        {
            node.ExpectFields(path, Fields);
            return new GroupEdit(
                Wire.ReadGroupAction(node.Field(path, "action").AsString(path + ".action"), path + ".action"),
                node.Field(path, "groupId").Kind == JsonKind.Null ? null : node.Field(path, "groupId").AsString(path + ".groupId"),
                node.Field(path, "name").Kind == JsonKind.Null ? null : node.Field(path, "name").AsString(path + ".name"),
                node.Field(path, "vesselIds").AsArray(path + ".vesselIds", (n0, i0) => n0.AsUInt(path + ".vesselIds" + "[" + i0 + "]")));
        }
    }

    internal static class Wire
    {
        public static string Name(ByeReason value)
        {
            switch (value)
            {
                case ByeReason.Shutdown: return "shutdown";
                case ByeReason.ProtocolMismatch: return "protocolMismatch";
                case ByeReason.MultiplayerSession: return "multiplayerSession";
                case ByeReason.UnsupportedPhysics: return "unsupportedPhysics";
                case ByeReason.UnsupportedScale: return "unsupportedScale";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static ByeReason ReadByeReason(string value, string path)
        {
            switch (value)
            {
                case "shutdown": return ByeReason.Shutdown;
                case "protocolMismatch": return ByeReason.ProtocolMismatch;
                case "multiplayerSession": return ByeReason.MultiplayerSession;
                case "unsupportedPhysics": return ByeReason.UnsupportedPhysics;
                case "unsupportedScale": return ByeReason.UnsupportedScale;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of shutdown, protocolMismatch, multiplayerSession, unsupportedPhysics, unsupportedScale");
            }
        }

        public static string Name(SpaceObjectKind value)
        {
            switch (value)
            {
                case SpaceObjectKind.Asteroid: return "asteroid";
                case SpaceObjectKind.Comet: return "comet";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static SpaceObjectKind ReadSpaceObjectKind(string value, string path)
        {
            switch (value)
            {
                case "asteroid": return SpaceObjectKind.Asteroid;
                case "comet": return SpaceObjectKind.Comet;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of asteroid, comet");
            }
        }

        public static string Name(SizeClass value)
        {
            switch (value)
            {
                case SizeClass.A: return "a";
                case SizeClass.B: return "b";
                case SizeClass.C: return "c";
                case SizeClass.D: return "d";
                case SizeClass.E: return "e";
                case SizeClass.F: return "f";
                case SizeClass.G: return "g";
                case SizeClass.H: return "h";
                case SizeClass.I: return "i";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static SizeClass ReadSizeClass(string value, string path)
        {
            switch (value)
            {
                case "a": return SizeClass.A;
                case "b": return SizeClass.B;
                case "c": return SizeClass.C;
                case "d": return SizeClass.D;
                case "e": return SizeClass.E;
                case "f": return SizeClass.F;
                case "g": return SizeClass.G;
                case "h": return SizeClass.H;
                case "i": return SizeClass.I;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of a, b, c, d, e, f, g, h, i");
            }
        }

        public static string Name(VesselType value)
        {
            switch (value)
            {
                case VesselType.Debris: return "debris";
                case VesselType.SpaceObject: return "spaceObject";
                case VesselType.Unknown: return "unknown";
                case VesselType.Probe: return "probe";
                case VesselType.Relay: return "relay";
                case VesselType.Rover: return "rover";
                case VesselType.Lander: return "lander";
                case VesselType.Ship: return "ship";
                case VesselType.Plane: return "plane";
                case VesselType.Station: return "station";
                case VesselType.Base: return "base";
                case VesselType.Eva: return "eva";
                case VesselType.Flag: return "flag";
                case VesselType.DeployedScienceController: return "deployedScienceController";
                case VesselType.DeployedSciencePart: return "deployedSciencePart";
                case VesselType.DroppedPart: return "droppedPart";
                case VesselType.DeployedGroundPart: return "deployedGroundPart";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static VesselType ReadVesselType(string value, string path)
        {
            switch (value)
            {
                case "debris": return VesselType.Debris;
                case "spaceObject": return VesselType.SpaceObject;
                case "unknown": return VesselType.Unknown;
                case "probe": return VesselType.Probe;
                case "relay": return VesselType.Relay;
                case "rover": return VesselType.Rover;
                case "lander": return VesselType.Lander;
                case "ship": return VesselType.Ship;
                case "plane": return VesselType.Plane;
                case "station": return VesselType.Station;
                case "base": return VesselType.Base;
                case "eva": return VesselType.Eva;
                case "flag": return VesselType.Flag;
                case "deployedScienceController": return VesselType.DeployedScienceController;
                case "deployedSciencePart": return VesselType.DeployedSciencePart;
                case "droppedPart": return VesselType.DroppedPart;
                case "deployedGroundPart": return VesselType.DeployedGroundPart;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of debris, spaceObject, unknown, probe, relay, rover, lander, ship, plane, station, base, eva, flag, deployedScienceController, deployedSciencePart, droppedPart, deployedGroundPart");
            }
        }

        public static string Name(OrbitalSituation value)
        {
            switch (value)
            {
                case OrbitalSituation.Flying: return "flying";
                case OrbitalSituation.SubOrbital: return "subOrbital";
                case OrbitalSituation.Orbiting: return "orbiting";
                case OrbitalSituation.Escaping: return "escaping";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static OrbitalSituation ReadOrbitalSituation(string value, string path)
        {
            switch (value)
            {
                case "flying": return OrbitalSituation.Flying;
                case "subOrbital": return OrbitalSituation.SubOrbital;
                case "orbiting": return OrbitalSituation.Orbiting;
                case "escaping": return OrbitalSituation.Escaping;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of flying, subOrbital, orbiting, escaping");
            }
        }

        public static string Name(SurfaceSituation value)
        {
            switch (value)
            {
                case SurfaceSituation.Prelaunch: return "prelaunch";
                case SurfaceSituation.Landed: return "landed";
                case SurfaceSituation.Splashed: return "splashed";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static SurfaceSituation ReadSurfaceSituation(string value, string path)
        {
            switch (value)
            {
                case "prelaunch": return SurfaceSituation.Prelaunch;
                case "landed": return SurfaceSituation.Landed;
                case "splashed": return SurfaceSituation.Splashed;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of prelaunch, landed, splashed");
            }
        }

        public static string Name(CommsState value)
        {
            switch (value)
            {
                case CommsState.Link: return "link";
                case CommsState.Los: return "los";
                case CommsState.None: return "none";
                case CommsState.Off: return "off";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static CommsState ReadCommsState(string value, string path)
        {
            switch (value)
            {
                case "link": return CommsState.Link;
                case "los": return CommsState.Los;
                case "none": return CommsState.None;
                case "off": return CommsState.Off;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of link, los, none, off");
            }
        }

        public static string Name(PartCategory value)
        {
            switch (value)
            {
                case PartCategory.Engine: return "engine";
                case PartCategory.Tank: return "tank";
                case PartCategory.Pod: return "pod";
                case PartCategory.Decoupler: return "decoupler";
                case PartCategory.Other: return "other";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static PartCategory ReadPartCategory(string value, string path)
        {
            switch (value)
            {
                case "engine": return PartCategory.Engine;
                case "tank": return PartCategory.Tank;
                case "pod": return PartCategory.Pod;
                case "decoupler": return PartCategory.Decoupler;
                case "other": return PartCategory.Other;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of engine, tank, pod, decoupler, other");
            }
        }

        public static string Name(DeltaVSource value)
        {
            switch (value)
            {
                case DeltaVSource.Stock: return "stock";
                case DeltaVSource.Estimate: return "estimate";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static DeltaVSource ReadDeltaVSource(string value, string path)
        {
            switch (value)
            {
                case "stock": return DeltaVSource.Stock;
                case "estimate": return DeltaVSource.Estimate;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of stock, estimate");
            }
        }

        public static string Name(ChangeKind value)
        {
            switch (value)
            {
                case ChangeKind.Circularize: return "circularize";
                case ChangeKind.ChangeApoapsis: return "changeApoapsis";
                case ChangeKind.ChangePeriapsis: return "changePeriapsis";
                case ChangeKind.ChangeInclination: return "changeInclination";
                case ChangeKind.CircularOrbit: return "circularOrbit";
                case ChangeKind.StandardLowOrbit: return "standardLowOrbit";
                case ChangeKind.RaiseApoapsis: return "raiseApoapsis";
                case ChangeKind.SpreadEvenly: return "spreadEvenly";
                case ChangeKind.Launch: return "launch";
                case ChangeKind.Transfer: return "transfer";
                case ChangeKind.Rendezvous: return "rendezvous";
                case ChangeKind.Dock: return "dock";
                case ChangeKind.Land: return "land";
                case ChangeKind.DeployConstellation: return "deployConstellation";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static ChangeKind ReadChangeKind(string value, string path)
        {
            switch (value)
            {
                case "circularize": return ChangeKind.Circularize;
                case "changeApoapsis": return ChangeKind.ChangeApoapsis;
                case "changePeriapsis": return ChangeKind.ChangePeriapsis;
                case "changeInclination": return ChangeKind.ChangeInclination;
                case "circularOrbit": return ChangeKind.CircularOrbit;
                case "standardLowOrbit": return ChangeKind.StandardLowOrbit;
                case "raiseApoapsis": return ChangeKind.RaiseApoapsis;
                case "spreadEvenly": return ChangeKind.SpreadEvenly;
                case "launch": return ChangeKind.Launch;
                case "transfer": return ChangeKind.Transfer;
                case "rendezvous": return ChangeKind.Rendezvous;
                case "dock": return ChangeKind.Dock;
                case "land": return ChangeKind.Land;
                case "deployConstellation": return ChangeKind.DeployConstellation;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of circularize, changeApoapsis, changePeriapsis, changeInclination, circularOrbit, standardLowOrbit, raiseApoapsis, spreadEvenly, launch, transfer, rendezvous, dock, land, deployConstellation");
            }
        }

        public static string Name(SurfaceMarkerKind value)
        {
            switch (value)
            {
                case SurfaceMarkerKind.Waypoint: return "waypoint";
                case SurfaceMarkerKind.Contract: return "contract";
                case SurfaceMarkerKind.Flag: return "flag";
                case SurfaceMarkerKind.Vessel: return "vessel";
                case SurfaceMarkerKind.Site: return "site";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static SurfaceMarkerKind ReadSurfaceMarkerKind(string value, string path)
        {
            switch (value)
            {
                case "waypoint": return SurfaceMarkerKind.Waypoint;
                case "contract": return SurfaceMarkerKind.Contract;
                case "flag": return SurfaceMarkerKind.Flag;
                case "vessel": return SurfaceMarkerKind.Vessel;
                case "site": return SurfaceMarkerKind.Site;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of waypoint, contract, flag, vessel, site");
            }
        }

        public static string Name(LandingSite value)
        {
            switch (value)
            {
                case LandingSite.LaunchPad: return "launchPad";
                case LandingSite.Runway: return "runway";
                case LandingSite.Custom: return "custom";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static LandingSite ReadLandingSite(string value, string path)
        {
            switch (value)
            {
                case "launchPad": return LandingSite.LaunchPad;
                case "runway": return LandingSite.Runway;
                case "custom": return LandingSite.Custom;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of launchPad, runway, custom");
            }
        }

        public static string Name(DeployMethod value)
        {
            switch (value)
            {
                case DeployMethod.Resonant: return "resonant";
                case DeployMethod.CarrierPhasing: return "carrierPhasing";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static DeployMethod ReadDeployMethod(string value, string path)
        {
            switch (value)
            {
                case "resonant": return DeployMethod.Resonant;
                case "carrierPhasing": return DeployMethod.CarrierPhasing;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of resonant, carrierPhasing");
            }
        }

        public static string Name(CarrierReturnKind value)
        {
            switch (value)
            {
                case CarrierReturnKind.Land: return "land";
                case CarrierReturnKind.TransferAndLand: return "transferAndLand";
                case CarrierReturnKind.Disposal: return "disposal";
                case CarrierReturnKind.Stay: return "stay";
                case CarrierReturnKind.BurnUp: return "burnUp";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static CarrierReturnKind ReadCarrierReturnKind(string value, string path)
        {
            switch (value)
            {
                case "land": return CarrierReturnKind.Land;
                case "transferAndLand": return CarrierReturnKind.TransferAndLand;
                case "disposal": return CarrierReturnKind.Disposal;
                case "stay": return CarrierReturnKind.Stay;
                case "burnUp": return CarrierReturnKind.BurnUp;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of land, transferAndLand, disposal, stay, burnUp");
            }
        }

        public static string Name(EndOfLife value)
        {
            switch (value)
            {
                case EndOfLife.BurnUp: return "burnUp";
                case EndOfLife.LandHome: return "landHome";
                case EndOfLife.Park: return "park";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static EndOfLife ReadEndOfLife(string value, string path)
        {
            switch (value)
            {
                case "burnUp": return EndOfLife.BurnUp;
                case "landHome": return EndOfLife.LandHome;
                case "park": return EndOfLife.Park;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of burnUp, landHome, park");
            }
        }

        public static string Name(Alert value)
        {
            switch (value)
            {
                case Alert.Amber: return "amber";
                case Alert.Red: return "red";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static Alert ReadAlert(string value, string path)
        {
            switch (value)
            {
                case "amber": return Alert.Amber;
                case "red": return Alert.Red;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of amber, red");
            }
        }

        public static string Name(DescentPhase value)
        {
            switch (value)
            {
                case DescentPhase.Correct: return "correct";
                case DescentPhase.Coast: return "coast";
                case DescentPhase.Brake: return "brake";
                case DescentPhase.Final: return "final";
                case DescentPhase.Landed: return "landed";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static DescentPhase ReadDescentPhase(string value, string path)
        {
            switch (value)
            {
                case "correct": return DescentPhase.Correct;
                case "coast": return DescentPhase.Coast;
                case "brake": return DescentPhase.Brake;
                case "final": return DescentPhase.Final;
                case "landed": return DescentPhase.Landed;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of correct, coast, brake, final, landed");
            }
        }

        public static string Name(AlarmKind value)
        {
            switch (value)
            {
                case AlarmKind.Impact: return "impact";
                case AlarmKind.Landing: return "landing";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static AlarmKind ReadAlarmKind(string value, string path)
        {
            switch (value)
            {
                case "impact": return AlarmKind.Impact;
                case "landing": return AlarmKind.Landing;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of impact, landing");
            }
        }

        public static string Name(OperationState value)
        {
            switch (value)
            {
                case OperationState.Draft: return "draft";
                case OperationState.Briefed: return "briefed";
                case OperationState.Approved: return "approved";
                case OperationState.Uploaded: return "uploaded";
                case OperationState.Waiting: return "waiting";
                case OperationState.Executing: return "executing";
                case OperationState.Hold: return "hold";
                case OperationState.Done: return "done";
                case OperationState.Missed: return "missed";
                case OperationState.Scrubbed: return "scrubbed";
                case OperationState.Aborted: return "aborted";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static OperationState ReadOperationState(string value, string path)
        {
            switch (value)
            {
                case "draft": return OperationState.Draft;
                case "briefed": return OperationState.Briefed;
                case "approved": return OperationState.Approved;
                case "uploaded": return OperationState.Uploaded;
                case "waiting": return OperationState.Waiting;
                case "executing": return OperationState.Executing;
                case "hold": return OperationState.Hold;
                case "done": return OperationState.Done;
                case "missed": return OperationState.Missed;
                case "scrubbed": return OperationState.Scrubbed;
                case "aborted": return OperationState.Aborted;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of draft, briefed, approved, uploaded, waiting, executing, hold, done, missed, scrubbed, aborted");
            }
        }

        public static string Name(PromptKind value)
        {
            switch (value)
            {
                case PromptKind.Warp: return "warp";
                case PromptKind.Switch: return "switch";
                case PromptKind.Fly: return "fly";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static PromptKind ReadPromptKind(string value, string path)
        {
            switch (value)
            {
                case "warp": return PromptKind.Warp;
                case "switch": return PromptKind.Switch;
                case "fly": return PromptKind.Fly;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of warp, switch, fly");
            }
        }

        public static string Name(GroupAction value)
        {
            switch (value)
            {
                case GroupAction.Create: return "create";
                case GroupAction.Rename: return "rename";
                case GroupAction.Delete: return "delete";
                case GroupAction.Add: return "add";
                case GroupAction.Remove: return "remove";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static GroupAction ReadGroupAction(string value, string path)
        {
            switch (value)
            {
                case "create": return GroupAction.Create;
                case "rename": return GroupAction.Rename;
                case "delete": return GroupAction.Delete;
                case "add": return GroupAction.Add;
                case "remove": return GroupAction.Remove;
                default: throw new ProtocolException(path + ": unknown value '" + value + "', expected one of create, rename, delete, add, remove");
            }
        }

    }

    /// <summary>Reads and writes protocol messages. Reading checks the protocol version before the shape.</summary>
    public static class ProtocolJson
    {
        public static string Write(ServerMessage message)
        {
            var w = new JsonWriter();
            (message ?? throw new ArgumentNullException(nameof(message))).Write(w);
            return w.ToString();
        }

        /// <exception cref="ProtocolVersionException">The message has another protocol version.</exception>
        /// <exception cref="ProtocolException">The message is not valid JSON or does not match the schema.</exception>
        public static ServerMessage ReadServerMessage(string json)
        {
            JsonNode node = JsonNode.ParseMessage(json, ProtocolInfo.Version);
            return ServerMessage.Read(node, "$");
        }

        public static string Write(ClientMessage message)
        {
            var w = new JsonWriter();
            (message ?? throw new ArgumentNullException(nameof(message))).Write(w);
            return w.ToString();
        }

        /// <exception cref="ProtocolVersionException">The message has another protocol version.</exception>
        /// <exception cref="ProtocolException">The message is not valid JSON or does not match the schema.</exception>
        public static ClientMessage ReadClientMessage(string json)
        {
            JsonNode node = JsonNode.ParseMessage(json, ProtocolInfo.Version);
            return ClientMessage.Read(node, "$");
        }

    }
}

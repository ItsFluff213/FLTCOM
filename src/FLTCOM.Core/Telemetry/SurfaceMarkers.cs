using System;
using System.Collections.Generic;
using FLTCOM.Core.Landing;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Protocol;
using FLTCOM.Core.TwoBody;
using LandingTarget = FLTCOM.Core.Landing.LandingTarget;

namespace FLTCOM.Core.Telemetry
{
    /// <summary>A waypoint as the game holds it, in degrees. Custom ones the player placed; the rest come from contracts.</summary>
    public readonly struct WaypointSighting
    {
        public WaypointSighting(string key, string name, string body, double latitudeDeg, double longitudeDeg, bool isCustom)
        {
            Key = key;
            Name = name;
            Body = body;
            LatitudeDeg = latitudeDeg;
            LongitudeDeg = longitudeDeg;
            IsCustom = isCustom;
        }

        public string Key { get; }
        public string Name { get; }
        public string Body { get; }
        public double LatitudeDeg { get; }
        public double LongitudeDeg { get; }
        public bool IsCustom { get; }
    }

    /// <summary>A landed or splashed vessel, in degrees.</summary>
    public readonly struct GroundedVessel
    {
        public GroundedVessel(uint id, string name, VesselType type, string body, double latitudeDeg, double longitudeDeg)
        {
            Id = id;
            Name = name;
            Type = type;
            Body = body;
            LatitudeDeg = latitudeDeg;
            LongitudeDeg = longitudeDeg;
        }

        public uint Id { get; }
        public string Name { get; }
        public VesselType Type { get; }
        public string Body { get; }
        public double LatitudeDeg { get; }
        public double LongitudeDeg { get; }
    }

    /// <summary>Turns what the game marks on its surfaces into the wire's marker list, in a stable order.</summary>
    public static class SurfaceMarkers
    {
        public const string LaunchPadId = "site:launchPad";
        public const string RunwayId = "site:runway";

        /// <summary>Coordinates are rounded to a micro-radian (under a meter on Kerbin) so a resting vessel's jitter is not a change.</summary>
        private const int Places = 6;

        private static readonly HashSet<VesselType> NotLandmarks = new HashSet<VesselType>
        {
            VesselType.Debris, VesselType.SpaceObject, VesselType.DroppedPart,
            VesselType.DeployedScienceController, VesselType.DeployedSciencePart, VesselType.DeployedGroundPart,
        };

        public static List<SurfaceMarker> Collect(string? homeBody, IEnumerable<WaypointSighting> waypoints, IEnumerable<GroundedVessel> vessels)
        {
            var markers = new List<SurfaceMarker>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            void Add(string id, string name, SurfaceMarkerKind kind, string body, double latitude, double longitude)
            {
                if (ids.Add(id))
                {
                    markers.Add(new SurfaceMarker(id, string.IsNullOrWhiteSpace(name) ? kind.ToString() : name.Trim(), kind, body, Latitude(latitude), Longitude(longitude)));
                }
            }

            if (homeBody != null)
            {
                Add(LaunchPadId, "KSC Launch Pad", SurfaceMarkerKind.Site, homeBody, LandingTarget.LaunchPad.Latitude, LandingTarget.LaunchPad.Longitude);
                Add(RunwayId, "KSC Runway", SurfaceMarkerKind.Site, homeBody, LandingTarget.Runway.Latitude, LandingTarget.Runway.Longitude);
            }
            foreach (WaypointSighting waypoint in waypoints)
            {
                SurfaceMarkerKind kind = waypoint.IsCustom ? SurfaceMarkerKind.Waypoint : SurfaceMarkerKind.Contract;
                Add((waypoint.IsCustom ? "waypoint:" : "contract:") + waypoint.Key, waypoint.Name, kind, waypoint.Body, Angles.ToRadians(waypoint.LatitudeDeg), Angles.ToRadians(waypoint.LongitudeDeg));
            }
            foreach (GroundedVessel vessel in vessels)
            {
                if (NotLandmarks.Contains(vessel.Type))
                {
                    continue;
                }
                bool flag = vessel.Type == VesselType.Flag;
                Add((flag ? "flag:" : "vessel:") + vessel.Id, vessel.Name, flag ? SurfaceMarkerKind.Flag : SurfaceMarkerKind.Vessel, vessel.Body, Angles.ToRadians(vessel.LatitudeDeg), Angles.ToRadians(vessel.LongitudeDeg));
            }
            markers.Sort(Order);
            return markers;
        }

        public static bool Same(IReadOnlyList<SurfaceMarker> a, IReadOnlyList<SurfaceMarker> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                SurfaceMarker x = a[i];
                SurfaceMarker y = b[i];
                if (x.Id != y.Id || x.Name != y.Name || x.Kind != y.Kind || x.Body != y.Body || x.Latitude != y.Latitude || x.Longitude != y.Longitude)
                {
                    return false;
                }
            }
            return true;
        }

        private static int Order(SurfaceMarker a, SurfaceMarker b)
        {
            int byKind = KindRank(a.Kind).CompareTo(KindRank(b.Kind));
            if (byKind != 0)
            {
                return byKind;
            }
            int byName = string.CompareOrdinal(a.Name, b.Name);
            return byName != 0 ? byName : string.CompareOrdinal(a.Id, b.Id);
        }

        private static int KindRank(SurfaceMarkerKind kind) => kind switch
        {
            SurfaceMarkerKind.Site => 0,
            SurfaceMarkerKind.Waypoint => 1,
            SurfaceMarkerKind.Contract => 2,
            SurfaceMarkerKind.Flag => 3,
            _ => 4,
        };

        private static double Latitude(double radians) => Math.Round(Math.Max(-Math.PI / 2, Math.Min(Math.PI / 2, radians)), Places);

        /// <summary>East-positive, in (-π, π].</summary>
        private static double Longitude(double radians)
        {
            double wrapped = Angles.Wrap(radians);
            return Math.Round(wrapped > Math.PI ? wrapped - Angles.TwoPi : wrapped, Places);
        }
    }

    /// <summary>
    /// Decides when a fleet snapshot carries the marker list: when it changed, and every <see cref="RefreshEvery"/>
    /// snapshots so a display that connected since still gets it. Otherwise null, and the display keeps what it has.
    /// </summary>
    public sealed class MarkerFeed
    {
        public const int RefreshEvery = 10;

        private IReadOnlyList<SurfaceMarker>? _sent;
        private int _since;

        public MarkerList? Next(IReadOnlyList<SurfaceMarker> markers)
        {
            if (_sent != null && ++_since < RefreshEvery && SurfaceMarkers.Same(_sent, markers))
            {
                return null;
            }
            _sent = markers;
            _since = 0;
            return new MarkerList(markers);
        }
    }

    /// <summary>A stock waypoint FLTCOM places on a landing target. Angles in radians.</summary>
    public sealed class LandingMark : IEquatable<LandingMark>
    {
        public LandingMark(string operationId, string name, string body, double latitude, double longitude)
        {
            OperationId = operationId;
            Name = name;
            Body = body;
            Latitude = latitude;
            Longitude = longitude;
        }

        public string OperationId { get; }
        public string Name { get; }
        public string Body { get; }
        public double Latitude { get; }
        public double Longitude { get; }

        public bool Equals(LandingMark? other) =>
            other != null && OperationId == other.OperationId && Name == other.Name && Body == other.Body && Latitude == other.Latitude && Longitude == other.Longitude;

        public override bool Equals(object? obj) => Equals(obj as LandingMark);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(OperationId);
    }

    /// <summary>
    /// Which landing waypoints to place and which to take away. Only marks FLTCOM placed itself are ever taken away,
    /// so a waypoint the player or a contract made is never touched.
    /// </summary>
    public static class LandingWaypointPlan
    {
        /// <summary>The waypoint name for an operation, so the player can tell it on the map and the navball.</summary>
        public static string NameFor(string codename) => "FLTCOM " + codename + " landing";

        /// <summary>
        /// One mark per approved landing that has not finished. The pad and the runway are on the home body; a custom
        /// target is on the body the vessel is at, which <paramref name="bodyOfVessel"/> gives, or null when unknown.
        /// </summary>
        public static List<LandingMark> Wanted(IEnumerable<Operation> operations, string? homeBody, Func<string, string?> bodyOfVessel)
        {
            var marks = new List<LandingMark>();
            foreach (Operation op in operations)
            {
                LandingTarget? target = op.Change.Landing;
                if (op.Change.Kind != OrbitChangeKind.Land || target == null || op.VesselIds.Count == 0 || StepMachine.IsEditable(op.State) || StepMachine.IsTerminal(op.State))
                {
                    continue;
                }
                string? body = target.Site == LandingSiteKind.Custom ? bodyOfVessel(op.VesselIds[0]) : homeBody;
                if (body != null)
                {
                    marks.Add(new LandingMark(op.Id, NameFor(op.Codename), body, target.Latitude, target.Longitude));
                }
            }
            return marks;
        }

        public static (List<LandingMark> Add, List<LandingMark> Remove) Diff(IEnumerable<LandingMark> wanted, IEnumerable<LandingMark> placed)
        {
            var want = new HashSet<LandingMark>(wanted);
            var have = new HashSet<LandingMark>(placed);
            var add = new List<LandingMark>();
            var remove = new List<LandingMark>();
            foreach (LandingMark mark in want)
            {
                if (!have.Contains(mark))
                {
                    add.Add(mark);
                }
            }
            foreach (LandingMark mark in have)
            {
                if (!want.Contains(mark))
                {
                    remove.Add(mark);
                }
            }
            return (add, remove);
        }
    }
}

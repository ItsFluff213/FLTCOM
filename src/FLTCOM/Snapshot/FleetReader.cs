using System;
using System.Collections.Generic;
using System.Globalization;
using FLTCOM.Art;
using FLTCOM.Core.Telemetry;
using FLTCOM.Core.Protocol;
using FLTCOM.Execution;
using Fleet = FLTCOM.Core.Protocol.Fleet;
using WireVessel = FLTCOM.Core.Protocol.Vessel;
using UnityEngine;
using WireVesselType = FLTCOM.Core.Protocol.VesselType;

namespace FLTCOM.Snapshot
{
    /// <summary>
    /// Reads the whole fleet into one snapshot. Where the scene has Vessel objects (flight, tracking station, space
    /// center) they are the source; elsewhere, such as the editor, the saved ProtoVessels are.
    /// </summary>
    internal sealed class FleetReader
    {
        private readonly LayoutReader _layouts;
        private readonly Dictionary<uint, (CommsState State, double? Strength)> _lastComms = new Dictionary<uint, (CommsState, double?)>();
        private readonly CommsFilter _commsFilter = new CommsFilter();
        private readonly MarkerFeed _markerFeed = new MarkerFeed();
        private static readonly Dictionary<string, (int Capacity, int? MinimumCrew)> PrefabCrews = new Dictionary<string, (int, int?)>();
        private uint _seq;

        public FleetReader()
        {
            _layouts = new LayoutReader(Models);
        }

        public CraftModels Models { get; } = new CraftModels();

        public BodyTextures Textures { get; } = new BodyTextures();

        public (Fleet Fleet, List<CraftLayout> Changed, List<uint> Gone) Read()
        {
            double ut = Planetarium.GetUniversalTime();
            string agency = FleetScenario.Agency;
            var vessels = new List<WireVessel>();
            var changed = new List<CraftLayout>();
            var present = new HashSet<uint>();
            var grounded = new List<GroundedVessel>();

            if (FlightGlobals.Vessels.Count > 0)
            {
                ReadComms();
                foreach (global::Vessel vessel in FlightGlobals.Vessels)
                {
                    if (vessel == null || vessel.protoVessel == null && !vessel.loaded || !present.Add(vessel.persistentId))
                    {
                        continue;
                    }
                    WireVessel wire = FromVessel(vessel, agency, ut);
                    vessels.Add(wire);
                    AddGrounded(grounded, wire);
                    CraftLayout? layout = _layouts.Changed(vessel);
                    if (layout != null)
                    {
                        changed.Add(layout);
                    }
                }
            }
            else if (HighLogic.CurrentGame?.flightState != null)
            {
                foreach (ProtoVessel proto in HighLogic.CurrentGame.flightState.protoVessels)
                {
                    if (present.Add(proto.persistentId))
                    {
                        WireVessel wire = FromProto(proto, agency, ut);
                        vessels.Add(wire);
                        AddGrounded(grounded, wire);
                    }
                }
            }

            List<uint> gone = _layouts.Retain(present);
            foreach (uint id in gone)
            {
                _lastComms.Remove(id);
            }
            global::Vessel? active = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
            List<SurfaceMarker> markers = SurfaceMarkers.Collect(FlightGlobals.GetHomeBodyName(), Waypoints(), grounded);
            var fleet = new Fleet(++_seq, ut, TimeWarp.CurrentRate, active != null ? active.persistentId : (uint?)null, BodyReader.ReadAll(ut, Textures), vessels, _markerFeed.Next(markers));
            return (fleet, changed, gone);
        }

        private static void AddGrounded(List<GroundedVessel> grounded, WireVessel vessel)
        {
            if (vessel.Location is SurfaceLocation surface && surface.Situation != SurfaceSituation.Prelaunch)
            {
                grounded.Add(new GroundedVessel(vessel.Id, vessel.Name, vessel.VesselType, surface.Body, surface.Latitude * 180 / Math.PI, surface.Longitude * 180 / Math.PI));
            }
        }

        private static List<WaypointSighting> Waypoints()
        {
            var sightings = new List<WaypointSighting>();
            FinePrint.WaypointManager? manager = FinePrint.WaypointManager.Instance();
            if (manager == null)
            {
                return sightings;
            }
            foreach (FinePrint.Waypoint waypoint in manager.Waypoints)
            {
                if (waypoint == null || string.IsNullOrEmpty(waypoint.celestialName))
                {
                    continue;
                }
                string name = waypoint.isCustom || string.IsNullOrEmpty(waypoint.FullName) ? waypoint.name : waypoint.FullName;
                string key = waypoint.navigationId != Guid.Empty
                    ? waypoint.navigationId.ToString("N")
                    : string.Join(":", waypoint.celestialName, name, waypoint.latitude.ToString("R", CultureInfo.InvariantCulture), waypoint.longitude.ToString("R", CultureInfo.InvariantCulture));
                sightings.Add(new WaypointSighting(key, name, waypoint.celestialName, waypoint.latitude, waypoint.longitude, waypoint.isCustom));
            }
            return sightings;
        }

        private void ReadComms()
        {
            var raw = new List<(uint Id, CommsState State, double? Strength)>();
            foreach (global::Vessel vessel in FlightGlobals.Vessels)
            {
                if (vessel == null || vessel.protoVessel == null && !vessel.loaded)
                {
                    continue;
                }
                var (state, strength) = CommsReader.Read(vessel, TypeOf(vessel.vesselType));
                raw.Add((vessel.persistentId, state, strength));
            }
            var filtered = _commsFilter.Filter(UnityEngine.Time.realtimeSinceStartup, raw);
            for (int i = 0; i < raw.Count; i++)
            {
                _lastComms[raw[i].Id] = filtered[i];
            }
        }

        private WireVessel FromVessel(global::Vessel vessel, string agency, double ut)
        {
            WireVesselType type = TypeOf(vessel.vesselType);
            (CommsState state, double? strength) comms = _lastComms.TryGetValue(vessel.persistentId, out var known) ? known : (CommsState.None, null);
            Location location = Where(vessel.situation, vessel.mainBody, vessel.latitude, vessel.longitude, vessel.altitude, vessel.orbit, ut);
            if (vessel.loaded)
            {
                return new WireVessel(
                    vessel.persistentId,
                    RegistryCode.For(agency, vessel.persistentId, type),
                    vessel.vesselName,
                    type,
                    location,
                    true,
                    vessel.IsControllable,
                    comms.state,
                    comms.strength,
                    vessel.GetCrewCapacity(),
                    Crew(vessel.GetVesselCrew()),
                    LoadedResources(vessel),
                    vessel.totalMass * 1000,
                    StockDeltaV(vessel),
                    AttitudeOf(vessel.mainBody, Quaternion.Inverse(vessel.mainBody.bodyTransform.rotation) * vessel.transform.rotation, ut),
                    VesselPorts.Wire(VesselPorts.Of(vessel)),
                    type == WireVesselType.SpaceObject ? SpaceRock.Of(type, vessel.DiscoveryInfo != null ? (int)vessel.DiscoveryInfo.objectSize : (int?)null, vessel.FindPartModuleImplementing<ModuleComet>() != null) : null);
            }
            return Unloaded(vessel.protoVessel, agency, type, location, comms, AttitudeOf(vessel.mainBody, vessel.srfRelRotation, ut));
        }

        private WireVessel FromProto(ProtoVessel proto, string agency, double ut)
        {
            WireVesselType type = TypeOf(proto.vesselType);
            OrbitSnapshot snapshot = proto.orbitSnapShot;
            CelestialBody body = FlightGlobals.Bodies[snapshot.ReferenceBodyIndex];
            Location location = Where(proto.situation, body, proto.latitude, proto.longitude, proto.altitude, snapshot.Load(), ut);
            if (!_lastComms.TryGetValue(proto.persistentId, out var comms))
            {
                comms = (CommsState.None, null);
            }
            return Unloaded(proto, agency, type, location, comms, AttitudeOf(body, proto.rotation, ut));
        }

        private static Quat AttitudeOf(CelestialBody body, Quaternion surfaceRelative, double ut) =>
            Attitude.ToOrbitFrame(new Quat(surfaceRelative.x, surfaceRelative.y, surfaceRelative.z, surfaceRelative.w), BodyReader.RotationAngle(body, ut));

        private static WireVessel Unloaded(ProtoVessel proto, string agency, WireVesselType type, Location location, (CommsState state, double? strength) comms, Quat attitude)
        {
            int crewCapacity = 0;
            bool controllable = false;
            double massTons = 0;
            var totals = new SortedDictionary<string, (double Amount, double Capacity)>(StringComparer.Ordinal);
            foreach (ProtoPartSnapshot part in proto.protoPartSnapshots)
            {
                massTons += part.mass;
                foreach (ProtoPartResourceSnapshot resource in part.resources)
                {
                    Add(totals, resource.resourceName, resource.amount, resource.maxAmount);
                    massTons += resource.amount * (resource.definition?.density ?? 0);
                }
                if (part.partInfo?.partPrefab == null)
                {
                    continue;
                }
                (int capacity, int? minimumCrew) = PrefabCrew(part.partInfo);
                crewCapacity += capacity;
                if (Core.Constellation.CommandPart.Controls(minimumCrew, part.protoModuleCrew.Count))
                {
                    controllable = true;
                }
            }
            return new WireVessel(
                proto.persistentId,
                RegistryCode.For(agency, proto.persistentId, type),
                proto.vesselName,
                type,
                location,
                false,
                controllable,
                comms.state,
                comms.strength,
                crewCapacity,
                Crew(proto.GetVesselCrew()),
                Resources(totals),
                massTons * 1000,
                null,
                attitude,
                VesselPorts.Wire(VesselPorts.Of(proto)),
                type == WireVesselType.SpaceObject ? SpaceRock.Of(type, SavedSize(proto), HasComet(proto)) : null);
        }

        private static int? SavedSize(ProtoVessel proto) =>
            int.TryParse(proto.discoveryInfo?.GetValue("size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int size) ? size : (int?)null;

        private static bool HasComet(ProtoVessel proto)
        {
            foreach (ProtoPartSnapshot part in proto.protoPartSnapshots)
            {
                foreach (ProtoPartModuleSnapshot module in part.modules)
                {
                    if (module.moduleName == "ModuleComet")
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Crew seats and the command module's minimum crew (-1 without one) never change for a part, so they are read from the prefab once.</summary>
        private static (int Capacity, int? MinimumCrew) PrefabCrew(AvailablePart info)
        {
            if (!PrefabCrews.TryGetValue(info.name, out var known))
            {
                Part prefab = info.partPrefab;
                ModuleCommand? command = prefab.FindModuleImplementing<ModuleCommand>();
                known = (prefab.CrewCapacity, command?.minimumCrew);
                PrefabCrews[info.name] = known;
            }
            return known;
        }

        private static Location Where(global::Vessel.Situations situation, CelestialBody body, double latitude, double longitude, double altitude, global::Orbit orbit, double ut)
        {
            switch (situation)
            {
                case global::Vessel.Situations.LANDED:
                    return Surface(SurfaceSituation.Landed);
                case global::Vessel.Situations.SPLASHED:
                    return Surface(SurfaceSituation.Splashed);
                case global::Vessel.Situations.PRELAUNCH:
                    return Surface(SurfaceSituation.Prelaunch);
                case global::Vessel.Situations.FLYING:
                    return new OrbitalLocation(OrbitalSituation.Flying, BodyReader.WireOrbit(orbit, ut));
                case global::Vessel.Situations.SUB_ORBITAL:
                    return new OrbitalLocation(OrbitalSituation.SubOrbital, BodyReader.WireOrbit(orbit, ut));
                case global::Vessel.Situations.ESCAPING:
                    return new OrbitalLocation(OrbitalSituation.Escaping, BodyReader.WireOrbit(orbit, ut));
                default:
                    return new OrbitalLocation(OrbitalSituation.Orbiting, BodyReader.WireOrbit(orbit, ut));
            }

            SurfaceLocation Surface(SurfaceSituation resting) =>
                new SurfaceLocation(resting, body.bodyName, latitude * Math.PI / 180, longitude * Math.PI / 180, altitude);
        }

        private static WireVesselType TypeOf(global::VesselType type) =>
            Enum.TryParse(type.ToString(), true, out WireVesselType parsed) ? parsed : WireVesselType.Unknown;

        private static List<CrewMember> Crew(List<ProtoCrewMember> members)
        {
            var crew = new List<CrewMember>(members.Count);
            foreach (ProtoCrewMember member in members)
            {
                crew.Add(new CrewMember(member.name, member.trait, member.experienceLevel));
            }
            return crew;
        }

        private static List<Resource> LoadedResources(global::Vessel vessel)
        {
            var totals = new SortedDictionary<string, (double Amount, double Capacity)>(StringComparer.Ordinal);
            foreach (Part part in vessel.parts)
            {
                foreach (PartResource resource in part.Resources)
                {
                    Add(totals, resource.resourceName, resource.amount, resource.maxAmount);
                }
            }
            return Resources(totals);
        }

        private static void Add(SortedDictionary<string, (double Amount, double Capacity)> totals, string name, double amount, double capacity)
        {
            totals.TryGetValue(name, out var sum);
            totals[name] = (sum.Amount + amount, sum.Capacity + capacity);
        }

        private static List<Resource> Resources(SortedDictionary<string, (double Amount, double Capacity)> totals)
        {
            var resources = new List<Resource>(totals.Count);
            foreach (KeyValuePair<string, (double Amount, double Capacity)> total in totals)
            {
                resources.Add(new Resource(total.Key, total.Value.Amount, total.Value.Capacity));
            }
            return resources;
        }

        private static DeltaV? StockDeltaV(global::Vessel vessel)
        {
            VesselDeltaV? stock = vessel.VesselDeltaV;
            if (stock == null || stock.OperatingStageInfo == null || stock.OperatingStageInfo.Count == 0)
            {
                return null;
            }
            var stages = new List<StageDeltaV>();
            foreach (DeltaVStageInfo stage in stock.OperatingStageInfo)
            {
                bool hasThrust = stage.thrustVac > 0;
                stages.Add(new StageDeltaV(stage.stage, stage.deltaVinVac, hasThrust ? stage.TWRVac : (double?)null, hasThrust ? stage.stageBurnTime : (double?)null));
            }
            stages.Sort((a, b) => b.Stage.CompareTo(a.Stage));
            return new DeltaV(stock.TotalDeltaVVac, DeltaVSource.Stock, stages);
        }
    }
}

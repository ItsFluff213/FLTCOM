using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FLTCOM.Core.AutoSystems;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Execution
{
    /// <summary>Reads what the planner needs from a KSP vessel: its body, its orbit and its stages.</summary>
    internal static class VesselTasks
    {
        public static string Id(global::Vessel vessel) => vessel.persistentId.ToString(CultureInfo.InvariantCulture);

        public static global::Vessel? Find(string vesselId) =>
            FlightGlobals.Vessels.FirstOrDefault(v => v != null && Id(v) == vesselId);

        public static VesselTask Task(global::Vessel vessel, int? pinnedStage, OrbitChange? change = null)
        {
            double now = Planetarium.GetUniversalTime();
            return new VesselTask(Id(vessel), vessel.vesselName, Body(vessel.mainBody), vessel.orbit.eccentricity < 1 ? Orbit(vessel.orbit) : null, Stages(vessel), new List<int?> { pinnedStage },
                vessel.LandedOrSplashed ? Guidance.InertialFrame.Site(vessel, now) : null, System(), vessel.LandedOrSplashed ? (Patch?)null : Trajectory(vessel.orbit, now),
                change?.Landing is Core.Landing.LandingTarget target ? LandingReader.Context(vessel, target) : null,
                change?.Kind == OrbitChangeKind.DeployConstellation ? CarrierReader.Parts(vessel) : null);
        }

        /// <summary>Every body on rails with its orbit, in the same element frame as the vessels' orbits.</summary>
        public static SolarSystem System() =>
            new SolarSystem(FlightGlobals.Bodies.Select(b => b.orbit == null || b.referenceBody == null || b.referenceBody == b
                ? new SystemBody(Body(b), null, null)
                : new SystemBody(Body(b), b.referenceBody.bodyName, Orbit(b.orbit))));

        /// <summary>Where the vessel is now relative to its body, from its elements, closed orbit or escape path.</summary>
        public static Patch Trajectory(global::Orbit orbit, double ut) =>
            new Patch(orbit.referenceBody.bodyName, Kepler.FromElements(orbit.referenceBody.gravParameter, orbit.semiMajorAxis, orbit.eccentricity, orbit.inclination * UtilMath.Deg2Rad, orbit.LAN * UtilMath.Deg2Rad, orbit.argumentOfPeriapsis * UtilMath.Deg2Rad, orbit.meanAnomalyAtEpoch, orbit.epoch, ut), ut);

        public static CentralBody Body(CelestialBody body) =>
            new CentralBody(body.bodyName, body.gravParameter, body.Radius, body.atmosphere ? body.atmosphereDepth : 0, body.pqsController != null ? body.pqsController.mapMaxHeight : 0, body.sphereOfInfluence, body.rotationPeriod);

        public static OrbitElements Orbit(global::Orbit orbit) =>
            new OrbitElements(
                orbit.referenceBody.gravParameter,
                orbit.semiMajorAxis,
                orbit.eccentricity,
                orbit.inclination * UtilMath.Deg2Rad,
                orbit.LAN * UtilMath.Deg2Rad,
                orbit.argumentOfPeriapsis * UtilMath.Deg2Rad,
                orbit.meanAnomalyAtEpoch,
                orbit.epoch);

        /// <summary>Vacuum stage figures from the fuel flow simulation, for loaded and unloaded vessels alike. Falls back to the simple estimator if the simulation fails on an unusual part.</summary>
        public static IReadOnlyList<StageDeltaV> Stages(global::Vessel vessel)
        {
            try
            {
                return FlowSimulator.Estimate(FlowReader.Read(vessel));
            }
            catch (System.Exception e)
            {
                PluginFiles.Log("fuel flow simulation failed on " + vessel.vesselName + ", using the simple estimate: " + e.Message);
                return DeltaVEstimator.Estimate(Parts(vessel));
            }
        }

        /// <summary>The vessel's parts as the estimator reads them, from live parts when loaded and from the save otherwise.</summary>
        public static IReadOnlyList<PartSnapshot> Parts(global::Vessel vessel)
        {
            if (vessel.loaded)
            {
                return vessel.parts.Select(p => Snapshot(p.partInfo.partPrefab, p.mass, DecoupledIn(p), p.Resources.Cast<global::PartResource>().Select(r => (r.resourceName, r.amount, (double)r.info.density)), p.inverseStage)).ToList();
            }
            List<ProtoPartSnapshot> protos = vessel.protoVessel.protoPartSnapshots;
            return protos.Select(p => Snapshot(p.partInfo.partPrefab, p.mass, DecoupledIn(protos, p), p.resources.Select(r => (r.resourceName, r.amount, (double)(r.definition?.density ?? 0))), p.inverseStageIndex)).ToList();
        }

        /// <summary>The vessel's parts as the stage plan and the stager read them: role, stage, and the stage that drops them.</summary>
        public static IReadOnlyList<StagePart> StageParts(global::Vessel vessel)
        {
            if (vessel.loaded)
            {
                return vessel.parts.Select(p => Classify(
                    p.partInfo.partPrefab,
                    p.protoModuleCrew.Count,
                    p.inverseStage,
                    DecoupledIn(p),
                    EngineOf(p),
                    p.Resources.Cast<global::PartResource>().Any(r => r.info.density > 0),
                    p.Resources.Cast<global::PartResource>().Any(r => r.info.density > 0 && r.amount >= 0.01))).ToList();
            }
            List<ProtoPartSnapshot> protos = vessel.protoVessel.protoPartSnapshots;
            return protos.Select(p => Classify(
                p.partInfo.partPrefab,
                p.protoModuleCrew.Count,
                p.inverseStageIndex,
                DecoupledIn(protos, p),
                EngineState.None,
                p.resources.Any(r => (r.definition?.density ?? 0) > 0),
                p.resources.Any(r => (r.definition?.density ?? 0) > 0 && r.amount >= 0.01))).ToList();
        }

        /// <summary>The stage the next staging leaves behind: KSP's current stage.</summary>
        public static int CurrentStage(global::Vessel vessel) =>
            vessel == FlightGlobals.ActiveVessel ? KSP.UI.Screens.StageManager.CurrentStage : vessel.loaded ? vessel.currentStage : vessel.protoVessel.stage;

        public static IEnumerable<Part> Prefabs(global::Vessel vessel) =>
            vessel.loaded ? vessel.parts.Select(p => p.partInfo.partPrefab) : vessel.protoVessel.protoPartSnapshots.Select(p => p.partInfo.partPrefab);

        private static StagePart Classify(Part prefab, int crew, int stage, int decoupledIn, EngineState engine, bool tank, bool fuelLeft)
        {
            PartRole roles = PartRole.None;
            if (prefab.FindModuleImplementing<ModuleEngines>() != null)
            {
                roles |= PartRole.Engine;
            }
            if (tank)
            {
                roles |= PartRole.Tank;
            }
            if (IsDecoupler(prefab))
            {
                roles |= PartRole.Decoupler;
            }
            if (prefab.FindModuleImplementing<ModuleProceduralFairing>() != null)
            {
                roles |= PartRole.Fairing;
            }
            if (prefab.FindModuleImplementing<LaunchClamp>() != null)
            {
                roles |= PartRole.Clamp;
            }
            if (prefab.FindModuleImplementing<ModuleParachute>() != null)
            {
                roles |= PartRole.Parachute;
            }
            if (Commands(prefab, crew))
            {
                roles |= PartRole.Command;
            }
            return new StagePart(prefab.partInfo.name, roles, stage, decoupledIn, engine, fuelLeft);
        }

        private static EngineState EngineOf(Part part)
        {
            List<ModuleEngines> engines = part.FindModulesImplementing<ModuleEngines>();
            if (engines.Count == 0)
            {
                return EngineState.None;
            }
            if (engines.Any(e => e.EngineIgnited && !e.flameout))
            {
                return EngineState.Burning;
            }
            return engines.Any(e => e.EngineIgnited) ? EngineState.Spent : EngineState.Unlit;
        }

        public static bool HasLink(global::Vessel vessel) =>
            !CommNet.CommNetScenario.CommNetEnabled || vessel.GetCrewCount() > 0 || vessel.connection?.IsConnectedHome == true;

        private static PartSnapshot Snapshot(Part prefab, double dryMass, int decoupledIn, IEnumerable<(string Name, double Amount, double Density)> resources, int inverseStage)
        {
            var held = resources.Select(r => new Core.Propulsion.PartResource(r.Name, r.Amount, r.Density)).ToList();
            ModuleEngines? engine = prefab.FindModulesImplementing<ModuleEngines>().FirstOrDefault();
            EngineData? data = engine == null
                ? null
                : new EngineData(
                    inverseStage,
                    engine.maxThrust,
                    engine.atmosphereCurve.Evaluate(0),
                    // Massless propellants (ElectricCharge on ion engines) add no mass to burn, so the core leaves them out.
                    engine.propellants.Where(p => !p.ignoreForIsp && (PartResourceLibrary.Instance.GetDefinition(p.name)?.density ?? 0) > 0).Select(p => new Core.Propulsion.Propellant(p.name, p.ratio)).ToList());
            return new PartSnapshot(prefab.partInfo.name, dryMass, decoupledIn, held, data);
        }

        public static bool Commands(Part prefab, int crew) =>
            Core.Constellation.CommandPart.Controls(prefab.FindModuleImplementing<ModuleCommand>()?.minimumCrew, crew);

        public static bool IsDecoupler(Part prefab) =>
            prefab.FindModuleImplementing<ModuleDecouple>() != null || prefab.FindModuleImplementing<ModuleAnchoredDecoupler>() != null;

        public static int DecoupledIn(Part part)
        {
            for (Part? p = part; p != null; p = p.parent)
            {
                if (IsDecoupler(p))
                {
                    return p.inverseStage;
                }
            }
            return -1;
        }

        private static int DecoupledIn(List<ProtoPartSnapshot> parts, ProtoPartSnapshot part)
        {
            for (ProtoPartSnapshot? p = part; p != null; p = p.parentIdx >= 0 && p.parentIdx < parts.Count && parts[p.parentIdx] != p ? parts[p.parentIdx] : null)
            {
                if (IsDecoupler(p.partInfo.partPrefab))
                {
                    return p.inverseStageIndex;
                }
            }
            return -1;
        }
    }
}

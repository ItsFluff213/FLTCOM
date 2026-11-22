using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Constellation;

namespace FLTCOM.Execution
{
    /// <summary>Reads a vessel's part tree for payload detection, from live parts when loaded and from the save otherwise.</summary>
    internal static class CarrierReader
    {
        public static IReadOnlyList<CarrierPart> Parts(global::Vessel vessel)
        {
            if (vessel.loaded)
            {
                return vessel.parts.Select(p => Read(
                    p.partInfo.partPrefab,
                    p.protoModuleCrew.Count,
                    p.persistentId,
                    p.parent?.persistentId,
                    p.partInfo.name,
                    p.mass + p.GetResourceMass(),
                    p.mass)).ToList();
            }
            List<ProtoPartSnapshot> protos = vessel.protoVessel.protoPartSnapshots;
            return protos.Select(p => Read(
                p.partInfo.partPrefab,
                p.protoModuleCrew.Count,
                p.persistentId,
                p.parentIdx >= 0 && p.parentIdx < protos.Count && protos[p.parentIdx] != p ? protos[p.parentIdx].persistentId : (uint?)null,
                p.partInfo.name,
                p.mass + p.resources.Sum(r => r.amount * (r.definition?.density ?? 0)),
                p.mass)).ToList();
        }

        private static CarrierPart Read(Part prefab, int crew, uint id, uint? parent, string name, double mass, double dry)
        {
            CarrierPartKind kinds = CarrierPartKind.None;
            if (VesselTasks.Commands(prefab, crew))
            {
                kinds |= CarrierPartKind.Command;
            }
            if (VesselTasks.IsDecoupler(prefab))
            {
                kinds |= CarrierPartKind.Separator;
            }
            if (prefab.FindModuleImplementing<ModuleDockingNode>() != null)
            {
                kinds |= CarrierPartKind.DockingPort;
            }
            if (prefab.FindModuleImplementing<ModuleParachute>() != null)
            {
                kinds |= CarrierPartKind.Parachute;
            }
            if (prefab.FindModuleImplementing<ModuleProceduralFairing>() != null)
            {
                kinds |= CarrierPartKind.Fairing;
            }
            ModuleEngines? engine = prefab.FindModuleImplementing<ModuleEngines>();
            if (engine != null)
            {
                kinds |= CarrierPartKind.Engine;
            }
            return new CarrierPart(id, parent, name, kinds, mass, dry, engine?.maxThrust ?? 0, engine?.atmosphereCurve.Evaluate(0) ?? 0);
        }
    }
}

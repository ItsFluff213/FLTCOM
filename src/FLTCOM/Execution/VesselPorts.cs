using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Protocol;
using UnityEngine;
using CoreVector = FLTCOM.Core.TwoBody.Vector3d;

namespace FLTCOM.Execution
{
    /// <summary>Reads a vessel's docking ports, loaded or on rails.</summary>
    internal static class VesselPorts
    {
        private const string Ready = "Ready";

        /// <summary>The ports of a loaded vessel, each facing given in the vessel's own frame.</summary>
        public static List<DockingPort> Of(global::Vessel vessel)
        {
            if (!vessel.loaded)
            {
                return vessel.protoVessel == null ? new List<DockingPort>() : Of(vessel.protoVessel);
            }
            Quaternion toVessel = Quaternion.Inverse(vessel.transform.rotation);
            return vessel.FindPartModulesImplementing<ModuleDockingNode>()
                .Where(n => n.part != null && n.nodeTransform != null)
                .Select(n =>
                {
                    Vector3 facing = toVessel * n.nodeTransform.forward;
                    return new DockingPort(n.part.persistentId, n.part.partInfo?.title ?? n.part.name, n.nodeType, n.state == Ready, new CoreVector(facing.x, facing.y, facing.z));
                })
                .ToList();
        }

        public static List<DockingPort> Of(ProtoVessel proto)
        {
            var ports = new List<DockingPort>();
            foreach (ProtoPartSnapshot part in proto.protoPartSnapshots)
            {
                ModuleDockingNode? prefab = part.partInfo?.partPrefab?.FindModuleImplementing<ModuleDockingNode>();
                if (prefab == null)
                {
                    continue;
                }
                string state = part.modules.FirstOrDefault(m => m.moduleName == nameof(ModuleDockingNode))?.moduleValues?.GetValue("state") ?? Ready;
                ports.Add(new DockingPort(part.persistentId, part.partInfo!.title, prefab.nodeType, state == Ready));
            }
            return ports;
        }

        /// <summary>Where <paramref name="other"/> lies seen from <paramref name="vessel"/>, in the frame port facings use; null unless both are loaded.</summary>
        public static CoreVector? Toward(global::Vessel vessel, global::Vessel other)
        {
            if (!vessel.loaded || !other.loaded)
            {
                return null;
            }
            Vector3 local = Quaternion.Inverse(vessel.transform.rotation) * (other.CoM - vessel.CoM);
            return new CoreVector(local.x, local.y, local.z);
        }

        public static Docking? Wire(IReadOnlyList<DockingPort> ports) =>
            ports.Count == 0 ? null : new Docking(ports.Select(p => new DockingPortInfo(p.Id, p.Name, p.Size, p.Free)).ToList());

        public static ModuleDockingNode? Node(global::Vessel vessel, uint partId) =>
            vessel.loaded ? vessel.FindPartModulesImplementing<ModuleDockingNode>().FirstOrDefault(n => n.part != null && n.part.persistentId == partId) : null;
    }
}

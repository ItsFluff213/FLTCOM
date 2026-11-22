using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Propulsion;

namespace FLTCOM.Execution
{
    /// <summary>Reads a vessel into the fuel flow simulation's model, from live parts when loaded and from the save otherwise.</summary>
    internal static class FlowReader
    {
        public static FlowVessel Read(global::Vessel vessel) => vessel.loaded ? FromLoaded(vessel) : FromSaved(vessel.protoVessel);

        private static FlowVessel FromLoaded(global::Vessel vessel)
        {
            var parts = new List<FlowPart>();
            foreach (Part p in vessel.parts)
            {
                FlowPart part = Make(p.persistentId, p.partInfo.name, p.inverseStage, p.mass, p.Resources.Cast<global::PartResource>().Select(r => (r.resourceName, r.amount, r.maxAmount, (double)r.info.density, r.flowState)), p.partInfo.partPrefab);
                part.IsRoot = p.parent == null;
                if (p.parent != null)
                {
                    part.Links.Add(p.parent.persistentId);
                }
                part.Links.AddRange(p.children.Select(c => c.persistentId));
                part.CrossFeed.AddRange(p.crossfeedPartSet.GetParts().Select(c => c.persistentId));
                ModuleDecouplerBase? decoupler = p.FindModuleImplementing<ModuleDecouplerBase>();
                if (decoupler != null)
                {
                    part.DecouplesId = decoupler.ExplosiveNode?.attachedPart?.persistentId;
                }
                parts.Add(part);
            }
            return new FlowVessel(System.Math.Max(0, vessel.currentStage), parts);
        }

        private static FlowVessel FromSaved(ProtoVessel proto)
        {
            List<ProtoPartSnapshot> saved = proto.protoPartSnapshots;
            var parts = new List<FlowPart>(saved.Count);
            foreach (ProtoPartSnapshot p in saved)
            {
                parts.Add(Make(p.persistentId, p.partInfo.name, p.inverseStageIndex, p.mass, p.resources.Select(r => (r.resourceName, r.amount, r.maxAmount, (double)(r.definition?.density ?? 0), r.flowState)), p.partInfo.partPrefab));
            }

            var neighbours = new List<int>[saved.Count];
            var crossfeed = new List<int>[saved.Count];
            for (int i = 0; i < saved.Count; i++)
            {
                neighbours[i] = new List<int>();
                crossfeed[i] = new List<int>();
            }
            for (int i = 0; i < saved.Count; i++)
            {
                int parent = saved[i].parentIdx;
                if (parent < 0 || parent >= saved.Count || parent == i)
                {
                    parts[i].IsRoot = true;
                    continue;
                }
                parts[i].Links.Add(parts[parent].Id);
                parts[parent].Links.Add(parts[i].Id);
                neighbours[i].Add(parent);
                neighbours[parent].Add(i);
                if (CarriesFuel(saved, i, parent))
                {
                    crossfeed[i].Add(parent);
                    crossfeed[parent].Add(i);
                }
            }

            for (int i = 0; i < saved.Count; i++)
            {
                foreach (int reached in Reach(i, saved, crossfeed))
                {
                    parts[i].CrossFeed.Add(parts[reached].Id);
                }
                ModuleDecouplerBase? decoupler = saved[i].partInfo.partPrefab.FindModuleImplementing<ModuleDecouplerBase>();
                if (decoupler != null)
                {
                    parts[i].DecouplesId = DecoupledPart(i, decoupler, saved, parts);
                }
            }
            return new FlowVessel(System.Math.Max(0, proto.stage), parts);
        }

        /// <summary>Fuel passes between two stack-attached parts when both of their nodes allow it; surface attachment never carries fuel.</summary>
        private static bool CarriesFuel(List<ProtoPartSnapshot> saved, int childIndex, int parentIndex)
        {
            ProtoPartSnapshot child = saved[childIndex];
            ProtoPartSnapshot parent = saved[parentIndex];
            bool childOk = false;
            foreach (AttachNodeSnapshot node in child.attachNodes)
            {
                if (node.partIdx == parentIndex && NodeAllows(child, node))
                {
                    childOk = true;
                }
            }
            if (!childOk)
            {
                return false;
            }
            foreach (AttachNodeSnapshot node in parent.attachNodes)
            {
                if (node.partIdx == childIndex && NodeAllows(parent, node))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool NodeAllows(ProtoPartSnapshot part, AttachNodeSnapshot node) =>
            part.partInfo.partPrefab.FindAttachNode(node.id)?.ResourceXFeed ?? true;

        private static IEnumerable<int> Reach(int start, List<ProtoPartSnapshot> saved, List<int>[] edges)
        {
            var seen = new HashSet<int> { start };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                if (at != start && !saved[at].partInfo.partPrefab.fuelCrossFeed)
                {
                    continue;
                }
                foreach (int next in edges[at])
                {
                    if (seen.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
            return seen;
        }

        private static uint? DecoupledPart(int index, ModuleDecouplerBase decoupler, List<ProtoPartSnapshot> saved, List<FlowPart> parts)
        {
            ProtoPartSnapshot part = saved[index];
            if (decoupler.explosiveNodeID == "srf")
            {
                return part.parentIdx >= 0 && part.parentIdx < saved.Count ? parts[part.parentIdx].Id : (uint?)null;
            }
            AttachNodeSnapshot? node = part.attachNodes.FirstOrDefault(n => n.id == decoupler.explosiveNodeID);
            if (node == null)
            {
                return null;
            }
            int target = node.partIdx;
            return target >= 0 && target < parts.Count ? parts[target].Id : (uint?)null;
        }

        private static FlowPart Make(uint id, string name, int inverseStage, double mass, IEnumerable<(string Name, double Amount, double Max, double Density, bool Flowing)> held, Part prefab)
        {
            double dry = mass;
            var resources = new List<FlowResource>();
            foreach (var r in held)
            {
                if (r.Flowing)
                {
                    resources.Add(new FlowResource(r.Name, r.Amount, r.Max, r.Density));
                }
                else
                {
                    dry += r.Amount * r.Density;
                }
            }
            var part = new FlowPart(id, name, inverseStage, dry, resources)
            {
                IsLaunchClamp = prefab.FindModuleImplementing<LaunchClamp>() != null,
            };
            ModuleDecouplerBase? decoupler = prefab.FindModuleImplementing<ModuleDecouplerBase>();
            if (decoupler != null)
            {
                part.IsDecoupler = true;
                part.IsOmniDecoupler = decoupler.isOmniDecoupler;
            }
            ModuleEngines? engine = prefab.FindModulesImplementing<ModuleEngines>().FirstOrDefault();
            if (engine != null)
            {
                part.Engine = new FlowEngine(
                    engine.maxThrust,
                    engine.minThrust,
                    engine.atmosphereCurve.Evaluate(0),
                    engine.g,
                    engine.throttleLocked,
                    engine.propellants.Select(p => new FlowPropellant(p.name, p.ratio, p.ignoreForIsp, (int)p.GetFlowMode())).ToList());
            }
            return part;
        }
    }
}

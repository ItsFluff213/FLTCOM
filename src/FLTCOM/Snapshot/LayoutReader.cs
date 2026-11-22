using System;
using System.Collections.Generic;
using FLTCOM.Art;
using FLTCOM.Core.Protocol;
using UnityEngine;

namespace FLTCOM.Snapshot
{
    /// <summary>
    /// Reads each vessel's part layout and reports it only when it changed since the last read: staging, docking and
    /// decoupling change the set of parts or their stages, and a vessel seen for the first time has no layout yet.
    /// </summary>
    internal sealed class LayoutReader
    {
        private readonly CraftModels _models;
        private readonly Dictionary<uint, (long Signature, uint Revision)> _sent = new Dictionary<uint, (long, uint)>();
        private static readonly Dictionary<string, (Vec3 Size, PartCategory Category)> Prefabs = new Dictionary<string, (Vec3, PartCategory)>();

        public LayoutReader(CraftModels models)
        {
            _models = models;
        }

        /// <summary>The layout to send for this vessel, or null when the one already sent is current.</summary>
        public CraftLayout? Changed(global::Vessel vessel)
        {
            long signature = Signature(vessel);
            uint revision = 1;
            if (_sent.TryGetValue(vessel.persistentId, out var sent))
            {
                if (sent.Signature == signature)
                {
                    return null;
                }
                revision = sent.Revision + 1;
            }
            _sent[vessel.persistentId] = (signature, revision);
            List<CraftPart> parts = Parts(vessel);
            return new CraftLayout(vessel.persistentId, revision, parts, parts.Count > 0 ? _models.PathFor(parts) : null);
        }

        /// <summary>Forgets every vessel not in the snapshot and returns their ids.</summary>
        public List<uint> Retain(HashSet<uint> present)
        {
            var gone = new List<uint>();
            foreach (uint id in _sent.Keys)
            {
                if (!present.Contains(id))
                {
                    gone.Add(id);
                }
            }
            foreach (uint id in gone)
            {
                _sent.Remove(id);
            }
            return gone;
        }

        private static long Signature(global::Vessel vessel)
        {
            unchecked
            {
                long hash = 17;
                if (vessel.loaded)
                {
                    foreach (Part part in vessel.parts)
                    {
                        hash = hash * 31 + part.persistentId;
                        hash = hash * 31 + part.inverseStage;
                    }
                    return hash * 31 + vessel.parts.Count;
                }
                foreach (ProtoPartSnapshot part in vessel.protoVessel.protoPartSnapshots)
                {
                    hash = hash * 31 + part.persistentId;
                    hash = hash * 31 + part.inverseStageIndex;
                }
                return hash * 31 + vessel.protoVessel.protoPartSnapshots.Count;
            }
        }

        private static List<CraftPart> Parts(global::Vessel vessel)
        {
            var parts = new List<CraftPart>();
            if (vessel.loaded)
            {
                foreach (Part part in vessel.parts)
                {
                    parts.Add(Make(part.persistentId, part.partInfo, part.orgPos, part.orgRot, part.inverseStage));
                }
                return parts;
            }
            foreach (ProtoPartSnapshot part in vessel.protoVessel.protoPartSnapshots)
            {
                if (part.partInfo != null)
                {
                    parts.Add(Make(part.persistentId, part.partInfo, part.position, part.rotation, part.inverseStageIndex));
                }
            }
            return parts;
        }

        private static CraftPart Make(uint id, AvailablePart info, Vector3 position, Quaternion rotation, int stage)
        {
            (Vec3 size, PartCategory category) = Prefab(info);
            return new CraftPart(
                id,
                info.name,
                new Vec3(position.x, position.y, position.z),
                new Quat(rotation.x, rotation.y, rotation.z, rotation.w),
                size,
                category,
                stage);
        }

        private static (Vec3 Size, PartCategory Category) Prefab(AvailablePart info)
        {
            if (!Prefabs.TryGetValue(info.name, out var known))
            {
                known = (Size(info.partPrefab), Category(info.partPrefab));
                Prefabs[info.name] = known;
            }
            return known;
        }

        /// <summary>
        /// Prefab renderers are disabled, so their world bounds are empty; the meshes' own bounds, carried into the
        /// part's frame, give the extents instead.
        /// </summary>
        private static Vec3 Size(Part prefab)
        {
            Transform root = prefab.transform;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                Include(filter.sharedMesh, filter.transform, root, ref min, ref max);
            }
            foreach (SkinnedMeshRenderer skinned in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Include(skinned.sharedMesh, skinned.transform, root, ref min, ref max);
            }
            if (min.x > max.x)
            {
                return new Vec3(1, 1, 1);
            }
            Vector3 size = max - min;
            return new Vec3(Math.Round(size.x, 3), Math.Round(size.y, 3), Math.Round(size.z, 3));
        }

        private static void Include(Mesh? mesh, Transform from, Transform root, ref Vector3 min, ref Vector3 max)
        {
            if (mesh == null)
            {
                return;
            }
            Matrix4x4 toRoot = root.worldToLocalMatrix * from.localToWorldMatrix;
            Bounds bounds = mesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                var local = new Vector3(
                    (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                Vector3 point = toRoot.MultiplyPoint3x4(local);
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }
        }

        private static PartCategory Category(Part prefab)
        {
            if (prefab.FindModuleImplementing<ModuleEngines>() != null)
            {
                return PartCategory.Engine;
            }
            if (prefab.FindModuleImplementing<ModuleDecouple>() != null || prefab.FindModuleImplementing<ModuleAnchoredDecoupler>() != null)
            {
                return PartCategory.Decoupler;
            }
            if (prefab.FindModuleImplementing<ModuleCommand>() != null)
            {
                return PartCategory.Pod;
            }
            foreach (PartResource resource in prefab.Resources)
            {
                if (resource.resourceName != "ElectricCharge" && resource.resourceName != "Ablator")
                {
                    return PartCategory.Tank;
                }
            }
            return PartCategory.Other;
        }
    }
}

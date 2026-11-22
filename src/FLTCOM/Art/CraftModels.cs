using System;
using System.Collections.Generic;
using System.Text;
using FLTCOM.Core.Art;
using FLTCOM.Core.Protocol;
using FLTCOM.Server;
using UnityEngine;

namespace FLTCOM.Art
{
    /// <summary>
    /// Builds a small grey model per craft design from the part prefabs and the layout's part placements, so it works
    /// for vessels that are not loaded. Designs are named by hash; each is built once, one per call to ExportNext.
    /// </summary>
    internal sealed class CraftModels
    {
        private const int PartTriangles = 400;
        private const int ModelTriangles = 3000;

        private static readonly Dictionary<string, TriangleMesh> PartMeshes = new Dictionary<string, TriangleMesh>(StringComparer.Ordinal);

        private readonly Queue<(string Path, IReadOnlyList<CraftPart> Parts)> _waiting = new Queue<(string, IReadOnlyList<CraftPart>)>();
        private readonly HashSet<string> _known = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The path the design's model is or will be served at.</summary>
        public string PathFor(IReadOnlyList<CraftPart> parts)
        {
            var placements = new List<(string, Vec3, Quat)>(parts.Count);
            foreach (CraftPart part in parts)
            {
                placements.Add((part.Name, part.Position, part.Rotation));
            }
            string path = "/art/craft/" + CraftModel.DesignHash(placements) + ".json";
            if (_known.Add(path))
            {
                _waiting.Enqueue((path, parts));
            }
            return path;
        }

        public void ExportNext(HttpServer server)
        {
            if (_waiting.Count == 0)
            {
                return;
            }
            (string path, IReadOnlyList<CraftPart> parts) = _waiting.Dequeue();
            var model = new CraftModel();
            foreach (CraftPart part in parts)
            {
                model.AddPart(PartMesh(part), part.Position, part.Rotation);
            }
            server.PublishArt(path, "application/json", Encoding.UTF8.GetBytes(CraftModel.ToJson(model.Build(ModelTriangles))));
        }

        private static TriangleMesh PartMesh(CraftPart part)
        {
            if (PartMeshes.TryGetValue(part.Name, out TriangleMesh? known))
            {
                return known;
            }
            Part? prefab = PartLoader.getPartInfoByName(part.Name)?.partPrefab;
            TriangleMesh? read = prefab != null ? Read(prefab) : null;
            known = read != null && read.TriangleCount > 0 ? CraftModel.Simplify(read, PartTriangles) : CraftModel.Box(part.Size);
            PartMeshes[part.Name] = known;
            return known;
        }

        /// <summary>Every mesh under the prefab, carried into the part's own frame.</summary>
        private static TriangleMesh? Read(Part prefab)
        {
            var positions = new List<float>();
            var indices = new List<int>();
            Transform root = prefab.transform;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                Append(filter.sharedMesh, filter.transform, root, positions, indices);
            }
            foreach (SkinnedMeshRenderer skinned in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Append(skinned.sharedMesh, skinned.transform, root, positions, indices);
            }
            return positions.Count == 0 ? null : new TriangleMesh(positions.ToArray(), indices.ToArray());
        }

        private static void Append(Mesh? mesh, Transform from, Transform root, List<float> positions, List<int> indices)
        {
            if (mesh == null || !mesh.isReadable)
            {
                return;
            }
            Matrix4x4 toRoot = root.worldToLocalMatrix * from.localToWorldMatrix;
            int start = positions.Count / 3;
            foreach (Vector3 vertex in mesh.vertices)
            {
                Vector3 point = toRoot.MultiplyPoint3x4(vertex);
                positions.Add(point.x);
                positions.Add(point.y);
                positions.Add(point.z);
            }
            foreach (int index in mesh.triangles)
            {
                indices.Add(start + index);
            }
        }
    }
}

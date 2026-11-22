using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FLTCOM.Core.Protocol;
using FLTCOM.Core.Telemetry;

namespace FLTCOM.Core.Art
{
    /// <summary>A triangle mesh: three floats per vertex, three indices per triangle.</summary>
    public sealed class TriangleMesh
    {
        public TriangleMesh(float[] positions, int[] indices)
        {
            Positions = positions;
            Indices = indices;
        }

        public float[] Positions { get; }
        public int[] Indices { get; }
        public int TriangleCount => Indices.Length / 3;
    }

    /// <summary>
    /// Builds the small grey model of a craft the display draws on the map and in the telescope view. Parts arrive as
    /// meshes in their own Unity frame; the model is in the craft frame of Vessel.attitude (y and z swapped).
    /// </summary>
    public sealed class CraftModel
    {
        private readonly List<float> _positions = new List<float>();
        private readonly List<int> _indices = new List<int>();

        /// <summary>Adds one part. Swapping y and z mirrors the mesh, so each triangle's winding is reversed to keep it facing out.</summary>
        public void AddPart(TriangleMesh part, Vec3 position, Quat rotation)
        {
            int start = _positions.Count / 3;
            for (int i = 0; i < part.Positions.Length; i += 3)
            {
                Vec3 local = Attitude.Rotate(rotation, new Vec3(part.Positions[i], part.Positions[i + 1], part.Positions[i + 2]));
                _positions.Add((float)(local.X + position.X));
                _positions.Add((float)(local.Z + position.Z));
                _positions.Add((float)(local.Y + position.Y));
            }
            for (int i = 0; i + 2 < part.Indices.Length; i += 3)
            {
                _indices.Add(start + part.Indices[i]);
                _indices.Add(start + part.Indices[i + 2]);
                _indices.Add(start + part.Indices[i + 1]);
            }
        }

        public TriangleMesh Build(int maxTriangles) => Simplify(new TriangleMesh(_positions.ToArray(), _indices.ToArray()), maxTriangles);

        /// <summary>A box of the given full extents around the origin, for parts whose mesh cannot be read.</summary>
        public static TriangleMesh Box(Vec3 size)
        {
            float x = (float)size.X / 2, y = (float)size.Y / 2, z = (float)size.Z / 2;
            var positions = new float[24];
            for (int corner = 0; corner < 8; corner++)
            {
                positions[corner * 3] = (corner & 1) == 0 ? -x : x;
                positions[corner * 3 + 1] = (corner & 2) == 0 ? -y : y;
                positions[corner * 3 + 2] = (corner & 4) == 0 ? -z : z;
            }
            int[] indices =
            {
                0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4,
                2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5,
            };
            return new TriangleMesh(positions, indices);
        }

        /// <summary>
        /// Vertex clustering: snaps every vertex to a grid over the mesh bounds, merges the vertices in each cell and
        /// drops triangles that collapse. The grid coarsens until the mesh has at most maxTriangles triangles.
        /// </summary>
        public static TriangleMesh Simplify(TriangleMesh mesh, int maxTriangles)
        {
            if (mesh.TriangleCount <= maxTriangles)
            {
                return mesh;
            }
            float[] p = mesh.Positions;
            int vertices = p.Length / 3;
            var min = new double[] { double.MaxValue, double.MaxValue, double.MaxValue };
            var max = new double[] { double.MinValue, double.MinValue, double.MinValue };
            for (int v = 0; v < vertices; v++)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    min[axis] = Math.Min(min[axis], p[v * 3 + axis]);
                    max[axis] = Math.Max(max[axis], p[v * 3 + axis]);
                }
            }
            double extent = Math.Max(1e-6, Math.Max(max[0] - min[0], Math.Max(max[1] - min[1], max[2] - min[2])));
            TriangleMesh result = mesh;
            for (int cells = 96; cells >= 2; cells = cells * 3 / 4)
            {
                result = Cluster(mesh, min, extent / cells);
                if (result.TriangleCount <= maxTriangles)
                {
                    break;
                }
            }
            return result;
        }

        private static TriangleMesh Cluster(TriangleMesh mesh, double[] min, double cell)
        {
            float[] p = mesh.Positions;
            var cellOf = new Dictionary<long, int>();
            var sums = new List<double>();
            var counts = new List<int>();
            var remap = new int[p.Length / 3];
            for (int v = 0; v < remap.Length; v++)
            {
                long key = 0;
                for (int axis = 0; axis < 3; axis++)
                {
                    key = key * 2097152 + (long)Math.Floor((p[v * 3 + axis] - min[axis]) / cell);
                }
                if (!cellOf.TryGetValue(key, out int index))
                {
                    index = counts.Count;
                    cellOf[key] = index;
                    counts.Add(0);
                    sums.Add(0);
                    sums.Add(0);
                    sums.Add(0);
                }
                counts[index]++;
                for (int axis = 0; axis < 3; axis++)
                {
                    sums[index * 3 + axis] += p[v * 3 + axis];
                }
                remap[v] = index;
            }
            var positions = new float[counts.Count * 3];
            for (int c = 0; c < counts.Count; c++)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    positions[c * 3 + axis] = (float)(sums[c * 3 + axis] / counts[c]);
                }
            }
            var seen = new HashSet<(int, int, int)>();
            var indices = new List<int>();
            int[] source = mesh.Indices;
            for (int i = 0; i + 2 < source.Length; i += 3)
            {
                int a = remap[source[i]], b = remap[source[i + 1]], c = remap[source[i + 2]];
                if (a == b || b == c || a == c)
                {
                    continue;
                }
                int low = Math.Min(a, Math.Min(b, c));
                (int, int, int) rotated = low == a ? (a, b, c) : low == b ? (b, c, a) : (c, a, b);
                if (seen.Add(rotated))
                {
                    indices.Add(a);
                    indices.Add(b);
                    indices.Add(c);
                }
            }
            return Compact(positions, indices);
        }

        private static TriangleMesh Compact(float[] positions, List<int> indices)
        {
            var used = new Dictionary<int, int>();
            var outPositions = new List<float>();
            var outIndices = new int[indices.Count];
            for (int i = 0; i < indices.Count; i++)
            {
                if (!used.TryGetValue(indices[i], out int index))
                {
                    index = used.Count;
                    used[indices[i]] = index;
                    outPositions.Add(positions[indices[i] * 3]);
                    outPositions.Add(positions[indices[i] * 3 + 1]);
                    outPositions.Add(positions[indices[i] * 3 + 2]);
                }
                outIndices[i] = index;
            }
            return new TriangleMesh(outPositions.ToArray(), outIndices);
        }

        /// <summary>
        /// Names a design: the same parts at the same places give the same hash, so every vessel built from one
        /// craft file shares one model. Positions count to the millimetre and rotations to a thousandth.
        /// </summary>
        public static string DesignHash(IEnumerable<(string Name, Vec3 Position, Quat Rotation)> parts)
        {
            var lines = new List<string>();
            foreach ((string name, Vec3 position, Quat rotation) in parts)
            {
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}|{1:F3},{2:F3},{3:F3}|{4:F3},{5:F3},{6:F3},{7:F3}",
                    name, position.X, position.Y, position.Z, rotation.X, rotation.Y, rotation.Z, rotation.W));
            }
            lines.Sort(StringComparer.Ordinal);
            ulong hash = 14695981039346656037UL;
            foreach (byte b in Encoding.UTF8.GetBytes(string.Join("\n", lines)))
            {
                hash = (hash ^ b) * 1099511628211UL;
            }
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        /// <summary>The CraftModel document: {"positions":[...],"indices":[...]}, positions in metres to the millimetre.</summary>
        public static string ToJson(TriangleMesh mesh)
        {
            var text = new StringBuilder(mesh.Positions.Length * 7 + mesh.Indices.Length * 4 + 32);
            text.Append("{\"positions\":[");
            for (int i = 0; i < mesh.Positions.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(',');
                }
                text.Append(Math.Round(mesh.Positions[i], 3).ToString("0.###", CultureInfo.InvariantCulture));
            }
            text.Append("],\"indices\":[");
            for (int i = 0; i < mesh.Indices.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(',');
                }
                text.Append(mesh.Indices[i].ToString(CultureInfo.InvariantCulture));
            }
            return text.Append("]}").ToString();
        }
    }
}

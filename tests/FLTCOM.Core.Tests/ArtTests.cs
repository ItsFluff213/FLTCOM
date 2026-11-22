using System;
using System.Linq;
using FLTCOM.Core.Art;
using FLTCOM.Core.Protocol;
using FLTCOM.Core.Telemetry;
using Xunit;

namespace FLTCOM.Core.Tests
{
    public class AttitudeTests
    {
        private static Vec3 Swap(Vec3 v) => new Vec3(v.X, v.Z, v.Y);

        private static Quat AxisAngle(double x, double y, double z, double angle)
        {
            double length = Math.Sqrt(x * x + y * y + z * z);
            double s = Math.Sin(angle / 2) / length;
            return new Quat(x * s, y * s, z * s, Math.Cos(angle / 2));
        }

        private static void Near(Vec3 expected, Vec3 actual)
        {
            Assert.Equal(expected.X, actual.X, 9);
            Assert.Equal(expected.Y, actual.Y, 9);
            Assert.Equal(expected.Z, actual.Z, 9);
        }

        [Theory]
        [InlineData(0.3, 0.8, -0.2, 1.1)]
        [InlineData(1, 0, 0, 2.5)]
        [InlineData(-0.4, 0.1, 0.9, -0.7)]
        public void Swizzled_rotation_turns_swapped_vectors_the_same_way(double x, double y, double z, double angle)
        {
            Quat q = AxisAngle(x, y, z, angle);
            var v = new Vec3(0.2, -1.3, 0.75);

            Near(Swap(Attitude.Rotate(q, v)), Attitude.Rotate(Attitude.Swizzle(q), Swap(v)));
        }

        [Fact]
        public void Body_rotation_turns_the_craft_about_north()
        {
            Quat level = new Quat(0, 0, 0, 1);
            Quat attitude = Attitude.ToOrbitFrame(level, Math.PI / 2);

            Near(new Vec3(0, 1, 0), Attitude.Rotate(attitude, new Vec3(1, 0, 0)));
            Near(new Vec3(0, 0, 1), Attitude.Rotate(attitude, new Vec3(0, 0, 1)));
        }

        [Fact]
        public void Craft_axis_follows_its_surface_rotation()
        {
            Quat pitched = AxisAngle(0, 0, 1, -Math.PI / 2);
            Quat attitude = Attitude.ToOrbitFrame(pitched, 0);

            Near(Swap(Attitude.Rotate(pitched, new Vec3(0, 1, 0))), Attitude.Rotate(attitude, new Vec3(0, 0, 1)));
            Near(new Vec3(1, 0, 0), Attitude.Rotate(attitude, new Vec3(0, 0, 1)));
        }
    }

    public class CraftModelTests
    {
        private static TriangleMesh Sphere(int rings, int segments)
        {
            var positions = new System.Collections.Generic.List<float>();
            var indices = new System.Collections.Generic.List<int>();
            for (int r = 0; r <= rings; r++)
            {
                for (int s = 0; s <= segments; s++)
                {
                    double theta = Math.PI * r / rings, phi = 2 * Math.PI * s / segments;
                    positions.AddRange(new[] { (float)(Math.Sin(theta) * Math.Cos(phi)), (float)Math.Cos(theta), (float)(Math.Sin(theta) * Math.Sin(phi)) });
                }
            }
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * (segments + 1) + s, b = a + segments + 1;
                    indices.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            }
            return new TriangleMesh(positions.ToArray(), indices.ToArray());
        }

        [Fact]
        public void Simplify_keeps_small_meshes_as_they_are()
        {
            TriangleMesh box = CraftModel.Box(new Vec3(1, 2, 3));

            Assert.Same(box, CraftModel.Simplify(box, 100));
        }

        [Fact]
        public void Simplify_cuts_a_dense_mesh_under_the_budget_and_keeps_its_size()
        {
            TriangleMesh dense = Sphere(80, 160);

            TriangleMesh small = CraftModel.Simplify(dense, 2000);

            Assert.True(dense.TriangleCount > 20000);
            Assert.InRange(small.TriangleCount, 200, 2000);
            Assert.All(small.Indices, index => Assert.InRange(index, 0, small.Positions.Length / 3 - 1));
            float top = Enumerable.Range(0, small.Positions.Length / 3).Max(v => small.Positions[v * 3 + 1]);
            Assert.InRange(top, 0.9f, 1.0f);
        }

        [Fact]
        public void AddPart_places_the_part_in_the_swapped_craft_frame()
        {
            var model = new CraftModel();
            model.AddPart(new TriangleMesh(new float[] { 0, 1, 0, 1, 0, 0, 0, 0, 1 }, new[] { 0, 1, 2 }), new Vec3(0, -2, 0), new Quat(0, 0, 0, 1));

            TriangleMesh mesh = model.Build(100);

            Assert.Equal(new float[] { 0, 0, -1, 1, 0, -2, 0, 1, -2 }, mesh.Positions);
            Assert.Equal(new[] { 0, 2, 1 }, mesh.Indices);
        }

        [Fact]
        public void Design_hash_ignores_part_order_and_sees_a_moved_part()
        {
            var tank = ("fuelTank", new Vec3(0, -1, 0), new Quat(0, 0, 0, 1));
            var pod = ("mk1pod", new Vec3(0, 0, 0), new Quat(0, 0, 0, 1));
            var moved = ("fuelTank", new Vec3(0, -1.5, 0), new Quat(0, 0, 0, 1));

            Assert.Equal(CraftModel.DesignHash(new[] { tank, pod }), CraftModel.DesignHash(new[] { pod, tank }));
            Assert.NotEqual(CraftModel.DesignHash(new[] { tank, pod }), CraftModel.DesignHash(new[] { moved, pod }));
            Assert.Matches("^[0-9a-f]{16}$", CraftModel.DesignHash(new[] { pod }));
        }

        [Fact]
        public void Json_lists_positions_and_indices()
        {
            Assert.Equal("{\"positions\":[0,1.5,-0.125],\"indices\":[0,0,0]}", CraftModel.ToJson(new TriangleMesh(new[] { 0f, 1.5f, -0.125f }, new[] { 0, 0, 0 })));
        }
    }
}

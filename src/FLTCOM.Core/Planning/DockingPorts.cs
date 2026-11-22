using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    /// <summary>One docking port. Size is the stock node type, such as size1; facing is in its vessel's frame, zero when unknown.</summary>
    public sealed class DockingPort
    {
        public DockingPort(uint id, string name, string size, bool free, Vector3d facing = default)
        {
            Id = id;
            Name = name;
            Size = size;
            Free = free;
            Facing = facing;
        }

        public uint Id { get; }

        public string Name { get; }

        public string Size { get; }

        public bool Free { get; }

        public Vector3d Facing { get; }
    }

    /// <summary>The two ports a dock joins, or why there are none.</summary>
    public sealed class PortChoice
    {
        private PortChoice(DockingPort? chaser, DockingPort? target, string? refusal)
        {
            Chaser = chaser;
            Target = target;
            Refusal = refusal;
        }

        public DockingPort? Chaser { get; }

        public DockingPort? Target { get; }

        public string? Refusal { get; }

        public bool Ok => Refusal == null;

        internal static PortChoice Of(DockingPort chaser, DockingPort target) => new PortChoice(chaser, target, null);

        internal static PortChoice Refused(string why) => new PortChoice(null, null, why);
    }

    public static class DockingPorts
    {
        /// <summary>
        /// Picks a free target port with a free chaser port of the same size. With <paramref name="toChaser"/> (the chaser's
        /// direction in the target's frame) the port facing the chaser wins; <paramref name="targetOverride"/> is the operator's pick.
        /// </summary>
        public static PortChoice Pick(string chaserName, IReadOnlyList<DockingPort> chaser, string targetName, IReadOnlyList<DockingPort> target, uint? targetOverride = null, Vector3d? toChaser = null)
        {
            if (chaser.Count == 0)
            {
                return PortChoice.Refused(chaserName + " has no docking port");
            }
            if (target.Count == 0)
            {
                return PortChoice.Refused(targetName + " has no docking port");
            }
            if (targetOverride is uint wanted)
            {
                DockingPort? port = target.FirstOrDefault(p => p.Id == wanted);
                if (port == null)
                {
                    return PortChoice.Refused(targetName + " has no port " + wanted);
                }
                if (!port.Free)
                {
                    return PortChoice.Refused(port.Name + " on " + targetName + " is occupied");
                }
                DockingPort? mate = Mate(chaser, port);
                return mate == null
                    ? PortChoice.Refused("size mismatch: " + port.Name + " on " + targetName + " is " + port.Size + ", " + chaserName + " has " + Sizes(chaser))
                    : PortChoice.Of(mate, port);
            }
            var free = target.Where(p => p.Free).ToList();
            if (free.Count == 0)
            {
                return PortChoice.Refused("every port on " + targetName + " is occupied");
            }
            DockingPort? best = free
                .Where(p => Mate(chaser, p) != null)
                .OrderByDescending(p => Facing(p, toChaser))
                .ThenBy(p => p.Id)
                .FirstOrDefault();
            return best == null
                ? PortChoice.Refused("size mismatch: no free port on " + targetName + " fits " + chaserName + " (" + Sizes(chaser) + " against " + string.Join(", ", free.Select(p => p.Size).Distinct()) + ")")
                : PortChoice.Of(Mate(chaser, best)!, best);
        }

        private static DockingPort? Mate(IReadOnlyList<DockingPort> chaser, DockingPort target) =>
            chaser.Where(p => p.Free && p.Size == target.Size).OrderBy(p => p.Id).FirstOrDefault();

        private static double Facing(DockingPort port, Vector3d? toChaser) =>
            toChaser is Vector3d toward && port.Facing.Magnitude > 0 && toward.Magnitude > 0
                ? Vector3d.Dot(port.Facing.Normalized, toward.Normalized)
                : 0;

        private static string Sizes(IReadOnlyList<DockingPort> ports)
        {
            var free = ports.Where(p => p.Free).Select(p => p.Size).Distinct().ToList();
            return free.Count == 0 ? "no free port" : string.Join(", ", free);
        }
    }
}

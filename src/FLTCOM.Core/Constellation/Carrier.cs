using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Propulsion;

namespace FLTCOM.Core.Constellation
{
    [Flags]
    public enum CarrierPartKind
    {
        None = 0,
        /// <summary>A probe core or crewed pod: whatever holds it can be flown on its own.</summary>
        Command = 1,
        Engine = 2,
        /// <summary>A decoupler or separator.</summary>
        Separator = 4,
        DockingPort = 8,
        Parachute = 16,
        Fairing = 32,
    }

    public static class CommandPart
    {
        /// <summary>
        /// Whether a part can fly its vessel: a probe core always, a pod only with at least its minimum crew aboard, so a
        /// pilot counts and an empty pod does not. <paramref name="minimumCrew"/> is null for a part without a command module.
        /// </summary>
        public static bool Controls(int? minimumCrew, int crew) => minimumCrew is int needed && crew >= needed;
    }

    /// <summary>One part of a vessel as payload detection sees it. Masses in tonnes, thrust in kN, Isp in seconds, both in vacuum.</summary>
    public sealed class CarrierPart
    {
        public CarrierPart(uint id, uint? parentId, string name, CarrierPartKind kinds, double mass, double dryMass, double thrust = 0, double isp = 0)
        {
            Id = id;
            ParentId = parentId;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Kinds = kinds;
            Mass = mass;
            DryMass = dryMass;
            Thrust = thrust;
            Isp = isp;
        }

        public uint Id { get; }

        /// <summary>The part it hangs from; null for the root part.</summary>
        public uint? ParentId { get; }

        public string Name { get; }

        public CarrierPartKind Kinds { get; }

        /// <summary>Mass with everything in it.</summary>
        public double Mass { get; }

        public double DryMass { get; }

        public double Thrust { get; }

        public double Isp { get; }

        public bool Is(CarrierPartKind kind) => (Kinds & kind) != 0;

        public bool Releases => Is(CarrierPartKind.Separator) || Is(CarrierPartKind.DockingPort);
    }

    /// <summary>A bundle of parts and what it can do on its own: the mass and the vacuum delta-v of its own engines and propellant.</summary>
    public sealed class Drive
    {
        public Drive(double mass, double dryMass, double thrust, double isp)
        {
            Mass = mass;
            DryMass = dryMass;
            Thrust = thrust;
            Isp = isp;
        }

        public double Mass { get; }

        public double DryMass { get; }

        public double Thrust { get; }

        public double Isp { get; }

        public double Propellant => Mass - DryMass;

        public double DeltaV => Thrust > 0 && Isp > 0 && DryMass > 0 ? Isp * DeltaVEstimator.G0 * Math.Log(Mass / DryMass) : 0;

        public bool CanBurn => DeltaV > 1;

        /// <summary>Propellant in tonnes to give <paramref name="mass"/> tonnes <paramref name="deltaV"/>.</summary>
        public double PropellantFor(double mass, double deltaV) => Isp > 0 ? mass * (1 - Math.Exp(-deltaV / (Isp * DeltaVEstimator.G0))) : double.PositiveInfinity;

        public static Drive Of(IEnumerable<CarrierPart> parts)
        {
            var list = parts.ToList();
            var engines = list.Where(p => p.Is(CarrierPartKind.Engine) && p.Thrust > 0 && p.Isp > 0).ToList();
            double thrust = engines.Sum(e => e.Thrust);
            double flow = engines.Sum(e => e.Thrust / e.Isp);
            return new Drive(list.Sum(p => p.Mass), list.Sum(p => p.DryMass), thrust, flow > 0 ? thrust / flow : 0);
        }
    }

    /// <summary>
    /// A part subtree beyond a decoupler, separator or docking port with its own probe core or pod. Number counts from 1 in
    /// the order the parts are listed. CoreId is the command part, which keeps its identity after release, so the plan
    /// names the payload by it until it becomes a vessel of its own.
    /// </summary>
    public sealed class Payload
    {
        public Payload(int number, uint coreId, uint separatorId, bool viaDockingPort, IReadOnlyList<uint> partIds, Drive own)
        {
            Number = number;
            CoreId = coreId;
            SeparatorId = separatorId;
            ViaDockingPort = viaDockingPort;
            PartIds = partIds;
            Own = own;
        }

        public int Number { get; }

        public uint CoreId { get; }

        public uint SeparatorId { get; }

        public bool ViaDockingPort { get; }

        public IReadOnlyList<uint> PartIds { get; }

        public Drive Own { get; }

        public double Mass => Own.Mass;

        public double DeltaV => Own.DeltaV;

        /// <summary>The placeholder vessel id of this payload's burns until it is released: its core part's id.</summary>
        public string PlaceholderId => CoreId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static string NameOf(string carrierName, int number) => carrierName + " Sat " + number;
    }

    public sealed class CarrierScan
    {
        public CarrierScan(IReadOnlyList<Payload> payloads, IReadOnlyList<string> uncontrolled, Drive carrier, bool carrierHasChutes, IReadOnlyList<uint> fairingIds)
        {
            Payloads = payloads;
            Uncontrolled = uncontrolled;
            Carrier = carrier;
            CarrierHasChutes = carrierHasChutes;
            FairingIds = fairingIds;
        }

        public IReadOnlyList<Payload> Payloads { get; }

        /// <summary>Releasable subtrees with an engine but no probe core: they would leave as debris nothing can fly.</summary>
        public IReadOnlyList<string> Uncontrolled { get; }

        /// <summary>The carrier alone: every part that is not a payload.</summary>
        public Drive Carrier { get; }

        public bool CarrierHasChutes { get; }

        /// <summary>Fairings on the carrier, opened before the first release.</summary>
        public IReadOnlyList<uint> FairingIds { get; }
    }

    /// <summary>
    /// The stage that lifted the stack to orbit, still attached below a decoupler, with its own probe core, engines and
    /// parachutes so it can come home on its own. Upper is everything else: the transfer stage and its satellites.
    /// </summary>
    public sealed class LaunchStage
    {
        public const string Needs = "a launch stage comes home only with its own probe core or crewed pod, engines and parachutes below a decoupler";

        private LaunchStage(uint coreId, uint separatorId, IReadOnlyList<CarrierPart> parts, IReadOnlyList<CarrierPart> upper)
        {
            CoreId = coreId;
            SeparatorId = separatorId;
            Parts = parts;
            Upper = upper;
            Own = Drive.Of(parts);
        }

        public uint CoreId { get; }

        public uint SeparatorId { get; }

        public IReadOnlyList<CarrierPart> Parts { get; }

        public IReadOnlyList<CarrierPart> Upper { get; }

        public Drive Own { get; }

        public const string Suffix = " Launch Stage";

        public static string NameOf(string carrierName) => carrierName + Suffix;

        /// <summary>
        /// Splits the vessel at the child of a decoupler or separator whose subtree, away from the root, holds a probe core,
        /// an engine and a parachute and no satellite of its own, while the root side keeps at least one satellite. The
        /// separating part stays with the root side, as KSP leaves it. When several qualify the heaviest is the launch stage.
        /// </summary>
        public static LaunchStage? Find(IReadOnlyList<CarrierPart> parts)
        {
            var children = parts.Where(p => p.ParentId != null).ToLookup(p => p.ParentId!.Value);
            LaunchStage? best = null;
            foreach (CarrierPart separator in parts.Where(p => p.Is(CarrierPartKind.Separator)))
            {
                foreach (CarrierPart top in children[separator.Id])
                {
                    var below = Subtree(top).ToList();
                    if (!below.Any(p => p.Is(CarrierPartKind.Command)) || !below.Any(p => p.Is(CarrierPartKind.Engine)) || !below.Any(p => p.Is(CarrierPartKind.Parachute)))
                    {
                        continue;
                    }
                    var cut = new HashSet<uint>(below.Select(p => p.Id));
                    var lower = below.Select(p => p.Id == top.Id ? new CarrierPart(p.Id, null, p.Name, p.Kinds, p.Mass, p.DryMass, p.Thrust, p.Isp) : p).ToList();
                    var upper = parts.Where(p => !cut.Contains(p.Id)).ToList();
                    if (PayloadDetector.Scan(lower).Payloads.Count > 0 || PayloadDetector.Scan(upper).Payloads.Count == 0)
                    {
                        continue;
                    }
                    var found = new LaunchStage(below.First(p => p.Is(CarrierPartKind.Command)).Id, separator.Id, below, upper);
                    if (best == null || found.Own.Mass > best.Own.Mass)
                    {
                        best = found;
                    }
                }
            }
            return best;

            IEnumerable<CarrierPart> Subtree(CarrierPart top)
            {
                var stack = new Stack<CarrierPart>();
                stack.Push(top);
                while (stack.Count > 0)
                {
                    CarrierPart p = stack.Pop();
                    yield return p;
                    foreach (CarrierPart c in children[p.Id])
                    {
                        stack.Push(c);
                    }
                }
            }
        }
    }

    public static class PayloadDetector
    {
        /// <summary>
        /// Walks the part tree from the root, which belongs to the carrier. The first separating part on a path whose far
        /// side holds a command part makes that far side a payload; nothing inside a payload is searched further.
        /// </summary>
        public static CarrierScan Scan(IReadOnlyList<CarrierPart> parts)
        {
            var children = parts.Where(p => p.ParentId != null).ToLookup(p => p.ParentId!.Value);
            var root = parts.FirstOrDefault(p => p.ParentId == null);
            var payloads = new List<Payload>();
            var uncontrolled = new List<string>();
            var inPayload = new HashSet<uint>();
            if (root != null)
            {
                Walk(root);
            }
            var carrierParts = parts.Where(p => !inPayload.Contains(p.Id)).ToList();
            return new CarrierScan(
                payloads,
                uncontrolled,
                Drive.Of(carrierParts),
                carrierParts.Any(p => p.Is(CarrierPartKind.Parachute)),
                carrierParts.Where(p => p.Is(CarrierPartKind.Fairing)).Select(p => p.Id).ToList());

            void Walk(CarrierPart part)
            {
                foreach (CarrierPart child in children[part.Id])
                {
                    if (part.Releases && Payload(part, child) is Payload found)
                    {
                        payloads.Add(found);
                        continue;
                    }
                    Walk(child);
                }
            }

            Payload? Payload(CarrierPart separator, CarrierPart top)
            {
                var subtree = Subtree(top).ToList();
                CarrierPart? core = subtree.FirstOrDefault(p => p.Is(CarrierPartKind.Command));
                if (core == null)
                {
                    if (subtree.Any(p => p.Is(CarrierPartKind.Engine)) && !subtree.Any(p => p.Releases))
                    {
                        uncontrolled.Add(top.Name);
                    }
                    return null;
                }
                foreach (CarrierPart p in subtree)
                {
                    inPayload.Add(p.Id);
                }
                return new Payload(payloads.Count + 1, core.Id, separator.Id, separator.Is(CarrierPartKind.DockingPort) || top.Is(CarrierPartKind.DockingPort), subtree.Select(p => p.Id).ToList(), Drive.Of(subtree));
            }

            IEnumerable<CarrierPart> Subtree(CarrierPart top)
            {
                var stack = new Stack<CarrierPart>();
                stack.Push(top);
                while (stack.Count > 0)
                {
                    CarrierPart p = stack.Pop();
                    yield return p;
                    foreach (CarrierPart c in children[p.Id])
                    {
                        stack.Push(c);
                    }
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace FLTCOM.Core.Missions
{
    /// <summary>An ordered list of operations. Operations may involve different vessels and may run at the same time.</summary>
    public sealed class Mission
    {
        public Mission(string id, string name, IReadOnlyList<Operation> operations)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Operations = operations ?? throw new ArgumentNullException(nameof(operations));
        }

        public string Id { get; }

        public string Name { get; }

        public IReadOnlyList<Operation> Operations { get; }

        public IEnumerable<string> VesselIds => Operations.SelectMany(o => o.VesselIds).Distinct();

        public bool IsFinished => Operations.All(o => StepMachine.IsTerminal(o.State));

        public Mission Replace(Operation operation) =>
            new Mission(Id, Name, Operations.Select(o => o.Id == operation.Id ? operation : o).ToList());
    }

    /// <summary>Deterministic two-word codenames. The operator may edit them before approval.</summary>
    public static class Codename
    {
        private static readonly string[] First =
        {
            "Amber", "Silent", "Copper", "Distant", "Iron", "Polar", "Quiet", "Rapid", "Silver", "Solar",
            "Steady", "Swift", "Violet", "Winter", "Bright", "Cobalt", "Crimson", "Daring", "Golden", "Hollow",
            "Jade", "Lunar", "Marble", "Northern", "Patient", "Scarlet", "Stellar", "Tidal", "Velvet", "Wandering",
            "Azure", "Brave",
        };

        private static readonly string[] Second =
        {
            "Albatross", "Anvil", "Beacon", "Comet", "Compass", "Condor", "Falcon", "Harbor", "Heron", "Kestrel",
            "Lantern", "Meridian", "Nomad", "Orbit", "Osprey", "Pelican", "Petrel", "Pilgrim", "Quarry", "Raven",
            "Sentinel", "Sextant", "Sparrow", "Spindle", "Swallow", "Tern", "Thistle", "Voyager", "Warden", "Zephyr",
            "Arrow", "Bastion",
        };

        public static string Generate(ulong seed)
        {
            ulong a = SplitMix(seed);
            ulong b = SplitMix(a);
            return First[(int)(a % (ulong)First.Length)] + " " + Second[(int)(b % (ulong)Second.Length)];
        }

        private static ulong SplitMix(ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                return x ^ (x >> 31);
            }
        }
    }
}

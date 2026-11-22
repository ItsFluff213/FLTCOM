using System;
using System.Collections.Generic;

namespace FLTCOM.Core.Propulsion
{
    /// <summary>A tank on a part. Mass per unit is Density, in tonnes.</summary>
    public sealed class FlowResource
    {
        public FlowResource(string name, double amount, double maxAmount, double density)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Amount = amount;
            MaxAmount = Math.Max(amount, maxAmount);
            Density = density;
        }

        public string Name { get; }

        public double Amount { get; }

        public double MaxAmount { get; }

        public double Density { get; }
    }

    /// <summary>One propellant of an engine. FlowMode is the integer value of KSP's ResourceFlowMode.</summary>
    public sealed class FlowPropellant
    {
        public FlowPropellant(string resource, double ratio, bool ignoreForIsp, int flowMode)
        {
            Resource = resource ?? throw new ArgumentNullException(nameof(resource));
            Ratio = ratio;
            IgnoreForIsp = ignoreForIsp;
            FlowMode = flowMode;
        }

        public string Resource { get; }

        public double Ratio { get; }

        public bool IgnoreForIsp { get; }

        public int FlowMode { get; }
    }

    /// <summary>Vacuum engine figures. Thrust in kN, Isp in seconds, G the engine's own g constant.</summary>
    public sealed class FlowEngine
    {
        public FlowEngine(double maxThrust, double minThrust, double ispVacuum, double g, bool throttleLocked, IReadOnlyList<FlowPropellant> propellants)
        {
            MaxThrust = maxThrust;
            MinThrust = minThrust;
            IspVacuum = ispVacuum;
            G = g;
            ThrottleLocked = throttleLocked;
            Propellants = propellants ?? throw new ArgumentNullException(nameof(propellants));
        }

        public double MaxThrust { get; }

        public double MinThrust { get; }

        public double IspVacuum { get; }

        public double G { get; }

        public bool ThrottleLocked { get; }

        public IReadOnlyList<FlowPropellant> Propellants { get; }
    }

    /// <summary>
    /// A part as the fuel flow simulation needs it. Parts refer to each other by Id: Links are the parent and the
    /// children, CrossFeed the parts the game lets this part draw fuel from, DecouplesId the part its decoupler
    /// separates (a decoupler only).
    /// </summary>
    public sealed class FlowPart
    {
        public FlowPart(uint id, string name, int inverseStage, double dryMass, IReadOnlyList<FlowResource> resources)
        {
            Id = id;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            InverseStage = inverseStage;
            DryMass = dryMass;
            Resources = resources ?? throw new ArgumentNullException(nameof(resources));
        }

        public uint Id { get; }

        public string Name { get; }

        public int InverseStage { get; }

        public double DryMass { get; }

        public IReadOnlyList<FlowResource> Resources { get; }

        public bool IsRoot { get; set; }

        public bool IsLaunchClamp { get; set; }

        public bool IsDecoupler { get; set; }

        public bool IsOmniDecoupler { get; set; }

        public uint? DecouplesId { get; set; }

        public FlowEngine? Engine { get; set; }

        public List<uint> Links { get; } = new List<uint>();

        public List<uint> CrossFeed { get; } = new List<uint>();
    }

    /// <summary>A vessel for the fuel flow simulation. CurrentStage is the stage the game would fire next.</summary>
    public sealed class FlowVessel
    {
        public FlowVessel(int currentStage, IReadOnlyList<FlowPart> parts)
        {
            CurrentStage = currentStage;
            Parts = parts ?? throw new ArgumentNullException(nameof(parts));
        }

        public int CurrentStage { get; }

        public IReadOnlyList<FlowPart> Parts { get; }
    }
}

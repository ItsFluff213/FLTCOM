using System;
using System.Collections.Generic;
using System.Linq;

namespace FLTCOM.Core.Propulsion
{
    /// <summary>A resource held by a part. Density in tonnes per unit.</summary>
    public sealed class PartResource
    {
        public PartResource(string name, double amount, double density)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Amount = amount;
            Density = density;
        }

        public string Name { get; }

        public double Amount { get; }

        public double Density { get; }
    }

    /// <summary>One propellant an engine burns, with its ratio in resource units.</summary>
    public sealed class Propellant
    {
        public Propellant(string resource, double ratio)
        {
            Resource = resource ?? throw new ArgumentNullException(nameof(resource));
            Ratio = ratio;
        }

        public string Resource { get; }

        public double Ratio { get; }
    }

    /// <summary>Vacuum engine data. Thrust in kN, Isp in seconds.</summary>
    public sealed class EngineData
    {
        public EngineData(int activationStage, double thrust, double isp, IReadOnlyList<Propellant> propellants)
        {
            ActivationStage = activationStage;
            Thrust = thrust;
            Isp = isp;
            Propellants = propellants ?? throw new ArgumentNullException(nameof(propellants));
        }

        public int ActivationStage { get; }

        public double Thrust { get; }

        public double Isp { get; }

        public IReadOnlyList<Propellant> Propellants { get; }
    }

    /// <summary>A part as saved in a vessel snapshot. Masses in tonnes. DecoupledIn is the KSP stage that drops it, or -1 when it stays.</summary>
    public sealed class PartSnapshot
    {
        public PartSnapshot(string name, double dryMass, int decoupledIn, IReadOnlyList<PartResource> resources, EngineData? engine = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            DryMass = dryMass;
            DecoupledIn = decoupledIn;
            Resources = resources ?? throw new ArgumentNullException(nameof(resources));
            Engine = engine;
        }

        public string Name { get; }

        public double DryMass { get; }

        public int DecoupledIn { get; }

        public IReadOnlyList<PartResource> Resources { get; }

        public EngineData? Engine { get; }
    }

    /// <summary>Delta-v of one KSP stage, in vacuum, with every later stage still attached.</summary>
    public sealed class StageDeltaV
    {
        public StageDeltaV(int stage, double startMass, double endMass, double thrust, double deltaV, double burnTime)
        {
            Stage = stage;
            StartMass = startMass;
            EndMass = endMass;
            Thrust = thrust;
            DeltaV = deltaV;
            BurnTime = burnTime;
        }

        public int Stage { get; }

        public double StartMass { get; }

        public double EndMass { get; }

        public double Thrust { get; }

        public double DeltaV { get; }

        public double BurnTime { get; }

        public double ExhaustVelocity => DeltaV / Math.Log(StartMass / EndMass);

        public double MassFlow => (StartMass - EndMass) / BurnTime;

        public double Twr(double surfaceGravity) => Thrust / (StartMass * surfaceGravity);
    }

    /// <summary>
    /// Estimates vacuum delta-v per stage from part snapshots. Engines drain only the tanks that share their
    /// decoupler group (no fuel lines or crossfeed). Stage s burns until every engine that stage s-1 drops has
    /// flamed out, or until all engines have.
    /// </summary>
    public static class DeltaVEstimator
    {
        public const double G0 = 9.80665;
        private const double Empty = 1e-9;

        public static IReadOnlyList<StageDeltaV> Estimate(IReadOnlyList<PartSnapshot> parts)
        {
            var present = new List<PartSnapshot>(parts);
            var pools = new Dictionary<(int Group, string Resource), double>();
            foreach (PartSnapshot part in parts)
            {
                foreach (PartResource resource in part.Resources)
                {
                    var key = (part.DecoupledIn, resource.Name);
                    pools.TryGetValue(key, out double mass);
                    pools[key] = mass + resource.Amount * resource.Density;
                }
            }
            Dictionary<string, double> densities = parts.SelectMany(p => p.Resources)
                .GroupBy(r => r.Name)
                .ToDictionary(g => g.Key, g => g.First().Density);

            int top = parts.Select(p => Math.Max(p.DecoupledIn, p.Engine?.ActivationStage ?? -1)).DefaultIfEmpty(-1).Max();
            var stages = new List<StageDeltaV>();
            for (int s = top; s >= 0; s--)
            {
                present.RemoveAll(p => p.DecoupledIn >= s);
                foreach (var key in pools.Keys.Where(k => k.Group >= s).ToList())
                {
                    pools.Remove(key);
                }
                List<PartSnapshot> engines = present.Where(p => p.Engine != null && p.Engine.ActivationStage >= s).ToList();
                if (engines.Count == 0)
                {
                    continue;
                }
                StageDeltaV? stage = BurnStage(s, present, engines, pools, densities);
                if (stage != null)
                {
                    stages.Add(stage);
                }
            }
            return stages;
        }

        private static StageDeltaV? BurnStage(int s, List<PartSnapshot> present, List<PartSnapshot> engines, Dictionary<(int Group, string Resource), double> pools, Dictionary<string, double> densities)
        {
            double Mass() => present.Sum(p => p.DryMass) + pools.Values.Sum();
            bool Running(PartSnapshot e) =>
                e.Engine!.Propellants.Count > 0 && e.Engine.Propellants.All(prop => pools.TryGetValue((e.DecoupledIn, prop.Resource), out double m) && m > Empty);

            bool dropsNext = engines.Any(e => e.DecoupledIn == s - 1);
            double startMass = Mass();
            double startThrust = engines.Where(Running).Sum(e => e.Engine!.Thrust);
            double deltaV = 0;
            double time = 0;

            for (int guard = 0; guard < 1000; guard++)
            {
                List<PartSnapshot> running = engines.Where(Running).ToList();
                if (running.Count == 0 || (dropsNext && !running.Any(e => e.DecoupledIn == s - 1)))
                {
                    break;
                }
                var drain = new Dictionary<(int Group, string Resource), double>();
                double thrust = 0;
                double flow = 0;
                foreach (PartSnapshot e in running)
                {
                    EngineData engine = e.Engine!;
                    double engineFlow = engine.Thrust / (engine.Isp * G0);
                    double unitMass = engine.Propellants.Sum(p => p.Ratio * densities[p.Resource]);
                    foreach (Propellant p in engine.Propellants)
                    {
                        var key = (e.DecoupledIn, p.Resource);
                        drain.TryGetValue(key, out double rate);
                        drain[key] = rate + engineFlow * p.Ratio * densities[p.Resource] / unitMass;
                    }
                    thrust += engine.Thrust;
                    flow += engineFlow;
                }
                double step = drain.Min(d => pools[d.Key] / d.Value);
                double before = Mass();
                foreach (var d in drain)
                {
                    pools[d.Key] = Math.Max(0, pools[d.Key] - d.Value * step);
                }
                double after = Mass();
                deltaV += thrust / flow * Math.Log(before / after);
                time += step;
            }
            return deltaV > 0 ? new StageDeltaV(s, startMass, Mass(), startThrust, deltaV, time) : null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.AutoSystems;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Execution
{
    /// <summary>The engines that fire next on an unloaded vessel, read from its saved parts. Masses in tonnes, thrust in kN.</summary>
    public sealed class Thruster
    {
        public Thruster(int stage, double thrust, double massFlow, double mass, double propellant, IReadOnlyDictionary<(int Group, string Resource), double> shares)
        {
            Stage = stage;
            Thrust = thrust;
            MassFlow = massFlow;
            Mass = mass;
            Propellant = propellant;
            Shares = shares;
        }

        /// <summary>The KSP stage these engines fire in; a vessel whose current stage is higher has to stage down to it first.</summary>
        public int Stage { get; }

        public double Thrust { get; }

        /// <summary>Propellant mass burned per second at full thrust.</summary>
        public double MassFlow { get; }

        public double Mass { get; }

        /// <summary>Propellant mass the stage can still burn.</summary>
        public double Propellant { get; }

        /// <summary>Share of the burned mass each resource of each tank group supplies, keyed by the stage that drops the group; the shares add to one.</summary>
        public IReadOnlyDictionary<(int Group, string Resource), double> Shares { get; }

        public double ExhaustVelocity => Thrust / MassFlow;
    }

    public sealed class BurnStep
    {
        public BurnStep(OrbitElements orbit, double deltaV, double massUsed, double ut, bool done, bool outOfFuel)
        {
            Orbit = orbit;
            DeltaV = deltaV;
            MassUsed = massUsed;
            Ut = ut;
            Done = done;
            OutOfFuel = outOfFuel;
        }

        public OrbitElements Orbit { get; }

        /// <summary>Delta-v flown in this step.</summary>
        public double DeltaV { get; }

        public double MassUsed { get; }

        /// <summary>Universal time the step ended at.</summary>
        public double Ut { get; }

        public bool Done { get; }

        public bool OutOfFuel { get; }
    }

    /// <summary>Units of <see cref="Resource"/> to take from the part at index <see cref="Part"/> of the saved part list.</summary>
    public sealed class Withdrawal
    {
        public Withdrawal(int part, string resource, double units)
        {
            Part = part;
            Resource = resource;
            Units = units;
        }

        public int Part { get; }

        public string Resource { get; }

        public double Units { get; }
    }

    /// <summary>One frame of a burn on rails: the engines that fired, how far they got and the propellant they took, or why the burn stops.</summary>
    public sealed class BurnFrame
    {
        public BurnFrame(string stop)
        {
            Stop = stop;
            Withdrawals = Array.Empty<Withdrawal>();
        }

        public BurnFrame(Thruster thruster, bool staged, BurnStep step, IReadOnlyList<Withdrawal> withdrawals)
        {
            Thruster = thruster;
            Staged = staged;
            Step = step;
            Withdrawals = withdrawals;
        }

        public string? Stop { get; }

        public Thruster? Thruster { get; }

        /// <summary>The engines fired as if staged on rails: a spent stage is still in the save.</summary>
        public bool Staged { get; }

        public BurnStep? Step { get; }

        public IReadOnlyList<Withdrawal> Withdrawals { get; }
    }

    /// <summary>
    /// Flies a finite burn on rails: thrust along a fixed direction, the rocket equation for the mass, the orbit
    /// propagated between kicks of at most <see cref="MaxStep"/> seconds.
    /// </summary>
    public static class BackgroundBurn
    {
        public const double MaxStep = 1;

        private const double Empty = 1e-6;

        /// <summary>The engines of the first stage that still has delta-v, or null with the reason none can fire.</summary>
        public static Thruster? Thruster(IReadOnlyList<PartSnapshot> parts, out string? refusal)
        {
            if (!parts.Any(p => p.Engine != null))
            {
                refusal = "no engine";
                return null;
            }
            StageDeltaV? stage = DeltaVEstimator.Estimate(parts).FirstOrDefault(s => s.DeltaV > Empty && s.Thrust > 0);
            if (stage == null)
            {
                refusal = "no fuel";
                return null;
            }
            var present = parts.Where(p => p.DecoupledIn < stage.Stage).ToList();
            var held = present
                .SelectMany(p => p.Resources.Select(r => (Key: (p.DecoupledIn, r.Name), Mass: r.Amount * r.Density)))
                .GroupBy(r => r.Key)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Mass));
            var densities = present.SelectMany(p => p.Resources).GroupBy(r => r.Name).ToDictionary(g => g.Key, g => g.First().Density);
            var engines = present
                .Where(p => p.Engine != null && p.Engine.ActivationStage >= stage.Stage && p.Engine.Propellants.Count > 0)
                .Where(p => p.Engine!.Propellants.All(prop => held.TryGetValue((p.DecoupledIn, prop.Resource), out double m) && m > Empty))
                .ToList();
            if (engines.Count == 0)
            {
                refusal = "no fuel";
                return null;
            }
            var rates = new Dictionary<(int Group, string Resource), double>();
            double thrust = 0;
            double flow = 0;
            foreach (PartSnapshot part in engines)
            {
                EngineData engine = part.Engine!;
                double engineFlow = engine.Thrust / (engine.Isp * DeltaVEstimator.G0);
                double unitMass = engine.Propellants.Sum(p => p.Ratio * densities[p.Resource]);
                foreach (Propellant p in engine.Propellants)
                {
                    var key = (part.DecoupledIn, p.Resource);
                    rates.TryGetValue(key, out double rate);
                    rates[key] = rate + engineFlow * p.Ratio * densities[p.Resource] / unitMass;
                }
                thrust += engine.Thrust;
                flow += engineFlow;
            }
            refusal = null;
            return new Thruster(stage.Stage, thrust, flow, stage.StartMass, stage.StartMass - stage.EndMass, rates.ToDictionary(r => r.Key, r => r.Value / flow));
        }

        /// <summary>
        /// Flies the burn from <paramref name="fromUt"/> towards <paramref name="toUt"/> on the first stage with fuel. When firing it
        /// leaves a spent stage behind, the vessel stages on rails only if <see cref="BackgroundStaging"/> lets it.
        /// </summary>
        public static BurnFrame Frame(IReadOnlyList<PartSnapshot> parts, IReadOnlyList<StagePart> stageParts, bool autoSystems, OrbitElements orbit, Vector3d direction, double fromUt, double toUt, double deltaVLeft)
        {
            Thruster? thruster = Thruster(parts, out string? refusal);
            if (thruster == null)
            {
                return new BurnFrame(refusal!);
            }
            string? held = BackgroundStaging.Refusal(stageParts, thruster.Stage, autoSystems);
            if (held != null)
            {
                return new BurnFrame(held);
            }
            BurnStep step = Step(orbit, direction, thruster, fromUt, toUt, deltaVLeft);
            return new BurnFrame(thruster, BackgroundStaging.Needed(stageParts, thruster.Stage), step, Withdrawals(parts, thruster, step.MassUsed));
        }

        /// <summary>What burning <paramref name="mass"/> takes from the tanks: each group's share, from that group's tanks in proportion to what each holds.</summary>
        public static IReadOnlyList<Withdrawal> Withdrawals(IReadOnlyList<PartSnapshot> parts, Thruster thruster, double mass)
        {
            var taken = new List<Withdrawal>();
            foreach (var share in thruster.Shares)
            {
                var tanks = parts
                    .Select((part, index) => (Index: index, Part: part))
                    .Where(t => t.Part.DecoupledIn == share.Key.Group)
                    .SelectMany(t => t.Part.Resources.Where(r => r.Name == share.Key.Resource && r.Amount > 0 && r.Density > 0).Select(r => (t.Index, r.Amount, r.Density)))
                    .ToList();
                double held = tanks.Sum(t => t.Amount);
                if (held <= 0)
                {
                    continue;
                }
                double units = Math.Min(held, mass * share.Value / tanks[0].Density);
                taken.AddRange(tanks.Select(t => new Withdrawal(t.Index, share.Key.Resource, units * t.Amount / held)));
            }
            return taken;
        }

        /// <summary>The inertial direction of <paramref name="burn"/>: its prograde, normal and radial parts on <paramref name="orbit"/> at the node.</summary>
        public static Vector3d Direction(OrbitElements orbit, PlannedBurn burn)
        {
            StateVector s = orbit.StateAt(burn.NodeUt);
            return (s.Prograde * burn.Prograde + s.Normal * burn.Normal + s.RadialOut * burn.Radial).Normalized;
        }

        /// <summary>Burns from <paramref name="fromUt"/> towards <paramref name="toUt"/> until <paramref name="deltaVLeft"/> is flown or the stage runs dry.</summary>
        public static BurnStep Step(OrbitElements orbit, Vector3d direction, Thruster thruster, double fromUt, double toUt, double deltaVLeft)
        {
            double t = fromUt;
            double mass = thruster.Mass;
            double used = 0;
            double flown = 0;
            double ve = thruster.ExhaustVelocity;
            bool outOfFuel = false;
            while (t < toUt && flown < deltaVLeft - Empty && !outOfFuel)
            {
                double dt = Math.Min(MaxStep, toUt - t);
                double burned = thruster.MassFlow * dt;
                double need = mass * (1 - Math.Exp(-(deltaVLeft - flown) / ve));
                if (need < burned)
                {
                    burned = need;
                    dt = burned / thruster.MassFlow;
                }
                if (used + burned >= thruster.Propellant)
                {
                    burned = Math.Max(0, thruster.Propellant - used);
                    dt = burned / thruster.MassFlow;
                    outOfFuel = true;
                }
                double dv = ve * Math.Log(mass / (mass - burned));
                double mid = t + dt / 2;
                StateVector s = orbit.StateAt(mid);
                orbit = OrbitElements.FromState(orbit.Mu, new StateVector(s.Position, s.Velocity + direction * dv), mid);
                t += dt;
                mass -= burned;
                used += burned;
                flown += dv;
            }
            bool done = flown >= deltaVLeft - Empty;
            return new BurnStep(orbit, flown, used, t, done, outOfFuel && !done);
        }
    }
}

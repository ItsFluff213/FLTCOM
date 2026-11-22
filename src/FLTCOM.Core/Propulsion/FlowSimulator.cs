using System;
using System.Collections.Generic;
using System.Linq;
using MechJebLib.FuelFlowSimulation;
using MechJebLib.FuelFlowSimulation.PartModules;
using MechJebLib.Interpolants;
using MechJebLib.Primitives;

namespace FLTCOM.Core.Propulsion
{
    /// <summary>
    /// Vacuum delta-v per stage from MechJebLib's fuel flow simulation, which follows crossfeed, fuel lines, decoupling
    /// order and per-propellant flow modes the way the game does. Loaded and unloaded vessels both come in as a FlowVessel.
    /// </summary>
    public static class FlowSimulator
    {
        public static IReadOnlyList<StageDeltaV> Estimate(FlowVessel vessel)
        {
            var resourceIds = new Dictionary<string, int>(StringComparer.Ordinal);
            int IdOf(string name)
            {
                if (!resourceIds.TryGetValue(name, out int id))
                {
                    id = resourceIds.Count + 1;
                    resourceIds[name] = id;
                }
                return id;
            }

            Dictionary<string, double> densities = vessel.Parts.SelectMany(p => p.Resources).GroupBy(r => r.Name).ToDictionary(g => g.Key, g => g.First().Density, StringComparer.Ordinal);
            SimVessel sim = SimVessel.Borrow();
            try
            {
                sim.SetCurrentStage(vessel.CurrentStage);
                var byId = new Dictionary<uint, SimPart>();
                var decouplers = new List<(SimModuleDecouple Module, uint Target)>();
                foreach (FlowPart part in vessel.Parts)
                {
                    SimPart sp = BuildPart(sim, part, IdOf, densities, decouplers);
                    sim.Parts.Add(sp);
                    byId[part.Id] = sp;
                }
                foreach (FlowPart part in vessel.Parts)
                {
                    SimPart sp = byId[part.Id];
                    Link(sp.Links, part.Links, byId);
                    Link(sp.CrossFeedPartSet, part.CrossFeed, byId);
                }
                foreach (var (module, target) in decouplers)
                {
                    module.AttachedPart = byId.TryGetValue(target, out SimPart? attached) ? attached : null;
                }

                DecouplingAnalyzer.Analyze(sim);
                sim.UpdateEngineSet();
                foreach (SimPart sp in sim.Parts)
                {
                    sp.UpdateMass();
                }

                var simulation = new FuelFlowSimulation();
                simulation.Run(sim);
                return Stages(simulation.Segments);
            }
            finally
            {
                sim.Dispose();
            }
        }

        private static void Link(List<SimPart> into, List<uint> ids, Dictionary<uint, SimPart> byId)
        {
            foreach (uint id in ids)
            {
                if (byId.TryGetValue(id, out SimPart? other))
                {
                    into.Add(other);
                }
            }
        }

        private static IReadOnlyList<StageDeltaV> Stages(List<FuelStats> segments)
        {
            var stages = new List<StageDeltaV>();
            foreach (var group in segments.Where(s => s.DeltaV > 0).GroupBy(s => s.KSPStage).OrderByDescending(g => g.Key))
            {
                List<FuelStats> ordered = group.OrderBy(s => s.StartTime).ToList();
                double burn = ordered.Sum(s => s.DeltaTime);
                stages.Add(new StageDeltaV(group.Key, ordered[0].StartMass, ordered[ordered.Count - 1].EndMass, ordered[0].Thrust, ordered.Sum(s => s.DeltaV), burn));
            }
            return stages;
        }

        private static SimPart BuildPart(SimVessel sim, FlowPart part, Func<string, int> idOf, Dictionary<string, double> densities, List<(SimModuleDecouple, uint)> decouplers)
        {
            SimPart sp = SimPart.Borrow(sim, part.Name);
            sp.PersistentId = part.Id;
            sp.InverseStage = part.InverseStage;
            sp.DryMass = part.DryMass;
            sp.Mass = part.DryMass;
            sp.IsRoot = part.IsRoot;
            foreach (FlowResource r in part.Resources)
            {
                int id = idOf(r.Name);
                sp.Resources[id] = new SimResource { Id = id, Amount = r.Amount, MaxAmount = r.MaxAmount, Density = r.Density, Free = r.Density == 0 };
            }
            if (part.IsLaunchClamp)
            {
                sp.IsLaunchClamp = true;
                sim.HasLaunchClamp = true;
                sp.Modules.Add(SimLaunchClamp.Borrow(sp));
            }
            if (part.IsDecoupler)
            {
                SimModuleDecouple decouple = SimModuleDecouple.Borrow(sp);
                decouple.IsOmniDecoupler = part.IsOmniDecoupler;
                sp.Modules.Add(decouple);
                if (part.DecouplesId.HasValue)
                {
                    decouplers.Add((decouple, part.DecouplesId.Value));
                }
            }
            if (part.Engine != null)
            {
                sp.Modules.Add(BuildEngine(sp, part.Engine, idOf, densities));
                sp.IsEngine = true;
                sp.IsThrottleLocked = part.Engine.ThrottleLocked;
            }
            return sp;
        }

        private static SimModuleEngines BuildEngine(SimPart part, FlowEngine data, Func<string, int> idOf, Dictionary<string, double> densities)
        {
            SimModuleEngines engine = SimModuleEngines.Borrow(part);
            double flow = data.IspVacuum > 0 ? data.MaxThrust / (data.IspVacuum * data.G) : 0;
            double minFlow = data.IspVacuum > 0 ? data.MinThrust / (data.IspVacuum * data.G) : 0;
            engine.IsEnabled = true;
            engine.IsOperational = part.InverseStage >= part.Vessel.CurrentStage;
            engine.ThrottleLocked = data.ThrottleLocked;
            engine.MaxThrust = (float)data.MaxThrust;
            engine.MinThrust = (float)data.MinThrust;
            engine.MaxFuelFlow = (float)flow;
            engine.MinFuelFlow = (float)minFlow;
            engine.G = (float)data.G;
            engine.Clamp = 0;
            engine.FlowMultCap = float.MaxValue;
            engine.FlowMultCapSharpness = 2;
            engine.FlowMultiplier = 1;
            engine.MultFlow = 1;
            engine.MultIsp = 1;
            engine.ThrottleLimiter = 100;
            engine.ThrottleResponseRate = -1;
            engine.ThrottleIgniteLevelMult = 1;
            engine.ThrottleStartupMult = 1;
            engine.ThrottleStartedMult = 1;
            engine.ThrottleShutdownMult = 100;
            engine.ThrottleClamp = 0.005f;
            engine.ThrottlePressureFedStartupMult = 5;
            engine.ThrottleDownMult = 100;
            engine.ThrustTransformMultipliers.Clear();
            engine.ThrustTransformMultipliers.Add(1);
            engine.ThrustDirectionVectors.Clear();
            engine.ThrustDirectionVectors.Add(new V3(0, 1, 0));
            foreach (DoubleInterpolant curve in new[] { engine.ThrustCurve, engine.ThrottleIspCurve, engine.ThrottleIspCurveAtmStrength, engine.VelCurve, engine.VelCurveIsp, engine.ATMCurve, engine.ATMCurveIsp, engine.ThrottleCurve })
            {
                Constant(curve, 0);
            }
            Constant(engine.AtmosphereCurve, data.IspVacuum);
            foreach (FlowPropellant p in data.Propellants)
            {
                engine.Propellants.Add(new SimPropellant(idOf(p.Resource), p.IgnoreForIsp, p.Ratio, (SimFlowMode)p.FlowMode, densities.TryGetValue(p.Resource, out double density) ? density : 0));
            }
            return engine;
        }

        private static void Constant(DoubleInterpolant curve, double value)
        {
            curve.Clear();
            curve.Append(ConstantDoubleNode.Rent(value), 0, 0);
        }
    }
}

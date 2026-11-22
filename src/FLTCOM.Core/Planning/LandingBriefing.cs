using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Landing;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Propulsion;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    /// <summary>
    /// Briefs a landing: a deorbit burn timed and aimed so the predicted descent falls on the target, then the descent
    /// itself (corrections, braking, touchdown) as one pseudo-burn that the landing guidance flies.
    /// </summary>
    public static class LandingBriefing
    {
        public const string DeorbitLabel = "deorbit";
        public const string DescentLabel = "descent";

        /// <summary>Delta-v held back for course corrections during the descent.</summary>
        public const double CorrectionReserve = 20;

        /// <summary>Thrust-to-weight on the target body below which braking cannot be trusted.</summary>
        public const double MinTwr = 1.1;

        /// <summary>Predicted miss above which the briefing warns; corrections in flight close smaller ones.</summary>
        public const double WarnMiss = 500;

        private const double DescentLead = 5;

        public static bool IsDescent(PlannedBurn burn) => burn.Label == DescentLabel;

        internal static Briefing Build(string operationName, OrbitChange change, IReadOnlyList<VesselTask> tasks, double now, double surfaceGravity, List<Contingency> contingencies)
        {
            LandingTarget target = change.Landing!;
            if (tasks.Count != 1)
            {
                return Refuse(operationName, change, contingencies, tasks.Count > 0 ? tasks[0].VesselId : string.Empty, "a landing flies one vessel at a time");
            }
            VesselTask task = tasks[0];
            if (task.Landing is not LandingContext context)
            {
                return Refuse(operationName, change, contingencies, task.VesselId, "no landing data for this vessel");
            }
            if (target.Site != LandingSiteKind.Custom && !context.HomeBody)
            {
                return Refuse(operationName, change, contingencies, task.VesselId, "the launch pad and the runway are on the home planet; pick coordinates on " + task.Body.Name);
            }
            LandingBody body = context.Body;
            LandingCraft craft = Craft(task, context);
            bool chutes = craft.HasChutes && body.AtmosphereHeight > 0;
            bool powered = craft.MaxAccel > 0;
            if (!powered && !chutes)
            {
                return Refuse(operationName, change, contingencies, task.VesselId, "no engine and no parachutes: the vessel would crash");
            }
            double twr = craft.MaxAccel / body.SurfaceGravity;
            if (!chutes && twr < MinTwr)
            {
                return Refuse(operationName, change, contingencies, task.VesselId, "thrust-to-weight " + Briefing.F(twr, "0.00") + " on " + task.Body.Name + " is too low to brake for touchdown");
            }

            LandingPlan plan = LandingPlanner.Plan(body, craft, task.Orbit, target, context.TargetAltitude, now + BriefingBuilder.Lead + 30);
            if (!plan.Ok)
            {
                return Refuse(operationName, change, contingencies, task.VesselId, plan.Error!);
            }
            LandingPrediction prediction = plan.Prediction!;
            double descent = powered ? prediction.DeltaVExpended + CorrectionReserve + TouchdownReserve(body, craft, context.TargetAltitude, chutes) : 0;
            var demands = new List<BurnDemand> { new BurnDemand(plan.DeorbitDeltaV, null), new BurnDemand(descent, null) };
            StageAllocation allocation = StageAllocation.Allocate(task.Stages, demands);
            BurnTiming deorbit = BurnTiming.Compute(task.Stages, allocation.Burns[0].Start, plan.DeorbitDeltaV);
            double deorbitStart = Math.Max(now, plan.BurnUt - deorbit.TimeToHalf);
            double descentStart = deorbitStart + deorbit.Duration + DescentLead;
            var burns = new List<PlannedBurn>
            {
                new PlannedBurn(task.VesselId, DeorbitLabel, plan.BurnUt, deorbitStart, deorbit.Duration, plan.Prograde, plan.Normal, plan.Radial, expected: Deorbited(task.Orbit, plan)),
                new PlannedBurn(task.VesselId, DescentLabel, descentStart, descentStart, Math.Max(0, prediction.Ut - descentStart), descent, 0, 0),
            };

            double need = plan.DeorbitDeltaV + descent;
            double have = task.AvailableDeltaV;
            double margin = need > 0 ? (have - need) / need : double.PositiveInfinity;
            double shortBy = Math.Max(allocation.Uncovered, need - have);
            if (shortBy > 0)
            {
                contingencies.Add(new Contingency(Severity.Red, task.VesselId, "abort: short of delta-v by " + Briefing.F(shortBy, "0.0") + " m/s to land; the vessel stays in orbit"));
            }
            else if (margin < BriefingBuilder.MarginFloor)
            {
                contingencies.Add(new Contingency(Severity.Amber, task.VesselId, "delta-v margin " + Briefing.F(margin * 100, "0.0") + " % is thin for a landing"));
            }
            if (plan.Miss > WarnMiss)
            {
                contingencies.Add(new Contingency(Severity.Amber, task.VesselId, "predicted miss " + Briefing.F(plan.Miss / 1000, "0.0") + " km; corrections during the descent close it"));
            }
            if (!powered)
            {
                contingencies.Add(new Contingency(Severity.Amber, task.VesselId, "no engine: the parachutes alone set the touchdown speed"));
            }
            var stages = allocation.Stages.Select(u => new BriefedStage(task.VesselId, u, surfaceGravity)).ToList();
            return new Briefing(operationName, change.Describe(), burns, need, have, margin, stages, contingencies, landing: prediction);
        }

        /// <summary>The vessel as the descent sees it, in SI units: the stages give thrust in kN, the context the mass in kg.</summary>
        public static LandingCraft Craft(VesselTask task, LandingContext context)
        {
            StageDeltaV? engine = task.Stages.FirstOrDefault(s => s.Thrust > 0 && s.DeltaV > 0);
            return new LandingCraft(
                context.Mass,
                context.DragArea,
                engine == null ? 0 : engine.Thrust * 1000,
                context.ChuteArea,
                context.ChuteDeployAltitude,
                engine == null ? 0 : engine.ExhaustVelocity);
        }

        /// <summary>Gravity paid while the final descent sinks slowly from the end of braking to the ground.</summary>
        private static double TouchdownReserve(LandingBody body, LandingCraft craft, double targetAltitude, bool chutes)
        {
            double height = DescentRules.DecelerationEndAltitude(body, craft, targetAltitude) - targetAltitude;
            double seconds = height / 5 + 10;
            return (chutes ? 0.3 : 1) * body.SurfaceGravity * seconds;
        }

        private static OrbitElements Deorbited(OrbitElements orbit, LandingPlan plan)
        {
            StateVector at = orbit.StateAt(plan.BurnUt);
            Vector3d velocity = at.Velocity + at.Prograde * plan.Prograde + at.Normal * plan.Normal + at.RadialOut * plan.Radial;
            return OrbitElements.FromState(orbit.Mu, new StateVector(at.Position, velocity), plan.BurnUt);
        }

        private static Briefing Refuse(string operationName, OrbitChange change, List<Contingency> contingencies, string vesselId, string text)
        {
            contingencies.Add(new Contingency(Severity.Red, vesselId, text));
            return new Briefing(operationName, change.Describe(), Array.Empty<PlannedBurn>(), 0, 0, double.PositiveInfinity, Array.Empty<BriefedStage>(), contingencies);
        }
    }
}

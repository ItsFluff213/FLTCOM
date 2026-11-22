using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FLTCOM.Core.AutoSystems;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Protocol;
using FLTCOM.Guidance;

namespace FLTCOM.Execution
{
    /// <summary>The briefing's stage plan and automatic actions for every vessel that burns, read from its parts.</summary>
    internal sealed class AutoBriefing
    {
        private AutoBriefing(List<StagePlanLine> plan, List<AutoAction> actions, List<Contingency> hazards)
        {
            Plan = plan;
            Actions = actions;
            Hazards = hazards;
        }

        public List<StagePlanLine> Plan { get; }

        public List<AutoAction> Actions { get; }

        /// <summary>Stages the auto-stager would stop at, red so the operation cannot be approved.</summary>
        public List<Contingency> Hazards { get; }

        public static AutoBriefing Of(OrbitChange change, Briefing briefing)
        {
            var plan = new List<StagePlanLine>();
            var actions = new List<AutoAction>();
            var hazards = new List<Contingency>();
            foreach (string id in briefing.Stages.Select(s => s.VesselId).Distinct())
            {
                if (VesselTasks.Find(id) is not global::Vessel vessel || !uint.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out uint wireId))
                {
                    continue;
                }
                double depth = vessel.mainBody.atmosphere ? vessel.mainBody.atmosphereDepth : 0;
                var figures = briefing.Stages.Where(s => s.VesselId == id).Select(s => new StageFigure(s.Stage, s.Assigned, s.BurnTime)).ToList();
                foreach (StageStep step in StagePlanner.Plan(VesselTasks.StageParts(vessel), VesselTasks.CurrentStage(vessel), new StagePlanContext(change.Kind, depth, figures, change.AutoSystems)))
                {
                    plan.Add(new StagePlanLine(wireId, step.Stage, step.Does, step.When, step.Fires, step.Hazard == Severity.Red ? Alert.Red : step.Hazard == Severity.Amber ? Alert.Amber : (Alert?)null, step.Warning));
                    if (change.AutoSystems && step.Hazard == Severity.Red)
                    {
                        hazards.Add(new Contingency(Severity.Red, id, vessel.vesselName + " stage " + step.Stage + " " + step.Warning + ": the auto-stager stops there, so restage it first"));
                    }
                }
                foreach (string text in AutoPlan.Actions(change.Kind, SystemsPilot.Kit(VesselTasks.Prefabs(vessel)), depth, change.AutoSystems))
                {
                    actions.Add(new AutoAction(wireId, text));
                }
            }
            return new AutoBriefing(plan, actions, hazards);
        }
    }
}

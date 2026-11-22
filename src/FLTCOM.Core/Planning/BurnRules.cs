using System;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    public enum CorrectionVerdict
    {
        /// <summary>The residual is small enough to fly as a trim burn inside the approved operation.</summary>
        FlyInside,
        /// <summary>The residual needs a new briefing and approval.</summary>
        NeedsBriefing,
    }

    public static class CorrectionRule
    {
        public const double Fraction = 0.01;
        public const double Floor = 2.0;

        public static double Threshold(double burnDeltaV) => Math.Max(Fraction * burnDeltaV, Floor);

        public static CorrectionVerdict Classify(double burnDeltaV, double residualDeltaV) =>
            residualDeltaV < Threshold(burnDeltaV) ? CorrectionVerdict.FlyInside : CorrectionVerdict.NeedsBriefing;
    }

    public sealed class ReplanResult
    {
        private ReplanResult(OrbitPlan? plan, string? reason)
        {
            Plan = plan;
            Reason = reason;
        }

        /// <summary>The re-planned burns, or null when the operation is now Missed.</summary>
        public OrbitPlan? Plan { get; }

        public string? Reason { get; }

        public bool Missed => Plan == null;

        public static ReplanResult To(OrbitPlan plan) => new ReplanResult(plan, null);

        public static ReplanResult Miss(string reason) => new ReplanResult(null, reason);
    }

    public static class MissedBurn
    {
        /// <summary>Re-plans a missed change from the current orbit to the next window, or reports Missed when the delta-v no longer fits.</summary>
        public static ReplanResult Replan(OrbitChange change, OrbitElements orbit, CentralBody body, double availableDeltaV, double now)
        {
            OrbitPlan plan = OrbitPlanner.Plan(change, orbit, body, now + BriefingBuilder.Lead);
            if (!plan.Ok)
            {
                return ReplanResult.Miss(plan.Error!);
            }
            return plan.TotalDeltaV <= availableDeltaV
                ? ReplanResult.To(plan)
                : ReplanResult.Miss("the next window needs " + Briefing.F(plan.TotalDeltaV, "0.0") + " m/s but only " + Briefing.F(availableDeltaV, "0.0") + " m/s is left");
        }
    }
}

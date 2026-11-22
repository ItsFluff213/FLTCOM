using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Planning;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Constellation
{
    public enum ReturnKind
    {
        /// <summary>Deorbit into the home atmosphere and come down on parachutes.</summary>
        Land,
        /// <summary>Transfer back to the home planet, then land there.</summary>
        TransferAndLand,
        /// <summary>Park the empty carrier on an orbit clear of the constellation.</summary>
        Disposal,
        /// <summary>Nothing more: the carrier stays on its last orbit.</summary>
        Stay,
        /// <summary>Lower the periapsis deep into the atmosphere: the carrier burns up or crashes, no parachutes needed.</summary>
        BurnUp,
    }

    /// <summary>What the carrier does once the payloads are out. Disposal carries its burns; a return is planned when the carrier gets there.</summary>
    public sealed class ReturnPlan
    {
        public ReturnPlan(ReturnKind kind, double deltaV, double available, string text, IReadOnlyList<ImpulsiveBurn>? burns = null, bool fallback = false)
        {
            Kind = kind;
            DeltaV = deltaV;
            Available = available;
            Text = text;
            Burns = burns ?? Array.Empty<ImpulsiveBurn>();
            Fallback = fallback;
        }

        public static ReturnPlan None { get; } = new ReturnPlan(ReturnKind.Stay, 0, 0, string.Empty);

        public ReturnKind Kind { get; }

        /// <summary>Estimated delta-v of the return or disposal, m/s.</summary>
        public double DeltaV { get; }

        /// <summary>Delta-v the carrier has left once the payloads are out.</summary>
        public double Available { get; }

        public string Text { get; }

        public IReadOnlyList<ImpulsiveBurn> Burns { get; }

        /// <summary>True when the carrier could not come home and this is the fallback: the briefing warns.</summary>
        public bool Fallback { get; }

        public bool GoesHome => Kind == ReturnKind.Land || Kind == ReturnKind.TransferAndLand;
    }

    /// <summary>Whether the launch stage splits off in the parking orbit and lands, and why not when it stays attached.</summary>
    public sealed class LaunchStagePlan
    {
        public LaunchStagePlan(LaunchStage? stage, bool comesHome, string? name, double deltaV, double available, string text)
        {
            Stage = stage;
            ComesHome = comesHome;
            Name = name;
            DeltaV = deltaV;
            Available = available;
            Text = text;
        }

        /// <summary>The launch stage found below a decoupler; null when the vessel has none that could fly on its own.</summary>
        public LaunchStage? Stage { get; }

        public string? Name { get; }

        /// <summary>Estimated deorbit delta-v, m/s.</summary>
        public double DeltaV { get; }

        /// <summary>The launch stage's own delta-v, m/s.</summary>
        public double Available { get; }

        public string Text { get; }

        public bool ComesHome { get; }

        public static LaunchStagePlan None { get; } = new LaunchStagePlan(null, false, null, 0, 0, LaunchStage.Needs);

        /// <summary>Splits the launch stage off when it can land at home from the parking orbit.</summary>
        public static LaunchStagePlan For(string carrierName, CentralBody body, OrbitElements parking, IReadOnlyList<CarrierPart> parts)
        {
            if (LaunchStage.Find(parts) is not LaunchStage stage)
            {
                return None;
            }
            string name = LaunchStage.NameOf(carrierName);
            double has = stage.Own.DeltaV;
            if (body.Name != CarrierReturn.Home)
            {
                return new LaunchStagePlan(stage, false, name, 0, has, "stays attached: it comes home only from an orbit around " + CarrierReturn.Home);
            }
            double need = CarrierReturn.Deorbit(body, parking);
            if (need * CarrierReturn.Reserve > has)
            {
                return new LaunchStagePlan(stage, false, name, need, has, "stays attached: it has " + Briefing.F(has, "0") + " m/s of the " + Briefing.F(need * CarrierReturn.Reserve, "0") + " m/s its deorbit needs");
            }
            return new LaunchStagePlan(stage, true, name, need, has, "splits off in the parking orbit, deorbits about " + Briefing.F(need, "0") + " m/s once the satellites are out, then lands on parachutes at the launch pad");
        }
    }

    public static class CarrierReturn
    {
        /// <summary>The planet the carrier comes home to: where the launch pad is.</summary>
        public const string Home = "Kerbin";

        /// <summary>A return aims its periapsis this far down the home atmosphere.</summary>
        public const double EntryDepth = 0.45;

        public const double Reserve = 1.1;

        /// <param name="lowSide">True to dispose below the constellation, false above it.</param>
        public static ReturnPlan Plan(CentralBody body, SolarSystem? system, OrbitElements orbit, double available, bool chutes, double ringRadius, bool lowSide, double afterUt, EndOfLife choice = EndOfLife.Auto)
        {
            switch (choice)
            {
                case EndOfLife.LandHome:
                    return LandHome(body, system, orbit, available, chutes, ringRadius, lowSide, afterUt);
                case EndOfLife.Park:
                    return Disposal(body, orbit, available, ringRadius, lowSide, afterUt, null);
                default:
                    ReturnPlan burnUp = BurnUp(body, orbit, available, afterUt, out string? why);
                    return why == null ? burnUp : Disposal(body, orbit, available, ringRadius, lowSide, afterUt, why, fallback: choice == EndOfLife.BurnUp);
            }
        }

        private static ReturnPlan BurnUp(CentralBody body, OrbitElements orbit, double available, double afterUt, out string? why)
        {
            why = null;
            if (body.AtmosphereHeight <= 0)
            {
                why = body.Name + " has no atmosphere to burn up in";
                return ReturnPlan.None;
            }
            double periapsis = body.AtmosphereHeight * EntryDepth;
            if (orbit.PeriapsisRadius <= body.Radius + periapsis)
            {
                return new ReturnPlan(ReturnKind.BurnUp, 0, available, "already falls into the atmosphere and burns up");
            }
            OrbitPlan plan = OrbitPlanner.Plan(OrbitChange.Periapsis(periapsis), orbit, body, afterUt);
            if (!plan.Ok)
            {
                why = plan.Error;
                return ReturnPlan.None;
            }
            if (plan.TotalDeltaV * Reserve > available)
            {
                why = "the transfer stage has " + Briefing.F(available, "0") + " m/s left of the " + Briefing.F(plan.TotalDeltaV * Reserve, "0") + " m/s a burn-up needs";
                return ReturnPlan.None;
            }
            var burns = plan.Burns.Select(b => new ImpulsiveBurn(ConstellationPlanner.BurnUpPrefix + b.Label, b.NodeUt, b.Prograde, b.Normal, b.Radial, b.OrbitAfter, b.Body)).ToList();
            return new ReturnPlan(ReturnKind.BurnUp, plan.TotalDeltaV, available, "periapsis to " + ConstellationPlanner.Km(periapsis) + ", about " + Briefing.F(plan.TotalDeltaV, "0") + " m/s, then it burns up in the atmosphere", burns);
        }

        private static ReturnPlan LandHome(CentralBody body, SolarSystem? system, OrbitElements orbit, double available, bool chutes, double ringRadius, bool lowSide, double afterUt)
        {
            string? why = null;
            double need = 0;
            ReturnKind kind = ReturnKind.Land;
            if (body.Name == Home)
            {
                need = Deorbit(body, orbit);
            }
            else if (system != null && system.Contains(body.Name) && system.Contains(Home) && Escape(system, body, orbit) is double escape)
            {
                kind = ReturnKind.TransferAndLand;
                need = escape;
            }
            else
            {
                why = "a return from " + body.Name + " is not planned";
            }
            if (why == null && !chutes)
            {
                why = "the carrier has no parachutes";
            }
            if (why == null && need * Reserve > available)
            {
                why = "the carrier has " + Briefing.F(available, "0") + " m/s left of the " + Briefing.F(need * Reserve, "0") + " m/s a return needs";
            }
            if (why == null)
            {
                string text = kind == ReturnKind.Land
                    ? "deorbit about " + Briefing.F(need, "0") + " m/s, then down on parachutes at the launch pad"
                    : "transfer to " + Home + " about " + Briefing.F(need, "0") + " m/s, then deorbit and down on parachutes";
                return new ReturnPlan(kind, need, available, text);
            }
            return Disposal(body, orbit, available, ringRadius, lowSide, afterUt, why, fallback: true);
        }

        /// <param name="why">Why it parks; null when the operator chose to park.</param>
        private static ReturnPlan Disposal(CentralBody body, OrbitElements orbit, double available, double ringRadius, bool lowSide, double afterUt, string? why, bool fallback = false)
        {
            string lead = why == null ? string.Empty : why + ": ";
            double gap = Math.Max(100_000, ringRadius * 0.05);
            double low = ringRadius - gap;
            double radius = lowSide && low - body.Radius > body.SafeFloor + ConstellationPlanner.Clearance ? low : ringRadius + gap;
            if (radius >= body.SphereOfInfluence * ConstellationPlanner.SoiMargin)
            {
                radius = low;
            }
            double far = Math.Abs(orbit.ApoapsisRadius - ringRadius) > Math.Abs(orbit.PeriapsisRadius - ringRadius) ? orbit.ApoapsisRadius : orbit.PeriapsisRadius;
            if (Math.Abs(far - ringRadius) >= gap)
            {
                radius = far;
            }
            OrbitPlan plan = OrbitPlanner.Plan(OrbitChange.CircularAt(radius - body.Radius), orbit, body, afterUt);
            double dv = plan.Ok ? plan.TotalDeltaV : double.PositiveInfinity;
            if (plan.Ok && dv * Reserve <= available)
            {
                return new ReturnPlan(ReturnKind.Disposal, dv, available, lead + "disposal orbit at " + ConstellationPlanner.Km(radius - body.Radius) + (why == null ? string.Empty : " instead") + ", clear of the constellation", plan.Burns, fallback);
            }
            return new ReturnPlan(ReturnKind.Stay, plan.Ok ? dv : 0, available, lead + "not enough delta-v for a disposal orbit, so the carrier stays on its release orbit", fallback: true);
        }

        /// <summary>From the apoapsis, lower the periapsis into the atmosphere.</summary>
        public static double Deorbit(CentralBody body, OrbitElements orbit)
        {
            double mu = body.GravParameter;
            double ra = orbit.ApoapsisRadius;
            double rp = body.Radius + body.AtmosphereHeight * EntryDepth;
            if (orbit.PeriapsisRadius <= rp)
            {
                return 0;
            }
            double now = Math.Sqrt(mu * (2 / ra - 1 / orbit.SemiMajorAxis));
            double then = Math.Sqrt(mu * (2 / ra - 2 / (ra + rp)));
            return now - then;
        }

        /// <summary>
        /// Ejection from the body onto a Hohmann path that drops to the home planet: the home atmosphere when the body is
        /// its moon, its orbit when they share a parent. Null when the body is neither.
        /// </summary>
        public static double? Escape(SolarSystem system, CentralBody body, OrbitElements orbit)
        {
            SystemBody here = system[body.Name];
            SystemBody home = system[Home];
            if (here.Orbit == null || here.Parent == null)
            {
                return null;
            }
            double from = here.Orbit.SemiMajorAxis;
            double to;
            if (here.Parent == Home)
            {
                to = home.Body.Radius + home.Body.AtmosphereHeight * EntryDepth;
            }
            else if (home.Orbit != null && here.Parent == home.Parent)
            {
                to = home.Orbit.SemiMajorAxis;
            }
            else
            {
                return null;
            }
            double mu = here.Orbit.Mu;
            double vInf = Math.Abs(Math.Sqrt(mu / from) - Math.Sqrt(mu * (2 / from - 2 / (from + to))));
            double rp = orbit.PeriapsisRadius;
            double vp = Math.Sqrt(body.GravParameter * (2 / rp - 1 / orbit.SemiMajorAxis));
            return Math.Sqrt(vInf * vInf + 2 * body.GravParameter / rp) - vp;
        }
    }
}

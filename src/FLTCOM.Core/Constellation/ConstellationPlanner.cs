using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FLTCOM.Core.Planning;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Constellation
{
    public enum DeployMethod
    {
        /// <summary>The carrier flies a resonant orbit and each payload circularizes itself at the release apsis.</summary>
        Resonant,
        /// <summary>The carrier circularizes, releases, and phases between releases; the payloads never burn.</summary>
        CarrierPhasing,
    }

    /// <summary>
    /// One release. The carrier lets go of the payload at <see cref="ReleaseUt"/>; a resonant release then has the payload
    /// circularize at <see cref="NodeUt"/>, the apsis on the target circle.
    /// </summary>
    public sealed class Release
    {
        public Release(Payload payload, string name, int pass, double releaseUt, double nodeUt, bool atApoapsis, double circularizeDeltaV, ImpulsiveBurn? circularize, OrbitElements final)
        {
            Payload = payload;
            Name = name;
            Pass = pass;
            ReleaseUt = releaseUt;
            NodeUt = nodeUt;
            AtApoapsis = atApoapsis;
            CircularizeDeltaV = circularizeDeltaV;
            Circularize = circularize;
            Final = final;
        }

        public Payload Payload { get; }

        public string Name { get; }

        public int Pass { get; }

        public double ReleaseUt { get; }

        public double NodeUt { get; }

        public bool AtApoapsis { get; }

        public double CircularizeDeltaV { get; }

        /// <summary>The payload's own burn; null when the carrier circularized it.</summary>
        public ImpulsiveBurn? Circularize { get; }

        /// <summary>The payload's orbit once it is on the target circle.</summary>
        public OrbitElements Final { get; }

        /// <summary>Signed angle from its even slot, radians, once every payload is out.</summary>
        public double SpacingError { get; internal set; }
    }

    public sealed class DeployPlan
    {
        internal DeployPlan(string? error, double? suggestedAltitude)
        {
            Error = error;
            SuggestedAltitude = suggestedAltitude;
            CarrierBurns = Array.Empty<ImpulsiveBurn>();
            Releases = Array.Empty<Release>();
            Return = ReturnPlan.None;
        }

        internal DeployPlan(double altitude, DeployMethod method, OrbitElements working, IReadOnlyList<ImpulsiveBurn> carrierBurns, IReadOnlyList<Release> releases, OrbitElements carrierAfter)
        {
            Altitude = altitude;
            Method = method;
            Working = working;
            CarrierBurns = carrierBurns;
            Releases = releases;
            CarrierAfter = carrierAfter;
            Return = ReturnPlan.None;
        }

        public string? Error { get; }

        /// <summary>When the altitude is refused: the highest one that works, in metres.</summary>
        public double? SuggestedAltitude { get; }

        public bool Ok => Error == null;

        public double Altitude { get; }

        public DeployMethod Method { get; }

        /// <summary>The carrier's orbit while it releases: the resonant orbit, or the target circle.</summary>
        public OrbitElements? Working { get; }

        /// <summary>The carrier's own burns before the return, in time order.</summary>
        public IReadOnlyList<ImpulsiveBurn> CarrierBurns { get; }

        public IReadOnlyList<Release> Releases { get; }

        public OrbitElements? CarrierAfter { get; }

        public ReturnPlan Return { get; internal set; }

        public LaunchStagePlan LaunchStage { get; internal set; } = LaunchStagePlan.None;

        public double MaxSpacingError => Releases.Select(r => Math.Abs(r.SpacingError)).DefaultIfEmpty(0).Max();
    }

    public static class ConstellationPlanner
    {
        public const string ReleasePrefix = "release ";

        /// <summary>Label of the step that turns the carrier for home once the payloads are out.</summary>
        public const string ReturnLabel = "fly home";

        /// <summary>Label of the step after the last payload when the transfer stage does not fly home: spacing check, trim, launch stage.</summary>
        public const string SpacingLabel = "check spacing";

        /// <summary>Prefix of the transfer stage's burn into the atmosphere; that burn means to fall, so it raises no impact alarm.</summary>
        public const string BurnUpPrefix = "burn up: ";

        /// <summary>The launch stage splits off this long before the deployment's first burn.</summary>
        public const double SplitLead = 600;

        /// <summary>A synchronous circle must sit this far inside the sphere of influence.</summary>
        public const double SoiMargin = 0.95;

        /// <summary>The resonant or phasing orbit's low side keeps this far above the safe floor.</summary>
        public const double Clearance = 10_000;

        /// <summary>A released payload's burn starts at least this long after the release: switch, turn, settle.</summary>
        public const double ReleaseLead = 90;

        /// <summary>The carrier waits this long after a release before it burns again.</summary>
        public const double Separation = 60;

        /// <summary>The fly-home step comes this long after the last payload burn.</summary>
        public const double ReturnDelay = 300;

        public const int MaxPhasingOrbits = 30;

        public static readonly double SpacingTolerance = Angles.ToRadians(1);

        private static readonly double FlatInclination = Angles.ToRadians(0.1);

        public static bool IsRelease(Missions.PlannedBurn burn) => burn.ReleasePart != null;

        public static bool IsStep(Missions.PlannedBurn burn) => burn.ReleasePart != null || burn.Label == ReturnLabel || burn.Label == SpacingLabel;

        public static bool IsBurnUp(Missions.PlannedBurn burn) => burn.Label.StartsWith(BurnUpPrefix, StringComparison.Ordinal);

        /// <summary>A release of a satellite, not of the launch stage.</summary>
        public static bool IsSatelliteRelease(Missions.PlannedBurn burn) => IsRelease(burn) && !burn.Label.EndsWith(Constellation.LaunchStage.Suffix, StringComparison.Ordinal);

        /// <summary>Altitude of the circle whose period is the body's day; null when the day is unknown.</summary>
        public static double? SynchronousAltitude(CentralBody body) =>
            body.RotationPeriod > 0 ? Math.Pow(body.GravParameter * body.RotationPeriod * body.RotationPeriod / (4 * Math.PI * Math.PI), 1.0 / 3) - body.Radius : (double?)null;

        /// <summary>The highest circle a constellation can use, whole kilometres.</summary>
        public static double HighestAltitude(CentralBody body) =>
            Math.Floor((Math.Min(body.SphereOfInfluence * SoiMargin, 1e12) - body.Radius) / 1000) * 1000;

        public static string? Refusal(CentralBody body, double altitude)
        {
            double highest = HighestAltitude(body);
            if (altitude > highest)
            {
                return Km(altitude) + " is outside " + body.Name + "'s sphere of influence; the highest valid altitude is " + Km(highest);
            }
            if (altitude <= body.SafeFloor + Clearance)
            {
                return Km(altitude) + " is under " + body.Name + "'s safe floor of " + Km(body.SafeFloor + Clearance) + "; the highest valid altitude is " + Km(highest);
            }
            return null;
        }

        public static DeployPlan Plan(string carrierName, CentralBody body, OrbitElements orbit, CarrierScan scan, double altitude, double earliestUt, RingPlane? ring = null)
        {
            ring ??= RingPlane.Equatorial;
            if (ring.Refusal is string tilt)
            {
                return new DeployPlan(tilt, null);
            }
            if (Refusal(body, altitude) is string refused)
            {
                return new DeployPlan(refused, HighestAltitude(body));
            }
            if (scan.Payloads.Count == 0)
            {
                return new DeployPlan(NoPayloads(scan), null);
            }
            double radius = body.Radius + altitude;
            bool ownEngines = scan.Payloads.All(p => p.Own.CanBurn);
            DeployPlan? plan = null;
            string? error = null;
            if (ownEngines)
            {
                foreach (bool inside in new[] { true, false })
                {
                    DeployPlan? candidate = Resonant(carrierName, body, orbit, scan, ring, altitude, radius, inside, earliestUt, ref error);
                    if (candidate != null && (plan == null || Cost(candidate) < Cost(plan)))
                    {
                        plan = candidate;
                    }
                }
            }
            else
            {
                plan = Phasing(carrierName, body, orbit, scan, ring, altitude, radius, earliestUt, ref error);
            }
            if (plan == null)
            {
                return new DeployPlan(error ?? "no resonant orbit fits " + body.Name + " at " + Km(altitude), HighestAltitude(body));
            }
            Space(plan.Releases, plan.Method == DeployMethod.Resonant && plan.Working!.ApoapsisRadius > radius * 1.0001 ? -1 : 1);
            return plan;
        }

        /// <summary>
        /// Every payload's angle from its even slot once the last is out: slots step by a whole share of the circle from
        /// the first payload, in the direction the releases fall. Negative is behind the slot.
        /// </summary>
        internal static void Space(IReadOnlyList<Release> releases, int direction)
        {
            if (releases.Count == 0)
            {
                return;
            }
            double reference = releases.Max(r => r.NodeUt) + 1;
            IReadOnlyList<double> errors = SpacingErrors(releases.Select(r => r.Final).ToList(), reference, direction);
            for (int k = 0; k < releases.Count; k++)
            {
                releases[k].SpacingError = errors[k];
            }
        }

        /// <summary>
        /// Signed angles from even slots for orbits flown, in the order they were released, measured in the plane they
        /// share by argument of latitude.
        /// </summary>
        public static IReadOnlyList<double> SpacingErrors(IReadOnlyList<OrbitElements> orbits, double ut, int direction)
        {
            if (orbits.Count == 0)
            {
                return Array.Empty<double>();
            }
            Vector3d normal = RingPlane.MeanNormal(orbits);
            double first = RingPlane.ArgumentOfLatitude(orbits[0], ut, normal);
            double step = direction * Angles.TwoPi / orbits.Count;
            return orbits.Select((o, k) => WrapPi(RingPlane.ArgumentOfLatitude(o, ut, normal) - first - k * step)).ToList();
        }

        private static double Cost(DeployPlan plan) =>
            plan.CarrierBurns.Sum(b => b.DeltaV) + plan.Releases.Sum(r => r.CircularizeDeltaV);

        private static DeployPlan? Resonant(string carrierName, CentralBody body, OrbitElements orbit, CarrierScan scan, RingPlane ring, double altitude, double radius, bool inside, double earliestUt, ref string? error)
        {
            int n = scan.Payloads.Count;
            double ratio = inside ? (n - 1.0) / n : (n + 1.0) / n;
            if (ratio <= 0)
            {
                return null;
            }
            double a = radius * Math.Pow(ratio, 2.0 / 3);
            double other = 2 * a - radius;
            if (inside && other <= body.Radius + body.SafeFloor + Clearance)
            {
                error = "the resonant orbit's periapsis of " + Km(other - body.Radius) + " would hit " + body.Name + "'s safe floor";
                return null;
            }
            if (!inside && other >= body.SphereOfInfluence * SoiMargin)
            {
                error = "the resonant orbit's apoapsis would leave " + body.Name + "'s sphere of influence";
                return null;
            }
            var burns = new List<ImpulsiveBurn>();
            OrbitElements working = orbit;
            double after = earliestUt;
            error = ReachRadius(burns, ref working, body, ring, radius, ref after)
                ?? OrbitPlanner.ApsisBurn(burns, ref working, after, AtApoapsis(working, radius), other, "resonant orbit " + Km(Math.Min(other, radius) - body.Radius) + " x " + Km(Math.Max(other, radius) - body.Radius));
            if (error != null)
            {
                return null;
            }
            bool apoapsis = AtApoapsis(working, radius);
            double first = working.NextTimeAtTrueAnomaly(apoapsis ? Math.PI : 0, burns[burns.Count - 1].NodeUt + 1);
            var releases = new List<Release>();
            foreach (Payload payload in scan.Payloads)
            {
                double node = first + (payload.Number - 1) * working.Period;
                StateVector state = working.StateAt(node);
                double speed = Math.Sqrt(working.Mu / state.Position.Magnitude);
                var own = new List<ImpulsiveBurn>();
                OrbitElements final = working;
                string name = Payload.NameOf(carrierName, payload.Number);
                OrbitPlanner.Apply(own, ref final, node, state, state.Prograde * speed, "circularize " + name);
                double lead = Math.Max(ReleaseLead, HalfBurnTime(payload.Own, payload.Mass, own[0].DeltaV) + ReleaseLead);
                releases.Add(new Release(payload, name, payload.Number, node - lead, node, apoapsis, own[0].DeltaV, own[0], final));
            }
            return new DeployPlan(altitude, DeployMethod.Resonant, working, burns, releases, working);
        }

        private static DeployPlan? Phasing(string carrierName, CentralBody body, OrbitElements orbit, CarrierScan scan, RingPlane ring, double altitude, double radius, double earliestUt, ref string? error)
        {
            int n = scan.Payloads.Count;
            int m = 0;
            double phasingA = 0;
            if (n > 1)
            {
                for (int k = 1; k <= MaxPhasingOrbits && m == 0; k++)
                {
                    double a = radius * Math.Pow(1 - 1.0 / (n * k), 2.0 / 3);
                    if (2 * a - radius > body.Radius + body.SafeFloor + Clearance)
                    {
                        m = k;
                        phasingA = a;
                    }
                }
                if (m == 0)
                {
                    error = "no phasing orbit within " + MaxPhasingOrbits + " revolutions clears " + body.Name + "'s safe floor";
                    return null;
                }
            }
            var burns = new List<ImpulsiveBurn>();
            OrbitElements working = orbit;
            double after = earliestUt;
            error = ReachRadius(burns, ref working, body, ring, radius, ref after)
                ?? OrbitPlanner.ApsisBurn(burns, ref working, after, AtApoapsis(working, radius), radius, "circularize at " + Km(altitude));
            if (error != null)
            {
                return null;
            }
            OrbitElements circleOrbit = working;
            double t = burns[burns.Count - 1].NodeUt + ReturnDelay;
            var releases = new List<Release>();
            foreach (Payload payload in scan.Payloads)
            {
                string name = Payload.NameOf(carrierName, payload.Number);
                releases.Add(new Release(payload, name, payload.Number, t, t, false, 0, null, working));
                if (payload.Number == n)
                {
                    break;
                }
                double burnAt = t + Separation;
                StateVector state = working.StateAt(burnAt);
                double r = state.Position.Magnitude;
                OrbitPlanner.Apply(burns, ref working, burnAt, state, state.Prograde * Math.Sqrt(working.Mu * (2 / r - 1 / phasingA)), "phasing orbit before " + Payload.NameOf(carrierName, payload.Number + 1));
                double back = burnAt + m * working.Period;
                StateVector there = working.StateAt(back);
                OrbitPlanner.Apply(burns, ref working, back, there, there.Prograde * Math.Sqrt(working.Mu / there.Position.Magnitude), "back on the circle for " + Payload.NameOf(carrierName, payload.Number + 1));
                t = back + Separation;
            }
            return new DeployPlan(altitude, DeployMethod.CarrierPhasing, circleOrbit, burns, releases, working);
        }

        /// <summary>
        /// Swings the orbit into the ring plane if it is out of it (an equatorial ring flattens it at the next node, any
        /// other plane turns at the cheaper node), then puts one apsis on the target circle.
        /// </summary>
        private static string? ReachRadius(List<ImpulsiveBurn> burns, ref OrbitElements orbit, CentralBody body, RingPlane ring, double radius, ref double after)
        {
            if (ring.IsEquatorial && orbit.Inclination > FlatInclination)
            {
                OrbitPlan flat = OrbitPlanner.Plan(OrbitChange.Inclination(0), orbit, body, after);
                if (!flat.Ok)
                {
                    return flat.Error;
                }
                burns.AddRange(flat.Burns);
                orbit = flat.Final!;
                after = burns.Count > 0 ? burns[burns.Count - 1].NodeUt + 1 : after;
            }
            else if (!ring.IsEquatorial)
            {
                int before = burns.Count;
                if (OrbitPlanner.MatchPlane(burns, ref orbit, after, ring.Normal, cheaperNode: true, label: "plane change into the " + ring.Describe()) is string error)
                {
                    return error;
                }
                after = burns.Count > before ? burns[burns.Count - 1].NodeUt + 1 : after;
            }
            OrbitPlan reach = OrbitPlanner.Plan(radius >= orbit.PeriapsisRadius ? OrbitChange.Apoapsis(radius - body.Radius) : OrbitChange.Periapsis(radius - body.Radius), orbit, body, after);
            if (!reach.Ok)
            {
                return reach.Error;
            }
            burns.AddRange(reach.Burns);
            orbit = reach.Final!;
            after = burns.Count > 0 ? burns[burns.Count - 1].NodeUt + 1 : after;
            return null;
        }

        private static bool AtApoapsis(OrbitElements orbit, double radius) =>
            Math.Abs(orbit.ApoapsisRadius - radius) <= Math.Abs(orbit.PeriapsisRadius - radius);

        /// <summary>Seconds to burn the first half of <paramref name="deltaV"/>: the lead before the node.</summary>
        public static double HalfBurnTime(Drive drive, double mass, double deltaV) => BurnTime(drive, mass, deltaV / 2);

        public static double BurnTime(Drive drive, double mass, double deltaV) =>
            drive.Thrust > 0 && drive.Isp > 0 ? drive.PropellantFor(mass, deltaV) * drive.Isp * Propulsion.DeltaVEstimator.G0 / drive.Thrust : 0;

        private static string NoPayloads(CarrierScan scan) =>
            scan.Uncontrolled.Count > 0
                ? "no payload has its own probe core or crewed pod: the parts beyond the decouplers (" + string.Join(", ", scan.Uncontrolled.Distinct()) + ") could not be flown once released. Add a probe core, or a pod with its pilot aboard, to each satellite"
                : "no payload found: a payload sits beyond a decoupler, separator or docking port and has its own probe core or crewed pod";

        internal static double WrapPi(double radians)
        {
            double a = Angles.Wrap(radians);
            return a > Math.PI ? a - Angles.TwoPi : a;
        }

        internal static string Km(double metres) => (metres / 1000).ToString("0.#", CultureInfo.InvariantCulture) + " km";
    }

}

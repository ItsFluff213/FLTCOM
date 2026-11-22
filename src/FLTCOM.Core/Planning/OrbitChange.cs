using System;
using System.Collections.Generic;
using FLTCOM.Core.Landing;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    public enum OrbitChangeKind
    {
        /// <summary>Raise the periapsis to the apoapsis at the next apoapsis.</summary>
        Circularize,
        ChangeApoapsis,
        ChangePeriapsis,
        ChangeInclination,
        /// <summary>Reach a circular orbit at the target altitude in two apsis burns.</summary>
        CircularOrbit,
        StandardLowOrbit,
        /// <summary>Raise the apoapsis by the target in metres.</summary>
        RaiseApoapsis,
        /// <summary>One circular orbit at the target altitude, the vessels of a group equally spaced around it.</summary>
        SpreadEvenly,
        /// <summary>Fly from the ground to the orbit in <see cref="OrbitChange.LaunchTarget"/>; from the air, plan the burns that finish it.</summary>
        Launch,
        /// <summary>Fly to a circular orbit at the target altitude around <see cref="OrbitChange.Destination"/>, another body.</summary>
        Transfer,
        /// <summary>Close on the vessel in <see cref="OrbitChange.Rendezvous"/> and hold beside it.</summary>
        Rendezvous,
        /// <summary>Meet the vessel in <see cref="OrbitChange.Rendezvous"/>, then dock with RCS to its port.</summary>
        Dock,
        /// <summary>Deorbit and touch down at <see cref="OrbitChange.Landing"/>.</summary>
        Land,
        /// <summary>Release every payload onto one equatorial circle at the target altitude, evenly spaced, then bring the carrier home.</summary>
        DeployConstellation,
    }

    /// <summary>What a deployment's transfer stage does once its satellites are out.</summary>
    public enum EndOfLife
    {
        /// <summary>Burn up where the body has air and the delta-v suffices, otherwise park.</summary>
        Auto,
        BurnUp,
        LandHome,
        Park,
    }

    /// <summary>What the operator wants the orbit to become. Target is an altitude in metres, or an inclination in radians.</summary>
    public sealed class OrbitChange
    {
        public OrbitChange(OrbitChangeKind kind, double target = 0, LaunchTarget? launch = null, RendezvousTarget? rendezvous = null, string? destination = null, double? arrivalInclination = null, LandingTarget? landing = null, bool autoSystems = true, bool thenLand = false, RingPlane? ring = null, EndOfLife endOfLife = EndOfLife.Auto)
        {
            if (kind == OrbitChangeKind.Transfer && string.IsNullOrEmpty(destination))
            {
                throw new ArgumentException("a transfer needs a destination body", nameof(destination));
            }
            if (kind == OrbitChangeKind.Launch && launch == null)
            {
                throw new ArgumentException("a launch needs a target orbit", nameof(launch));
            }
            if (Meets(kind) && rendezvous == null)
            {
                throw new ArgumentException("a rendezvous needs a target vessel", nameof(rendezvous));
            }
            Kind = kind;
            Target = kind == OrbitChangeKind.Launch ? launch!.Apoapsis : Meets(kind) ? 0 : target;
            LaunchTarget = kind == OrbitChangeKind.Launch ? launch : null;
            Destination = kind == OrbitChangeKind.Transfer ? destination : null;
            ArrivalInclination = kind == OrbitChangeKind.Transfer ? arrivalInclination : null;
            Rendezvous = Meets(kind) ? rendezvous : null;
            Landing = kind == OrbitChangeKind.Land ? landing ?? LandingTarget.LaunchPad : null;
            AutoSystems = autoSystems;
            ThenLand = kind == OrbitChangeKind.Transfer && thenLand;
            Ring = kind == OrbitChangeKind.DeployConstellation ? ring ?? RingPlane.Equatorial : null;
            EndOfLife = kind == OrbitChangeKind.DeployConstellation ? endOfLife : EndOfLife.Auto;
        }

        /// <summary>The plane a deployment's ring lies in; null for every other kind.</summary>
        public RingPlane? Ring { get; }

        /// <summary>What a deployment's transfer stage does once its satellites are out; Auto for every other kind.</summary>
        public EndOfLife EndOfLife { get; }

        /// <summary>A transfer that lands at the launch pad once it arrives: how a carrier comes home from another body.</summary>
        public bool ThenLand { get; }

        /// <summary>The kinds that close on another vessel.</summary>
        public static bool Meets(OrbitChangeKind kind) => kind == OrbitChangeKind.Rendezvous || kind == OrbitChangeKind.Dock;

        /// <summary>Where a landing touches down; null for every other kind.</summary>
        public LandingTarget? Landing { get; }

        /// <summary>FLTCOM stages in every burn and works the gear, fairings and panels; off keeps to what the guidance did before.</summary>
        public bool AutoSystems { get; }

        public OrbitChange WithAutoSystems(bool on) =>
            on == AutoSystems ? this : new OrbitChange(Kind, Target, LaunchTarget, Rendezvous, Destination, ArrivalInclination, Landing, on, ThenLand, Ring, EndOfLife);

        public static OrbitChange Deploy(double altitude, RingPlane? ring = null, EndOfLife endOfLife = EndOfLife.Auto) => new OrbitChange(OrbitChangeKind.DeployConstellation, altitude, ring: ring, endOfLife: endOfLife);

        public static OrbitChange HomeTo(string body, double altitude) => new OrbitChange(OrbitChangeKind.Transfer, altitude, destination: body, thenLand: true);

        public static OrbitChange Land(LandingTarget target) => new OrbitChange(OrbitChangeKind.Land, landing: target);

        public OrbitChangeKind Kind { get; }

        public double Target { get; }

        /// <summary>The orbit a launch reaches; null for every other kind.</summary>
        public LaunchTarget? LaunchTarget { get; }

        /// <summary>The body a transfer flies to; null for every other kind.</summary>
        public string? Destination { get; }

        /// <summary>For a transfer, the inclination of the orbit at the destination, in radians; null takes whatever the pass gives, prograde.</summary>
        public double? ArrivalInclination { get; }
        /// <summary>The vessel, and maybe the docking port, to meet; null for every other kind.</summary>
        public RendezvousTarget? Rendezvous { get; }

        public static OrbitChange RendezvousWith(RendezvousTarget target) => new OrbitChange(OrbitChangeKind.Rendezvous, rendezvous: target);

        public static OrbitChange DockWith(RendezvousTarget target) => new OrbitChange(OrbitChangeKind.Dock, rendezvous: target);

        public static OrbitChange Circularize() => new OrbitChange(OrbitChangeKind.Circularize);

        public static OrbitChange Apoapsis(double altitude) => new OrbitChange(OrbitChangeKind.ChangeApoapsis, altitude);

        public static OrbitChange Periapsis(double altitude) => new OrbitChange(OrbitChangeKind.ChangePeriapsis, altitude);

        public static OrbitChange Inclination(double radians) => new OrbitChange(OrbitChangeKind.ChangeInclination, radians);

        public static OrbitChange CircularAt(double altitude) => new OrbitChange(OrbitChangeKind.CircularOrbit, altitude);

        public static OrbitChange StandardLow() => new OrbitChange(OrbitChangeKind.StandardLowOrbit);

        public static OrbitChange RaiseApoapsisBy(double metres) => new OrbitChange(OrbitChangeKind.RaiseApoapsis, metres);

        public static OrbitChange Spread(double altitude) => new OrbitChange(OrbitChangeKind.SpreadEvenly, altitude);

        public static OrbitChange Launch(LaunchTarget target) => new OrbitChange(OrbitChangeKind.Launch, launch: target);

        public static OrbitChange TransferTo(string body, double altitude, double? inclination = null) => new OrbitChange(OrbitChangeKind.Transfer, altitude, destination: body, arrivalInclination: inclination);

        public string Describe() => Kind switch
        {
            OrbitChangeKind.Circularize => "circularize at apoapsis",
            OrbitChangeKind.ChangeApoapsis => "apoapsis to " + Km(Target),
            OrbitChangeKind.ChangePeriapsis => "periapsis to " + Km(Target),
            OrbitChangeKind.ChangeInclination => "inclination to " + Angles.ToDegrees(Target).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " deg",
            OrbitChangeKind.CircularOrbit => "circular orbit at " + Km(Target),
            OrbitChangeKind.StandardLowOrbit => "standard low orbit",
            OrbitChangeKind.RaiseApoapsis => "raise apoapsis by " + Km(Target),
            OrbitChangeKind.SpreadEvenly => "spread evenly at " + Km(Target),
            OrbitChangeKind.Launch => DescribeLaunch(LaunchTarget!),
            OrbitChangeKind.Transfer => "transfer to " + Destination + ", orbit at " + Km(Target) + (ArrivalInclination is double i ? ", inclined " + Briefing.F(Angles.ToDegrees(i), "0.#") + " deg" : string.Empty),
            OrbitChangeKind.Rendezvous => "rendezvous with " + Rendezvous!.Name,
            OrbitChangeKind.Dock => "dock with " + Rendezvous!.Name,
            OrbitChangeKind.Land => Landing!.Describe(),
            OrbitChangeKind.DeployConstellation => "deploy constellation at " + Km(Target) + (Ring!.IsEquatorial ? string.Empty : ", " + Ring.Describe()),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null),
        };

        private static string DescribeLaunch(LaunchTarget t)
        {
            string text = Math.Abs(t.Apoapsis - t.Periapsis) < 1 ? "launch to " + Km(t.Apoapsis) + " circular" : "launch to " + Km(t.Apoapsis) + " x " + Km(t.Periapsis);
            if (t.Inclination is double inc)
            {
                text += ", inclination " + Deg(inc) + " deg";
            }
            if (t.Lan is double lan)
            {
                text += ", LAN " + Deg(lan) + " deg";
            }
            return text;
        }

        private static string Deg(double radians) => Angles.ToDegrees(radians).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        private static string Km(double metres) => (metres / 1000).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " km";
    }

    /// <summary>An instantaneous burn at a node. Components in m/s along prograde, orbit normal and radial out at the node.</summary>
    public sealed class ImpulsiveBurn
    {
        public ImpulsiveBurn(string label, double nodeUt, double prograde, double normal, double radial, OrbitElements? orbitAfter = null, string? body = null)
        {
            Body = body;
            OrbitAfter = orbitAfter;
            Label = label;
            NodeUt = nodeUt;
            Prograde = prograde;
            Normal = normal;
            Radial = radial;
        }

        public string Label { get; }

        public double NodeUt { get; }

        public double Prograde { get; }

        public double Normal { get; }

        public double Radial { get; }

        public OrbitElements? OrbitAfter { get; }

        /// <summary>The body <see cref="OrbitAfter"/> goes round; null for the body the vessel orbits now.</summary>
        public string? Body { get; }

        public double DeltaV => Math.Sqrt(Prograde * Prograde + Normal * Normal + Radial * Radial);

        /// <summary>The same burn whole orbits later: <paramref name="seconds"/> is a multiple of the period of the orbit it starts from.</summary>
        public ImpulsiveBurn Later(double seconds) => new ImpulsiveBurn(Label, NodeUt + seconds, Prograde, Normal, Radial, OrbitAfter?.Later(seconds), Body);
    }

    public sealed class OrbitPlan
    {
        private OrbitPlan(IReadOnlyList<ImpulsiveBurn> burns, OrbitElements? final, string? error)
        {
            Burns = burns;
            Final = final;
            Error = error;
        }

        public IReadOnlyList<ImpulsiveBurn> Burns { get; }

        public OrbitElements? Final { get; }

        public string? Error { get; }

        public bool Ok => Error == null;

        public double TotalDeltaV
        {
            get
            {
                double sum = 0;
                foreach (ImpulsiveBurn burn in Burns)
                {
                    sum += burn.DeltaV;
                }
                return sum;
            }
        }

        public static OrbitPlan Success(IReadOnlyList<ImpulsiveBurn> burns, OrbitElements final) => new OrbitPlan(burns, final, null);

        public static OrbitPlan Failure(string error) => new OrbitPlan(Array.Empty<ImpulsiveBurn>(), null, error);
    }

    public static class OrbitPlanner
    {
        internal const double NegligibleDeltaV = 0.01;
        private const double FlatInclination = 0.1 * Math.PI / 180;
        private const double PlaneTolerance = 0.02 * Math.PI / 180;
        private const double ApsisTolerance = 200;

        /// <summary>Plans <paramref name="change"/> from <paramref name="orbit"/>. No burn node falls at or before <paramref name="earliestUt"/>.</summary>
        public static OrbitPlan Plan(OrbitChange change, OrbitElements orbit, CentralBody body, double earliestUt)
        {
            var burns = new List<ImpulsiveBurn>();
            string? error = change.Kind switch
            {
                OrbitChangeKind.Circularize => ApsisBurn(burns, ref orbit, earliestUt, atApoapsis: true, orbit.ApoapsisRadius, "circularize"),
                OrbitChangeKind.ChangeApoapsis => ChangeApoapsis(burns, ref orbit, body, earliestUt, body.Radius + change.Target),
                OrbitChangeKind.ChangePeriapsis => ChangePeriapsis(burns, ref orbit, body, earliestUt, body.Radius + change.Target),
                OrbitChangeKind.ChangeInclination => ChangeInclination(burns, ref orbit, earliestUt, change.Target),
                OrbitChangeKind.CircularOrbit => CircularOrbit(burns, ref orbit, body, earliestUt, body.Radius + change.Target),
                OrbitChangeKind.StandardLowOrbit => StandardLowOrbit(burns, ref orbit, body, earliestUt),
                OrbitChangeKind.RaiseApoapsis => ChangeApoapsis(burns, ref orbit, body, earliestUt, orbit.ApoapsisRadius + change.Target),
                OrbitChangeKind.SpreadEvenly => CircularOrbit(burns, ref orbit, body, earliestUt, body.Radius + change.Target),
                OrbitChangeKind.Launch => FinishLaunch(burns, ref orbit, body, earliestUt, change.LaunchTarget!),
                OrbitChangeKind.Transfer => "a transfer is planned with the whole solar system; brief it as a transfer",
                OrbitChangeKind.Rendezvous or OrbitChangeKind.Dock => "a rendezvous is planned against its target, not alone",
                OrbitChangeKind.Land => "a landing is planned by the landing planner",
                OrbitChangeKind.DeployConstellation => "a deployment is planned by the constellation planner",
                _ => throw new ArgumentOutOfRangeException(nameof(change), change.Kind, null),
            };
            return error == null ? OrbitPlan.Success(burns, orbit) : OrbitPlan.Failure(error);
        }

        private static string? ChangeApoapsis(List<ImpulsiveBurn> burns, ref OrbitElements orbit, CentralBody body, double earliestUt, double radius)
        {
            if (radius < orbit.PeriapsisRadius)
            {
                return "apoapsis cannot go below the periapsis";
            }
            if (radius >= body.SphereOfInfluence)
            {
                return "apoapsis would leave " + body.Name + "'s sphere of influence";
            }
            return ApsisBurn(burns, ref orbit, earliestUt, atApoapsis: false, radius, "apoapsis to " + Altitude(body, radius));
        }

        private static string? ChangePeriapsis(List<ImpulsiveBurn> burns, ref OrbitElements orbit, CentralBody body, double earliestUt, double radius)
        {
            if (radius > orbit.ApoapsisRadius)
            {
                return "periapsis cannot go above the apoapsis";
            }
            if (radius <= 0)
            {
                return "periapsis must stay above the body's centre";
            }
            return ApsisBurn(burns, ref orbit, earliestUt, atApoapsis: true, radius, "periapsis to " + Altitude(body, radius));
        }

        private static string? CircularOrbit(List<ImpulsiveBurn> burns, ref OrbitElements orbit, CentralBody body, double earliestUt, double radius)
        {
            if (radius <= body.Radius)
            {
                return "target altitude is below the surface";
            }
            if (radius >= body.SphereOfInfluence)
            {
                return "target altitude is outside " + body.Name + "'s sphere of influence";
            }
            bool raise = radius >= orbit.PeriapsisRadius;
            string label = Altitude(body, radius);
            string? error = ApsisBurn(burns, ref orbit, earliestUt, atApoapsis: !raise, radius, (raise ? "apoapsis to " : "periapsis to ") + label);
            if (error != null)
            {
                return error;
            }
            double after = burns.Count > 0 ? burns[burns.Count - 1].NodeUt : earliestUt;
            bool targetIsApoapsis = Math.Abs(orbit.ApoapsisRadius - radius) <= Math.Abs(orbit.PeriapsisRadius - radius);
            double opposite = targetIsApoapsis ? orbit.ApoapsisRadius : orbit.PeriapsisRadius;
            return ApsisBurn(burns, ref orbit, after, atApoapsis: targetIsApoapsis, opposite, "circularize at " + label);
        }

        private static string? StandardLowOrbit(List<ImpulsiveBurn> burns, ref OrbitElements orbit, CentralBody body, double earliestUt)
        {
            if (orbit.Inclination > FlatInclination)
            {
                string? error = ChangeInclination(burns, ref orbit, earliestUt, 0);
                if (error != null)
                {
                    return error;
                }
            }
            double after = burns.Count > 0 ? burns[burns.Count - 1].NodeUt : earliestUt;
            return CircularOrbit(burns, ref orbit, body, after, body.Radius + StandardOrbit.LowOrbitAltitude(body));
        }

        /// <summary>
        /// The burns after an ascent: raise the periapsis at the apoapsis, set the apoapsis at the periapsis if it is
        /// off, then swing into the target plane at the next crossing if the ascent left it.
        /// </summary>
        private static string? FinishLaunch(List<ImpulsiveBurn> burns, ref OrbitElements orbit, CentralBody body, double earliestUt, LaunchTarget target)
        {
            double ra = body.Radius + target.Apoapsis;
            double rp = body.Radius + target.Periapsis;
            if (rp <= body.Radius + body.SafeFloor)
            {
                return "the periapsis must clear the atmosphere and the terrain";
            }
            if (ra >= body.SphereOfInfluence)
            {
                return "apoapsis would leave " + body.Name + "'s sphere of influence";
            }
            if (Math.Abs(orbit.PeriapsisRadius - rp) > ApsisTolerance || Math.Abs(orbit.ApoapsisRadius - ra) > ApsisTolerance)
            {
                string? error = orbit.ApoapsisRadius < rp - ApsisTolerance
                    ? ApsisBurn(burns, ref orbit, earliestUt, atApoapsis: true, ra, "apoapsis to " + Altitude(body, ra))
                    : null;
                error ??= ApsisBurn(burns, ref orbit, LastNode(burns, earliestUt), atApoapsis: true, rp, "periapsis to " + Altitude(body, rp));
                if (error != null)
                {
                    return error;
                }
                if (Math.Abs(orbit.ApoapsisRadius - ra) > ApsisTolerance)
                {
                    error = ApsisBurn(burns, ref orbit, LastNode(burns, earliestUt), atApoapsis: false, ra, "apoapsis to " + Altitude(body, ra));
                    if (error != null)
                    {
                        return error;
                    }
                }
            }
            if (target.Inclination is double inc)
            {
                double after = LastNode(burns, earliestUt);
                return target.Lan is double lan
                    ? MatchPlane(burns, ref orbit, after, LaunchPlane.PlaneNormal(inc, lan))
                    : Math.Abs(orbit.Inclination - inc) > PlaneTolerance ? ChangeInclination(burns, ref orbit, after, inc) : null;
            }
            return null;
        }

        private static double LastNode(List<ImpulsiveBurn> burns, double earliestUt) => burns.Count > 0 ? burns[burns.Count - 1].NodeUt : earliestUt;

        /// <summary>
        /// Where the orbit crosses the plane with <paramref name="normal"/>, turns the velocity into it: at the next crossing,
        /// or at the crossing that costs less. A reversed plane turns round at the apoapsis.
        /// </summary>
        internal static string? MatchPlane(List<ImpulsiveBurn> burns, ref OrbitElements orbit, double earliestUt, Vector3d normal, bool cheaperNode = false, string label = "match the target plane")
        {
            StateVector now = orbit.StateAt(earliestUt);
            Vector3d h = Vector3d.Cross(now.Position, now.Velocity).Normalized;
            Vector3d line = Vector3d.Cross(h, normal);
            if (line.Magnitude < Math.Sin(PlaneTolerance))
            {
                if (Vector3d.Dot(h, normal) > 0)
                {
                    return null;
                }
                line = orbit.StateAtTrueAnomaly(Math.PI).Position;
            }
            (double Ut, StateVector State, Vector3d Velocity)? best = null;
            foreach (Vector3d direction in new[] { line, line * -1 })
            {
                double nu = Angles.Wrap(Vector3d.AngleAbout(orbit.StateAtTrueAnomaly(0).Position, direction, h));
                double ut = orbit.NextTimeAtTrueAnomaly(nu, earliestUt);
                StateVector state = orbit.StateAt(ut);
                Vector3d up = state.Position.Normalized;
                double radial = Vector3d.Dot(state.Velocity, up);
                double horizontal = (state.Velocity - up * radial).Magnitude;
                Vector3d velocity = up * radial + Vector3d.Cross(normal, up).Normalized * horizontal;
                bool better = best is not { } b
                    || (cheaperNode && Math.Abs((velocity - state.Velocity).Magnitude - (b.Velocity - b.State.Velocity).Magnitude) > NegligibleDeltaV
                        ? (velocity - state.Velocity).Magnitude < (b.Velocity - b.State.Velocity).Magnitude
                        : ut < b.Ut);
                if (better)
                {
                    best = (ut, state, velocity);
                }
            }
            return Apply(burns, ref orbit, best!.Value.Ut, best.Value.State, best.Value.Velocity, label);
        }

        /// <summary>Burns prograde or retrograde at the next apoapsis or periapsis so the opposite apsis lands at <paramref name="oppositeRadius"/>.</summary>
        internal static string? ApsisBurn(List<ImpulsiveBurn> burns, ref OrbitElements orbit, double afterUt, bool atApoapsis, double oppositeRadius, string label)
        {
            double ut = orbit.NextTimeAtTrueAnomaly(atApoapsis ? Math.PI : 0, afterUt);
            StateVector state = orbit.StateAt(ut);
            double r = state.Position.Magnitude;
            double speed = Math.Sqrt(orbit.Mu * (2 / r - 2 / (r + oppositeRadius)));
            Vector3d newVelocity = state.Prograde * speed;
            return Apply(burns, ref orbit, ut, state, newVelocity, label);
        }

        private static string? ChangeInclination(List<ImpulsiveBurn> burns, ref OrbitElements orbit, double earliestUt, double target)
        {
            if (target < 0 || target > Math.PI)
            {
                return "inclination must be between 0 and 180 degrees";
            }
            double ascending = orbit.NextTimeAtTrueAnomaly(orbit.AscendingNodeTrueAnomaly, earliestUt);
            double descending = orbit.NextTimeAtTrueAnomaly(Angles.Wrap(orbit.AscendingNodeTrueAnomaly + Math.PI), earliestUt);
            double ut = Math.Min(ascending, descending);
            StateVector state = orbit.StateAt(ut);
            Vector3d axis = state.Position.Normalized;
            double change = target - orbit.Inclination;
            Vector3d plus = Vector3d.Rotate(state.Velocity, axis, change);
            Vector3d minus = Vector3d.Rotate(state.Velocity, axis, -change);
            double plusError = Math.Abs(InclinationOf(state.Position, plus) - target);
            double minusError = Math.Abs(InclinationOf(state.Position, minus) - target);
            Vector3d newVelocity = plusError <= minusError ? plus : minus;
            return Apply(burns, ref orbit, ut, state, newVelocity, "inclination to " + Angles.ToDegrees(target).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " deg");
        }

        /// <summary>Adds the burn that changes the velocity at <paramref name="ut"/> to <paramref name="newVelocity"/>, and moves <paramref name="orbit"/> onto the result. Returns an error, or null.</summary>
        public static string? Apply(List<ImpulsiveBurn> burns, ref OrbitElements orbit, double ut, StateVector state, Vector3d newVelocity, string label)
        {
            Vector3d dv = newVelocity - state.Velocity;
            if (dv.Magnitude < NegligibleDeltaV)
            {
                return null;
            }
            if (Vector3d.Dot(newVelocity, newVelocity) / 2 - orbit.Mu / state.Position.Magnitude >= 0)
            {
                return "the burn would escape the body";
            }
            orbit = OrbitElements.FromState(orbit.Mu, new StateVector(state.Position, newVelocity), ut);
            burns.Add(new ImpulsiveBurn(label, ut, Vector3d.Dot(dv, state.Prograde), Vector3d.Dot(dv, state.Normal), Vector3d.Dot(dv, state.RadialOut), orbit));
            return null;
        }

        private static double InclinationOf(Vector3d position, Vector3d velocity) =>
            Math.Acos(Math.Max(-1, Math.Min(1, Vector3d.Cross(position, velocity).Normalized.Z)));

        private static string Altitude(CentralBody body, double radius) =>
            ((radius - body.Radius) / 1000).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " km";
    }
}

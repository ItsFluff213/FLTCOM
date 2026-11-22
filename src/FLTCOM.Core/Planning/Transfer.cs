using System;
using System.Collections.Generic;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    /// <summary>The burns of a transfer and what the planner noticed on the way.</summary>
    public sealed class TransferPlan
    {
        public TransferPlan(OrbitPlan plan, IReadOnlyList<string> notes, PorkchopGrid? porkchop = null)
        {
            Plan = plan;
            Notes = notes;
            Porkchop = porkchop;
        }

        public OrbitPlan Plan { get; }

        public IReadOnlyList<string> Notes { get; }

        /// <summary>The window search between two planets; null for a moon.</summary>
        public PorkchopGrid? Porkchop { get; }

        public static TransferPlan Failure(string error) => new TransferPlan(OrbitPlan.Failure(error), Array.Empty<string>());
    }

    /// <summary>
    /// Plans a flight to a circular orbit around another body from wherever the vessel is: in a parking orbit (ejection,
    /// mid-course correction if the aim is off, capture), already on the way (correction, capture) or inside the target's
    /// sphere of influence (capture). Re-planning after each burn is the same call from the new state.
    /// </summary>
    public static class TransferPlanner
    {
        public const string CorrectionLabel = "course correction";

        /// <summary>No correction is planned closer than this to the periapsis it aims.</summary>
        private const double CorrectionLead = 600;

        public static TransferPlan Plan(SolarSystem system, Patch vessel, string target, double altitude, double earliestUt, double? inclination = null)
        {
            if (!system.Contains(target))
            {
                return TransferPlan.Failure("unknown body " + target);
            }
            SystemBody goal = system[target];
            CentralBody body = goal.Body;
            double floor = body.SafeFloor;
            double rp = body.Radius + altitude;
            if (altitude <= floor)
            {
                return TransferPlan.Failure("the orbit must be above " + Briefing.F(floor / 1000, "0.#") + " km to stay up at " + target);
            }
            if (rp >= body.SphereOfInfluence)
            {
                return TransferPlan.Failure("that orbit would leave " + target + "'s sphere of influence");
            }
            var burns = new List<ImpulsiveBurn>();
            var notes = new List<string>();
            Patch start = PatchedConics.Coast(system, vessel, earliestUt);
            double horizon = Horizon(system, start, goal);
            Encounter? encounter = PatchedConics.Find(system, start, target, horizon);
            bool ejected = false;
            PorkchopGrid? porkchop = null;
            if (encounter == null)
            {
                string? home = system[start.Body].Parent;
                string? error;
                if (home == null)
                {
                    // Already on the way between planets: the course correction planned with the ejection stays as it was.
                    return TransferPlan.Failure("no pass through " + target + "'s sphere of influence on this path around " + start.Body);
                }
                if (goal.Parent == start.Body)
                {
                    error = Eject(system, start, goal, rp, burns, out encounter);
                }
                else if (home != null && goal.Parent == home && system[home].Parent == null)
                {
                    error = EjectToPlanet(system, start, goal, rp, inclination, burns, notes, out encounter, out porkchop);
                }
                else
                {
                    return TransferPlan.Failure("from " + start.Body + " only its own moons and the other planets of " + (home ?? start.Body) + " can be reached");
                }
                if (error != null)
                {
                    return TransferPlan.Failure(error);
                }
                ejected = true;
            }
            Encounter arrival = encounter!.Value;
            horizon = Math.Max(horizon, arrival.Periapsis.Ut + 1);
            if (Off(arrival, rp, altitude, inclination))
            {
                Patch from = Fly(system, start, burns);
                double correctionUt = ejected ? (from.Ut + arrival.Entry.Ut) / 2 : earliestUt;
                if (arrival.Periapsis.Ut - correctionUt > CorrectionLead)
                {
                    Patch at = PatchedConics.Coast(system, from, correctionUt);
                    double reach = body.SphereOfInfluence / Math.Max(1, arrival.Periapsis.Ut - correctionUt);
                    Vector3d? dv = Targeting.Correct(system, at, target, rp, horizon, inclination == null ? 0.25 * Math.Max(200, reach) : Math.Max(50, reach), inclination);
                    if (dv is Vector3d c)
                    {
                        burns.Add(new ImpulsiveBurn(CorrectionLabel, at.Ut, c.X, c.Y, c.Z, body: at.Body));
                        arrival = Targeting.After(system, at, c.X, c.Y, c.Z, target, horizon)!.Value;
                        if (inclination is double want && Math.Abs(arrival.Inclination - want) > InclinationTolerance)
                        {
                            notes.Add("the pass can only reach " + Briefing.F(Angles.ToDegrees(arrival.Inclination), "0.#") + " deg inclination at " + target);
                        }
                    }
                    else
                    {
                        notes.Add("no correction found; the pass misses the planned periapsis");
                    }
                }
            }
            if (arrival.PeriapsisRadius <= body.Radius + floor)
            {
                return TransferPlan.Failure("the pass hits " + target + "; a correction is needed earlier");
            }
            OrbitElements captured = Capture(body, arrival, burns);
            if (Math.Abs(captured.SemiMajorAxis - rp) > Tolerance(altitude))
            {
                OrbitPlan trim = OrbitPlanner.Plan(OrbitChange.CircularAt(altitude), captured, body, arrival.Periapsis.Ut + 1);
                if (!trim.Ok)
                {
                    return TransferPlan.Failure(trim.Error!);
                }
                foreach (ImpulsiveBurn b in trim.Burns)
                {
                    burns.Add(new ImpulsiveBurn(b.Label, b.NodeUt, b.Prograde, b.Normal, b.Radial, b.OrbitAfter, target));
                }
                captured = trim.Final!;
            }
            return new TransferPlan(OrbitPlan.Success(burns, captured), notes, porkchop);
        }

        /// <summary>The Hohmann lead angle: how far ahead of the vessel the moon is at departure, in radians.</summary>
        public static double PhaseAngle(double mu, double r1, double r2)
        {
            double tof = Math.PI * Math.Sqrt(Math.Pow((r1 + r2) / 2, 3) / mu);
            return Math.PI - Math.Sqrt(mu / (r2 * r2 * r2)) * tof;
        }

        /// <summary>The state of the vessel just after <paramref name="burn"/>, which starts from <paramref name="before"/>'s coast.</summary>
        public static Patch AfterBurn(SolarSystem system, Patch before, ImpulsiveBurn burn)
        {
            Patch at = PatchedConics.Coast(system, before, burn.NodeUt);
            return new Patch(at.Body, Targeting.Apply(at.State, burn.Prograde, burn.Normal, burn.Radial), at.Ut);
        }

        private static double Tolerance(double altitude) => Math.Max(5_000, 0.1 * altitude);

        private static readonly double InclinationTolerance = Angles.ToRadians(0.5);

        private static bool Off(Encounter arrival, double rp, double altitude, double? inclination) =>
            Math.Abs(arrival.PeriapsisRadius - rp) > Tolerance(altitude)
            || (inclination is double i ? Math.Abs(arrival.Inclination - i) > InclinationTolerance : !arrival.Prograde);

        private static double Horizon(SolarSystem system, Patch start, SystemBody goal)
        {
            double mu = system[start.Body].Body.GravParameter;
            double a = Kepler.SemiMajorAxis(mu, start.State);
            double own = a > 0 ? 2 * Math.PI * Math.Sqrt(a * a * a / mu) : double.PositiveInfinity;
            double target = goal.Orbit?.Period ?? double.PositiveInfinity;
            double span = Math.Min(own, target);
            return start.Ut + (double.IsInfinity(span) ? 1e8 : span);
        }

        /// <summary>
        /// A prograde ejection to a moon of the body the vessel orbits: the Hohmann lead angle picks the departure (the first one
        /// with the moon near the vessel's plane on arrival), then the departure time is tuned until the pass goes round at <paramref name="rp"/>.
        /// </summary>
        private static string? Eject(SolarSystem system, Patch start, SystemBody moon, double rp, List<ImpulsiveBurn> burns, out Encounter? encounter)
        {
            encounter = null;
            double mu = system[start.Body].Body.GravParameter;
            double a = Kepler.SemiMajorAxis(mu, start.State);
            if (a <= 0 || Kepler.EccentricityVector(mu, start.State).Magnitude >= 1)
            {
                return "the vessel must be in orbit around " + start.Body + " to leave for " + moon.Name;
            }
            OrbitElements parking = OrbitElements.FromState(mu, start.State, start.Ut);
            OrbitElements target = moon.Orbit!;
            if (parking.ApoapsisRadius >= target.PeriapsisRadius - moon.Body.SphereOfInfluence)
            {
                return "the parking orbit already reaches " + moon.Name + "'s orbit; lower it first";
            }
            Vector3d normal = Vector3d.Cross(start.State.Position, start.State.Velocity).Normalized;
            double tof = Math.PI * Math.Sqrt(Math.Pow((parking.SemiMajorAxis + target.SemiMajorAxis) / 2, 3) / mu);
            double synodic = Angles.TwoPi / Math.Abs(parking.MeanMotion - target.MeanMotion);
            double tilt = Math.Acos(Math.Max(-1, Math.Min(1, Vector3d.Dot(normal, Vector3d.Cross(target.StateAt(0).Position, target.StateAt(0).Velocity).Normalized))));
            double window = 2 * synodic + (tilt > Angles.ToRadians(0.5) ? target.Period / 2 : 0);
            double spread = parking.Period / 8;
            double from = start.Ut + spread;

            double Lead(double t)
            {
                Vector3d away = parking.StateAt(t).Position * -1;
                Vector3d m = target.StateAt(t + tof).Position;
                m -= normal * Vector3d.Dot(m, normal);
                return Math.Atan2(Vector3d.Dot(normal, Vector3d.Cross(away, m)), Vector3d.Dot(away, m));
            }

            double? best = null;
            double bestOff = double.PositiveInfinity;
            double step = Math.Min(parking.Period, synodic) / 24;
            double prev = Lead(from);
            for (double t = from + step; t < from + window; t += step)
            {
                double g = Lead(t);
                if (prev > 0 && g <= 0 && prev - g < Math.PI)
                {
                    double lo = t - step, hi = t;
                    for (int i = 0; i < 40; i++)
                    {
                        double mid = (lo + hi) / 2;
                        if (Lead(mid) > 0)
                        {
                            lo = mid;
                        }
                        else
                        {
                            hi = mid;
                        }
                    }
                    double off = Math.Abs(Vector3d.Dot(target.StateAt(hi + tof).Position, normal));
                    if (off < 0.25 * moon.Body.SphereOfInfluence)
                    {
                        best = hi;
                        break;
                    }
                    if (off < bestOff)
                    {
                        bestOff = off;
                        best = hi;
                    }
                }
                prev = g;
            }
            if (best == null)
            {
                return "no departure to " + moon.Name + " found";
            }

            double horizon = best.Value + 3 * tof;
            foreach (double reach in new[] { 1.0, 0.98, 1.03 })
            {
                double Prograde(double t)
                {
                    StateVector s = parking.StateAt(t);
                    double r = s.Position.Magnitude;
                    double apo = reach * target.SemiMajorAxis;
                    return Math.Sqrt(mu * (2 / r - 2 / (r + apo))) - s.Velocity.Magnitude;
                }
                double? Aim(double t)
                {
                    Encounter? e = Targeting.After(system, new Patch(start.Body, parking.StateAt(t), t), Prograde(t), 0, 0, moon.Name, horizon);
                    BPlane? b = e == null ? null : BPlane.Of(moon.Body.GravParameter, e.Value.Entry.State);
                    return b == null ? null : b.Value.T - BPlane.ImpactParameter(moon.Body.GravParameter, rp, b.Value.ExcessSpeedSquared);
                }

                const int Samples = 48;
                double dt = 2 * spread / Samples;
                double? chosen = null;
                double fallback = double.NaN;
                double closest = double.PositiveInfinity;
                double? last = null;
                for (int i = 0; i <= Samples && chosen == null; i++)
                {
                    double t = best.Value - spread + dt * i;
                    double? f = Aim(t);
                    if (f is double v)
                    {
                        if (Math.Abs(v) < closest)
                        {
                            closest = Math.Abs(v);
                            fallback = t;
                        }
                        if (last is double before && Math.Sign(v) != Math.Sign(before))
                        {
                            chosen = Refine(Aim, t - dt, before, t, v);
                        }
                    }
                    last = f;
                }
                double ut = chosen ?? fallback;
                if (double.IsNaN(ut))
                {
                    continue;
                }
                StateVector at = parking.StateAt(ut);
                double dv = Prograde(ut);
                var after = new StateVector(at.Position, at.Velocity + at.Prograde * dv);
                encounter = Targeting.After(system, new Patch(start.Body, at, ut), dv, 0, 0, moon.Name, horizon);
                if (encounter == null)
                {
                    continue;
                }
                burns.Add(new ImpulsiveBurn("leave for " + moon.Name, ut, dv, 0, 0, OrbitElements.FromState(mu, after, ut), start.Body));
                return null;
            }
            return "no path to " + moon.Name + " found from this orbit";
        }

        /// <summary>The vessel after flying <paramref name="burns"/> in order from <paramref name="start"/>.</summary>
        private static Patch Fly(SolarSystem system, Patch start, IEnumerable<ImpulsiveBurn> burns)
        {
            Patch p = start;
            foreach (ImpulsiveBurn b in burns)
            {
                p = AfterBurn(system, p, b);
            }
            return p;
        }

        /// <summary>
        /// An ejection to another planet of the same star. The porkchop search picks the window; the burn is the one whose escape has its
        /// periapsis in the parking plane and leaves along the window's excess velocity, re-aimed until the patched-conic path out of the
        /// sphere of influence follows the window's Lambert arc. If the planet's sphere is still missed (its orbit is inclined), a correction
        /// half way retargets the arc onto the planet and aims the pass at <paramref name="rp"/> in the same burn.
        /// </summary>
        private static string? EjectToPlanet(SolarSystem system, Patch start, SystemBody planet, double rp, double? inclination, List<ImpulsiveBurn> burns, List<string> notes, out Encounter? encounter, out PorkchopGrid? porkchop)
        {
            encounter = null;
            porkchop = null;
            SystemBody home = system[start.Body];
            string star = home.Parent!;
            double mu = home.Body.GravParameter;
            double a = Kepler.SemiMajorAxis(mu, start.State);
            if (a <= 0 || Kepler.EccentricityVector(mu, start.State).Magnitude >= 1)
            {
                return "the vessel must be in orbit around " + start.Body + " to leave for " + planet.Name;
            }
            OrbitElements parking = OrbitElements.FromState(mu, start.State, start.Ut);
            if (parking.ApoapsisRadius >= 0.5 * home.Body.SphereOfInfluence)
            {
                return "the parking orbit reaches too far from " + start.Body + "; lower it first";
            }
            Vector3d normal = start.State.Normal;
            try
            {
                porkchop = Porkchop.Search(system, home.Name, planet.Name, start.Ut + parking.Period, parking.SemiMajorAxis, rp, normal);
            }
            catch (InvalidOperationException e)
            {
                return e.Message;
            }
            TransferWindow window = porkchop.Best;
            double arrivalUt = window.ArrivalUt;
            double horizon = arrivalUt + 0.25 * window.FlightTime;
            double starMu = home.Orbit!.Mu;
            Vector3d orbitNormal = home.Orbit.StateAt(start.Ut).Normal;
            Vector3d aim = window.Aim;

            Vector3d excess = window.DepartureExcess;
            double ut = window.DepartureUt;
            Patch? exit = null;
            ImpulsiveBurn? eject = null;
            for (int pass = 0; pass < 8; pass++)
            {
                (double Ut, Vector3d DeltaV)? departure = Departure(parking, mu, excess, ut, start.Ut);
                if (departure == null)
                {
                    return "no escape toward " + planet.Name + " leaves from this orbit's plane";
                }
                ut = departure.Value.Ut;
                StateVector at = parking.StateAt(ut);
                Vector3d dv = departure.Value.DeltaV;
                var gone = new Patch(start.Body, new StateVector(at.Position, at.Velocity + dv), ut);
                exit = Leave(system, gone, star, horizon);
                if (exit == null)
                {
                    return "the escape does not leave " + start.Body + "'s sphere of influence";
                }
                (Vector3d V1, Vector3d V2)? need = Porkchop.Lambert(starMu, exit.Value.State.Position, aim, arrivalUt - exit.Value.Ut, orbitNormal);
                if (need == null)
                {
                    return "no path to " + planet.Name + " found from " + start.Body + "'s sphere of influence";
                }
                double after = Kepler.SemiMajorAxis(starMu, exit.Value.State);
                eject = new ImpulsiveBurn("leave for " + planet.Name, ut, Vector3d.Dot(dv, at.Prograde), Vector3d.Dot(dv, at.Normal), Vector3d.Dot(dv, at.RadialOut),
                    after > 0 ? OrbitElements.FromState(starMu, exit.Value.State, exit.Value.Ut) : null, star);
                Vector3d miss = need.Value.V1 - exit.Value.State.Velocity;
                if (miss.Magnitude < 0.05)
                {
                    break;
                }
                excess += miss;
            }
            burns.Add(eject!);
            notes.Add("the " + planet.Name + " window: leave at " + KerbinTime.Format(eject!.NodeUt) + ", " + KerbinTime.Span(eject.NodeUt - start.Ut) + " from now; "
                + KerbinTime.Span(window.FlightTime) + " on the way");

            encounter = PatchedConics.Find(system, exit!.Value, planet.Name, horizon);
            if (encounter != null)
            {
                return null;
            }
            double midUt = (exit.Value.Ut + arrivalUt) / 2;
            Patch mid = PatchedConics.Coast(system, exit.Value, midUt);
            if (mid.Body != star)
            {
                return "the path to " + planet.Name + " passes another body; no correction planned";
            }
            (Vector3d V1, Vector3d V2)? onto = Porkchop.Lambert(starMu, mid.State.Position, planet.Orbit!.StateAt(arrivalUt).Position, arrivalUt - midUt, orbitNormal);
            if (onto == null)
            {
                return "no correction onto " + planet.Name + " found";
            }
            Vector3d turn = onto.Value.V1 - mid.State.Velocity;
            var lifted = new Patch(star, new StateVector(mid.State.Position, onto.Value.V1), midUt);
            double reach = planet.Body.SphereOfInfluence / Math.Max(1, arrivalUt - midUt);
            if (Targeting.Correct(system, lifted, planet.Name, rp, horizon, Math.Max(50, reach), inclination) is Vector3d fine)
            {
                turn += lifted.State.Prograde * fine.X + lifted.State.Normal * fine.Y + lifted.State.RadialOut * fine.Z;
            }
            double p = Vector3d.Dot(turn, mid.State.Prograde);
            double n = Vector3d.Dot(turn, mid.State.Normal);
            double r = Vector3d.Dot(turn, mid.State.RadialOut);
            StateVector turned = Targeting.Apply(mid.State, p, n, r);
            burns.Add(new ImpulsiveBurn(CorrectionLabel, midUt, p, n, r, OrbitElements.FromState(starMu, turned, midUt), star));
            encounter = Targeting.After(system, mid, p, n, r, planet.Name, horizon);
            return encounter == null ? "no correction brings the path into " + planet.Name + "'s sphere of influence" : null;
        }

        /// <summary>
        /// The burn in a parking orbit that leaves on an escape with excess velocity <paramref name="excess"/>, its periapsis (the burn point)
        /// in the parking plane, at the pass nearest <paramref name="near"/> and not before <paramref name="earliest"/>. Null if the excess
        /// points too far out of the plane for such an escape.
        /// </summary>
        private static (double Ut, Vector3d DeltaV)? Departure(OrbitElements parking, double mu, Vector3d excess, double near, double earliest)
        {
            StateVector s0 = parking.StateAt(near);
            Vector3d n = s0.Normal;
            Vector3d e1 = s0.Position.Normalized;
            Vector3d e2 = Vector3d.Cross(n, e1);
            double vInf = excess.Magnitude;
            Vector3d v = excess / vInf;
            double c1 = Vector3d.Dot(e1, v);
            double c2 = Vector3d.Dot(e2, v);
            double inPlane = Math.Sqrt(c1 * c1 + c2 * c2);
            double t = near;
            double r = s0.Position.Magnitude;
            Vector3d periapsis = e1;
            double asymptote = 0;
            for (int i = 0; i < 4; i++)
            {
                double e = 1 + r * vInf * vInf / mu;
                asymptote = Math.Acos(-1 / e);
                double ratio = Math.Cos(asymptote) / inPlane;
                if (Math.Abs(ratio) > 1)
                {
                    return null;
                }
                double phi = Math.Atan2(c2, c1) - Math.Acos(ratio);
                periapsis = e1 * Math.Cos(phi) + e2 * Math.Sin(phi);
                for (int k = 0; k < 6; k++)
                {
                    double off = Angles.Wrap(Vector3d.AngleAbout(parking.StateAt(t).Position, periapsis, n) + Math.PI) - Math.PI;
                    t += off / parking.MeanMotion;
                }
                while (t < earliest)
                {
                    t += parking.Period;
                }
                r = parking.StateAt(t).Position.Magnitude;
            }
            StateVector at = parking.StateAt(t);
            Vector3d along = (v - periapsis * Math.Cos(asymptote)) / Math.Sin(asymptote);
            double vp = Math.Sqrt(vInf * vInf + 2 * mu / r);
            return (t, along * vp - at.Velocity);
        }

        /// <summary>Coasts out of the current sphere of influence into <paramref name="star"/>'s, or null if that does not happen before <paramref name="horizon"/>.</summary>
        private static Patch? Leave(SolarSystem system, Patch p, string star, double horizon)
        {
            for (int i = 0; i < 6; i++)
            {
                if (p.Body == star)
                {
                    return p;
                }
                p = PatchedConics.Step(system, p, horizon, out bool changed);
                if (!changed)
                {
                    return null;
                }
            }
            return null;
        }

        /// <summary>Secant steps between a bracket where <paramref name="f"/> changes sign.</summary>
        private static double Refine(Func<double, double?> f, double lo, double fLo, double hi, double fHi)
        {
            for (int i = 0; i < 30 && hi - lo > 0.05; i++)
            {
                double mid = (lo + hi) / 2;
                double? v = f(mid);
                if (v == null)
                {
                    break;
                }
                if (Math.Sign(v.Value) == Math.Sign(fLo))
                {
                    lo = mid;
                    fLo = v.Value;
                }
                else
                {
                    hi = mid;
                    fHi = v.Value;
                }
            }
            return Math.Abs(fLo) < Math.Abs(fHi) ? lo : hi;
        }

        /// <summary>Retrograde at periapsis down to circular speed there.</summary>
        private static OrbitElements Capture(CentralBody body, Encounter arrival, List<ImpulsiveBurn> burns)
        {
            StateVector pe = arrival.Periapsis.State;
            double mu = body.GravParameter;
            double r = pe.Position.Magnitude;
            double circular = Math.Sqrt(mu / r);
            double dv = pe.Velocity.Magnitude - circular;
            var after = new StateVector(pe.Position, pe.Prograde * circular);
            OrbitElements orbit = OrbitElements.FromState(mu, after, arrival.Periapsis.Ut);
            if (Math.Abs(dv) > 0.05)
            {
                burns.Add(new ImpulsiveBurn("capture at " + body.Name, arrival.Periapsis.Ut, -dv, 0, 0, orbit, body.Name));
            }
            return orbit;
        }
    }
}

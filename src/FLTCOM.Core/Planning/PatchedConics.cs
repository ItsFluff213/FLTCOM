using System;
using System.Linq;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    /// <summary>A vessel's state relative to <see cref="Body"/> at <see cref="Ut"/>.</summary>
    public readonly struct Patch
    {
        public Patch(string body, StateVector state, double ut)
        {
            Body = body;
            State = state;
            Ut = ut;
        }

        public string Body { get; }

        public StateVector State { get; }

        public double Ut { get; }
    }

    /// <summary>Where a coast first comes nearest the centre of a body it enters.</summary>
    public readonly struct Encounter
    {
        public Encounter(Patch entry, Patch periapsis)
        {
            Entry = entry;
            Periapsis = periapsis;
        }

        /// <summary>The state on crossing into the body's sphere of influence.</summary>
        public Patch Entry { get; }

        /// <summary>The state at periapsis inside that sphere.</summary>
        public Patch Periapsis { get; }

        public double PeriapsisRadius => Periapsis.State.Position.Magnitude;

        /// <summary>The pass goes round the body the way the body spins its own children: angular momentum along +z.</summary>
        public bool Prograde => Vector3d.Cross(Periapsis.State.Position, Periapsis.State.Velocity).Z >= 0;

        /// <summary>The inclination of the pass to the body's equator, in radians.</summary>
        public double Inclination
        {
            get
            {
                Vector3d h = Vector3d.Cross(Periapsis.State.Position, Periapsis.State.Velocity);
                return Math.Acos(Math.Max(-1, Math.Min(1, h.Z / h.Magnitude)));
            }
        }
    }

    /// <summary>
    /// KSP's patched conics in the core: a coast is a Kepler conic around one body until it leaves that body's sphere
    /// of influence or enters a child's. Steps are short enough that no sphere is crossed unseen; crossings are found by bisection.
    /// </summary>
    public static class PatchedConics
    {
        private const double CrossingTolerance = 0.5;
        private const double MinStep = 1;

        /// <summary>Coasts until <paramref name="untilUt"/>, following every change of sphere of influence.</summary>
        public static Patch Coast(SolarSystem system, Patch from, double untilUt)
        {
            Patch p = from;
            for (int i = 0; i < 16 && p.Ut < untilUt; i++)
            {
                p = Step(system, p, untilUt, out bool changed);
                if (!changed)
                {
                    break;
                }
            }
            return p;
        }

        /// <summary>
        /// Coasts at most until <paramref name="untilUt"/> or the first change of sphere of influence, whichever comes first.
        /// On a change the patch returned is just inside the new body's sphere.
        /// </summary>
        public static Patch Step(SolarSystem system, Patch from, double untilUt, out bool changed)
        {
            changed = false;
            SystemBody body = system[from.Body];
            double mu = body.Body.GravParameter;
            double soi = body.Body.SphereOfInfluence;
            var children = system.Children(body.Name).ToList();
            double vMax = Kepler.MaxSpeed(mu, from.State);
            double t = from.Ut;
            double prev = from.Ut;
            while (t < untilUt)
            {
                StateVector s = Kepler.Propagate(mu, from.State, t - from.Ut);
                double r = s.Position.Magnitude;
                bool start = t == from.Ut;
                double gap = double.IsInfinity(soi) ? double.PositiveInfinity : Math.Max(0, (soi - r) / vMax);
                if (gap <= 0 && !start)
                {
                    return Cross(system, from, body.Parent!, prev, t, out changed, inside: false);
                }
                foreach (SystemBody child in children)
                {
                    StateVector c = child.Orbit!.StateAt(t);
                    double d = (s.Position - c.Position).Magnitude;
                    double childGap = Math.Max(0, (d - child.Body.SphereOfInfluence) / (vMax + child.Orbit.SpeedAtRadius(child.Orbit.PeriapsisRadius)));
                    if (childGap <= 0 && !start)
                    {
                        return Cross(system, from, child.Name, prev, t, out changed, inside: true);
                    }
                    gap = Math.Min(gap, childGap);
                }
                prev = t;
                t = Math.Min(untilUt, t + Math.Max(MinStep, gap));
            }
            return new Patch(from.Body, Kepler.Propagate(mu, from.State, untilUt - from.Ut), untilUt);
        }

        /// <summary>Coasts until the first sphere of <paramref name="target"/> is entered and returns the periapsis there, or null.</summary>
        public static Encounter? Find(SolarSystem system, Patch from, string target, double horizon)
        {
            Patch p = from;
            for (int i = 0; i < 8 && p.Ut < horizon; i++)
            {
                if (p.Body == target)
                {
                    double mu = system[target].Body.GravParameter;
                    double toPe = Kepler.TimeToPeriapsis(mu, p.State);
                    if (toPe < 0)
                    {
                        toPe = 0;
                    }
                    var pe = new Patch(target, Kepler.Propagate(mu, p.State, toPe), p.Ut + toPe);
                    return new Encounter(p, pe);
                }
                p = Step(system, p, horizon, out bool changed);
                if (!changed)
                {
                    return null;
                }
            }
            return null;
        }

        private static Patch Cross(SolarSystem system, Patch from, string into, double before, double after, out bool changed, bool inside)
        {
            SystemBody body = system[from.Body];
            double mu = body.Body.GravParameter;
            double lo = before;
            double hi = after;
            bool Over(double ut)
            {
                StateVector s = Kepler.Propagate(mu, from.State, ut - from.Ut);
                if (!inside)
                {
                    return s.Position.Magnitude >= body.Body.SphereOfInfluence;
                }
                SystemBody child = system[into];
                return (s.Position - child.Orbit!.StateAt(ut).Position).Magnitude <= child.Body.SphereOfInfluence;
            }
            while (hi - lo > CrossingTolerance)
            {
                double mid = (lo + hi) / 2;
                if (Over(mid))
                {
                    hi = mid;
                }
                else
                {
                    lo = mid;
                }
            }
            changed = true;
            StateVector state = Kepler.Propagate(mu, from.State, hi - from.Ut);
            return new Patch(into, system.Rebase(state, from.Body, into, hi), hi);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    /// <summary>A body with its orbit around its parent; the root star has neither.</summary>
    public sealed class SystemBody
    {
        public SystemBody(CentralBody body, string? parent, OrbitElements? orbit)
        {
            Body = body ?? throw new ArgumentNullException(nameof(body));
            if ((parent == null) != (orbit == null))
            {
                throw new ArgumentException("a body has both a parent and an orbit, or neither", nameof(orbit));
            }
            Parent = parent;
            Orbit = orbit;
        }

        public CentralBody Body { get; }

        public string Name => Body.Name;

        public string? Parent { get; }

        /// <summary>The orbit around <see cref="Parent"/>, with its gravitational parameter.</summary>
        public OrbitElements? Orbit { get; }
    }

    /// <summary>The bodies of a planetary system on rails, as KSP moves them: fixed Keplerian orbits.</summary>
    public sealed class SolarSystem
    {
        private readonly Dictionary<string, SystemBody> _bodies;

        public SolarSystem(IEnumerable<SystemBody> bodies)
        {
            _bodies = bodies.ToDictionary(b => b.Name);
        }

        public IEnumerable<SystemBody> Bodies => _bodies.Values;

        public SystemBody this[string name] => _bodies.TryGetValue(name, out SystemBody body) ? body : throw new ArgumentException("unknown body " + name, nameof(name));

        public bool Contains(string name) => _bodies.ContainsKey(name);

        public IEnumerable<SystemBody> Children(string name) => _bodies.Values.Where(b => b.Parent == name);

        /// <summary>The state of <paramref name="name"/> relative to its parent at <paramref name="ut"/>.</summary>
        public StateVector StateOf(string name, double ut) =>
            this[name].Orbit?.StateAt(ut) ?? new StateVector(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));

        /// <summary>The state of <paramref name="name"/> relative to the root star.</summary>
        public StateVector Absolute(string name, double ut)
        {
            var position = new Vector3d(0, 0, 0);
            var velocity = new Vector3d(0, 0, 0);
            for (SystemBody? b = this[name]; b?.Parent != null; b = this[b.Parent])
            {
                StateVector s = b.Orbit!.StateAt(ut);
                position += s.Position;
                velocity += s.Velocity;
            }
            return new StateVector(position, velocity);
        }

        /// <summary>A state relative to <paramref name="from"/> re-expressed relative to <paramref name="to"/>.</summary>
        public StateVector Rebase(StateVector state, string from, string to, double ut)
        {
            StateVector a = Absolute(from, ut);
            StateVector b = Absolute(to, ut);
            return new StateVector(state.Position + a.Position - b.Position, state.Velocity + a.Velocity - b.Velocity);
        }
    }
}

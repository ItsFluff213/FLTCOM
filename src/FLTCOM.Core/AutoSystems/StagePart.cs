using System;

namespace FLTCOM.Core.AutoSystems
{
    /// <summary>What a part does when its stage fires or when its decoupler lets go. A part can do several things.</summary>
    [Flags]
    public enum PartRole
    {
        None = 0,
        Engine = 1,
        Tank = 2,
        Decoupler = 4,
        Fairing = 8,
        Clamp = 16,
        Parachute = 32,
        Command = 64,
    }

    public enum EngineState
    {
        None,
        Unlit,
        Burning,
        Spent,
    }

    /// <summary>
    /// One part as the stage logic sees it. Stage is the KSP stage that activates it (its inverseStage); DecoupledIn is
    /// the stage whose decoupler drops it, or -1 when it stays with the vessel.
    /// </summary>
    public sealed class StagePart
    {
        public StagePart(string name, PartRole roles, int stage, int decoupledIn, EngineState engine = EngineState.None, bool fuelLeft = true)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Roles = roles;
            Stage = stage;
            DecoupledIn = decoupledIn;
            Engine = (roles & PartRole.Engine) != 0 && engine == EngineState.None ? EngineState.Unlit : engine;
            FuelLeft = fuelLeft;
        }

        public string Name { get; }

        public PartRole Roles { get; }

        public int Stage { get; }

        public int DecoupledIn { get; }

        public EngineState Engine { get; }

        /// <summary>For a tank: some propellant with mass is left in it.</summary>
        public bool FuelLeft { get; }

        public bool Is(PartRole role) => (Roles & role) != 0;
    }
}

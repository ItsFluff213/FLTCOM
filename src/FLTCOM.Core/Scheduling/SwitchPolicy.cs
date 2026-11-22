namespace FLTCOM.Core.Scheduling
{
    public enum GameScene
    {
        Flight,
        TrackingStation,
        SpaceCenter,
        Other,
    }

    public enum ActiveVesselCondition
    {
        StableOrbit,
        Landed,
        Unstable,
    }

    /// <summary>What the game is showing now, as the switch policy needs it.</summary>
    public sealed class ActiveView
    {
        /// <param name="turnLead">Seconds the active vessel needs to turn toward a burn, from <see cref="Execution.IgnitionTiming.TurnLead"/>.</param>
        public ActiveView(GameScene scene, string? activeVesselId, ActiveVesselCondition condition, bool activeVesselBurning, double turnLead = Execution.IgnitionTiming.TurnFloor)
        {
            Scene = scene;
            ActiveVesselId = activeVesselId;
            Condition = condition;
            ActiveVesselBurning = activeVesselBurning;
            TurnLead = turnLead;
        }

        public GameScene Scene { get; }

        public string? ActiveVesselId { get; }

        public ActiveVesselCondition Condition { get; }

        public bool ActiveVesselBurning { get; }

        public double TurnLead { get; }
    }

    public enum SwitchDecision
    {
        /// <summary>The next burn's vessel is already active, or nothing is due.</summary>
        Stay,
        Switch,
        /// <summary>Switching now could leave the current vessel in danger; ask the operator.</summary>
        Ask,
        /// <summary>The active vessel is burning; never switch away mid-burn.</summary>
        Wait,
    }

    public static class SwitchPolicy
    {
        /// <summary>Switches without asking only from the Tracking Station or Space Center, or from a vessel in a stable orbit or landed.</summary>
        public static SwitchDecision Decide(ActiveView view, string nextVesselId)
        {
            if (view.Scene == GameScene.TrackingStation || view.Scene == GameScene.SpaceCenter)
            {
                return SwitchDecision.Switch;
            }
            if (view.ActiveVesselId == nextVesselId)
            {
                return SwitchDecision.Stay;
            }
            if (view.ActiveVesselBurning)
            {
                return SwitchDecision.Wait;
            }
            return view.Scene == GameScene.Flight && view.Condition != ActiveVesselCondition.Unstable
                ? SwitchDecision.Switch
                : SwitchDecision.Ask;
        }
    }
}

namespace FLTCOM.Execution
{
    /// <summary>Makes a burn's vessel the active one: a direct switch when it is loaded, otherwise save and fly it.</summary>
    internal static class VesselSwitcher
    {
        /// <summary>True while FLTCOM itself is changing scenes, so the load that follows is not a player load.</summary>
        public static bool Switching { get; set; }

        public static string? Switch(string vesselId)
        {
            Vessel? vessel = VesselTasks.Find(vesselId);
            if (vessel == null)
            {
                return "vessel " + vesselId + " is gone";
            }
            if (HighLogic.LoadedSceneIsFlight && vessel.loaded)
            {
                FlightGlobals.ForceSetActiveVessel(vessel);
                return null;
            }
            int index = FlightGlobals.Vessels.IndexOf(vessel);
            Switching = true;
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            FlightDriver.StartAndFocusVessel("persistent", index);
            return null;
        }
    }
}

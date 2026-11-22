using System;
using System.Text;
using FLTCOM.Core.Groups;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Telemetry;
using FLTCOM.Execution;

namespace FLTCOM
{
    /// <summary>FLTCOM's per-save state: the agency prefix of the fleet registry codes, the vessel groups and every mission operation.</summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.FLIGHT, GameScenes.TRACKSTATION, GameScenes.SPACECENTER)]
    public sealed class FleetScenario : ScenarioModule
    {
        private const string MissionsKey = "missions";
        private const string GroupsKey = "groups";

        [KSPField(isPersistant = true)]
        public string agency = RegistryCode.DefaultAgency;

        private static FleetScenario? _instance;
        private static Game? _loadedGame;

        public static string Agency => _instance != null && !string.IsNullOrWhiteSpace(_instance.agency) ? _instance.agency : RegistryCode.DefaultAgency;

        public override void OnAwake()
        {
            base.OnAwake();
            _instance = this;
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            string json = MissionJson.Write(new Mission("fleet", "fleet", MissionDesk.Runner.Operations));
            node.SetValue(MissionsKey, Base64(json), true);
            node.SetValue(GroupsKey, Base64(FleetGroups.Write(MissionDesk.Groups.All)), true);
            LandingWaypoints.Save(node);
        }

        /// <summary>A scene change keeps the missions in memory; a game load (quickload, revert, a save from the menu) replaces them and holds every running one.</summary>
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            bool sameGame = ReferenceEquals(HighLogic.CurrentGame, _loadedGame) || VesselSwitcher.Switching;
            _loadedGame = HighLogic.CurrentGame;
            VesselSwitcher.Switching = false;
            if (sameGame)
            {
                return;
            }
            string? saved = node.GetValue(MissionsKey);
            try
            {
                Mission? mission = string.IsNullOrEmpty(saved) ? null : MissionJson.Read(Encoding.UTF8.GetString(Convert.FromBase64String(saved)));
                MissionDesk.Runner.Load(mission?.Operations ?? Array.Empty<Operation>());
            }
            catch (Exception e)
            {
                PluginFiles.Log("saved missions unreadable, starting empty: " + e.Message);
                MissionDesk.Runner.Load(Array.Empty<Operation>());
            }
            string? groups = node.GetValue(GroupsKey);
            try
            {
                MissionDesk.Groups.Load(string.IsNullOrEmpty(groups) ? Array.Empty<FleetGroup>() : FleetGroups.Read(Encoding.UTF8.GetString(Convert.FromBase64String(groups))));
            }
            catch (Exception e)
            {
                PluginFiles.Log("saved groups unreadable, starting empty: " + e.Message);
                MissionDesk.Groups.Load(Array.Empty<FleetGroup>());
            }
            LandingWaypoints.Load(node);
        }

        private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        public void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}

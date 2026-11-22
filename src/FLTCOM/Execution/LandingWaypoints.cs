using System;
using System.Collections.Generic;
using System.Globalization;
using FinePrint;
using FLTCOM.Core.Telemetry;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Execution
{
    /// <summary>
    /// Keeps one stock custom waypoint on the target of each approved landing, so the player sees it on the map and
    /// can steer by it on the navball, and takes it away when the landing ends. The marks FLTCOM placed are saved with
    /// the game; a waypoint is only ever removed when it matches one of them, so the player's own are never touched.
    /// </summary>
    internal static class LandingWaypoints
    {
        private const string NodeName = "LANDING_WAYPOINT";
        private const double SameDegrees = 1e-6;

        private static readonly List<LandingMark> Placed = new List<LandingMark>();

        public static void Sync()
        {
            if (!(HighLogic.LoadedSceneIsFlight || HighLogic.LoadedScene == GameScenes.TRACKSTATION || HighLogic.LoadedScene == GameScenes.SPACECENTER)
                || ScenarioCustomWaypoints.Instance == null || WaypointManager.Instance() == null)
            {
                return;
            }
            List<LandingMark> wanted = LandingWaypointPlan.Wanted(MissionDesk.Runner.Operations, FlightGlobals.GetHomeBodyName(), BodyOf);
            var (add, remove) = LandingWaypointPlan.Diff(wanted, Placed);
            foreach (LandingMark mark in remove)
            {
                Waypoint? waypoint = Find(mark);
                if (waypoint != null)
                {
                    NavWaypoint.DeactivateIfWaypoint(waypoint);
                    ScenarioCustomWaypoints.RemoveWaypoint(waypoint);
                    PluginFiles.Log("landing waypoint removed: " + mark.Name);
                }
                Placed.Remove(mark);
            }
            foreach (LandingMark mark in add)
            {
                if (Find(mark) == null)
                {
                    Place(mark);
                }
                Placed.Add(mark);
            }
        }

        public static void Save(ConfigNode scenario)
        {
            foreach (LandingMark mark in Placed)
            {
                ConfigNode node = scenario.AddNode(NodeName);
                node.AddValue("operation", mark.OperationId);
                node.AddValue("name", mark.Name);
                node.AddValue("body", mark.Body);
                node.AddValue("latitude", mark.Latitude.ToString("R", CultureInfo.InvariantCulture));
                node.AddValue("longitude", mark.Longitude.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        public static void Load(ConfigNode scenario)
        {
            Placed.Clear();
            foreach (ConfigNode node in scenario.GetNodes(NodeName))
            {
                string? operation = node.GetValue("operation");
                string? name = node.GetValue("name");
                string? body = node.GetValue("body");
                if (operation != null && name != null && body != null
                    && double.TryParse(node.GetValue("latitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude)
                    && double.TryParse(node.GetValue("longitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude))
                {
                    Placed.Add(new LandingMark(operation, name, body, latitude, longitude));
                }
            }
        }

        private static string? BodyOf(string vesselId) => VesselTasks.Find(vesselId)?.mainBody?.bodyName;

        private static void Place(LandingMark mark)
        {
            var waypoint = new Waypoint
            {
                name = mark.Name,
                celestialName = mark.Body,
                latitude = Angles.ToDegrees(mark.Latitude),
                longitude = Angles.ToDegrees(mark.Longitude),
                altitude = 0,
                id = "custom",
                seed = StringComparer.Ordinal.GetHashCode(mark.OperationId),
                isOnSurface = true,
                isNavigatable = true,
                isCustom = true,
            };
            ScenarioCustomWaypoints.AddWaypoint(waypoint);
            PluginFiles.Log("landing waypoint placed: " + mark.Name);
            NavWaypoint? nav = NavWaypoint.fetch;
            if (HighLogic.LoadedSceneIsFlight && nav != null && !nav.IsActive)
            {
                nav.Setup(waypoint);
                nav.Activate();
            }
        }

        /// <summary>The custom waypoint FLTCOM placed for this mark, or null when the player deleted it.</summary>
        private static Waypoint? Find(LandingMark mark)
        {
            double latitude = Angles.ToDegrees(mark.Latitude);
            double longitude = Angles.ToDegrees(mark.Longitude);
            foreach (Waypoint waypoint in WaypointManager.Instance().Waypoints)
            {
                if (waypoint.isCustom && waypoint.name == mark.Name && waypoint.celestialName == mark.Body
                    && Math.Abs(waypoint.latitude - latitude) < SameDegrees
                    && Math.Abs(Math.IEEERemainder(waypoint.longitude - longitude, 360)) < SameDegrees)
                {
                    return waypoint;
                }
            }
            return null;
        }
    }
}

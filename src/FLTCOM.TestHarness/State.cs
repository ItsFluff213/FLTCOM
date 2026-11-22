using System;
using System.Collections.Generic;

namespace FLTCOM.TestHarness
{
    internal static class State
    {
        public static Dictionary<string, object?> Describe()
        {
            var scene = HighLogic.LoadedScene;
            var state = new Dictionary<string, object?>
            {
                ["scene"] = scene.ToString(),
                ["save"] = HighLogic.CurrentGame != null ? HighLogic.SaveFolder : null,
                ["ut"] = HighLogic.CurrentGame != null ? Planetarium.GetUniversalTime() : (double?)null,
            };
            var vessels = new List<object?>();
            var hasVessels = HighLogic.LoadedSceneHasPlanetarium && FlightGlobals.fetch != null;
            var active = hasVessels && HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
            state["active"] = active?.vesselName;
            if (hasVessels)
            {
                foreach (var vessel in FlightGlobals.Vessels)
                {
                    vessels.Add(DescribeVessel(vessel, active));
                }
            }
            state["vessels"] = vessels;
            return state;
        }

        private static Dictionary<string, object?> DescribeVessel(Vessel vessel, Vessel? active)
        {
            var orbit = vessel.orbit;
            var description = new Dictionary<string, object?>
            {
                ["name"] = vessel.vesselName,
                ["id"] = vessel.id.ToString("N"),
                ["persistentId"] = vessel.persistentId,
                ["type"] = vessel.vesselType.ToString(),
                ["situation"] = vessel.situation.ToString(),
                ["body"] = vessel.mainBody != null ? vessel.mainBody.bodyName : null,
                ["loaded"] = vessel.loaded,
                ["packed"] = vessel.packed,
                ["active"] = vessel == active,
            };
            if (orbit != null)
            {
                description["sma"] = orbit.semiMajorAxis;
                description["ecc"] = orbit.eccentricity;
                description["inc"] = orbit.inclination;
                description["period"] = orbit.period;
                description["apA"] = orbit.ApA;
                description["peA"] = orbit.PeA;
            }
            var connection = vessel.connection;
            description["signal"] = connection != null ? connection.IsConnected : (bool?)null;
            if (active != null && vessel != active)
            {
                description["distanceKm"] = Math.Round((vessel.GetWorldPos3D() - active.GetWorldPos3D()).magnitude / 1000.0, 1);
            }
            return description;
        }

        public static Vessel FindVessel(string query)
        {
            var text = query.Trim();
            Vessel? partial = null;
            foreach (var vessel in FlightGlobals.Vessels)
            {
                if (string.Equals(vessel.vesselName, text, StringComparison.OrdinalIgnoreCase) || vessel.id.ToString("N") == text || vessel.persistentId.ToString() == text)
                {
                    return vessel;
                }
                if (partial == null && text.Length > 0 && vessel.vesselName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    partial = vessel;
                }
            }
            return partial ?? throw new ArgumentException("no vessel matches '" + query + "'");
        }
    }
}

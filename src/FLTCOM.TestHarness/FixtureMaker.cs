using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FLTCOM.TestHarness
{
    internal static class FixtureMaker
    {
        private const string FlagUrl = "Squad/Flags/default";

        public static IEnumerator Make(string name, HarnessFiles files, Func<int> sceneLoads, Action<float, string> ready)
        {
            var started = Time.realtimeSinceStartup;
            var recipe = FindRecipe(files.Recipes, name);
            var folder = Harness.SaveFolderFor(name);
            var saveDir = Path.Combine(files.Saves, folder);
            var marker = Path.Combine(saveDir, Harness.FixtureMarker);
            if (Directory.Exists(saveDir))
            {
                if (!File.Exists(marker))
                {
                    throw new InvalidOperationException("saves/" + folder + " has no " + Harness.FixtureMarker + " marker, so it is not a test fixture. make-fixture never replaces it.");
                }
                Directory.Delete(saveDir, true);
            }
            Directory.CreateDirectory(saveDir);
            File.WriteAllText(marker, string.Empty);

            var parameters = GameParameters.GetDefaultParameters(Game.Modes.SANDBOX, GameParameters.Preset.Normal);
            var game = GamePersistence.CreateNewGame(folder, Game.Modes.SANDBOX, parameters, FlagUrl, GameScenes.SPACECENTER, EditorFacility.VAB);
            HighLogic.CurrentGame = game;
            HighLogic.SaveFolder = folder;
            GamePersistence.SaveGame(game, "persistent", folder, SaveMode.OVERWRITE);
            var loads = sceneLoads();
            game.Start();
            var wait = SceneWait.Until(GameScenes.SPACECENTER, loads, sceneLoads, null);
            while (wait.MoveNext())
            {
                yield return wait.Current;
            }

            foreach (var spec in recipe.GetNodes("VESSEL"))
            {
                Spawn(spec, files.KspRoot);
                yield return null;
            }
            GamePersistence.SaveGame("persistent", folder, SaveMode.OVERWRITE);

            loads = sceneLoads();
            var launch = recipe.GetNode("LAUNCH");
            if (launch != null)
            {
                var craft = CraftPath(files.KspRoot, Required(launch, "craft"));
                var craftNode = ConfigNode.Load(craft);
                var crew = HighLogic.CurrentGame.CrewRoster.DefaultCrewForVessel(craftNode, null, true, false);
                FlightDriver.StartWithNewLaunch(craft, FlagUrl, launch.GetValue("site") ?? "LaunchPad", crew);
            }
            else
            {
                var saved = GamePersistence.LoadGame("persistent", folder, true, false);
                var focus = Required(recipe, "focus");
                var index = saved.flightState.protoVessels.FindIndex(p => p.vesselName == focus);
                if (index < 0)
                {
                    throw new InvalidOperationException("focus vessel '" + focus + "' was not spawned");
                }
                HighLogic.CurrentGame = saved;
                FlightDriver.StartAndFocusVessel(saved, index);
            }
            wait = SceneWait.Until(GameScenes.FLIGHT, loads, sceneLoads, null);
            while (wait.MoveNext())
            {
                yield return wait.Current;
            }
            var settle = Time.realtimeSinceStartup + 3;
            while (Time.realtimeSinceStartup < settle)
            {
                yield return null;
            }
            var launchName = launch?.GetValue("name");
            if (!string.IsNullOrEmpty(launchName))
            {
                FlightGlobals.ActiveVessel.vesselName = launchName;
            }
            GamePersistence.SaveGame("persistent", folder, SaveMode.OVERWRITE);
            Harness.Log("fixture " + name + " saved to " + saveDir);
            ready(Time.realtimeSinceStartup - started, HighLogic.LoadedScene.ToString());
        }

        private static ConfigNode FindRecipe(string path, string name)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("recipes not found at " + path + ". The driver copies tests/fixtures/saves/recipes.cfg there.");
            }
            var root = ConfigNode.Load(path);
            foreach (var node in root.GetNodes("FIXTURE"))
            {
                if (node.GetValue("name") == name)
                {
                    return node;
                }
            }
            throw new ArgumentException("no FIXTURE named '" + name + "' in " + path);
        }

        private static string Required(ConfigNode node, string key)
        {
            var value = node.GetValue(key);
            if (string.IsNullOrEmpty(value))
            {
                throw new FormatException(node.name + " needs a value for '" + key + "'");
            }
            return value;
        }

        private static double Number(ConfigNode node, string key, double fallback)
        {
            var value = node.GetValue(key);
            return string.IsNullOrEmpty(value) ? fallback : double.Parse(value, CultureInfo.InvariantCulture);
        }

        private static string CraftPath(string kspRoot, string relative)
        {
            var path = Path.Combine(kspRoot, relative);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("craft file not found: " + path);
            }
            return path;
        }

        private static void Spawn(ConfigNode spec, string kspRoot)
        {
            var name = Required(spec, "name");
            var type = (VesselType)Enum.Parse(typeof(VesselType), spec.GetValue("type") ?? "Ship");
            var body = FlightGlobals.GetBodyByName(Required(spec, "body")) ?? throw new ArgumentException("unknown body in " + name);
            var periapsis = body.Radius + Number(spec, "periapsis", 100000);
            var apoapsis = body.Radius + Number(spec, "apoapsis", Number(spec, "periapsis", 100000));
            var sma = (periapsis + apoapsis) / 2;
            var ecc = (apoapsis - periapsis) / (apoapsis + periapsis);
            var orbit = new Orbit(
                Number(spec, "inclination", 0),
                ecc,
                sma,
                Number(spec, "lan", 0),
                Number(spec, "argumentOfPeriapsis", 0),
                Number(spec, "meanAnomaly", 0),
                Planetarium.GetUniversalTime(),
                body);
            var crewCount = (int)Number(spec, "crew", 0);
            var part = spec.GetValue("part");
            var parts = string.IsNullOrEmpty(part)
                ? PartsFromCraft(CraftPath(kspRoot, Required(spec, "craft")), crewCount)
                : new[] { ProtoVessel.CreatePartNode(part, ShipConstruction.GetUniqueFlightID(HighLogic.CurrentGame.flightState), HireCrew(crewCount)) };
            var fuel = Number(spec, "fuel", 1);
            foreach (var resource in parts.SelectMany(p => p.GetNodes("RESOURCE")).Where(r => r.GetValue("name") != "ElectricCharge"))
            {
                var max = double.Parse(resource.GetValue("maxAmount"), CultureInfo.InvariantCulture);
                resource.SetValue("amount", (max * fuel).ToString("R", CultureInfo.InvariantCulture));
            }
            var discovery = ProtoVessel.CreateDiscoveryNode(DiscoveryLevels.Owned, UntrackedObjectClass.A, double.PositiveInfinity, double.PositiveInfinity);
            var firstStage = parts.Select(p => int.TryParse(p.GetValue("istg"), out var stage) ? stage : -1).DefaultIfEmpty(-1).Max() + 1;
                        var vesselNode = ProtoVessel.CreateVesselNode(name, type, orbit, firstStage, parts, discovery);
            HighLogic.CurrentGame.AddVessel(vesselNode);
            Harness.Log("spawned " + name + " around " + body.bodyName + " with " + parts.Length + " parts");
        }

        private static ProtoCrewMember[] HireCrew(int count)
        {
            var crew = new ProtoCrewMember[count];
            for (var i = 0; i < count; i++)
            {
                var member = HighLogic.CurrentGame.CrewRoster.GetNextOrNewKerbal(ProtoCrewMember.KerbalType.Crew);
                member.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                member.seatIdx = i;
                crew[i] = member;
            }
            return crew;
        }

        private static ConfigNode[] PartsFromCraft(string craftPath, int crewCount)
        {
            var previousShip = ShipConstruction.ShipConfig;
            var ship = ShipConstruction.LoadShip(craftPath);
            ShipConstruction.ShipConfig = previousShip;
            if (ship == null)
            {
                throw new InvalidOperationException("ShipConstruction.LoadShip failed for " + craftPath + "; check KSP.log for missing parts");
            }
            try
            {
                foreach (var clamp in ship.parts.FindAll(p => p.FindModuleImplementing<LaunchClamp>() != null))
                {
                    clamp.parent?.children.Remove(clamp);
                    ship.parts.Remove(clamp);
                    UnityEngine.Object.Destroy(clamp.gameObject);
                }
                var missionId = (uint)Guid.NewGuid().GetHashCode();
                var launchId = HighLogic.CurrentGame.launchID++;
                foreach (var p in ship.parts)
                {
                    p.flightID = ShipConstruction.GetUniqueFlightID(HighLogic.CurrentGame.flightState);
                    p.missionID = missionId;
                    p.launchID = launchId;
                    p.flagURL = FlagUrl;
                    p.temperature = 1.0;
                }

                var dummyProto = new ProtoVessel(new ConfigNode(), null);
                var dummyVessel = new Vessel();
                dummyVessel.parts = ship.parts;
                dummyProto.vesselRef = dummyVessel;
                foreach (var p in ship.parts)
                {
                    dummyProto.protoPartSnapshots.Add(new ProtoPartSnapshot(p, dummyProto));
                }
                foreach (var snapshot in dummyProto.protoPartSnapshots)
                {
                    snapshot.storePartRefs();
                }

                var crewPart = ship.parts.FindIndex(p => p.CrewCapacity > 0 && p.FindModuleImplementing<ModuleCommand>() != null);
                var nodes = new List<ConfigNode>();
                for (var i = 0; i < dummyProto.protoPartSnapshots.Count; i++)
                {
                    var node = new ConfigNode("PART");
                    dummyProto.protoPartSnapshots[i].Save(node);
                    if (i == crewPart)
                    {
                        node.RemoveValues("crew");
                        foreach (var member in HireCrew(Math.Min(crewCount, ship.parts[i].CrewCapacity)))
                        {
                            node.AddValue("crew", member.name);
                        }
                    }
                    nodes.Add(node);
                }
                if (crewCount > 0 && crewPart < 0)
                {
                    throw new InvalidOperationException(craftPath + " has no crewed command part for the requested crew");
                }
                return nodes.ToArray();
            }
            finally
            {
                foreach (var p in ship.parts)
                {
                    if (p != null)
                    {
                        UnityEngine.Object.Destroy(p.gameObject);
                    }
                }
            }
        }
    }
}

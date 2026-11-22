using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FLTCOM.TestHarness
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class Harness : MonoBehaviour
    {
        internal const string FixtureMarker = ".fltcom-fixture";
        private const float PollSeconds = 0.25f;
        private static readonly Regex SlugPattern = new Regex("^[A-Za-z0-9_-]{1,64}$");

        private readonly Queue<Command> queue = new Queue<Command>();
        private HarnessFiles files = null!;
        private float nextPoll;
        private int sceneLoads;
        private Command? warpWatch;
        private double warpTarget;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            var harnessRoot = Path.GetDirectoryName(typeof(Harness).Assembly.Location) ?? ".";
            files = new HarnessFiles(harnessRoot, KSPUtil.ApplicationRootPath);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnSceneLoaded);
            Log("harness awake, commands at " + files.Commands);
        }

        private void OnDestroy()
        {
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnSceneLoaded);
        }

        private void OnSceneLoaded(GameScenes scene)
        {
            sceneLoads++;
            Log("scene loaded: " + scene);
        }

        private void Start()
        {
            files.Append(Event("menu", ("scene", HighLogic.LoadedScene.ToString())));
            var boot = ReadBootConfig();
            if (boot.fixture != null)
            {
                queue.Enqueue(new Command("boot", "load", boot.fixture));
            }
            else if (boot.menu)
            {
                files.Append(Ready("boot", "MAINMENU", 0));
            }
            StartCoroutine(RunQueue());
        }

        private (string? fixture, bool menu) ReadBootConfig()
        {
            if (!File.Exists(files.BootConfig))
            {
                return (null, false);
            }
            var node = ConfigNode.Load(files.BootConfig);
            if (node == null)
            {
                return (null, false);
            }
            var fixture = node.GetValue("fixture");
            return (string.IsNullOrEmpty(fixture) ? null : fixture, node.GetValue("menu") == "true");
        }

        private void Update()
        {
            if (warpWatch != null && Planetarium.GetUniversalTime() >= warpTarget - 1)
            {
                files.Append(Event("warp-done", ("ut", Planetarium.GetUniversalTime())));
                warpWatch = null;
            }
            if (Time.realtimeSinceStartup < nextPoll)
            {
                return;
            }
            nextPoll = Time.realtimeSinceStartup + PollSeconds;
            List<string> lines;
            try
            {
                lines = files.ReadNewCommands();
            }
            catch (IOException e)
            {
                Log("could not read commands: " + e.Message);
                return;
            }
            foreach (var line in lines)
            {
                try
                {
                    var fields = Json.ParseFlatObject(line);
                    fields.TryGetValue("id", out var id);
                    fields.TryGetValue("cmd", out var name);
                    fields.TryGetValue("arg", out var arg);
                    queue.Enqueue(new Command(id ?? "?", name ?? string.Empty, arg ?? string.Empty));
                }
                catch (FormatException e)
                {
                    files.Append(Reply("?", false, "bad command line: " + e.Message));
                }
            }
        }

        private IEnumerator RunQueue()
        {
            while (true)
            {
                if (queue.Count == 0)
                {
                    yield return null;
                    continue;
                }
                var command = queue.Dequeue();
                Log("command " + command.Id + ": " + command.Name + " " + command.Arg);
                IEnumerator body;
                try
                {
                    body = Execute(command);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    files.Append(Reply(command.Id, false, e.GetType().Name + ": " + e.Message));
                    continue;
                }
                while (true)
                {
                    object? current;
                    try
                    {
                        if (!body.MoveNext())
                        {
                            break;
                        }
                        current = body.Current;
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        files.Append(Reply(command.Id, false, e.GetType().Name + ": " + e.Message));
                        break;
                    }
                    yield return current;
                }
            }
        }

        private IEnumerator Execute(Command command)
        {
            switch (command.Name)
            {
                case "state":
                    files.Append(Reply(command.Id, true, "state", State.Describe()));
                    break;
                case "screenshot":
                    return Screenshot(command);
                case "switch":
                    return Switch(command);
                case "warp":
                    StartWarp(command);
                    break;
                case "load":
                    return Load(command, command.Arg);
                case "log-tail":
                    var count = int.TryParse(command.Arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 40;
                    files.Append(Reply(command.Id, true, "log-tail", new Dictionary<string, object?> { ["lines"] = files.TailKspLog(count) }));
                    break;
                case "make-fixture":
                    return FixtureMaker.Make(command.Arg, files, () => sceneLoads, (seconds, scene) => files.Append(Ready(command.Id, scene, seconds)));
                case "quit":
                    files.Append(Reply(command.Id, true, "quitting"));
                    return Quit();
                default:
                    files.Append(Reply(command.Id, false, "unknown command '" + command.Name + "'"));
                    break;
            }
            return Done();
        }

        private static IEnumerator Done()
        {
            yield break;
        }

        private static IEnumerator Quit()
        {
            yield return null;
            Application.Quit();
        }

        private IEnumerator Load(Command command, string fixture)
        {
            var started = Time.realtimeSinceStartup;
            var folder = SaveFolderFor(fixture);
            var file = Path.Combine(Path.Combine(files.Saves, folder), "persistent.sfs");
            if (!File.Exists(file))
            {
                throw new FileNotFoundException("fixture save not found: " + file + ". The driver copies fixtures into saves/fltcom-<fixture>/ before load.");
            }
            var game = GamePersistence.LoadGame("persistent", folder, true, false);
            if (game == null || game.flightState == null)
            {
                throw new InvalidOperationException("GamePersistence.LoadGame returned no game for " + folder);
            }
            GamePersistence.UpdateScenarioModules(game);
            var loadsBefore = sceneLoads;
            HighLogic.SaveFolder = folder;
            HighLogic.CurrentGame = game;
            var expected = GameScenes.SPACECENTER;
            if (game.flightState.protoVessels.Count > 0)
            {
                var index = Mathf.Clamp(game.flightState.activeVesselIdx, 0, game.flightState.protoVessels.Count - 1);
                expected = GameScenes.FLIGHT;
                FlightDriver.StartAndFocusVessel(game, index);
            }
            else
            {
                game.startScene = GameScenes.SPACECENTER;
                game.Start();
            }
            var wait = SceneWait.Until(expected, loadsBefore, () => sceneLoads, null);
            while (wait.MoveNext())
            {
                yield return wait.Current;
            }
            files.Append(Ready(command.Id, HighLogic.LoadedScene.ToString(), Time.realtimeSinceStartup - started));
        }

        private IEnumerator Switch(Command command)
        {
            if (!HighLogic.LoadedSceneIsFlight)
            {
                throw new InvalidOperationException("switch needs the flight scene, the game is in " + HighLogic.LoadedScene);
            }
            var target = State.FindVessel(command.Arg);
            if (FlightGlobals.ActiveVessel == target)
            {
                files.Append(Reply(command.Id, true, "already active: " + target.vesselName));
                yield break;
            }
            if (target.loaded)
            {
                FlightGlobals.SetActiveVessel(target);
                var deadline = Time.realtimeSinceStartup + 10;
                while (FlightGlobals.ActiveVessel != target && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
            }
            else
            {
                var folder = HighLogic.SaveFolder;
                GamePersistence.SaveGame("persistent", folder, SaveMode.OVERWRITE);
                var game = GamePersistence.LoadGame("persistent", folder, true, false);
                var index = game.flightState.protoVessels.FindIndex(p => p.vesselID == target.id);
                if (index < 0)
                {
                    throw new InvalidOperationException("vessel " + target.vesselName + " is not in the saved game");
                }
                var loadsBefore = sceneLoads;
                var id = target.id;
                FlightDriver.StartAndFocusVessel(game, index);
                var wait = SceneWait.Until(GameScenes.FLIGHT, loadsBefore, () => sceneLoads, () => FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.id == id);
                while (wait.MoveNext())
                {
                    yield return wait.Current;
                }
            }
            var active = FlightGlobals.ActiveVessel;
            var ok = active != null && active.id == target.id;
            files.Append(Reply(command.Id, ok, ok ? "active vessel: " + active!.vesselName : "switch did not take", State.Describe()));
        }

        private void StartWarp(Command command)
        {
            if (!HighLogic.LoadedSceneIsFlight || FlightGlobals.ActiveVessel == null)
            {
                throw new InvalidOperationException("warp needs the flight scene with an active vessel");
            }
            var now = Planetarium.GetUniversalTime();
            var target = ParseWarpTarget(command.Arg, now, FlightGlobals.ActiveVessel.orbit.period);
            if (target <= now)
            {
                throw new ArgumentException("warp target " + target + " is not after the current UT " + now);
            }
            TimeWarp.fetch.WarpTo(target);
            warpTarget = target;
            warpWatch = command;
            files.Append(Reply(command.Id, true, "warping", new Dictionary<string, object?> { ["from"] = now, ["to"] = target }));
        }

        internal static double ParseWarpTarget(string spec, double now, double period)
        {
            var text = spec.Trim();
            if (text.StartsWith("+", StringComparison.Ordinal))
            {
                text = text.Substring(1);
                if (text.EndsWith("orbit", StringComparison.OrdinalIgnoreCase) || text.EndsWith("orbits", StringComparison.OrdinalIgnoreCase))
                {
                    var count = text.TrimEnd('s', 'S').Substring(0, text.TrimEnd('s', 'S').Length - "orbit".Length);
                    var orbits = count.Length == 0 ? 1 : double.Parse(count, CultureInfo.InvariantCulture);
                    return now + orbits * period;
                }
                return now + double.Parse(text, CultureInfo.InvariantCulture);
            }
            return double.Parse(text, CultureInfo.InvariantCulture);
        }

        private IEnumerator Screenshot(Command command)
        {
            if (!SlugPattern.IsMatch(command.Arg))
            {
                throw new ArgumentException("screenshot slug must match " + SlugPattern);
            }
            var path = Path.Combine(files.Screenshots, command.Arg + ".png");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            ScreenCapture.CaptureScreenshot(path);
            var deadline = Time.realtimeSinceStartup + 15;
            long lastSize = -1;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (!File.Exists(path))
                {
                    continue;
                }
                var size = new FileInfo(path).Length;
                if (size > 0 && size == lastSize)
                {
                    files.Append(Reply(command.Id, true, "screenshot saved", new Dictionary<string, object?> { ["path"] = path }));
                    yield break;
                }
                lastSize = size;
            }
            throw new TimeoutException("screenshot did not appear at " + path);
        }

        internal static string SaveFolderFor(string fixture)
        {
            if (!SlugPattern.IsMatch(fixture))
            {
                throw new ArgumentException("fixture names match " + SlugPattern);
            }
            return "fltcom-" + fixture;
        }

        internal static void Log(string message)
        {
            Debug.Log("[FLTCOM.TestHarness] " + message);
        }

        internal static Dictionary<string, object?> Reply(string id, bool ok, string message, Dictionary<string, object?>? data = null)
        {
            var reply = new Dictionary<string, object?> { ["id"] = id, ["ok"] = ok, ["msg"] = message };
            if (data != null)
            {
                reply["data"] = data;
            }
            return reply;
        }

        private static Dictionary<string, object?> Ready(string id, string scene, float seconds)
        {
            var reply = Reply(id, true, "ksp ready scene=" + scene);
            reply["event"] = "ready";
            reply["scene"] = scene;
            reply["seconds"] = Math.Round(seconds, 1);
            return reply;
        }

        private static Dictionary<string, object?> Event(string name, params (string key, object? value)[] fields)
        {
            var message = new Dictionary<string, object?> { ["event"] = name };
            foreach (var (key, value) in fields)
            {
                message[key] = value;
            }
            return message;
        }

        private sealed class Command
        {
            public Command(string id, string name, string arg)
            {
                Id = id;
                Name = name;
                Arg = arg;
            }

            public string Id { get; }

            public string Name { get; }

            public string Arg { get; }
        }
    }
}

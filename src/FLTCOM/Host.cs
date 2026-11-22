using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using FLTCOM.Compat;
using FLTCOM.Execution;
using FLTCOM.Core.Protocol;
using FLTCOM.Server;
using FLTCOM.Snapshot;
using FLTCOM.Ui;
using UnityEngine;

namespace FLTCOM
{
    /// <summary>
    /// Lives for the whole game session. It runs the display server and, while a save is loaded, publishes a fleet
    /// snapshot from the main thread twice a second. A server that cannot start leaves the game running and the
    /// toolbar button explaining why.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class Host : MonoBehaviour
    {
        private const float SnapshotInterval = 0.5f;
        private const int TimingWindow = 120;

        private readonly ToolbarButton _toolbar = new ToolbarButton();
        private readonly FleetReader _reader = new FleetReader();
        private readonly List<double> _snapshotMs = new List<double>(TimingWindow);
        private HttpServer? _server;
        private string? _announcedSave;
        private float _nextSnapshot;
        private string? _lastError;
        private string? _lastWaypointError;

        private static string ServerName => "FLTCOM " + typeof(Host).Assembly.GetName().Version.ToString(3);

        public void Awake()
        {
            DontDestroyOnLoad(gameObject);
            PluginFiles.StartLog();
            _toolbar.Attach();

            ByeReason? denial = ModDetector.Denial();
            if (denial != null)
            {
                string words = ModDetector.Words(denial.Value);
                PluginFiles.Log(words);
                _toolbar.Fail(words);
                return;
            }

            int port = PluginFiles.ReadPort();
            try
            {
                _server = HttpServer.Start(new ServerOptions(port, PluginFiles.WebRoot, new ServerHello(ServerName, null, SnapshotInterval), PluginFiles.Log));
                MissionDesk.Server = _server;
                string url = "http://127.0.0.1:" + _server.Port.ToString(CultureInfo.InvariantCulture) + "/";
                _toolbar.Serve(url);
                PluginFiles.Log(ServerName + " serving " + url);
            }
            catch (PortBusyException busy)
            {
                PluginFiles.Log(busy.Message);
                _toolbar.Fail(busy.Message + ". Set another port in GameData/FLTCOM/PluginData/settings.cfg and restart KSP.");
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException || e is System.Net.Sockets.SocketException)
            {
                PluginFiles.Log("server did not start: " + e);
                _toolbar.Fail("display server did not start: " + e.Message);
            }
        }

        public void Update()
        {
            if (_server == null)
            {
                return;
            }
            while (_server.Inbox.TryTake(out ClientMessage message))
            {
                if (SaveLoaded && HighLogic.LoadedScene != GameScenes.EDITOR)
                {
                    MissionDesk.Handle(message);
                }
            }

            string? save = SaveLoaded ? HighLogic.SaveFolder : null;
            if (save != _announcedSave)
            {
                _announcedSave = save;
                _server.Announce(new ServerHello(ServerName, save, SnapshotInterval));
                PluginFiles.Log("save " + (save ?? "(none)"));
            }
            if (save == null)
            {
                return;
            }
            MakeArt(_server);
            if (Time.realtimeSinceStartup < _nextSnapshot)
            {
                return;
            }
            _nextSnapshot = Time.realtimeSinceStartup + SnapshotInterval;
            Publish(_server);
        }

        public void OnDestroy()
        {
            _toolbar.Detach();
            _server?.Dispose();
            _server = null;
        }

        private static bool SaveLoaded =>
            HighLogic.CurrentGame != null
            && (HighLogic.LoadedScene == GameScenes.FLIGHT || HighLogic.LoadedScene == GameScenes.TRACKSTATION
                || HighLogic.LoadedScene == GameScenes.SPACECENTER || HighLogic.LoadedScene == GameScenes.EDITOR);

        private void Publish(HttpServer server)
        {
            try
            {
                LandingWaypoints.Sync();
            }
            catch (Exception e)
            {
                if (e.Message != _lastWaypointError)
                {
                    _lastWaypointError = e.Message;
                    PluginFiles.Log("landing waypoints failed: " + e);
                }
            }
            var watch = Stopwatch.StartNew();
            try
            {
                (Core.Protocol.Fleet fleet, List<CraftLayout> changed, List<uint> gone) = _reader.Read();
                server.Publish(fleet);
                foreach (CraftLayout layout in changed)
                {
                    server.PublishLayout(layout);
                }
                foreach (uint id in gone)
                {
                    server.ForgetLayout(id);
                }
                _lastError = null;
            }
            catch (Exception e)
            {
                string error = e.GetType().Name + ": " + e.Message;
                if (error != _lastError)
                {
                    _lastError = error;
                    PluginFiles.Log("snapshot failed: " + e);
                }
                return;
            }
            Record(watch.Elapsed.TotalMilliseconds);
        }

        /// <summary>At most one body texture and one craft model a frame, so making art never stalls the game.</summary>
        private void MakeArt(HttpServer server)
        {
            try
            {
                _reader.Textures.CaptureNext(server);
                _reader.Models.ExportNext(server);
            }
            catch (Exception e)
            {
                PluginFiles.Log("art failed: " + e);
            }
        }

        private void Record(double ms)
        {
            _snapshotMs.Add(ms);
            if (_snapshotMs.Count < TimingWindow)
            {
                return;
            }
            _snapshotMs.Sort();
            double p95 = _snapshotMs[(int)(TimingWindow * 0.95) - 1];
            PluginFiles.Log("snapshot ms p95 " + p95.ToString("F3", CultureInfo.InvariantCulture) + " max " + _snapshotMs[TimingWindow - 1].ToString("F3", CultureInfo.InvariantCulture));
            _snapshotMs.Clear();
        }
    }
}

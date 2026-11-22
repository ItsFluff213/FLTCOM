using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace FLTCOM
{
    /// <summary>FLTCOM's folder in GameData, its settings file, and its log.</summary>
    internal static class PluginFiles
    {
        public const int DefaultPort = 47800;

        private static readonly object LogLock = new object();

        public static string ModRoot => Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "FLTCOM");
        public static string WebRoot => Path.Combine(ModRoot, "web");
        private static string PluginData => Path.Combine(ModRoot, "PluginData");
        private static string SettingsPath => Path.Combine(PluginData, "settings.cfg");
        private static string LogPath => Path.Combine(PluginData, "fltcom.log");

        /// <summary>Reads the port from settings.cfg, writing the file with the default port when it is missing.</summary>
        public static int ReadPort()
        {
            Directory.CreateDirectory(PluginData);
            if (!File.Exists(SettingsPath))
            {
                var root = new ConfigNode();
                root.AddNode("FLTCOM").AddValue("port", DefaultPort);
                root.Save(SettingsPath);
                return DefaultPort;
            }
            string? text = ConfigNode.Load(SettingsPath)?.GetNode("FLTCOM")?.GetValue("port");
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port > 0 && port <= 65535 && port != 50000 && port != 50001)
            {
                return port;
            }
            Log("settings.cfg port '" + text + "' is not usable; using " + DefaultPort.ToString(CultureInfo.InvariantCulture));
            return DefaultPort;
        }

        public static void Log(string line)
        {
            Debug.Log("[FLTCOM] " + line);
            lock (LogLock)
            {
                try
                {
                    Directory.CreateDirectory(PluginData);
                    File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + line + Environment.NewLine);
                }
                catch (IOException)
                {
                }
            }
        }

        public static void StartLog()
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(PluginData);
                File.WriteAllText(LogPath, string.Empty);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FLTCOM.TestHarness
{
    internal sealed class HarnessFiles
    {
        private long commandOffset;

        public HarnessFiles(string harnessRoot, string kspRoot)
        {
            KspRoot = kspRoot;
            Folder = Path.Combine(Path.Combine(harnessRoot, "PluginData"), "harness");
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(Screenshots);
            commandOffset = File.Exists(Commands) ? new FileInfo(Commands).Length : 0;
        }

        public string KspRoot { get; }

        public string Folder { get; }

        public string Commands => Path.Combine(Folder, "cmd.jsonl");

        public string Output => Path.Combine(Folder, "out.jsonl");

        public string BootConfig => Path.Combine(Folder, "boot.cfg");

        public string Recipes => Path.Combine(Folder, "recipes.cfg");

        public string Screenshots => Path.Combine(Folder, "screenshots");

        public string KspLog => Path.Combine(KspRoot, "KSP.log");

        public string Saves => Path.Combine(KspRoot, "saves");

        public List<string> ReadNewCommands()
        {
            var lines = new List<string>();
            if (!File.Exists(Commands))
            {
                return lines;
            }
            using var stream = new FileStream(Commands, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < commandOffset)
            {
                commandOffset = 0;
            }
            if (stream.Length == commandOffset)
            {
                return lines;
            }
            stream.Seek(commandOffset, SeekOrigin.Begin);
            var buffer = new byte[stream.Length - commandOffset];
            var read = 0;
            while (read < buffer.Length)
            {
                var chunk = stream.Read(buffer, read, buffer.Length - read);
                if (chunk == 0)
                {
                    break;
                }
                read += chunk;
            }
            var lastNewline = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
            if (lastNewline < 0)
            {
                return lines;
            }
            commandOffset += lastNewline + 1;
            var text = new UTF8Encoding(false).GetString(buffer, 0, lastNewline + 1);
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim().TrimStart('﻿');
                if (trimmed.Length > 0)
                {
                    lines.Add(trimmed);
                }
            }
            return lines;
        }

        public void Append(IDictionary<string, object?> message)
        {
            var line = Json.Write(message) + "\n";
            var bytes = new UTF8Encoding(false).GetBytes(line);
            using var stream = new FileStream(Output, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            stream.Write(bytes, 0, bytes.Length);
        }

        public List<string> TailKspLog(int count)
        {
            var lines = new List<string>();
            if (!File.Exists(KspLog))
            {
                return lines;
            }
            using var stream = new FileStream(KspLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var window = Math.Min(stream.Length, 256 * 1024);
            stream.Seek(-window, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            var all = reader.ReadToEnd().Replace("\r", string.Empty).Split('\n');
            var end = all.Length;
            while (end > 0 && all[end - 1].Length == 0)
            {
                end--;
            }
            for (var i = Math.Max(0, end - count); i < end; i++)
            {
                lines.Add(all[i]);
            }
            return lines;
        }
    }
}

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using FLTCOM.Core.Protocol;
using FLTCOM.Server;

namespace FLTCOM.ServerHost
{
    /// <summary>
    /// Runs FLTCOM.Server outside the game. The mock telemetry server, started with --feed, writes one fleet
    /// message per line; each is parsed and published the way the plugin will publish its own snapshots.
    /// </summary>
    public static class Program
    {
        private const string Usage = "usage: dotnet run --project tools/server-host -- [--fixture kerbin-small] [--port 47800] [--web <dir>]";

        public static int Main(string[] args)
        {
            string root = RepoRoot();
            string fixture = "kerbin-small";
            int port = 47800;
            string web = Path.Combine(root, "dist", "GameData", "FLTCOM", "web");
            for (int i = 0; i < args.Length; i++)
            {
                string? value = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "--fixture" when value != null:
                        fixture = value;
                        break;
                    case "--port" when value != null && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port):
                        break;
                    case "--web" when value != null:
                        web = Path.GetFullPath(value);
                        break;
                    default:
                        Console.Error.WriteLine(Usage);
                        return 2;
                }
                i++;
            }

            HttpServer server;
            try
            {
                server = HttpServer.Start(new ServerOptions(port, web, new ServerHello("FLTCOM server-host", fixture, 1), Log));
            }
            catch (PortBusyException e)
            {
                Console.Error.WriteLine(e.Message);
                return 1;
            }

            using (server)
            using (Process feed = StartFeed(root, fixture))
            {
                using var stop = new ManualResetEventSlim();
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    stop.Set();
                };
                var reader = new Thread(() => PublishEachLine(feed, server, stop)) { IsBackground = true };
                reader.Start();
                Log("server host listening on 127.0.0.1:" + server.Port.ToString(CultureInfo.InvariantCulture));

                while (!stop.Wait(50))
                {
                    while (server.Inbox.TryTake(out ClientMessage message))
                    {
                        Log("inbox " + ProtocolJson.Write(message));
                    }
                }

                server.Stop(ByeReason.Shutdown);
                if (!feed.HasExited)
                {
                    feed.Kill(entireProcessTree: true);
                }
            }
            return 0;
        }

        private static void PublishEachLine(Process feed, HttpServer server, ManualResetEventSlim stop)
        {
            for (string? line = feed.StandardOutput.ReadLine(); line != null; line = feed.StandardOutput.ReadLine())
            {
                try
                {
                    ServerMessage message = ProtocolJson.ReadServerMessage(line);
                    if (message is Fleet fleet)
                    {
                        server.Publish(fleet);
                    }
                }
                catch (ProtocolException e)
                {
                    Log("feed line is not a valid server message: " + e.Message);
                }
            }
            Log("feed ended");
            stop.Set();
        }

        private static Process StartFeed(string root, string fixture)
        {
            var start = new ProcessStartInfo("node")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = root,
            };
            foreach (string arg in new[] { Path.Combine("tools", "mock-telemetry", "server.mjs"), "--fixture", fixture, "--feed" })
            {
                start.ArgumentList.Add(arg);
            }
            return Process.Start(start) ?? throw new InvalidOperationException("node did not start");
        }

        private static string RepoRoot()
        {
            for (DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FLTCOM.sln")))
                {
                    return dir.FullName;
                }
            }
            throw new DirectoryNotFoundException("no FLTCOM.sln above " + AppContext.BaseDirectory);
        }

        private static void Log(string line) =>
            Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + line);
    }
}

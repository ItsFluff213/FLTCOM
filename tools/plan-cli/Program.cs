using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FLTCOM.Core.Missions;
using FLTCOM.Core.Planning;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.PlanCli
{
    public static class Program
    {
        private const string Usage =
            "usage: plan-cli <change> [value] --fixture <name> [--pin <stage>,<stage>...] [--name <codename>]\n" +
            "  changes: circularize | apoapsis <km> | periapsis <km> | inclination <deg> | circular <km> | standard\n" +
            "  fixtures: ";

        public static int Main(string[] args)
        {
            try
            {
                var positional = new List<string>();
                string? fixture = null;
                string? name = null;
                IReadOnlyList<int?>? pins = null;
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--fixture": fixture = args[++i]; break;
                        case "--name": name = args[++i]; break;
                        case "--pin": pins = args[++i].Split(',').Select(p => p == "auto" ? (int?)null : int.Parse(p, CultureInfo.InvariantCulture)).ToList(); break;
                        default: positional.Add(args[i]); break;
                    }
                }
                if (positional.Count == 0 || fixture == null)
                {
                    Console.Error.WriteLine(Usage + string.Join(", ", Fixtures.Names));
                    return 2;
                }
                OrbitChange change = ParseChange(positional);
                VesselTask task = Fixtures.Load(fixture);
                if (pins != null)
                {
                    task = new VesselTask(task.VesselId, task.VesselName, task.Body, task.Orbit, task.Stages, pins);
                }
                string codename = name ?? Codename.Generate(Seed(fixture + "/" + change.Describe()));
                Briefing briefing = BriefingBuilder.Build(codename, change, new[] { task }, Fixtures.Epoch);
                Console.Write(briefing.Format(Fixtures.Epoch));
                return briefing.CanApprove ? 0 : 1;
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is IndexOutOfRangeException)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine(Usage + string.Join(", ", Fixtures.Names));
                return 2;
            }
        }

        private static ulong Seed(string text)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in text)
            {
                hash = unchecked((hash ^ c) * 1099511628211UL);
            }
            return hash;
        }

        private static OrbitChange ParseChange(List<string> positional)
        {
            double Value() => positional.Count > 1
                ? double.Parse(positional[1], CultureInfo.InvariantCulture)
                : throw new ArgumentException(positional[0] + " needs a value");
            return positional[0] switch
            {
                "circularize" => OrbitChange.Circularize(),
                "apoapsis" => OrbitChange.Apoapsis(Value() * 1000),
                "periapsis" => OrbitChange.Periapsis(Value() * 1000),
                "inclination" => OrbitChange.Inclination(Angles.ToRadians(Value())),
                "circular" => OrbitChange.CircularAt(Value() * 1000),
                "standard" => OrbitChange.StandardLow(),
                _ => throw new ArgumentException("unknown change " + positional[0]),
            };
        }
    }
}

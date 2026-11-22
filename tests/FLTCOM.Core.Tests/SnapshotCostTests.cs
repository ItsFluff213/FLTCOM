using System.Collections.Generic;
using System.Diagnostics;
using FLTCOM.Core.Protocol;
using FLTCOM.Core.Telemetry;
using Xunit;

namespace FLTCOM.Core.Tests
{
    /// <summary>
    /// The Core-side share of a snapshot for a 300-vessel fleet (registry codes and the comms filter) has to stay far
    /// under the 1 ms snapshot target, so the p95 seen in game comes from reading the game's own objects.
    /// It runs alone, after the parallel tests, so CPU-heavy planner tests cannot inflate its timing.
    /// </summary>
    [Collection(TimingCollection.Name)]
    public class SnapshotCostTests
    {
        [Fact]
        public void CoreWorkForALargeFleetIsAFractionOfTheBudget()
        {
            const int vessels = 300;
            var filter = new CommsFilter();
            var raw = new List<(uint, CommsState, double?)>();
            for (uint i = 0; i < vessels; i++)
            {
                raw.Add((i + 1, i % 3 == 0 ? CommsState.Los : CommsState.Link, 0.5));
            }
            var samples = new List<double>();
            for (int run = 0; run < 220; run++)
            {
                var watch = Stopwatch.StartNew();
                foreach (var r in raw)
                {
                    RegistryCode.For("FLT", r.Item1, VesselType.Probe);
                }
                filter.Filter(run, raw);
                samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            samples.RemoveRange(0, 20);
            samples.Sort();
            double p95 = samples[(int)(samples.Count * 0.95) - 1];
            Assert.True(p95 < 0.5, "core snapshot work p95 " + p95 + " ms");
        }
    }
}

namespace FLTCOM.Core.Tests
{
    /// <summary>Tests that measure wall-clock time run in this collection, alone and after every parallel test.</summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class TimingCollection
    {
        public const string Name = "Timing";
    }
}

using System.Collections.Generic;
using FLTCOM.Core.Protocol;

namespace FLTCOM.Core.Telemetry
{
    /// <summary>
    /// Stops the fleet display lying about signal. After an on-rails orbit change or a warp the game's comm network
    /// can sit unsolved for a while and report every vessel as out of contact at once. A single vessel losing signal is
    /// believed; every vessel losing it in the same snapshot, after some had a link, is treated as an unsolved network
    /// and each vessel keeps its last known link reading, for at most <see cref="MaxHoldSeconds"/>.
    /// </summary>
    public sealed class CommsFilter
    {
        public const double MaxHoldSeconds = 120;

        private readonly Dictionary<uint, double?> _lastLink = new Dictionary<uint, double?>();
        private double? _holdingSince;

        public IReadOnlyList<(CommsState State, double? Strength)> Filter(double nowSeconds, IReadOnlyList<(uint Id, CommsState State, double? Strength)> raw)
        {
            int links = 0;
            int lost = 0;
            foreach (var r in raw)
            {
                if (r.State == CommsState.Link)
                {
                    links++;
                }
                else if (r.State == CommsState.Los)
                {
                    lost++;
                }
            }

            var result = new List<(CommsState State, double? Strength)>(raw.Count);
            if (links > 0)
            {
                _holdingSince = null;
                foreach (var r in raw)
                {
                    if (r.State == CommsState.Link)
                    {
                        _lastLink[r.Id] = r.Strength;
                    }
                    else
                    {
                        _lastLink.Remove(r.Id);
                    }
                    result.Add((r.State, r.Strength));
                }
                return result;
            }

            bool fleetWide = lost >= 2 && _lastLink.Count > 0;
            if (fleetWide && _holdingSince == null)
            {
                _holdingSince = nowSeconds;
            }
            bool holding = fleetWide && nowSeconds - _holdingSince!.Value <= MaxHoldSeconds;
            foreach (var r in raw)
            {
                if (holding && r.State == CommsState.Los && _lastLink.TryGetValue(r.Id, out double? strength))
                {
                    result.Add((CommsState.Link, strength));
                }
                else
                {
                    result.Add((r.State, r.Strength));
                }
            }
            if (!fleetWide)
            {
                _holdingSince = null;
                _lastLink.Clear();
            }
            return result;
        }
    }
}

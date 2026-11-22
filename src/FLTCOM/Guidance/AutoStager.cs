using FLTCOM.Core.AutoSystems;
using FLTCOM.Execution;
using KSP.UI.Screens;
using UnityEngine;

namespace FLTCOM.Guidance
{
    /// <summary>Reads what the next staging would do and stages when <see cref="StagingRule"/> says so, at most once a second.</summary>
    internal sealed class AutoStager
    {
        private const float Cooldown = 1f;

        private readonly Vessel _vessel;
        private float _next;
        private string? _held;

        public AutoStager(Vessel vessel)
        {
            _vessel = vessel;
        }

        public int Stagings { get; private set; }

        /// <summary>Stages if it should. True when the vessel has no thrust and nothing it may stage.</summary>
        /// <param name="chutesArmed">A landing has opened its parachutes, so a stage holding them may fire.</param>
        public bool Tick(bool chutesArmed = false)
        {
            if (Time.time < _next)
            {
                return false;
            }
            StagingView view = StagingView.Of(VesselTasks.StageParts(_vessel), StageManager.CurrentStage);
            string? held = StagingRule.Held(view, chutesArmed);
            if (held != _held && held != null)
            {
                PluginFiles.Log("auto-staging " + _vessel.vesselName + " holds at stage " + StageManager.CurrentStage + ": " + held);
            }
            _held = held;
            if (!StagingRule.ShouldStage(view, chutesArmed))
            {
                return view.BurningEngines == 0 && (held != null || !view.EnginesRemain);
            }
            if (StageManager.CurrentStage <= 0)
            {
                return view.BurningEngines == 0;
            }
            _next = Time.time + Cooldown;
            Stagings++;
            PluginFiles.Log("auto-staging " + _vessel.vesselName + " into stage " + (StageManager.CurrentStage - 1) + ": burning " + view.BurningEngines + ", spent dropped " + view.DroppedSpent + ", empty tanks " + view.DropsEmptyTanks + ", clamps " + view.ReleasesClamps);
            StageManager.ActivateNextStage();
            return false;
        }
    }
}

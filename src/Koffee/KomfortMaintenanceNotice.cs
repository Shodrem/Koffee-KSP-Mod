using System;
using System.Collections.Generic;
using System.Globalization;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames,
        GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    public sealed class KomfortMaintenanceNotice : ScenarioModule
    {
        private static Game? game;
        private static readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
        private sealed class Pending
        {
            internal Vessel Vessel = null!;
            internal uint PartId;
            internal int Index;
        }

        internal static void Observe(Vessel vessel, KomfortMaintenance.Unit unit)
        {
            EnsureGame();
            if (unit.State == null || unit.State.InProgress || unit.State.LowAutonomyWarned || !unit.State.LowAutonomy) return;
            string key = vessel.id.ToString("N") + ":" + unit.PartId + ":" + unit.Index;
            pending[key] = new Pending { Vessel = vessel, PartId = unit.PartId, Index = unit.Index };
        }

        private static void EnsureGame()
        {
            if (ReferenceEquals(game, HighLogic.CurrentGame)) return;
            pending.Clear(); game = HighLogic.CurrentGame;
        }

        public void Update() => Deliver();

        internal static void Deliver()
        {
            EnsureGame();
            if (pending.Count == 0 || game == null || !EngineeringSettingsLoader.Current.Enabled ||
                !KomfortRuntime.OptionsAt(Planetarium.GetUniversalTime()).Enabled ||
                (HighLogic.LoadedScene != GameScenes.FLIGHT && HighLogic.LoadedScene != GameScenes.SPACECENTER &&
                 HighLogic.LoadedScene != GameScenes.TRACKSTATION)) return;
            // Deliver outside the simulation pass: SetRate invokes stock callbacks synchronously.
            foreach (var pair in new List<KeyValuePair<string, Pending>>(pending))
            {
                var notice = pair.Value;
                KomfortMaintenance.Unit? found = null;
                foreach (var unit in KomfortMaintenance.Capture(notice.Vessel))
                    if (unit.PartId == notice.PartId && unit.Index == notice.Index) { found = unit; break; }
                var state = found?.State;
                if (state == null || state.InProgress || state.LowAutonomyWarned || !state.LowAutonomy)
                { pending.Remove(pair.Key); continue; }
                try
                {
                    if (TimeWarp.CurrentRateIndex != 0 || TimeWarp.CurrentRate > 1)
                    {
                        TimeWarp.SetRate(0, true);
                        // Native warp callbacks may replace proto snapshots. Do not write stale hours back.
                        found = null;
                        foreach (var refreshed in KomfortMaintenance.Capture(notice.Vessel))
                            if (refreshed.PartId == notice.PartId && refreshed.Index == notice.Index) { found = refreshed; break; }
                        state = found?.State;
                        if (state == null || state.InProgress || state.LowAutonomyWarned || !state.LowAutonomy)
                        { pending.Remove(pair.Key); continue; }
                    }
                    string equipment = Localizer.Format(found!.Definition.part?.partInfo?.title ?? found.Definition.service);
                    PopupDialog.SpawnPopupDialog(new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                        new MultiOptionDialog("KoffeeServiceAutonomy", Localizer.Format("#Koffee_EvaMaintenance_LowBody",
                            notice.Vessel.vesselName, equipment, state.RemainingHours.ToString("0.##", CultureInfo.CurrentCulture)),
                            Localizer.Format("#Koffee_EvaMaintenance_LowTitle"), HighLogic.UISkin,
                            new DialogGUIButton(Localizer.Format("#Koffee_EvaMaintenance_Close"), () => { })), false, HighLogic.UISkin, true);
                    state.LowAutonomyWarned = true; found.Store(state);
                    pending.Remove(pair.Key);
                }
                catch (Exception error) { Debug.LogError("[Koffee] Service autonomy warning remains pending: " + error); }
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node); pending.Clear(); game = HighLogic.CurrentGame;
        }
    }
}

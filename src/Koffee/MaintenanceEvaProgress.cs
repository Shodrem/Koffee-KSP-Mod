using System;
using System.Collections.Generic;
using System.Globalization;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class MaintenanceEvaProgress
    {
        private static readonly List<ModuleKomfortService> services = new List<ModuleKomfortService>();
        private static Vessel? engineerVessel;
        private static Game? game;
        private static float nextScan;
        private static GUIStyle? label;

        internal static void Draw()
        {
            var eva = FlightGlobals.ActiveVessel;
            if (!HighLogic.LoadedSceneIsFlight || MapView.MapIsEnabled || eva?.loaded != true || !eva.isEVA ||
                eva.packed || eva.evaController == null || FlightCamera.fetch?.mainCamera == null) return;
            var crew = eva.GetVesselCrew();
            string name = crew.Count > 0 ? crew[0].name : "";
            if (!ReferenceEquals(eva, engineerVessel) || !ReferenceEquals(game, HighLogic.CurrentGame) || Time.realtimeSinceStartup >= nextScan)
            {
                services.Clear(); engineerVessel = eva; game = HighLogic.CurrentGame;
                nextScan = Time.realtimeSinceStartup + .1f;
                foreach (var vessel in FlightGlobals.Vessels)
                    if (vessel?.loaded == true && !vessel.isEVA)
                        foreach (var part in vessel.parts)
                            if (part != null && part.State != PartStates.DEAD)
                                foreach (var module in part.Modules)
                                    if (module is ModuleKomfortService service && service.Maintainable &&
                                        service.Maintenance?.InProgress == true && service.Maintenance.Engineer == name) services.Add(service);
            }
            Vector3 point = FlightCamera.fetch.mainCamera.WorldToScreenPoint(
                eva.evaController.part.transform.position + eva.evaController.part.transform.up * 1.5f);
            if (point.z <= 0 || point.x < 0 || point.x > Screen.width || point.y < 0 || point.y > Screen.height) return;
            if (label == null) label = new GUIStyle(GUI.skin.label) {
                alignment = TextAnchor.MiddleCenter, fontSize = 14, wordWrap = false };
            label.normal.textColor = Color.white;
            float width = Math.Min(360, Screen.width - 16);
            float x = Mathf.Clamp(point.x - width / 2, 8, Screen.width - width - 8);
            float y = Mathf.Clamp(Screen.height - point.y - 80, 8, Screen.height - 88);
            Color previous = GUI.color;
            try
            {
                foreach (var service in services)
                {
                    var state = service != null ? service.Maintenance : null;
                    if (state?.InProgress != true || state.Engineer != name || service!.part.State == PartStates.DEAD) continue;
                    string status = state.Paused ? "#Koffee_EvaMaintenance_ProgressPaused" :
                        MaintenanceEva.Nearby(service.part, name) == null ? "#Koffee_EvaMaintenance_Approach" :
                        !EngineeringSettingsLoader.Current.Enabled || !KomfortRuntime.OptionsAt(Planetarium.GetUniversalTime()).Enabled ?
                            "#Koffee_Maintenance_Disabled" : "#Koffee_EvaMaintenance_ProgressWorking";
                    GUI.color = new Color(.05f, .07f, .09f, .95f);
                    GUI.DrawTexture(new Rect(x, y, width, 76), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(x + 4, y + 2, width - 8, 24), Localizer.Format(status), label);
                    GUI.Label(new Rect(x + 4, y + 25, width - 8, 20),
                        Localizer.Format(service.part.partInfo?.title ?? service.service), label);
                    GUI.color = new Color(.2f, .23f, .26f, 1);
                    GUI.DrawTexture(new Rect(x + 8, y + 49, width - 16, 19), Texture2D.whiteTexture);
                    GUI.color = status == "#Koffee_EvaMaintenance_ProgressWorking" ? new Color(.15f, .6f, .35f, 1) : new Color(.7f, .45f, .1f, 1);
                    GUI.DrawTexture(new Rect(x + 8, y + 49, (width - 16) * (float)state.Progress, 19), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(x + 8, y + 48, width - 16, 21), (state.Progress * 100).ToString("0", CultureInfo.CurrentCulture) + " %", label);
                    y += 82;
                }
            }
            finally { GUI.color = previous; }
        }
    }
}

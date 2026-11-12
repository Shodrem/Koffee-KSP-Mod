using System;
using System.Globalization;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal sealed class KoffeeSpaceCenterView
    {
        internal const float Width = 640;
        private readonly KoffeeRosterNavigation navigation = new KoffeeRosterNavigation();
        private readonly KoffeeRosterFlight flight = new KoffeeRosterFlight();
        private KoffeeRosterSnapshot snapshot = KoffeeRosterSnapshot.Empty;
        private Vector2 scroll;
        private GUISkin? skin;
        private GUIStyle? label, button;
        private Guid? pendingVessel;
        private Game? pendingGame;
        private string error = "";
        private string selectedCrew = "";

        internal void Refresh()
        {
            snapshot = KoffeeRosterSnapshot.Capture();
            if (navigation.Reconcile(snapshot)) { scroll = Vector2.zero; error = ""; selectedCrew = ""; }
        }

        internal void CancelPending() { pendingVessel = null; pendingGame = null; }

        internal bool ProcessFlight(Action releaseWindow)
        {
            if (!pendingVessel.HasValue) return false;
            Guid id = pendingVessel.Value;
            Game? game = pendingGame;
            CancelPending();
            if (flight.TryStart(game, id, releaseWindow, out string key)) return true;
            Refresh();
            if (!string.IsNullOrEmpty(key)) error = Localizer.Format(key);
            return false;
        }

        internal void Draw()
        {
            if (!ReferenceEquals(skin, GUI.skin))
            {
                skin = GUI.skin;
                label = new GUIStyle(skin.label) { wordWrap = true };
                button = new GUIStyle(skin.button) { wordWrap = true, alignment = TextAnchor.MiddleLeft };
            }
            GUILayout.Label(Localizer.Format("#Koffee_Crew_RosterTitle"), label!);
            GUILayout.Label(Localizer.Format("#Koffee_Crew_KomfortHelp"), label!);
            if (!string.IsNullOrEmpty(error)) GUILayout.Label(error, label!);
            scroll = GUILayout.BeginScrollView(scroll, false, true,
                GUILayout.Height(Math.Min(520, Math.Max(100, Screen.height / KoffeeStatusWindow.Scale - 170))));
            foreach (KoffeeRosterGroup group in snapshot.Groups)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                bool open = navigation.IsOpen(group.Key);
                string name = group.Kind == KoffeeRosterGroupKind.Vessel ?
                    (group.IsEva ? Localizer.Format("#Koffee_Crew_Eva", group.Name) : group.Name) :
                    Localizer.Format("#Koffee_Crew_Group_" + group.Kind);
                if (!string.IsNullOrEmpty(group.DeadlyWarning)) name += " — " + group.DeadlyWarning;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button((open ? "▼ " : "▶ ") + name + " (" +
                    group.Crew.Count.ToString(CultureInfo.InvariantCulture) + ")", button!, GUILayout.ExpandWidth(true)))
                    navigation.Toggle(group.Key);
                if (group.Kind == KoffeeRosterGroupKind.Vessel)
                {
                    bool enabled = GUI.enabled;
                    GUI.enabled = enabled && !pendingVessel.HasValue && !flight.Starting;
                    if (GUILayout.Button(Localizer.Format("#Koffee_Crew_GoToVessel"), GUILayout.Width(110)))
                    { pendingVessel = group.VesselId; pendingGame = snapshot.Game; }
                    GUI.enabled = enabled;
                }
                GUILayout.EndHorizontal();
                // Always outside the fold: the common value belongs to the ship.
                if (group.Kind == KoffeeRosterGroupKind.Vessel)
                {
                    KoffeeStatusBars.Komfort(group.Komfort, label!);
                    KoffeeStatusBars.Air(group.AirFraction, group.AirLastKnown, label!);
                }
                else if (group.Kind == KoffeeRosterGroupKind.Available)
                    GUILayout.Label(Localizer.Format("#Koffee_Crew_NotApplicable"), label!);
                if (navigation.IsOpen(group.Key))
                {
                    if (group.Crew.Count == 0) GUILayout.Label(Localizer.Format("#Koffee_Crew_EmptyGroup"), label!);
                    foreach (KoffeeCrewStatus crew in group.Crew)
                    {
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button((selectedCrew == crew.Name ? "▼ " : "▶ ") + crew.Name, button!, GUILayout.Width(200)))
                            selectedCrew = selectedCrew == crew.Name ? "" : crew.Name;
                        GUILayout.BeginVertical(GUILayout.MinWidth(165), GUILayout.ExpandWidth(true));
                        GUILayout.Label(KoffeeCrewDisplay.ProfessionLevel(crew), label!);
                        KoffeeStatusBars.Health(crew);
                        GUILayout.EndVertical();
                        GUILayout.EndHorizontal();
                        if (selectedCrew == crew.Name)
                        {
                            GUILayout.Label(KoffeeCrewDisplay.Row(crew), label!);
                            var hp = DeadlyScenario.Health(crew.Name);
                            if (hp != null && crew.HealthFraction.HasValue) GUILayout.Label(Localizer.Format("#Koffee_Health_" + hp.Status), label!);
                            GUILayout.Label(KoffeeCrewDisplay.State(crew.RosterState), label!);
                            if (KerbalSalaryScenario.TryRead(crew.Name, out double salary, out double rate, out int departureLevel))
                                GUILayout.Label(Localizer.Format("#Koffee_Salary_Accrued",
                                    salary.ToString("N2", CultureInfo.CurrentCulture), rate.ToString("N2", CultureInfo.CurrentCulture),
                                    departureLevel.ToString(CultureInfo.InvariantCulture)), label!);
                            if (crew.IsEngineer)
                                foreach (string line in EngineeringDisplay.Profile(crew.Name)) GUILayout.Label(line, label!);
                            if (crew.Scientist != null)
                            {
                                GUILayout.Label(KoffeeResearchDisplay.Specialties(crew.Scientist.Profile), label!);
                                if (crew.Scientist.Profile != null)
                                    foreach (var category in ScienceCategories.All)
                                        GUILayout.Label(KoffeeResearchDisplay.Category(category) + ": " + crew.Scientist.Profile.Score(category) + " / 10", label!);
                            }
                        }
                        if (group.Kind == KoffeeRosterGroupKind.Other || group.Kind == KoffeeRosterGroupKind.Unknown)
                            GUILayout.Label(KoffeeCrewDisplay.State(crew.RosterState), label!);
                    }
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, Width, 24));
        }
    }
}

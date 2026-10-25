using System;
using System.Globalization;
using System.Linq;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Owned only by a flight window. No editor styles, scroll positions or selections are shared.
    internal sealed class KoffeeFlightStatusView
    {
        internal const float Width = 540;
        private readonly KoffeeFlightNavigation navigation = new KoffeeFlightNavigation();
        private readonly Vector2[] pageScroll = new Vector2[3];
        private Vector2 rosterScroll;
        private KoffeeFlightExperiment[] experiments = Array.Empty<KoffeeFlightExperiment>();
        private GUISkin? styledSkin;
        private GUIStyle? label, heading, button;
        private ModuleKoffeeExperiment? scrollToExperiment;
        private bool scrollToSupply;

        internal void Refresh(KoffeeStatusSnapshot snapshot)
        {
            if (navigation.Reconcile(snapshot))
            {
                for (int i = 0; i < pageScroll.Length; i++) pageScroll[i] = Vector2.zero;
                rosterScroll = Vector2.zero;
                scrollToExperiment = null;
                scrollToSupply = false;
            }
            if (!snapshot.Experiments.Any(e => ReferenceEquals(e.Module, scrollToExperiment))) scrollToExperiment = null;
            experiments = snapshot.Experiments.Select(e => new KoffeeFlightExperiment(e)).ToArray();
        }

        internal void Draw(KoffeeStatusSnapshot snapshot)
        {
            EnsureStyles();
            GUILayout.BeginHorizontal();
            Tab(KoffeeFlightTab.Summary, "#Koffee_Flight_Summary");
            Tab(KoffeeFlightTab.Experiments, "#Koffee_Flight_Experiments");
            Tab(KoffeeFlightTab.Scientists, "#Koffee_Flight_Scientists");
            GUILayout.EndHorizontal();
            // Capture the page: an attention button can change tabs during this GUI event.
            int page = (int)navigation.Tab;
            pageScroll[page] = GUILayout.BeginScrollView(pageScroll[page], false, true,
                GUILayout.Height(Math.Min(520, Math.Max(100, Screen.height / KoffeeStatusWindow.Scale - 170))));
            if (page == (int)KoffeeFlightTab.Scientists) DrawScientists(snapshot);
            else if (!snapshot.HasActiveVessel) Text(Localizer.Format("#Koffee_UI_NoVessel"));
            else if (page == (int)KoffeeFlightTab.Summary) DrawSummary(snapshot);
            else DrawExperiments();
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, Width, 24));
        }

        private void Tab(KoffeeFlightTab tab, string key)
        {
            if (GUILayout.Toggle(navigation.Tab == tab, Localizer.Format(key), GUI.skin.button)) navigation.Tab = tab;
        }

        private void DrawSummary(KoffeeStatusSnapshot snapshot)
        {
            Text(snapshot.VesselName, true);
            Text(KoffeeFlightExperiment.Body(snapshot.Body) + " / " + KoffeeFlightExperiment.Situation(snapshot.Situation));
            Text(Localizer.Format("#Koffee_UI_Aboard", snapshot.AvailableCups.ToString("0", CultureInfo.InvariantCulture)), true);
            Text(Localizer.Format("#Koffee_Flight_Counts",
                Count(KoffeeFlightExperimentGroup.Running), Count(KoffeeFlightExperimentGroup.Stopped),
                Count(KoffeeFlightExperimentGroup.Completed)));
            if (experiments.Any(e => e.Group == KoffeeFlightExperimentGroup.Ready || e.Group == KoffeeFlightExperimentGroup.Unknown))
                Text(Localizer.Format("#Koffee_Flight_OtherCounts", Count(KoffeeFlightExperimentGroup.Ready),
                    Count(KoffeeFlightExperimentGroup.Unknown)));
            if (experiments.Length == 0) Text(Localizer.Format("#Koffee_Flight_NoExperiments"));

            Text(Localizer.Format("#Koffee_Flight_Attention"), true);
            bool attention = false;
            foreach (KoffeeFlightExperiment experiment in experiments.Where(e => !string.IsNullOrEmpty(e.Attention)))
            {
                attention = true;
                if (GUILayout.Button(experiment.Title + " — " + experiment.Attention, button!))
                {
                    navigation.SelectExperiment(experiment.Runtime.Module, true);
                    scrollToExperiment = experiment.Runtime.Module;
                }
            }
            // A forecast is global; navigate to its own detail without choosing a container.
            if (HasForecastWork(snapshot) && snapshot.Supply == null)
            {
                attention = true;
                if (GUILayout.Button(Localizer.Format("#Koffee_Flight_ForecastUnavailable"), button!))
                {
                    navigation.SupplyOpen = true;
                    scrollToSupply = true;
                }
            }
            if (!attention) Text(Localizer.Format("#Koffee_Flight_NoAttention"));

            navigation.SupplyOpen = Fold(navigation.SupplyOpen, Localizer.Format("#Koffee_Flight_Forecast"));
            if (scrollToSupply && Event.current.type == EventType.Repaint)
            {
                pageScroll[(int)KoffeeFlightTab.Summary].y = Math.Max(0, GUILayoutUtility.GetLastRect().y);
                scrollToSupply = false;
            }
            if (navigation.SupplyOpen) DrawSupply(snapshot);
            navigation.PersonnelOpen = Fold(navigation.PersonnelOpen, Localizer.Format("#Koffee_Flight_Personnel"));
            if (navigation.PersonnelOpen) DrawPersonnel(snapshot);
        }

        private string Count(KoffeeFlightExperimentGroup group) =>
            experiments.Count(e => e.Group == group).ToString(CultureInfo.InvariantCulture);

        private static bool HasForecastWork(KoffeeStatusSnapshot snapshot) =>
            snapshot.Experiments.Any(e => e.State?.Status == ExperimentContainerStatus.Running && !e.WorkComplete);

        private void DrawSupply(KoffeeStatusSnapshot snapshot)
        {
            if (!HasForecastWork(snapshot)) Text(Localizer.Format("#Koffee_Flight_NoForecastWork"));
            else if (snapshot.Supply == null) Text(Localizer.Format("#Koffee_Flight_ForecastUnavailable"));
            else
            {
                Text(Localizer.Format("#Koffee_Research_TotalSupply",
                    (snapshot.Supply.TotalPurchases * snapshot.ActivationCost).ToString("0", CultureInfo.InvariantCulture)), true);
                foreach (var scientist in snapshot.Supply.ScientistPurchases.OrderBy(s => s.Key, StringComparer.Ordinal))
                {
                    double coverage = snapshot.Crew.FirstOrDefault(c => c.Name == scientist.Key).RemainingSeconds;
                    Text(Localizer.Format("#Koffee_Research_ScientistSupply", scientist.Key,
                        (scientist.Value * snapshot.ActivationCost).ToString("0", CultureInfo.InvariantCulture), Duration(coverage)));
                }
                Text(Localizer.Format("#Koffee_Research_EngineerSupply", snapshot.EngineerName,
                    (snapshot.Supply.EngineerPurchases * snapshot.ActivationCost).ToString("0", CultureInfo.InvariantCulture),
                    Duration(snapshot.EngineerCoverage)));
            }
            Text(Localizer.Format("#Koffee_Flight_ForecastScope"));
            Text(Localizer.Format("#Koffee_Research_SupplyAssumption"));
        }

        private void DrawPersonnel(KoffeeStatusSnapshot snapshot)
        {
            if (snapshot.Crew.Count == 0) Text(Localizer.Format("#Koffee_UI_NoCrew"));
            foreach (KoffeeCrewStatus crew in snapshot.Crew)
            {
                string status = Localizer.Format("#Koffee_UI_NotKaffeinated");
                if (crew.IsKaffeinated)
                {
                    string key = LabDisplay.TimeKey(crew.RemainingSeconds, out string hours, out string minutes);
                    status = Localizer.Format("#Koffee_UI_Kaffeinated") + " — " + Localizer.Format(key, hours, minutes);
                }
                Text(crew.Name + " — " + crew.Profession + " — " + status);
            }
            Text(Localizer.Format("#Koffee_Flight_ManagerEngineer", string.IsNullOrEmpty(snapshot.EngineerName) ?
                Localizer.Format("#Koffee_Station_None") : snapshot.EngineerName));
            Text(Localizer.Format("#Koffee_Station_Labs"), true);
            if (snapshot.Laboratories.Count == 0) Text(Localizer.Format("#Koffee_Flight_NoLaboratories"));
            foreach (KoffeeLabStationStatus lab in snapshot.Laboratories)
                Text(lab.PartTitle + " — " + Localizer.Format("#Koffee_Station_Slots", lab.Slots.ToString(CultureInfo.InvariantCulture)));
        }

        private void DrawExperiments()
        {
            if (experiments.Length == 0) Text(Localizer.Format("#Koffee_Flight_NoExperiments"));
            foreach (KoffeeFlightExperiment experiment in experiments)
            {
                bool selected = ReferenceEquals(navigation.SelectedExperiment, experiment.Runtime.Module);
                GUILayout.BeginVertical(GUI.skin.box);
                if (GUILayout.Button((selected ? "▼ " : "▶ ") + experiment.Title + " — " + experiment.Status, button!))
                    navigation.SelectExperiment(experiment.Runtime.Module);
                Text(experiment.Progress.HasValue ? Localizer.Format("#Koffee_Flight_Progress",
                    (experiment.Progress.Value * 100).ToString("0.#", CultureInfo.CurrentCulture)) :
                    Localizer.Format("#Koffee_Flight_ProgressUnknown"));
                if (experiment.Progress.HasValue) DrawProgress(experiment.Progress.Value);
                Text(Scientist(experiment.Runtime));
                if (experiment.HasPendingResult) Text(Localizer.Format("#Koffee_Flight_ResultPending"));
                if (experiment.Group != KoffeeFlightExperimentGroup.Completed) Text(WorkRemaining(experiment));
                if (selected) DrawExperimentDetail(experiment);
                GUILayout.EndVertical();
                if (ReferenceEquals(scrollToExperiment, experiment.Runtime.Module) && Event.current.type == EventType.Repaint)
                {
                    // Use layout coordinates, not a row index: long lists and wrapped titles can vary in height.
                    pageScroll[(int)KoffeeFlightTab.Experiments].y = Math.Max(0, GUILayoutUtility.GetLastRect().y);
                    scrollToExperiment = null;
                }
            }
        }

        private void DrawExperimentDetail(KoffeeFlightExperiment experiment)
        {
            StationExperimentRuntimeSnapshot runtime = experiment.Runtime;
            ExperimentContainerState? state = runtime.State;
            Text(Localizer.Format("#Koffee_Flight_AssignmentContext"), true);
            if (state?.HasAssignedLaboratory == true)
            {
                ModuleKoffeeLab? lab = runtime.AssignedLaboratory;
                Text(Localizer.Format("#Koffee_Station_LabSlot", lab == null ?
                    Localizer.Format("#Koffee_Station_LabUnavailable", state.AssignedLaboratoryPersistentId.ToString(CultureInfo.InvariantCulture)) :
                    lab.part.partInfo?.title ?? lab.part.partInfo?.name ?? lab.part.name,
                    state.HasSlotBinding ? (state.AssignedLaboratorySlot + 1).ToString(CultureInfo.InvariantCulture) :
                        KoffeeResearchDisplay.Unknown));
            }
            else Text(Localizer.Format("#Koffee_Flight_NoAssignedLaboratory"));
            string body = string.IsNullOrEmpty(state?.ResultBody) ? runtime.Body : state!.ResultBody;
            string situation = string.IsNullOrEmpty(state?.ResultSituation) ? runtime.Situation : state!.ResultSituation;
            Text(Localizer.Format("#Koffee_Flight_StudyContext", KoffeeFlightExperiment.Body(body), KoffeeFlightExperiment.Situation(situation)));
            if (experiment.Group != KoffeeFlightExperimentGroup.Completed)
            {
                Text(WorkRemaining(experiment));
                Text(Localizer.Format("#Koffee_Research_TimeAssumption"));
            }
            if (!string.IsNullOrEmpty(experiment.Attention)) Text(experiment.Attention);

            navigation.ResearchOpen = Fold(navigation.ResearchOpen, Localizer.Format("#Koffee_Flight_Research"));
            if (navigation.ResearchOpen)
            {
                StationResearchPresentation research = runtime.Research;
                Text(Localizer.Format("#Koffee_Research_CategoryLine", research.Category.HasValue ?
                    KoffeeResearchDisplay.Category(research.Category.Value) : KoffeeResearchDisplay.Unknown));
                Text(research.Profile == null ? Localizer.Format("#Koffee_Research_ProfileUnavailable") :
                    Localizer.Format("#Koffee_Research_Worker", research.Affinity.ToString(CultureInfo.InvariantCulture),
                        research.Level.ToString(CultureInfo.InvariantCulture)));
                Text(Localizer.Format("#Koffee_Research_RateLine", research.Profile == null ? KoffeeResearchDisplay.Unknown :
                    research.RatePerMinute.ToString("0.0", CultureInfo.CurrentCulture)));
                Text(Localizer.Format("#Koffee_Flight_Work", experiment.Progress.HasValue ?
                    runtime.AccumulatedPoints.ToString("0.##", CultureInfo.CurrentCulture) : KoffeeResearchDisplay.Unknown,
                    experiment.Progress.HasValue ? runtime.RequiredPoints.ToString("0.##", CultureInfo.CurrentCulture) : KoffeeResearchDisplay.Unknown));
            }
            navigation.ConditionsOpen = Fold(navigation.ConditionsOpen, Localizer.Format("#Koffee_Flight_Conditions"));
            if (navigation.ConditionsOpen)
            {
                Text(Localizer.Format("#Koffee_Equipment_Required") + ": " + StationExperimentEquipment.Description(runtime.Definition));
                Text(Localizer.Format("#Koffee_Environment_Bodies") + ": " + StationExperimentEnvironment.Bodies(runtime.Definition));
                var situations = StationExperimentRuntime.ValidSituations(runtime.Module.experimentID, runtime.Module.part);
                Text(Localizer.Format("#Koffee_Station_ValidSituations", situations.Count == 0 ?
                    KoffeeResearchDisplay.Unknown : string.Join(", ", situations.Select(KoffeeFlightExperiment.Situation))));
                Text(Localizer.Format("#Koffee_Environment_Atmosphere") + ": " + StationExperimentEnvironment.Atmosphere(runtime.Module.experimentID));
                if (state?.HasContextBinding == true)
                    Text(Localizer.Format("#Koffee_Environment_Context") + ": " + StationExperimentEnvironment.Context(state));
                if (state?.HasSurfaceBinding == true)
                    Text(Localizer.Format("#Koffee_Environment_Surface") + ": " + StationExperimentEnvironment.Surface(state));
            }
        }

        private static string Scientist(StationExperimentRuntimeSnapshot experiment) =>
            Localizer.Format("#Koffee_Station_Scientist", experiment.State?.HasAssignment != true ?
                Localizer.Format("#Koffee_Station_None") : string.IsNullOrEmpty(experiment.State.AssignedKerbalDisplayName) ?
                    experiment.State.AssignedKerbalName : experiment.State.AssignedKerbalDisplayName);

        private static string WorkRemaining(KoffeeFlightExperiment experiment) =>
            Localizer.Format("#Koffee_Flight_WorkRemaining", experiment.RemainingSeconds.HasValue ?
                Duration(experiment.RemainingSeconds.Value) : KoffeeResearchDisplay.Unknown);

        private static void DrawProgress(double fraction)
        {
            float height = Math.Max(10, GUI.skin.horizontalScrollbar.fixedHeight);
            Rect track = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
            GUI.Box(track, GUIContent.none, GUI.skin.horizontalScrollbar);
            // A scrollbar thumb has end caps/grips that collapse into a blob at tiny widths.
            // Draw a flat fill inside the native track, rounded down to whole screen pixels.
            float scale = KoffeeStatusWindow.Scale;
            float fillWidth = Mathf.Floor(Math.Max(0, track.width - 4) * (float)fraction * scale) / scale;
            if (fillWidth <= 0) return;
            Color previousColor = GUI.color;
            try
            {
                GUI.color = previousColor * GUI.skin.label.normal.textColor;
                GUI.DrawTexture(new Rect(track.x + 2, track.y + 2, fillWidth, track.height - 4), Texture2D.whiteTexture);
            }
            finally { GUI.color = previousColor; }
        }

        private void DrawScientists(KoffeeStatusSnapshot snapshot)
        {
            if (snapshot.Scientists.Count == 0) { Text(Localizer.Format("#Koffee_Research_NoScientists")); return; }
            rosterScroll = GUILayout.BeginScrollView(rosterScroll, false, true, GUILayout.Height(140));
            foreach (KoffeeScientistStatus scientist in snapshot.Scientists)
                if (GUILayout.Toggle(scientist.Name == navigation.SelectedScientist,
                    scientist.Name + " — " + KoffeeResearchDisplay.RosterStatus(scientist), button!))
                {
                    if (navigation.SelectedScientist != scientist.Name) navigation.AffinitiesOpen = false;
                    navigation.SelectedScientist = scientist.Name;
                }
            GUILayout.EndScrollView();
            KoffeeScientistStatus? selected = snapshot.Scientists.FirstOrDefault(s => s.Name == navigation.SelectedScientist);
            if (selected == null) return;
            Text(Localizer.Format("#Koffee_Research_ScientistLevel", selected.Name, selected.Level.ToString(CultureInfo.InvariantCulture)), true);
            Text(KoffeeResearchDisplay.RosterStatus(selected));
            Text(KoffeeResearchDisplay.Specialties(selected.Profile));
            navigation.AffinitiesOpen = Fold(navigation.AffinitiesOpen, Localizer.Format("#Koffee_Flight_Affinities"));
            if (navigation.AffinitiesOpen && selected.Profile != null)
                foreach (ScienceCategory category in ScienceCategories.All)
                    Text(KoffeeResearchDisplay.Category(category) + ": " + selected.Profile.Score(category).ToString(CultureInfo.InvariantCulture) + " / 10");
        }

        private bool Fold(bool open, string title)
        {
            GUILayout.Space(6);
            return GUILayout.Toggle(open, (open ? "▼ " : "▶ ") + title, button!);
        }

        private void Text(string value, bool strong = false) => GUILayout.Label(value, strong ? heading! : label!);
        private static string Duration(double seconds) => KerbalWorkState.FormatDuration(Math.Max(0, seconds));

        private void EnsureStyles()
        {
            if (ReferenceEquals(styledSkin, GUI.skin)) return;
            styledSkin = GUI.skin;
            label = new GUIStyle(GUI.skin.label) { wordWrap = true };
            heading = new GUIStyle(label) { fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { wordWrap = true, alignment = TextAnchor.MiddleLeft };
        }
    }
}

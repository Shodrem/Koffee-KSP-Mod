using System;
using System.Globalization;
using System.Linq;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal sealed class KoffeeStatusWindow
    {
        private const int WindowId = 1764219;
        private const float WindowWidth = 460f;
        private const float RefreshInterval = 0.5f;
        private static Vector2 rememberedPosition = new Vector2(-1, -1);
        private static Vector2 rememberedFlightPosition = new Vector2(-1, -1);
        private readonly KoffeeFlightStatusView? flightView = HighLogic.LoadedSceneIsFlight ? new KoffeeFlightStatusView() : null;
        private static Vector2 rememberedRosterPosition = new Vector2(-1, -1);
        private readonly KoffeeSpaceCenterView? rosterView = HighLogic.LoadedScene == GameScenes.SPACECENTER ? new KoffeeSpaceCenterView() : null;
        private float CurrentWidth => rosterView != null ? KoffeeSpaceCenterView.Width :
            flightView == null ? WindowWidth : KoffeeFlightStatusView.Width;

        private Rect windowRect = new Rect(0, 80, WindowWidth, 120);
        private Vector2 scrollPosition;
        private Vector2 rosterScroll;
        private Vector2 scientistPageScroll;
        private int selectedTab = HighLogic.LoadedSceneIsEditor ? 1 : 0;
        private string selectedScientist = "";
        private KoffeeStatusSnapshot snapshot = KoffeeStatusViewModel.Capture();
        private GUIStyle? rightLabel;
        private GUIStyle? strongLabel;
        private GUIStyle? wrappedLabel;
        private GUIStyle? rosterButton;
        private GUISkin? styledSkin;
        private float nextRefresh;
        private Guid capturedVesselId;
        private Game? capturedGame;

        internal bool IsOpen { get; private set; }
        internal Rect WindowRect => windowRect;

        internal void Open()
        {
            IsOpen = true;
            Invalidate();
            RefreshIfNeeded();
        }

        internal void Close() { IsOpen = false; rosterView?.CancelPending(); }

        internal void CancelPendingFlight() => rosterView?.CancelPending();

        internal bool ProcessFlight(Action releaseWindow) => rosterView?.ProcessFlight(releaseWindow) == true;

        internal void Invalidate() => nextRefresh = 0;

        internal void RefreshIfNeeded()
        {
            Guid activeId = flightView != null ? FlightGlobals.ActiveVessel?.id ?? Guid.Empty : Guid.Empty;
            bool vesselChanged = flightView != null && capturedVesselId != activeId;
            bool gameChanged = !ReferenceEquals(capturedGame, HighLogic.CurrentGame);
            if (!IsOpen || (!vesselChanged && !gameChanged && Time.realtimeSinceStartup < nextRefresh)) return;
            if (rosterView != null) rosterView.Refresh();
            else
            {
                snapshot = KoffeeStatusViewModel.Capture();
                flightView?.Refresh(snapshot);
            }
            if (gameChanged && HighLogic.LoadedSceneIsEditor)
            { selectedScientist = ""; rosterScroll = Vector2.zero; scientistPageScroll = Vector2.zero; }
            capturedVesselId = activeId;
            capturedGame = HighLogic.CurrentGame;
            nextRefresh = Time.realtimeSinceStartup + RefreshInterval;
        }

        internal void Draw()
        {
            GUISkin skin = HighLogic.Skin;
            if (!IsOpen || skin == null) return;
            if (flightView != null) RefreshIfNeeded();
            EnsureStyles(skin);

            GUISkin previousSkin = GUI.skin;
            Matrix4x4 previousMatrix = GUI.matrix;
            try
            {
                GUI.skin = skin;
                float scale = Scale;
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                    new Vector3(scale, scale, 1));
                EnsureInitialPosition(scale);
                windowRect = GUILayout.Window(WindowId, windowRect, DrawContents,
                    Localizer.Format("#Koffee_UI_Title"), skin.window,
                    GUILayout.Width(CurrentWidth));
                ClampToScreen(scale);
                if (rosterView != null) rememberedRosterPosition = new Vector2(windowRect.x, windowRect.y);
                else if (flightView == null) rememberedPosition = new Vector2(windowRect.x, windowRect.y);
                else rememberedFlightPosition = new Vector2(windowRect.x, windowRect.y);
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.skin = previousSkin;
            }
        }

        internal static float Scale
        {
            get
            {
                float scale = GameSettings.UI_SCALE * GameSettings.UI_SCALE_APPS;
                return scale > 0 && !float.IsNaN(scale) && !float.IsInfinity(scale) ? scale : 1;
            }
        }

        private void DrawContents(int id)
        {
            if (rosterView != null)
            {
                rosterView.Draw();
                return;
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(!ScienceReportUI.Visible, ScienceReportUI.Text("Work"), GUI.skin.button)) ScienceReportUI.Visible = false;
            if (GUILayout.Toggle(ScienceReportUI.Visible, ScienceReportUI.Text("Title"), GUI.skin.button)) ScienceReportUI.Visible = true;
            GUILayout.EndHorizontal();
            if (ScienceReportUI.Visible)
            {
                ScienceReportUI.Draw();
                GUI.DragWindow(new Rect(0, 0, CurrentWidth, 24));
                return;
            }
            if (flightView != null)
            {
                flightView.Draw(snapshot);
                return;
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(selectedTab == 0, Localizer.Format("#Koffee_Research_VesselTab"), GUI.skin.button)) selectedTab = 0;
            if (GUILayout.Toggle(selectedTab == 1, Localizer.Format(HighLogic.LoadedSceneIsEditor ? "#Koffee_EditorCrew_Tab" : "#Koffee_Research_ScientistsTab"), GUI.skin.button)) selectedTab = 1;
            GUILayout.EndHorizontal();
            Vector2 pageScroll = selectedTab == 1 ? scientistPageScroll : scrollPosition;
            pageScroll = GUILayout.BeginScrollView(pageScroll, false, true,
                GUILayout.Height(Math.Min(520, Math.Max(100, Screen.height / Scale - 170))));
            if (selectedTab == 1) DrawScientists();
            else DrawVessel();
            GUILayout.EndScrollView();
            if (selectedTab == 1) scientistPageScroll = pageScroll;
            else scrollPosition = pageScroll;
            GUI.DragWindow(new Rect(0, 0, WindowWidth, 24));
        }

        private void DrawScientists()
        {
            if (HighLogic.LoadedSceneIsEditor) { DrawEditorCrew(); return; }
            if (snapshot.Scientists.Count == 0)
            {
                GUILayout.Label(Localizer.Format(HighLogic.LoadedSceneIsEditor ?
                    "#Koffee_Research_NoHiredScientists" : "#Koffee_Research_NoScientists"));
                return;
            }
            if (!snapshot.Scientists.Any(c => c.Name == selectedScientist))
                selectedScientist = snapshot.Scientists[0].Name;
            rosterScroll = GUILayout.BeginScrollView(rosterScroll, false, true, GUILayout.Height(140));
            foreach (KoffeeScientistStatus scientist in snapshot.Scientists)
                if (GUILayout.Toggle(scientist.Name == selectedScientist,
                    scientist.Name + " — " + KoffeeResearchDisplay.RosterStatus(scientist), rosterButton!))
                    selectedScientist = scientist.Name;
            GUILayout.EndScrollView();
            KoffeeScientistStatus selected = snapshot.Scientists.First(c => c.Name == selectedScientist);
            GUILayout.Label(Localizer.Format("#Koffee_Research_ScientistLevel", selected.Name,
                selected.Level.ToString(CultureInfo.InvariantCulture)), strongLabel!);
            GUILayout.Label(KoffeeResearchDisplay.RosterStatus(selected), wrappedLabel!);
            GUILayout.Label(KoffeeResearchDisplay.Specialties(selected.Profile), wrappedLabel!);
            if (selected.Profile == null) return;
            foreach (ScienceCategory category in ScienceCategories.All)
                GUILayout.Label(KoffeeResearchDisplay.Category(category) + ": " +
                    selected.Profile.Score(category).ToString(CultureInfo.InvariantCulture) + " / 10");
        }

        private void DrawEditorCrew()
        {
            var crew = snapshot.EditorCrew;
            if (crew.Count == 0) { GUILayout.Label(Localizer.Format("#Koffee_EditorCrew_Empty")); return; }
            if (!crew.Any(c => c.Crew.Name == selectedScientist)) selectedScientist = crew[0].Crew.Name;
            rosterScroll = GUILayout.BeginScrollView(rosterScroll, false, true, GUILayout.Height(140));
            foreach (var member in crew)
                if (GUILayout.Toggle(member.Crew.Name == selectedScientist, member.Crew.Name + " — " + member.Crew.Profession + " — " +
                    Localizer.Format(member.Assigned ? "#Koffee_EditorCrew_Assigned" : "#Koffee_EditorCrew_Available"), rosterButton!))
                    selectedScientist = member.Crew.Name;
            GUILayout.EndScrollView();
            var selected = crew.First(c => c.Crew.Name == selectedScientist);
            GUILayout.Label(Localizer.Format("#Koffee_EditorCrew_Level", selected.Crew.Name, selected.Crew.Profession,
                selected.Crew.Level.ToString(CultureInfo.InvariantCulture)), strongLabel!);
            GUILayout.Label(Localizer.Format(selected.Assigned ? "#Koffee_EditorCrew_Assigned" : "#Koffee_EditorCrew_Available"), wrappedLabel!);
            if (selected.Crew.IsEngineer)
            {
                foreach (string line in EngineeringDisplay.Profile(selected.Crew.Name)) GUILayout.Label(line, wrappedLabel!);
                foreach (string line in EngineeringDisplay.EditorPotential(selected.Crew.Name)) GUILayout.Label(line, wrappedLabel!);
            }
            else if (selected.Crew.Scientist is KoffeeScientistStatus scientist)
            {
                GUILayout.Label(KoffeeResearchDisplay.Specialties(scientist.Profile), wrappedLabel!);
                if (scientist.Profile != null)
                    foreach (ScienceCategory category in ScienceCategories.All)
                        GUILayout.Label(KoffeeResearchDisplay.Category(category) + ": " +
                            scientist.Profile.Score(category).ToString(CultureInfo.InvariantCulture) + " / 10");
            }
        }

        private void DrawVessel()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                DrawEditorKomfort();
                return;
            }
            if (!snapshot.HasActiveVessel)
            {
                GUILayout.Label(Localizer.Format("#Koffee_UI_NoVessel"));
            }
            else
            {
                if (snapshot.Crew.Count == 0)
                {
                    GUILayout.Label(Localizer.Format("#Koffee_UI_NoCrew"));
                }
                else
                {
                    DrawCrew();
                }

                GUILayout.Space(4);
                GUILayout.Label(Localizer.Format("#Koffee_UI_Aboard",
                    snapshot.AvailableCups.ToString("0", CultureInfo.InvariantCulture)), strongLabel!);
                DrawStationLaboratories();
                DrawStationExperiments();
                DrawSupply();
            }
        }

        private void DrawEditorKomfort()
        {
            KoffeeEditorKomfort? editor = snapshot.EditorKomfort;
            if (editor?.HasShip != true)
            {
                GUILayout.Label(Localizer.Format("#Koffee_EditorKomfort_NoShip"), wrappedLabel!);
                return;
            }
            GUILayout.Label(Localizer.Format("#Koffee_EditorKomfort_Value",
                KoffeeCrewDisplay.Komfort(editor.Komfort)), strongLabel!);
            GUILayout.Label(Localizer.Format("#Koffee_EditorKomfort_Crew", editor.CrewCount.HasValue ?
                editor.CrewCount.Value.ToString(CultureInfo.InvariantCulture) : KoffeeResearchDisplay.Unknown), wrappedLabel!);
            GUILayout.Label(Localizer.Format(editor.AssumesHomeConnection ? "#Koffee_EditorKomfort_AssumptionHome" :
                HighLogic.CurrentGame?.Parameters.Difficulty.EnableCommNet == true ?
                    "#Koffee_EditorKomfort_AssumptionNoAntenna" : "#Koffee_EditorKomfort_AssumptionNoHome"), wrappedLabel!);
        }

        private void DrawStationLaboratories()
        {
            if (snapshot.Laboratories.Count == 0) return;
            GUILayout.Space(8);
            GUILayout.Label(Localizer.Format("#Koffee_Station_Labs"), strongLabel!);
            foreach (KoffeeLabStationStatus lab in snapshot.Laboratories)
            {
                GUILayout.Label(lab.PartTitle + " — " +
                    Localizer.Format("#Koffee_Station_Slots", lab.Slots.ToString(
                        CultureInfo.InvariantCulture)));
            }
        }

        private void DrawStationExperiments()
        {
            if (snapshot.Experiments.Count == 0) return;
            GUILayout.Space(8);
            GUILayout.Label(Localizer.Format("#Koffee_Station_Experiments"), strongLabel!);
            foreach (StationExperimentRuntimeSnapshot experiment in snapshot.Experiments)
            {
                ExperimentContainerState? state = experiment.State;
                string id = experiment.Module.experimentID;
                string title = Localizer.Format(string.IsNullOrWhiteSpace(id) ? "#Koffee_Selector_Unconfigured" :
                    ResearchAndDevelopment.GetExperiment(id)?.experimentTitle ?? id);
                GUILayout.Label(title, strongLabel!);
                GUILayout.Label(Localizer.Format("#Koffee_Station_State",
                    state?.Status.ToString() ?? "Invalid", StationExperimentEquipment.PauseText(experiment.PauseReason, experiment.Definition)));
                GUILayout.Label(Localizer.Format("#Koffee_Station_Progress",
                    experiment.RequiredPoints > 0 ? experiment.AccumulatedPoints.ToString("0.##", CultureInfo.CurrentCulture) + " RP" : KoffeeResearchDisplay.Unknown,
                    experiment.RequiredPoints > 0 ? experiment.RequiredPoints.ToString("0.##", CultureInfo.CurrentCulture) + " RP" : KoffeeResearchDisplay.Unknown,
                    experiment.Research.RemainingSeconds.HasValue ? Duration(experiment.Research.RemainingSeconds.Value) : KoffeeResearchDisplay.Unknown));
                StationResearchPresentation research = experiment.Research;
                GUILayout.Label(Localizer.Format("#Koffee_Research_CategoryLine", research.Category.HasValue ?
                    KoffeeResearchDisplay.Category(research.Category.Value) : KoffeeResearchDisplay.Unknown));
                if (research.Profile != null)
                {
                    GUILayout.Label(Localizer.Format("#Koffee_Research_Worker", research.Affinity.ToString(CultureInfo.InvariantCulture),
                        research.Level.ToString(CultureInfo.InvariantCulture)));
                    GUILayout.Label(Localizer.Format("#Koffee_Research_RateLine", research.RatePerMinute.ToString("0.0", CultureInfo.CurrentCulture)));
                }
                if (!research.Complete) GUILayout.Label(Localizer.Format("#Koffee_Research_TimeAssumption"), wrappedLabel!);
                string contextBody = string.IsNullOrEmpty(state?.ResultBody)
                    ? experiment.Body : state!.ResultBody;
                string contextSituation = string.IsNullOrEmpty(state?.ResultSituation)
                    ? experiment.Situation : state!.ResultSituation;
                GUILayout.Label(Localizer.Format("#Koffee_Station_Context",
                    contextBody, contextSituation));
                GUILayout.Label(Localizer.Format("#Koffee_Equipment_Required") + ": " + StationExperimentEquipment.Description(experiment.Definition));
                GUILayout.Label(Localizer.Format("#Koffee_Environment_Bodies") + ": " + StationExperimentEnvironment.Bodies(experiment.Definition));
                GUILayout.Label(Localizer.Format("#Koffee_Environment_Atmosphere") + ": " + StationExperimentEnvironment.Atmosphere(experiment.Module.experimentID));
                if (state?.HasContextBinding == true)
                    GUILayout.Label(Localizer.Format("#Koffee_Environment_Context") + ": " + StationExperimentEnvironment.Context(state));
                if (state?.HasSurfaceBinding == true)
                    GUILayout.Label(Localizer.Format("#Koffee_Environment_Surface") + ": " + StationExperimentEnvironment.Surface(state));
                GUILayout.Label(Localizer.Format("#Koffee_Station_ValidSituations",
                    string.Join(", ", StationExperimentRuntime.ValidSituations(
                        experiment.Module.experimentID, experiment.Module.part))));
                GUILayout.Label(Localizer.Format("#Koffee_Station_Scientist",
                    string.IsNullOrEmpty(state?.AssignedKerbalDisplayName)
                        ? Localizer.Format("#Koffee_Station_None")
                        : state!.AssignedKerbalDisplayName));
                if (state?.HasAssignedLaboratory == true)
                {
                    ModuleKoffeeLab? assignedLab = experiment.AssignedLaboratory;
                    GUILayout.Label(Localizer.Format("#Koffee_Station_LabSlot",
                        assignedLab == null ? Localizer.Format("#Koffee_Station_LabUnavailable",
                            state.AssignedLaboratoryPersistentId.ToString(CultureInfo.InvariantCulture)) :
                            (assignedLab.part.partInfo?.title ?? assignedLab.part.partInfo?.name ??
                                assignedLab.part.name),
                        (state.AssignedLaboratorySlot + 1).ToString(CultureInfo.InvariantCulture)));
                }


            }
        }

        private void DrawSupply()
        {
            if (!snapshot.Experiments.Any(e => e.State?.Status == ExperimentContainerStatus.Running && !e.WorkComplete)) return;
            GUILayout.Space(8);
            GUILayout.Label(Localizer.Format("#Koffee_Research_SupplyTitle"), strongLabel!);
            if (snapshot.Supply == null)
            {
                GUILayout.Label(KoffeeResearchDisplay.Unknown);
                return;
            }
            foreach (var scientist in snapshot.Supply.ScientistPurchases.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                double coverage = snapshot.Crew.FirstOrDefault(c => c.Name == scientist.Key).RemainingSeconds;
                GUILayout.Label(Localizer.Format("#Koffee_Research_ScientistSupply", scientist.Key,
                    (scientist.Value * snapshot.ActivationCost).ToString("0", CultureInfo.InvariantCulture), Duration(coverage)), wrappedLabel!);
            }
            if (snapshot.Experiments.Any(e => e.State?.Status == ExperimentContainerStatus.Running && !e.WorkComplete))
                GUILayout.Label(Localizer.Format("#Koffee_Research_EngineerSupply", snapshot.EngineerName,
                (snapshot.Supply.EngineerPurchases * snapshot.ActivationCost).ToString("0", CultureInfo.InvariantCulture), Duration(snapshot.EngineerCoverage)), wrappedLabel!);
            GUILayout.Label(Localizer.Format("#Koffee_Research_TotalSupply",
                (snapshot.Supply.TotalPurchases * snapshot.ActivationCost).ToString("0", CultureInfo.InvariantCulture)));
            GUILayout.Label(Localizer.Format("#Koffee_Research_SupplyAssumption"), wrappedLabel!);
        }

        private static string Pause(StationExperimentPauseReason reason) =>
            Localizer.Format("#Koffee_Station_Pause_" + reason);

        private static string Duration(double seconds) =>
            KerbalWorkState.FormatDuration(Math.Max(0, seconds));

        private void DrawCrew()
        {
            foreach (KoffeeCrewStatus kerbal in snapshot.Crew)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(kerbal.Name + " — " + kerbal.Profession, GUILayout.Width(250));
                GUILayout.Label(Status(kerbal), rightLabel!, GUILayout.Width(180));
                GUILayout.EndHorizontal();
            }
        }

        private static string Status(KoffeeCrewStatus kerbal)
        {
            if (!kerbal.IsKaffeinated) return Localizer.Format("#Koffee_UI_NotKaffeinated");
            string key = LabDisplay.TimeKey(kerbal.RemainingSeconds,
                out string hours, out string minutes);
            return Localizer.Format("#Koffee_UI_Kaffeinated") + " — " +
                Localizer.Format(key, hours, minutes);
        }

        private void EnsureStyles(GUISkin skin)
        {
            if (ReferenceEquals(styledSkin, skin)) return;
            styledSkin = skin;
            rightLabel = new GUIStyle(skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                wordWrap = false
            };
            strongLabel = new GUIStyle(skin.label) { fontStyle = FontStyle.Bold };
            wrappedLabel = new GUIStyle(skin.label) { wordWrap = true };
            rosterButton = new GUIStyle(skin.button) { wordWrap = true, alignment = TextAnchor.MiddleLeft };
        }

        private void EnsureInitialPosition(float scale)
        {
            if (windowRect.x > 0) return;
            Vector2 position = rosterView != null ? rememberedRosterPosition :
                flightView == null ? rememberedPosition : rememberedFlightPosition;
            if (position.x >= 0 && position.y >= 0)
            {
                windowRect.x = position.x;
                windowRect.y = position.y;
                return;
            }
            windowRect.x = Math.Max(0, Screen.width / scale - CurrentWidth - 54);
        }

        private void ClampToScreen(float scale)
        {
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            windowRect.x = Mathf.Clamp(windowRect.x, 0, Math.Max(0, width - windowRect.width));
            windowRect.y = Mathf.Clamp(windowRect.y, 0, Math.Max(0, height - windowRect.height));
        }
    }
}

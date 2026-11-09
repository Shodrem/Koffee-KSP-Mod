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
        private bool servicesOpen, engineeringOpen, maintenanceOpen, emergencyOpen;

        internal void Refresh(KoffeeStatusSnapshot snapshot)
        {
            if (navigation.Reconcile(snapshot))
            {
                for (int i = 0; i < pageScroll.Length; i++) pageScroll[i] = Vector2.zero;
                rosterScroll = Vector2.zero;
                scrollToExperiment = null;
                scrollToSupply = false;
                servicesOpen = engineeringOpen = maintenanceOpen = emergencyOpen = false;
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
            Tab(KoffeeFlightTab.Crew, "#Koffee_Crew_Tab");
            GUILayout.EndHorizontal();
            // Capture the page: an attention button can change tabs during this GUI event.
            int page = (int)navigation.Tab;
            pageScroll[page] = GUILayout.BeginScrollView(pageScroll[page], false, true,
                GUILayout.Height(Math.Min(520, Math.Max(100, Screen.height / KoffeeStatusWindow.Scale - 170))));
            if (page == (int)KoffeeFlightTab.Crew) DrawCrew(snapshot);
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
            Text(snapshot.AirFraction.HasValue ? snapshot.VesselName :
                snapshot.VesselName + " — " + Localizer.Format("#Koffee_Flight_SurvivalReserve",
                    KoffeeCrewDisplay.SurvivalReserve(snapshot.Komfort)), true);
            DrawVesselCondition(snapshot);
            Text(KoffeeFlightExperiment.Body(snapshot.Body) + " / " + KoffeeFlightExperiment.Situation(snapshot.Situation));
            Text(Localizer.Format("#Koffee_UI_Aboard", snapshot.AvailableCups.ToString("0", CultureInfo.InvariantCulture)), true);
            Text(Localizer.Format("#Koffee_Flight_Counts",
                Count(KoffeeFlightExperimentGroup.Running), Count(KoffeeFlightExperimentGroup.Stopped),
                Count(KoffeeFlightExperimentGroup.Completed)));
            if (experiments.Any(e => e.Group == KoffeeFlightExperimentGroup.Ready || e.Group == KoffeeFlightExperimentGroup.Unknown))
                Text(Localizer.Format("#Koffee_Flight_OtherCounts", Count(KoffeeFlightExperimentGroup.Ready),
                    Count(KoffeeFlightExperimentGroup.Unknown)));
            if (experiments.Length == 0) Text(Localizer.Format("#Koffee_Flight_NoExperiments"));

            servicesOpen = Fold(servicesOpen, Localizer.Format("#Koffee_Komfort_VesselServices"));
            if (servicesOpen) DrawServices(snapshot);

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
                servicesOpen = engineeringOpen = maintenanceOpen = emergencyOpen = false;
            }
            if (navigation.SupplyOpen) DrawSupply(snapshot);
            navigation.PersonnelOpen = Fold(navigation.PersonnelOpen, Localizer.Format("#Koffee_Flight_Personnel"));
            if (navigation.PersonnelOpen) DrawPersonnel(snapshot);
            engineeringOpen = Fold(engineeringOpen, Localizer.Format("#Koffee_Engineering_Activities"));
            if (engineeringOpen) DrawEngineering(snapshot);
            maintenanceOpen = Fold(maintenanceOpen, Localizer.Format("#Koffee_Maintenance_Status"));
            if (maintenanceOpen) DrawMaintenance(snapshot);
        }

        private void DrawServices(KoffeeStatusSnapshot snapshot)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel?.loaded != true || vessel.id != snapshot.VesselId) return;
            var services = vessel.parts.Where(p => p.State != PartStates.DEAD)
                .SelectMany(p => p.Modules.OfType<ModuleKomfortService>()).Where(s => s.Valid).ToArray();
            foreach (string type in new[] { "Climate", "Air", "Hygiene", "Leisure" })
            {
                var group = services.Where(s => s.service == type).ToArray();
                int enabled = group.Count(s => s.serviceEnabled);
                GUILayout.BeginVertical(GUI.skin.box);
                Text(Localizer.Format("#Koffee_Komfort_Service" + type) + " — " +
                    Localizer.Format("#Koffee_Komfort_EnabledCount", enabled.ToString(CultureInfo.InvariantCulture),
                        group.Length.ToString(CultureInfo.InvariantCulture)));
                GUILayout.BeginHorizontal();
                bool previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && group.Any(s => !s.serviceEnabled && s.Maintenance?.InProgress != true);
                if (GUILayout.Button(Localizer.Format("#Koffee_Komfort_ServiceOn"), button!))
                    foreach (var service in group.Where(s => !s.serviceEnabled && s.Maintenance?.InProgress != true)) service.ToggleService();
                GUI.enabled = previousEnabled && group.Any(s => s.serviceEnabled && s.Maintenance?.InProgress != true);
                if (GUILayout.Button(Localizer.Format("#Koffee_Komfort_ServiceOff"), button!))
                    foreach (var service in group.Where(s => s.serviceEnabled && s.Maintenance?.InProgress != true)) service.ToggleService();
                GUI.enabled = previousEnabled;
                GUILayout.EndHorizontal();
                if (group.Any(s => s.Maintenance?.InProgress == true)) Text(Localizer.Format("#Koffee_Komfort_ServiceMaintenanceLock"));
                GUILayout.EndVertical();
            }
            DrawEmergencyService(vessel);
        }

        private void DrawEmergencyService(Vessel vessel)
        {
            var devices = EmergencySupport.Capture(vessel);
            if (devices.Count == 0) return;
            emergencyOpen = Fold(emergencyOpen, Localizer.Format("#Koffee_Health_Service"));
            if (!emergencyOpen) return;
            Text(Localizer.Format("#Koffee_Health_SupportHelp"));
            bool previous = GUI.enabled;
            GUI.enabled = previous && DeadlyScenario.OptionsAt(Planetarium.GetUniversalTime()).Enabled && KomfortRuntime.OptionsAt(Planetarium.GetUniversalTime()).Enabled;
            if (GUILayout.Button(Localizer.Format(DeadlyScenario.Active(vessel) ? "#Koffee_Komfort_ServiceOff" : "#Koffee_Komfort_ServiceOn"), button!))
                DeadlyScenario.SetEmergency(vessel, !DeadlyScenario.Active(vessel));
            GUI.enabled = previous;
        }

        private void DrawEngineering(KoffeeStatusSnapshot snapshot)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel?.loaded != true || vessel.id != snapshot.VesselId) return;
            Text(EngineeringDisplay.Management(vessel));
            if (!StationPowerPolicy.Applies(vessel)) Text(Localizer.Format("#Koffee_Engineering_ElectricalExempt"));
            Text(Localizer.Format("#Koffee_Engineering_KomfortHelp"));
            foreach (var converter in vessel.parts.Where(p => p.State != PartStates.DEAD).SelectMany(p => p.Modules.OfType<BaseConverter>()))
            {
                if (!EngineeringModuleFilter.Matches(converter.GetType(), converter.part.partInfo?.name ?? "", converter.UseSpecialistBonus, converter.ExperienceEffect)) continue;
                var candidates = EngineeringWorkAssignment.Relevant(converter);
                var affinity = converter.GetType() == typeof(ModuleResourceHarvester) ? EngineeringAffinity.Extraction : EngineeringAffinity.Refining;
                string name = candidates.Count == 0 ? "" : candidates[0].Name;
                Text(converter.part.partInfo?.title ?? converter.part.partInfo?.name ?? "", true);
                Text(Localizer.Format(converter.IsActivated ? "#Koffee_Engineering_Active" : "#Koffee_Engineering_Idle"));
                Text(EngineeringDisplay.Effect(vessel, name, affinity));
                var field = typeof(BaseConverter).GetField("inputList", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                var inputs = field?.GetValue(converter) as System.Collections.Generic.IEnumerable<ResourceRatio>;
                if (inputs != null)
                    Text(EngineeringDisplay.Power(inputs.Where(r => r.ResourceName == "ElectricCharge").Sum(r => r.Ratio),
                        converter.IsActivated ? 1 - EngineeringContribution.Savings(vessel, name, affinity, Planetarium.GetUniversalTime(), true) : 0));
                Text(Localizer.Format("#Koffee_Engineering_RecipeHelp"));
            }
            Text(Localizer.Format("#Koffee_Engineering_DemandHelp"));
        }
        private void DrawMaintenance(KoffeeStatusSnapshot snapshot)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel?.loaded != true || vessel.id != snapshot.VesselId) return;
            Text(Localizer.Format("#Koffee_EvaMaintenance_Help"));
            foreach (var service in vessel.parts.Where(p => p.State != PartStates.DEAD).SelectMany(p => p.Modules.OfType<ModuleKomfortService>()).Where(s => s.Maintainable))
            {
                GUILayout.BeginVertical(GUI.skin.box);
                Text(service.part.partInfo?.title ?? service.service, true);
                Text(service.autonomyDisplay); Text(service.maintenanceDisplay);
                var state = service.Maintenance;
                if (state != null && !string.IsNullOrEmpty(state.GrantEngineer))
                {
                    Text(Localizer.Format("#Koffee_EvaMaintenance_HoursRecord", state.RemainingHours.ToString("0.##", CultureInfo.CurrentCulture),
                        state.GrantedHours.ToString("0.##", CultureInfo.CurrentCulture), state.GrantEngineer));
                    Text(Localizer.Format("#Koffee_Maintenance_GrantDetails", (state.GrantBonus * 100).ToString("0.##", CultureInfo.CurrentCulture),
                        (state.GrantKomfort * 100).ToString("0.#", CultureInfo.CurrentCulture), Localizer.Format(state.GrantCaffeinated ? "#Koffee_UI_Kaffeinated" : "#Koffee_UI_NotKaffeinated")));
                }
                if (state?.InProgress != true && GUILayout.Button(Localizer.Format("#Koffee_Komfort_Toggle"), button!)) service.ToggleService();
                GUILayout.EndVertical();
            }
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
            Text(Localizer.Format("#Koffee_Engineering_OptionalSupply"));
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
            Text(EngineeringDisplay.Management(runtime.Module.vessel));
            var activeManager = ExperimentManagerService.Select(runtime.Module.vessel, Planetarium.GetUniversalTime(), false, out _, true);
            if (activeManager?.Part != null && runtime.Definition != null)
            {
                var consumers = StationStockPower.Consumers(activeManager.Part.Modules.OfType<ModuleKoffeeExperimentManager>().First(),
                    runtime.Module, runtime.Definition, runtime.AssignedLaboratory?.part);
                bool electrical = StationPowerPolicy.Applies(runtime.Module.vessel);
                double factor = electrical ? EngineeringContribution.ManagementFactor(runtime.Module.vessel, activeManager.EngineerName, Planetarium.GetUniversalTime(), true) : 0;
                if (!electrical) Text(Localizer.Format("#Koffee_Engineering_ElectricalExempt"));
                Text(EngineeringDisplay.Power(consumers.Values.Sum(), factor));
                foreach (var consumer in consumers.Where(p => p.Value > 0))
                    Text((consumer.Key.partInfo?.title ?? consumer.Key.partInfo?.name ?? "") + ": " + EngineeringDisplay.Power(consumer.Value, factor));
                Text(Localizer.Format("#Koffee_Engineering_DemandHelp"));
            }
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

        private void DrawVesselCondition(KoffeeStatusSnapshot snapshot)
        {
            KoffeeStatusBars.Komfort(snapshot.Komfort, heading!);
            KoffeeStatusBars.Air(snapshot.AirFraction, snapshot.AirLastKnown, heading!);
        }

        private void DrawCrew(KoffeeStatusSnapshot snapshot)
        {
            if (!snapshot.HasActiveVessel) { Text(Localizer.Format("#Koffee_UI_NoVessel")); return; }
            DrawVesselCondition(snapshot);
            Text(Localizer.Format("#Koffee_Crew_KomfortHelp"));
            if (snapshot.Crew.Count == 0) { Text(Localizer.Format("#Koffee_UI_NoCrew")); return; }
            rosterScroll = GUILayout.BeginScrollView(rosterScroll, false, true, GUILayout.Height(140));
            Vessel vessel = FlightGlobals.ActiveVessel;
            string maintenanceEngineer = vessel != null && vessel.id == snapshot.VesselId ? MaintenanceEva.BestEngineer(vessel) : "";
            foreach (KoffeeCrewStatus crew in snapshot.Crew)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Toggle(crew.Name == navigation.SelectedCrew, crew.Name, button!, GUILayout.ExpandWidth(true)))
                {
                    if (navigation.SelectedCrew != crew.Name) navigation.AffinitiesOpen = false;
                    navigation.SelectedCrew = crew.Name;
                }
                GUILayout.BeginVertical(GUILayout.MinWidth(165), GUILayout.ExpandWidth(true));
                GUILayout.Label(KoffeeCrewDisplay.ProfessionLevel(crew), label!);
                KoffeeStatusBars.Health(crew);
                GUILayout.EndVertical();
                if (crew.Name == maintenanceEngineer)
                {
                    if (GUILayout.Button(Localizer.Format("#Koffee_EvaMaintenance_Depart"), button!, GUILayout.Width(140)))
                        MaintenanceEva.Depart(vessel!, crew.Name);
                }
                else if (!string.IsNullOrEmpty(maintenanceEngineer))
                    GUILayout.Space(140 + button!.margin.left + button.margin.right);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            KoffeeCrewStatus member = snapshot.Crew.FirstOrDefault(s => s.Name == navigation.SelectedCrew);
            if (string.IsNullOrEmpty(member.Name)) return;
            Text(KoffeeCrewDisplay.Row(member), true);
            var hp = DeadlyScenario.Health(member.Name);
            if (hp != null && member.HealthFraction.HasValue) Text(Localizer.Format("#Koffee_Health_" + hp.Status));
            var eva = DeadlyScenario.Eva(member.Name);
            if (eva != null && member.HealthFraction.HasValue)
                Text(Localizer.Format("#Koffee_Health_EvaFilter", Duration(eva.Filter)));
            Text(KoffeeCrewDisplay.State(member.RosterState));
            Text(snapshot.VesselName);
            Text(Kaffeinated(member));
            if (member.IsEngineer)
            {
                navigation.AffinitiesOpen = Fold(navigation.AffinitiesOpen, Localizer.Format("#Koffee_Engineering_Profile"));
                if (navigation.AffinitiesOpen)
                    foreach (string line in EngineeringDisplay.Profile(member.Name)) Text(line);
            }
            KoffeeScientistStatus? selected = member.Scientist;
            if (selected == null) return;
            Text(Localizer.Format("#Koffee_Research_ScientistLevel", selected.Name, selected.Level.ToString(CultureInfo.InvariantCulture)), true);
            Text(KoffeeResearchDisplay.RosterStatus(selected));
            Text(KoffeeResearchDisplay.Specialties(selected.Profile));
            navigation.AffinitiesOpen = Fold(navigation.AffinitiesOpen, Localizer.Format("#Koffee_Flight_Affinities"));
            if (navigation.AffinitiesOpen && selected.Profile != null)
                foreach (ScienceCategory category in ScienceCategories.All)
                    Text(KoffeeResearchDisplay.Category(category) + ": " + selected.Profile.Score(category).ToString(CultureInfo.InvariantCulture) + " / 10");
        }

        private static string Kaffeinated(KoffeeCrewStatus crew)
        {
            if (!crew.IsKaffeinated) return Localizer.Format("#Koffee_UI_NotKaffeinated");
            string key = LabDisplay.TimeKey(crew.RemainingSeconds, out string hours, out string minutes);
            return Localizer.Format("#Koffee_UI_Kaffeinated") + " — " + Localizer.Format(key, hours, minutes);
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

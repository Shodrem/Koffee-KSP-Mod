using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP.Localization;
using KSP.UI.Screens.Flight.Dialogs;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    public sealed class ModuleKoffeeExperiment : PartModule, IScienceDataContainer
    {
        [KSPField(isPersistant = true)]
        public string experimentID = "";

        // Empty on legacy fixed parts; only configurable hosts set this.
        [KSPField] public string editorExperimentFamily = "";

        // Stock PARTUPGRADE changes this per-part capability; snapshots retain it.
        // Ordinary installations keep their existing surface access by default.
        [KSPField(isPersistant = true)]
        public bool surfaceEnabled = true;

        [KSPField(guiActive = true, guiName = " ", groupName = "ScientistAssignment",
            groupDisplayName = "#Koffee_Experiment_SelectScientist", groupStartCollapsed = false)]
        [UI_ChooseOption(scene = UI_Scene.Flight, affectSymCounterparts = UI_Scene.None)]
        public string scientistCandidate = "";
        [KSPField(isPersistant = true)] public bool manuallyPaused;
        [KSPField(guiActive = true, guiName = " ")] public string experimentSpacer = " ";
        [KSPField(guiActive = true, guiName = "#Koffee_Experiment_Status")] public string experimentStatus = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Manager_Scientist")] public string scientistDisplay = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Lab_RequiredCapability")] public string laboratoryRequirementDisplay = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Manager_Laboratory")] public string laboratoryDisplay = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Manager_Slot")] public string slotDisplay = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Manager_Progress")] public string progressDisplay = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Manager_Remaining")] public string remainingDisplay = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Manager_Pause")] public string pauseDisplay = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Research_SpecialtiesLabel", groupName = "ScientistAssignment")]
        public string specialtiesDisplay = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Research_CategoryLabel")]
        public string categoryDisplay = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Research_WorkerLabel", groupName = "ScientistAssignment")]
        public string researchWorkerDisplay = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Research_RateLabel")]
        public string rateDisplay = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Research_EffectiveTimeLabel")]
        public string effectiveTimeDisplay = "";

        [KSPField(guiActive = true, guiName = "#Koffee_Environment_Bodies")] public string bodiesDisplay = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Environment_Atmosphere")] public string atmosphereDisplay = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Environment_Surface")] public string surfaceDisplay = "";

        [KSPField(guiActive = true, guiName = "#Koffee_Environment_Context")] public string contextDisplay = "";

        [KSPField(guiActive = true, guiName = "#Koffee_Equipment_Required")] public string equipmentDisplay = "";

        // Per-flight budget, independent of the current global processing speed.
        private const string LaunchBudgetKey = "launchResearchPointsRequired";
        private const string LaunchExperimentKey = "launchResearchExperimentId";
        private double launchBudget;
        private string launchExperiment = "";

        private ExperimentContainerState? state;
        private ConfigNode? incompatibleState;
        private ScienceData? scienceData;
        private ExperimentsResultDialog? resultsDialog;
        private bool diagnosticLogged;
        private bool checkpointRebasePending;
        // Temporary personnel diagnostics: remove after the intermittent PAW issue is captured.
        private string personnelTraceSnapshot = "";

        internal bool ElectricityBlocked;
        internal bool ElectricalWorkReady;
        internal ExperimentContainerState? State => state;
        internal bool HasIncompatibleState => incompatibleState != null;
        internal bool CheckpointRebasePending => checkpointRebasePending;
        internal ScienceData? ScienceData => scienceData;
        public IReadOnlyList<TelescopeCapture> TelescopeObservations => state?.TelescopeObservations ?? Array.Empty<TelescopeCapture>();

        public override void OnLoad(ConfigNode node)
        {
            StationExperimentAnimationPreparation.Cancel(this);
            ElectricityBlocked = false;
            ElectricalWorkReady = false;
            base.OnLoad(node);
            surfaceEnabled = StationExperimentEnvironment.SurfaceCapability(node, surfaceEnabled);
            // Only the stock partial callback is state-free. Full MODULE loads can
            // contain name__ after ApplyUpgradeNode merges an upgrade into them.
            if (node.name == "CURRENTUPGRADE") return;
            experimentID = string.IsNullOrEmpty(editorExperimentFamily) ? FixedExperimentId() : node.GetValue(nameof(experimentID)) ?? experimentID;
            state = null;
            launchBudget = 0;
            launchExperiment = "";
            incompatibleState = null;
            scienceData = null;
            manuallyPaused = false;
            ConfigNode raw = node.CreateCopy();
            ExperimentContainerReadResult result = ExperimentContainerStateCodec.Read(
                node, experimentID, out ExperimentContainerState? loaded, out string error);
            if (result == ExperimentContainerReadResult.Valid)
            {
                state = loaded;
                manuallyPaused = state?.Status == ExperimentContainerStatus.Running &&
                    bool.TryParse(node.GetValue(nameof(manuallyPaused)), out bool paused) && paused;
                try
                {
                    ConfigNode[] dataNodes = node.GetNodes("ScienceData");
                    if (dataNodes.Length > 1)
                        throw new InvalidOperationException("Container contains more than one ScienceData node.");
                    if (dataNodes.Length == 1) scienceData = new ScienceData(dataNodes[0]);
                    ValidateScienceState();
                }
                catch (Exception exception)
                {
                    state = null;
                    scienceData = null;
                    incompatibleState = raw;
                    LogError("Container science state was preserved but disabled: " + exception.Message);
                    return;
                }
                checkpointRebasePending = state?.Status == ExperimentContainerStatus.Running;
            }
            else if (result == ExperimentContainerReadResult.Invalid)
            {
                incompatibleState = raw;
                LogError("Container state was preserved but disabled: " + error);
            }
            else if (node.GetNodes("ScienceData").Length != 0)
            {
                incompatibleState = raw;
                LogError("ScienceData exists without a registered container state.");
            }
            if (node.HasValue(LaunchBudgetKey) || node.HasValue(LaunchExperimentKey))
            {
                if (node.GetValues(LaunchBudgetKey).Length != 1 || node.GetValues(LaunchExperimentKey).Length != 1 ||
                    node.GetValue(LaunchExperimentKey) != experimentID ||
                    !double.TryParse(node.GetValue(LaunchBudgetKey), NumberStyles.Float, CultureInfo.InvariantCulture, out launchBudget) ||
                    !ExperimentContainerState.Finite(launchBudget) || launchBudget <= 0)
                {
                    state = null;
                    scienceData = null;
                    incompatibleState = raw;
                    LogError("Invalid launch research budget was preserved but disabled.");
                    return;
                }
                launchExperiment = experimentID;
            }
        }

        public override void OnStart(StartState startState)
        {
            base.OnStart(startState);
            if (startState == StartState.Editor)
            {
                launchBudget = 0;
                launchExperiment = "";
                state = null;
                manuallyPaused = false;
                incompatibleState = null;
                diagnosticLogged = false;
                checkpointRebasePending = false;
                scienceData = null;
                return;
            }
            if (!HighLogic.LoadedSceneIsFlight) return;
            EnsureLaunchBudget();
            EnsureRegistered();
            Update();
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            node.SetValue(StationBackgroundElectricity.SchemaKey, StationPowerSaveMarker.Schema, true);
            node.SetValue(StationBackgroundElectricity.ProviderKey, StationBackgroundElectricity.ProviderId, true);
            node.SetValue(nameof(surfaceEnabled), surfaceEnabled.ToString(), true);
            node.RemoveNodes("ScienceData");
            if (HighLogic.LoadedSceneIsEditor)
            {
                node.RemoveValues(LaunchBudgetKey);
                node.RemoveValues(LaunchExperimentKey);
                ExperimentContainerStateCodec.Remove(node);
                if (!string.IsNullOrEmpty(editorExperimentFamily)) node.SetValue(nameof(experimentID), experimentID, true);
                node.RemoveValues(nameof(manuallyPaused));
                return;
            }
            if (incompatibleState != null)
            {
                ExperimentContainerStateCodec.CopyRaw(incompatibleState, node);
                return;
            }
            if (HighLogic.LoadedSceneIsFlight)
            {
                EnsureLaunchBudget();
                EnsureRegistered();
            }
            if (launchBudget > 0)
            {
                node.SetValue(LaunchBudgetKey, launchBudget.ToString("R", CultureInfo.InvariantCulture), true);
                node.SetValue(LaunchExperimentKey, launchExperiment, true);
            }
            if (state != null)
            {
                ExperimentContainerStateCodec.Write(node, state);
                node.SetValue(nameof(manuallyPaused), (manuallyPaused &&
                    state.Status == ExperimentContainerStatus.Running).ToString(), true);
            }
            if (scienceData != null) scienceData.Save(node.AddNode("ScienceData"));
        }

        public void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded) return;
            EnsureRegistered();
            RefreshReviewEvent();
            // Observe departures every frame, even between research ticks. Returning
            // cannot turn the unobserved interval into research credit.
            if (state?.Status == ExperimentContainerStatus.Running && state.HasContextBinding && !state.WorkComplete() &&
                StationExperimentCatalogLoader.Current.TryGet(experimentID, out var surfaceDefinition))
            {
                var environment = StationExperimentEnvironment.Evaluate(surfaceDefinition, state, vessel,
                    StationExperimentRuntime.CurrentSituation(vessel), part);
                if (environment != StationExperimentPauseReason.None)
                {
                    RebaseCheckpoint(Planetarium.GetUniversalTime(), StationExperimentRuntime.CurrentBody(vessel),
                        StationExperimentRuntime.CurrentSituation(vessel));
                    StationExperimentNotifications.Observe(state!, vessel.vesselName,
                        manuallyPaused ? StationExperimentPauseReason.ManuallyPaused : environment);
                }
            }
            StationExperimentRuntimeSnapshot? current = StationExperimentRuntime.Capture(vessel)
                .FirstOrDefault(e => ReferenceEquals(e.Module, this));
            if (current?.State?.Status == ExperimentContainerStatus.Running && !current.WorkComplete &&
                current.PauseReason != StationExperimentPauseReason.None &&
                current.PauseReason != StationExperimentPauseReason.NoElectricity)
                RebaseCheckpoint(Planetarium.GetUniversalTime(), StationExperimentRuntime.CurrentBody(vessel),
                    StationExperimentRuntime.CurrentSituation(vessel));
            bodiesDisplay = StationExperimentEnvironment.Bodies(current?.Definition);
            atmosphereDisplay = StationExperimentEnvironment.Atmosphere(experimentID);
            surfaceDisplay = StationExperimentEnvironment.Surface(state);
            contextDisplay = StationExperimentEnvironment.Context(state);
            equipmentDisplay = StationExperimentEquipment.Description(current?.Definition);
            bool available = current?.State?.Status == ExperimentContainerStatus.Available;
            StationScientistCandidate[] candidates = current?.SelectableScientists.ToArray() ??
                Array.Empty<StationScientistCandidate>();
            string observedCandidate = scientistCandidate;
            if (!candidates.Any(c => CandidateKey(c) == scientistCandidate))
            {
                StationScientistCandidate assigned = candidates.FirstOrDefault(c =>
                    c.Scientist.name == state?.AssignedKerbalName &&
                    c.Seat.persistentId == state?.AssignedLaboratoryPersistentId);
                scientistCandidate = assigned.Scientist != null ? CandidateKey(assigned) :
                    candidates.Length > 0 ? CandidateKey(candidates[0]) : "";
            }
            bool running = current?.State?.Status == ExperimentContainerStatus.Running;
            Fields[nameof(scientistCandidate)].guiActive = available || running;
            if (running)
            {
                scientistCandidate = state!.AssignedLaboratoryPersistentId.ToString(CultureInfo.InvariantCulture) + ":" + state.AssignedKerbalName;
                StationPersonnelSelector.Refresh(Fields[nameof(scientistCandidate)],
                    new[] { scientistCandidate }, new[] { state.AssignedKerbalName }, false);
            }
            else
            {
                StationPersonnelSelector.Refresh(Fields[nameof(scientistCandidate)],
                    candidates.Length == 0 ? new[] { "" } : candidates.Select(CandidateKey).ToArray(),
                    candidates.Length == 0 ? new[] { None() } : candidates.Select(c => c.Scientist.name).ToArray(), available);
            }
            TracePersonnelSelector(observedCandidate, candidates);
            experimentStatus = Localizer.Format("#Koffee_Experiment_Status_" +
                (state == null ? "Incompatible" : running && manuallyPaused ? "Paused" : state.Status.ToString()));
            scientistDisplay = string.IsNullOrEmpty(state?.AssignedKerbalName) ? None() : state!.AssignedKerbalName;
            laboratoryRequirementDisplay = current?.Definition == null ? None() : KoffeeResearchDisplay.Laboratory(current.Definition.LabCapability);
            laboratoryDisplay = current?.AssignedLaboratory?.part.partInfo?.title ?? None();
            slotDisplay = state?.HasSlotBinding == true ?
                (state.AssignedLaboratorySlot + 1).ToString(CultureInfo.InvariantCulture) : None();
            progressDisplay = current == null || current.RequiredPoints <= 0 ? None() :
                (100 * current.AccumulatedPoints / current.RequiredPoints).ToString("0.0", CultureInfo.CurrentCulture) + " %";
            StationScientistCandidate selected = candidates.FirstOrDefault(c => CandidateKey(c) == scientistCandidate);
            StationResearchPresentation research = new StationResearchPresentation(this, current?.Definition,
                current?.State, selected.Scientist);
            specialtiesDisplay = KoffeeResearchDisplay.Specialties(research.Profile);
            categoryDisplay = research.Category.HasValue ? KoffeeResearchDisplay.Category(research.Category.Value) : KoffeeResearchDisplay.Unknown;
            researchWorkerDisplay = research.Profile == null ? KoffeeResearchDisplay.Unknown : Localizer.Format(
                "#Koffee_Research_Worker", research.Affinity.ToString(CultureInfo.InvariantCulture), research.Level.ToString(CultureInfo.InvariantCulture));
            rateDisplay = research.RatePerMinute > 0 ? research.RatePerMinute.ToString("0.0", CultureInfo.CurrentCulture) + " RP/min" : KoffeeResearchDisplay.Unknown;
            effectiveTimeDisplay = research.RemainingSeconds.HasValue ? KerbalWorkState.FormatDuration(research.RemainingSeconds.Value) : KoffeeResearchDisplay.Unknown;
            remainingDisplay = research.Complete || (running && current?.PauseReason == StationExperimentPauseReason.None) ?
                effectiveTimeDisplay : KoffeeResearchDisplay.Unknown;
            pauseDisplay = current == null ? None() : StationExperimentEquipment.PauseText(current.PauseReason, current.Definition);
            string solarReason = available && current?.Definition != null ?
                StationSolarAcquisition.Readiness(current.Definition, vessel, part, Planetarium.GetUniversalTime(), false) : "";
            if (solarReason != "") pauseDisplay = StationSolarAcquisition.Error(solarReason);
            bool hasCandidate = candidates.Any(c => CandidateKey(c) == scientistCandidate);
            bool telescopeExperiment = current?.Definition != null && StationTelescopeObservation.Required(current.Definition);
            SetEvent(nameof(OpenTelescopeObservation), telescopeExperiment && StationTelescopeObservation.Readiness(this) == "");
            SetEvent(nameof(DiscardTelescopeObservation), telescopeExperiment && available && state?.TelescopeCapture != null);
            SetEvent(nameof(AssignScientist), available && hasCandidate);
            SetEvent(nameof(ReleaseScientist), available && state!.HasAssignment);
            SetEvent(nameof(StopExperiment), running && !manuallyPaused && current?.WorkComplete == false);
            SetEvent(nameof(ResumeExperiment), running && manuallyPaused && current?.WorkComplete == false);
            bool preparing = StationExperimentAnimationPreparation.Pending(this);
            SetEvent(nameof(CancelAnimationPreparation), preparing);
            if (preparing) pauseDisplay = Localizer.Format("#Koffee_Animation_Preparing");
            SetEvent(nameof(StartExperiment), !preparing && available && state!.HasAssignment && solarReason == "" &&
                (!telescopeExperiment || state.TelescopeCapture != null) &&
                ExperimentManagerService.Select(vessel, Planetarium.GetUniversalTime(), false, out _) != null);
        }

        [KSPEvent(guiActive = true, guiName = "#Koffee_Telescope_Open")]
        public void OpenTelescopeObservation() => StationTelescopeObservation.Open(this);

        [KSPEvent(guiActive = true, guiName = "#Koffee_Telescope_Discard")]
        public void DiscardTelescopeObservation()
        {
            if (state?.Status == ExperimentContainerStatus.Available && incompatibleState == null)
                state = state.DiscardTelescopeCapture();
            Update();
        }

        internal bool TryRecordTelescopeCapture(Guid containerId, long revision, TelescopeCapture capture)
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel?.loaded != true || state == null || incompatibleState != null ||
                state.ContainerId != containerId || state.Revision != revision ||
                (state.Status != ExperimentContainerStatus.Available && state.TelescopeCapture == null) || part.State == PartStates.DEAD || !vessel.parts.Contains(part) ||
                !StationExperimentCatalogLoader.Current.TryGet(experimentID, out var definition) ||
                !StationTelescopeObservation.Required(definition) || !StationTelescopeObservation.CaptureMatches(capture, definition) || !definition.AllowsBody(capture.Target) ||
                StationTelescopeObservation.Readiness(this) != "") return false;
            try { state = state.RecordTelescopeCapture(capture); }
            catch (InvalidOperationException) { return false; }
            TelescopeObservationEvents.Publish(state.ContainerId, experimentID, capture);
            return true;
        }

        private void TracePersonnelSelector(string observedCandidate, StationScientistCandidate[] candidates)
        {
            try
            {
                UI_ChooseOption? control = Fields[nameof(scientistCandidate)].uiControlFlight as UI_ChooseOption;
                string details = "observed=" + observedCandidate + "; effective=" + scientistCandidate +
                    "; candidates=[" + string.Join(",", candidates.Select(CandidateKey)) +
                    "]; options=[" + string.Join(",", control?.options ?? Array.Empty<string>()) +
                    "]; display=[" + string.Join(",", control?.display ?? Array.Empty<string>()) +
                    "]; enabled=" + control?.controlEnabled + "; " + PersonnelState();
                if (details == personnelTraceSnapshot) return;
                personnelTraceSnapshot = details;
                TracePersonnel("Selector changed", details, false);
            }
            catch (Exception) { /* Diagnostics must never interrupt the PAW. */ }
        }

        private string PersonnelState() => "status=" + state?.Status +
            "; assigned=" + (state?.AssignedKerbalName ?? "<none>") +
            "; lab=" + state?.AssignedLaboratoryPersistentId +
            "; slot=" + state?.AssignedLaboratorySlot + "; paused=" + manuallyPaused;

        private void TracePersonnel(string action, string details, bool includePeers)
        {
            try
            {
                string peers = includePeers && vessel?.parts != null ? string.Join(" | ", vessel.parts
                    .SelectMany(p => p.Modules.OfType<ModuleKoffeeExperiment>())
                    .Select(m => m.experimentID + ":" + m.state?.ContainerId + ":" + m.PersonnelState())) : "";
                Debug.Log("[Koffee][PersonnelTrace] " + action + "; UT=" +
                    Planetarium.GetUniversalTime().ToString("R", CultureInfo.InvariantCulture) +
                    "; vessel=" + vessel?.vesselName + "; flightID=" + part?.flightID +
                    "; experiment=" + experimentID + "; container=" + state?.ContainerId +
                    "; run=" + state?.RunId + "; " + PersonnelState() + "; " + details +
                    (includePeers ? "; peers=[" + peers + "]" : ""));
            }
            catch (Exception) { /* Diagnostics must never change command outcomes. */ }
        }

        private static string None() => Localizer.Format("#Koffee_Manager_None");
        private static string CandidateKey(StationScientistCandidate candidate) =>
            candidate.Seat.persistentId.ToString(CultureInfo.InvariantCulture) + ":" + candidate.Scientist.name;
        private void SetEvent(string name, bool enabled) { if (Events[name] != null) Events[name].active = enabled; }
        private void Notify(bool success, string error)
        {
            if (!success) ScreenMessages.PostScreenMessage(error, 5f, ScreenMessageStyle.UPPER_CENTER);
            Update();
        }

        [KSPEvent(guiActive = true, guiName = "#Koffee_PAW_Assign", groupName = "ScientistAssignment",
            groupDisplayName = "#Koffee_Experiment_SelectScientist", groupStartCollapsed = false)]
        public void AssignScientist()
        {
            // Revalidate the selected pair; never silently assign a replacement after a crew change.
            TracePersonnel("Assign request", "selected=" + scientistCandidate, true);
            StationScientistCandidate candidate = StationExperimentRuntime.Capture(vessel)
                .FirstOrDefault(e => ReferenceEquals(e.Module, this))?.SelectableScientists
                .FirstOrDefault(c => CandidateKey(c) == scientistCandidate) ?? default;
            bool ok = StationExperimentCommands.Assign(this, state?.ContainerId ?? Guid.Empty,
                candidate.Scientist?.name ?? "", candidate.Seat?.persistentId ?? 0, out string error);
            TracePersonnel("Assign result", "ok=" + ok + "; error=" + error +
                "; resolved=" + (candidate.Scientist?.name ?? "<none>") +
                "; resolvedLab=" + (candidate.Seat?.persistentId ?? 0), true);
            Notify(ok, error);
        }

        [KSPEvent(guiActive = true, guiName = "#Koffee_Station_Clear", groupName = "ScientistAssignment",
            groupDisplayName = "#Koffee_Experiment_SelectScientist", groupStartCollapsed = false)]
        public void ReleaseScientist()
        {
            TracePersonnel("ReleaseScientist request", "selected=" + scientistCandidate, true);
            bool ok = StationExperimentCommands.ClearAssignment(this, state?.ContainerId ?? Guid.Empty, out string error);
            TracePersonnel("ReleaseScientist result", "ok=" + ok + "; error=" + error, true);
            Notify(ok, error);
        }

        [KSPEvent(guiActive = true, guiName = "#Koffee_Animation_Cancel")]
        public void CancelAnimationPreparation() => StationExperimentAnimationPreparation.Cancel(this);
        private void OnDestroy() => StationExperimentAnimationPreparation.Cancel(this);

        [KSPEvent(guiActive = true, guiName = "#Koffee_Station_Start")]
        public void StartExperiment()
        {
            TracePersonnel("StartExperiment request", "selected=" + scientistCandidate, true);
            bool ok = StationExperimentCommands.Start(this, state?.ContainerId ?? Guid.Empty, out string error);
            TracePersonnel("StartExperiment result", "ok=" + ok + "; error=" + error, true);
            Notify(ok, error);
        }

        [KSPEvent(guiActive = true, guiName = "#Koffee_Station_Stop")]
        public void StopExperiment()
        {
            TracePersonnel("StopExperiment request", "selected=" + scientistCandidate, true);
            bool ok = StationExperimentCommands.SetPaused(this, state?.ContainerId ?? Guid.Empty, true, out string error);
            TracePersonnel("StopExperiment result", "ok=" + ok + "; error=" + error, true);
            Notify(ok, error);
        }

        [KSPEvent(guiActive = true, guiName = "#Koffee_Station_Resume")]
        public void ResumeExperiment()
        {
            TracePersonnel("ResumeExperiment request", "selected=" + scientistCandidate, true);
            bool ok = StationExperimentCommands.SetPaused(this, state?.ContainerId ?? Guid.Empty, false, out string error);
            TracePersonnel("ResumeExperiment result", "ok=" + ok + "; error=" + error, true);
            Notify(ok, error);
        }

        internal bool TrySetPaused(bool paused, double now, out string error)
        {
            error = Localizer.Format("#Koffee_Station_Error_ContainerUnavailable");
            if (state?.Status != ExperimentContainerStatus.Running || incompatibleState != null ||
                !ExperimentContainerState.Finite(now) ||
                !StationExperimentCatalogLoader.Current.TryGet(experimentID, out var definition) ||
                state.WorkComplete()) return false;
            if (!paused)
            {
                var equipmentReason = StationExperimentEquipment.Evaluate(definition, vessel, part);
                if (equipmentReason != StationExperimentPauseReason.None)
                { error = StationExperimentEquipment.PauseText(equipmentReason, definition); return false; }
            }
            if (manuallyPaused != paused)
            {
                manuallyPaused = paused;
                StationExperimentNotifications.Observe(state, vessel.vesselName ?? "",
                    paused ? StationExperimentPauseReason.ManuallyPaused :
                    StationExperimentEnvironment.Evaluate(definition, state, vessel, StationExperimentRuntime.CurrentSituation(vessel), part));
                RebaseCheckpoint(now, StationExperimentRuntime.CurrentBody(vessel),
                    StationExperimentRuntime.CurrentSituation(vessel));
            }
            error = "";
            return true;
        }

        [KSPEvent(guiActive = true, guiName = "#Koffee_Station_ReviewExperiment")]
        public void ReviewExperiment() => ReviewData();

        internal bool TryAssignScientist(ProtoCrewMember kerbal,
            StationExperimentDefinition definition, uint laboratoryPersistentId,
            out string error)
        {
            error = "";
            if (state == null || incompatibleState != null ||
                state.Status != ExperimentContainerStatus.Available)
            {
                error = Localizer.Format("#Koffee_Station_Error_ContainerUnavailable");
                return false;
            }
            if (kerbal == null || !StationPersonnelRules.Valid(kerbal.name, kerbal.trait,
                kerbal.experienceLevel, definition.Personnel.Trait,
                definition.Personnel.MinimumLevel))
            {
                error = Localizer.Format("#Koffee_Station_Error_PersonnelRequirement");
                return false;
            }
            state = state.Assign(kerbal.name, kerbal.name, laboratoryPersistentId);
            return true;
        }

        internal bool TryClearScientist(out string error)
        {
            error = "";
            if (state == null || incompatibleState != null ||
                state.Status != ExperimentContainerStatus.Available)
            {
                error = Localizer.Format("#Koffee_Station_Error_ClearAfterStart");
                return false;
            }
            state = state.ClearAssignment();
            return true;
        }

        internal bool TryStartRun(double now, string body, string situation,
            int laboratorySlot, out string error)
        {
            error = "";
            if (state == null || incompatibleState != null ||
                state.Status != ExperimentContainerStatus.Available || !state.HasAssignment)
            {
                error = Localizer.Format("#Koffee_Station_Error_ContainerNotReady");
                return false;
            }
            if (!TryPrepareResearch(now, out StationExperimentDefinition definition,
                out ProtoCrewMember scientist, out ScientistSpecialtyProfile profile, out double required))
            {
                error = Localizer.Format("#Koffee_Station_Error_ContainerNotReady");
                return false;
            }
            if (!StationTelescopeObservation.Matches(state, definition))
            { error = Localizer.Format("#Koffee_Telescope_CaptureRequired"); return false; }
            StationExperimentPauseReason environment = StationExperimentEnvironment.Evaluate(definition, state, vessel, situation, part);
            if (environment != StationExperimentPauseReason.None)
            { error = StationExperimentEquipment.PauseText(environment, definition); return false; }
            string surfaceBiome = ""; bool hasBiome = false;
            if (situation == "SrfLanded" && !StationExperimentEnvironment.TryBiome(vessel.mainBody,
                vessel.latitude, vessel.longitude, out surfaceBiome, out hasBiome))
            { error = Localizer.Format("#Koffee_Station_Pause_SurfaceContextUnverified"); return false; }
            if (!StationSurfaceAcquisition.TryCapture(definition, vessel, part, now, out var surfaceCapture, out error))
                return false;
            StationTemperatureReading? temperature = StationExperimentReadings.Capture(definition, vessel, part, now);
            StationPressureReading? pressure = StationExperimentReadings.CapturePressure(definition, vessel, part, now);
            Guid runId = Guid.NewGuid();
            if (!StationSolarAcquisition.TryCapture(definition, vessel, part, runId, now, body, situation, out var solarCapture, out error))
                return false;
            state = state.Start(runId, now, body, situation, laboratorySlot,
                definition.ScienceCategory, required, profile.Score(definition.ScienceCategory), scientist.experienceLevel);
            if (temperature != null) state = state.RecordTemperature(temperature);
            if (pressure != null) state = state.RecordPressure(pressure);
            if (surfaceCapture != null) state = state.RecordSurfaceCapture(surfaceCapture);
            if (solarCapture != null) state = state.RecordSolarCapture(solarCapture);
            if (situation == "SrfLanded") state = state.BindSurface(body, surfaceBiome, hasBiome);
            else if (!StationSolarAcquisition.Required(definition) && ExperimentContainerState.CanBindSituation(situation)) state = state.BindSituation(body, situation);
            StationSurfaceContinuity.Observe(state, vessel);
            StationEquipmentContinuity.Observe(state, definition, vessel, part, StationExperimentEquipment.Capture(vessel));
            checkpointRebasePending = false;
            manuallyPaused = false;
            return true;
        }

        internal void ApplyCheckpoint(double now, double creditedUT, bool verified, string body,
            string situation)
        {
            if (state == null || incompatibleState != null) return;
            state = state.Checkpoint(now, creditedUT, verified, body, situation);
        }

        internal bool ApplyResearchCheckpoint(double now, StationResearchCredit credit, bool verified,
            string body, string situation, int level)
        {
            if (state == null || incompatibleState != null) return false;
            ExperimentContainerState next = state.CheckpointResearch(now, credit.Points, credit.CoveredSeconds, verified, body, situation, level);
            if (ReferenceEquals(next, state) && credit.Points > 0) return false;
            state = next;
            return true;
        }

        internal bool TryPrepareResearch(double now, out StationExperimentDefinition definition,
            out ProtoCrewMember scientist, out ScientistSpecialtyProfile profile, out double required, bool readOnly = false)
        {
            definition = null!; scientist = null!; profile = null!; required = 0;
            if (state == null || !ScientistLevelHistory.Time(now) ||
                !StationExperimentCatalogLoader.Current.TryGet(experimentID, out definition)) return false;
            scientist = vessel?.GetVesselCrew().FirstOrDefault(c => c.name == state.AssignedKerbalName)!;
            if (!StationExperimentRuntime.ValidScientist(scientist, definition) ||
                !KerbalSpecialtyScenario.TryGetScientistProfile(state.AssignedKerbalName, out profile)) return false;
            if (!readOnly) KerbalSpecialtyScenario.ObserveScientistLevelAtCheckpoint(scientist);
            if (!readOnly && !KerbalSpecialtyScenario.TryGetScientistLevelSegments(scientist.name, now, now, out _)) return false;
            try
            {
                if (!readOnly) EnsureLaunchBudget();
                required = ResearchBudget(definition);
                if (!ExperimentContainerState.Finite(required) || required <= 0) return false;
                StationResearchMath.RatePerHour(profile.Score(definition.ScienceCategory), scientist.experienceLevel);
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        internal void RebaseCheckpoint(double now, string body, string situation)
        {
            if (state != null && incompatibleState == null)
                state = state.RebaseCheckpoint(now, body, situation);
            checkpointRebasePending = false;
        }

        internal bool TryFinalize(StationExperimentDefinition definition, double now,
            out string error)
        {
            error = "";
            if (state == null || incompatibleState != null ||
                state.Status != ExperimentContainerStatus.Running ||
                !state.WorkComplete()) return false;
            try
            {
                if (scienceData != null)
                {
                    state = FinalizedStateFromExistingData(state, scienceData, now);
                    RefreshReviewEvent();
                    return true;
                }
                CelestialBody? body = FlightGlobals.Bodies.FirstOrDefault(candidate => candidate != null &&
                    string.Equals(candidate.bodyName, state.CheckpointBody,
                        StringComparison.Ordinal));
                if (body == null) throw new InvalidOperationException(
                    "Certified celestial body is unavailable.");
                StationScienceResult result = StationScienceResultFactory.Create(state,
                    definition, body, vessel?.latitude ?? 0, vessel?.longitude ?? 0,
                    part.flightID, now);
                scienceData = result.Data;
                state = result.State;
                StationExperimentNotifications.Observe(state, vessel?.vesselName ?? "", StationExperimentPauseReason.ResultAvailable);
                checkpointRebasePending = false;
                RefreshReviewEvent();
                try { GameEvents.OnExperimentDeployed.Fire(result.Data); }
                catch (Exception eventError)
                {
                    Debug.LogError("[Koffee] Station ScienceData was finalized, but an " +
                        "OnExperimentDeployed listener failed: " + eventError);
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                LogError("Station experiment could not be finalized: " + exception);
                return false;
            }
        }

        public ScienceData[] GetData() => scienceData == null
            ? new ScienceData[0] : new[] { scienceData };

        public int GetScienceCount() => scienceData == null ? 0 : 1;

        public bool IsRerunnable() => false;

        public void ReviewData()
        {
            if (!CanReview() || resultsDialog != null) return;
            ScienceData data = scienceData!;
            var page = new ExperimentResultDialogPage(part, data, data.baseTransmitValue,
                data.transmitBonus, false, "", true, new ScienceLabSearch(vessel, data),
                DiscardResult, KeepResult, TransmitResult, SendResultToLab);
            page.UpdatePageLabValue();
            resultsDialog = ExperimentsResultDialog.DisplayResult(page);
            PartItemTransfer.DismissActive();
        }

        public void ReviewDataItem(ScienceData data)
        {
            if (ReferenceEquals(data, scienceData)) ReviewData();
        }

        public void DumpData(ScienceData data)
        {
            if (!ReferenceEquals(data, scienceData)) return;
            MarkExtracted("StockTransfer", Planetarium.GetUniversalTime());
        }

        public void ReturnData(ScienceData data)
        {
            if (data == null || scienceData != null ||
                state?.Status != ExperimentContainerStatus.Finalized) return;
            scienceData = data;
            RefreshReviewEvent();
        }

        private bool CanReview() => state?.Status == ExperimentContainerStatus.Finalized &&
            scienceData != null;

        private void KeepResult(ScienceData data)
        {
            resultsDialog = null;
            RefreshReviewEvent();
        }

        private void DiscardResult(ScienceData data)
        {
            if (!ReferenceEquals(data, scienceData) ||
                state?.Status != ExperimentContainerStatus.Finalized) return;
            state = state.ResetResult();
            scienceData = null;
            resultsDialog = null;
            checkpointRebasePending = false;
            RefreshReviewEvent();
        }

        private void TransmitResult(ScienceData data)
        {
            if (!ReferenceEquals(data, scienceData) || !CanReview()) return;
            IScienceDataTransmitter? transmitter = ScienceUtil.GetBestTransmitter(vessel);
            if (transmitter == null)
            {
                ScreenMessages.PostScreenMessage(Localizer.Format(
                    "#Koffee_Station_NoTransmitter"),
                    3f, ScreenMessageStyle.UPPER_CENTER);
                resultsDialog = null;
                RefreshReviewEvent();
                return;
            }
            transmitter.TransmitData(new List<ScienceData> { data });
            MarkExtracted("Transmission", Planetarium.GetUniversalTime());
        }

        private void SendResultToLab(ScienceData data)
        {
            if (!ReferenceEquals(data, scienceData) || !CanReview()) return;
            if (!(data.labValue > 0f))
            {
                resultsDialog = null;
                RefreshReviewEvent();
                return;
            }
            var search = new ScienceLabSearch(vessel, data);
            if (!search.NextLabForDataFound)
            {
                search.PostErrorToScreen();
                resultsDialog = null;
                RefreshReviewEvent();
                return;
            }
            ModuleScienceLab lab = search.NextLabForData;
            float dataStoredBefore = lab.dataStored;
            resultsDialog = null;
            RefreshReviewEvent();
            StartCoroutine(lab.ProcessData(data, accepted =>
            {
                if (ReferenceEquals(accepted, data) && ReferenceEquals(data, scienceData) &&
                    data.labValue > 0f && lab.dataStored > dataStoredBefore)
                    MarkExtracted("MobileProcessingLab", Planetarium.GetUniversalTime());
            }));
        }

        private void MarkExtracted(string kind, double now)
        {
            if (state?.Status != ExperimentContainerStatus.Finalized || scienceData == null) return;
            ExperimentContainerState spent = state.MarkSpent(Guid.NewGuid(), kind, now);
            state = spent;
            scienceData = null;
            resultsDialog = null;
            RefreshReviewEvent();
        }

        private ExperimentContainerState FinalizedStateFromExistingData(
            ExperimentContainerState running, ScienceData data, double now)
        {
            string body = running.SolarCapture?.Body ?? running.TelescopeCapture?.Target ?? running.CheckpointBody;
            string situation = running.SolarCapture?.Situation ?? (running.TelescopeCapture == null ? running.CheckpointSituation : TelescopeCapture.ScienceSituation);
            if (string.IsNullOrWhiteSpace(data.subjectID) || string.IsNullOrWhiteSpace(body) ||
                string.IsNullOrWhiteSpace(situation))
                throw new InvalidOperationException("Recovered ScienceData has no certified context.");
            return running.FinalizeRun(data.subjectID, body, situation, "", "", now);
        }

        private void ValidateScienceState()
        {
            if (state == null) return;
            if (state.Status == ExperimentContainerStatus.Running && scienceData != null)
                state = FinalizedStateFromExistingData(state, scienceData,
                    state.LastCheckpointUT ?? state.StartUT ?? 0);
            bool expectsData = state.Status == ExperimentContainerStatus.Finalized;
            if (expectsData != (scienceData != null))
                throw new InvalidOperationException("Container state and ScienceData disagree.");
            if (scienceData != null && !string.Equals(scienceData.subjectID,
                state.ResultSubjectId, StringComparison.Ordinal))
                throw new InvalidOperationException("Persisted ScienceData subject does not match its receipt.");
            if (scienceData != null && state.TelescopeCapture != null)
            {
                var target = FlightGlobals.GetBodyByName(state.TelescopeCapture.Target);
                if (target == null || target.isStar || scienceData.subjectID !=
                    experimentID + "@" + target.name + TelescopeCapture.ScienceSituation)
                    throw new InvalidOperationException("ScienceData does not belong to the captured target.");
            }
        }

        private void RefreshReviewEvent()
        {
            BaseEvent? review = Events?[nameof(ReviewExperiment)];
            if (review != null) review.active = CanReview() && resultsDialog == null;
        }

        internal double ResearchBudget(StationExperimentDefinition definition)
        {
            if (state != null && state.Status != ExperimentContainerStatus.Available)
                return state.ResearchPointsRequired;
            if (launchBudget > 0 && launchExperiment == definition.Id) return launchBudget;
            double factor = KoffeeResearchRates.Current?.DurationMultiplier ?? 1;
            return definition.ResearchPointsRequired * factor;
        }

        private void EnsureLaunchBudget()
        {
            if (!HighLogic.LoadedSceneIsFlight || incompatibleState != null ||
                (launchBudget > 0 && launchExperiment == experimentID) ||
                !StationExperimentCatalogLoader.Current.TryGet(experimentID, out var definition)) return;
            double required = ResearchBudget(definition);
            if (!ExperimentContainerState.Finite(required) || required <= 0) return;
            launchBudget = required;
            launchExperiment = experimentID;
        }

        private void EnsureRegistered()
        {
            if (state != null || incompatibleState != null) return;
            // Stock saves modules while preparing a craft, before assembly
            // assigns flight IDs. This is a pending registration, not bad data.
            if (part == null || part.flightID == 0) return;
            if (string.IsNullOrWhiteSpace(experimentID) ||
                !StationExperimentCatalogLoader.Current.TryGet(experimentID, out _))
            {
                LogError("Container has unknown station experimentID '" + experimentID + "'.");
                return;
            }
            state = ExperimentContainerRegistration.Register(null, experimentID, Guid.NewGuid);
            Debug.Log("[Koffee] Registered station experiment container " +
                state.ContainerId.ToString("N") + " for " + experimentID +
                "; flightID=" + part.flightID + ".");
        }

        private string FixedExperimentId()
        {
            Part? prefab = part?.partInfo?.partPrefab;
            if (prefab == null || ReferenceEquals(prefab, part)) return experimentID ?? "";
            ModuleKoffeeExperiment[] modules = prefab.Modules
                .OfType<ModuleKoffeeExperiment>().ToArray();
            return modules.Length == 1 ? modules[0].experimentID ?? "" : experimentID ?? "";
        }

        private void LogError(string message)
        {
            if (diagnosticLogged) return;
            diagnosticLogged = true;
            Debug.LogError("[Koffee] " + message);
        }
    }
}

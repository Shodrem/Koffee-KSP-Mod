using System;
using System.Globalization;
using System.Linq;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    public sealed class ModuleKoffeeExperimentManager : PartModule
    {
        // Shared model contract for every Manager version; no model animation needed.
        [KSPField] public string ledTransform = "LED_CPU";
        private Renderer[] ledRenderers = Array.Empty<Renderer>();
        private Color[] ledOnColors = Array.Empty<Color>();
        private MaterialPropertyBlock? ledProperties;
        private bool? ledLit;

        [KSPField(isPersistant = true)] public string assignedEngineer = "";
        [KSPField(isPersistant = true)] public string topologySignature = "";
        [KSPField(isPersistant = true)] public double validSinceUT;
        [KSPField(isPersistant = true)] public uint activeManagerId;
        [KSPField(guiActive = true, guiName = " ", groupName = "EngineerAssignment",
            groupDisplayName = "#Koffee_Manager_SelectEngineer", groupStartCollapsed = false)]
        [UI_ChooseOption(scene = UI_Scene.Flight, affectSymCounterparts = UI_Scene.None)]
        public string engineerCandidate = "";
        [KSPField(guiActive = true, guiName = " ")] public string managerSpacer = " ";
        [KSPField(guiActive = true, guiName = "#Koffee_Manager_Status")] public string managerStatus = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Manager_Engineer")] public string engineerDisplay = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Manager_Kaffeinated")] public string kaffeinated = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Manager_Koffee")] public string cups = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Manager_Count")] public string experimentCount = "";

        public override void OnStart(StartState startState)
        {
            base.OnStart(startState);
            InitializeLed();
            if (startState == StartState.Editor)
            {
                assignedEngineer = topologySignature = "";
                validSinceUT = 0;
                activeManagerId = 0;
            }
            Update();
        }

        private void InitializeLed()
        {
            Transform? led = string.IsNullOrEmpty(ledTransform) ? null : part.FindModelTransform(ledTransform);
            ledRenderers = led == null ? Array.Empty<Renderer>() : led.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Color") &&
                    r.sharedMaterial.HasProperty("_EmissiveColor")).ToArray();
            ledOnColors = ledRenderers.Select(r => r.sharedMaterial.GetColor("_Color")).ToArray();
            ledProperties = ledRenderers.Length == 0 ? null : new MaterialPropertyBlock();
            ledLit = null;
            SetLed(false);
        }

        private void SetLed(bool lit)
        {
            if (ledLit == lit || ledProperties == null) return;
            for (int i = 0; i < ledRenderers.Length; i++)
            {
                Renderer renderer = ledRenderers[i];
                if (renderer == null) continue;
                // Per-renderer overrides preserve shared materials and other property overrides.
                renderer.GetPropertyBlock(ledProperties);
                Color color = ledOnColors[i];
                ledProperties.SetColor("_Color", lit ? color :
                    new Color(color.r * 0.04f, color.g * 0.04f, color.b * 0.04f, color.a));
                ledProperties.SetColor("_EmissiveColor", lit ? Color.white : Color.black);
                renderer.SetPropertyBlock(ledProperties);
            }
            ledLit = lit;
        }

        private static string Text(string key) => Localizer.Format("#Koffee_Manager_" + key);
        private ProtoCrewMember[] Engineers() => vessel.GetVesselCrew()
            .Where(ExperimentManagerService.ValidEngineer)
            .Where(c => !ExperimentManagerService.Managers(vessel)
                .Any(m => !ReferenceEquals(m, this) && m.assignedEngineer == c.name))
            .OrderBy(c => c.name, StringComparer.Ordinal).ToArray();
        public void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded)
            {
                SetLed(false);
                return;
            }
            ProtoCrewMember[] engineers = Engineers();
            if (!engineers.Any(c => c.name == engineerCandidate))
                engineerCandidate = engineers.FirstOrDefault(c => c.name == assignedEngineer)?.name ??
                    engineers.FirstOrDefault()?.name ?? "";
            StationPersonnelSelector.Refresh(Fields[nameof(engineerCandidate)],
                engineers.Length == 0 ? new[] { "" } : engineers.Select(c => c.name).ToArray(),
                engineers.Length == 0 ? new[] { Text("None") } : engineers.Select(c => c.name).ToArray(), true);
            engineerDisplay = string.IsNullOrEmpty(assignedEngineer) ? Text("None") : assignedEngineer;
            ProtoCrewMember? assigned = vessel.GetVesselCrew().FirstOrDefault(c => c.name == assignedEngineer);
            kaffeinated = KerbalWorkState.FormatDuration(assigned == null ? 0 : KerbalWorkState.GetKaffeinatedRemaining(assigned));
            cups = KoffeeSources.Available(part).ToString(CultureInfo.InvariantCulture);
            var active = ExperimentManagerService.Select(vessel, Planetarium.GetUniversalTime(), false, out var reason);
            var all = StationExperimentRuntime.Capture(vessel);
            experimentCount = all.Count.ToString(CultureInfo.InvariantCulture);
            bool conflict = !string.IsNullOrEmpty(assignedEngineer) && ExperimentManagerService.Managers(vessel)
                .Any(m => m.part.persistentId < part.persistentId && m.assignedEngineer == assignedEngineer);
            bool working = all.Any(e => e.State?.Status == ExperimentContainerStatus.Running &&
                !e.WorkComplete && e.PauseReason == StationExperimentPauseReason.None);
            SetLed(!conflict && ExperimentManagerService.ValidEngineer(assigned) &&
                active != null && active.Id == part.persistentId && working);
            managerStatus = conflict ? Text("Conflict") : string.IsNullOrEmpty(assignedEngineer)
                ? Localizer.Format("#Koffee_Station_Pause_NoEngineerAssigned")
                : !ExperimentManagerService.ValidEngineer(assigned)
                ? Localizer.Format("#Koffee_Station_Pause_EngineerAbsentOrInvalid") : active == null
                ? Localizer.Format("#Koffee_Station_Pause_" + reason)
                : active.Id != part.persistentId ? Text("Reserve")
                : working ? Text("Online") : all.Any(e => e.PauseReason == StationExperimentPauseReason.NoElectricity)
                ? Localizer.Format("#Koffee_Station_Pause_NoElectricity") : Text("Standby");
            SetEvent(nameof(AssignEngineer), engineers.Length > 0);
            SetEvent(nameof(ReleaseEngineer), !string.IsNullOrEmpty(assignedEngineer));
        }
        private void SetEvent(string name, bool enabled) { if (Events[name] != null) Events[name].active = enabled; }
        private void Changed()
        {
            topologySignature = "";
            validSinceUT = Planetarium.GetUniversalTime();
            foreach (var experiment in vessel.parts.SelectMany(p => p.Modules.OfType<ModuleKoffeeExperiment>()))
                if (StationExperimentCatalogLoader.Current.TryGet(experiment.experimentID, out _))
                    experiment.RebaseCheckpoint(validSinceUT, StationExperimentRuntime.CurrentBody(vessel), StationExperimentRuntime.CurrentSituation(vessel));
            Update();
        }
        [KSPEvent(guiActive = true, guiName = "#Koffee_PAW_Assign", groupName = "EngineerAssignment",
            groupDisplayName = "#Koffee_Manager_SelectEngineer", groupStartCollapsed = false)]
        public void AssignEngineer()
        {
            if (assignedEngineer != engineerCandidate && Engineers().Any(c => c.name == engineerCandidate))
            {
                assignedEngineer = engineerCandidate;
                Changed();
            }
        }
        [KSPEvent(guiActive = true, guiName = "#Koffee_Manager_ReleaseEngineer", groupName = "EngineerAssignment",
            groupDisplayName = "#Koffee_Manager_SelectEngineer", groupStartCollapsed = false)]
        public void ReleaseEngineer() { assignedEngineer = ""; Changed(); }
    }
}

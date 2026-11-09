using System;
using System.Linq;
using System.Globalization;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    // Tariffs/capacity are declarative; the vessel coordinator owns payment.
    // PAW and action-group controls share the persisted operating state.
    public sealed class ModuleKomfortService : PartModule
    {
        [KSPField] public string service = "Climate";
        [KSPField(isPersistant = true)] public bool energySupportRestore;
        [KSPField] public string indicatorTransforms = "";
        [KSPField] public string inactiveIndicatorTransforms = "";
        [KSPField] public string overloadIndicatorTransforms = "";
        private bool overCapacity;
        private KomfortServiceIndicators? indicators;
        [KSPField] public double capacity = 1;
        [KSPField] public double electricChargePerSecond = .02;
        [KSPField] public double heatKilowattsPerKerbal = 5;
        [KSPField(isPersistant = true)] public bool serviceEnabled = true;
        [KSPField] public double lifetimeSeconds;
        [KSPField(isPersistant = true)] public double maintenanceBaseHours;
        internal double MaintenanceBaseHours => EngineeringMath.Finite(maintenanceBaseHours) && maintenanceBaseHours > 0
            ? maintenanceBaseHours : EngineeringSettingsLoader.Current.MaintenanceHours;
        [KSPField(isPersistant = true)] public double lifeRemainingSeconds = -1;

        [KSPField] public int maintenanceKits = 1;
        [KSPField(guiActive = true, guiActiveUnfocused = true, unfocusedRange = MaintenanceEva.Range, guiName = "#Koffee_Maintenance_Autonomy")] public string autonomyDisplay = "";
        [KSPField(guiActive = true, guiActiveUnfocused = true, unfocusedRange = MaintenanceEva.Range, guiName = "#Koffee_Maintenance_Status")] public string maintenanceDisplay = "";
        internal KomfortMaintenanceState? Maintenance;
        private ConfigNode[] originalMaintenance = Array.Empty<ConfigNode>();
        private string actionError = "";
        internal bool Maintainable => lifetimeSeconds <= 0 && (service == "Climate" || service == "Air" || service == "Hygiene");
        private bool AutonomyExhausted => lifetimeSeconds > 0 ? KomfortServiceLife.RemainingFor(this) <= 0 :
            Maintainable && Maintenance?.RemainingHours <= 0;
        internal bool MaintenanceInvalid => Maintenance == null && originalMaintenance.Length > 0;
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (node.HasValue(nameof(lifeRemainingSeconds)))
                lifeRemainingSeconds = double.TryParse(node.GetValue(nameof(lifeRemainingSeconds)), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double remaining) ? remaining : 0;
            if (bool.TryParse(node.GetValue(nameof(serviceEnabled)), out bool on)) serviceEnabled = on;
            if (bool.TryParse(node.GetValue(nameof(energySupportRestore)), out bool restore)) energySupportRestore = restore;
            var saved = node.GetNodes(KomfortMaintenanceState.NodeName);
            if (saved.Length == 0) return; // partial stock upgrade load
            originalMaintenance = saved.Select(n => n.CreateCopy()).ToArray();
            Maintenance = saved.Length == 1 && KomfortMaintenanceState.TryRead(saved[0], out var state) ? state : null;
        }
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (lifetimeSeconds > 0) node.SetValue(nameof(lifeRemainingSeconds), lifeRemainingSeconds.ToString("R", CultureInfo.InvariantCulture), true);
            node.SetValue(nameof(serviceEnabled), serviceEnabled.ToString(), true);
            node.SetValue(nameof(energySupportRestore), energySupportRestore.ToString(), true);
            node.RemoveNodes(KomfortMaintenanceState.NodeName);
            if (Maintenance != null) node.AddNode(Maintenance.Save());
            else foreach (var saved in originalMaintenance) node.AddNode(saved.CreateCopy());
        }
        public override void OnStart(StartState startState)
        {
            base.OnStart(startState);
            indicators = new KomfortServiceIndicators(part, indicatorTransforms, overloadIndicatorTransforms, inactiveIndicatorTransforms);
            RefreshDisplay();
        }
        private float nextDisplayUpdate;
        private bool? displayedServiceEnabled;
        private void RefreshDisplay() { nextDisplayUpdate = 0; Update(); }

        public void OnDestroy() => indicators?.Dispose();

        public void Update()
        {
            indicators?.SetEnabled(serviceEnabled, overCapacity, exhausted: AutonomyExhausted);
            var toggle = Events[nameof(ToggleService)];
            if (toggle != null)
            {
                if (displayedServiceEnabled != serviceEnabled)
                {
                    toggle.guiName = Localizer.Format(serviceEnabled ? "#Koffee_Komfort_ServiceOff" : "#Koffee_Komfort_ServiceOn");
                    displayedServiceEnabled = serviceEnabled;
                }
                toggle.active = service != "Emergency" && (!Maintainable || Maintenance?.InProgress != true);
            }
            if (!HighLogic.LoadedSceneIsFlight || vessel?.loaded != true) return;
            if (UnityEngine.Time.realtimeSinceStartup < nextDisplayUpdate) return;
            nextDisplayUpdate = UnityEngine.Time.realtimeSinceStartup + .5f;
            overCapacity = serviceEnabled && !string.IsNullOrEmpty(overloadIndicatorTransforms) && KomfortRuntime.ServiceOverCapacity(vessel, service);
            // Startup/unpack can restore material colors after OnStart. Reassert
            // the persisted switch state on this bounded refresh, without toggling it.
            indicators?.SetEnabled(serviceEnabled, overCapacity, force: true, exhausted: AutonomyExhausted);
            foreach (string field in new[] { nameof(autonomyDisplay), nameof(maintenanceDisplay) })
                if (Fields[field] != null)
                    Fields[field].guiActive = Fields[field].guiActiveUnfocused = Maintainable || (field == nameof(autonomyDisplay) && lifetimeSeconds > 0);
            double remainingHours = lifetimeSeconds > 0 ? KomfortServiceLife.RemainingFor(this) / 3600 :
                Maintenance?.RemainingHours ?? MaintenanceBaseHours;
            double grantedHours = lifetimeSeconds > 0 ? lifetimeSeconds / 3600 :
                Maintenance?.GrantedHours ?? MaintenanceBaseHours;
            autonomyDisplay = MaintenanceInvalid ? Localizer.Format("#Koffee_Crew_NoData") : Localizer.Format("#Koffee_Maintenance_Hours",
                remainingHours.ToString("0.##", CultureInfo.CurrentCulture), grantedHours.ToString("0.##", CultureInfo.CurrentCulture));
            if (!Maintainable)
            {
                foreach (string action in new[] { nameof(StartMaintenance), nameof(PauseMaintenance), nameof(CancelMaintenance) })
                    if (Events[action] != null) Events[action].active = false;
                return;
            }
            maintenanceDisplay = Localizer.Format("#Koffee_Maintenance_" + (MaintenanceInvalid ? "Invalid" : Maintenance?.InProgress == true ?
                (Maintenance.Paused ? "Paused" : MaintenanceEva.Nearby(part, Maintenance.Engineer) == null ? "EvaNoEngineer" :
                !KomfortRuntime.OptionsAt(Planetarium.GetUniversalTime()).Enabled ? "Disabled" : "Working") :
                Maintenance?.RemainingHours <= 0 ? "Degraded" : serviceEnabled ? "Operational" : "Off"));
            if (Maintenance?.InProgress == true) maintenanceDisplay += " — " + Maintenance.Engineer + " — " +
                (100 * Maintenance.Progress).ToString("0.#", CultureInfo.CurrentCulture) + " %";
            else maintenanceDisplay += " — " + Localizer.Format("#Koffee_EvaMaintenance_Requirement", "1",
                EngineeringSettingsLoader.Current.MaintenanceSeconds.ToString("0.#", CultureInfo.CurrentCulture));
            if (Events[nameof(StartMaintenance)] != null) Events[nameof(StartMaintenance)].active = !MaintenanceInvalid && Maintenance != null;
            if (Events[nameof(PauseMaintenance)] != null) Events[nameof(PauseMaintenance)].active = Maintenance?.InProgress == true;
            if (Events[nameof(CancelMaintenance)] != null) Events[nameof(CancelMaintenance)].active = Maintenance?.InProgress == true;
            if (actionError != "") maintenanceDisplay += " — " + Localizer.Format("#Koffee_Maintenance_" + actionError);
        }
        [KSPEvent(guiActive = false, guiActiveUnfocused = true, externalToEVAOnly = true, unfocusedRange = MaintenanceEva.Range, guiName = "#Koffee_EvaMaintenance_Start")]
        public void StartMaintenance()
        {
            if (!Maintainable || !HighLogic.LoadedSceneIsFlight || vessel?.loaded != true) return;
            actionError = "";
            var engineer = MaintenanceEva.Nearby(part);
            if (engineer == null) { actionError = "EvaNoEngineer"; RefreshDisplay(); return; }
            if (!EngineeringSettingsLoader.Current.Enabled || !KomfortRuntime.OptionsAt(Planetarium.GetUniversalTime()).Enabled)
            { actionError = "Disabled"; RefreshDisplay(); return; }
            if (MaintenanceInvalid) { actionError = "Invalid"; RefreshDisplay(); return; }
            double now = Planetarium.GetUniversalTime();
            bool wasInProgress = Maintenance?.InProgress == true;
            StationExperimentRuntime.Tick(vessel, now);
            if (Maintenance == null || (wasInProgress && !Maintenance.InProgress)) { RefreshDisplay(); return; }
            if (EngineerBusy(engineer.name))
            { MaintenanceEva.ShowError("#Koffee_EvaMaintenance_Busy"); return; }
            if (!Maintenance.InProgress)
            {
                var inventory = FlightGlobals.ActiveVessel?.evaController?.part.FindModuleImplementing<ModuleInventoryPart>();
                if (inventory == null || MaintenanceEva.KitCount(inventory) < 1)
                { MaintenanceEva.ShowError("#Koffee_EvaMaintenance_NeedsKits"); return; }
                int before = MaintenanceEva.KitCount(inventory);
                try { inventory.RemoveNPartsFromInventory(MaintenanceEva.Kit, 1, false); }
                catch (Exception error) { UnityEngine.Debug.LogError("[Koffee] EVA maintenance kit removal: " + error); }
                if (before - MaintenanceEva.KitCount(inventory) != 1) return;
                Maintenance.KitsPaid = true; Maintenance.ProgressSeconds = 0;
            }
            bool coffee = KerbalWorkState.IsKaffeinatedAt(engineer, now);
            if (!coffee)
            {
                var sources = KoffeeSources.NearestMaintenanceCup(part, FlightGlobals.ActiveVessel!.evaController.part.transform.position);
                using (var attempt = KerbalWorkState.TryBeginKaffeination(engineer, part, now, false, sources))
                {
                    if (attempt != null) { attempt.Commit(); coffee = true; }
                }
            }
            KomfortMaintenance.PrepareRepair(Maintenance, engineer.name, coffee, KomfortRuntime.QuotedFactor(vessel, now), MaintenanceBaseHours);
            // No reservation on the vessel: the actual nearby EVA engineer performs the work.
            Maintenance.Engineer = engineer.name; Maintenance.InProgress = true; Maintenance.Paused = false;
            serviceEnabled = false; KomfortRuntime.Invalidate(vessel); KomfortRuntime.Observe(vessel, now);
            RefreshDisplay();
        }
        [KSPEvent(guiActive = false, guiActiveUnfocused = true, externalToEVAOnly = true, unfocusedRange = MaintenanceEva.Range, guiName = "#Koffee_Maintenance_Pause")]
        public void PauseMaintenance()
        {
            if (!Maintainable || Maintenance?.InProgress != true || vessel?.loaded != true || MaintenanceEva.Nearby(part) == null) return;
            StationExperimentRuntime.Tick(vessel, Planetarium.GetUniversalTime());
            if (!Maintenance.InProgress) { RefreshDisplay(); return; }
            if (Maintenance.Paused && EngineerBusy(Maintenance.Engineer))
            { MaintenanceEva.ShowError("#Koffee_EvaMaintenance_Busy"); return; }
            Maintenance.Paused = !Maintenance.Paused; KomfortRuntime.Invalidate(vessel); KomfortRuntime.Observe(vessel, Planetarium.GetUniversalTime()); RefreshDisplay();
        }

        [KSPEvent(guiActive = false, guiActiveUnfocused = true, externalToEVAOnly = true, unfocusedRange = MaintenanceEva.Range, guiName = "#Koffee_Maintenance_Cancel")]
        public void CancelMaintenance()
        {
            if (!Maintainable || Maintenance?.InProgress != true || vessel?.loaded != true || MaintenanceEva.Nearby(part) == null) return;
            double now = Planetarium.GetUniversalTime();
            StationExperimentRuntime.Tick(vessel, now);
            if (!Maintenance.InProgress) return;
            Maintenance.InProgress = Maintenance.Paused = Maintenance.KitsPaid = false;
            Maintenance.ProgressSeconds = 0;
            Maintenance.RepairSeconds = Maintenance.RepairStartHours = Maintenance.RepairTargetHours = 0;
            serviceEnabled = Maintenance.RemainingHours > 0;
            KomfortRuntime.Invalidate(vessel); KomfortRuntime.Observe(vessel, now); RefreshDisplay();
        }

        private bool EngineerBusy(string engineer) => KomfortMaintenance.Capture(vessel).Any(u =>
            !ReferenceEquals(u.Definition, this) && u.State?.InProgress == true && !u.State.Paused && u.State.Engineer == engineer);

        [KSPAction("#Koffee_Komfort_Toggle")]
        public void Toggle(KSPActionParam action) => ToggleService();
        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "#Koffee_Komfort_Toggle")]
        public void ToggleService()
        {
            SetServiceEnabled(!serviceEnabled);
        }
        internal void SetServiceEnabled(bool enabled, bool coordinated = false)
        {
            if (service == "Emergency" || enabled == serviceEnabled) return;
            if (enabled && (Maintainable && Maintenance?.InProgress == true || DeadlyScenario.Active(vessel))) return;
            var flightVessel = vessel;
            if (!HighLogic.LoadedSceneIsFlight || flightVessel?.loaded != true) flightVessel = null;
            if (!coordinated && flightVessel != null) StationExperimentRuntime.Tick(flightVessel, Planetarium.GetUniversalTime());
            serviceEnabled = enabled;
            if (!coordinated && flightVessel != null) { KomfortRuntime.Invalidate(flightVessel); KomfortRuntime.Observe(flightVessel, Planetarium.GetUniversalTime()); }
            if (!coordinated) RefreshDisplay();
        }
        internal bool Valid => (service == "Climate" || service == "Air" || service == "Leisure" || service == "Hygiene" || service == "Emergency") &&
            ExperimentContainerState.Finite(capacity) && capacity > 0 &&
            ExperimentContainerState.Finite(lifetimeSeconds) && lifetimeSeconds >= 0 &&
            ExperimentContainerState.Finite(electricChargePerSecond) && electricChargePerSecond >= 0;
    }

    // Eligibility, not extra capacity: each physical part is counted once.
    public sealed class ModuleKomfortHabitat : PartModule
    {
        [KSPField] public bool laboratory;
        [KSPField] public bool eligible = true;
        [KSPField] public string deploymentModule = "";
        [KSPField] public int deployedCapacity;
        [KSPField] public int retractedCapacity;
    }
}

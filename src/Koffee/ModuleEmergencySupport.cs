using System;
using System.Linq;
using System.Globalization;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    // Vessel-wide Energy Support. Filtration has no crew assignment or seat limit.
    public sealed class ModuleEmergencySupport : PartModule
    {
        // Generator output; the Komfort service declares its own consumption.
        [KSPField] public double electricChargePerSecond = .05;
        [KSPField] public double heatKilowatts = .25;
        [KSPField] public string generationBackend = "Stock";
        [KSPField] public double reactorFuelPerSecond;
        internal bool UsesNfeReactor => generationBackend == "SystemHeatFissionReactor";
        internal bool NuclearFuelExhausted => UsesNfeReactor &&
            (Resource("EnrichedUranium")?.amount ?? 0) <= EmergencyNfeReactor.FuelReserve;
        private PartResource? Resource(string name)
        {
            var definition = PartResourceLibrary.Instance.GetDefinition(name);
            return definition == null ? null : part.Resources.Get(definition.id);
        }
        [KSPField(guiActive = true, guiActiveEditor = true, guiActiveUnfocused = true, unfocusedRange = MaintenanceEva.Range, guiName = "#Koffee_Emergency_Fuel")]
        public string nuclearFuelDisplay = "";
        [KSPField(guiActive = true, guiActiveEditor = true, guiActiveUnfocused = true, unfocusedRange = MaintenanceEva.Range, guiName = "#Koffee_Emergency_Years")]
        public string nuclearYearsDisplay = "";
        [KSPField(guiActive = true, guiActiveEditor = true, guiActiveUnfocused = true, unfocusedRange = MaintenanceEva.Range, guiName = "#Koffee_Emergency_Waste")]
        public string nuclearWasteDisplay = "";
        private float nextNuclearDisplay;
        internal ModuleKomfortService? Service
        {
            get
            {
                var modules = part.Modules.OfType<ModuleKomfortService>().Where(s => s.service == "Emergency").ToArray();
                return modules.Length == 1 ? modules[0] : null;
            }
        }
        internal bool Configured
        {
            get
            {
                if (UsesNfeReactor)
                    return part.Modules.OfType<ModuleEmergencySupport>().Count() == 1 &&
                        part.Modules.OfType<ModuleKomfortService>().Count() == 1 && Service?.Valid == true &&
                        Service.lifetimeSeconds == 0 &&
                        ExperimentContainerState.Finite(heatKilowatts) && heatKilowatts >= 0 &&
                        EmergencyNfeReactor.Valid(this);
                var generators = part.Modules.OfType<ModuleGenerator>().ToArray();
                if (generationBackend != "Stock" || part.Modules.OfType<ModuleEmergencySupport>().Count() != 1 || generators.Length != 1 ||
                    part.Modules.OfType<ModuleKomfortService>().Count() != 1 ||
                    generators[0].isAlwaysActive || generators[0].isThrottleControlled || Service?.Valid != true || Service.lifetimeSeconds != 0 ||
                    !ExperimentContainerState.Finite(heatKilowatts) || heatKilowatts < 0 ||
                    !ExperimentContainerState.Finite(Service.heatKilowattsPerKerbal) ||
                    Math.Abs(Service.heatKilowattsPerKerbal - heatKilowatts) > 1e-9) return false;
                var outputs = generators[0].resHandler.outputResources;
                return generators[0].resHandler.inputResources.Count == 0 && outputs.Count == 1 && outputs[0].name == "ElectricCharge" &&
                    Math.Abs(outputs[0].rate - electricChargePerSecond) <= 1e-9;
            }
        }
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            foreach (var generator in part.Modules.OfType<ModuleGenerator>())
            {
                foreach (BaseEvent evt in generator.Events) evt.guiActive = evt.guiActiveEditor = false;
                foreach (BaseAction action in generator.Actions) action.active = false;
            }
            if (UsesNfeReactor) EmergencyNfeReactor.HideControls(part);
            Update();
        }
        public void Update()
        {
            UpdateNuclearDisplay();
        }
        private void UpdateNuclearDisplay()
        {
            foreach (string name in new[] { nameof(nuclearFuelDisplay), nameof(nuclearYearsDisplay), nameof(nuclearWasteDisplay) })
                if (Fields[name] != null) Fields[name].guiActive = Fields[name].guiActiveEditor = Fields[name].guiActiveUnfocused = UsesNfeReactor;
            if (!UsesNfeReactor || UnityEngine.Time.realtimeSinceStartup < nextNuclearDisplay) return;
            nextNuclearDisplay = UnityEngine.Time.realtimeSinceStartup + .5f;
            var fuel = Resource("EnrichedUranium"); var waste = Resource("DepletedFuel");
            string Tank(PartResource? resource) => resource == null ? Localizer.Format("#Koffee_Crew_NoData") :
                Localizer.Format("#Koffee_Emergency_Units", resource.amount.ToString("0.000", CultureInfo.CurrentCulture), resource.maxAmount.ToString("0.000", CultureInfo.CurrentCulture));
            nuclearFuelDisplay = Tank(fuel); nuclearWasteDisplay = Tank(waste);
            double usable = Math.Max(0, Math.Min(fuel?.amount ?? 0, waste == null ? 0 : waste.maxAmount - waste.amount) - EmergencyNfeReactor.FuelReserve);
            nuclearYearsDisplay = fuel == null || waste == null || !(reactorFuelPerSecond > 0) ? Localizer.Format("#Koffee_Crew_NoData") :
                Localizer.Format("#Koffee_Emergency_YearsValue", (usable / reactorFuelPerSecond / 9201600).ToString("0.0", CultureInfo.CurrentCulture));
        }
    }
}

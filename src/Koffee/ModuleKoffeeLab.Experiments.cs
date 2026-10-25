using System;
using System.Linq;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    // This gate affects long experiments only. Stock conversion never consults it.
    public sealed partial class ModuleKoffeeLab : PartModule
    {
        [KSPField]
        public string stationCapabilities = "StationBasic";
        [KSPField]
        public string requiredExperimentUpgrade = "";
        [KSPField(isPersistant = true)]
        public bool experimentExecutionEnabled = true;
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "#LOC_Koffee_StockLabUpgrade_Status")]
        public string experimentCapabilityDisplay = "";

        internal int StationSlotCapacity => StationLabRules.SlotCapacity(part?.CrewCapacity ?? 0);

        // Configuration authority stays on the prefab, never on imported MODULE values.
        private ModuleKoffeeLab? Definition
        {
            get
            {
                var prefab = part?.partInfo?.partPrefab;
                if (prefab == null) return this;
                var modules = prefab.Modules?.OfType<ModuleKoffeeLab>().ToArray() ?? Array.Empty<ModuleKoffeeLab>();
                return modules.Length == 1 ? modules[0] : null;
            }
        }

        internal static bool UpgradeAvailable(string id) => string.IsNullOrEmpty(id) ||
            (PartUpgradeManager.Handler != null && PartUpgradeManager.Handler.IsUnlocked(id) &&
             PartUpgradeManager.Handler.IsEnabled(id));

        internal static bool ReadExecutionFlag(ConfigNode? node)
        {
            string[] values = node?.GetValues(nameof(experimentExecutionEnabled)) ?? Array.Empty<string>();
            return values.Length == 1 && bool.TryParse(values[0], out bool enabled) && enabled;
        }

        internal bool ExecutionAvailable => Definition is ModuleKoffeeLab definition &&
            (string.IsNullOrEmpty(definition.requiredExperimentUpgrade) ||
             (experimentExecutionEnabled && UpgradeAvailable(definition.requiredExperimentUpgrade)));

        internal bool Supports(StationLabCapability capability) => ExecutionAvailable &&
            HasCapability(string.IsNullOrEmpty(Definition!.requiredExperimentUpgrade)
                ? stationCapabilities : Definition.stationCapabilities, capability);

        internal static bool HasCapability(string capabilities, StationLabCapability capability) =>
            (capabilities ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(value => string.Equals(value, capability.ToString(), StringComparison.Ordinal));

        internal bool SnapshotExecutionAvailable(ConfigNode? node) =>
            string.IsNullOrEmpty(requiredExperimentUpgrade) ||
            (ReadExecutionFlag(node) && UpgradeAvailable(requiredExperimentUpgrade));

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            // Stock can call OnLoad with just CURRENTUPGRADE. Missing fields in
            // that callback must not reset an already prepared module.
            if (node.name == "CURRENTUPGRADE" && !node.HasValue(nameof(experimentExecutionEnabled))) return;
            experimentExecutionEnabled = ReadExecutionFlag(node);
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            node.RemoveValues(nameof(experimentExecutionEnabled));
            node.AddValue(nameof(experimentExecutionEnabled), experimentExecutionEnabled.ToString());
        }

        public override string GetInfo()
        {
            string required = Definition?.requiredExperimentUpgrade ?? "";
            if (!string.IsNullOrEmpty(required) && !ExecutionAvailable)
                return Localizer.Format("#LOC_Koffee_StockLabUpgrade_Required",
                    Localizer.Format("#LOC_Koffee_StockLabUpgrade_Title"));
            return Localizer.Format("#Koffee_Lab_OfferedCapabilities",
                string.Join(", ", (string.IsNullOrEmpty(required) ? stationCapabilities : Definition!.stationCapabilities)
                    .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => Enum.TryParse(value, false, out StationLabCapability capability) && Enum.IsDefined(typeof(StationLabCapability), capability)
                        ? KoffeeResearchDisplay.Laboratory(capability) : value)));
        }

        private void RefreshExperimentDisplay()
        {
            bool gated = !string.IsNullOrEmpty(Definition?.requiredExperimentUpgrade);
            Fields[nameof(experimentCapabilityDisplay)].guiActive = gated;
            Fields[nameof(experimentCapabilityDisplay)].guiActiveEditor = gated;
            experimentCapabilityDisplay = gated ? GetInfo() : "";
        }
    }
}

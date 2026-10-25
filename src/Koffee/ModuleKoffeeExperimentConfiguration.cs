using KSP.Localization;

namespace ShoAerospace.Koffee
{
    // Catalog records have a stock R&D icon and tooltip, but are never built.
    public sealed class ModuleKoffeeExperimentConfiguration : PartModule
    {
        [KSPField] public string hardwareModel = "";

        public override string GetModuleDisplayName() => Localizer.Format("#Koffee_Selector_ConfigurationTitle");

        public override string GetInfo() => Localizer.Format("#Koffee_Selector_ConfigurationInfo",
            Localizer.Format(hardwareModel == "ShoAerospace/Koffee/Parts/Experiments_Device_ED-01/Experiment_Device_ED01"
                ? "#Koffee_Selector_DeviceFormat" : "#Koffee_Selector_ContainerFormat"));
    }
}

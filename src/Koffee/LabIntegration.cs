namespace ShoAerospace.Koffee
{
    internal static class LabIntegration
    {
        internal static ModuleKoffeeLab? FindOptedInLab(PartModule module)
        {
            if (!(module is ModuleScienceConverter) ||
                module.GetType() != typeof(ModuleScienceConverter) ||
                module.part == null || module.part.partInfo == null) return null;
            return module.part.FindModuleImplementing<ModuleKoffeeLab>();
        }
    }
}

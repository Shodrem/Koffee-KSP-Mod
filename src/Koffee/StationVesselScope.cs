namespace ShoAerospace.Koffee
{
    internal static class StationVesselScope
    {
        // Inspect physical parts rather than VesselType: a captured asteroid
        // shares a vessel with the tug, whose own modules must keep working.
        internal static bool IsNaturalObject(Vessel vessel)
        {
            if (vessel.loaded && vessel.parts.Count > 0)
            {
                foreach (Part part in vessel.parts)
                    if (!NaturalPart(part?.partInfo?.name, part)) return false;
                return true;
            }
            var parts = vessel.protoVessel?.protoPartSnapshots;
            if (parts == null || parts.Count == 0) return false;
            foreach (ProtoPartSnapshot part in parts)
            {
                if (part == null) return false;
                if (NaturalPart(part.partName, part.partPrefab ?? part.partInfo?.partPrefab)) continue;
                // Some modded objects retain only their module snapshots.
                bool natural = false;
                foreach (ProtoPartModuleSnapshot module in part.modules)
                    if (module.moduleName == nameof(ModuleAsteroid) || module.moduleName == nameof(ModuleComet))
                    { natural = true; break; }
                if (!natural) return false;
            }
            return true;
        }

        private static bool NaturalPart(string? name, Part? part) =>
            name == "PotatoRoid" || name == "PotatoComet" ||
            part?.FindModuleImplementing<ModuleAsteroid>() != null ||
            part?.FindModuleImplementing<ModuleComet>() != null;
    }
}

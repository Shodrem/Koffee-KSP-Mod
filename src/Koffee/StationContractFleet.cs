using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal static class StationContractFleet
    {
        private static readonly StationLabCapability[] Capabilities = {
            StationLabCapability.StationBasic, StationLabCapability.BiGEn, StationLabCapability.MaPhyTec };

        internal static int CapabilityMask(ModuleKoffeeLab lab, ConfigNode? snapshot = null)
        {
            bool available = snapshot == null ? lab.ExecutionAvailable : lab.SnapshotExecutionAvailable(snapshot);
            if (!available) return 0;
            int mask = 0;
            for (int i = 0; i < Capabilities.Length; i++)
                if (ModuleKoffeeLab.HasCapability(lab.stationCapabilities, Capabilities[i])) mask |= 1 << i;
            return mask;
        }

        internal static bool HardwareAvailable(int capability)
        {
            var parts = PartLoader.LoadedPartsList;
            if (parts == null) return false;
            bool manager = parts.Any(p => p?.partPrefab != null && ResearchAndDevelopment.PartTechAvailable(p) &&
                p.partPrefab.Modules.OfType<ModuleKoffeeExperimentManager>().Any());
            return manager && parts.Any(p => p?.partPrefab != null && ResearchAndDevelopment.PartTechAvailable(p) &&
                p.partPrefab.Modules.OfType<ModuleKoffeeLab>().Any(lab => lab.StationSlotCapacity > 0 &&
                    ModuleKoffeeLab.UpgradeAvailable(lab.requiredExperimentUpgrade) &&
                    Capabilities.Select((c, i) => new { Capability = c, Mask = 1 << i }).Any(c =>
                        (capability == 0 || (c.Mask & capability) != 0) &&
                        ModuleKoffeeLab.HasCapability(lab.stationCapabilities, c.Capability))));
        }

        internal static StationContractCandidate? Observe(Vessel? vessel)
        {
            if (vessel == null || HighLogic.CurrentGame == null) return null;
            if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
            {
                var matches = HighLogic.CurrentGame.flightState?.protoVessels?
                    .Where(p => p.vesselID == vessel.id).ToArray();
                return matches?.Length == 1 ? Saved(matches[0]) : null;
            }
            if (HighLogic.LoadedScene != GameScenes.FLIGHT && HighLogic.LoadedScene != GameScenes.TRACKSTATION) return null;
            return vessel.loaded ? Loaded(vessel) : Saved(vessel.protoVessel);
        }

        private static StationContractCandidate? Loaded(Vessel vessel)
        {
            if (vessel.situation != Vessel.Situations.ORBITING || vessel.mainBody == null) return null;
            var parts = vessel.parts.Where(p => p != null && p.State != PartStates.DEAD).ToArray();
            var labs = parts.SelectMany(p => p.Modules.OfType<ModuleKoffeeLab>())
                .Where(l => l.StationSlotCapacity > 0 && CapabilityMask(l) != 0).ToArray();
            var managers = parts.SelectMany(p => p.Modules.OfType<ModuleKoffeeExperimentManager>()).ToArray();
            var crew = vessel.GetVesselCrew();
            return new StationContractCandidate {
                Orbiting = true,
                Parts = parts.Select(p => p.persistentId).ToArray(),
                Laboratories = labs.Select(l => l.part.persistentId).ToArray(),
                Capabilities = labs.Aggregate(0, (mask, lab) => mask | CapabilityMask(lab)),
                Manager = managers.Length > 0,
                Engineer = managers.Any(m => crew.Any(c => AssignedEngineer(c, m.assignedEngineer)))
            };
        }

        private static bool AssignedEngineer(ProtoCrewMember crew, string name) =>
            !string.IsNullOrEmpty(name) && crew != null && crew.name == name &&
            crew.type == ProtoCrewMember.KerbalType.Crew &&
            crew.rosterStatus == ProtoCrewMember.RosterStatus.Assigned && ExperimentManagerService.ValidEngineer(crew);

        private static StationContractCandidate? Saved(ProtoVessel? vessel)
        {
            if (vessel == null || vessel.situation != Vessel.Situations.ORBITING || vessel.orbitSnapShot == null ||
                vessel.protoPartSnapshots == null) return null;
            var body = FlightGlobals.Bodies.FirstOrDefault(b => b.flightGlobalsIndex == vessel.orbitSnapShot.ReferenceBodyIndex);
            if (body == null) return null;
            var parts = vessel.protoPartSnapshots.Where(p => p != null).ToArray();
            var laboratories = new List<uint>();
            var engineers = new List<string>();
            int capabilities = 0;
            bool manager = false;
            foreach (var part in parts)
            {
                var prefab = part.partInfo?.partPrefab ?? PartLoader.getPartInfoByName(part.partName)?.partPrefab;
                if (prefab == null) continue;
                var definitions = prefab.Modules.OfType<ModuleKoffeeLab>().ToArray();
                var lab = definitions.Length == 1 ? definitions[0] : null;
                var labNodes = part.modules.Where(m => m.moduleName == nameof(ModuleKoffeeLab)).ToArray();
                if (lab != null && prefab.CrewCapacity > 0 && labNodes.Length == 1)
                {
                    int mask = CapabilityMask(lab, labNodes[0].moduleValues);
                    if (mask != 0) { laboratories.Add(part.persistentId); capabilities |= mask; }
                }
                if (!prefab.Modules.OfType<ModuleKoffeeExperimentManager>().Any()) continue;
                foreach (var module in part.modules.Where(m => m.moduleName == nameof(ModuleKoffeeExperimentManager)))
                {
                    manager = true;
                    string[] values = module.moduleValues.GetValues("assignedEngineer");
                    if (values.Length == 1) engineers.Add(values[0]);
                }
            }
            // Never call ProtoVessel.GetVesselCrew: it can repair/mutate the roster while querying.
            var crew = parts.SelectMany(p => p.protoModuleCrew).Where(c => c != null).ToArray();
            return new StationContractCandidate {
                Orbiting = true,
                Parts = parts.Select(p => p.persistentId).ToArray(), Laboratories = laboratories.ToArray(),
                Capabilities = capabilities, Manager = manager,
                Engineer = engineers.Any(name => crew.Any(c => AssignedEngineer(c, name)))
            };
        }
    }

    // Small read-only API for the optional Contract Configurator adapter.
    public static class StationContractChecks
    {
        public static int Capability(string name) => name == "Any" ? 0 : name == "StationBasic" ? 1 :
            name == "BiGEn" ? 2 : name == "MaPhyTec" ? 4 : -1;
        public static bool HardwareAvailable(string name) => Capability(name) >= 0 &&
            StationContractFleet.HardwareAvailable(Capability(name));
        public static bool HasLaboratory(Vessel vessel, string name)
        {
            var c = StationContractFleet.Observe(vessel);
            int capability = Capability(name);
            return c != null && StationContractRules.HasLaboratory(c, capability);
        }
        public static bool HasAssignedEngineer(Vessel vessel) =>
            StationContractFleet.Observe(vessel)?.Engineer == true;
        public static bool CanExpand(Vessel vessel, string name)
        {
            var c = StationContractFleet.Observe(vessel);
            return c != null && StationContractRules.MissingAdvanced(c, Capability(name));
        }
        public static uint LaboratoryAnchor(Vessel vessel) =>
            StationContractFleet.Observe(vessel)?.Laboratories.OrderBy(id => id).FirstOrDefault() ?? 0;
        public static bool ContainsAnchor(Vessel vessel, uint anchor) => anchor != 0 &&
            StationContractFleet.Observe(vessel)?.Parts.Count(id => id == anchor) == 1;
    }
}

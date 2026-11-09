using System;
using System.Collections.Generic;
using System.Linq;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class MaintenanceEva
    {
        internal const string Kit = "evaRepairKit";
        internal const float Range = 6;
        private static bool departing;

        // Stock returns -1 when no matching part is stored, rather than zero.
        internal static int KitCount(ModuleInventoryPart inventory) => Math.Max(0, inventory.TotalAmountOfPartStored(Kit));

        // Passive: do not create missing profiles or initialize personal inventories here.
        internal static string BestEngineer(Vessel? vessel) => vessel?.loaded == true && !vessel.isEVA ?
            vessel.GetVesselCrew().Where(c => ExperimentManagerService.ValidEngineer(c) && !c.inactive &&
                KerbalSpecialtyScenario.TryGetEngineerProfile(c.name, out _))
                .OrderByDescending(c => { KerbalSpecialtyScenario.TryGetEngineerProfile(c.name, out var p); return p.Score(EngineeringAffinity.Maintenance); })
                .ThenBy(c => c.name, StringComparer.Ordinal).Select(c => c.name).FirstOrDefault() ?? "" : "";

        internal static ProtoCrewMember? Nearby(Part part, string? requiredName = null)
        {
            var eva = FlightGlobals.ActiveVessel;
            if (!HighLogic.LoadedSceneIsFlight || part?.State == PartStates.DEAD || part?.vessel?.loaded != true || eva?.loaded != true ||
                !eva.isEVA || eva.packed || part.vessel.packed || eva.evaController == null ||
                !(Vector3.Distance(eva.evaController.part.transform.position, part.transform.position) <= Range)) return null;
            return eva.GetVesselCrew().FirstOrDefault(c => ExperimentManagerService.ValidEngineer(c) && !c.inactive &&
                (requiredName == null || c.name == requiredName));
        }

        internal static void ShowError(string key)
        {
            PopupDialog.SpawnPopupDialog(new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new MultiOptionDialog("KoffeeMaintenanceEva", Localizer.Format(key),
                    Localizer.Format("#Koffee_EvaMaintenance_Title"), HighLogic.UISkin,
                    new DialogGUIButton(Localizer.Format("#Koffee_EvaMaintenance_Close"), () => { })), false, HighLogic.UISkin, true);
        }

        internal static void Depart(Vessel vessel, string name)
        {
            if (!TryDepart(vessel, name, out string error)) ShowError(error);
        }

        internal static bool TryDepart(Vessel vessel, string name, out string error)
        {
            error = "#Koffee_EvaMaintenance_CannotExit";
            if (departing || !HighLogic.LoadedSceneIsFlight || vessel == null || vessel != FlightGlobals.ActiveVessel ||
                !vessel.loaded || vessel.packed || vessel.isEVA || name != BestEngineer(vessel)) return false;
            var parts = vessel.parts.Where(p => p != null && p.State != PartStates.DEAD && p.protoModuleCrew.Any(c => c.name == name)).ToArray();
            if (parts.Length != 1 || FlightEVA.fetch == null) return false;
            Part origin = parts[0];
            ProtoCrewMember crew = origin.protoModuleCrew.Single(c => c.name == name);
            if (!GameVariables.Instance.EVAIsPossible(GameVariables.Instance.UnlockedEVA(
                ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex)), vessel) || origin.airlock == null) return false;

            departing = true;
            var backups = new Dictionary<ModuleInventoryPart, StoredPart[]>();
            ModuleInventoryPart? personal = null;
            bool launched = false;
            try
            {
                personal = crew.KerbalInventoryModule;
                if (personal == null) { error = "#Koffee_EvaMaintenance_NeedsKits"; return false; }
                var kit = PartLoader.getPartInfoByName(Kit);
                var cargo = kit?.partPrefab?.FindModuleImplementing<ModuleCargoPart>();
                if (kit == null || cargo == null || cargo.packedVolume <= 0) return false;
                var sources = vessel.parts.Where(p => p.State != PartStates.DEAD).OrderBy(p => p.flightID)
                    .SelectMany(p => p.Modules.OfType<ModuleInventoryPart>()).Where(i => !ReferenceEquals(i, personal)).ToArray();
                backups.Add(personal, Copy(personal));
                while (KitCount(personal) < 4)
                {
                    int slot = Enumerable.Range(0, personal.InventorySlots).Where(i =>
                        personal.storedParts.TryGetValue(i, out var stored) && stored.partName == Kit && stored.quantity < stored.stackCapacity).DefaultIfEmpty(-1).First();
                    if (slot < 0) slot = personal.FirstEmptySlot();
                    if (slot < 0 || (personal.HasPackedVolumeLimit && OccupiedVolume(personal) + cargo.packedVolume > personal.packedVolumeLimit + 1e-5) ||
                        (personal.HasMassLimit && OccupiedMass(personal) + personal.GetPartMass(kit.partPrefab) > personal.massLimit + 1e-6)) break;
                    var source = sources.FirstOrDefault(i => KitCount(i) > 0);
                    if (source == null) break;
                    if (!backups.ContainsKey(source)) backups.Add(source, Copy(source));
                    var item = source.storedParts.Values.First(s => s.partName == Kit && s.quantity > 0);
                    var snapshot = item.snapshot; string variant = item.variantName;
                    int before = KitCount(personal), sourceBefore = KitCount(source);
                    if (!source.RemoveNPartsFromInventory(Kit, 1, false) || KitCount(source) != sourceBefore - 1)
                        throw new InvalidOperationException("Repair kit source did not debit exactly one kit.");
                    bool added = personal.IsSlotEmpty(slot) ? personal.StoreCargoPartAtSlot(snapshot, slot) :
                        personal.UpdateStackAmountAtSlot(slot, personal.GetStackAmountAtSlot(slot) + 1, variant);
                    if (!added || KitCount(personal) != before + 1)
                        throw new InvalidOperationException("Repair kit destination did not receive exactly one kit.");
                }
                if (KitCount(personal) < 1) { error = "#Koffee_EvaMaintenance_NeedsKits"; return false; }
                crew.SaveInventory(personal);
                var eva = FlightEVA.fetch.spawnEVA(crew, origin, origin.airlock, true);
                // A native override may complete an EVA and return null; never restore an aboard copy after departure.
                launched = eva != null || !origin.protoModuleCrew.Contains(crew);
                return launched;
            }
            catch (Exception exception)
            {
                launched = !origin.protoModuleCrew.Contains(crew);
                Debug.LogError("[Koffee] Maintenance EVA preparation failed: " + exception);
                return launched;
            }
            finally
            {
                try
                {
                    if (!launched)
                    {
                        foreach (var backup in backups)
                        {
                            backup.Key.storedParts.Clear();
                            foreach (var item in backup.Value) backup.Key.storedParts.Add(item.slotIndex, Clone(item));
                            GameEvents.onModuleInventoryChanged.Fire(backup.Key);
                        }
                        if (personal != null) crew.SaveInventory(personal);
                    }
                    }
                finally { departing = false; }
            }
        }

        private static double OccupiedVolume(ModuleInventoryPart inventory) => inventory.storedParts.Values.Sum(s =>
            (double)(s.snapshot?.partPrefab?.FindModuleImplementing<ModuleCargoPart>()?.packedVolume ?? 0) * s.quantity);
        private static double OccupiedMass(ModuleInventoryPart inventory) => inventory.storedParts.Values.Sum(s =>
            s.snapshot?.partPrefab == null ? 0 : (double)inventory.GetPartMass(s.snapshot.partPrefab) * s.quantity);
        private static StoredPart Clone(StoredPart item)
        {
            var node = new ConfigNode(); item.Save(node);
            var copy = new StoredPart(item.partName, item.slotIndex); copy.Load(node); return copy;
        }
        private static StoredPart[] Copy(ModuleInventoryPart inventory) => inventory.storedParts.Values.Select(p => Clone(p)).ToArray();
    }
}

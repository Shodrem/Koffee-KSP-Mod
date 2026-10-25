using System;
using System.Collections.Generic;
using System.Linq;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    // Shared by the editor and launch validation; never changes a saved selection.
    internal static class ExperimentSelectionRules
    {
        internal const string TextureRoot = "ShoAerospace/Koffee/Parts/Experiment_Container_62/";

        internal static AvailablePart? Source(string id, string family) =>
            PartLoader.LoadedPartsList.FirstOrDefault(p => p?.partPrefab != null &&
                p.partPrefab.Modules.OfType<ModuleKoffeeExperiment>().Any(m =>
                    string.IsNullOrEmpty(m.editorExperimentFamily) && m.experimentID == id) && Family(p) == family);

        internal static string Family(AvailablePart source)
        {
            string model = source.partConfig?.GetNode("MODEL")?.GetValue("model") ?? "";
            if (model == TextureRoot + "Experiment_Container_625") return "Container";
            if (model == "ShoAerospace/Koffee/Parts/Experiments_Device_ED-01/Experiment_Device_ED01") return "Device";
            return "";
        }

        internal static bool Unlocked(AvailablePart source)
        {
            Game? game = HighLogic.CurrentGame;
            if (game == null) return false;
            if (game.Mode == Game.Modes.SANDBOX) return true;
            if (game.Mode != Game.Modes.SCIENCE_SANDBOX && game.Mode != Game.Modes.CAREER) return false;
            if (ResearchAndDevelopment.Instance == null ||
                ResearchAndDevelopment.GetTechnologyState(source.TechRequired) != RDTech.State.Available) return false;
            return true; // Configuration research never requires a dedicated-part purchase.
        }

        internal static string TechnologyTitle(AvailablePart source) => TechnologyTitle(source.TechRequired);

        private static string TechnologyTitle(string id)
        {
            string title = ResearchAndDevelopment.GetTechnologyTitle(id);
            return string.IsNullOrEmpty(title) ? id : Localizer.Format(title);
        }

        internal static string[] SurfaceTechnologyTitles(AvailablePart source)
        {
            var config = source.partConfig?.GetNodes("MODULE").SingleOrDefault(n => n.GetValue("name") == nameof(ModuleKoffeeExperiment));
            return (config?.GetNode("UPGRADES")?.GetNodes("UPGRADE") ?? Array.Empty<ConfigNode>())
                .Where(n => bool.TryParse(n.GetValue("surfaceEnabled"), out bool enabled) && enabled)
                .Select(n => n.GetValue("name__")).Where(id => !string.IsNullOrEmpty(id))
                .Select(id => PartUpgradeManager.Handler?.GetUpgrade(id)?.techRequired)
                .Where(id => !string.IsNullOrEmpty(id)).Select(id => TechnologyTitle(id!))
                .Distinct(StringComparer.Ordinal).ToArray();
        }

        internal static bool SurfaceCapability(AvailablePart source)
        {
            bool enabled = source.partPrefab.FindModuleImplementing<ModuleKoffeeExperiment>().surfaceEnabled;
            var config = source.partConfig?.GetNodes("MODULE").SingleOrDefault(n => n.GetValue("name") == nameof(ModuleKoffeeExperiment));
            foreach (ConfigNode upgrade in config?.GetNode("UPGRADES")?.GetNodes("UPGRADE") ?? Array.Empty<ConfigNode>())
            {
                string upgradeId = upgrade.GetValue("name__");
                if (!string.IsNullOrEmpty(upgradeId) && PartUpgradeManager.Handler != null &&
                    PartUpgradeManager.Handler.IsUnlocked(upgradeId) && PartUpgradeManager.Handler.IsEnabled(upgradeId))
                    enabled = StationExperimentEnvironment.SurfaceCapability(upgrade, enabled);
            }
            return enabled;
        }

        internal static string[] LaunchErrors(ConfigNode craft, IEnumerable<ConfigNode> crewInventories)
        {
            var errors = new List<string>();
            foreach (ConfigNode part in craft.GetNodes("PART")) CheckPart(part, errors);
            foreach (ConfigNode inventory in crewInventories) CheckInventory(inventory, errors);
            return errors.Distinct(StringComparer.Ordinal).ToArray();
        }

        private static void CheckPart(ConfigNode node, List<string> errors)
        {
            string name = node.GetValue("name") ?? "";
            if (string.IsNullOrEmpty(name) && node.GetValue("part") is string craftPart)
                name = KSPUtil.GetPartName(craftPart);
            var host = PartLoader.LoadedPartsList.FirstOrDefault(p => p.name == name);
            var prefab = host?.partPrefab?.FindModuleImplementing<ModuleKoffeeExperiment>();
            if (!string.IsNullOrEmpty(prefab?.editorExperimentFamily))
            {
                var modules = node.GetNodes("MODULE").Where(n => n.GetValue("name") == nameof(ModuleKoffeeExperiment)).ToArray();
                string[] ids = modules.Length == 1 ? modules[0].GetValues(nameof(ModuleKoffeeExperiment.experimentID)) : Array.Empty<string>();
                string id = ids.Length == 1 ? ids[0] : "";
                // A genuinely empty host is allowed; duplicate modules/IDs are ambiguous.
                if (modules.Length > 1 || ids.Length > 1)
                    errors.Add(Localizer.Format("#Koffee_Selector_LaunchInvalid", host!.title));
                else if (!string.IsNullOrEmpty(id))
                {
                    var source = Source(id, prefab!.editorExperimentFamily);
                    if (source == null || !StationExperimentCatalogLoader.Current.TryGet(id, out _))
                        errors.Add(Localizer.Format("#Koffee_Selector_LaunchInvalid", host!.title));
                    else if (!Unlocked(source))
                        errors.Add(Localizer.Format("#Koffee_Selector_LaunchLocked", source.title));
                    else if (StationExperimentEnvironment.SurfaceCapability(modules[0], prefab.surfaceEnabled) && !SurfaceCapability(source))
                        errors.Add(Localizer.Format("#Koffee_Selector_LaunchSurfaceLocked", source.title));
                }
            }
            foreach (ConfigNode module in node.GetNodes("MODULE"))
                if (module.GetValue("name") == "ModuleInventoryPart") CheckInventory(module, errors);
        }

        private static void CheckInventory(ConfigNode inventory, List<string> errors)
        {
            foreach (ConfigNode storedParts in inventory.GetNodes("STOREDPARTS"))
                foreach (ConfigNode stored in storedParts.GetNodes("STOREDPART"))
                    foreach (ConfigNode part in stored.GetNodes("PART")) CheckPart(part, errors);
        }
    }
}

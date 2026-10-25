using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class StationExperimentCatalogLoader
    {
        private static StationExperimentCatalog structural = StationExperimentCatalog.Empty;
        private static bool resolutionPending;
        private static readonly Dictionary<string, AvailablePart> equipmentParts = new Dictionary<string, AvailablePart>(StringComparer.Ordinal);
        internal static AvailablePart? EquipmentPart(string name) => equipmentParts.TryGetValue(name, out var part) ? part : null;
        internal static StationExperimentCatalog Current { get; private set; } =
            StationExperimentCatalog.Empty;

        internal static void Load()
        {
            Current = StationExperimentCatalog.Empty;
            structural = StationExperimentCatalog.Empty;
            resolutionPending = false;
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes(
                KoffeeIdentifiers.StationExperimentNode);
            var configs = new List<StationExperimentConfig>(nodes.Length);
            foreach (ConfigNode node in nodes) configs.Add(Convert(node));

            structural = StationExperimentCatalog.Parse(configs,
                out IReadOnlyList<string> parseDiagnostics);
            LogDiagnostics(parseDiagnostics);

            ResolveReferences(true);
        }

        internal static void ResolvePendingReferences()
        {
            if (resolutionPending) ResolveReferences(false);
        }

        private static void ResolveReferences(bool reportDeferred)
        {
            bool bodiesReady = FlightGlobals.Bodies != null && FlightGlobals.Bodies.Count > 0;
            var loadedParts = PartLoader.LoadedPartsList;
            bool partsReady = false;
            equipmentParts.Clear();
            Dictionary<string, int>? links = null;
            if (loadedParts != null && loadedParts.Count > 0)
            {
                partsReady = true;
                foreach (var group in loadedParts.Where(p => p != null && p.partPrefab != null &&
                    !string.IsNullOrWhiteSpace(p.name)).GroupBy(p => p.name, StringComparer.Ordinal))
                    if (group.Count() == 1) equipmentParts.Add(group.Key, group.Single());
                links = PartLinks(out IReadOnlyList<string> linkDiagnostics);
                LogDiagnostics(linkDiagnostics);
            }

            Current = structural.Resolve(
                id =>
                {
                    ScienceExperiment science = ResearchAndDevelopment.GetExperiment(id);
                    if (science != null && science.requireAtmosphere && science.requireNoAtmosphere)
                    {
                        Debug.LogWarning("[Koffee] Station experiment '" + id +
                            "' requires both atmosphere and no atmosphere; entry ignored.");
                        return false;
                    }
                    return science != null;
                },
                bodiesReady ? new Func<string, bool>(name => FlightGlobals.GetBodyByName(name) != null) : null,
                links == null ? null : new Func<string, int>(id =>
                    links.TryGetValue(id, out int count) ? count : 0),
                out IReadOnlyList<string> resolutionDiagnostics);
            LogDiagnostics(resolutionDiagnostics);
            Current = Current.ResolveEquipment(partsReady ? new Func<string, bool>(equipmentParts.ContainsKey) : null,
                out var equipmentDiagnostics);
            LogDiagnostics(equipmentDiagnostics);
            resolutionPending = !bodiesReady || !partsReady;
            if (reportDeferred && !bodiesReady)
                Debug.LogWarning("[Koffee] Celestial bodies are not available while loading station experiments; body resolution deferred.");
            if (reportDeferred && !partsReady)
                Debug.LogWarning("[Koffee] Loaded PART definitions are not available while loading station experiments; PART link resolution deferred.");
            Debug.Log("[Koffee] Loaded " + Current.Count + " station experiment definitions.");
        }

        private static StationExperimentConfig Convert(ConfigNode node) =>
            new StationExperimentConfig(Fields(node, "name", "schemaVersion", "description",
                    "executionModel", "scienceCategory", "researchPointsRequired", "durationSeconds", "xmitDataScalar", "recoveryScienceBonusPercent", "bodyMode", "resultReading"),
                node.GetNodes("BODY").Select(body => Fields(body, "name")).ToArray(),
                node.GetNodes("PERSONNEL").Select(personnel => Fields(personnel,
                    "trait", "minimumLevel", "workerCount")).ToArray(),
                node.GetNodes("LAB_REQUIREMENT").Select(requirement => Fields(requirement,
                    "capability")).ToArray(),
                node.GetNodes("PART_REQUIREMENT").Select(requirement => Fields(requirement, "name")).ToArray());

        private static IReadOnlyDictionary<string, string?> Fields(ConfigNode node, params string[] names)
        {
            var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (string name in names)
            {
                string[] values = node.GetValues(name);
                fields[name] = values.Length == 1 ? values[0] : values.Length == 0
                    ? null : StationExperimentConfig.DuplicateValue;
            }
            return fields;
        }

        private static Dictionary<string, int> PartLinks(out IReadOnlyList<string> diagnostics)
        {
            var links = new Dictionary<string, int>(StringComparer.Ordinal);
            var messages = new List<string>();
            foreach (AvailablePart available in PartLoader.LoadedPartsList)
            {
                if (available?.partPrefab == null) continue;
                foreach (ModuleKoffeeExperiment module in available.partPrefab.Modules
                    .OfType<ModuleKoffeeExperiment>())
                {
                    if (!string.IsNullOrEmpty(module.editorExperimentFamily)) continue;
                    string id = module.experimentID ?? "";
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        messages.Add("PART '" + (available.name ?? "<unknown>") +
                            "' has ModuleKoffeeExperiment without experimentID.");
                        continue;
                    }
                    links[id] = links.TryGetValue(id, out int count) ? count + 1 : 1;
                }
            }
            foreach (string id in links.Keys)
                if (!GameDatabase.Instance.GetConfigNodes(KoffeeIdentifiers.StationExperimentNode)
                    .Any(node => string.Equals(node.GetValue("name"), id, StringComparison.Ordinal)))
                    messages.Add("PART link references missing KOFFEE_STATION_EXPERIMENT '" + id + "'.");
            diagnostics = messages;
            return links;
        }

        private static void LogDiagnostics(IEnumerable<string> diagnostics)
        {
            foreach (string diagnostic in diagnostics) Debug.LogWarning("[Koffee] " + diagnostic);
        }
    }
}

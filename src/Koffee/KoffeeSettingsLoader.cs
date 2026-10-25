using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class KoffeeSettingsLoader
    {
        internal static KoffeeSettings Current { get; private set; } = KoffeeSettings.Defaults;

        internal static void Load()
        {
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes(KoffeeIdentifiers.SettingsNode);
            var values = new List<IReadOnlyDictionary<string, string?>>(nodes.Length);
            foreach (ConfigNode node in nodes)
            {
                values.Add(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["activationDuration"] = SingleValue(node, "activationDuration"),
                    ["activationCost"] = SingleValue(node, "activationCost")
                });
            }

            Current = KoffeeSettings.Parse(values, out IReadOnlyList<string> warnings);
            foreach (string warning in warnings) Debug.LogWarning("[Koffee] " + warning);
            Debug.Log("[Koffee] Loaded global settings: activationDuration=" +
                Current.ActivationDuration.ToString("R", CultureInfo.InvariantCulture) +
                ", activationCost=" + Current.ActivationCost.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static string? SingleValue(ConfigNode node, string field)
        {
            string[] found = node.GetValues(field);
            if (found.Length == 0) return null;
            if (found.Length == 1) return found[0];
            return "<duplicate values: " + string.Join(" | ", found) + ">";
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class EngineeringSettingsLoader
    {
        internal static EngineeringKoffeeSettings Current { get; private set; } =
            EngineeringKoffeeSettings.Disabled;

        internal static void Load()
        {
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes(KoffeeIdentifiers.EngineeringSettingsNode);
            var values = new List<IReadOnlyDictionary<string, string?>>(nodes.Length);
            foreach (ConfigNode node in nodes)
            {
                var entry = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["engineerSelection"] = node.GetValue("engineerSelection")
                };
                for (int level = 0; level <= 5; level++)
                {
                    string key = "bonusLevel" + level;
                    entry[key] = node.GetValue(key);
                }
                values.Add(entry);
            }

            Current = EngineeringKoffeeSettings.Parse(values, out IReadOnlyList<string> errors);
            foreach (string error in errors) Debug.LogError("[Koffee] " + error);
            Debug.Log("[Koffee] Engineering Koffee " + (Current.Enabled ? "enabled" : "disabled") +
                "; selection=" + Current.Selection + ".");
        }
    }
}

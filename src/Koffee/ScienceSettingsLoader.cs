using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class ScienceSettingsLoader
    {
        internal static ScienceKoffeeSettings Current { get; private set; } =
            ScienceKoffeeSettings.Disabled;

        internal static void Load()
        {
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes(KoffeeIdentifiers.ScienceSettingsNode);
            var values = new List<IReadOnlyDictionary<string, string?>>(nodes.Length);
            foreach (ConfigNode node in nodes)
            {
                values.Add(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["scientistKoffeeBonus"] = node.GetValue("scientistKoffeeBonus")
                });
            }

            Current = ScienceKoffeeSettings.Parse(values, out IReadOnlyList<string> errors);
            foreach (string error in errors) Debug.LogError("[Koffee] " + error);
            Debug.Log("[Koffee] Scientist Koffee bonus " +
                (Current.Enabled ? "enabled" : "disabled") + ".");
        }
    }
}

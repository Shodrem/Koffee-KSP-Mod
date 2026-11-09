using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class EngineeringSettingsLoader
    {
        internal static EngineeringKoffeeSettings Current { get; private set; } = EngineeringKoffeeSettings.Disabled;
        internal static void Load()
        {
            var values = new List<IReadOnlyDictionary<string, string?>>();
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(KoffeeIdentifiers.EngineeringSettingsNode))
            {
                var entry = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (string key in EngineeringKoffeeSettings.Keys) entry[key] = node.GetValue(key);
                values.Add(entry);
            }
            Current = EngineeringKoffeeSettings.Parse(values, out var errors);
            EngineeringPowerDiagnostics.Enabled = values.Count == 1 &&
                string.Equals(GameDatabase.Instance.GetConfigNodes(KoffeeIdentifiers.EngineeringSettingsNode)[0].GetValue("engineeringPowerDiagnostics"), "true", StringComparison.OrdinalIgnoreCase);
            foreach (string error in errors) Debug.LogError("[Koffee] " + error);
        }
    }
}

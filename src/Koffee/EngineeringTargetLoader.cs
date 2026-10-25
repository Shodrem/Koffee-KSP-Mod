using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class EngineeringTargetLoader
    {
        internal static EngineeringTargetRegistry Current { get; private set; } =
            EngineeringTargetRegistry.Empty;

        internal static void Load()
        {
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes(
                KoffeeIdentifiers.EngineeringTargetNode);
            var values = new List<IReadOnlyDictionary<string, string?>>(nodes.Length);
            foreach (ConfigNode node in nodes)
            {
                values.Add(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = node.GetValue("name"),
                    ["part"] = node.GetValue("part"),
                    ["module"] = node.GetValue("module"),
                    ["experienceEffect"] = node.GetValue("experienceEffect"),
                    ["enabled"] = node.GetValue("enabled")
                });
            }

            Current = EngineeringTargetRegistry.Parse(values, out IReadOnlyList<string> warnings);
            foreach (string warning in warnings) Debug.LogWarning("[Koffee] " + warning);
            Debug.Log("[Koffee] Loaded " + Current.Count + " engineering targets.");
        }
    }
}

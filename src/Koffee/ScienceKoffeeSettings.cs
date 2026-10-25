using System;
using System.Collections.Generic;
using System.Globalization;

namespace ShoAerospace.Koffee
{
    internal sealed class ScienceKoffeeSettings
    {
        internal const double MaximumBonus = 10;

        internal bool Enabled { get; }
        internal double ScientistBonus { get; }

        internal static ScienceKoffeeSettings Disabled { get; } =
            new ScienceKoffeeSettings(false, 0);

        private ScienceKoffeeSettings(bool enabled, double scientistBonus)
        {
            Enabled = enabled;
            ScientistBonus = scientistBonus;
        }

        internal static ScienceKoffeeSettings Parse(
            IReadOnlyList<IReadOnlyDictionary<string, string?>> nodes,
            out IReadOnlyList<string> errors)
        {
            var messages = new List<string>();
            if (nodes == null || nodes.Count != 1)
            {
                messages.Add(nodes == null || nodes.Count == 0
                    ? KoffeeIdentifiers.ScienceSettingsNode +
                        " is missing; the scientist Koffee bonus is disabled."
                    : "Multiple " + KoffeeIdentifiers.ScienceSettingsNode +
                        " nodes found; the scientist Koffee bonus is disabled.");
                errors = messages;
                return Disabled;
            }

            IReadOnlyDictionary<string, string?> node = nodes[0];
            if (!TryGet(node, "scientistKoffeeBonus", out string? text) ||
                !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double value) || double.IsNaN(value) || double.IsInfinity(value) ||
                value < 0 || value > MaximumBonus)
            {
                messages.Add("Invalid or missing scientistKoffeeBonus; the scientist Koffee bonus is disabled.");
                errors = messages;
                return Disabled;
            }

            errors = messages;
            return new ScienceKoffeeSettings(true, value);
        }

        private static bool TryGet(IReadOnlyDictionary<string, string?> node,
            string key, out string? value)
        {
            if (node.TryGetValue(key, out value)) return true;
            foreach (KeyValuePair<string, string?> entry in node)
                if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = entry.Value;
                    return true;
                }
            value = null;
            return false;
        }
    }
}

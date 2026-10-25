using System;
using System.Collections.Generic;
using System.Globalization;

namespace ShoAerospace.Koffee
{
    internal sealed class KoffeeSettings
    {
        internal const double DefaultActivationDuration = 10800;
        internal const int DefaultActivationCost = 1;

        internal double ActivationDuration { get; }
        internal int ActivationCost { get; }

        internal static KoffeeSettings Defaults { get; } =
            new KoffeeSettings(DefaultActivationDuration, DefaultActivationCost);

        private KoffeeSettings(double activationDuration, int activationCost)
        {
            ActivationDuration = activationDuration;
            ActivationCost = activationCost;
        }

        internal static KoffeeSettings Parse(
            IReadOnlyList<IReadOnlyDictionary<string, string?>> nodes,
            out IReadOnlyList<string> warnings)
        {
            var messages = new List<string>();
            if (nodes == null || nodes.Count == 0)
            {
                messages.Add(KoffeeIdentifiers.SettingsNode +
                    " is missing; defaults will be used: activationDuration=" +
                    DefaultActivationDuration.ToString("R", CultureInfo.InvariantCulture) +
                    ", activationCost=" + DefaultActivationCost.ToString(CultureInfo.InvariantCulture) + ".");
                warnings = messages;
                return Defaults;
            }
            if (nodes.Count != 1)
            {
                messages.Add("Multiple " + KoffeeIdentifiers.SettingsNode +
                    " nodes found; defaults will be used: activationDuration=" +
                    DefaultActivationDuration.ToString("R", CultureInfo.InvariantCulture) +
                    ", activationCost=" + DefaultActivationCost.ToString(CultureInfo.InvariantCulture) + ".");
                warnings = messages;
                return Defaults;
            }

            IReadOnlyDictionary<string, string?> node = nodes[0];
            double duration = DefaultActivationDuration;
            int cost = DefaultActivationCost;

            if (!TryGet(node, "activationDuration", out string? durationText) ||
                !double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double parsedDuration) || double.IsNaN(parsedDuration) ||
                double.IsInfinity(parsedDuration) || parsedDuration <= 0)
            {
                messages.Add(Invalid("activationDuration", durationText,
                    DefaultActivationDuration.ToString("R", CultureInfo.InvariantCulture)));
            }
            else duration = parsedDuration;

            if (!TryGet(node, "activationCost", out string? costText) ||
                !int.TryParse(costText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int parsedCost) || parsedCost <= 0)
            {
                messages.Add(Invalid("activationCost", costText,
                    DefaultActivationCost.ToString(CultureInfo.InvariantCulture)));
            }
            else cost = parsedCost;

            warnings = messages;
            return duration == DefaultActivationDuration && cost == DefaultActivationCost
                ? Defaults : new KoffeeSettings(duration, cost);
        }

        private static string Invalid(string field, string? value, string fallback) =>
            KoffeeIdentifiers.SettingsNode + "." + field + " has invalid value '" +
            (value ?? "<missing>") + "'; fallback " + fallback + " will be used.";

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

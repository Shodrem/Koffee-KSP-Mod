using System;
using System.Collections.Generic;
using System.Globalization;

namespace ShoAerospace.Koffee
{
    internal enum EngineerSelection
    {
        BestEngineer,
        AnyKaffeinated
    }

    internal sealed class EngineeringKoffeeSettings
    {
        internal const double MaximumBonus = 10;
        private readonly double[] bonuses;

        internal bool Enabled { get; }
        internal EngineerSelection Selection { get; }

        internal static EngineeringKoffeeSettings Disabled { get; } =
            new EngineeringKoffeeSettings(false, EngineerSelection.BestEngineer, new double[6]);

        private EngineeringKoffeeSettings(bool enabled, EngineerSelection selection, double[] bonuses)
        {
            Enabled = enabled;
            Selection = selection;
            this.bonuses = (double[])bonuses.Clone();
        }

        internal double BonusForLevel(int level) => level >= 0 && level < bonuses.Length
            ? bonuses[level] : 0;

        internal static EngineeringKoffeeSettings Parse(
            IReadOnlyList<IReadOnlyDictionary<string, string?>> nodes, out IReadOnlyList<string> errors)
        {
            var messages = new List<string>();
            var values = new double[6];
            if (nodes == null || nodes.Count != 1)
            {
                messages.Add(nodes == null || nodes.Count == 0
                    ? KoffeeIdentifiers.EngineeringSettingsNode +
                        " is missing; engineering Koffee is disabled."
                    : "Multiple " + KoffeeIdentifiers.EngineeringSettingsNode +
                        " nodes found; engineering Koffee is disabled.");
                errors = messages;
                return new EngineeringKoffeeSettings(false, EngineerSelection.BestEngineer, values);
            }

            IReadOnlyDictionary<string, string?> node = nodes[0];
            EngineerSelection selection = EngineerSelection.BestEngineer;
            if (!TryGet(node, "engineerSelection", out string? policy) ||
                !Enum.TryParse(policy?.Trim(), true, out selection) ||
                !Enum.IsDefined(typeof(EngineerSelection), selection))
            {
                selection = EngineerSelection.BestEngineer;
                messages.Add("Invalid or missing engineerSelection; BestEngineer will be used.");
            }

            for (int level = 0; level < values.Length; level++)
            {
                string key = "bonusLevel" + level.ToString(CultureInfo.InvariantCulture);
                if (!TryGet(node, key, out string? text) ||
                    !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                    double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > MaximumBonus)
                {
                    messages.Add("Invalid or missing " + key + "; 0 will be used.");
                    continue;
                }
                values[level] = value;
            }

            errors = messages;
            return new EngineeringKoffeeSettings(true, selection, values);
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

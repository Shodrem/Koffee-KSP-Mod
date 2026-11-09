using System;
using System.Collections.Generic;
using System.Globalization;

namespace ShoAerospace.Koffee
{
    internal sealed class EngineeringKoffeeSettings
    {
        internal bool Enabled { get; private set; }
        internal double PerPoint { get; private set; }
        internal double CaffeinatedExtra { get; private set; }
        internal double MaximumSaving { get; private set; }
        internal double MaintenancePerPoint { get; private set; } = .05;
        internal double MaintenanceCaffeinatedExtra { get; private set; } = .05;
        internal double MaintenanceHours { get; private set; }
        internal double MaintenanceSeconds { get; private set; }
        internal double DegradedCapacity { get; private set; }
        internal static readonly string[] Keys = { "affinityPerPoint", "kaffeinatedExtra", "maximumSaving", "maintenanceHours", "maintenanceSeconds", "degradedCapacity", "maintenanceAffinityPerPoint", "maintenanceKaffeinatedExtra" };
        internal static EngineeringKoffeeSettings Disabled { get; } = new EngineeringKoffeeSettings();
        private EngineeringKoffeeSettings()
        {
            MaintenanceHours = 1278; MaintenanceSeconds = 45; DegradedCapacity = .5;
        }
        internal double Contribution(int score, bool caffeinated, double komfort, bool maintenance = false)
        {
            if (!Enabled || score < 1 || score > 10 || !EngineeringMath.Finite(komfort)) return 0;
            return Math.Min(MaximumSaving, Math.Max(0, (score * (maintenance ? MaintenancePerPoint : PerPoint) + (caffeinated ? (maintenance ? MaintenanceCaffeinatedExtra : CaffeinatedExtra) : 0)) * Math.Max(0, Math.Min(1, komfort))));
        }
        internal static EngineeringKoffeeSettings Parse(IReadOnlyList<IReadOnlyDictionary<string, string?>> nodes, out IReadOnlyList<string> errors)
        {
            var messages = new List<string>();
            var result = new EngineeringKoffeeSettings();
            if (nodes == null || nodes.Count != 1)
            {
                messages.Add("Exactly one KOFFEE_ENGINEERING_SETTINGS is required; engineering contributions disabled.");
                errors = messages; return result;
            }
            var values = new double[Keys.Length];
            double[] defaults = { .03, .03, .95, 1278, 45, .5, .05, .05 };
            double[] maxima = { .1, .1, .95, 1000000, 86400, 1, .1, .1 };
            for (int i = 0; i < Keys.Length; i++)
            {
                if (i >= 6 && (!nodes[0].TryGetValue(Keys[i], out var optional) || string.IsNullOrEmpty(optional)))
                { values[i] = defaults[i]; continue; }
                if (!nodes[0].TryGetValue(Keys[i], out var text) || !double.TryParse(text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out values[i]) || !EngineeringMath.Finite(values[i]) || values[i] < 0 || values[i] > maxima[i] ||
                    (i >= 3 && i < 6 && values[i] <= 0))
                { values[i] = defaults[i]; messages.Add("Invalid " + Keys[i] + "; using " + defaults[i].ToString(CultureInfo.InvariantCulture)); }
            }
            result.Enabled = true; result.PerPoint = values[0]; result.CaffeinatedExtra = values[1]; result.MaximumSaving = values[2];
            result.MaintenanceHours = values[3]; result.MaintenanceSeconds = values[4]; result.DegradedCapacity = values[5];
            result.MaintenancePerPoint = values[6]; result.MaintenanceCaffeinatedExtra = values[7];
            errors = messages; return result;
        }
    }
}

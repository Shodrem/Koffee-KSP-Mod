using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    // Stock owns the current settings. Historical effective rates only describe
    // intervals before a reload; pendingPreviousRate keeps an interval open
    // between acceptance and reload. Neither defines the player's chosen options.
    public sealed class KoffeeGameParameters : GameParameters.CustomParameterNode
    {
        private double? pendingPreviousRate;
        private float rp = 1f;
        private string duration = "1×";
        private readonly List<Boundary> history = new List<Boundary>();
        private static readonly string[] durations = { "0.1×", "0.5×", "1×", "2×", "4×" };
        private static readonly double[] factors = { 0.1, 0.5, 1, 2, 4 };
        public override string Title => Localizer.Format("#Koffee_Settings_Title");
        public override string Section => "Koffee";
        public override string DisplaySection => "Koffee";
        public override int SectionOrder => 0;
        public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;
        public override bool HasPresets => false;

        [GameParameters.CustomFloatParameterUI("#Koffee_Settings_RP", toolTip = "#Koffee_Settings_RPHelp",
            minValue = 0.1f, maxValue = 10f, stepCount = 100, displayFormat = "F1", asPercentage = false,
            autoPersistance = true)]
        public float ResearchSpeedMultiplier
        {
            get => rp;
            set { if (!float.IsNaN(value) && !float.IsInfinity(value) && value >= 0.1f && value <= 10f) rp = value; }
        }

        [GameParameters.CustomParameterUI("#Koffee_Settings_Duration", toolTip = "#Koffee_Settings_LaunchDurationHelp", autoPersistance = true)]
        public string ExperimentDuration
        {
            get => duration;
            set { if (Array.IndexOf(durations, value) >= 0) duration = value; }
        }

        public override IList ValidValues(MemberInfo member) => member.Name == nameof(ExperimentDuration)
            ? Array.AsReadOnly(durations) : base.ValidValues(member);
        internal double DurationMultiplier => factors[Array.IndexOf(durations, duration)];
        internal double Multiplier => rp;

        [GameParameters.CustomStringParameterUI("", autoPersistance = false, lines = 10)]
        public string ReloadNotice
        {
            get => Localizer.Format("#Koffee_Settings_LaunchReloadNotice");
            set { }
        }
        internal double EffectiveMultiplier => pendingPreviousRate ?? Multiplier;

        private readonly struct Boundary
        {
            internal readonly double UntilUT, Multiplier;
            internal Boundary(double until, double multiplier) { UntilUT = until; Multiplier = multiplier; }
        }

        internal void Accept(KoffeeGameParameters previous, double ut)
        {
            if (!ScientistLevelHistory.Time(ut)) throw new ArgumentOutOfRangeException(nameof(ut));
            history.Clear(); history.AddRange(previous.history);
            // Keep the pre-edit interval open until an actual save deserialization.
            // This is temporal evidence, not a second authority for chosen settings.
            double effective = previous.EffectiveMultiplier;
            pendingPreviousRate = Multiplier == effective ? (double?)null : effective;
        }

        internal void ActivateAfterLoad(double ut)
        {
            if (!pendingPreviousRate.HasValue) return;
            if (!ScientistLevelHistory.Time(ut) || (history.Count > 0 && ut < history[history.Count - 1].UntilUT))
                throw new InvalidOperationException("Research activation precedes its saved history.");
            history.Add(new Boundary(ut, pendingPreviousRate.Value));
            pendingPreviousRate = null;
        }

        internal double MultiplierAt(double ut)
        {
            foreach (var boundary in history) if (ut < boundary.UntilUT) return boundary.Multiplier;
            return EffectiveMultiplier;
        }
        internal double NextBoundary(double ut)
        {
            foreach (var boundary in history) if (ut < boundary.UntilUT) return boundary.UntilUT;
            return double.PositiveInfinity;
        }

        public override void OnSave(ConfigNode node)
        {
            // Auxiliary temporal evidence, separate from the two stock-persisted controls.
            if (pendingPreviousRate.HasValue)
                node.AddValue("pendingPreviousRate", pendingPreviousRate.Value.ToString("R", CultureInfo.InvariantCulture));
            foreach (var boundary in history)
            {
                var entry = node.AddNode("RESEARCH_RATE_INTERVAL");
                entry.AddValue("untilUT", boundary.UntilUT.ToString("R", CultureInfo.InvariantCulture));
                entry.AddValue("effectiveMultiplier", boundary.Multiplier.ToString("R", CultureInfo.InvariantCulture));
            }
        }
        public override void OnLoad(ConfigNode node)
        {
            history.Clear();
            pendingPreviousRate = null;
            if (node.HasValue("pendingPreviousRate"))
            {
                if (!double.TryParse(node.GetValue("pendingPreviousRate"), NumberStyles.Float, CultureInfo.InvariantCulture, out double previous) ||
                    double.IsNaN(previous) || previous < 0.1 || previous > 10)
                    throw new InvalidOperationException("Invalid pending Koffee research interval.");
                pendingPreviousRate = previous;
            }
            foreach (var entry in node.GetNodes("RESEARCH_RATE_INTERVAL"))
            {
                if (!double.TryParse(entry.GetValue("untilUT"), NumberStyles.Float, CultureInfo.InvariantCulture, out double ut) ||
                    !double.TryParse(entry.GetValue("effectiveMultiplier"), NumberStyles.Float, CultureInfo.InvariantCulture, out double factor) ||
                    !ScientistLevelHistory.Time(ut) || double.IsNaN(factor) || factor < 0.1 || factor > 10 ||
                    (history.Count > 0 && ut < history[history.Count - 1].UntilUT))
                    throw new InvalidOperationException("Invalid Koffee research interval history.");
                history.Add(new Boundary(ut, factor));
            }
        }
    }

    internal static class KoffeeResearchRates
    {
        internal static KoffeeGameParameters? Current => HighLogic.CurrentGame?.Parameters.CustomParams<KoffeeGameParameters>();
        internal static double At(double ut) => Current?.MultiplierAt(ut) ?? 1;
        internal static double NextBoundary(double ut) => Current?.NextBoundary(ut) ?? double.PositiveInfinity;
        internal static double RatePerHour(int affinity, int level) => StationResearchMath.RatePerHour(affinity, level) * (Current?.EffectiveMultiplier ?? 1);
        internal static double RatePerMinute(int affinity, int level) => RatePerHour(affinity, level) / 60;
        internal static double RatePerHourAt(int affinity, int level, double ut) => StationResearchMath.RatePerHour(affinity, level) * At(ut);
    }
}

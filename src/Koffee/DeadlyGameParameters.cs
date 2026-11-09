using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    public sealed class DeadlyGameParameters : GameParameters.CustomParameterNode
    {
        private int hours = 12;
        private readonly List<KeyValuePair<double, DeadlyOptions>> history = new List<KeyValuePair<double, DeadlyOptions>>();
        private readonly List<ConfigNode> opaque = new List<ConfigNode>();
        private bool invalidHistory;
        public override string Title => Localizer.Format("#Koffee_Deadly_Settings");
        public override string Section => "Koffee";
        public override string DisplaySection => "Koffee";
        public override int SectionOrder => 2;
        public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;
        public override bool HasPresets => false;

        [GameParameters.CustomParameterUI("#Koffee_Deadly_Enable", toolTip = "#Koffee_Health_EnableHelp", autoPersistance = true)]
        public bool DeadlyEnabled { get; set; }

        public int AutonomyHours
        {
            get => hours;
            set { if (value >= 1 && value <= 168) hours = value; }
        }

        [GameParameters.CustomStringParameterUI("#Koffee_Health_Duration", lines = 2, autoPersistance = false)]
        public string Duration
        {
            get => Localizer.Format("#Koffee_Health_DurationValue") +
                (invalidHistory ? "\n" + Localizer.Format("#Koffee_Deadly_InvalidHistory") : "");
            set { }
        }

        public override bool Interactible(MemberInfo member, GameParameters parameters) => member.Name != nameof(Duration);
        internal DeadlyOptions Current => new DeadlyOptions(DeadlyEnabled && !invalidHistory, hours * 3600d);
        internal void Accept(DeadlyGameParameters previous, double ut)
        {
            if (!ScientistLevelHistory.Time(ut) || (previous.history.Count > 0 && ut < previous.history[previous.history.Count - 1].Key))
                throw new InvalidOperationException("Invalid Deadly acceptance time.");
            history.Clear(); history.AddRange(previous.history);
            opaque.Clear(); opaque.AddRange(previous.opaque.Select(n => n.CreateCopy())); invalidHistory = previous.invalidHistory;
            if (!Current.Equals(previous.Current)) history.Add(new KeyValuePair<double, DeadlyOptions>(ut, previous.Current));
        }
        internal DeadlyOptions At(double ut)
        {
            if (invalidHistory) return new DeadlyOptions(false, hours * 3600d);
            foreach (var interval in history) if (ut < interval.Key) return interval.Value;
            return Current;
        }
        internal double NextBoundary(double ut)
        {
            foreach (var interval in history) if (ut < interval.Key) return interval.Key;
            return double.PositiveInfinity;
        }
        public override void OnSave(ConfigNode node)
        {
            foreach (var child in opaque) node.AddNode(child.CreateCopy());
            foreach (var interval in history)
            {
                var child = node.AddNode("DEADLY_INTERVAL");
                child.AddValue("untilUT", interval.Key.ToString("R", CultureInfo.InvariantCulture));
                child.AddValue("enabled", interval.Value.Enabled.ToString());
                child.AddValue("seconds", interval.Value.AutonomySeconds.ToString("R", CultureInfo.InvariantCulture));
            }
        }
        public override void OnLoad(ConfigNode node)
        {
            history.Clear(); opaque.Clear(); invalidHistory = false;
            foreach (var child in node.GetNodes("DEADLY_INTERVAL"))
            {
                if (child.GetValues("untilUT").Length != 1 || child.GetValues("enabled").Length != 1 || child.GetValues("seconds").Length != 1 ||
                    !double.TryParse(child.GetValue("untilUT"), NumberStyles.Float, CultureInfo.InvariantCulture, out double ut) ||
                    !ScientistLevelHistory.Time(ut) || !bool.TryParse(child.GetValue("enabled"), out bool enabled) ||
                    !double.TryParse(child.GetValue("seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) ||
                    !DeadlyOptions.ValidDuration(seconds) || (history.Count > 0 && ut < history[history.Count - 1].Key))
                { invalidHistory = true; opaque.Add(child.CreateCopy()); continue; }
                history.Add(new KeyValuePair<double, DeadlyOptions>(ut, new DeadlyOptions(enabled, seconds)));
            }
        }
    }

    internal readonly struct DeadlyOptions : IEquatable<DeadlyOptions>
    {
        internal readonly bool Enabled;
        internal readonly double AutonomySeconds;
        internal DeadlyOptions(bool enabled, double seconds)
        { Enabled = enabled; AutonomySeconds = ValidDuration(seconds) ? seconds : 43200; }
        internal static bool ValidDuration(double seconds) => seconds >= 3600 && seconds <= 604800 && seconds % 3600 == 0;
        public bool Equals(DeadlyOptions other) => Enabled == other.Enabled && AutonomySeconds == other.AutonomySeconds;
    }
}

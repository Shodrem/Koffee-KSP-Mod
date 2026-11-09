using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    public sealed class KomfortGameParameters : GameParameters.CustomParameterNode
    {
        private string seats = "3";
        private static readonly string[] choices = { "1", "2", "3", "4", "5", "6" };
        private readonly List<Interval> history = new List<Interval>();
        public override string Title => Localizer.Format("#Koffee_Komfort_Settings");
        public override string Section => "Koffee";
        public override string DisplaySection => "Koffee";
        public override int SectionOrder => 1;
        public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;
        public override bool HasPresets => false;

        [GameParameters.CustomParameterUI("#Koffee_Komfort_Enable", toolTip = "#Koffee_Komfort_EnableHelp", autoPersistance = true)]
        public bool SystemEnabled { get; set; } = true;

        [GameParameters.CustomParameterUI("#Koffee_Komfort_Seats", toolTip = "#Koffee_Komfort_SeatsHelp", autoPersistance = true)]
        public string SeatsPerKerbal
        {
            get => seats;
            set { if (Array.IndexOf(choices, value) >= 0) seats = value; }
        }

        [GameParameters.CustomParameterUI("#Koffee_Komfort_Labs", toolTip = "#Koffee_Komfort_LabsHelp", autoPersistance = true)]
        public bool LaboratorySeats { get; set; } = true;

        public override IList ValidValues(MemberInfo member) => member.Name == nameof(SeatsPerKerbal)
            ? Array.AsReadOnly(choices) : base.ValidValues(member);
        public override bool Interactible(MemberInfo member, GameParameters parameters) =>
            member.Name == nameof(SystemEnabled) || SystemEnabled;

        internal KomfortOptions Current => new KomfortOptions(SystemEnabled, seats[0] - '0', LaboratorySeats);
        private readonly struct Interval
        {
            internal readonly double Until;
            internal readonly KomfortOptions Options;
            internal Interval(double until, KomfortOptions options) { Until = until; Options = options; }
        }
        internal void Accept(KomfortGameParameters previous, double ut)
        {
            if (!ScientistLevelHistory.Time(ut) || (previous.history.Count > 0 && ut < previous.history[previous.history.Count - 1].Until))
                throw new InvalidOperationException("Invalid Komfort acceptance time.");
            history.Clear(); history.AddRange(previous.history);
            if (!Current.Equals(previous.Current)) history.Add(new Interval(ut, previous.Current));
        }
        internal KomfortOptions At(double ut)
        {
            foreach (var interval in history) if (ut < interval.Until) return interval.Options;
            return Current;
        }
        internal double NextBoundary(double ut)
        {
            foreach (var interval in history) if (ut < interval.Until) return interval.Until;
            return double.PositiveInfinity;
        }
        public override void OnSave(ConfigNode node)
        {
            foreach (var interval in history)
            {
                var saved = node.AddNode("KOMFORT_INTERVAL");
                saved.AddValue("untilUT", interval.Until.ToString("R", CultureInfo.InvariantCulture));
                saved.AddValue("enabled", interval.Options.Enabled.ToString());
                saved.AddValue("seats", interval.Options.Seats.ToString(CultureInfo.InvariantCulture));
                saved.AddValue("labs", interval.Options.Labs.ToString());
            }
        }
        public override void OnLoad(ConfigNode node)
        {
            history.Clear();
            foreach (var saved in node.GetNodes("KOMFORT_INTERVAL"))
            {
                if (!double.TryParse(saved.GetValue("untilUT"), NumberStyles.Float, CultureInfo.InvariantCulture, out double ut) ||
                    !ScientistLevelHistory.Time(ut) || !bool.TryParse(saved.GetValue("enabled"), out bool enabled) ||
                    !int.TryParse(saved.GetValue("seats"), out int count) || count < 1 || count > 6 ||
                    !bool.TryParse(saved.GetValue("labs"), out bool labs) || (history.Count > 0 && ut < history[history.Count - 1].Until))
                    throw new InvalidOperationException("Invalid Komfort interval history.");
                history.Add(new Interval(ut, new KomfortOptions(enabled, count, labs)));
            }
        }
    }

    internal readonly struct KomfortOptions : IEquatable<KomfortOptions>
    {
        internal readonly bool Enabled, Labs;
        internal readonly int Seats;
        internal KomfortOptions(bool enabled, int seats, bool labs)
        { Enabled = enabled; Seats = seats >= 1 && seats <= 6 ? seats : 3; Labs = labs; }
        public bool Equals(KomfortOptions other) => Enabled == other.Enabled && Labs == other.Labs && Seats == other.Seats;
    }

    internal static class KomfortMath
    {
        internal static double? Calculate(int crew, int seats, int laboratorySeats, KomfortOptions options,
            double climate, double air, double leisure, bool home, double hygiene = 0)
        {
            if (crew <= 0) return null;
            if (!options.Enabled) return 1;
            double accommodation = Clamp((Math.Max(0, seats) + (options.Labs ? Math.Max(0, laboratorySeats) : 0)) / (double)(options.Seats * crew));
            return .5 * accommodation + .1 * (Clamp(climate / crew) + Clamp(air / crew) + Clamp(leisure / crew) + Clamp(hygiene / crew) + (home ? 1 : 0));
        }
        internal static double Factor(double? komfort) => komfort.HasValue ? Math.Max(.25, Math.Min(1, komfort.Value)) : 1;
        private static double Clamp(double value) => double.IsNaN(value) || value < 0 ? 0 : Math.Min(1, value);
    }
}

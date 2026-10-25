using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal readonly struct EngineeringDiagnosticSample
    {
        internal readonly PartModule Module;
        internal readonly string PartName;
        internal readonly uint FlightId;
        internal readonly string TopEngineer;
        internal readonly int TopEngineerLevel;
        internal readonly double StartUT;
        internal readonly double EndUT;
        internal readonly double AccessibleCups;
        internal readonly double StockEfficiency;
        internal readonly double KoffeeBonus;
        internal readonly double FinalEfficiency;

        internal EngineeringDiagnosticSample(PartModule module, string partName, uint flightId,
            string topEngineer, int topEngineerLevel, double startUT, double endUT,
            double accessibleCups, double stockEfficiency, double koffeeBonus,
            double finalEfficiency)
        {
            Module = module;
            PartName = partName;
            FlightId = flightId;
            TopEngineer = topEngineer;
            TopEngineerLevel = topEngineerLevel;
            StartUT = startUT;
            EndUT = endUT;
            AccessibleCups = accessibleCups;
            StockEfficiency = stockEfficiency;
            KoffeeBonus = koffeeBonus;
            FinalEfficiency = finalEfficiency;
        }
    }

    internal static class EngineeringDiagnostics
    {
        private static readonly Dictionary<PartModule, State> Last =
            new Dictionary<PartModule, State>();

        internal static void Report(EngineeringDiagnosticSample sample, bool progressed,
            double timeFactor, Exception? error)
        {
            bool boosted = sample.KoffeeBonus > 1e-12;
            bool sourceAccessible = sample.AccessibleCups >= 1;
            bool failed = error != null;
            var state = new State(boosted, sourceAccessible, progressed, failed);
            if (Last.TryGetValue(sample.Module, out State previous) && previous.Equals(state)) return;
            Last[sample.Module] = state;

            double multiplier = sample.StockEfficiency > 0
                ? sample.FinalEfficiency / sample.StockEfficiency : 1;
            Debug.Log("[Koffee][Engineering] part=" + sample.PartName +
                " flightID=" + sample.FlightId.ToString(CultureInfo.InvariantCulture) +
                " module=" + sample.Module.GetType().Name +
                " mode=" + (boosted ? "KOFFEE" : "STOCK") +
                " topEngineer=" + sample.TopEngineer +
                " level=" + sample.TopEngineerLevel.ToString(CultureInfo.InvariantCulture) +
                " accessibleCupsBefore=" + Format(sample.AccessibleCups) +
                " processed=" + (progressed ? "yes" : "no") +
                " TimeFactor=" + Format(timeFactor) +
                " intervalUT=" + Format(sample.StartUT) + ".." + Format(sample.EndUT) +
                " stockEfficiency=" + Format(sample.StockEfficiency) +
                " weightedKoffeeBonus=" + Format(sample.KoffeeBonus * 100) + "%" +
                " finalEfficiency=" + Format(sample.FinalEfficiency) +
                " productionMultiplier=" + Format(multiplier) +
                (failed ? " error=" + error!.GetType().Name : ""));
        }

        internal static void Clear() => Last.Clear();

        private static string Format(double value) =>
            value.ToString("0.######", CultureInfo.InvariantCulture);

        private readonly struct State : IEquatable<State>
        {
            private readonly bool boosted;
            private readonly bool sourceAccessible;
            private readonly bool progressed;
            private readonly bool failed;

            internal State(bool boosted, bool sourceAccessible, bool progressed, bool failed)
            {
                this.boosted = boosted;
                this.sourceAccessible = sourceAccessible;
                this.progressed = progressed;
                this.failed = failed;
            }

            public bool Equals(State other) => boosted == other.boosted &&
                sourceAccessible == other.sourceAccessible && progressed == other.progressed &&
                failed == other.failed;
            public override bool Equals(object? obj) => obj is State other && Equals(other);
            public override int GetHashCode() => (boosted ? 1 : 0) | (sourceAccessible ? 2 : 0) |
                (progressed ? 4 : 0) | (failed ? 8 : 0);
        }
    }
}

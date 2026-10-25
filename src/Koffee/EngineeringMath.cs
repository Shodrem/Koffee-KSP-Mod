using System;

namespace ShoAerospace.Koffee
{
    internal static class EngineeringMath
    {
        internal static double WeightedBonus(double weightedSeconds, double interval)
        {
            if (!Finite(weightedSeconds) || !Finite(interval) || weightedSeconds <= 0 || interval <= 0) return 0;
            return weightedSeconds / interval;
        }

        internal static double Apply(double stockEfficiency, double weightedBonus)
        {
            if (!Finite(stockEfficiency) || stockEfficiency <= 0 ||
                !Finite(weightedBonus) || weightedBonus <= 0) return stockEfficiency;
            double result = stockEfficiency * (1 + weightedBonus);
            return Finite(result) && result > 0 ? result : stockEfficiency;
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

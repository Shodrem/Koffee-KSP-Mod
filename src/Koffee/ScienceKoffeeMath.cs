using System;

namespace ShoAerospace.Koffee
{
    internal static class ScienceKoffeeMath
    {
        internal static double Apply(double stockContribution, double koffeeBonus,
            double coveredSeconds, double intervalSeconds)
        {
            if (!Finite(koffeeBonus) || koffeeBonus <= 0 ||
                !Finite(coveredSeconds) || coveredSeconds <= 0 ||
                !Finite(intervalSeconds) || intervalSeconds <= 0 ||
                !Finite(stockContribution)) return stockContribution;

            double coverage = Math.Min(1, coveredSeconds / intervalSeconds);
            double result = stockContribution * (1 + koffeeBonus * coverage);
            return Finite(result) ? result : stockContribution;
        }

        private static bool Finite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

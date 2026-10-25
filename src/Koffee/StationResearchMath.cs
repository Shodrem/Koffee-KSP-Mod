using System;

namespace ShoAerospace.Koffee
{
    internal static class StationResearchMath
    {
        internal const double PointsPerMinutePerScore = 0.1;
        // Maximum score 15 and global speed 10x. Duration changes launch
        // budgets, never the processing rate or this validation ceiling.
        internal const double MaximumRatePerHour = 90 * 10;

        internal static double RatePerMinute(int affinity, int level)
        {
            ValidateWorker(affinity, level);
            return (affinity + level) * PointsPerMinutePerScore;
        }

        internal static double RatePerHour(int affinity, int level)
        {
            ValidateWorker(affinity, level);
            return 6.0 * (affinity + level);
        }

        internal static double RemainingSeconds(double required, double accumulated, double ratePerHour)
        {
            ValidateWork(required, accumulated, ratePerHour);
            double seconds = (required - accumulated) / ratePerHour * 3600;
            NonNegativeFinite(seconds, nameof(ratePerHour));
            return seconds;
        }

        // Only the runtime can certify coverage and UT boundaries. This pure function
        // converts a single covered interval; it never buys Koffee or revisits past work.
        internal static double Credit(double required, double accumulated, double ratePerHour,
            double coveredSeconds)
        {
            NonNegativeFinite(coveredSeconds, nameof(coveredSeconds));
            double remaining = RemainingSeconds(required, accumulated, ratePerHour);
            if (coveredSeconds == 0) return accumulated;
            if (coveredSeconds >= remaining) return required;
            double credited = Math.Min(required, accumulated + ratePerHour * (coveredSeconds / 3600));
            // Match checkpoint normalization before deciding whether the team needs
            // another coffee window at an otherwise complete boundary.
            return required - credited < 1e-9 ? required : credited;
        }

        private static void ValidateWorker(int affinity, int level)
        {
            if (affinity < 1 || affinity > 10) throw new ArgumentOutOfRangeException(nameof(affinity));
            if (level < 0 || level > 5) throw new ArgumentOutOfRangeException(nameof(level));
        }

        private static void ValidateWork(double required, double accumulated, double ratePerHour)
        {
            PositiveFinite(required, nameof(required));
            NonNegativeFinite(accumulated, nameof(accumulated));
            if (accumulated > required) throw new ArgumentOutOfRangeException(nameof(accumulated));
            PositiveFinite(ratePerHour, nameof(ratePerHour));
        }

        private static void PositiveFinite(double value, string parameter)
        {
            if (!Finite(value) || value <= 0) throw new ArgumentOutOfRangeException(parameter);
        }

        private static void NonNegativeFinite(double value, string parameter)
        {
            if (!Finite(value) || value < 0) throw new ArgumentOutOfRangeException(parameter);
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

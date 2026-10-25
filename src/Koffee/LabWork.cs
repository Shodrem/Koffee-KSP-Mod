using System;

namespace ShoAerospace.Koffee
{
    internal static class LabWork
    {
        internal static bool Positive(double value) => value > 0 &&
            !double.IsNaN(value) && !double.IsInfinity(value);

        internal static double Effective(double interval, double efficiency, double delta) =>
            delta * ((interval * efficiency) / delta);

        internal static double Coverage(double activationStartUT, double activationEndUT,
            double intervalStartUT, double intervalEndUT)
        {
            if (activationEndUT <= activationStartUT || intervalEndUT <= intervalStartUT) return 0;
            return Math.Max(0, Math.Min(intervalEndUT, activationEndUT) -
                Math.Max(intervalStartUT, activationStartUT));
        }

        internal static bool CanAdvance(double factor, float data, float stored, double contribution,
            double used, double produced, int cap) =>
            data >= 1e-9 && contribution >= 1e-9 && factor >= 1e-9 &&
            Positive(used) && Positive(produced) && used <= data && produced + stored <= cap &&
            (data - (float)used != data || stored + (float)produced != stored);
    }
}

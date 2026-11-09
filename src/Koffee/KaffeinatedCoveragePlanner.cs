using System;
using System.Collections.Generic;

namespace ShoAerospace.Koffee
{
    internal readonly struct KaffeinatedWindow
    {
        internal readonly double StartUT;
        internal readonly double EndUT;

        internal KaffeinatedWindow(double startUT, double endUT)
        {
            StartUT = startUT;
            EndUT = endUT;
        }

        internal bool Contains(double universalTime) =>
            StartUT <= universalTime && universalTime < EndUT;
    }

    internal static class KaffeinatedCoveragePlanner
    {
        internal static double ContinuousCoverage(KaffeinatedWindow? initial,
            double startUT, double endUT, double duration, bool mayPurchase,
            Func<double, double> tryRenew)
        {
            if (!Positive(duration) || endUT <= startUT) return 0;
            double coverage = initial.HasValue
                ? LabWork.Coverage(initial.Value.StartUT, initial.Value.EndUT, startUT, endUT) : 0;
            if (!mayPurchase || initial.HasValue &&
                (initial.Value.EndUT >= endUT || initial.Value.StartUT > startUT)) return coverage;

            double renewalUT = initial.HasValue && initial.Value.EndUT > startUT
                ? initial.Value.EndUT : startUT;
            while (renewalUT < endUT)
            {
                double next = tryRenew(renewalUT);
                if (!Positive(next) || next <= renewalUT) break;
                coverage += Math.Max(0, Math.Min(endUT, next) - renewalUT);
                renewalUT = next;
            }
            return Math.Min(endUT - startUT, coverage);
        }

        private static bool Positive(double value) => value > 0 && EngineeringMath.Finite(value);
    }
}

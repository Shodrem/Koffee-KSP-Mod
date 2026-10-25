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

        internal static double WeightedBonus<T>(IReadOnlyList<EngineeringCandidate<T>> ordered,
            EngineerSelection policy, double startUT, double endUT,
            Func<T, IReadOnlyList<KaffeinatedWindow>> windows,
            Func<int, double> bonusForLevel,
            Func<T, double, KaffeinatedWindow?> tryActivate,
            Action<T, KaffeinatedWindow> observe)
        {
            if (ordered.Count == 0 || endUT <= startUT) return 0;
            double cursor = startUT;
            double weightedSeconds = 0;
            int guard = 0;
            while (cursor < endUT && guard++ < 100000)
            {
                int selected = ActiveCandidate(ordered, policy, cursor, windows,
                    out KaffeinatedWindow active);
                if (selected < 0)
                {
                    double future = NextStart(ordered, policy, cursor, endUT, windows);
                    if (future < endUT)
                    {
                        cursor = future;
                        continue;
                    }

                    selected = 0;
                    if (bonusForLevel(ordered[selected].Level) <= 0) break;
                    KaffeinatedWindow? activated = tryActivate(ordered[selected].Worker, cursor);
                    if (!activated.HasValue || !activated.Value.Contains(cursor)) break;
                    active = activated.Value;
                }

                observe(ordered[selected].Worker, active);
                double segmentEnd = Math.Min(endUT, active.EndUT);
                if (policy == EngineerSelection.AnyKaffeinated)
                {
                    double betterStart = BetterCandidateStart(ordered, selected, cursor,
                        segmentEnd, windows);
                    if (betterStart < segmentEnd) segmentEnd = betterStart;
                }
                if (segmentEnd <= cursor) break;
                weightedSeconds += (segmentEnd - cursor) *
                    Math.Max(0, bonusForLevel(ordered[selected].Level));
                cursor = segmentEnd;
            }
            return EngineeringMath.WeightedBonus(weightedSeconds, endUT - startUT);
        }

        private static int ActiveCandidate<T>(IReadOnlyList<EngineeringCandidate<T>> ordered,
            EngineerSelection policy, double universalTime,
            Func<T, IReadOnlyList<KaffeinatedWindow>> windows, out KaffeinatedWindow active)
        {
            int count = policy == EngineerSelection.BestEngineer ? Math.Min(1, ordered.Count) : ordered.Count;
            for (int i = 0; i < count; i++)
                if (ActiveWindow(windows(ordered[i].Worker), universalTime, out active)) return i;
            active = default;
            return -1;
        }

        private static double NextStart<T>(IReadOnlyList<EngineeringCandidate<T>> ordered,
            EngineerSelection policy, double cursor, double endUT,
            Func<T, IReadOnlyList<KaffeinatedWindow>> windows)
        {
            double next = endUT;
            int count = policy == EngineerSelection.BestEngineer ? Math.Min(1, ordered.Count) : ordered.Count;
            for (int i = 0; i < count; i++)
                foreach (KaffeinatedWindow window in windows(ordered[i].Worker))
                    if (window.StartUT > cursor && window.StartUT < next) next = window.StartUT;
            return next;
        }

        private static double BetterCandidateStart<T>(IReadOnlyList<EngineeringCandidate<T>> ordered,
            int selected, double cursor, double segmentEnd,
            Func<T, IReadOnlyList<KaffeinatedWindow>> windows)
        {
            double next = segmentEnd;
            for (int i = 0; i < selected; i++)
                foreach (KaffeinatedWindow window in windows(ordered[i].Worker))
                    if (window.StartUT > cursor && window.StartUT < next) next = window.StartUT;
            return next;
        }

        private static bool ActiveWindow(IReadOnlyList<KaffeinatedWindow> windows,
            double universalTime, out KaffeinatedWindow active)
        {
            for (int i = 0; i < windows.Count; i++)
                if (windows[i].Contains(universalTime))
                {
                    active = windows[i];
                    return true;
                }
            active = default;
            return false;
        }

        private static bool Positive(double value) => value > 0 && EngineeringMath.Finite(value);
    }
}

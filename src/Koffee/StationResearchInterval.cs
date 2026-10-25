using System;
using System.Collections.Generic;

namespace ShoAerospace.Koffee
{
    internal readonly struct StationResearchCredit
    {
        internal double Points { get; }
        internal double CoveredSeconds { get; }
        internal bool FullyCovered { get; }
        internal bool Complete { get; }
        internal StationResearchCredit(double points, double seconds, bool full, bool complete)
        { Points = points; CoveredSeconds = seconds; FullyCovered = full; Complete = complete; }
    }

    internal static class StationResearchInterval
    {
        // Coverage receives UT seconds, clipped at completion in each rate segment.
        // It may skip uncovered time. A varying-rate segment never reuses a summed
        // coverage duration at a different level or a different UT.
        internal static StationResearchCredit Process(double required, double accumulated, int affinity,
            IReadOnlyList<ScientistLevelSegment> segments, Func<double, double, double, double> coverage,
            Func<double, double>? multiplierAt = null, Func<double, double>? nextBoundary = null)
        {
            double original = accumulated, seconds = 0;
            bool full = true;
            foreach (var segment in segments)
            {
                double from = segment.StartUT;
                while (from < segment.EndUT)
                {
                    double to = Math.Min(segment.EndUT, nextBoundary?.Invoke(from) ?? double.PositiveInfinity);
                    double multiplier = multiplierAt?.Invoke(from) ?? 1;
                    if (!(to > from) || !ExperimentContainerState.Finite(multiplier) || multiplier <= 0)
                        throw new InvalidOperationException("Invalid research rate interval.");
                    double rate = segment.RatePerHour(affinity) * multiplier;
                    double remaining = StationResearchMath.RemainingSeconds(required, accumulated, rate);
                    if (remaining <= 0) break;
                    double covered = coverage(from, to, remaining);
                    double limit = Math.Min(to - from, remaining);
                    if (!ExperimentContainerState.Finite(covered) || covered < 0 || covered > limit + 1e-8)
                        throw new InvalidOperationException("Coverage returned invalid research seconds.");
                    covered = Math.Min(covered, limit);
                    accumulated = StationResearchMath.Credit(required, accumulated, rate, covered);
                    seconds += covered;
                    if (accumulated >= required) break;
                    if (covered + 1e-8 < to - from) full = false;
                    from = to;
                }
                if (accumulated >= required) break;
            }
            return new StationResearchCredit(accumulated - original, seconds, full, accumulated >= required);
        }
    }
}

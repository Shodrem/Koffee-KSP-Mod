using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal readonly struct ScientistLevelBoundary
    {
        internal double UT { get; }
        internal int Level { get; }
        // A missing event leaves a gap [KnownThroughUT, UT). Normal events use UT.
        internal double KnownThroughUT { get; }
        internal ScientistLevelBoundary(double ut, int level, double knownThroughUT)
        {
            UT = ut; Level = level; KnownThroughUT = knownThroughUT;
        }
    }

    internal readonly struct ScientistLevelSegment
    {
        internal double StartUT { get; }
        internal double EndUT { get; }
        internal int Level { get; }
        internal double Seconds => EndUT - StartUT;
        internal double RatePerHour(int affinity) => StationResearchMath.RatePerHour(affinity, Level);
        internal ScientistLevelSegment(double start, double end, int level)
        {
            StartUT = start; EndUT = end; Level = level;
        }
    }

    internal enum ScientistLevelObservation { Unchanged, Changed, Gap, Rejected }

    // Records effective stock levels, never pending XP. All boundaries are retained:
    // multiple experiments may still need different earlier checkpoints.
    internal sealed class ScientistLevelHistory
    {
        private readonly List<ScientistLevelBoundary> boundaries;
        internal IReadOnlyList<ScientistLevelBoundary> Boundaries => boundaries.AsReadOnly();
        internal double LastObservedUT { get; private set; }
        internal int CurrentLevel => boundaries[boundaries.Count - 1].Level;

        internal ScientistLevelHistory(int level, double ut) : this(
            new[] { new ScientistLevelBoundary(ut, level, ut) }, ut) { }

        internal ScientistLevelHistory(IEnumerable<ScientistLevelBoundary> entries, double lastObservedUT)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            boundaries = entries.ToList();
            if (boundaries.Count == 0 || !Time(lastObservedUT)) throw new ArgumentException("Invalid level history.");
            for (int i = 0; i < boundaries.Count; i++)
            {
                var entry = boundaries[i];
                if (!Time(entry.UT) || !Time(entry.KnownThroughUT) || entry.Level < 0 || entry.Level > 5 ||
                    entry.KnownThroughUT > entry.UT || (i == 0 && entry.KnownThroughUT != entry.UT) ||
                    (i > 0 && (entry.UT < boundaries[i - 1].UT ||
                        entry.KnownThroughUT < boundaries[i - 1].UT)))
                    throw new ArgumentException("Invalid level boundary.");
            }
            if (lastObservedUT < boundaries[boundaries.Count - 1].UT)
                throw new ArgumentException("Observation precedes level history.");
            LastObservedUT = lastObservedUT;
        }

        internal ScientistLevelObservation Observe(int level, double ut, bool effectiveEvent)
        {
            if (level < 0 || level > 5 || !Time(ut) || ut < LastObservedUT)
                return ScientistLevelObservation.Rejected;
            var result = ScientistLevelObservation.Unchanged;
            if (level != CurrentLevel)
            {
                boundaries.Add(new ScientistLevelBoundary(ut, level, effectiveEvent ? ut : LastObservedUT));
                result = effectiveEvent || ut == LastObservedUT ?
                    ScientistLevelObservation.Changed : ScientistLevelObservation.Gap;
            }
            LastObservedUT = ut;
            return result;
        }

        // Only partitions time by level. The caller must separately certify context,
        // pauses and team coverage, and advance its own checkpoint transactionally.
        internal bool TryGetSegments(double start, double end, out IReadOnlyList<ScientistLevelSegment> segments)
        {
            segments = Array.Empty<ScientistLevelSegment>();
            if (!Time(start) || !Time(end) || end < start || start < boundaries[0].UT) return false;
            if (start == end) return true;
            var result = new List<ScientistLevelSegment>();
            for (int i = 0; i < boundaries.Count; i++)
            {
                var entry = boundaries[i];
                double until = i + 1 < boundaries.Count ? boundaries[i + 1].KnownThroughUT : end;
                if (i + 1 < boundaries.Count && until < boundaries[i + 1].UT &&
                    start < boundaries[i + 1].UT && end > until)
                    return false; // The requested interval intersects an uncertain gap.
                double from = Math.Max(start, entry.UT);
                double to = Math.Min(end, until);
                if (to > from) result.Add(new ScientistLevelSegment(from, to, entry.Level));
            }
            segments = result.AsReadOnly();
            return true;
        }

        internal static bool Time(double ut) => !double.IsNaN(ut) && !double.IsInfinity(ut) && ut >= 0;
    }
}

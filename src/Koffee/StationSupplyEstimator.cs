using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal readonly struct StationSupplyWork
    {
        internal readonly string Scientist, Engineer;
        internal readonly double StartUT, EndUT;
        internal readonly bool RequiresEngineer;
        internal StationSupplyWork(string scientist, string engineer, double start, double end, bool requiresEngineer = true)
        { Scientist = scientist; Engineer = engineer; StartUT = start; EndUT = end; RequiresEngineer = requiresEngineer; }
    }

    internal sealed class StationSupplyEstimate
    {
        internal IReadOnlyDictionary<string, int> ScientistPurchases { get; }
        internal int EngineerPurchases { get; }
        internal int TotalPurchases => ScientistPurchases.Values.Sum() + EngineerPurchases;
        internal StationSupplyEstimate(Dictionary<string, int> scientists, int engineers)
        {
            ScientistPurchases = new ReadOnlyDictionary<string, int>(scientists);
            EngineerPurchases = engineers;
        }
    }

    // Virtual windows over a fixed calendar. No resource, ledger or runtime dependencies.
    internal static class StationSupplyEstimator
    {
        internal static bool TryEstimate(IReadOnlyList<StationSupplyWork> work,
            IReadOnlyDictionary<string, IReadOnlyList<KaffeinatedWindow>> paid,
            double duration, out StationSupplyEstimate? estimate)
        {
            estimate = null;
            if (!Finite(duration) || duration <= 0 || work == null || paid == null) return false;
            if (work.Any(w => string.IsNullOrEmpty(w.Scientist) || (w.RequiresEngineer && string.IsNullOrEmpty(w.Engineer)) ||
                w.Scientist == w.Engineer || !Finite(w.StartUT) || w.StartUT < 0 ||
                !Finite(w.EndUT) || w.EndUT < w.StartUT)) return false;
            var scientistNames = new HashSet<string>(work.Select(w => w.Scientist), StringComparer.Ordinal);
            var engineerNames = new HashSet<string>(work.Where(w => w.RequiresEngineer).Select(w => w.Engineer), StringComparer.Ordinal);
            if (scientistNames.Overlaps(engineerNames)) return false;
            var windows = new Dictionary<string, List<KaffeinatedWindow>>(StringComparer.Ordinal);
            var purchases = new Dictionary<string, int>(StringComparer.Ordinal);
            var virtualIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string name in scientistNames.Concat(engineerNames))
            {
                var copy = paid.TryGetValue(name, out var existing) ? existing.ToList() : new List<KaffeinatedWindow>();
                if (copy.Any(w => !Finite(w.StartUT) || w.StartUT < 0 || !Finite(w.EndUT) || w.EndUT <= w.StartUT)) return false;
                windows.Add(name, copy);
                virtualIndex.Add(name, copy.Count);
                purchases.Add(name, 0);
            }
            StationSupplyWork[] intervals = work.Where(w => w.EndUT > w.StartUT).ToArray();
            double cursor = intervals.Select(w => w.StartUT).DefaultIfEmpty(0).Min();
            double end = intervals.Select(w => w.EndUT).DefaultIfEmpty(0).Max();
            // Bound pathological modded forecasts before building thousands of virtual windows.
            if ((end - cursor) / duration > 100000) return false;
            int guard = 0;
            while (cursor < end)
            {
                if (++guard > 100000) return false;
                double next = end;
                foreach (StationSupplyWork interval in intervals)
                {
                    if (interval.StartUT > cursor) { next = Math.Min(next, interval.StartUT); continue; }
                    if (interval.EndUT <= cursor) continue;
                    next = Math.Min(next, interval.EndUT);
                    double ready = interval.RequiresEngineer ? Math.Max(NextStart(windows[interval.Scientist], cursor),
                        NextStart(windows[interval.Engineer], cursor)) : NextStart(windows[interval.Scientist], cursor);
                    if (ready > cursor) { next = Math.Min(next, ready); continue; }
                    foreach (string name in interval.RequiresEngineer ? new[] { interval.Scientist, interval.Engineer } : new[] { interval.Scientist })
                    {
                        KaffeinatedWindow? active = At(windows[name], cursor);
                        if (!active.HasValue)
                        {
                            double until = cursor + duration;
                            if (!Finite(until) || until <= cursor) return false;
                            active = new KaffeinatedWindow(cursor, until);
                            // The sweep never revisits past UT. Keep just the latest virtual window.
                            int index = virtualIndex[name];
                            if (windows[name].Count == index) windows[name].Add(active.Value);
                            else windows[name][index] = active.Value;
                            purchases[name]++;
                        }
                        next = Math.Min(next, active.Value.EndUT);
                    }
                }
                if (!(next > cursor)) return false;
                cursor = next;
            }
            estimate = new StationSupplyEstimate(scientistNames.ToDictionary(n => n, n => purchases[n],
                StringComparer.Ordinal), engineerNames.Sum(n => purchases[n]));
            return true;
        }

        private static KaffeinatedWindow? At(List<KaffeinatedWindow> windows, double now) =>
            windows.Where(w => w.Contains(now)).Cast<KaffeinatedWindow?>().LastOrDefault();
        private static double NextStart(List<KaffeinatedWindow> windows, double now) => At(windows, now).HasValue ? now :
            windows.Where(w => w.StartUT > now).Select(w => w.StartUT).DefaultIfEmpty(now).Min();
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

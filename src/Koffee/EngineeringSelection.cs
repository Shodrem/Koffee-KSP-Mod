using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal readonly struct EngineeringCandidate<T>
    {
        internal readonly T Worker;
        internal readonly int Level;
        internal readonly uint PersistentId;
        internal readonly string Name;

        internal EngineeringCandidate(T worker, int level, uint persistentId, string? name)
        {
            Worker = worker;
            Level = level;
            PersistentId = persistentId;
            Name = name ?? "";
        }
    }

    internal static class EngineeringSelection
    {
        internal static IReadOnlyList<EngineeringCandidate<T>> Order<T>(
            IEnumerable<EngineeringCandidate<T>> candidates) => candidates
            .OrderByDescending(candidate => candidate.Level)
            .ThenBy(candidate => candidate.PersistentId == 0 ? uint.MaxValue : candidate.PersistentId)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToArray();

        internal static EngineeringCandidate<T>? Select<T>(
            IReadOnlyList<EngineeringCandidate<T>> ordered, EngineerSelection policy,
            Func<T, bool> isKaffeinated)
        {
            if (ordered.Count == 0) return null;
            if (policy == EngineerSelection.AnyKaffeinated)
                for (int i = 0; i < ordered.Count; i++)
                    if (isKaffeinated(ordered[i].Worker)) return ordered[i];
            return ordered[0];
        }
    }
}

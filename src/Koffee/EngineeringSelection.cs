using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal readonly struct EngineeringCandidate<T>
    {
        internal readonly T Worker;
        internal readonly int Score;
        internal readonly uint PersistentId;
        internal readonly string Name;

        internal EngineeringCandidate(T worker, int score, uint persistentId, string? name)
        {
            Worker = worker;
            Score = score;
            PersistentId = persistentId;
            Name = name ?? "";
        }
    }

    internal static class EngineeringSelection
    {
        internal static IReadOnlyList<EngineeringCandidate<T>> Order<T>(
            IEnumerable<EngineeringCandidate<T>> candidates) => candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.PersistentId == 0 ? uint.MaxValue : candidate.PersistentId)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToArray();

    }
}

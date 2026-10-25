using System;
using System.Collections.Generic;

namespace ShoAerospace.Koffee
{
    // This is a compatibility registry of work types, never a price table.
    public static class ScienceExperimentKoffeeRegistry
    {
        private static readonly HashSet<string> Experiments =
            new HashSet<string>(StringComparer.Ordinal);

        public static void Register(string experimentId)
        {
            if (string.IsNullOrWhiteSpace(experimentId)) throw new ArgumentException(nameof(experimentId));
            Experiments.Add(experimentId);
        }

        public static bool Contains(string? experimentId) =>
            experimentId != null && Experiments.Contains(experimentId);

        internal static void Clear() => Experiments.Clear();
    }
}

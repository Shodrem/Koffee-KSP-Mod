using System;
using System.Collections.Generic;

namespace ShoAerospace.Koffee
{
    internal enum EngineeringAffinity { Extraction, Refining, Systems, Maintenance }

    internal static class EngineeringAffinities
    {
        internal static readonly IReadOnlyList<EngineeringAffinity> All = Array.AsReadOnly(new[] {
            EngineeringAffinity.Extraction, EngineeringAffinity.Refining,
            EngineeringAffinity.Systems, EngineeringAffinity.Maintenance });
        internal static bool IsValid(EngineeringAffinity value) =>
            value >= EngineeringAffinity.Extraction && value <= EngineeringAffinity.Maintenance;
        internal static bool TryParse(string? id, out EngineeringAffinity value)
        {
            value = default;
            return id != null && Enum.TryParse(id, false, out value) && IsValid(value) &&
                string.Equals(id, value.ToString(), StringComparison.Ordinal);
        }
    }
}

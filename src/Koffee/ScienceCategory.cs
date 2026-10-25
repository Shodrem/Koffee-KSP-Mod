using System;
using System.Collections.Generic;

namespace ShoAerospace.Koffee
{
    internal enum ScienceCategory
    {
        Biology,
        Geology,
        MaterialsChemistry,
        Technology,
        Environment,
        Astronomy,
        Physics
    }

    internal static class ScienceCategories
    {
        // Stable IDs, independent of textures, localization and installed experiments.
        internal static readonly IReadOnlyList<ScienceCategory> All = Array.AsReadOnly(new[]
        {
            ScienceCategory.Biology, ScienceCategory.Geology,
            ScienceCategory.MaterialsChemistry, ScienceCategory.Technology,
            ScienceCategory.Environment, ScienceCategory.Astronomy, ScienceCategory.Physics
        });

        internal static bool IsValid(ScienceCategory category) =>
            category >= ScienceCategory.Biology && category <= ScienceCategory.Physics;

        internal static bool TryParse(string? id, out ScienceCategory category)
        {
            category = default;
            return id != null && Enum.TryParse(id, false, out category) && IsValid(category) &&
                string.Equals(id, category.ToString(), StringComparison.Ordinal);
        }
    }
}

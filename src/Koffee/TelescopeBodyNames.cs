using System;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    internal static class TelescopeBodyNames
    {
        internal static string Display(CelestialBody? body,string fallback="")
        {
            // bodyDisplayName normally already contains the active localized text.
            // Resolve an explicit mod localization tag, without formatting literal names again.
            string value=body?.bodyDisplayName ?? fallback;
            return (value.StartsWith("#",StringComparison.Ordinal) ? Localizer.Format(value) : value).LocalizeRemoveGender();
        }
    }
}

using System.Globalization;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    internal static class KoffeeResearchDisplay
    {
        // Editor reference: affinity 8, level 0; a Kerbin day is six hours.
        // This is an estimate at the effective game speed, not an assigned crew ETA.
        internal static string EditorRequiredWork(double basePoints)
        {
            double points = basePoints * (KoffeeResearchRates.Current?.DurationMultiplier ?? 1);
            double days = StationResearchMath.RemainingSeconds(points, 0,
                KoffeeResearchRates.RatePerHour(8, 0)) / 21600;
            return points.ToString("0.##", CultureInfo.CurrentCulture) + " RP (" +
                Localizer.Format("#Koffee_Selector_KerbinDays", days.ToString("0.###", CultureInfo.CurrentCulture)) + ")";
        }

        internal static string Laboratory(StationLabCapability capability) =>
            Localizer.Format("#Koffee_Lab_Capability_" + capability);
        internal static string Category(ScienceCategory category) =>
            Localizer.Format("#Koffee_Research_Category_" + category);
        internal static string Specialties(ScientistSpecialtyProfile? profile) => profile == null ?
            Localizer.Format("#Koffee_Research_ProfileUnavailable") : Localizer.Format("#Koffee_Research_Specialties",
                Category(profile.PrimaryCategory), Category(profile.SecondaryCategory));
        internal static string Unknown => Localizer.Format("#Koffee_Research_Unavailable");
        internal static string RosterStatus(KoffeeScientistStatus scientist) =>
            scientist.Status == "Assigned" && !string.IsNullOrWhiteSpace(scientist.AssignedVesselName) ?
                Localizer.Format("#Koffee_Research_RosterAssignedVessel", scientist.AssignedVesselName) :
                Localizer.Format("#Koffee_Research_Roster_" + scientist.Status);
    }
}

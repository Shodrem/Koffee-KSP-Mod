using System.Globalization;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    internal static class KoffeeCrewDisplay
    {
        internal static string Profession(ProtoCrewMember crew)
        {
            if (crew.type == ProtoCrewMember.KerbalType.Tourist) return Localizer.Format("#Koffee_Crew_Tourist");
            string trait = crew.GetLocalizedTrait();
            if (!string.IsNullOrWhiteSpace(trait)) return trait;
            return Localizer.Format("#Koffee_Crew_Type", crew.type.ToString());
        }

        internal static string Row(KoffeeCrewStatus crew) => Localizer.Format("#Koffee_Crew_Row", crew.Name,
            crew.Profession, crew.Level.ToString(CultureInfo.InvariantCulture));

        internal static string ProfessionLevel(KoffeeCrewStatus crew) => Localizer.Format(
            "#Koffee_Crew_ProfessionLevel", crew.Profession, crew.Level.ToString(CultureInfo.InvariantCulture));

        internal static string Kaffeinated(KoffeeCrewStatus crew) => crew.IsKaffeinated ?
            Localizer.Format("#Koffee_UI_Kaffeinated") + " (" + KerbalWorkState.FormatDuration(crew.RemainingSeconds) + ")" :
            Localizer.Format("#Koffee_UI_NotKaffeinated");

        internal static string State(string status)
        {
            string key = "#Koffee_Research_Roster_" + status;
            string localized = Localizer.Format(key);
            return localized == key ? Localizer.Format("#Koffee_Crew_OtherState", status) : localized;
        }

        internal static string SurvivalReserve(KomfortStatus status)
        {
            if (!status.Value.HasValue) return Komfort(status);
            string points = (100 * status.Value.Value).ToString("0.0", CultureInfo.CurrentCulture) + " / 100";
            return status.Availability == KomfortAvailability.LastKnown ?
                Localizer.Format("#Koffee_Crew_LastKnown", points,
                    status.ObservedUT?.ToString("0.##", CultureInfo.CurrentCulture) ?? KoffeeResearchDisplay.Unknown) : points;
        }

        internal static string Komfort(KomfortStatus status)
        {
            if (status.Availability == KomfortAvailability.Disabled) return Localizer.Format("#Koffee_Crew_Disabled");
            if (status.Availability == KomfortAvailability.NotApplicable) return Localizer.Format("#Koffee_Crew_NotApplicable");
            if (!status.Value.HasValue) return Localizer.Format("#Koffee_Crew_NoData");
            string percent = (100 * status.Value.Value).ToString("0.#", CultureInfo.CurrentCulture) + " %";
            return status.Availability == KomfortAvailability.LastKnown ?
                Localizer.Format("#Koffee_Crew_LastKnown", percent,
                    status.ObservedUT?.ToString("0.##", CultureInfo.CurrentCulture) ?? KoffeeResearchDisplay.Unknown) : percent;
        }
    }
}

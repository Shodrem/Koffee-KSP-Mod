using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    internal static class EngineeringDisplay
    {
        internal static string Effect(Vessel vessel, string name, EngineeringAffinity affinity)
        {
            if (string.IsNullOrEmpty(name)) return Localizer.Format("#Koffee_Engineering_NoEngineer");
            if (!KerbalSpecialtyScenario.TryGetEngineerProfile(name, out var profile)) return name + " — " + Localizer.Format("#Koffee_Engineering_NoProfile");
            double ut = Planetarium.GetUniversalTime();
            double saving = EngineeringContribution.Savings(vessel, name, affinity, ut, true);
            return Localizer.Format("#Koffee_Engineering_Effect", name, Affinity(affinity), profile.Score(affinity).ToString(CultureInfo.InvariantCulture),
                (saving * 100).ToString("0.##", CultureInfo.CurrentCulture),
                Localizer.Format(EngineeringContribution.CoffeeAt(vessel, name, ut) ? "#Koffee_UI_Kaffeinated" : "#Koffee_Engineering_WithoutCoffee"),
                (KomfortRuntime.ProjectedFactor(vessel, ut) * 100).ToString("0.#", CultureInfo.CurrentCulture));
        }
        internal static string Power(double nominal, double factor) => Localizer.Format("#Koffee_Engineering_Power",
            nominal.ToString("0.####", CultureInfo.CurrentCulture), (nominal * factor).ToString("0.####", CultureInfo.CurrentCulture));

        internal static string Management(Vessel vessel)
        {
            var manager = ExperimentManagerService.Select(vessel, Planetarium.GetUniversalTime(), false, out _, true);
            return Effect(vessel, manager?.EngineerName ?? "", EngineeringAffinity.Systems);
        }
        internal static string Affinity(EngineeringAffinity value) => Localizer.Format("#Koffee_Engineering_" + value);
        internal static IEnumerable<EngineeringAffinity> Ordered(EngineerAffinityProfile profile) =>
            EngineeringAffinities.All.OrderByDescending(profile.Score).ThenBy(a => a);
        internal static string Tooltip(string name)
        {
            if (!KerbalSpecialtyScenario.TryGetEngineerProfile(name, out var profile))
                return Localizer.Format("#Koffee_Engineering_NoProfile");
            var best = Ordered(profile).Take(2).ToArray();
            return Localizer.Format("#Koffee_Research_TooltipPrimary", Affinity(best[0]), profile.Score(best[0]).ToString(CultureInfo.InvariantCulture)) + "\n" +
                Localizer.Format("#Koffee_Research_TooltipSecondary", Affinity(best[1]), profile.Score(best[1]).ToString(CultureInfo.InvariantCulture));
        }
        internal static IEnumerable<string> EditorPotential(string name)
        {
            if (!KerbalSpecialtyScenario.TryGetEngineerProfile(name, out var profile)) yield break;
            var settings = EngineeringSettingsLoader.Current;
            yield return Localizer.Format("#Koffee_EditorCrew_PotentialHelp");
            foreach (var affinity in Ordered(profile))
            {
                bool maintenance = affinity == EngineeringAffinity.Maintenance;
                double baseValue = settings.Contribution(profile.Score(affinity), false, 1, maintenance);
                double extra = settings.Contribution(profile.Score(affinity), true, 1, maintenance) - baseValue;
                yield return Localizer.Format(maintenance ? "#Koffee_EditorCrew_PotentialHours" : "#Koffee_EditorCrew_PotentialPower",
                    Affinity(affinity), ((maintenance ? settings.MaintenanceHours : 100) * baseValue).ToString("0.##", CultureInfo.CurrentCulture),
                    ((maintenance ? settings.MaintenanceHours : 100) * extra).ToString("0.##", CultureInfo.CurrentCulture));
            }
        }
        internal static IEnumerable<string> Profile(string name)
        {
            if (!KerbalSpecialtyScenario.TryGetEngineerProfile(name, out var profile))
            {
                yield return Localizer.Format("#Koffee_Engineering_NoProfile");
                yield break;
            }
            foreach (var affinity in Ordered(profile))
                yield return Localizer.Format("#Koffee_Engineering_Score", Affinity(affinity),
                    profile.Score(affinity).ToString(CultureInfo.InvariantCulture),
                    Localizer.Format(affinity == profile.PrimaryCategory ? "#Koffee_Engineering_Primary" :
                        affinity == profile.SecondaryCategory ? "#Koffee_Engineering_Secondary" : "#Koffee_Engineering_Low"));
        }
    }
}

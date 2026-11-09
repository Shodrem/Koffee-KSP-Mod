using System;
using System.Linq;
using System.Collections.Generic;

namespace ShoAerospace.Koffee
{
    internal static class EngineeringContribution
    {
        internal static double Savings(Vessel vessel, string name, EngineeringAffinity affinity, double ut, bool forecast = false)
        {
            if (!KerbalSpecialtyScenario.TryGetEngineerProfile(name, out var profile)) return 0;
            bool coffee = CoffeeAt(vessel, name, ut);
            double komfort = forecast ? KomfortRuntime.QuotedFactor(vessel, ut) : KomfortRuntime.At(vessel, ut);
            return EngineeringSettingsLoader.Current.Contribution(profile.Score(affinity), coffee, komfort, affinity == EngineeringAffinity.Maintenance);
        }
        internal static double AverageSavings(Vessel vessel, string name, EngineeringAffinity affinity, double from, double to)
        {
            if (!KerbalSpecialtyScenario.TryGetEngineerProfile(name, out var profile)) return 0;
            var settings = EngineeringSettingsLoader.Current;
            if (!settings.Enabled) return 0;
            bool maintenance = affinity == EngineeringAffinity.Maintenance;
            double coefficient = profile.Score(affinity) * (maintenance ? settings.MaintenancePerPoint : settings.PerPoint) +
                (CoffeeAt(vessel, name, from) ? (maintenance ? settings.MaintenanceCaffeinatedExtra : settings.CaffeinatedExtra) : 0);
            if (coefficient <= 0 || to <= from) return 0;
            return coefficient * KomfortRuntime.Integral(vessel, from, to, .25, Math.Min(1, settings.MaximumSaving / coefficient)) / (to - from);
        }
        internal static bool CoffeeAt(Vessel vessel, string name, double ut)
        {
            var crew = vessel.loaded ? vessel.GetVesselCrew().FirstOrDefault(c => c.name == name) : null;
            if (crew != null) return EngineeringRenewalCoordinator.CaptureWindows(crew).Any(w => w.Contains(ut));
            return KerbalKoffeeScenario.Store.TryGetStart(name, out double start) &&
                new KaffeinatedWindow(start, start + KerbalWorkState.KaffeinatedDuration).Contains(ut);
        }
        internal static double ManagementFactor(Vessel vessel, string name, double ut, bool forecast = false) =>
            1 - Savings(vessel, name, EngineeringAffinity.Systems, ut, forecast);

        internal static double NextBoundary(Vessel vessel, string name, double ut)
        {
            double next = KomfortRuntime.NextBoundary(vessel, ut);
            var crew = vessel.loaded ? vessel.GetVesselCrew().FirstOrDefault(c => c.name == name) : null;
            if (crew != null)
                foreach (var window in EngineeringRenewalCoordinator.CaptureWindows(crew))
                {
                    if (window.StartUT > ut) next = Math.Min(next, window.StartUT);
                    if (window.EndUT > ut) next = Math.Min(next, window.EndUT);
                }
            else if (KerbalKoffeeScenario.Store.TryGetStart(name, out double start))
            {
                if (start > ut) next = Math.Min(next, start);
                if (start + KerbalWorkState.KaffeinatedDuration > ut) next = Math.Min(next, start + KerbalWorkState.KaffeinatedDuration);
            }
            return next;
        }
        internal static bool MayBuyOptional(Vessel vessel, Part consumer, double ut)
        {
            return KoffeeSources.Available(consumer) >= (RequiredScientistCups(vessel, ut) + 1) * KoffeeSettingsLoader.Current.ActivationCost;
        }
        internal static int RequiredScientistCups(Vessel vessel, double ut)
        {
            IEnumerable<string> names;
            if (vessel.loaded)
                names = vessel.parts.SelectMany(p => p.Modules.OfType<ModuleKoffeeExperiment>())
                    .Where(e => e.State?.Status == ExperimentContainerStatus.Running && !e.State.WorkComplete() && !e.manuallyPaused)
                    .Select(e => e.State!.AssignedKerbalName);
            else
                names = (vessel.protoVessel?.protoPartSnapshots ?? new List<ProtoPartSnapshot>()).SelectMany(p => p.modules)
                    .Where(m => m.moduleName == nameof(ModuleKoffeeExperiment) &&
                        m.moduleValues.GetValue(KoffeeIdentifiers.ContainerStatusValue) == "Running" &&
                        !string.Equals(m.moduleValues.GetValue("manuallyPaused"), "True", StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.moduleValues.GetValue("assignedKerbalName") ?? "");
            return names.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).Count(n => !CoffeeAt(vessel, n, ut));
        }
        internal static bool OptionalUseful(Vessel vessel, string name, double ut) =>
            KerbalSpecialtyScenario.TryGetEngineerProfile(name, out var profile) &&
            EngineeringSettingsLoader.Current.Contribution(profile.Score(EngineeringAffinity.Systems), true,
                KomfortRuntime.ProjectedFactor(vessel, ut)) >
            EngineeringSettingsLoader.Current.Contribution(profile.Score(EngineeringAffinity.Systems), false,
                KomfortRuntime.ProjectedFactor(vessel, ut));
    }
}

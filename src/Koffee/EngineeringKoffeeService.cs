using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class EngineeringKoffeeService
    {
        private static bool errorLogged;

        internal static EngineeringProcessingLease? Begin(double deltaTime, Part resPart,
            PartModule resModule, ref float efficiencyBonus)
        {
            EngineeringProcessingLease? processing = null;
            try
            {
                EngineeringKoffeeSettings settings = EngineeringSettingsLoader.Current;
                if (!settings.Enabled || !GeneralFilter(deltaTime, resPart, resModule,
                    efficiencyBonus, out BaseConverter? converter)) return null;
                BaseConverter activeConverter = converter!;
                if (!EngineeringModuleFilter.Matches(activeConverter.GetType(),
                    resPart.partInfo.name, activeConverter.UseSpecialistBonus,
                    activeConverter.ExperienceEffect)) return null;

                IReadOnlyList<EngineeringCandidate<ProtoCrewMember>> candidates =
                    EngineeringWorkAssignment.Relevant(activeConverter);
                if (candidates.Count == 0 || !AnyConfiguredBonus(candidates, settings)) return null;

                double endUT = ConverterTiming.UpdateEndUT(activeConverter);
                double now = Planetarium.GetUniversalTime();
                if (!EngineeringMath.Finite(endUT) || endUT <= 0 || endUT > now + 1) endUT = now;
                double startUT = endUT - deltaTime;
                if (!EngineeringMath.Finite(startUT) || endUT <= startUT) return null;

                double accessibleCups = KoffeeSources.Available(resPart);
                double stockEfficiency = efficiencyBonus;
                processing = new EngineeringProcessingLease();
                double weightedBonus = KaffeinatedCoveragePlanner.WeightedBonus(candidates,
                    settings.Selection, startUT, endUT,
                    EngineeringRenewalCoordinator.Windows, settings.BonusForLevel,
                    (kerbal, activationUT) => EngineeringRenewalCoordinator.TryReserve(
                        kerbal, resPart, activationUT, processing),
                    (kerbal, window) => EngineeringRenewalCoordinator.Observe(
                        kerbal, window, processing));
                double adjusted = EngineeringMath.Apply(efficiencyBonus, weightedBonus);
                if (!EngineeringMath.Finite(adjusted) || adjusted <= 0)
                {
                    processing.Cancel();
                    return null;
                }
                efficiencyBonus = (float)adjusted;
                if (!EngineeringMath.Finite(efficiencyBonus) || efficiencyBonus <= 0)
                {
                    processing.Cancel();
                    return null;
                }
                EngineeringCandidate<ProtoCrewMember> primary = candidates[0];
                processing.SetDiagnostic(new EngineeringDiagnosticSample(activeConverter,
                    resPart.partInfo.name, resPart.flightID, primary.Worker.name ?? "",
                    primary.Level, startUT, endUT, accessibleCups, stockEfficiency,
                    weightedBonus, efficiencyBonus));
                return processing;
            }
            catch (Exception error)
            {
                processing?.Cancel();
                if (!errorLogged)
                {
                    errorLogged = true;
                    Debug.LogError("[Koffee] Engineering bonus failed; stock processing preserved: " + error);
                }
                return null;
            }
        }

        internal static void Clear()
        {
            errorLogged = false;
            EngineeringRenewalCoordinator.Clear();
            EngineeringDiagnostics.Clear();
        }

        private static bool GeneralFilter(double deltaTime, Part resPart, PartModule resModule,
            float efficiencyBonus, out BaseConverter? converter)
        {
            converter = resModule as BaseConverter;
            return HighLogic.LoadedSceneIsFlight && resPart != null && resModule != null &&
                converter != null && ReferenceEquals(resModule.part, resPart) &&
                resPart.partInfo != null && resPart.State != PartStates.DEAD &&
                resPart.vessel != null && resPart.vessel.loaded && converter.IsActivated &&
                deltaTime > 0 && EngineeringMath.Finite(deltaTime) && efficiencyBonus > 0 &&
                EngineeringMath.Finite(efficiencyBonus);
        }

        private static bool AnyConfiguredBonus(
            IReadOnlyList<EngineeringCandidate<ProtoCrewMember>> candidates,
            EngineeringKoffeeSettings settings)
        {
            for (int i = 0; i < candidates.Count; i++)
                if (settings.BonusForLevel(candidates[i].Level) > 0) return true;
            return false;
        }
    }

    internal static class EngineeringModuleFilter
    {
        internal static bool Matches(Type exactType, string partName,
            bool useSpecialistBonus, string experienceEffect)
        {
            if (!useSpecialistBonus || string.IsNullOrEmpty(partName) ||
                !TryFamily(exactType, out EngineeringModuleFamily family)) return false;
            return EngineeringTargetLoader.Current.Matches(family, partName, experienceEffect);
        }

        internal static bool TryFamily(Type exactType, out EngineeringModuleFamily family)
        {
            if (exactType == typeof(ModuleResourceHarvester))
            {
                family = EngineeringModuleFamily.ResourceHarvester;
                return true;
            }
            if (exactType == typeof(ModuleResourceConverter))
            {
                family = EngineeringModuleFamily.ResourceConverter;
                return true;
            }
            family = default;
            return false;
        }
    }
}

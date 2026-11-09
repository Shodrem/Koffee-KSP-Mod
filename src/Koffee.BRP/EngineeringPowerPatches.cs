using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing;
using BackgroundResourceProcessing.Behaviour;
using HarmonyLib;
using BrpConverter = BackgroundResourceProcessing.Core.ResourceConverter;

namespace ShoAerospace.Koffee.Brp
{
    // Patch the checked native recipe providers, not the solver or efficiency.
    // ConstantConverter returns its own lists: always replace Inputs with a copy.
    internal static class EngineeringPowerPatches
    {
        private static Harmony? harmony;
        private sealed class DiagnosticRecipe
        {
            internal Game? Game;
            internal string Engineer = "";
            internal EngineeringAffinity Affinity;
            internal double UT, Nominal, Adjusted, Saving, Komfort;
            internal int Score, Level = -1;
            internal bool Coffee;
        }
        private static readonly Dictionary<ConverterBehaviour, DiagnosticRecipe> diagnosticRecipes = new Dictionary<ConverterBehaviour, DiagnosticRecipe>();
        internal static void ClearDiagnostics() => diagnosticRecipes.Clear();
        internal static void Install()
        {
            if (harmony != null) return;
            harmony = new Harmony("ShoAerospace.Koffee.Brp.Engineering");
            foreach (var type in new[] { typeof(ConstantConverter), typeof(ResourceRelativeBehaviour) })
                harmony.Patch(AccessTools.DeclaredMethod(type, "GetResources"),
                    postfix: new HarmonyMethod(typeof(EngineeringPowerPatches), nameof(Adjust)));
            foreach (var type in new[] { typeof(ConverterBehaviour), typeof(ResourceRelativeBehaviour) })
                harmony.Patch(AccessTools.DeclaredMethod(type, "OnRatesComputed"),
                    postfix: new HarmonyMethod(typeof(EngineeringPowerPatches), nameof(Boundary)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Vessel), "Initialize", new[] { typeof(bool), typeof(bool), typeof(bool), typeof(bool) }),
                postfix: new HarmonyMethod(typeof(BrpPowerProvider), nameof(BrpPowerProvider.AfterInitialize)));
        }
        private static void Adjust(ConverterBehaviour __instance, VesselState state, ref ConverterResources __result)
        {
            var vessel = __instance.Vessel;
            if (__result.Inputs == null || vessel == null) return;
            bool selected = TryWorker(__instance, state.CurrentTime, out var name, out var affinity);
            double saving = selected ? EngineeringContribution.Savings(vessel, name, affinity, state.CurrentTime, true) : 0;
            if (EngineeringPowerDiagnostics.Enabled &&
                (__instance.SourceModule == nameof(ModuleResourceConverter) || __instance.SourceModule == nameof(ModuleResourceHarvester) || __instance.SourceModule == nameof(ModuleScienceConverter)))
            {
                if (diagnosticRecipes.Count >= 2048) diagnosticRecipes.Clear();
                var crew = vessel.protoVessel?.protoPartSnapshots?.SelectMany(p => p.protoModuleCrew).FirstOrDefault(c => c.name == name);
                int score = KerbalSpecialtyScenario.TryGetEngineerProfile(name, out var profile) ? profile.Score(affinity) : 0;
                double nominal = __result.Inputs.Where(r => r.ResourceName == "ElectricCharge").Sum(r => r.Ratio);
                diagnosticRecipes[__instance] = new DiagnosticRecipe { Game = HighLogic.CurrentGame, Engineer = name, Affinity = affinity,
                    UT = state.CurrentTime, Nominal = nominal, Adjusted = nominal * (1 - saving), Saving = saving, Score = score, Level = crew?.experienceLevel ?? -1,
                    Coffee = selected && EngineeringContribution.CoffeeAt(vessel, name, state.CurrentTime),
                    Komfort = selected ? KomfortRuntime.ProjectedFactor(vessel, state.CurrentTime) : 1 };
            }
            if (!selected) return;
            __result.NextChangepoint = Math.Min(__result.NextChangepoint, KomfortRuntime.PriceBoundary(vessel, state.CurrentTime));
            __result.Inputs = __result.Inputs.Select(r => r.ResourceName == "ElectricCharge" ?
                new ResourceRatio(r.ResourceName, r.Ratio * (1 - saving), r.DumpExcess, r.FlowMode) : r).ToList();
            __result.NextChangepoint = Math.Min(__result.NextChangepoint,
                EngineeringContribution.NextBoundary(vessel, name, state.CurrentTime));
        }
        private static void Boundary(ConverterBehaviour __instance, BackgroundResourceProcessor processor, BrpConverter converter, ConverterBehaviour.RateCalculatedEvent evt)
        {
            if (!((__instance.GetType() == typeof(ConstantConverter) &&
                (__instance.SourceModule == nameof(ModuleResourceHarvester) || __instance.SourceModule == nameof(ModuleResourceConverter))) ||
                (__instance.GetType() == typeof(ResourceRelativeBehaviour) && __instance.SourceModule == nameof(ModuleScienceConverter)))) return;
            var service = processor.Converters.FirstOrDefault(c => c.Behaviour is KomfortPowerBehaviour);
            if (service != null) KomfortRuntime.Forecast(__instance.Vessel, service.Rate);
            // The first record refresh precedes insertion into Converters. At
            // solver callbacks the real FlightId is available. Reprice at the
            // current boundary before any interval can run with stale EC ratios.
            var expected = __instance.GetResources(processor.GetVesselState(evt.CurrentTime));
            double actual = converter.Inputs.Where(r => r.Value.ResourceName == "ElectricCharge").Sum(r => r.Value.Ratio);
            double desired = expected.Inputs?.Where(r => r.ResourceName == "ElectricCharge").Sum(r => r.Ratio) ?? 0;
            if (Math.Abs(actual - desired) > 1e-9)
            {
                converter.NextChangepoint = evt.CurrentTime;
                processor.MarkDirty(); processor.SuppressNoProgressError(); return;
            }
            if (expected.NextChangepoint > evt.CurrentTime)
                converter.NextChangepoint = Math.Min(converter.NextChangepoint, expected.NextChangepoint);
            if (TryWorker(__instance, evt.CurrentTime, out var name, out _))
                converter.NextChangepoint = Math.Min(converter.NextChangepoint,
                    EngineeringContribution.NextBoundary(__instance.Vessel, name, evt.CurrentTime));
        }
        private static bool TryWorker(ConverterBehaviour behaviour, double ut, out string name, out EngineeringAffinity affinity)
        {
            name = ""; affinity = EngineeringAffinity.Systems;
            var vessel = behaviour.Vessel;
            if (vessel?.protoVessel?.protoPartSnapshots == null) return false;
            var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
            var native = processor?.Converters.FirstOrDefault(c => ReferenceEquals(c.Behaviour, behaviour));
            var part = vessel.protoVessel.protoPartSnapshots.FirstOrDefault(p => p.flightID == native?.FlightId);
            var prefab = part?.partPrefab ?? part?.partInfo?.partPrefab;
            if (behaviour.GetType() == typeof(ResourceRelativeBehaviour) && behaviour.SourceModule == nameof(ModuleScienceConverter))
            {
                if (prefab?.Modules.OfType<ModuleKoffeeLab>().Any() != true ||
                    !prefab.Modules.OfType<ModuleScienceConverter>().Any(m => m.GetType() == typeof(ModuleScienceConverter))) return false;
                var manager = ExperimentManagerService.SelectSnapshots(vessel.protoVessel.protoPartSnapshots,
                    ut, out _, false);
                name = manager?.EngineerName ?? "";
                return !string.IsNullOrEmpty(name);
            }
            if (behaviour.GetType() != typeof(ConstantConverter)) return false;
            EngineeringModuleFamily family;
            string skill;
            if (behaviour.SourceModule == nameof(ModuleResourceHarvester))
            { affinity = EngineeringAffinity.Extraction; family = EngineeringModuleFamily.ResourceHarvester; skill = "DrillSkill"; }
            else if (behaviour.SourceModule == nameof(ModuleResourceConverter))
            { affinity = EngineeringAffinity.Refining; family = EngineeringModuleFamily.ResourceConverter; skill = "ConverterSkill"; }
            else return false;
            // SourcePart is Unity Object.name, not the canonical part ID (real
            // RecordPartModuleConverters IL). Use the linked physical snapshot.
            string partName = part?.partInfo?.name ?? prefab?.partInfo?.name ?? "";
            if (!EngineeringTargetLoader.Current.Matches(family, partName, skill)) return false;
            var expected = family == EngineeringModuleFamily.ResourceHarvester ? typeof(ModuleResourceHarvester) : typeof(ModuleResourceConverter);
            if (prefab?.Modules.OfType<BaseConverter>().Any(m => m.GetType() == expected &&
                m.UseSpecialistBonus && m.ExperienceEffect == skill) != true) return false;
            var candidates = EngineeringWorkAssignment.FromCrew(vessel.protoVessel.protoPartSnapshots
                .SelectMany(p => p.protoModuleCrew), skill, affinity);
            name = candidates.FirstOrDefault().Name ?? "";
            return name != "";
        }
        internal static void Publish(BackgroundResourceProcessor processor, ChangepointEvent change)
        {
            if (processor.Vessel.loaded || change.CurrentChangepoint <= change.LastChangepoint) return;
            var session = new StationKoffeeSession();
            if (EngineeringPowerDiagnostics.Enabled)
                foreach (var consumer in processor.Converters)
                    if (diagnosticRecipes.TryGetValue(consumer.Behaviour, out var reading) && ReferenceEquals(reading.Game, HighLogic.CurrentGame))
                        EngineeringPowerDiagnostics.Solver(consumer, reading.Engineer + "/" + reading.Saving.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "/" + consumer.Rate,
                            () => string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "route=BRP UT=[{0:R},{1:R}] vessel={2} part={3} module={4}/{5} consumer={6} loaded={7} packed={8} warp={9}/{10:R} activity={11} engineer={12} score={13} stockLevel={14} coffee={15} komfort={16:R} saving={17:R} recipeAtUT={18:R} nominalCoeff={19:R}EC/s adjustedCoeff={20:R}EC/s solverRateNow={21:R} activeTimeCumulative={22:R}s observed=solver-recipe-and-rate physicalRequested=unobserved physicalReceived=unobserved (no per-consumer withdrawal or historical rate inferred)",
                                change.LastChangepoint, change.CurrentChangepoint, processor.Vessel.id, consumer.FlightId, consumer.Behaviour.SourceModule,
                                consumer.ModuleId, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(consumer), processor.Vessel.loaded, processor.Vessel.packed,
                                TimeWarp.WarpMode, TimeWarp.CurrentRate, reading.Affinity, reading.Engineer, reading.Score, reading.Level, reading.Coffee,
                                reading.Komfort, reading.Saving, reading.UT, reading.Nominal, reading.Adjusted, consumer.Rate, consumer.ActiveTime));
            foreach (var converter in processor.Converters)
            {
                if (!(converter.ActiveTime > 0) || !(converter.Rate > 0) ||
                    !converter.Inputs.Any(r => r.Value.ResourceName == "ElectricCharge" && r.Value.Ratio > 0) ||
                    !TryWorker(converter.Behaviour, change.CurrentChangepoint, out string name, out var affinity)) continue;
                double ut = change.CurrentChangepoint;
                if (EngineeringContribution.CoffeeAt(processor.Vessel, name, ut) ||
                    !KerbalSpecialtyScenario.TryGetEngineerProfile(name, out var profile)) continue;
                double komfort = KomfortRuntime.ProjectedFactor(processor.Vessel, ut);
                var settings = EngineeringSettingsLoader.Current;
                if (settings.Contribution(profile.Score(affinity), true, komfort) <= settings.Contribution(profile.Score(affinity), false, komfort)) continue;
                if (ResourcePayment.Available(BrpPowerProvider.Accounts(processor)) <
                    (EngineeringContribution.RequiredScientistCups(processor.Vessel, ut) + 1) * KoffeeSettingsLoader.Current.ActivationCost) continue;
                using (var renewal = session.Begin(name, BrpPowerProvider.Accounts(processor)))
                    if (renewal.Ensure(ut))
                    { renewal.Commit(); converter.NextChangepoint = ut; processor.MarkDirty(); }
            }
        }
    }
}

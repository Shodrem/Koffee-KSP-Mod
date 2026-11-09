using HarmonyLib;

namespace ShoAerospace.Koffee
{
    internal static class EngineeringPatches
    {
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ResourceConverter), "ProcessRecipe"),
                prefix: new HarmonyMethod(typeof(EngineeringPatches), nameof(Process)),
                postfix: new HarmonyMethod(typeof(EngineeringPatches), nameof(AfterProcess)),
                finalizer: new HarmonyMethod(typeof(EngineeringPatches), nameof(FinishProcess)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Part), "RequestResource", new[] { typeof(int), typeof(double), typeof(ResourceFlowMode) }),
                prefix: new HarmonyMethod(typeof(EngineeringPowerDiagnostics), nameof(EngineeringPowerDiagnostics.BeforeDebit)),
                postfix: new HarmonyMethod(typeof(EngineeringPowerDiagnostics), nameof(EngineeringPowerDiagnostics.AfterDebit)),
                finalizer: new HarmonyMethod(typeof(EngineeringPowerDiagnostics), nameof(EngineeringPowerDiagnostics.FinishDebit)));
        }

        private static bool Process(ResourceConverter __instance, double deltaTime, ConversionRecipe recipe,
            Part resPart, PartModule resModule, float efficiencyBonus, ref ConverterResults __result, out EngineeringPowerDiagnostics.Sample? __state)
        {
            __state = null;
            if (EngineeringKoffeeService.IsProcessing(resModule)) return true;
            if (!EngineeringKoffeeService.TryProcess(__instance, deltaTime, recipe, resPart, resModule, efficiencyBonus, out var result, out string reason))
            {
                if (HighLogic.LoadedSceneIsFlight && resModule is BaseConverter converter && resPart?.vessel?.loaded == true &&
                    EngineeringModuleFilter.Matches(converter.GetType(), resPart.partInfo?.name ?? "", converter.UseSpecialistBonus, converter.ExperienceEffect))
                {
                    EngineeringKoffeeService.ForgetMonitor(converter);
                    double end = Planetarium.GetUniversalTime();
                    __state = EngineeringPowerDiagnostics.Begin(recipe, recipe, resPart, resModule, end - deltaTime, end, efficiencyBonus, reason,
                        affinity: converter.GetType() == typeof(ModuleResourceHarvester) ? EngineeringAffinity.Extraction : EngineeringAffinity.Refining);
                }
                return true;
            }
            __result = result; return false;
        }
        private static void AfterProcess(ConverterResults __result, EngineeringPowerDiagnostics.Sample? __state) => __state?.Complete(__result.TimeFactor);
        private static System.Exception? FinishProcess(System.Exception? __exception, EngineeringPowerDiagnostics.Sample? __state)
        { __state?.Dispose(); return __exception; }
    }
}

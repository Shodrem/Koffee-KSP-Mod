using System;
using HarmonyLib;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class EngineeringPatches
    {
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ResourceConverter), "ProcessRecipe"),
                prefix: new HarmonyMethod(typeof(EngineeringPatches), nameof(Begin)),
                postfix: new HarmonyMethod(typeof(EngineeringPatches), nameof(Observe)),
                finalizer: new HarmonyMethod(typeof(EngineeringPatches), nameof(Finish)));
            Debug.Log("[Koffee] Engineering hooks installed: ResourceConverter.ProcessRecipe.");
        }

        private static void Begin(double deltaTime, Part resPart, PartModule resModule,
            ref float efficiencyBonus, out EngineeringProcessingLease? __state) =>
            __state = EngineeringKoffeeService.Begin(deltaTime, resPart, resModule,
                ref efficiencyBonus);

        private static void Observe(ConverterResults __result,
            EngineeringProcessingLease? __state)
        {
            __state?.Observe(__result.TimeFactor);
        }

        private static Exception? Finish(Exception? __exception,
            EngineeringProcessingLease? __state)
        {
            __state?.Complete(__exception);
            return __exception;
        }
    }
}

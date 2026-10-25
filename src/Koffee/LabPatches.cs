using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class LabPatches
    {
        private static readonly MethodInfo GetScientists = AccessTools.DeclaredMethod(
            typeof(ModuleScienceConverter), "GetScientists");
        private static readonly FieldInfo LastUpdateTime = AccessTools.Field(
            typeof(BaseConverter), "lastUpdateTime");

        internal static double UpdateEndUT(ModuleScienceConverter converter) =>
            (double)LastUpdateTime.GetValue(converter);

        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.DeclaredMethod(typeof(BaseConverter), "FixedUpdate"),
                prefix: new HarmonyMethod(typeof(LabPatches), nameof(BeginUpdate)),
                finalizer: new HarmonyMethod(typeof(LabPatches), nameof(FinishUpdate)));
            harmony.Patch(GetScientists,
                prefix: new HarmonyMethod(typeof(LabPatches), nameof(ActiveScientists)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ResourceConverter), "ProcessRecipe"),
                prefix: new HarmonyMethod(typeof(LabPatches), nameof(BeginRecipe)),
                postfix: new HarmonyMethod(typeof(LabPatches), nameof(ReportPower)),
                transpiler: new HarmonyMethod(typeof(LabPatches), nameof(AdjustRecipe)));
            Debug.Log("[Koffee] Lab hooks installed: ResourceConverter.ProcessRecipe / BaseConverter.FixedUpdate.");
        }

        private static ModuleKoffeeLab? Find(PartModule module) =>
            LabIntegration.FindOptedInLab(module);

        private static void BeginRecipe(PartModule resModule, ref ConversionRecipe recipe) =>
            recipe = StationLaboratoryPower.Recipe(recipe, resModule);

        private static void ReportPower(PartModule resModule, ref ConverterResults __result)
        {
            if (Find(resModule)?.PowerBlocked == true)
                __result.Status = KSP.Localization.Localizer.Format("#Koffee_Station_Pause_NoElectricity");
        }

        private static void FinishUpdate(BaseConverter __instance) => Find(__instance)?.Finish();
        private static void BeginUpdate(BaseConverter __instance) => Find(__instance)?.BeginUpdate();
        private static bool ActiveScientists(ModuleScienceConverter __instance, ref float __result)
        {
            ModuleKoffeeLab? lab = Find(__instance);
            if (lab == null) return true;
            __result = lab.ScientistContribution(__instance.scientistBonus);
            return false;
        }

        private static double Adjust(double interval, double efficiency, double delta, PartModule module)
        {
            ModuleKoffeeLab? lab = Find(module);
            if (lab == null) return interval;
            try { return lab.Reserve((ModuleScienceConverter)module, interval, efficiency, delta); }
            catch (Exception error)
            {
                lab.Finish();
                lab.LogError("Lab Koffee bonus failed; stock processing preserved: " + error);
                return StationLaboratoryPower.Enabled(module) ? 0 : interval;
            }
        }

        private static IEnumerable<CodeInstruction> AdjustRecipe(IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            var code = new List<CodeInstruction>(instructions);
            var locals = __originalMethod.GetMethodBody().LocalVariables;
            // Verified locals: 1 = requirements * efficiency, 3 = resource-limited seconds.
            if (locals.Count != 29 || locals[1].LocalType != typeof(double) ||
                locals[3].LocalType != typeof(double)) throw new InvalidOperationException("Unknown ProcessRecipe locals.");
            MethodInfo inputs = AccessTools.PropertyGetter(typeof(ConversionRecipe), "Inputs");
            MethodInfo count = AccessTools.PropertyGetter(typeof(List<ResourceRatio>), "Count");
            int insertion = -1;
            int matches = 0;
            for (int i = 1; i + 6 < code.Count; i++)
            {
                // The second Inputs.Count is the debit loop, after every stock constraint.
                if (!code[i].Calls(inputs) || !code[i + 3].Calls(count)) continue;
                matches++;
                if (matches == 2) insertion = i - 1;
            }
            int deltaLoad = code.FindIndex(c => c.opcode == OpCodes.Stloc_3) - 1;
            int moduleLoad = code.FindIndex(c => c.opcode == OpCodes.Ldsfld &&
                c.operand is FieldInfo field && field.Name == "OnResourceConverterOutput") + 1;
            if (matches != 2 || insertion < 0 || deltaLoad < 0 || moduleLoad <= 0 ||
                !code[insertion].IsLdarg() || !code[deltaLoad].IsLdarg() || !code[moduleLoad].IsLdarg())
                throw new InvalidOperationException("Unknown ProcessRecipe input debit boundary.");
            // Harmony may insert a hidden return-buffer argument for ConverterResults on
            // CLR (but not Mono). Copy its actual argument loads instead of assuming slots.
            var injected = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldloc_3),
                new CodeInstruction(OpCodes.Ldloc_1),
                new CodeInstruction(code[deltaLoad].opcode, code[deltaLoad].operand),
                new CodeInstruction(code[moduleLoad].opcode, code[moduleLoad].operand),
                new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(LabPatches), nameof(Adjust))),
                new CodeInstruction(OpCodes.Stloc_3)
            };
            injected[0].labels.AddRange(code[insertion].labels);
            code[insertion].labels.Clear();
            code.InsertRange(insertion, injected);
            return code;
        }
    }
}

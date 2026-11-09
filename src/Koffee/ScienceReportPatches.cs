using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ShoAerospace.Koffee
{
    internal static class ScienceReportPatches
    {
        internal static void Install(Harmony harmony)
        {
            Patch(harmony, typeof(ModuleScienceExperiment), "sendDataToLab", nameof(ExperimentLab));
            Patch(harmony, typeof(ModuleScienceContainer), "sendDataToLab", nameof(ContainerLab));
            Patch(harmony, typeof(ModuleScienceContainer), "StoreData", nameof(Store));
            Patch(harmony, typeof(ModuleScienceContainer), "AddData", nameof(Add));
            Patch(harmony, typeof(ModuleScienceContainer), "ReturnData", nameof(Return));
            Patch(harmony, typeof(ModuleScienceExperiment), "ReturnData", nameof(ReturnInstrument));
            Patch(harmony, typeof(ModuleScienceLab), "ProcessData", nameof(Process));
            Patch(harmony, typeof(ModuleScienceLab), "StoreData", nameof(StoreLab));
            Patch(harmony, typeof(ModuleScienceLab), "IsStorable", nameof(Storable));
            Patch(harmony, typeof(ExperimentResultDialogPage), "UpdatePageLabValue", nameof(PageValue));
            // Also protect antenna-wide "transmit all" and direct transmitter
            // calls; those need not use a result dialog on the physical part.
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(ModuleDataTransmitter)).Where(m =>
                m.Name == "TransmitData" && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(List<ScienceData>)))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(ScienceReportPatches), nameof(Transmitter)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ModuleDataTransmitter), "queueVesselData"),
                postfix: new HarmonyMethod(typeof(ScienceReportPatches), nameof(Queued)));
            harmony.Patch(AccessTools.Constructor(typeof(ScienceLabSearch), new[] { typeof(Vessel), typeof(ScienceData) }),
                postfix: new HarmonyMethod(typeof(ScienceReportPatches), nameof(Search)));
        }
        private static void Patch(Harmony harmony, Type type, string name, string prefix) => harmony.Patch(
            AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.Name, name),
            prefix: new HarmonyMethod(typeof(ScienceReportPatches), prefix));

        private static bool ExperimentLab(ModuleScienceExperiment __instance, ScienceData __0)
        { SendToLab(__instance, __0); return false; }
        private static bool ContainerLab(ModuleScienceContainer __instance, ScienceData __0)
        { SendToLab(__instance, __0); return false; }
        internal static void SendToLab(IScienceDataContainer source, ScienceData data)
        {
            if (!(source is PartModule module)) return;
            var lab = ScienceLabAcceptance.Destinations(module.vessel, data).FirstOrDefault();
            if (lab == null) ScienceReportUI.Message("LabRejected");
            else ScienceLabAcceptance.Accept(source, data, lab);
        }
        private static bool Store(ModuleScienceContainer __instance, ref List<IScienceDataContainer> __0,
            bool __2, ref bool __result)
        {
            if (ScienceReportTransfer.Mutating) return true;
            var store = __instance.part.FindModuleImplementing<ModuleKoffeeScienceStore>();
            if (store != null)
            {
                var sources = (__0 ?? new List<IScienceDataContainer>()).Where(s => !__2 || !(s is ModuleScienceContainer));
                __result = ScienceReportTransfer.Move(sources, store) > 0;
                return false;
            }
            // Even stock collectors must not extract recovery-only Koffee parts.
            __0 = __0?.Where(s => !(s is ModuleKoffeeExperiment k && ScienceReportPolicy.Physical(k))).ToList()!;
            return true;
        }
        private static bool Add(ModuleScienceContainer __instance, ScienceData __0, ref bool __result)
        {
            if (ScienceReportPolicy.Physical(__0)) { __result = false; return false; }
            var receiver = __instance.part.FindModuleImplementing<ModuleKoffeeScienceStore>();
            if (receiver == null) return true;
            if (!ScienceReportPolicy.CanReceiveInManager(__0)) { __result = false; return false; }
            if (ScienceReportTransfer.Mutating) return true;
            var source = ScienceReportTransfer.Source(__instance.vessel, __0);
            __result = source != null && ScienceReportTransfer.Move(new[] { source }, receiver, __0) > 0;
            return false;
        }
        private static bool Return(ModuleScienceContainer __instance, ScienceData __0)
        {
            if (ScienceReportPolicy.Physical(__0)) return false;
            var receiver = __instance.part.FindModuleImplementing<ModuleKoffeeScienceStore>();
            if (receiver == null) return true;
            if (__0.container == __instance.part.flightID)
            {
                // Stock transmitter return: never charge, never discard an aborted
                // report because a second copy of its subject arrived meanwhile.
                var list = (List<ScienceData>)AccessTools.Field(typeof(ModuleScienceContainer), "dataStore").GetValue(__instance)!;
                if (!list.Any(d => ReferenceEquals(d, __0))) list.Add(__0);
            }
            else
            {
                if (!ScienceReportPolicy.CanReceiveInManager(__0)) return false;
                if (ScienceReportTransfer.Mutating) return true;
                var source = ScienceReportTransfer.Source(__instance.vessel, __0);
                if (source != null) ScienceReportTransfer.Move(new[] { source }, receiver, __0);
            }
            return false;
        }
        private static bool ReturnInstrument(ScienceData __0) => !ScienceReportPolicy.Physical(__0);
        private static void Transmitter(ref List<ScienceData> __0)
        {
            if (__0 != null) __0 = __0.Where(d => !ScienceReportPolicy.Physical(d)).ToList();
        }
        private static void Queued(ref List<ScienceData> __result) => Transmitter(ref __result);
        private static bool Process(ModuleScienceLab __instance, ScienceData __0, Callback<ScienceData> __1, ref IEnumerator __result)
        { __result = ProcessControlled(__instance, __0, __1); return false; }
        private static IEnumerator ProcessControlled(ModuleScienceLab lab, ScienceData data, Callback<ScienceData> callback)
        {
            var source = ScienceReportTransfer.Source(lab.vessel, data);
            if (source != null && ScienceLabAcceptance.Accept(source, data, lab))
            {
                // Notification only, after commit. A failing observer cannot roll
                // back accepted science or turn the same report into a second use.
                try { callback?.Invoke(data); }
                catch (Exception error) { UnityEngine.Debug.LogError("[Koffee] Lab completion observer failed: " + error); }
            }
            yield break;
        }
        private static bool StoreLab(ModuleScienceLab __instance, ScienceData __0)
        {
            var source = ScienceReportTransfer.Source(__instance.vessel, __0);
            if (source != null) ScienceLabAcceptance.Accept(source, __0, __instance);
            return false;
        }
        private static bool Storable(ModuleScienceLab __instance, ScienceData __0, ref bool __result)
        { __result = ScienceLabAcceptance.Preview(__instance, __0, out _, out _, out _); return false; }
        private static bool PageValue(ExperimentResultDialogPage __instance, ref float __result)
        {
            var data = __instance.pageData;
            var vessel = __instance.host?.vessel;
            var lab = vessel == null ? null : ScienceLabAcceptance.Destinations(vessel, data).FirstOrDefault();
            __result = lab != null && ScienceLabAcceptance.Preview(lab, data, out _, out _, out float amount) ? amount : 0;
            data.labValue = __result;
            return false;
        }
        private static void Search(ScienceLabSearch __instance, Vessel __0, ScienceData __1)
        {
            if (__0 == null || !__0.loaded || __1 == null) return;
            __instance.NextLabForData = ScienceLabAcceptance.Destinations(__0, __1).FirstOrDefault();
            if (__instance.NextLabForData != null && ScienceLabAcceptance.Preview(__instance.NextLabForData, __1, out _, out _, out float amount))
                __1.labValue = amount;
        }
    }
}

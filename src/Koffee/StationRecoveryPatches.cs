using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KSP.Localization;
using KSP.UI.Screens;
using UnityEngine;
using Widget = KSP.UI.Screens.SpaceCenter.MissionSummaryDialog.ScienceSubjectWidget;

namespace ShoAerospace.Koffee
{
    internal static class StationRecoveryPatches
    {
        private sealed class Recovery
        {
            internal readonly float Before = ResearchAndDevelopment.Instance?.Science ?? 0;
            internal MissionRecoveryDialog? Dialog;
            internal readonly List<RecoveryBonusReceipt> Bonus = new List<RecoveryBonusReceipt>();
            internal readonly HashSet<ConfigNode> Seen = new HashSet<ConfigNode>();
        }
        private sealed class Grant { internal CurrencyModifierQuery? Query; }
        [ThreadStatic] private static Recovery? recovery;
        [ThreadStatic] private static Grant? grant;
        [ThreadStatic] private static Grant? submission;
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(VesselRecovery), "OnVesselRecovered"),
                prefix: Hook(nameof(Begin)), postfix: Hook(nameof(Finish)), finalizer: Hook(nameof(Cleanup)));
            harmony.Patch(AccessTools.Method(typeof(ResearchAndDevelopment), "onVesselRecoveryProcessing"),
                transpiler: Hook(nameof(RecoveryIL)));
            MethodInfo submit = AccessTools.Method(typeof(ResearchAndDevelopment), "SubmitScienceData",
                new[] { typeof(float), typeof(float), typeof(ScienceSubject), typeof(float), typeof(ProtoVessel), typeof(bool) });
            harmony.Patch(submit, transpiler: Hook(nameof(SubmitIL)));
            harmony.Patch(AccessTools.Method(typeof(ResearchAndDevelopment), "AddScience"), transpiler: Hook(nameof(CurrencyIL)));
        }
        private static HarmonyMethod Hook(string name) => new HarmonyMethod(AccessTools.Method(typeof(StationRecoveryPatches), name));
        private static void Begin(out object? __state)
        { __state = recovery; recovery = new Recovery(); }
        private static Exception? Cleanup(Exception? __exception, object? __state)
        { recovery = __state as Recovery; return __exception; }
        private static void Finish()
        {
            Recovery? context = recovery;
            MissionRecoveryDialog? dialog = context?.Dialog;
            if (context == null || dialog == null) return;
            dialog.scienceEarned = ResearchAndDevelopment.Instance.Science - context.Before;
            dialog.totalScience = ResearchAndDevelopment.Instance.Science;
            foreach (RecoveryBonusReceipt receipt in context.Bonus)
                try
                {
                    StationRecoveryScenario? scenario = StationRecoveryScenario.Current;
                    if (scenario != null) { scenario.Attach(receipt, dialog, receipt.Paid); scenario.Present(receipt); }
                    else if (receipt.Paid && receipt.Amount > 0) AddBonusWidget(dialog, receipt);
                }
                catch (Exception e) { Debug.LogError("[Koffee] Recovery bonus widget failed: " + e); }
        }
        internal static void AddBonusWidget(MissionRecoveryDialog dialog, RecoveryBonusReceipt receipt)
        {
            string id = "KoffeeRecoveryBonus:" + receipt.Key;
            var widgets = AccessTools.Field(typeof(MissionRecoveryDialog), "scienceWidgets").GetValue(dialog) as IEnumerable<Widget>;
            if (widgets != null && widgets.Any(w => w != null && w.subject?.id == id)) return;
            var subject = new ScienceSubject(id,
                Localizer.Format("#Koffee_Recovery_Bonus", receipt.Title), 1, 1, receipt.Amount);
            dialog.AddDataWidget(Widget.Create(subject, 0, receipt.Amount, dialog));
        }
        // Bind to the exact local variables of the inspected KSP 1.12.5 method,
        // and refuse installation if its signature/layout has changed.
        private static IEnumerable<CodeInstruction> RecoveryIL(IEnumerable<CodeInstruction> input, MethodBase __originalMethod)
        {
            var locals = __originalMethod.GetMethodBody()!.LocalVariables;
            if (locals.Count < 9 || locals[2].LocalType != typeof(ProtoPartSnapshot) || locals[8].LocalType != typeof(ConfigNode))
                throw new InvalidOperationException("Unknown stock recovery locals.");
            var code = input.ToList(); int count = 0;
            for (int i = 0; i < code.Count; i++)
                if (code[i].operand is MethodInfo m && m.DeclaringType == typeof(ResearchAndDevelopment) &&
                    m.Name == "SubmitScienceData" && m.GetParameters().Length == 6)
                {
                    var first = new CodeInstruction(OpCodes.Ldloc_2); first.labels.AddRange(code[i].labels); code[i].labels.Clear();
                    code.InsertRange(i, new[] { first, new CodeInstruction(OpCodes.Ldloc_S, (byte)8), new CodeInstruction(OpCodes.Ldarg_2) });
                    i += 3; code[i] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(StationRecoveryPatches), nameof(Submit)));
                    count++;
                }
            if (count != 1) throw new InvalidOperationException("Unknown stock recovery submission path.");
            return code;
        }
        private static IEnumerable<CodeInstruction> SubmitIL(IEnumerable<CodeInstruction> input)
            => Replace(input, typeof(ResearchAndDevelopment), "AddScience", nameof(StockGrant));
        private static IEnumerable<CodeInstruction> CurrencyIL(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList(); int count = 0;
            // Capture the original query before any strategy listeners can grant unrelated
            // currencies recursively. Its reference receives the final strategy deltas.
            for (int i = 1; i < code.Count; i++)
                if (code[i].operand is MethodInfo m && m.Name == "Fire" &&
                    m.DeclaringType == typeof(EventData<CurrencyModifierQuery>) &&
                    code.Take(i).Reverse().FirstOrDefault(c => c.operand is FieldInfo)?.operand is FieldInfo field &&
                    field.Name == "OnCurrencyModifierQuery")
                { code[i].opcode = OpCodes.Call; code[i].operand = AccessTools.Method(typeof(StationRecoveryPatches), nameof(Modified)); count++; }
            if (count != 1) throw new InvalidOperationException("Unknown AddScience modifier path.");
            return code;
        }
        private static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> input, Type owner, string name, string replacement)
        {
            var code = input.ToList(); int count = 0;
            foreach (CodeInstruction c in code)
                if (c.operand is MethodInfo m && m.DeclaringType == owner && m.Name == name)
                { c.opcode = OpCodes.Call; c.operand = AccessTools.Method(typeof(StationRecoveryPatches), replacement); count++; }
            if (count != 1) throw new InvalidOperationException("Unknown stock science grant path.");
            return code;
        }
        private static void Modified(EventData<CurrencyModifierQuery> evt, CurrencyModifierQuery query)
        { if (grant != null && grant.Query == null) grant.Query = query; evt.Fire(query); }
        private static void StockGrant(ResearchAndDevelopment rnd, float amount, TransactionReasons reason)
        {
            Grant? previous = grant; grant = submission;
            try { rnd.AddScience(amount, reason); }
            finally { grant = previous; }
        }
        private static float Submit(ResearchAndDevelopment rnd, float data, float ratio, ScienceSubject subject,
            float scalar, ProtoVessel source, bool reverse, ProtoPartSnapshot part, ConfigNode dataNode, MissionRecoveryDialog dialog)
        {
            Recovery? context = recovery;
            if (context != null) context.Dialog = dialog;
            Grant? previous = submission; var capture = new Grant(); submission = capture;
            float stock;
            try { stock = rnd.SubmitScienceData(data, ratio, subject, scalar, source, reverse); }
            finally { submission = previous; }
            try
            {
                StationRecoveryScenario? scenario = StationRecoveryScenario.Current;
                if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX ||
                    context == null || scenario == null || !scenario.CanPay || !context.Seen.Add(dataNode) ||
                    !Eligible(source, part, dataNode, subject.id, out var state, out var definition)) return stock;
                float effective = capture.Query == null ? 0 : capture.Query.GetInput(Currency.Science) + capture.Query.GetEffectDelta(Currency.Science);
                RecoveryBonusReceipt receipt = scenario.Ledger.Stage(state.ContainerId, state.RunId!.Value,
                    Localizer.Format(ResearchAndDevelopment.GetExperiment(definition.Id).experimentTitle),
                    RecoveryBonusLedger.Calculate(effective, definition.RecoveryScienceBonusPercent));
                if (!receipt.Paid)
                {
                    context.Bonus.Add(receipt);
                    scenario.Pay(receipt);
                }
            }
            catch (Exception e) { Debug.LogError("[Koffee] Recovery bonus failed; stock science preserved: " + e); }
            return stock;
        }
        internal static bool Eligible(ProtoVessel vessel, ProtoPartSnapshot part, ConfigNode data,
            string subject, out ExperimentContainerState state, out StationExperimentDefinition definition)
        {
            state = null!; definition = null!;
            if (vessel == null || !(vessel.landed || vessel.splashed) || vessel.orbitSnapShot == null ||
                !FlightGlobals.Bodies.Any(b => b != null && b.flightGlobalsIndex == vessel.orbitSnapShot.ReferenceBodyIndex && b.bodyName == "Kerbin")) return false;
            ModuleKoffeeExperiment[] prefabs = part.partPrefab?.Modules?.OfType<ModuleKoffeeExperiment>().ToArray() ?? Array.Empty<ModuleKoffeeExperiment>();
            if (prefabs.Length != 1 || !StationExperimentCatalogLoader.Current.TryGet(ExperimentContainerSnapshotAdapter.ExperimentId(part), out definition) ||
                definition.RecoveryScienceBonusPercent <= 0) return false;
            if (ExperimentContainerSnapshotAdapter.Read(part, definition.Id, out var read, out string error) != ExperimentContainerReadResult.Valid || read == null)
            { Debug.LogError("[Koffee] Recovery container excluded: " + error); return false; }
            state = read;
            return state.Status == ExperimentContainerStatus.Finalized && state.WorkComplete() &&
                state.ContainerId != Guid.Empty && state.RunId.HasValue && state.RunId.Value != Guid.Empty &&
                !state.ExtractionReceiptId.HasValue && string.IsNullOrEmpty(state.ExtractionKind) && !state.ExtractionUT.HasValue &&
                state.ResearchCategory == definition.ScienceCategory && state.ResultSubjectId == subject &&
                subject.StartsWith(definition.Id + "@", StringComparison.Ordinal) && data.GetValues("subjectID").Length == 1 && data.GetValue("subjectID") == subject &&
                part.modules.Count(m => m.moduleName == nameof(ModuleKoffeeExperiment)) == 1 &&
                part.modules.Where(m => m.moduleName == nameof(ModuleKoffeeExperiment))
                    .Single().moduleValues.GetNodes("ScienceData").Length == 1 &&
                ReferenceEquals(part.modules.Single(m => m.moduleName == nameof(ModuleKoffeeExperiment))
                    .moduleValues.GetNodes("ScienceData")[0], data);
        }
    }
}

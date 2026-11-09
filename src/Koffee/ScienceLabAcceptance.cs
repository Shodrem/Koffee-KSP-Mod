using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ShoAerospace.Koffee
{
    internal static class ScienceLabAcceptance
    {
        private static readonly FieldInfo History = AccessTools.Field(typeof(ModuleScienceLab), "ExperimentData");
        private static bool busy;
        internal static bool Busy => busy;
        internal static List<string> LocalHistory(ModuleScienceLab lab) => (List<string>)History.GetValue(lab)!;
        private static bool Positive(float value) => ExperimentContainerState.Finite(value) && value > 0;

        internal static bool Preview(ModuleScienceLab lab, ScienceData data, out ScienceSubject subject,
            out float remaining, out float amount)
        {
            try { return Compute(lab, data, out subject, out remaining, out amount); }
            catch (Exception error)
            {
                subject = null!; remaining = amount = 0;
                UnityEngine.Debug.LogError("[Koffee] Invalid laboratory preview rejected: " + error);
                return false;
            }
        }

        private static bool Compute(ModuleScienceLab lab, ScienceData data, out ScienceSubject subject,
            out float remaining, out float amount)
        {
            subject = null!; remaining = amount = 0;
            var ledger = ScienceLabLedgerScenario.Current;
            if (ledger == null || data == null || ScienceReportPolicy.Physical(data) ||
                string.IsNullOrWhiteSpace(data.subjectID) || ledger.Contains(data.subjectID) ||
                lab == null || !ScienceReportTransfer.Live(lab.part) || lab.Converter == null ||
                lab.processingData || !lab.IsOperational() || LocalHistory(lab).Contains(data.subjectID) ||
                ResearchAndDevelopment.Instance == null || HighLogic.CurrentGame == null ||
                !Positive(data.dataAmount) || !Positive(data.scienceValueRatio)) return false;
            subject = ResearchAndDevelopment.GetSubjectByID(data.subjectID);
            if (subject == null || !Positive(subject.dataScale) || !Positive(subject.scienceCap) ||
                !Positive(subject.subjectValue) || !ExperimentContainerState.Finite(subject.science) ||
                subject.science < 0 || !ExperimentContainerState.Finite(subject.scientificValue)) return false;
            remaining = ResearchAndDevelopment.GetScienceValue(data.dataAmount, data.scienceValueRatio, subject, 1f);
            float gain = HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier;
            if (!Positive(remaining) || !Positive(gain)) return false;
            // Preserve the stock float operations, conditions and final banker's
            // rounding, substituting only the recoverable remainder and destination.
            float value = remaining * gain;
            Vessel vessel = lab.vessel;
            if (vessel.Landed) value *= 1 + lab.SurfaceBonus;
            if (data.subjectID.Contains(vessel.mainBody.bodyName)) value *= 1 + lab.ContextBonus;
            if ((vessel.Landed || vessel.Splashed) && vessel.mainBody == FlightGlobals.GetHomeBody()) value *= lab.homeworldMultiplier;
            amount = (float)Math.Round(value);
            float next = lab.dataStored + amount;
            return Positive(amount) && ExperimentContainerState.Finite(lab.dataStored) && lab.dataStored >= 0 &&
                Positive(lab.dataStorage) && ExperimentContainerState.Finite(next) && next > lab.dataStored && next <= lab.dataStorage &&
                ExperimentContainerState.Finite(subject.science + remaining) && subject.science + remaining > subject.science;
        }

        internal static IEnumerable<ModuleScienceLab> Destinations(Vessel vessel, ScienceData data) =>
            vessel.parts.Where(p => p != null && p.State != PartStates.DEAD)
                .SelectMany(p => p.Modules.OfType<ModuleScienceLab>())
                .Where(lab => Preview(lab, data, out _, out _, out _));

        internal static bool Accept(IScienceDataContainer source, ScienceData data, ModuleScienceLab lab, bool notify = true)
        {
            if (busy || ScienceReportTransfer.Mutating || !(source is PartModule module) ||
                !ScienceReportTransfer.Live(module.part) || module.vessel != lab?.vessel ||
                !source.GetData().Any(d => ReferenceEquals(d, data)) ||
                (source is ModuleKoffeeExperiment k && ScienceReportPolicy.Physical(k)) ||
                !Preview(lab!, data, out ScienceSubject subject, out float remaining, out float amount))
            { if (notify) ScienceReportUI.Message("LabRejected"); return false; }
            ScienceReportSnapshot snapshot;
            try { snapshot = new ScienceReportSnapshot(source); }
            catch (NotSupportedException) { if (notify) ScienceReportUI.Message("LabRejected"); return false; }
            var ledger = ScienceLabLedgerScenario.Current!;
            float oldScience = subject.science, oldValue = subject.scientificValue;
            float oldStored = lab.dataStored, oldLabValue = data.labValue;
            string[] oldHistory = LocalHistory(lab).ToArray();
            Action? extractionCompletion = null;
            busy = true;
            try
            {
                // Delegate calls happen before any mutation. Do not call
                // SubmitScienceData/AddScience or emit payment/contract events.
                float next = oldScience + remaining;
                float nextValue = subject.applyScienceScale ? ResearchAndDevelopment.GetSubjectValue(next, subject) : oldValue;
                if (!ExperimentContainerState.Finite(nextValue) || nextValue < 0) return false;
                using (var charge = ScienceOperationCharge.Take(module.part))
                {
                    if (charge == null) { if (notify) ScienceReportUI.Message("NoEC"); return false; }
                    try
                    {
                        lab.dataStored = oldStored + amount;
                        LocalHistory(lab).Add(data.subjectID);
                        subject.science = next; subject.scientificValue = nextValue;
                        data.labValue = amount;
                        extractionCompletion = ScienceReportExtraction.Remove(source, data);
                        if (source.GetData().Any(d => ReferenceEquals(d, data))) throw new InvalidOperationException("Source retained lab report.");
                        ledger.Add(data, module.part, lab, remaining, amount);
                        charge.Commit();
                    }
                    catch
                    {
                        ledger.Remove(data.subjectID); snapshot.Restore();
                        lab.dataStored = oldStored; LocalHistory(lab).Clear(); LocalHistory(lab).AddRange(oldHistory);
                        subject.science = oldScience; subject.scientificValue = oldValue; data.labValue = oldLabValue;
                        throw;
                    }
                }
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogError("[Koffee] Lab acceptance rolled back: " + error);
                if (notify) ScienceReportUI.Message("Failed"); return false;
            }
            finally { busy = false; }
            ScienceReportExtraction.Complete(extractionCompletion);
            if (notify) ScienceReportUI.Message("LabAccepted", amount.ToString("0"));
            return true;
        }
    }
}

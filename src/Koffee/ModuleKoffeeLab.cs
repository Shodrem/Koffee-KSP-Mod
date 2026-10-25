using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    public sealed partial class ModuleKoffeeLab : PartModule
    {
        [KSPField(guiActive = true, guiName = "#Koffee_Lab_Status")]
        public string koffeeStatus = "";
        [KSPField(guiActive = true, guiName = "#Koffee_Lab_Autonomy")]
        public string estimatedAutonomy = "";

        private readonly List<KaffeinationAttempt> activations = new List<KaffeinationAttempt>();
        private readonly LabTransitions transitions = new LabTransitions();
        private ModuleScienceConverter? converter;
        private StationLaboratoryPower.Lease? laboratoryPower;
        internal bool PowerBlocked => laboratoryPower?.Blocked == true;
        private float beforeData;
        private float beforeScience;
        private bool errorLogged;
        private bool initializationLogged;
        private bool processingContributionSet;
        private float processingContribution;
        private float nextDisplayUpdate;
        private double availableCups;
        private int loggedWorking = -1;
        private int loggedScientists = -1;

        private IReadOnlyList<ProtoCrewMember> Scientists() => KerbalWorkAssignment.LabScientists(part);

        private LabState Condition(ModuleScienceConverter? science, int scientistCount)
        {
            if (scientistCount == 0) return LabState.NoScientists;
            if (science == null || !science.IsActivated) return LabState.Inactive;
            if (science.Lab == null || science.Lab.dataStored < 1e-9) return LabState.NoData;
            return LabState.Ready;
        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsEditor && part != null) RefreshExperimentDisplay();
            if (!HighLogic.LoadedSceneIsFlight || part == null || vessel == null) return;
            if (Time.realtimeSinceStartup >= nextDisplayUpdate) RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            nextDisplayUpdate = Time.realtimeSinceStartup + 0.5f;
            RefreshExperimentDisplay();
            availableCups = KoffeeSources.Available(part);
            IReadOnlyList<ProtoCrewMember> scientists = Scientists();
            double[] remaining = scientists.Select(kerbal =>
                KerbalWorkState.GetKaffeinatedRemaining(kerbal)).ToArray();
            int working = remaining.Count(value => value > 0);
            string key = LabDisplay.TimeKey(LabDisplay.Autonomy(remaining, availableCups,
                KerbalWorkState.KaffeinatedDuration), out string hours, out string minutes);
            estimatedAutonomy = Localizer.Format(key, hours, minutes);
            var science = part.FindModuleImplementing<ModuleScienceConverter>();
            key = transitions.StatusKey(Condition(science, scientists.Count));
            koffeeStatus = Localizer.Format(key);
            LogActiveCount(working, scientists.Count);
        }

        internal void BeginUpdate()
        {
            Finish(false);
            IReadOnlyList<ProtoCrewMember> scientists = Scientists();
            var science = part.FindModuleImplementing<ModuleScienceConverter>();
            StationLaboratoryPower.Observe(science);
            transitions.SetCondition(Condition(science, scientists.Count));
            processingContributionSet = false;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            RefreshExperimentDisplay();
            bool foundConverter = part.FindModuleImplementing<ModuleScienceConverter>() != null;
            // These PAW fields describe stock conversion, not Koffee experiment jobs.
            Fields[nameof(koffeeStatus)].guiActive = foundConverter;
            Fields[nameof(estimatedAutonomy)].guiActive = foundConverter;
            if (state == StartState.Editor) return;
            if (!HighLogic.LoadedSceneIsFlight || initializationLogged) return;
            initializationLogged = true;
            Debug.Log("[Koffee] Lab integration enabled for part " + (part.partInfo?.name ?? part.name) +
                "; flightID=" + part.flightID + "; Kaffeinated duration=" +
                KerbalWorkState.KaffeinatedDuration.ToString("R", CultureInfo.InvariantCulture) +
                "; ModuleScienceConverter found=" + foundConverter);
        }

        internal double Reserve(ModuleScienceConverter science, double interval, double efficiency, double delta)
        {
            Finish(false);
            IReadOnlyList<ProtoCrewMember> scientists = Scientists();
            transitions.SetCondition(Condition(science, scientists.Count));
            processingContributionSet = true;
            processingContribution = TotalContribution(scientists, science.scientistBonus);
            if (!science.IsActivated || scientists.Count == 0 || science.Lab == null) return interval;

            bool stockCanAdvance = CanAdvance(science, interval, efficiency, delta,
                processingContribution);

            double endUT = LabPatches.UpdateEndUT(science);
            if (!LabWork.Positive(endUT) || endUT > Planetarium.GetUniversalTime() + 1)
                endUT = Planetarium.GetUniversalTime();
            double startUT = endUT - delta;
            double bonusInterval = delta;
            if (StationLaboratoryPower.Enabled(science))
            {
                laboratoryPower = StationLaboratoryPower.Begin(science, startUT, endUT, interval, stockCanAdvance);
                interval = laboratoryPower.Seconds;
                startUT = laboratoryPower.Start; endUT = laboratoryPower.End;
                bonusInterval = laboratoryPower.Seconds;
                stockCanAdvance = stockCanAdvance && CanAdvance(science, interval, efficiency, delta, processingContribution);
            }
            ScienceKoffeeSettings settings = ScienceSettingsLoader.Current;
            double bonus = settings.Enabled ? settings.ScientistBonus : 0;
            double totalContribution = 0;
            foreach (ProtoCrewMember kerbal in scientists)
            {
                double stockContribution = KerbalPerformance.StockLabContribution(
                    kerbal, science.scientistBonus);
                double coverage = Coverage(kerbal, startUT, endUT,
                    stockCanAdvance && bonus > 0);
                totalContribution += ScienceKoffeeMath.Apply(stockContribution,
                    bonus, coverage, bonusInterval);
            }

            availableCups = KoffeeSources.Available(part);
            LogActiveCount(WorkingCount(), scientists.Count);
            processingContribution = (float)totalContribution;
            if (stockCanAdvance)
            {
                converter = science;
                beforeData = science.Lab.dataStored;
                beforeScience = science.Lab.storedScience;
            }
            return interval;
        }

        internal float ScientistContribution(float scientistBonus)
        {
            if (processingContributionSet) return processingContribution;
            return TotalContribution(Scientists(), scientistBonus);
        }

        internal void Finish(bool observe = true)
        {
            bool stateChanged = activations.Count > 0;
            bool progressed = converter != null && converter.Lab != null &&
                (converter.Lab.storedScience > beforeScience || converter.Lab.dataStored < beforeData);
            try
            {
                if (progressed)
                {
                    laboratoryPower?.Commit();
                    foreach (KaffeinationAttempt activation in activations) activation.Commit();
                    transitions.ConfirmProgress();
                }
            }
            finally
            {
                laboratoryPower?.Dispose(); laboratoryPower = null;
                for (int i = activations.Count - 1; i >= 0; i--) activations[i].Dispose();
                activations.Clear();
                converter = null;
                processingContributionSet = false;
            }
            if (!observe) return;

            var science = part.FindModuleImplementing<ModuleScienceConverter>();
            int total = Scientists().Count;
            transitions.SetCondition(Condition(science, total));
            if (stateChanged || Time.realtimeSinceStartup >= nextDisplayUpdate) RefreshDisplay();
        }

        internal bool NativePowerActive()
        {
            var science = part.FindModuleImplementing<ModuleScienceConverter>();
            return StationLaboratoryPower.Enabled(science) && science != null && science.IsActivated &&
                science.isEnabled && science.isActiveAndEnabled && science.Lab != null &&
                CanAdvance(science, .02, 1, .02, TotalContribution(Scientists(), science.scientistBonus));
        }

        private static bool CanAdvance(ModuleScienceConverter science, double interval,
            double efficiency, double delta, double contribution)
        {
            if (!LabWork.Positive(interval) || !LabWork.Positive(efficiency) ||
                !LabWork.Positive(delta) || contribution <= 0) return false;
            float data = Math.Min(science.Lab.dataStored, science.Lab.dataStorage);
            float stored = Math.Min(science.Lab.storedScience, science.scienceCap);
            double time = LabWork.Effective(interval, efficiency, delta);
            double used = time * science.dataProcessingMultiplier * contribution * data /
                Math.Pow(10, science.researchTime);
            double produced = science.scienceMultiplier * used;
            return LabWork.CanAdvance(interval * efficiency / delta, data, stored, contribution,
                used, produced, science.scienceCap);
        }

        private static float TotalContribution(IEnumerable<ProtoCrewMember> scientists, float scientistBonus) =>
            (float)scientists.Sum(kerbal => KerbalPerformance.StockLabContribution(kerbal, scientistBonus));

        private double Coverage(ProtoCrewMember kerbal, double startUT, double endUT, bool mayPurchase)
        {
            double duration = KerbalWorkState.KaffeinatedDuration;
            KaffeinatedWindow? initial = KerbalWorkState.TryGetKaffeinatedWindow(
                kerbal, out KaffeinatedWindow stored) ? stored : (KaffeinatedWindow?)null;
            return KaffeinatedCoveragePlanner.ContinuousCoverage(initial, startUT, endUT,
                duration, mayPurchase, renewalUT =>
            {
                KaffeinationAttempt? activation = KerbalWorkState.TryBeginKaffeination(
                    kerbal, part, renewalUT, false);
                if (activation == null || !activation.NewlyActivated)
                {
                    activation?.Dispose();
                    return 0;
                }
                activations.Add(activation);
                return activation.UntilUT;
            });
        }

        private int WorkingCount() => Scientists().Count(KerbalWorkState.IsKaffeinated);

        private void LogActiveCount(int working, int total)
        {
            if (working == loggedWorking && total == loggedScientists) return;
            loggedWorking = working;
            loggedScientists = total;
            Debug.Log("[Koffee] Lab active scientists: " + working + "/" + total);
        }

        internal void LogError(string message)
        {
            if (errorLogged) return;
            errorLogged = true;
            Debug.LogError("[Koffee] " + message);
        }
    }
}

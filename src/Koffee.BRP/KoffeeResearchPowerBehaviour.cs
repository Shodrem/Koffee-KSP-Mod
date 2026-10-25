using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BackgroundResourceProcessing;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Core;
using UnityEngine;
using BrpConverter = BackgroundResourceProcessing.Core.ResourceConverter;

namespace ShoAerospace.Koffee.Brp
{
    // One research recipe per vessel deduplicates physical equipment. BRP
    // applies a common fractional operating rate to this shared research load.
    // Standard lab data/science converters remain owned by BRP.
    public sealed class KoffeeResearchPowerBehaviour : ConverterBehaviour
    {
        [KSPField(isPersistant = true)] public double AppliedUT;
        [KSPField(isPersistant = true)] public double StartUT;
        [KSPField(isPersistant = true)] public double EndUT;
        private StationBackgroundPowerPlan plan = new StationBackgroundPowerPlan();
        private readonly Dictionary<Guid, double> rates = new Dictionary<Guid, double>();
        private bool failed;
        private bool baselineKnown;
        private Game? baselineGame;
        private GameScenes baselineScene;
        private object? baselineProto;
        internal bool Idle => !failed && plan.Work.Count == 0;

        internal void RememberBaseline(double now)
        {
            baselineKnown = true; baselineGame = HighLogic.CurrentGame;
            baselineScene = HighLogic.LoadedScene; baselineProto = Vessel.protoVessel;
            AppliedUT = Math.Max(AppliedUT, now);
        }

        internal bool PrimeContinuity(BackgroundResourceProcessor processor, double now)
        {
            if (failed || (baselineKnown && ReferenceEquals(baselineGame, HighLogic.CurrentGame) &&
                baselineScene == HighLogic.LoadedScene && ReferenceEquals(baselineProto, Vessel.protoVessel))) return false;
            if (!StationExperimentBackground.TickPowered(Vessel, now, BrpPowerProvider.Accounts(processor), (_, a, b) => 0)) failed = true;
            RememberBaseline(now);
            return true;
        }

        public override ConverterResources GetResources(VesselState state)
        {
            var processor = Vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
            if (processor == null || failed) return Empty();
            if (state.CurrentTime < AppliedUT) return new ConverterResources { NextChangepoint = AppliedUT };
            try
            {
                var accounts = BrpPowerProvider.Accounts(processor);
                // This also establishes the scientific baseline on first record
                // and applies provider migration through the ordinary publisher.
                if (!StationExperimentBackground.TickPowered(Vessel, state.CurrentTime, accounts, (_, a, b) => 0))
                    throw new InvalidOperationException("Could not publish background baseline.");
                plan = StationExperimentBackground.PowerPlan(Vessel, state.CurrentTime, accounts,
                    id => NativeLaboratory(processor, id) != null);
                StartUT = state.CurrentTime;
                EndUT = plan.NextUT;
                rates.Clear();
                var resources = new ConverterResources();
                resources.NextChangepoint = plan.NextUT;
                if (plan.Work.Count > 0 && plan.Rate > 0)
                    resources.Inputs.Add(new ResourceRatio("ElectricCharge", plan.Rate, false, ResourceFlowMode.STAGE_PRIORITY_FLOW));
                return resources;
            }
            catch (Exception error)
            {
                failed = true;
                Debug.LogError("[Koffee] BRP research recipe disabled for this vessel; scientific progress is waiting: " + error);
                return Empty();
            }
        }

        private static ConverterResources Empty() => new ConverterResources { NextChangepoint = double.PositiveInfinity };

        public override void OnRatesComputed(BackgroundResourceProcessor processor, BrpConverter converter, RateCalculatedEvent evt)
        {
            try { ComputeRates(processor, converter, evt); }
            catch (Exception error)
            {
                failed = true;
                rates.Clear();
                converter.NextChangepoint = evt.CurrentTime;
                processor.MarkDirty(); processor.SuppressNoProgressError();
                Debug.LogError("[Koffee] BRP operating-rate update failed; this vessel's research is waiting: " + error);
            }
        }

        private void ComputeRates(BackgroundResourceProcessor processor, BrpConverter converter, RateCalculatedEvent evt)
        {
            if (failed) return;
            if (evt.CurrentTime < AppliedUT)
            {
                rates.Clear(); converter.NextChangepoint = AppliedUT; return;
            }
            var current = StationExperimentBackground.PowerPlan(Vessel, evt.CurrentTime, BrpPowerProvider.Accounts(processor),
                id => NativeLaboratory(processor, id) != null);
            if (!current.Consumers.OrderBy(p => p.Key).SequenceEqual(plan.Consumers.OrderBy(p => p.Key)) ||
                !current.Work.Select(w => new { w.ContainerId, w.RunId, w.SharedStockLaboratory })
                    .SequenceEqual(plan.Work.Select(w => new { w.ContainerId, w.RunId, w.SharedStockLaboratory })))
            {
                converter.NextChangepoint = evt.CurrentTime;
                processor.SuppressNoProgressError();
                processor.MarkDirty();
                return;
            }
            plan = current;
            StartUT = evt.CurrentTime;
            EndUT = plan.NextUT;
            rates.Clear();
            foreach (var work in plan.Work)
            {
                double rate = converter.Rate;
                if (work.SharedStockLaboratory)
                    rate = Math.Min(rate, NativeLaboratory(processor, work.LaboratoryFlightId)?.Rate ?? 0);
                if (!ExperimentContainerState.Finite(rate) || rate < 0 || rate > 1)
                    throw new InvalidOperationException("BRP returned an invalid operating rate.");
                rates[work.ContainerId] = rate;
                if (rate > 0) EndUT = Math.Min(EndUT, evt.CurrentTime + work.RemainingSeconds / rate);
            }
            // Ensure the recipe is refreshed at every electrical changepoint as
            // well as coffee expiry/completion; stock lab capacity may change.
            converter.NextChangepoint = EndUT;
            if (!processor.Vessel.loaded && rates.Values.Any(r => r > 0))
            {
                StationExperimentBackground.ActivatePoweredTeam(Vessel, evt.CurrentTime, BrpPowerProvider.Accounts(processor),
                    rates.Where(r => r.Value > 0).Select(r => r.Key).ToArray());
                processor.MarkDirty();
            }
        }

        internal void Publish(BackgroundResourceProcessor processor, ChangepointEvent change)
        {
            if (failed || change.CurrentChangepoint <= AppliedUT || change.CurrentChangepoint <= change.LastChangepoint) return;
            double end = Math.Min(change.CurrentChangepoint, EndUT);
            if (!ExperimentContainerState.Finite(end) || end < AppliedUT) return;
            double begin = Math.Max(StartUT, Math.Max(AppliedUT, change.LastChangepoint));
            var matching = new HashSet<Guid>();
            foreach (var part in Vessel.protoVessel.protoPartSnapshots)
                if (ExperimentContainerSnapshotAdapter.Read(part, ExperimentContainerSnapshotAdapter.ExperimentId(part), out var state, out _) ==
                    ExperimentContainerReadResult.Valid && state != null && plan.Work.Any(w => w.ContainerId == state.ContainerId && w.RunId == state.RunId))
                    matching.Add(state.ContainerId);
            try
            {
                bool published = StationExperimentBackground.TickPowered(Vessel, end, BrpPowerProvider.Accounts(processor), (id, from, to) =>
                    matching.Contains(id) && rates.TryGetValue(id, out double rate) ?
                        Math.Max(0, Math.Min(to, end) - Math.Max(from, begin)) * rate : 0);
                if (!published) throw new InvalidOperationException("Could not publish powered research checkpoint.");
                AppliedUT = end;
                processor.MarkDirty();
            }
            catch (Exception error)
            {
                failed = true;
                foreach (var converter in processor.Converters)
                    if (ReferenceEquals(converter.Behaviour, this)) converter.NextChangepoint = change.CurrentChangepoint;
                processor.MarkDirty();
                processor.SuppressNoProgressError();
                Debug.LogError("[Koffee] BRP publication failed; this vessel's research is waiting: " + error);
            }
        }

        // Native conversion already pays the laboratory's configured 4 EC/s.
        // Exclude that physical part from the extra research recipe and require
        // its actual BRP operating rate before granting research on that lab.
        private static BrpConverter? NativeLaboratory(BackgroundResourceProcessor processor, uint flightId)
        {
            foreach (var converter in processor.Converters)
            {
                if (converter.FlightId != flightId || converter.Behaviour is KoffeeResearchPowerBehaviour ||
                    !converter.Inputs.Any(r => r.Value.ResourceName == "ElectricCharge" && r.Value.Ratio > 0) ||
                    !converter.Outputs.Any(r => r.Value.ResourceName == "BRPScience" && r.Value.Ratio > 0)) continue;
                bool data = false, capacity = false;
                for (int i = 0; i < processor.Inventories.Count; i++)
                {
                    var inventory = processor.Inventories[i];
                    if (inventory == null) continue;
                    if (converter.Pull[i] && inventory.ResourceName == "BRPScienceLabData" && inventory.Amount > 1e-8) data = true;
                    if (converter.Push[i] && inventory.ResourceName == "BRPScience" && inventory.Available > 1e-8) capacity = true;
                }
                if (data && capacity) return converter;
            }
            return null;
        }

        protected override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            node.AddValue("failed", failed.ToString());
            node.AddValue("planNextUT", plan.NextUT.ToString("R", CultureInfo.InvariantCulture));
            foreach (var work in plan.Work)
            {
                var saved = node.AddNode("KOFFEE_WORK");
                saved.AddValue("container", work.ContainerId.ToString("N")); saved.AddValue("run", work.RunId.ToString("N"));
                saved.AddValue("lab", work.LaboratoryFlightId.ToString(CultureInfo.InvariantCulture)); saved.AddValue("sharedLab", work.SharedStockLaboratory.ToString());
                saved.AddValue("remaining", work.RemainingSeconds.ToString("R", CultureInfo.InvariantCulture));
                saved.AddValue("rate", (rates.TryGetValue(work.ContainerId, out double rate) ? rate : 0).ToString("R", CultureInfo.InvariantCulture));
            }
        }

        protected override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (bool.TryParse(node.GetValue("failed"), out bool wasFailed)) failed = wasFailed;
            plan = new StationBackgroundPowerPlan(); rates.Clear();
            if (double.TryParse(node.GetValue("planNextUT"), NumberStyles.Float, CultureInfo.InvariantCulture, out double next)) plan.NextUT = next;
            if (!ExperimentContainerState.Finite(StartUT) || StartUT < 0 ||
                !ExperimentContainerState.Finite(AppliedUT) || AppliedUT < 0 || double.IsNaN(EndUT) || EndUT < StartUT ||
                double.IsNaN(plan.NextUT) || plan.NextUT < StartUT) failed = true;
            foreach (var saved in node.GetNodes("KOFFEE_WORK"))
            {
                if (!Guid.TryParseExact(saved.GetValue("container"), "N", out Guid id) || id == Guid.Empty ||
                    !Guid.TryParseExact(saved.GetValue("run"), "N", out Guid run) || run == Guid.Empty ||
                    !uint.TryParse(saved.GetValue("lab"), out uint lab) ||
                    !bool.TryParse(saved.GetValue("sharedLab"), out bool shared) ||
                    !double.TryParse(saved.GetValue("remaining"), NumberStyles.Float, CultureInfo.InvariantCulture, out double remaining) ||
                    !ExperimentContainerState.Finite(remaining) || remaining <= 0 ||
                    !double.TryParse(saved.GetValue("rate"), NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) ||
                    !ExperimentContainerState.Finite(rate) || rate < 0 || rate > 1 || rates.ContainsKey(id))
                { failed = true; continue; }
                plan.Work.Add(new StationBackgroundWork { ContainerId = id, RunId = run, LaboratoryFlightId = lab,
                    SharedStockLaboratory = shared, RemainingSeconds = remaining });
                rates.Add(id, rate);
            }
        }
    }
}

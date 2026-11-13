using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Core;
using BrpConverter = BackgroundResourceProcessing.Core.ResourceConverter;

namespace ShoAerospace.Koffee.Brp
{
    // Separate recipe: a service deficit never gates the research recipe.
    // BRP's operating fraction scales the capacity actually provided during
    // that solver interval. No energy is reconstructed from final inventories.
    public sealed class KomfortPowerBehaviour : ConverterBehaviour
    {
        [KSPField(isPersistant = true)] public double AppliedUT;
        private double start, end = double.PositiveInfinity, fraction;
        private Dictionary<uint, double> demand = new Dictionary<uint, double>();
        private Game? baselineGame;
        private object? baselineProto;
        private GameScenes baselineScene;
        private bool failed, ratesKnown;
        private KomfortOptions? observedOptions;
        private DeadlyOptions? observedDeadly;
        internal double PendingBoundary(double now)
        {
            double next = KomfortRuntime.NextBoundary(Vessel, AppliedUT);
            return next > AppliedUT && next <= now ? next : double.PositiveInfinity;
        }
        internal bool OptionsChanged(double now)
        {
            var current = KomfortRuntime.OptionsAt(now);
            var deadly = DeadlyScenario.OptionsAt(now);
            bool changed = observedOptions.HasValue && !observedOptions.Value.Equals(current) ||
                observedDeadly.HasValue && !observedDeadly.Value.Equals(deadly);
            observedOptions = current;
            observedDeadly = deadly;
            return changed;
        }

        internal bool Prime(double now)
        {
            if (ReferenceEquals(baselineGame, HighLogic.CurrentGame) && ReferenceEquals(baselineProto, Vessel.protoVessel) &&
                baselineScene == HighLogic.LoadedScene) return false;
            baselineGame = HighLogic.CurrentGame; baselineProto = Vessel.protoVessel; baselineScene = HighLogic.LoadedScene;
            AppliedUT = Math.Max(now, AppliedUT); start = AppliedUT; fraction = 0; ratesKnown = false;
            OptionsChanged(now);
            KomfortRuntime.Invalidate(Vessel);
            KomfortRuntime.Observe(Vessel, AppliedUT, backgroundFraction: 0);
            return true;
        }
        public override ConverterResources GetResources(VesselState state)
        {
            var result = new ConverterResources();
            if (failed) { result.NextChangepoint = double.PositiveInfinity; return result; }
            if (state.CurrentTime < AppliedUT) { result.NextChangepoint = AppliedUT; return result; }
            start = state.CurrentTime;
            demand = KomfortRuntime.BackgroundDemand(Vessel, start);
            end = DeadlyScenario.SafeBoundary(Vessel, start, Math.Min(KomfortRuntime.NextBoundary(Vessel, start), KomfortMaintenance.NextBoundary(Vessel, start, fraction)));
            result.NextChangepoint = end;
            double rate = demand.Values.Sum();
            if (rate > 0) result.Inputs.Add(new ResourceRatio("ElectricCharge", rate, false, ResourceFlowMode.STAGE_PRIORITY_FLOW));
            return result;
        }
        public override void OnRatesComputed(BackgroundResourceProcessor processor, BrpConverter converter, RateCalculatedEvent evt)
        {
            var current = KomfortRuntime.BackgroundDemand(Vessel, evt.CurrentTime);
            if (!current.OrderBy(p => p.Key).SequenceEqual(demand.OrderBy(p => p.Key)))
            {
                converter.NextChangepoint = evt.CurrentTime;
                processor.MarkDirty(); processor.SuppressNoProgressError();
                return;
            }
            if (evt.CurrentTime < AppliedUT) { fraction = 0; converter.NextChangepoint = AppliedUT; return; }
            start = evt.CurrentTime;
            fraction = converter.Rate;
            if (!ExperimentContainerState.Finite(fraction) || fraction < 0 || fraction > 1)
            { failed = true; fraction = 0; }
            if (!failed && !ratesKnown)
            {
                // BRP publishes the old state before its first rate callback.
                // Default zero after load/Prime is unknown, not a power failure.
                // Begin evidence at the solver UT; do not charge the preceding gap.
                AppliedUT = start;
                KomfortRuntime.Invalidate(Vessel);
                KomfortRuntime.Observe(Vessel, start, backgroundFraction: fraction);
                ratesKnown = true;
            }
            KomfortRuntime.Forecast(Vessel, fraction);
            end = DeadlyScenario.SafeBoundary(Vessel, start, Math.Min(KomfortRuntime.NextBoundary(Vessel, start), KomfortMaintenance.NextBoundary(Vessel, start, fraction)));
            converter.NextChangepoint = end;
        }
        internal void Publish(BackgroundResourceProcessor processor, ChangepointEvent change)
        {
            double to = Math.Min(change.CurrentChangepoint, end);
            if (failed || !ratesKnown || to <= AppliedUT || to < start || !ExperimentContainerState.Finite(to)) return;
            long revision = DeadlyScenario.Revision;
            KomfortRuntime.Observe(Vessel, to, backgroundFraction: fraction,
                authorizedFrom: Math.Max(start, Math.Max(AppliedUT, change.LastChangepoint)), maintenanceAccounts: BrpPowerProvider.Accounts(processor));
            AppliedUT = to;
            if (revision != DeadlyScenario.Revision)
            {
                foreach (var converter in processor.Converters.Where(c => c.Behaviour is EmergencyGenerationBehaviour || c.Behaviour is EmergencyReactorGenerationBehaviour))
                    converter.NextChangepoint = to;
                processor.MarkDirty(); processor.SuppressNoProgressError();
            }
        }
    }
}

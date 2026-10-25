using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    // Declarative only: the station coordinator owns every resource transaction.
    public sealed class ModuleKoffeePowerConsumer : PartModule
    {
        [KSPField] public double electricChargePerSecond;
        [KSPField] public double laboratoryElectricChargePerSecond;
        [KSPField] public double dbsFlightDemand;
        [KSPField] public double dbsEditorDemand;
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            dbsEditorDemand = StationElectricalDemand.Editor(part);
            dbsFlightDemand = 0;
        }
        public void Update()
        {
            if (part == null) return;
            dbsEditorDemand = StationElectricalDemand.Editor(part);
            if (HighLogic.LoadedSceneIsFlight && vessel?.loaded == true && StationDbsIntegration.Active)
            {
                // One read-only refresh per vessel/render frame, regardless of
                // the number of consumers. Coordinator ticks refresh separately.
                if (lastFrame != UnityEngine.Time.frameCount || lastVessel != vessel.id)
                {
                    lastFrame = UnityEngine.Time.frameCount; lastVessel = vessel.id;
                    StationElectricalDemand.Refresh(vessel);
                }
            }
            else dbsFlightDemand = 0;
        }
        private static int lastFrame = -1;
        private static Guid lastVessel;
        public override string GetInfo()
        {
            string own = electricChargePerSecond > 0 || laboratoryElectricChargePerSecond <= 0 ?
                Localizer.Format("#Koffee_Power_Nominal", electricChargePerSecond.ToString("0.###", CultureInfo.InvariantCulture)) : "";
            return own + (laboratoryElectricChargePerSecond > 0 ? (own == "" ? "" : "\n") +
                Localizer.Format("#Koffee_Power_LaboratoryNominal", laboratoryElectricChargePerSecond.ToString("0.###", CultureInfo.InvariantCulture)) : "");
        }
    }

    internal static class StationPowerPolicy
    {
        private sealed class Observation
        {
            internal Vessel Vessel = null!;
            internal object? Proto;
            internal bool Applies;
            internal double UT;
        }
        private static readonly Dictionary<Guid, Observation> observations = new Dictionary<Guid, Observation>();
        private static Game? game;
        internal static bool HighWarp => TimeWarp.WarpMode == TimeWarp.Modes.HIGH &&
            (TimeWarp.CurrentRateIndex != 0 || TimeWarp.CurrentRate != 1);
        internal static bool Applies(Vessel vessel) => vessel.loaded && (!HighWarp || StationDbsIntegration.Active);
        internal static void Clear() => observations.Clear();
        internal static bool Observe(Vessel vessel, double now, bool? closingMode = null)
        {
            if (!ReferenceEquals(game, HighLogic.CurrentGame)) { Clear(); game = HighLogic.CurrentGame; }
            bool applies = Applies(vessel);
            observations.TryGetValue(vessel.id, out var previous);
            // The first loaded observation never charges a gap from another scene,
            // snapshot or unloaded vessel. No electrical history/debt is persisted.
            bool intervalApplies = (closingMode ?? applies) && previous != null && previous.Applies &&
                ReferenceEquals(previous.Vessel, vessel) && ReferenceEquals(previous.Proto, vessel.protoVessel) && now >= previous.UT;
            observations[vessel.id] = new Observation { Vessel = vessel, Proto = vessel.protoVessel, Applies = applies, UT = now };
            return intervalApplies;
        }
    }

    // Shared ledger in the current loaded mode. Entries refer to physical parts and exact UT
    // ranges, never to a summed duration that could cover a different interval.
    internal sealed class StationStockPower
    {
        private static readonly Dictionary<Guid, StationStockPower> sessions = new Dictionary<Guid, StationStockPower>();
        private static Game? game;
        private readonly bool applies;
        private PartModule context;
        private readonly Vessel vessel;
        private readonly object? proto;
        private readonly Dictionary<Part, List<Range>> paid = new Dictionary<Part, List<Range>>();
        private double lastUT;
        private string topology = "";
        internal StationStockPower(PartModule manager, bool applies)
        { context = manager; vessel = manager.vessel; proto = manager.vessel.protoVessel; this.applies = applies; }

        internal static double Rate(Part part, bool laboratory = false)
        {
            // Old craft/saves may lack the new MODULE snapshot. Its declarative
            // nonpersistent tariff still comes from the current physical prefab.
            var consumer = part.Modules.OfType<ModuleKoffeePowerConsumer>().FirstOrDefault() ??
                part.partInfo?.partPrefab?.Modules.OfType<ModuleKoffeePowerConsumer>().FirstOrDefault();
            double value = laboratory ? consumer?.laboratoryElectricChargePerSecond ?? 0 : consumer?.electricChargePerSecond ?? 0;
            if (!ExperimentContainerState.Finite(value) || value < 0)
                throw new InvalidOperationException("Invalid Koffee electrical rate.");
            return value;
        }

        internal static void Clear() { sessions.Clear(); StationPowerDiagnostics.Clear(); }
        internal static void Forget(Vessel vessel) => sessions.Remove(vessel.id);
        internal static StationStockPower For(PartModule context, bool applies, double now)
        {
            if (!ReferenceEquals(game, HighLogic.CurrentGame)) { Clear(); game = HighLogic.CurrentGame; }
            Vessel vessel = context.vessel;
            string topology = string.Join(",", vessel.parts.Where(p => p != null && p.State != PartStates.DEAD)
                .Select(p => p.persistentId + ":" + p.flightID));
            if (!sessions.TryGetValue(vessel.id, out var session) || !ReferenceEquals(session.vessel, vessel) ||
                !ReferenceEquals(session.proto, vessel.protoVessel) || session.applies != applies || session.lastUT > now || session.topology != topology)
                sessions[vessel.id] = session = new StationStockPower(context, applies);
            session.context = context; session.lastUT = now; session.topology = topology;
            // Retain common payments across a rejected publication, until every
            // running task has moved past them. Never reuse them in another save/mode.
            double floor = vessel.parts.SelectMany(p => p.Modules.OfType<ModuleKoffeeExperiment>())
                .Where(m => m.State?.Status == ExperimentContainerStatus.Running && !m.State.WorkComplete())
                .Select(m => m.State!.LastCheckpointUT ?? now).DefaultIfEmpty(now).Min();
            floor = Math.Min(floor, StationLaboratoryPower.RetentionFloor(vessel, now));
            // A second route may request the same endpoint in this physics tick.
            foreach (var history in session.paid.Values) history.RemoveAll(r => r.End < floor);
            return session;
        }

        internal Transaction Begin(ModuleKoffeeExperiment experiment, StationExperimentDefinition definition, Part? laboratory = null) =>
            new Transaction(this, Consumers(context, experiment, definition, laboratory), context);

        internal static Dictionary<Part, double> Consumers(PartModule context, ModuleKoffeeExperiment experiment,
            StationExperimentDefinition definition, Part? laboratory = null)
        {
            var parts = new[] { context.part, experiment.part,
                StationExperimentReadings.RequiredInstrument(definition, experiment.vessel, experiment.part) }
                .Where(p => p != null && p.State != PartStates.DEAD && ReferenceEquals(p.vessel, experiment.vessel) &&
                    experiment.vessel.parts.Contains(p)).Cast<Part>().Distinct().ToArray();
            var demand = parts.ToDictionary(p => p, p => Rate(p));
            if (laboratory != null && laboratory.State != PartStates.DEAD &&
                ReferenceEquals(laboratory.vessel, experiment.vessel) && experiment.vessel.parts.Contains(laboratory))
                demand[laboratory] = Math.Max(demand.TryGetValue(laboratory, out double own) ? own : 0, Rate(laboratory, true));
            return demand;
        }

        internal Transaction BeginLaboratory(Part laboratory, PartModule caller) =>
            new Transaction(this, new Dictionary<Part, double> { [laboratory] = Rate(laboratory, true) }, caller);

        internal readonly struct Range
        {
            internal readonly double Start, End, Rate;
            internal Range(double start, double end, double rate = 0) { Start = start; End = end; Rate = rate; }
            internal bool Contains(double ut) => Start <= ut && ut < End;
        }

        internal sealed class Transaction : IDisposable
        {
            private readonly StationStockPower owner;
            private readonly PartModule context;
            private readonly Dictionary<Part, double> consumers;
            private readonly List<Range> ranges = new List<Range>();
            private readonly List<KeyValuePair<PartResource, double>> debits = new List<KeyValuePair<PartResource, double>>();
            private bool committed;
            internal bool Blocked { get; private set; }
            internal bool ControlsWork => consumers.Values.Any(rate => rate > 0);
            internal Transaction(StationStockPower owner, Dictionary<Part, double> consumers, PartModule context)
            { this.owner = owner; this.consumers = consumers; this.context = context; }

            internal double Authorize(double from, double to)
            {
                if (!ExperimentContainerState.Finite(from) || !ExperimentContainerState.Finite(to) || from < 0 || to < from)
                    throw new ArgumentException("Invalid electrical interval.");
                if (!owner.applies)
                {
                    // DBS never turns an unobserved/exempt gap into free work.
                    if (StationDbsIntegration.Active && StationPowerPolicy.Applies(owner.vessel))
                    { Blocked = true; return 0; }
                    return to - from; // Legacy exempt route: no resource query.
                }
                double cursor = from;
                while (cursor < to)
                {
                    double end = to, rate = 0;
                    foreach (var consumer in consumers)
                    {
                        double covered = 0;
                        if (owner.paid.TryGetValue(consumer.Key, out var history))
                            foreach (Range range in history)
                            {
                                if (range.Contains(cursor)) covered = Math.Max(covered, range.Rate);
                                if (range.Start > cursor) end = Math.Min(end, range.Start);
                                if (range.End > cursor) end = Math.Min(end, range.End);
                            }
                        rate += Math.Max(0, consumer.Value - covered);
                    }
                    double seconds = Process(end - cursor, rate);
                    if (seconds > 0) ranges.Add(new Range(cursor, cursor + seconds));
                    cursor += seconds;
                    if (cursor + 1e-9 < end) { Blocked = true; break; }
                    cursor = end;
                }
                return cursor - from;
            }

            private double Process(double seconds, double rate)
            {
                if (!ExperimentContainerState.Finite(rate) || rate < 0)
                    throw new InvalidOperationException("Invalid total Koffee electrical rate.");
                if (rate == 0) return seconds;
                // Record actual withdrawals, including the broker's stock scaling,
                // so rejected checkpoints restore the original tanks exactly.
                var definition = PartResourceLibrary.Instance.GetDefinition("ElectricCharge");
                var before = context.vessel.parts.Where(p => p != null && p.State != PartStates.DEAD)
                    .Select(p => p.Resources.GetFlowing(definition.id, true)).Where(r => r != null)
                    .Distinct().ToDictionary(r => r, r => r.amount);
                try
                {
                    var recipe = new ConversionRecipe();
                    recipe.Inputs.Add(new ResourceRatio("ElectricCharge", rate, false, definition.resourceFlowMode));
                    var broker = StationDbsIntegration.Active ? new StationDbsResourceBroker() : null;
                    var converter = broker == null ? new ResourceConverter() : new ResourceConverter(broker);
                    double authorized = converter.ProcessRecipe(seconds, recipe, context.part, context, 1f).TimeFactor;
                    if (broker != null)
                    {
                        // ProcessRecipe limits on availability but does not use the
                        // debit return value to revise TimeFactor. Credit only paid EC.
                        authorized = Math.Min(authorized, broker.Received / rate);
                        StationPowerDiagnostics.Report(context, seconds, rate, broker.Requested, broker.Received,
                            authorized, before.Keys.Sum(r => r.maxAmount));
                    }
                    if (!ExperimentContainerState.Finite(authorized) || authorized < 0 || authorized > seconds + 1e-8)
                        throw new InvalidOperationException("Stock returned invalid electrical seconds.");
                    return Math.Min(seconds, authorized);
                }
                finally
                {
                    foreach (var entry in before)
                        if (entry.Key.amount < entry.Value)
                            debits.Add(new KeyValuePair<PartResource, double>(entry.Key, entry.Value - entry.Key.amount));
                }
            }

            internal void Commit()
            {
                if (committed) return;
                foreach (var consumer in consumers)
                {
                    if (!owner.paid.TryGetValue(consumer.Key, out var history)) owner.paid.Add(consumer.Key, history = new List<Range>());
                    history.AddRange(ranges.Select(range => new Range(range.Start, range.End, consumer.Value)));
                }
                committed = true;
            }
            public void Dispose()
            {
                if (committed) return;
                foreach (var debit in debits) debit.Key.amount += debit.Value;
                debits.Clear();
            }
        }
    }
}

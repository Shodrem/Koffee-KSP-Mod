using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ShoAerospace.Koffee
{
    // Only explicitly configured Koffee labs opt in. Stock/foreign MPLs retain
    // their unmodified recipes. A marker context prevents recursive lab hooks.
    internal static class StationLaboratoryPower
    {
        private sealed class Observation
        {
            internal object? Proto;
            internal bool Applies, IntervalApplies;
            internal double UT, Start;
        }
        private static ConditionalWeakTable<ModuleScienceConverter, Observation> observations =
            new ConditionalWeakTable<ModuleScienceConverter, Observation>();
        private static Game? game;
        internal static void Clear() => observations = new ConditionalWeakTable<ModuleScienceConverter, Observation>();
        internal static bool Enabled(PartModule? module) => module is ModuleScienceConverter &&
            module.GetType() == typeof(ModuleScienceConverter) && module.part != null &&
            module.part.Modules.OfType<ModuleKoffeeLab>().Any() && StationStockPower.Rate(module.part, true) > 0;
        internal static void Observe(ModuleScienceConverter? converter)
        {
            if (converter == null || !Enabled(converter)) return;
            if (!ReferenceEquals(game, HighLogic.CurrentGame)) { Clear(); game = HighLogic.CurrentGame; }
            double now = Planetarium.GetUniversalTime();
            bool applies = StationPowerPolicy.Applies(converter.vessel);
            observations.TryGetValue(converter, out var previous);
            bool interval = applies && previous != null && previous.Applies && now >= previous.UT &&
                ReferenceEquals(previous.Proto, converter.vessel.protoVessel);
            observations.Remove(converter);
            observations.Add(converter, new Observation { Proto = converter.vessel.protoVessel, Applies = applies,
                IntervalApplies = interval, UT = now, Start = LabPatches.UpdateEndUT(converter) });
        }
        internal static void OnWarp(Vessel vessel)
        {
            if (!StationPowerPolicy.HighWarp || StationDbsIntegration.Active) return;
            foreach (var converter in vessel.parts.SelectMany(p => p.Modules.OfType<ModuleScienceConverter>()))
                if (observations.TryGetValue(converter, out var previous))
                { previous.Applies = false; previous.IntervalApplies = false; }
        }
        internal static double RetentionFloor(Vessel vessel, double now) => vessel.parts
            .Where(p => p != null && p.State != PartStates.DEAD)
            .SelectMany(p => p.Modules.OfType<ModuleScienceConverter>())
            .Where(c => Enabled(c) && c.IsActivated && c.isEnabled && c.isActiveAndEnabled)
            .Select(c => observations.TryGetValue(c, out var observation) ? observation.Start : now)
            .DefaultIfEmpty(now).Min();

        internal static ConversionRecipe Recipe(ConversionRecipe original, PartModule caller)
        {
            if (!Enabled(caller)) return original;
            var recipe = new ConversionRecipe { FillAmount = original.FillAmount, TakeAmount = original.TakeAmount };
            recipe.Inputs.AddRange(original.Inputs.Where(r => r.ResourceName != "ElectricCharge"));
            recipe.Outputs.AddRange(original.Outputs);
            recipe.Requirements.AddRange(original.Requirements);
            return recipe;
        }

        internal static Lease Begin(ModuleScienceConverter converter, double start, double end, double interval, bool processable)
        {
            if (!ExperimentContainerState.Finite(start) || !ExperimentContainerState.Finite(end) || start < 0 || end < start)
                throw new ArgumentException("Invalid stock laboratory interval.");
            bool applies = StationPowerPolicy.Applies(converter.vessel) && observations.TryGetValue(converter, out var observed) && observed.IntervalApplies;
            var marker = converter.part.Modules.OfType<ModuleKoffeePowerConsumer>().FirstOrDefault();
            // Missing new MODULE in an old craft still uses the live lab as an
            // inert context for an inner EC-only recipe (not the science converter).
            PartModule context = marker ?? (PartModule)converter.part.Modules.OfType<ModuleKoffeeLab>().First();
            var payment = StationStockPower.For(context, applies, end).BeginLaboratory(converter.part, context);
            try
            {
                // With DBS, an unobserved load/mode gap is rebased, never free work.
                bool unobserved = StationDbsIntegration.Active && !applies;
                double seconds = processable && !unobserved ? payment.Authorize(start, start + Math.Min(end - start, Math.Max(0, interval))) : 0;
                return new Lease(payment, start, seconds);
            }
            catch { payment.Dispose(); throw; }
        }

        internal sealed class Lease : IDisposable
        {
            private readonly StationStockPower.Transaction transaction;
            internal double Start { get; }
            internal double Seconds { get; }
            internal double End => Start + Seconds;
            internal bool Blocked => transaction.Blocked;
            internal Lease(StationStockPower.Transaction transaction, double start, double seconds)
            { this.transaction = transaction; Start = start; Seconds = seconds; }
            internal void Commit() => transaction.Commit();
            public void Dispose() => transaction.Dispose();
        }
    }
}

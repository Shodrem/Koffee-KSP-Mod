using System;
using System.Globalization;

namespace ShoAerospace.Koffee
{
    // Optional consumable-filter contract, independent of the maintainable V4.
    // Time advances only in the coordinator, with the service on and crew demand.
    internal sealed class KomfortServiceLife
    {
        internal double Remaining;
        private readonly Action<double> store;
        private KomfortServiceLife(double remaining, Action<double> store) { Remaining = remaining; this.store = store; }
        internal static double RemainingFor(ModuleKomfortService module, ConfigNode? saved = null)
        {
            if (!(module.lifetimeSeconds > 0) || !ExperimentContainerState.Finite(module.lifetimeSeconds)) return double.PositiveInfinity;
            double remaining = module.lifeRemainingSeconds;
            if (saved != null && saved.HasValue(nameof(module.lifeRemainingSeconds)) &&
                !double.TryParse(saved.GetValue(nameof(module.lifeRemainingSeconds)), NumberStyles.Float, CultureInfo.InvariantCulture, out remaining)) return 0;
            if (remaining == -1) return module.lifetimeSeconds;
            return ExperimentContainerState.Finite(remaining) && remaining >= 0 ? Math.Min(remaining, module.lifetimeSeconds) : 0;
        }
        internal static KomfortServiceLife? Capture(ModuleKomfortService module, ConfigNode? saved = null, Action<double>? write = null)
        {
            double remaining = RemainingFor(module, saved);
            if (double.IsPositiveInfinity(remaining)) return null;
            return new KomfortServiceLife(remaining, value =>
            {
                if (write != null) { write(value); return; }
                if (saved == null) module.lifeRemainingSeconds = value;
                else saved.SetValue(nameof(module.lifeRemainingSeconds), value.ToString("R", CultureInfo.InvariantCulture), true);
            });
        }
        internal void Spend(double seconds)
        {
            if (seconds <= 0 || !ExperimentContainerState.Finite(seconds)) return;
            Remaining = Math.Max(0, Remaining - seconds); store(Remaining);
        }
    }
}

namespace ShoAerospace.Koffee
{
    internal enum KomfortAvailability { NoData, Disabled, NotApplicable, Current, LastKnown }

    internal readonly struct KomfortStatus
    {
        internal readonly KomfortAvailability Availability;
        internal readonly double? Value;
        internal readonly double? ObservedUT;

        internal KomfortStatus(KomfortAvailability availability, double? value = null, double? observedUT = null)
        {
            Availability = availability;
            Value = (availability == KomfortAvailability.Current || availability == KomfortAvailability.LastKnown) &&
                value.HasValue && ExperimentContainerState.Finite(value.Value) && value.Value >= 0 && value.Value <= 1 ? value : null;
            ObservedUT = Value.HasValue ? observedUT : null;
        }
    }
}

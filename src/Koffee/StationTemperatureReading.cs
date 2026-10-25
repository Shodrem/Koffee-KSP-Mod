using System;

namespace ShoAerospace.Koffee
{
    // An observed instrument value, independent of the science subject and work budget.
    internal sealed class StationTemperatureReading
    {
        internal string PartName { get; }
        internal uint PartId { get; }
        internal double UT { get; }
        internal double Kelvin { get; }

        internal StationTemperatureReading(string partName, uint partId, double ut, double kelvin)
        {
            if (string.IsNullOrWhiteSpace(partName) || !ExperimentContainerState.Finite(ut) || ut < 0 ||
                !ExperimentContainerState.Finite(kelvin) || kelvin <= 0)
                throw new ArgumentException("Invalid initial instrument temperature reading.");
            PartName = partName;
            PartId = partId;
            UT = ut;
            Kelvin = kelvin;
        }
    }
}

using System;

namespace ShoAerospace.Koffee
{
    // An observed instrument value, independent of the science subject and work budget.
    internal sealed class StationPressureReading
    {
        internal string PartName { get; }
        internal uint PartId { get; }
        internal double UT { get; }
        internal double Kpa { get; }

        internal StationPressureReading(string partName, uint partId, double ut, double kpa)
        {
            if (string.IsNullOrWhiteSpace(partName) || !ExperimentContainerState.Finite(ut) || ut < 0 ||
                !ExperimentContainerState.Finite(kpa) || kpa < 0)
                throw new ArgumentException("Invalid initial static pressure reading.");
            PartName = partName;
            PartId = partId;
            UT = ut;
            Kpa = kpa;
        }
    }
}

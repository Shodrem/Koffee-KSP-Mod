using System.Globalization;
using System.Linq;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    internal static class StationExperimentReadings
    {
        internal static StationTemperatureReading? Capture(StationExperimentDefinition definition,
            Vessel vessel, Part container, double now)
        {
            if (definition.ResultReading != StationResultReading.RequiredPartTemperature || vessel?.loaded != true)
                return null;
            Part? instrument = RequiredInstrument(definition, vessel, container);
            if (instrument == null || !ExperimentContainerState.Finite(instrument.temperature) || instrument.temperature <= 0 ||
                !ExperimentContainerState.Finite(now) || now < 0) return null;
            return new StationTemperatureReading(instrument.partInfo.name, instrument.persistentId, now, instrument.temperature);
        }

        internal static StationPressureReading? CapturePressure(StationExperimentDefinition definition,
            Vessel vessel, Part container, double now)
        {
            if (definition.ResultReading != StationResultReading.RequiredPartPressure || vessel?.loaded != true)
                return null;
            Part? instrument = RequiredInstrument(definition, vessel, container);
            double pressure = vessel.staticPressurekPa;
            if (instrument == null || !ExperimentContainerState.Finite(pressure) || pressure < 0 ||
                !ExperimentContainerState.Finite(now) || now < 0) return null;
            return new StationPressureReading(instrument.partInfo.name, instrument.persistentId, now, pressure);
        }

        internal static Part? RequiredInstrument(StationExperimentDefinition definition, Vessel vessel, Part container)
        {
            AvailablePart? required = StationExperimentCatalogLoader.EquipmentPart(definition.RequiredPartName);
            if (required == null) return null;
            return vessel.parts.Where(p => p != null && p.State != PartStates.DEAD &&
                !ReferenceEquals(p, container) && ReferenceEquals(p.vessel, vessel) && ReferenceEquals(p.partInfo, required))
                .OrderBy(p => p.persistentId).ThenBy(p => p.flightID).FirstOrDefault();
        }

        internal static string ResultText(ExperimentContainerState state, StationExperimentDefinition definition)
        {
            if (state.SolarCapture != null) return StationSolarAcquisition.ResultText(state.SolarCapture);
            if (StationSolarAcquisition.Required(definition)) return StationSolarAcquisition.Error("NotReady");
            if (state.SurfaceCapture != null) return StationSurfaceAcquisition.ResultText(state.SurfaceCapture);
            if (StationSurfaceCapture.Required(definition.ResultReading))
                return Localizer.Format("#Koffee_Station_Pause_InitialCaptureUnavailable");
            var pressure = state.PressureReading;
            if (pressure != null) return Localizer.Format("#Koffee_Reading_Pressure_Initial",
                pressure.Kpa.ToString("G8", CultureInfo.CurrentCulture));
            if (definition.ResultReading == StationResultReading.RequiredPartPressure && state.TemperatureReading == null)
                return Localizer.Format("#Koffee_Reading_Pressure_Unavailable");
            var reading = state.TemperatureReading;
            if (reading == null) return definition.ResultReading == StationResultReading.RequiredPartTemperature ?
                Localizer.Format("#Koffee_Reading_Temperature_Unavailable") : "";
            return Localizer.Format("#Koffee_Reading_Temperature_Initial",
                reading.Kelvin.ToString("F2", CultureInfo.CurrentCulture),
                (reading.Kelvin - 273.15).ToString("F2", CultureInfo.CurrentCulture));
        }
    }
}

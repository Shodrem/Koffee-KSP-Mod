using System;
using System.Globalization;
using System.Linq;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    internal static class StationSolarAcquisition
    {
        internal static bool Required(StationExperimentDefinition definition) => definition.ResultReading == StationResultReading.SolarIrradiance ||
            StationRadiationPressureAcquisition.Required(definition);
        internal static string Readiness(StationExperimentDefinition definition, Vessel vessel, Part container, double now, bool requireOpen = true)
        {
            if (!Required(definition)) return "";
            if (StationRadiationPressureAcquisition.Required(definition)) return StationRadiationPressureAcquisition.Readiness(definition,vessel,container,now);
            if (!ExperimentContainerState.Finite(now) || now < 0 || vessel?.mainBody == null ||
                string.IsNullOrWhiteSpace(vessel.mainBody.bodyDisplayName)) return "NotReady";
            var instrument = StationExperimentReadings.RequiredInstrument(definition, vessel, container);
            if (instrument == null || instrument.persistentId == 0 || definition.RequiredPartName != StationSolarCapture.Equipment) return "Equipment";
            if (!StationSolarPublication.TryRead(vessel, now, out double flux, out bool sunlight)) return "NotReady";
            if (!sunlight && flux == 0) return "NoLight";
            if (!ExperimentContainerState.Finite(flux) || flux < 0 || (!sunlight && flux > 0) || (sunlight && flux == 0)) return "NotReady";
            if (requireOpen && !LidOpen(instrument)) return "LidClosed";
            return "";
        }
        internal static bool LidOpen(Part instrument)
        {
            return ModuleKoffeeExperimentAnimation.Find(instrument, StationExperimentAnimationPreparation.SolarClip)?.Open == true;
        }
        internal static bool TryCapture(StationExperimentDefinition definition, Vessel vessel, Part container, Guid runId,
            double now, string body, string situation, out StationSolarCapture? capture, out string error)
        {
            capture = null; error = "";
            if (!Required(definition)) return true;
            if (StationRadiationPressureAcquisition.Required(definition))
                return StationRadiationPressureAcquisition.TryCapture(definition,vessel,container,runId,now,body,situation,out capture,out error);
            string reason = Readiness(definition, vessel, container, now);
            if (reason != "") { error = Error(reason); return false; }
            var instrument = StationExperimentReadings.RequiredInstrument(definition, vessel, container);
            if (vessel.mainBody == null || body != vessel.mainBody.bodyName || situation != StationExperimentRuntime.CurrentSituation(vessel) ||
                (situation != "InSpaceLow" && situation != "InSpaceHigh") || !definition.AllowsBody(body))
            { error = Error("NotReady"); return false; }
            // Re-read at the commit boundary: a PAW readiness result never authorizes a start.
            if (instrument == null || Readiness(definition, vessel, container, now) != "" ||
                !ReferenceEquals(instrument, StationExperimentReadings.RequiredInstrument(definition, vessel, container)) || !LidOpen(instrument) ||
                !StationSolarPublication.TryRead(vessel, now, out double flux, out bool light, out string source, out bool stockLight) || !light || flux <= 0)
            { error = Error(Readiness(definition, vessel, container, now)); return false; }
            capture = new StationSolarCapture(runId, now, body, vessel.mainBody.bodyDisplayName, situation, instrument.persistentId, flux, source, stockLight);
            return true;
        }
        internal static string Error(string reason) => Localizer.Format(reason.StartsWith("RPA_",StringComparison.Ordinal) ?
            "#Koffee_RPA_Error_" + reason.Substring(4) : "#Koffee_Solar_Error_" + (reason == "" ? "NotReady" : reason));
        internal static bool Matches(ExperimentContainerState state, StationExperimentDefinition definition) => Required(definition) ?
            state.SolarCapture != null && state.SolarCapture.RunId == state.RunId && state.SolarCapture.UT == state.StartUT &&
            definition.RequiredPartName == state.SolarCapture.EquipmentName &&
            StationRadiationPressureAcquisition.Required(definition) == (state.SolarCapture.RadiationPressure != null) &&
            definition.AllowsBody(state.SolarCapture.Body) : state.SolarCapture == null;
        internal static string ResultText(StationSolarCapture capture)
        {
            var body = FlightGlobals.GetBodyByName(capture.Body);
            string name = Localizer.Format(body?.bodyDisplayName ?? capture.BodyDisplayName).LocalizeRemoveGender();
            if (capture.RadiationPressure != null) return StationRadiationPressureAcquisition.ResultText(capture,name);
            return Localizer.Format("#LOC_Koffee_SolarRadiometry_Result", name) + "\n\n" +
                Localizer.Format("#Koffee_Solar_Reading", capture.Flux.ToString("G10", CultureInfo.CurrentCulture),
                    Localizer.Format("#Koffee_Environment_Situation_" + capture.Situation), capture.UT.ToString("G17", CultureInfo.CurrentCulture));
        }
    }
}

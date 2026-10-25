using System;

namespace ShoAerospace.Koffee
{
    internal sealed class StationSolarCapture
    {
        internal const string Equipment = "SHOSolarRadiometerRSA";
        internal Guid RunId { get; }
        internal double UT { get; }
        internal string Body { get; }
        internal string BodyDisplayName { get; }
        internal string Situation { get; }
        internal uint PartId { get; }
        internal double Flux { get; }
        internal string Source { get; }
        internal bool StockDirectSunlight { get; }
        internal StationRadiationPressure? RadiationPressure { get; }
        internal string EquipmentName => RadiationPressure == null ? Equipment : StationRadiationPressure.Equipment;
        internal StationSolarCapture(Guid runId, double ut, string body, string displayName,
            string situation, uint partId, double flux, string source = "StockFlightIntegrator", bool stockDirectSunlight = true)
            : this(runId,ut,body,displayName,situation,partId,flux,source,stockDirectSunlight,null) { }
        internal StationSolarCapture(Guid runId, double ut, string body, string displayName,
            string situation, uint partId, double flux, string source, bool stockDirectSunlight,
            StationRadiationPressure? radiationPressure)
        {
            if (runId == Guid.Empty || !ExperimentContainerState.Finite(ut) || ut < 0 ||
                string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(displayName) ||
                (situation != "InSpaceLow" && situation != "InSpaceHigh") || partId == 0 ||
                !ExperimentContainerState.Finite(flux) || flux <= 0 ||
                (source != "StockFlightIntegrator" && source != "KopernicusAggregate") ||
                (source == "StockFlightIntegrator" && !stockDirectSunlight)) throw new ArgumentException("Invalid solar capture.");
            RunId = runId; UT = ut; Body = body; BodyDisplayName = displayName;
            Situation = situation; PartId = partId; Flux = flux;
            Source = source; StockDirectSunlight = stockDirectSunlight;
            if (radiationPressure != null && (!stockDirectSunlight || !radiationPressure.MatchesFlux(flux)))
                throw new ArgumentException("Invalid initial radiation pressure estimate.");
            RadiationPressure = radiationPressure;
        }
    }

    // Immutable initial estimate, separate from atmospheric pressure and mechanical force.
    // Saved results are restored verbatim; consistency checks never replace them.
    internal sealed class StationRadiationPressure
    {
        internal const string Equipment = "SHORadiationPressureRPA";
        internal const string Model = "IdealNormalIncidenceV1";
        internal const string LightTest = "SingleStarFiveApertureRaysV1";
        internal const double LightSpeed = 299792458;
        internal double AbsorbingPa { get; }
        internal double ReflectingPa { get; }
        internal string Star { get; }
        internal double IncidenceCosine { get; }
        internal StationRadiationPressure(double absorbingPa, double reflectingPa, string star, double incidenceCosine)
        {
            if (!ExperimentContainerState.Finite(absorbingPa) || absorbingPa <= 0 ||
                !ExperimentContainerState.Finite(reflectingPa) || reflectingPa <= 0 ||
                !ExperimentContainerState.Finite(incidenceCosine) || incidenceCosine <= 0 || incidenceCosine > 1 ||
                string.IsNullOrWhiteSpace(star) || reflectingPa != 2 * absorbingPa)
                throw new ArgumentException("Invalid radiation pressure estimate.");
            AbsorbingPa=absorbingPa; ReflectingPa=reflectingPa; Star=star; IncidenceCosine=incidenceCosine;
        }
        internal bool MatchesFlux(double flux) => AbsorbingPa == flux / LightSpeed;
        internal static StationRadiationPressure Calculate(double flux, string star, double cosine)
        {
            double absorb=flux/LightSpeed;
            return new StationRadiationPressure(absorb,2*absorb,star,cosine);
        }
    }
}

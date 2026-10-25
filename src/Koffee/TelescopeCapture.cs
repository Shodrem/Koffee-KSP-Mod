using System;

namespace ShoAerospace.Koffee
{
    // An acquisition, independent of the viewer and of the later research run.
    // Optical coordinates: +X right, +Y up, +Z forward; metres at one instant.
    public sealed class TelescopeCapture
    {
        public Guid ObservationId { get; }
        public string Target { get; }
        internal TelescopePairEvidence? Pair { get; }
        public string CompanionPlanet => Pair?.Planet ?? "";
        public int AcquiredSampleCount => 1;
        public string ObserverBody { get; }
        public string ObserverSituation { get; }
        public Guid VesselId { get; }
        public uint InstrumentId { get; }
        public double UT { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double Radius { get; }
        public double FieldOfView { get; }
        public double MaximumError { get; }
        public double FieldFraction { get; }
        public double MinimumPixels { get; }
        public double Distance => Math.Sqrt(X * X + Y * Y + Z * Z);
        public double AngularRadius => Math.Asin(Radius / Distance) * 180 / Math.PI;
        public double PointingError => Math.Atan2(Math.Sqrt(X * X + Y * Y), Z) * 180 / Math.PI;
        public double Tolerance => TelescopeOptics.CaptureTolerance(FieldOfView, AngularRadius, MaximumError, FieldFraction);
        internal const string ScienceSituation = "InSpaceHigh";

        internal TelescopeCapture(string target, string observerBody, string observerSituation,
            Guid vesselId, uint instrumentId, double ut, double x, double y, double z,
            double radius, double fieldOfView, double maximumError, double fieldFraction, double minimumPixels, Guid? observationId = null, TelescopePairEvidence? pair = null)
        {
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(observerBody) ||
                (observerSituation != "InSpaceLow" && observerSituation != "InSpaceHigh") ||
                vesselId == Guid.Empty || instrumentId == 0 || !Finite(ut) || ut < 0 ||
                !Finite(x) || !Finite(y) || !Finite(z) || !Finite(radius) || radius <= 0)
                throw new ArgumentException("Invalid telescope acquisition identity or geometry.");
            ObservationId = observationId ?? Guid.NewGuid();
            if (ObservationId == Guid.Empty) throw new ArgumentException("Invalid observation identity.");
            Target = target; ObserverBody = observerBody; ObserverSituation = observerSituation;
            VesselId = vesselId; InstrumentId = instrumentId; UT = ut;
            X = x; Y = y; Z = z; Radius = radius; FieldOfView = fieldOfView;
            MaximumError = maximumError; FieldFraction = fieldFraction; MinimumPixels = minimumPixels;
            Pair = pair;
            if (pair != null && (pair.Planet == target || pair.Sample.UT != ut ||
                pair.Sample.MX != x || pair.Sample.MY != y || pair.Sample.MZ != z ||
                pair.Sample.MoonRadius != radius || pair.Sample.Fov != fieldOfView))
                throw new ArgumentException("Planet/moon endpoint does not match capture.");
            if (!Finite(Distance) || Distance <= radius ||
                !TelescopeOptics.CaptureSettingsValid(fieldOfView, maximumError, fieldFraction, minimumPixels) ||
                (pair == null && TelescopeOptics.CaptureFailure(fieldOfView, AngularRadius, PointingError,
                    maximumError, fieldFraction, minimumPixels) != ""))
                throw new ArgumentException("Telescope acquisition is outside the optical limits.");
        }

        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    }
}

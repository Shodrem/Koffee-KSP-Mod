using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal sealed class StationObservedBiome
    {
        internal string Name { get; }
        internal string LocalizationTag { get; }
        internal string DisplayName { get; }
        internal StationObservedBiome(string name, string localizationTag, string displayName)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));
            Name = name; LocalizationTag = localizationTag ?? "";
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        }
    }

    // One immutable acquisition per run. An empty biome catalog is a successful
    // narrative capture; a missing capture is never a zero-metre terrain sample.
    internal sealed class StationSurfaceCapture
    {
        internal StationResultReading Mode { get; }
        internal string Body { get; }
        internal string BodyDisplayName { get; }
        internal string PartName { get; }
        internal uint PartId { get; }
        internal double UT { get; }
        internal IReadOnlyList<StationObservedBiome> Biomes { get; }
        internal double? Latitude { get; }
        internal double? Longitude { get; }
        internal double? Height { get; }
        internal double? ReferenceRadius { get; }
        internal const string TerrainSource = "PQS";

        internal static bool Required(StationResultReading mode) =>
            mode == StationResultReading.SurfaceBiomeObservation || mode == StationResultReading.SurfaceTerrainAltitude;

        internal StationSurfaceCapture(StationResultReading mode, string body, string bodyDisplayName,
            string partName, uint partId, double ut, IEnumerable<StationObservedBiome> biomes,
            double? latitude = null, double? longitude = null, double? height = null, double? referenceRadius = null)
        {
            if (!Required(mode) || string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(partName) ||
                !ExperimentContainerState.Finite(ut) || ut < 0) throw new ArgumentException("Invalid surface capture identity.");
            var zones = biomes.ToArray();
            if (zones.Any(b => b == null) || zones.Length > 3 || zones.Select(b => b.Name).Distinct(StringComparer.Ordinal).Count() != zones.Length)
                throw new ArgumentException("Invalid narrative biome selection.");
            if (mode == StationResultReading.SurfaceTerrainAltitude)
            {
                if (zones.Length != 0 || !Valid(latitude) || latitude < -90 || latitude > 90 ||
                    !Valid(longitude) || longitude < -180 || longitude > 180 || !Valid(height) ||
                    !Valid(referenceRadius) || referenceRadius <= 0 || !Valid(referenceRadius + height) || referenceRadius + height <= 0)
                    throw new ArgumentException("Invalid initial PQS terrain sample.");
            }
            else if (latitude.HasValue || longitude.HasValue || height.HasValue || referenceRadius.HasValue)
                throw new ArgumentException("Narrative regions have no measured coordinates.");
            Mode = mode; Body = body; BodyDisplayName = string.IsNullOrWhiteSpace(bodyDisplayName) ? body : bodyDisplayName;
            PartName = partName; PartId = partId; UT = ut;
            Biomes = new ReadOnlyCollection<StationObservedBiome>(zones);
            Latitude = latitude; Longitude = longitude; Height = height; ReferenceRadius = referenceRadius;
        }

        private static bool Valid(double? value) => value.HasValue && ExperimentContainerState.Finite(value.Value);
    }
}

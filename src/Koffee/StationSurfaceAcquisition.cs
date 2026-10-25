using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class StationSurfaceAcquisition
    {
        private static readonly System.Random random = new System.Random();

        internal static bool TryCapture(StationExperimentDefinition definition, Vessel vessel, Part container,
            double now, out StationSurfaceCapture? capture, out string error)
        {
            capture = null; error = "";
            if (!StationSurfaceCapture.Required(definition.ResultReading)) return true;
            string reason = "Loaded";
            try
            {
                if (vessel?.loaded != true) return Fail(reason, out error);
                reason = "Context";
                CelestialBody body = vessel.mainBody;
                if (body == null || !body.hasSolidSurface || body.isStar ||
                    ScienceUtil.GetExperimentSituation(vessel) != ExperimentSituations.InSpaceLow ||
                    !definition.AllowsBody(body.bodyName)) return Fail(reason, out error);
                reason = "Equipment";
                Part? instrument = StationExperimentReadings.RequiredInstrument(definition, vessel, container);
                if (instrument == null) return Fail(reason, out error);
                if (definition.ResultReading == StationResultReading.SurfaceBiomeObservation)
                {
                    // Stock metadata only: this is the user's narrative abstraction,
                    // not a footprint or a claim about the vessel's orbital track.
                    var attributes = body.BiomeMap?.Attributes;
                    var zones = attributes == null ? Array.Empty<StationObservedBiome>() : attributes
                        .Where(a => a != null && !string.IsNullOrWhiteSpace(a.name))
                        .GroupBy(a => a.name, StringComparer.Ordinal).Select(g => g.First())
                        .Select(a => new StationObservedBiome(a.name, a.localizationTag,
                            ScienceUtil.GetBiomedisplayName(body, a.name))).ToArray();
                    capture = new StationSurfaceCapture(definition.ResultReading, body.bodyName, body.bodyDisplayName,
                        instrument.partInfo.name, instrument.persistentId, now, SelectBiomes(zones, random));
                }
                else
                {
                    reason = "Terrain";
                    if (body.pqsController == null) return Fail(reason, out error);
                    double latitude = vessel.latitude, longitude = vessel.longitude;
                    if (!ExperimentContainerState.Finite(latitude) || latitude < -90 || latitude > 90 ||
                        !ExperimentContainerState.Finite(longitude)) return Fail(reason, out error);
                    longitude = ((longitude + 180) % 360 + 360) % 360 - 180;
                    double radius = body.pqsController.radius;
                    if (!ExperimentContainerState.Finite(radius) || radius <= 0) return Fail(reason, out error);
                    double height = body.TerrainAltitude(latitude, longitude, true);
                    capture = new StationSurfaceCapture(definition.ResultReading, body.bodyName, body.bodyDisplayName,
                        instrument.partInfo.name, instrument.persistentId, now, Array.Empty<StationObservedBiome>(),
                        latitude, longitude, height, radius);
                }
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[Koffee] Initial surface capture failed: " + exception.Message);
                return Fail(reason, out error);
            }
        }

        internal static StationObservedBiome[] SelectBiomes(IEnumerable<StationObservedBiome> available, System.Random rng)
        {
            var pool = available.GroupBy(b => b.Name, StringComparer.Ordinal).Select(g => g.First()).ToArray();
            int count = Math.Min(3, pool.Length);
            for (int i = 0; i < count; i++)
            {
                int selected = rng.Next(i, pool.Length);
                var swap = pool[i]; pool[i] = pool[selected]; pool[selected] = swap;
            }
            return pool.Take(count).ToArray();
        }

        internal static bool Matches(ExperimentContainerState state, StationExperimentDefinition definition) =>
            (!StationSurfaceCapture.Required(definition.ResultReading) && state.SurfaceCapture == null) ||
            (state.SurfaceCapture != null && state.SurfaceCapture.Mode == definition.ResultReading &&
             state.SurfaceCapture.Body == state.BoundBody && state.SurfaceCapture.PartName == definition.RequiredPartName &&
             state.BoundSituation == "InSpaceLow" && state.SurfaceCapture.UT == state.StartUT);

        internal static string ResultText(StationSurfaceCapture capture)
        {
            if (capture.Mode == StationResultReading.SurfaceBiomeObservation)
            {
                var names = capture.Biomes.Select(BiomeName).ToArray();
                return Localizer.Format("#Koffee_Surface_Observation_" + names.Length.ToString(CultureInfo.InvariantCulture), names);
            }
            return Localizer.Format("#Koffee_Surface_Altimetry_Initial",
                capture.Height!.Value.ToString("F2", CultureInfo.CurrentCulture),
                capture.ReferenceRadius!.Value.ToString("G17", CultureInfo.CurrentCulture),
                capture.BodyDisplayName.LocalizeRemoveGender(),
                capture.Latitude!.Value.ToString("F5", CultureInfo.CurrentCulture),
                capture.Longitude!.Value.ToString("F5", CultureInfo.CurrentCulture));
        }

        private static string BiomeName(StationObservedBiome biome)
        {
            string localized = string.IsNullOrWhiteSpace(biome.LocalizationTag) ? "" : Localizer.Format(biome.LocalizationTag);
            return (string.IsNullOrWhiteSpace(localized) || localized.StartsWith("#", StringComparison.Ordinal) ?
                biome.DisplayName : localized).LocalizeRemoveGender();
        }
        private static bool Fail(string reason, out string error)
        { error = Localizer.Format("#Koffee_Surface_Error_" + reason); return false; }
    }
}

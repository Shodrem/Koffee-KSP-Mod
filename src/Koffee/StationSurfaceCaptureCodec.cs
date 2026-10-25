using System;
using System.Globalization;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal static class StationSurfaceCaptureCodec
    {
        internal const string NodeName = "KOFFEE_SURFACE_CAPTURE";
        private static readonly string[] Identity = { "version", "mode", "body", "bodyDisplayName", "part", "partId", "ut" };
        private static readonly string[] Terrain = { "latitude", "longitude", "height", "referenceRadius", "source" };

        internal static bool Read(ConfigNode parent, double startUT, string body, out StationSurfaceCapture? capture)
        {
            capture = null;
            var nodes = parent.GetNodes(NodeName);
            if (nodes.Length == 0) return true;
            if (nodes.Length != 1) return false;
            ConfigNode node = nodes[0];
            if (Identity.Any(k => node.GetValues(k).Length != 1) || node.GetValue("version") != "1" ||
                !Enum.TryParse(node.GetValue("mode"), false, out StationResultReading mode) ||
                mode.ToString() != node.GetValue("mode") || !StationSurfaceCapture.Required(mode) ||
                !uint.TryParse(node.GetValue("partId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint partId) ||
                !Number(node, "ut", out double ut) || ut != startUT || node.GetValue("body") != body) return false;
            try
            {
                var biomes = node.GetNodes("BIOME");
                if (biomes.Any(b => new[] { "name", "localizationTag", "displayName" }.Any(k => b.GetValues(k).Length != 1))) return false;
                var zones = biomes.Select(b => new StationObservedBiome(b.GetValue("name"), b.GetValue("localizationTag"), b.GetValue("displayName"))).ToArray();
                double? lat = null, lon = null, height = null, radius = null;
                if (mode == StationResultReading.SurfaceTerrainAltitude)
                {
                    if (Terrain.Any(k => node.GetValues(k).Length != 1) || node.GetValue("source") != StationSurfaceCapture.TerrainSource ||
                        !Number(node, "latitude", out double a) || !Number(node, "longitude", out double o) ||
                        !Number(node, "height", out double h) || !Number(node, "referenceRadius", out double r)) return false;
                    lat = a; lon = o; height = h; radius = r;
                }
                else if (Terrain.Any(node.HasValue)) return false;
                capture = new StationSurfaceCapture(mode, node.GetValue("body"), node.GetValue("bodyDisplayName"),
                    node.GetValue("part"), partId, ut, zones, lat, lon, height, radius);
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        internal static void Write(ConfigNode parent, StationSurfaceCapture? capture)
        {
            parent.RemoveNodes(NodeName);
            if (capture == null) return;
            ConfigNode node = parent.AddNode(NodeName);
            node.AddValue("version", "1"); node.AddValue("mode", capture.Mode.ToString());
            node.AddValue("body", capture.Body); node.AddValue("bodyDisplayName", capture.BodyDisplayName);
            node.AddValue("part", capture.PartName); node.AddValue("partId", capture.PartId.ToString(CultureInfo.InvariantCulture));
            node.AddValue("ut", capture.UT.ToString("R", CultureInfo.InvariantCulture));
            foreach (var zone in capture.Biomes)
            {
                var biome = node.AddNode("BIOME"); biome.AddValue("name", zone.Name);
                biome.AddValue("localizationTag", zone.LocalizationTag); biome.AddValue("displayName", zone.DisplayName);
            }
            if (capture.Mode != StationResultReading.SurfaceTerrainAltitude) return;
            node.AddValue("source", StationSurfaceCapture.TerrainSource);
            node.AddValue("latitude", capture.Latitude!.Value.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("longitude", capture.Longitude!.Value.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("height", capture.Height!.Value.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("referenceRadius", capture.ReferenceRadius!.Value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static bool Number(ConfigNode node, string key, out double value) =>
            double.TryParse(node.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && ExperimentContainerState.Finite(value);
    }
}

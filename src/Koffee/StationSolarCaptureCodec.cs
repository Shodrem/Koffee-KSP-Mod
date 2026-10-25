using System;
using System.Globalization;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal static class StationSolarCaptureCodec
    {
        internal const string NodeName = "KOFFEE_SOLAR_CAPTURE";
        private static readonly string[] Keys = { "version", "runId", "ut", "body", "bodyDisplayName", "situation", "part", "partId", "flux", "directSunlight", "lidOpen", "source" };
        private static readonly string[] PressureKeys = { "opticalModel", "lightTest", "lightSpeed", "star", "incidenceCosine", "absorbingPa", "reflectingPa" };
        internal static bool Read(ConfigNode parent, Guid runId, double startUT, out StationSolarCapture? capture)
        {
            capture = null;
            var nodes = parent.GetNodes(NodeName);
            if (nodes.Length == 0) return true;
            if (nodes.Length != 1) return false;
            var n = nodes[0];
            bool pressure = n.GetValue("version") == "2";
            if (Keys.Any(k => n.GetValues(k).Length != 1) || (!pressure && n.GetValue("version") != "1") ||
                n.GetValue("part") != (pressure ? StationRadiationPressure.Equipment : StationSolarCapture.Equipment) ||
                !bool.TryParse(n.GetValue("directSunlight"), out bool direct) || n.GetValue("lidOpen") != (pressure ? "NotApplicable" : "True") ||
                !Guid.TryParseExact(n.GetValue("runId"), "N", out var savedRun) || savedRun != runId ||
                !double.TryParse(n.GetValue("ut"), NumberStyles.Float, CultureInfo.InvariantCulture, out double ut) || ut != startUT ||
                !double.TryParse(n.GetValue("flux"), NumberStyles.Float, CultureInfo.InvariantCulture, out double flux) ||
                !uint.TryParse(n.GetValue("partId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id)) return false;
            StationRadiationPressure? estimate = null;
            if (pressure)
            {
                if (PressureKeys.Any(k => n.GetValues(k).Length != 1) ||
                    n.GetValue("opticalModel") != StationRadiationPressure.Model || n.GetValue("lightTest") != StationRadiationPressure.LightTest ||
                    n.GetValue("lightSpeed") != "299792458" ||
                    !double.TryParse(n.GetValue("absorbingPa"), NumberStyles.Float, CultureInfo.InvariantCulture, out double a) ||
                    !double.TryParse(n.GetValue("reflectingPa"), NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ||
                    !double.TryParse(n.GetValue("incidenceCosine"), NumberStyles.Float, CultureInfo.InvariantCulture, out double cosine)) return false;
                try { estimate = new StationRadiationPressure(a,r,n.GetValue("star"),cosine); }
                catch (ArgumentException) { return false; }
            }
            else if (PressureKeys.Any(k => n.HasValue(k))) return false;
            try { capture = new StationSolarCapture(savedRun, ut, n.GetValue("body"), n.GetValue("bodyDisplayName"), n.GetValue("situation"), id, flux, n.GetValue("source"), direct, estimate); return true; }
            catch (ArgumentException) { return false; }
        }
        internal static void Write(ConfigNode parent, StationSolarCapture? capture)
        {
            parent.RemoveNodes(NodeName);
            if (capture == null) return;
            var n = parent.AddNode(NodeName);
            n.AddValue("version", capture.RadiationPressure == null ? "1" : "2"); n.AddValue("source", capture.Source);
            n.AddValue("runId", capture.RunId.ToString("N")); n.AddValue("ut", capture.UT.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("body", capture.Body); n.AddValue("bodyDisplayName", capture.BodyDisplayName); n.AddValue("situation", capture.Situation);
            n.AddValue("part", capture.EquipmentName); n.AddValue("partId", capture.PartId.ToString(CultureInfo.InvariantCulture));
            n.AddValue("flux", capture.Flux.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("directSunlight", capture.StockDirectSunlight.ToString()); n.AddValue("lidOpen", capture.RadiationPressure == null ? "True" : "NotApplicable");
            var p=capture.RadiationPressure;
            if (p == null) return;
            n.AddValue("opticalModel",StationRadiationPressure.Model); n.AddValue("lightTest",StationRadiationPressure.LightTest);
            n.AddValue("lightSpeed","299792458"); n.AddValue("star",p.Star);
            n.AddValue("incidenceCosine",p.IncidenceCosine.ToString("R",CultureInfo.InvariantCulture));
            n.AddValue("absorbingPa",p.AbsorbingPa.ToString("R",CultureInfo.InvariantCulture));
            n.AddValue("reflectingPa",p.ReflectingPa.ToString("R",CultureInfo.InvariantCulture));
        }
    }
}

using System;
using System.Globalization;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal static class TelescopeCaptureCodec
    {
        internal const string NodeName = "KOFFEE_TELESCOPE_CAPTURE";
        private static readonly string[] keys = { "version", "id", "frame", "target", "observerBody", "observerSituation",
            "vesselId", "instrumentId", "ut", "x", "y", "z", "radius", "fov", "maximumError", "fieldFraction", "minimumPixels" };

        internal static bool Read(ConfigNode parent, out TelescopeCapture[] captures)
        {
            captures = Array.Empty<TelescopeCapture>();
            var result = new System.Collections.Generic.List<TelescopeCapture>();
            foreach (var node in parent.GetNodes(NodeName))
            {
                if (!ReadOne(node, out var capture) || result.Any(c => (c.Pair != null) != (capture!.Pair != null) ||
                    (capture.Pair != null && c.Target == capture.Target) || c.ObservationId == capture.ObservationId ||
                    (c.UT == capture!.UT && c.InstrumentId == capture.InstrumentId && c.VesselId == capture.VesselId)) ||
                    (result.Count > 0 && result[result.Count - 1].UT > capture!.UT)) return false;
                result.Add(capture!);
            }
            captures = result.ToArray(); return true;
        }

        private static bool ReadOne(ConfigNode node, out TelescopeCapture? capture)
        {
            capture = null;
            if (keys.Any(k => node.GetValues(k).Length != 1) || (node.GetValue("version") != "1" && node.GetValue("version") != "3") ||
                node.GetValue("frame") != "OpticalLocal" ||
                !Guid.TryParseExact(node.GetValue("id"), "N", out var id) ||
                !Guid.TryParseExact(node.GetValue("vesselId"), "N", out var vessel) ||
                !uint.TryParse(node.GetValue("instrumentId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint instrument)) return false;
            var numbers = new double[9];
            string[] names = { "ut", "x", "y", "z", "radius", "fov", "maximumError", "fieldFraction", "minimumPixels" };
            for (int i = 0; i < names.Length; ++i)
                if (!double.TryParse(node.GetValue(names[i]), NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i])) return false;
            try
            {
                var pair = node.GetValue("version") == "3" ? TelescopePairEvidence.Read(node) : null;
                // Version 2 occultation archives are intentionally unsupported and preserved raw by the module.
                if (node.GetNodes("OCCULTATION").Length != 0 ||
                    (pair == null && node.GetNodes(TelescopePairEvidence.Node).Length != 0)) return false;
                capture = new TelescopeCapture(node.GetValue("target"), node.GetValue("observerBody"), node.GetValue("observerSituation"),
                    vessel, instrument, numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5], numbers[6], numbers[7], numbers[8], id, pair);
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
        }

        internal static void Write(ConfigNode parent, System.Collections.Generic.IEnumerable<TelescopeCapture> captures)
        {
            parent.RemoveNodes(NodeName);
            foreach (var capture in captures) WriteOne(parent.AddNode(NodeName), capture);
        }

        private static void WriteOne(ConfigNode n, TelescopeCapture capture)
        {
            n.AddValue("id", capture.ObservationId.ToString("N"));
            n.AddValue("version", capture.Pair == null ? "1" : "3");
            capture.Pair?.Write(n); n.AddValue("frame", "OpticalLocal"); n.AddValue("target", capture.Target);
            n.AddValue("observerBody", capture.ObserverBody); n.AddValue("observerSituation", capture.ObserverSituation);
            n.AddValue("vesselId", capture.VesselId.ToString("N"));
            n.AddValue("instrumentId", capture.InstrumentId.ToString(CultureInfo.InvariantCulture));
            Add(n, "ut", capture.UT); Add(n, "x", capture.X); Add(n, "y", capture.Y); Add(n, "z", capture.Z);
            Add(n, "radius", capture.Radius); Add(n, "fov", capture.FieldOfView);
            Add(n, "maximumError", capture.MaximumError); Add(n, "fieldFraction", capture.FieldFraction); Add(n, "minimumPixels", capture.MinimumPixels);
        }

        private static void Add(ConfigNode n, string key, double value) => n.AddValue(key, value.ToString("R", CultureInfo.InvariantCulture));
    }
}

using System;
using System.Globalization;

namespace ShoAerospace.Koffee
{
    internal static class DeadlyStateCodec
    {
        internal static void Write(ConfigNode node, DeadlyAirState s)
        {
            node.AddValue("schema", "2");
            node.AddValue("initialized", s.Initialized.ToString());
            node.AddValue("episode", s.Episode.ToString());
            node.AddValue("emergency", s.Emergency.ToString());
            Number(node, "ut", s.UT); Number(node, "air", s.Air);
        }
        internal static bool TryRead(ConfigNode n, out DeadlyAirState s)
        {
            s = new DeadlyAirState();
            return One(n, "schema") && n.GetValue("schema") == "2" &&
                Boolean(n, "initialized", out s.Initialized) && Boolean(n, "episode", out s.Episode) &&
                Boolean(n, "emergency", out s.Emergency) && ReadNumber(n, "ut", double.MaxValue, out s.UT) &&
                ReadNumber(n, "air", DeadlyAirState.AirMaximum, out s.Air);
        }
        internal static bool One(ConfigNode n, string key) => n.GetValues(key).Length == 1;
        internal static bool Boolean(ConfigNode n, string key, out bool value)
        { value = false; return One(n, key) && bool.TryParse(n.GetValue(key), out value); }
        internal static void Number(ConfigNode n, string key, double value) =>
            n.AddValue(key, value.ToString("R", CultureInfo.InvariantCulture));
        internal static bool ReadNumber(ConfigNode n, string key, double max, out double value)
        {
            value = 0;
            return One(n, key) && double.TryParse(n.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                ExperimentContainerState.Finite(value) && value >= 0 && value <= max;
        }
    }
}

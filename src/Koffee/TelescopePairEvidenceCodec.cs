using System;
using System.Globalization;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal sealed partial class TelescopePairEvidence
    {
        internal static TelescopePairEvidence Read(ConfigNode parent)
        {
            var nodes=parent.GetNodes(Node);
            string[] keys={"version","planet","sample","maximumSeparationRadii","minimumPlanetPixels","minimumMoonPixels"};
            if (nodes.Length != 1 || keys.Any(k => nodes[0].GetValues(k).Length != 1) || nodes[0].GetValue("version") != "1")
                throw new ArgumentException("Invalid planet/moon evidence node.");
            var node=nodes[0];
            var n=node.GetValue("sample").Split(',').Select(Parse).ToArray();
            if (n.Length != 10) throw new ArgumentException("Invalid planet/moon frame.");
            var sample=new TelescopePairSample(n[0],n[1],n[2],n[3],n[4],n[5],n[6],n[7],n[8],n[9]);
            return new TelescopePairEvidence(node.GetValue("planet"),sample,Parse(node.GetValue(keys[3])),Parse(node.GetValue(keys[4])),Parse(node.GetValue(keys[5])));
        }
        private static double Parse(string value) => double.Parse(value,NumberStyles.Float,CultureInfo.InvariantCulture);
        internal void Write(ConfigNode parent)
        {
            var n=parent.AddNode(Node); n.AddValue("version","1"); n.AddValue("planet",Planet);
            var s=Sample;
            n.AddValue("sample",string.Join(",",new[] {s.UT,s.PX,s.PY,s.PZ,s.MX,s.MY,s.MZ,s.PlanetRadius,s.MoonRadius,s.Fov}
                .Select(v => v.ToString("R",CultureInfo.InvariantCulture))));
            n.AddValue("maximumSeparationRadii",MaximumSeparationRadii.ToString("R",CultureInfo.InvariantCulture));
            n.AddValue("minimumPlanetPixels",MinimumPlanetPixels.ToString("R",CultureInfo.InvariantCulture));
            n.AddValue("minimumMoonPixels",MinimumMoonPixels.ToString("R",CultureInfo.InvariantCulture));
        }
    }
}

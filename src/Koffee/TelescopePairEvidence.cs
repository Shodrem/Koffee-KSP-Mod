using System;

namespace ShoAerospace.Koffee
{
    internal sealed partial class TelescopePairEvidence
    {
        internal const string Node = "PLANET_MOON";
        internal string Planet { get; }
        internal TelescopePairSample Sample { get; }
        internal double MaximumSeparationRadii { get; }
        internal double MinimumPlanetPixels { get; }
        internal double MinimumMoonPixels { get; }
        internal TelescopePairEvidence(string planet,TelescopePairSample sample,double separation=32,double planetPixels=24,double moonPixels=4)
        {
            if (string.IsNullOrWhiteSpace(planet) || sample.Failure(separation,planetPixels,moonPixels) != "")
                throw new ArgumentException("Invalid planet/moon composition.");
            Planet=planet; Sample=sample; MaximumSeparationRadii=separation;
            MinimumPlanetPixels=planetPixels; MinimumMoonPixels=moonPixels;
        }
    }
}

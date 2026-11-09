using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal sealed class DeadlyEnvironment
    {
        internal readonly struct Slice
        {
            internal readonly double From, To;
            internal readonly bool Oxygen;
            internal Slice(double from, double to, bool oxygen) { From = from; To = to; Oxygen = oxygen; }
        }
        private readonly CelestialBody? body;
        private readonly Orbit? orbit;
        private readonly bool local;
        internal string Identity { get; }
        internal DeadlyEnvironment(Vessel vessel)
        {
            body = vessel.mainBody;
            if (body?.atmosphere != true || !body.atmosphereContainsOxygen) { Identity = "vacuum"; return; }
            if ((!vessel.loaded || vessel.packed) && !vessel.Landed && !vessel.Splashed && vessel.orbit != null)
            {
                // Copy the stock conic at capture. It predicts local pressure on
                // rails; it is not a sampled history of a loaded physics flight.
                orbit = new Orbit(vessel.orbit);
                Identity = "orbit:" + body.flightGlobalsIndex + ":" + orbit.semiMajorAxis.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                    ":" + orbit.eccentricity.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + orbit.epoch.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                    ":" + orbit.meanAnomalyAtEpoch.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                double pressure = vessel.loaded ? vessel.staticPressurekPa : body.GetPressure(vessel.altitude);
                local = ExperimentContainerState.Finite(pressure) && pressure > 0;
                Identity = "local:" + body.flightGlobalsIndex + ":" + local;
            }
        }
        internal IEnumerable<Slice> Slices(double from, double to)
        {
            if (orbit == null || body == null) { yield return new Slice(from, to, local); yield break; }
            var cuts = new List<double> { from, to };
            if (orbit.PeA < body.atmosphereDepth && orbit.ApA > body.atmosphereDepth ||
                orbit.eccentricity >= 1 && orbit.PeA < body.atmosphereDepth)
            {
                double anomaly = orbit.TrueAnomalyAtRadius(body.Radius + body.atmosphereDepth);
                if (!ExperimentContainerState.Finite(anomaly)) yield break;
                foreach (double angle in new[] { anomaly, -anomaly })
                {
                    double crossing = orbit.GetUTforTrueAnomaly(angle, 0);
                    if (!ExperimentContainerState.Finite(crossing)) yield break;
                    if (orbit.eccentricity < 1)
                    {
                        if (!(orbit.period > 0) || !ExperimentContainerState.Finite(orbit.period)) yield break;
                        crossing += Math.Ceiling((from - crossing) / orbit.period) * orbit.period;
                        // Bound pathological saves/warps without inventing exposure.
                        if ((to - from) / orbit.period > 10000) yield break;
                        for (; crossing < to; crossing += orbit.period) if (crossing > from) cuts.Add(crossing);
                    }
                    else if (crossing > from && crossing < to) cuts.Add(crossing);
                }
            }
            var points = cuts.Distinct().OrderBy(t => t).ToArray();
            for (int i = 0; i + 1 < points.Length; i++)
            {
                double altitude = orbit.GetRadiusAtUT((points[i] + points[i + 1]) / 2) - body.Radius;
                if (!ExperimentContainerState.Finite(altitude)) yield break;
                double pressure = body.GetPressure(altitude);
                if (!ExperimentContainerState.Finite(pressure)) yield break;
                yield return new Slice(points[i], points[i + 1], pressure > 0);
            }
        }
    }
}

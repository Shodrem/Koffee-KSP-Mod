using System;
using System.Collections.Generic;

namespace ShoAerospace.Koffee
{
    internal static class KomfortRate
    {
        // Exact area of a clamped straight line, including a floor/cap crossing.
        internal static double Integral(double a, double b, double seconds, double lower = .25, double upper = 1)
        {
            if (upper <= lower) return upper * seconds;
            var cuts = new List<double> { 0, 1 };
            if (a != b)
                foreach (double value in new[] { lower, upper })
                { double fraction = (value - a) / (b - a); if (fraction > 0 && fraction < 1) cuts.Add(fraction); }
            cuts.Sort(); double area = 0;
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                double x = cuts[i], y = cuts[i + 1];
                double first = Math.Max(lower, Math.Min(upper, a + (b - a) * x));
                double last = Math.Max(lower, Math.Min(upper, a + (b - a) * y));
                area += (first + last) * .5 * (y - x) * seconds;
            }
            return area;
        }
        internal static double Limit(double from, double to, double budget, Func<double, double, double> integral, bool conservative = false)
        {
            if (integral(from, to) <= budget) return to;
            double low = from, high = to;
            for (int i = 0; i < 60; i++)
            { double middle = low + (high - low) * .5; if (integral(from, middle) <= budget) low = middle; else high = middle; }
            return conservative ? low : high;
        }
    }
}

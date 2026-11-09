using System;

namespace ShoAerospace.Koffee
{
    internal static class EngineeringMath
    {
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

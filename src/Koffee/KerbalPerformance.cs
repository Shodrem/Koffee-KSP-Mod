using System;

namespace ShoAerospace.Koffee
{
    internal static class KerbalPerformance
    {
        // Kept separate so Koffee and a future Comfort system can be balanced independently.
        internal static double EffectiveContribution(double stockContribution,
            double koffeeModifier, double comfortModifier = 1) =>
            Math.Max(0, stockContribution) * Math.Max(0, koffeeModifier) * Math.Max(0, comfortModifier);

        internal static double StockLabContribution(ProtoCrewMember kerbal, float scientistBonus) =>
            1 + scientistBonus * kerbal.experienceLevel;
    }
}

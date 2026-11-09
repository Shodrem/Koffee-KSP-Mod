using System;

namespace ShoAerospace.Koffee
{
    // Immutable observations only. Contract checks never assign crew, buy coffee or run research.
    internal sealed class StationContractCandidate
    {
        internal uint[] Parts = Array.Empty<uint>();
        internal uint[] Laboratories = Array.Empty<uint>();
        internal bool Orbiting, Manager, Engineer;
        internal int Capabilities;
        internal bool Laboratory => Laboratories.Length > 0;
    }

    internal static class StationContractRules
    {
        internal const int Basic = 1, BiGEn = 2, MaPhyTec = 4;
        internal static bool HasLaboratory(StationContractCandidate candidate, int capability) =>
            capability >= 0 && candidate.Laboratory &&
            (capability == 0 || (candidate.Capabilities & capability) == capability);

        internal static bool MissingAdvanced(StationContractCandidate candidate, int capability) =>
            candidate.Orbiting && candidate.Laboratory && (capability == BiGEn || capability == MaPhyTec) &&
            (candidate.Capabilities & capability) == 0;
    }
}

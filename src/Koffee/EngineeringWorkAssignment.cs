using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal static class EngineeringWorkAssignment
    {
        internal static IReadOnlyList<EngineeringCandidate<ProtoCrewMember>> Relevant(
            BaseConverter converter)
        {
            if (converter == null || converter.vessel == null ||
                string.IsNullOrWhiteSpace(converter.ExperienceEffect))
                return new EngineeringCandidate<ProtoCrewMember>[0];

            IEnumerable<EngineeringCandidate<ProtoCrewMember>> candidates = converter.vessel
                .GetVesselCrew()
                .Where(kerbal => kerbal != null && kerbal.HasEffect(converter.ExperienceEffect))
                .Select(kerbal => new EngineeringCandidate<ProtoCrewMember>(kerbal,
                    kerbal.experienceLevel, kerbal.persistentID, kerbal.name));
            return EngineeringSelection.Order(candidates);
        }
    }
}

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

            EngineeringAffinity affinity = converter.GetType() == typeof(ModuleResourceHarvester) ? EngineeringAffinity.Extraction : EngineeringAffinity.Refining;
            return FromCrew(converter.vessel.GetVesselCrew(), converter.ExperienceEffect, affinity);
        }

        internal static IReadOnlyList<EngineeringCandidate<ProtoCrewMember>> FromCrew(
            IEnumerable<ProtoCrewMember> crew, string experienceEffect, EngineeringAffinity affinity)
        {
            IEnumerable<EngineeringCandidate<ProtoCrewMember>> candidates = crew
                .Where(kerbal => kerbal != null && kerbal.trait == "Engineer" && kerbal.HasEffect(experienceEffect) &&
                    KerbalSpecialtyScenario.TryGetEngineerProfile(kerbal.name, out _))
                .Select(kerbal => new EngineeringCandidate<ProtoCrewMember>(kerbal,
                    KerbalSpecialtyScenario.TryGetEngineerProfile(kerbal.name, out var profile) ? profile.Score(affinity) : 0, kerbal.persistentID, kerbal.name));
            return EngineeringSelection.Order(candidates);
        }
    }
}

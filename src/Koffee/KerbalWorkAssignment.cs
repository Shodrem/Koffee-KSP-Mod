using System;
using System.Collections.Generic;
using System.Linq;
using Experience.Effects;

namespace ShoAerospace.Koffee
{
    internal static class KerbalWorkAssignment
    {
        internal static IReadOnlyList<ProtoCrewMember> LabScientists(Part part) =>
            Scientists(part?.protoModuleCrew).OrderBy(StableId).ThenBy(kerbal => kerbal.name,
                StringComparer.Ordinal).ToArray();

        internal static ProtoCrewMember? ExperimentScientist(Part consumer)
        {
            if (consumer == null || consumer.vessel == null) return null;
            var candidates = new List<Candidate>();
            Add(candidates, consumer.protoModuleCrew, 0);
            if (!consumer.vessel.isEVA)
            {
                int rank = 1;
                foreach (Part part in consumer.vessel.parts)
                {
                    if (part != null && !ReferenceEquals(part, consumer)) Add(candidates, part.protoModuleCrew, rank);
                    rank++;
                }
            }
            return candidates.GroupBy(candidate => candidate.Kerbal)
                .Select(group => group.OrderBy(candidate => candidate.Rank).First())
                .OrderByDescending(candidate => KerbalWorkState.IsKaffeinated(candidate.Kerbal))
                .ThenBy(candidate => candidate.Rank)
                .ThenBy(candidate => StableId(candidate.Kerbal))
                .ThenBy(candidate => candidate.Kerbal.name, StringComparer.Ordinal)
                .Select(candidate => candidate.Kerbal).FirstOrDefault();
        }

        private static IEnumerable<ProtoCrewMember> Scientists(IEnumerable<ProtoCrewMember>? crew) =>
            crew == null ? Enumerable.Empty<ProtoCrewMember>() :
                crew.Where(kerbal => kerbal != null && kerbal.HasEffect<ScienceSkill>());

        private static void Add(ICollection<Candidate> candidates,
            IEnumerable<ProtoCrewMember>? crew, int rank)
        {
            foreach (ProtoCrewMember kerbal in Scientists(crew)) candidates.Add(new Candidate(kerbal, rank));
        }

        private static ulong StableId(ProtoCrewMember kerbal) => kerbal.persistentID == 0
            ? ulong.MaxValue : kerbal.persistentID;

        private readonly struct Candidate
        {
            internal readonly ProtoCrewMember Kerbal;
            internal readonly int Rank;
            internal Candidate(ProtoCrewMember kerbal, int rank) { Kerbal = kerbal; Rank = rank; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    // Editor seats, not flight assignments or research jobs. Reading the stock
    // manifest does not ask the crew dialog to rebuild/write its seat lists.
    internal sealed class KoffeeEditorCrew
    {
        internal readonly KoffeeCrewStatus Crew;
        internal readonly bool Assigned;
        private KoffeeEditorCrew(ProtoCrewMember crew, bool assigned)
        { Crew = new KoffeeCrewStatus(crew, ""); Assigned = assigned; }

        internal static IReadOnlyList<KoffeeEditorCrew> Capture()
        {
            var roster = HighLogic.CurrentGame?.CrewRoster;
            if (!HighLogic.LoadedSceneIsEditor || roster == null) return Array.Empty<KoffeeEditorCrew>();
            var ids = new HashSet<uint>(EditorLogic.fetch?.ship?.parts?
                .Where(p => p != null && p.State != PartStates.DEAD).Select(p => p.craftID) ?? Enumerable.Empty<uint>());
            var seated = new HashSet<string>(ShipConstruction.ShipManifest?.PartManifests
                .Where(p => p != null && ids.Contains(p.PartID)).SelectMany(p => p.GetPartCrew())
                .Where(c => c != null).Select(c => c!.name) ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            return roster.Kerbals().Where(c => c != null && !string.IsNullOrEmpty(c.name) &&
                c.type == ProtoCrewMember.KerbalType.Crew &&
                (c.rosterStatus == ProtoCrewMember.RosterStatus.Available || c.rosterStatus == ProtoCrewMember.RosterStatus.Assigned) && (c.trait == "Engineer" || c.trait == ScientistSpecialtyProfile.ProfessionId) &&
                (seated.Contains(c.name) || c.rosterStatus == ProtoCrewMember.RosterStatus.Available))
                .GroupBy(c => c.name, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new KoffeeEditorCrew(g.First(), seated.Contains(g.Key))).ToArray();
        }
    }
}

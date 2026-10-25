using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal sealed class KerbalSpecialtyPersistence
    {
        private readonly KerbalSpecialtyStore store;
        private ConfigNode[] originalNodes = Array.Empty<ConfigNode>();
        private HashSet<string> blockedNames = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> restoredNames = new HashSet<string>(StringComparer.Ordinal);
        private bool unidentifiedScientistProfile;
        internal bool IsLoaded { get; private set; }

        internal KerbalSpecialtyPersistence(ScientistSpecialtyGenerator generator)
        {
            store = new KerbalSpecialtyStore(generator);
        }

        internal IReadOnlyList<string> Load(ConfigNode node)
        {
            ConfigNode[] originals = node.GetNodes(ScientistSpecialtyProfileCodec.NodeName)
                .Select(profile => profile.CreateCopy()).ToArray();
            var blocked = new HashSet<string>(StringComparer.Ordinal);
            var candidates = new Dictionary<string, List<ScientistSpecialtyRecord>>(StringComparer.Ordinal);
            var diagnostics = new List<string>();
            bool unidentified = false;
            foreach (ConfigNode profile in originals)
            {
                string[] professions = profile.GetValues("professionId");
                // Future profession profiles are retained opaquely, with no effects here.
                if (professions.Length == 1 && !string.IsNullOrEmpty(professions[0]) &&
                    professions[0] != ScientistSpecialtyProfile.ProfessionId) continue;
                string[] names = profile.GetValues("kerbalName");
                string[] identities = names.Where(name => !string.IsNullOrEmpty(name))
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (names.Length != 1 || identities.Length != 1)
                {
                    foreach (string identity in identities) blocked.Add(identity);
                    if (identities.Length == 0) unidentified = true;
                    diagnostics.Add("Ambiguous or missing scientist profile name; original node retained.");
                    continue;
                }
                string name = identities[0];
                if (!ScientistSpecialtyProfileCodec.TryRead(profile, out ScientistSpecialtyRecord record, out string error))
                {
                    blocked.Add(name);
                    diagnostics.Add("Specialty profile for '" + name + "' retained without regeneration: " + error);
                    continue;
                }
                if (!candidates.TryGetValue(name, out List<ScientistSpecialtyRecord>? entries))
                    candidates.Add(name, entries = new List<ScientistSpecialtyRecord>());
                entries.Add(record);
            }
            var records = new List<ScientistSpecialtyRecord>();
            foreach (var candidate in candidates)
            {
                ScientistSpecialtyProfile first = candidate.Value[0].Profile;
                if (candidate.Value.Any(entry => !SameProfile(first, entry.Profile)))
                {
                    blocked.Add(candidate.Key);
                    diagnostics.Add("Conflicting specialty profiles for '" + candidate.Key + "'; all original nodes retained.");
                }
                if (!blocked.Contains(candidate.Key)) records.Add(candidate.Value[0]);
            }
            if (unidentified)
                diagnostics.Add("An unidentified scientist profile prevents generating absent profiles; existing valid profiles remain usable.");
            store.Load(records);
            originalNodes = originals;
            blockedNames = blocked;
            restoredNames = new HashSet<string>(records.Select(record => record.KerbalName), StringComparer.Ordinal);
            unidentifiedScientistProfile = unidentified;
            IsLoaded = true;
            return diagnostics;
        }

        internal bool TryGetScientistProfile(string? name, out ScientistSpecialtyProfile profile) =>
            store.TryGetScientistProfile(name, out profile);

        internal bool EnsureScientistProfile(string name, string? profession, out ScientistSpecialtyProfile profile)
        {
            profile = null!;
            if (!IsLoaded || string.IsNullOrEmpty(name) || blockedNames.Contains(name)) return false;
            if (profession != ScientistSpecialtyProfile.ProfessionId) return false;
            if (store.TryGetScientistProfile(name, out profile)) return true;
            if (unidentifiedScientistProfile) return false;
            return store.EnsureScientistProfile(name, profession, out profile);
        }

        internal void Save(ConfigNode node)
        {
            if (!IsLoaded) throw new InvalidOperationException("Specialty profiles must load before saving.");
            node.RemoveNodes(ScientistSpecialtyProfileCodec.NodeName);
            // Preserve unknown fields, versions and duplicate evidence in loaded nodes.
            foreach (ConfigNode original in originalNodes) node.AddNode(original.CreateCopy());
            foreach (ScientistSpecialtyRecord record in store.Save())
                if (!restoredNames.Contains(record.KerbalName))
                    node.AddNode(ScientistSpecialtyProfileCodec.Write(record));
        }

        private static bool SameProfile(ScientistSpecialtyProfile a, ScientistSpecialtyProfile b) =>
            a.PrimaryCategory == b.PrimaryCategory && a.SecondaryCategory == b.SecondaryCategory &&
            ScienceCategories.All.All(category => a.Score(category) == b.Score(category));
    }
}

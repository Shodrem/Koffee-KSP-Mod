using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal sealed class EngineerAffinityPersistence
    {
        private readonly EngineerAffinityStore store;
        private ConfigNode[] originalNodes = Array.Empty<ConfigNode>();
        private HashSet<string> blockedNames = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> restoredNames = new HashSet<string>(StringComparer.Ordinal);
        private bool unidentifiedEngineerProfile;
        internal bool IsLoaded { get; private set; }

        internal EngineerAffinityPersistence(EngineerAffinityGenerator generator)
        {
            store = new EngineerAffinityStore(generator);
        }

        internal IReadOnlyList<string> Load(ConfigNode node)
        {
            ConfigNode[] originals = node.GetNodes(EngineerAffinityProfileCodec.NodeName)
                .Select(profile => profile.CreateCopy()).ToArray();
            var blocked = new HashSet<string>(StringComparer.Ordinal);
            var candidates = new Dictionary<string, List<EngineerAffinityRecord>>(StringComparer.Ordinal);
            var diagnostics = new List<string>();
            bool unidentified = false;
            foreach (ConfigNode profile in originals)
            {
                string[] professions = profile.GetValues("professionId");
                // Future profession profiles are retained opaquely, with no effects here.
                if (professions.Length == 1 && !string.IsNullOrEmpty(professions[0]) &&
                    professions[0] != EngineerAffinityProfile.ProfessionId) continue;
                string[] names = profile.GetValues("kerbalName");
                string[] identities = names.Where(name => !string.IsNullOrEmpty(name))
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (names.Length != 1 || identities.Length != 1)
                {
                    foreach (string identity in identities) blocked.Add(identity);
                    if (identities.Length == 0) unidentified = true;
                    diagnostics.Add("Ambiguous or missing engineer profile name; original node retained.");
                    continue;
                }
                string name = identities[0];
                if (!EngineerAffinityProfileCodec.TryRead(profile, out EngineerAffinityRecord record, out string error))
                {
                    blocked.Add(name);
                    diagnostics.Add("Specialty profile for '" + name + "' retained without regeneration: " + error);
                    continue;
                }
                if (!candidates.TryGetValue(name, out List<EngineerAffinityRecord>? entries))
                    candidates.Add(name, entries = new List<EngineerAffinityRecord>());
                entries.Add(record);
            }
            var records = new List<EngineerAffinityRecord>();
            foreach (var candidate in candidates)
            {
                EngineerAffinityProfile first = candidate.Value[0].Profile;
                if (candidate.Value.Any(entry => !SameProfile(first, entry.Profile)))
                {
                    blocked.Add(candidate.Key);
                    diagnostics.Add("Conflicting specialty profiles for '" + candidate.Key + "'; all original nodes retained.");
                }
                if (!blocked.Contains(candidate.Key)) records.Add(candidate.Value[0]);
            }
            if (unidentified)
                diagnostics.Add("An unidentified engineer profile prevents generating absent profiles; existing valid profiles remain usable.");
            store.Load(records);
            originalNodes = originals;
            blockedNames = blocked;
            restoredNames = new HashSet<string>(records.Select(record => record.KerbalName), StringComparer.Ordinal);
            unidentifiedEngineerProfile = unidentified;
            IsLoaded = true;
            return diagnostics;
        }

        internal bool TryGetEngineerProfile(string? name, out EngineerAffinityProfile profile) =>
            store.TryGetEngineerProfile(name, out profile);

        internal bool EnsureEngineerProfile(string name, string? profession, out EngineerAffinityProfile profile)
        {
            profile = null!;
            if (!IsLoaded || string.IsNullOrEmpty(name) || blockedNames.Contains(name)) return false;
            if (profession != EngineerAffinityProfile.ProfessionId) return false;
            if (store.TryGetEngineerProfile(name, out profile)) return true;
            if (unidentifiedEngineerProfile) return false;
            return store.EnsureEngineerProfile(name, profession, out profile);
        }

        internal void Save(ConfigNode node)
        {
            if (!IsLoaded) throw new InvalidOperationException("Specialty profiles must load before saving.");
            node.RemoveNodes(EngineerAffinityProfileCodec.NodeName);
            // Preserve unknown fields, versions and duplicate evidence in loaded nodes.
            foreach (ConfigNode original in originalNodes) node.AddNode(original.CreateCopy());
            foreach (EngineerAffinityRecord record in store.Save())
                if (!restoredNames.Contains(record.KerbalName))
                    node.AddNode(EngineerAffinityProfileCodec.Write(record));
        }

        private static bool SameProfile(EngineerAffinityProfile a, EngineerAffinityProfile b) =>
            a.PrimaryCategory == b.PrimaryCategory && a.SecondaryCategory == b.SecondaryCategory &&
            EngineeringAffinities.All.All(category => a.Score(category) == b.Score(category));
    }
}

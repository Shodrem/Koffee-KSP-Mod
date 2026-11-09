using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal sealed class EngineerAffinityStore
    {
        // Each profession owns its profile model. Only the Engineer branch exists now.
        private readonly Dictionary<string, EngineerAffinityProfile> engineers =
            new Dictionary<string, EngineerAffinityProfile>(StringComparer.Ordinal);
        private readonly EngineerAffinityGenerator generator;

        internal EngineerAffinityStore(EngineerAffinityGenerator generator)
        {
            this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
        }

        internal bool TryGetEngineerProfile(string? kerbalName, out EngineerAffinityProfile profile)
        {
            if (!string.IsNullOrEmpty(kerbalName) &&
                engineers.TryGetValue(kerbalName!, out EngineerAffinityProfile? found))
            {
                profile = found;
                return true;
            }
            profile = null!;
            return false;
        }

        internal bool EnsureEngineerProfile(string kerbalName, string? professionId,
            out EngineerAffinityProfile profile)
        {
            profile = null!;
            if (!string.Equals(professionId, EngineerAffinityProfile.ProfessionId, StringComparison.Ordinal))
                return false;
            ValidateName(kerbalName);
            if (TryGetEngineerProfile(kerbalName, out profile)) return true;
            profile = generator.Generate();
            engineers.Add(kerbalName, profile);
            return true;
        }

        // The Scenario codec validates/quarantines raw nodes before loading records.
        // A bad batch must not partially replace the active game's profiles.
        internal void Load(IEnumerable<EngineerAffinityRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            var restored = new Dictionary<string, EngineerAffinityProfile>(StringComparer.Ordinal);
            foreach (EngineerAffinityRecord record in records)
            {
                ValidateName(record.KerbalName);
                if (record.Profile == null) throw new ArgumentException("Restored profile is missing.", nameof(records));
                if (restored.ContainsKey(record.KerbalName))
                    throw new ArgumentException("Duplicate engineer profile identity.", nameof(records));
                restored.Add(record.KerbalName, record.Profile);
            }
            engineers.Clear();
            foreach (var record in restored) engineers.Add(record.Key, record.Value);
        }

        internal IReadOnlyList<EngineerAffinityRecord> Save() => engineers
            .OrderBy(record => record.Key, StringComparer.Ordinal)
            .Select(record => new EngineerAffinityRecord(record.Key, record.Value)).ToArray();

        internal void Clear() => engineers.Clear();

        private static void ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Kerbal name must not be empty.", nameof(name));
        }
    }

    internal readonly struct EngineerAffinityRecord
    {
        internal string KerbalName { get; }
        internal EngineerAffinityProfile Profile { get; }

        internal EngineerAffinityRecord(string kerbalName, EngineerAffinityProfile profile)
        {
            KerbalName = kerbalName;
            Profile = profile;
        }
    }
}

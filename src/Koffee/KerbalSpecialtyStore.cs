using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal sealed class KerbalSpecialtyStore
    {
        // Each profession owns its profile model. Only the Scientist branch exists now.
        private readonly Dictionary<string, ScientistSpecialtyProfile> scientists =
            new Dictionary<string, ScientistSpecialtyProfile>(StringComparer.Ordinal);
        private readonly ScientistSpecialtyGenerator generator;

        internal KerbalSpecialtyStore(ScientistSpecialtyGenerator generator)
        {
            this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
        }

        internal bool TryGetScientistProfile(string? kerbalName, out ScientistSpecialtyProfile profile)
        {
            if (!string.IsNullOrEmpty(kerbalName) &&
                scientists.TryGetValue(kerbalName!, out ScientistSpecialtyProfile? found))
            {
                profile = found;
                return true;
            }
            profile = null!;
            return false;
        }

        internal bool EnsureScientistProfile(string kerbalName, string? professionId,
            out ScientistSpecialtyProfile profile)
        {
            profile = null!;
            if (!string.Equals(professionId, ScientistSpecialtyProfile.ProfessionId, StringComparison.Ordinal))
                return false;
            ValidateName(kerbalName);
            if (TryGetScientistProfile(kerbalName, out profile)) return true;
            profile = generator.Generate();
            scientists.Add(kerbalName, profile);
            return true;
        }

        // The Scenario codec validates/quarantines raw nodes before loading records.
        // A bad batch must not partially replace the active game's profiles.
        internal void Load(IEnumerable<ScientistSpecialtyRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            var restored = new Dictionary<string, ScientistSpecialtyProfile>(StringComparer.Ordinal);
            foreach (ScientistSpecialtyRecord record in records)
            {
                ValidateName(record.KerbalName);
                if (record.Profile == null) throw new ArgumentException("Restored profile is missing.", nameof(records));
                if (restored.ContainsKey(record.KerbalName))
                    throw new ArgumentException("Duplicate scientist profile identity.", nameof(records));
                restored.Add(record.KerbalName, record.Profile);
            }
            scientists.Clear();
            foreach (var record in restored) scientists.Add(record.Key, record.Value);
        }

        internal IReadOnlyList<ScientistSpecialtyRecord> Save() => scientists
            .OrderBy(record => record.Key, StringComparer.Ordinal)
            .Select(record => new ScientistSpecialtyRecord(record.Key, record.Value)).ToArray();

        internal void Clear() => scientists.Clear();

        private static void ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Kerbal name must not be empty.", nameof(name));
        }
    }

    internal readonly struct ScientistSpecialtyRecord
    {
        internal string KerbalName { get; }
        internal ScientistSpecialtyProfile Profile { get; }

        internal ScientistSpecialtyRecord(string kerbalName, ScientistSpecialtyProfile profile)
        {
            KerbalName = kerbalName;
            Profile = profile;
        }
    }
}

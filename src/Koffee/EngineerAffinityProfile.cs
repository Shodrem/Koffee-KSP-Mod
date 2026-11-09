using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ShoAerospace.Koffee
{
    internal sealed class EngineerAffinityProfile
    {
        internal const int CurrentSchemaVersion = 1;
        internal const int CurrentGeneratorVersion = 1;
        internal const string ProfessionId = "Engineer";
        internal EngineeringAffinity PrimaryCategory { get; }
        internal EngineeringAffinity SecondaryCategory { get; }
        internal IReadOnlyDictionary<EngineeringAffinity, int> Scores { get; }

        internal EngineerAffinityProfile(EngineeringAffinity primary, EngineeringAffinity secondary,
            IReadOnlyDictionary<EngineeringAffinity, int> scores)
        {
            if (!EngineeringAffinities.IsValid(primary)) throw new ArgumentOutOfRangeException(nameof(primary));
            if (!EngineeringAffinities.IsValid(secondary)) throw new ArgumentOutOfRangeException(nameof(secondary));
            if (primary == secondary) throw new ArgumentException("Specialty categories must differ.");
            if (scores == null) throw new ArgumentNullException(nameof(scores));
            if (scores.Count != EngineeringAffinities.All.Count)
                throw new ArgumentException("A engineer requires exactly four affinity scores.", nameof(scores));

            var copy = new Dictionary<EngineeringAffinity, int>();
            foreach (EngineeringAffinity category in EngineeringAffinities.All)
            {
                int minimum = category == primary ? 8 : category == secondary ? 5 : 1;
                int maximum = category == primary ? 10 : category == secondary ? 7 : 4;
                if (!scores.TryGetValue(category, out int score) || score < minimum || score > maximum)
                    throw new ArgumentException("Affinity is missing or outside its specialty range.", nameof(scores));
                copy.Add(category, score);
            }
            PrimaryCategory = primary;
            SecondaryCategory = secondary;
            Scores = new ReadOnlyDictionary<EngineeringAffinity, int>(copy);
        }

        internal int Score(EngineeringAffinity category)
        {
            if (!EngineeringAffinities.IsValid(category)) throw new ArgumentOutOfRangeException(nameof(category));
            return Scores[category];
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ShoAerospace.Koffee
{
    internal sealed class ScientistSpecialtyProfile
    {
        internal const int CurrentSchemaVersion = 1;
        internal const int CurrentGeneratorVersion = 1;
        internal const string ProfessionId = "Scientist";
        internal ScienceCategory PrimaryCategory { get; }
        internal ScienceCategory SecondaryCategory { get; }
        internal IReadOnlyDictionary<ScienceCategory, int> Scores { get; }

        internal ScientistSpecialtyProfile(ScienceCategory primary, ScienceCategory secondary,
            IReadOnlyDictionary<ScienceCategory, int> scores)
        {
            if (!ScienceCategories.IsValid(primary)) throw new ArgumentOutOfRangeException(nameof(primary));
            if (!ScienceCategories.IsValid(secondary)) throw new ArgumentOutOfRangeException(nameof(secondary));
            if (primary == secondary) throw new ArgumentException("Specialty categories must differ.");
            if (scores == null) throw new ArgumentNullException(nameof(scores));
            if (scores.Count != ScienceCategories.All.Count)
                throw new ArgumentException("A scientist requires exactly seven affinity scores.", nameof(scores));

            var copy = new Dictionary<ScienceCategory, int>();
            foreach (ScienceCategory category in ScienceCategories.All)
            {
                int minimum = category == primary ? 8 : category == secondary ? 5 : 1;
                int maximum = category == primary ? 10 : category == secondary ? 7 : 4;
                if (!scores.TryGetValue(category, out int score) || score < minimum || score > maximum)
                    throw new ArgumentException("Affinity is missing or outside its specialty range.", nameof(scores));
                copy.Add(category, score);
            }
            PrimaryCategory = primary;
            SecondaryCategory = secondary;
            Scores = new ReadOnlyDictionary<ScienceCategory, int>(copy);
        }

        internal int Score(ScienceCategory category)
        {
            if (!ScienceCategories.IsValid(category)) throw new ArgumentOutOfRangeException(nameof(category));
            return Scores[category];
        }
    }
}

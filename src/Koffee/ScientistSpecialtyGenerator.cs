using System;
using System.Collections.Generic;

namespace ShoAerospace.Koffee
{
    internal sealed class ScientistSpecialtyGenerator
    {
        private readonly Func<int, int> nextIndex;

        // Construct one generator per store; never reseed for an individual kerbal.
        internal ScientistSpecialtyGenerator() : this(new Random()) { }

        internal ScientistSpecialtyGenerator(Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            nextIndex = random.Next;
        }

        internal ScientistSpecialtyGenerator(Func<int, int> nextIndex)
        {
            this.nextIndex = nextIndex ?? throw new ArgumentNullException(nameof(nextIndex));
        }

        internal ScientistSpecialtyProfile Generate()
        {
            int primaryIndex = Next(ScienceCategories.All.Count);
            int secondaryIndex = Next(ScienceCategories.All.Count - 1);
            if (secondaryIndex >= primaryIndex) secondaryIndex++;
            ScienceCategory primary = ScienceCategories.All[primaryIndex];
            ScienceCategory secondary = ScienceCategories.All[secondaryIndex];
            var scores = new Dictionary<ScienceCategory, int>();
            foreach (ScienceCategory category in ScienceCategories.All)
                scores.Add(category, category == primary ? 8 + Next(3) :
                    category == secondary ? 5 + Next(3) : 1 + Next(4));
            return new ScientistSpecialtyProfile(primary, secondary, scores);
        }

        private int Next(int exclusiveMaximum)
        {
            int value = nextIndex(exclusiveMaximum);
            if (value < 0 || value >= exclusiveMaximum)
                throw new InvalidOperationException("Random source returned an out-of-range index.");
            return value;
        }
    }
}

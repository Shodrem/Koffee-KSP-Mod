using System;
using System.Collections.Generic;

namespace ShoAerospace.Koffee
{
    internal sealed class EngineerAffinityGenerator
    {
        private readonly Func<int, int> nextIndex;

        // Construct one generator per store; never reseed for an individual kerbal.
        internal EngineerAffinityGenerator() : this(new Random()) { }

        internal EngineerAffinityGenerator(Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            nextIndex = random.Next;
        }

        internal EngineerAffinityGenerator(Func<int, int> nextIndex)
        {
            this.nextIndex = nextIndex ?? throw new ArgumentNullException(nameof(nextIndex));
        }

        internal EngineerAffinityProfile Generate()
        {
            int primaryIndex = Next(EngineeringAffinities.All.Count);
            int secondaryIndex = Next(EngineeringAffinities.All.Count - 1);
            if (secondaryIndex >= primaryIndex) secondaryIndex++;
            EngineeringAffinity primary = EngineeringAffinities.All[primaryIndex];
            EngineeringAffinity secondary = EngineeringAffinities.All[secondaryIndex];
            var scores = new Dictionary<EngineeringAffinity, int>();
            foreach (EngineeringAffinity category in EngineeringAffinities.All)
                scores.Add(category, category == primary ? 8 + Next(3) :
                    category == secondary ? 5 + Next(3) : 1 + Next(4));
            return new EngineerAffinityProfile(primary, secondary, scores);
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

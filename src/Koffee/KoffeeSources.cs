using System.Collections.Generic;

namespace ShoAerospace.Koffee
{
    internal static class KoffeeSources
    {
        internal static List<IResourceAccount> Find(Part consumer)
        {
            var sources = new List<IResourceAccount>();
            var definition = PartResourceLibrary.Instance.GetDefinition(KoffeeIdentifiers.ResourceName);
            if (definition == null || consumer.vessel == null) return sources;
            foreach (Part source in consumer.vessel.parts)
            {
                // These are the same access and pulling checks used by stock totals.
                PartResource? resource = Accessible(consumer, source, definition);
                if (resource != null) sources.Add(new Account(consumer, source, resource, definition));
            }
            return sources;
        }

        private static PartResource? Accessible(Part consumer, Part source, PartResourceDefinition definition) =>
            consumer.CanCrossfeed(source, definition.id, definition.resourceFlowMode)
                ? source.Resources.GetFlowing(definition.id, true) : null;

        internal static double Available(Part consumer)
        {
            var definition = PartResourceLibrary.Instance.GetDefinition(KoffeeIdentifiers.ResourceName);
            if (definition == null || consumer.vessel == null) return 0;
            double cups = 0;
            foreach (Part source in consumer.vessel.parts)
            {
                PartResource? resource = Accessible(consumer, source, definition);
                if (resource != null) cups += ResourcePayment.WholeCups(resource.amount, true);
            }
            return cups;
        }

        // Vessel-wide inventory for display: count each source once when it can
        // supply at least its own part under the same stock flow checks as payment.
        internal static double Available(Vessel vessel)
        {
            var definition = PartResourceLibrary.Instance.GetDefinition(KoffeeIdentifiers.ResourceName);
            if (definition == null || vessel == null || vessel.parts == null) return 0;
            double cups = 0;
            foreach (Part source in vessel.parts)
            {
                if (source == null) continue;
                PartResource? resource = Accessible(source, source, definition);
                if (resource != null) cups += ResourcePayment.WholeCups(resource.amount, true);
            }
            return cups;
        }

        private sealed class Account : IResourceAccount
        {
            private readonly Part consumer;
            private readonly Part source;
            private readonly PartResource resource;
            private readonly PartResourceDefinition definition;

            internal Account(Part consumer, Part source, PartResource resource, PartResourceDefinition definition)
            {
                this.consumer = consumer;
                this.source = source;
                this.resource = resource;
                this.definition = definition;
            }

            public double Amount { get => resource.amount; set => resource.amount = value; }
            public double Capacity => resource.maxAmount;
            public bool CanWithdraw => consumer != null && source != null &&
                consumer.CanCrossfeed(source, definition.id, definition.resourceFlowMode) &&
                ReferenceEquals(source.Resources.GetFlowing(definition.id, true), resource);
            public int Priority => source.GetResourcePriority();
            public uint SourceId => source.flightID;
        }
    }
}

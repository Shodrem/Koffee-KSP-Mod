using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing;
using BackgroundResourceProcessing.Core;
using UnityEngine;
using BrpConverter = BackgroundResourceProcessing.Core.ResourceConverter;

[assembly: KSPAssemblyDependency("BackgroundResourceProcessing", 0, 3)]
[assembly: KSPAssemblyDependency("Koffee", 1, 0)]

namespace ShoAerospace.Koffee.Brp
{
    public sealed class BrpPowerProvider : IStationBackgroundElectricity
    {
        private readonly HashSet<Guid> missing = new HashSet<Guid>();
        private BrpPowerProvider()
        {
            BackgroundResourceProcessor.onVesselRecord.Add(OnRecord);
            BackgroundResourceProcessor.onStateUpdate.Add(OnStateUpdate);
            BackgroundResourceProcessor.onVesselRestore.Add(OnRestore);
        }

        // Called reflectively only after BRP's API has been checked. The core
        // assembly never acquires a reference to any BRP type.
        public static object Create() => new BrpPowerProvider();

        public void Ensure(Vessel vessel, double now)
        {
            var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
            if (processor == null)
            {
                if (missing.Add(vessel.id)) Debug.LogError("[Koffee] BRP selected at startup, but this vessel has no BRP processor; background research is waiting: " + vessel.id);
                return;
            }
            missing.Remove(vessel.id);
            Ensure(processor, now);
        }

        private static void Ensure(BackgroundResourceProcessor processor, double now)
        {
            var existing = processor.Converters.FirstOrDefault(c => c.Behaviour is KoffeeResearchPowerBehaviour);
            if (existing?.Behaviour is KoffeeResearchPowerBehaviour current)
            {
                if (current.PrimeContinuity(processor, now))
                {
                    existing.Inputs.Clear(); existing.NextChangepoint = now; processor.MarkDirty();
                }
                if (current.Idle && now > processor.LastChangepoint && processor.Vessel.protoVessel?.protoPartSnapshots != null &&
                    processor.Vessel.protoVessel.protoPartSnapshots.Any(p => p.modules.Any(m =>
                        m.moduleName == nameof(ModuleKoffeeExperiment) && m.moduleValues.GetValue(KoffeeIdentifiers.ContainerStatusValue) == "Running")))
                {
                    existing.NextChangepoint = now;
                    processor.MarkDirty();
                }
                return;
            }
            if (processor.Vessel.protoVessel?.protoPartSnapshots == null) return;
            var accounts = Accounts(processor);
            StationExperimentBackground.TickPowered(processor.Vessel, now, accounts, (_, a, b) => 0);
            // Never bill an interval predating registration in the solver.
            var behaviour = new KoffeeResearchPowerBehaviour { Vessel = processor.Vessel, AppliedUT = now };
            behaviour.RememberBaseline(now);
            var converter = new BrpConverter(behaviour) { Priority = -10 };
            processor.AddConverter(converter,
                new AddConverterOptions { LinkToAll = true });
            // LinkToAll refreshes the recipe to establish its EC connections.
            // A freshly added unloaded converter still needs a solver pass:
            // MarkDirty alone does not request one when constraints are stable.
            // Invalidate the cached inputs and refresh them at this exact UT.
            converter.Inputs.Clear();
            converter.NextChangepoint = now;
            processor.MarkDirty();
        }

        private void OnRecord(BackgroundResourceProcessor processor)
        {
            // Snapshot creation can precede the last loaded coordinator tick.
            // Copy the currently published modules without manufacturing work.
            foreach (var snapshot in processor.Vessel.protoVessel.protoPartSnapshots)
            {
                Part? live = processor.Vessel.parts.FirstOrDefault(p => p.flightID == snapshot.flightID);
                if (live == null) continue;
                foreach (var module in live.Modules.OfType<ModuleKoffeeExperiment>())
                {
                    var target = snapshot.modules.FirstOrDefault(m => m.moduleName == nameof(ModuleKoffeeExperiment));
                    if (target == null) continue;
                    var node = target.moduleValues.CreateCopy(); module.OnSave(node); target.moduleValues = node;
                }
            }
            Ensure(processor, processor.LastChangepoint);
        }

        private void OnStateUpdate(BackgroundResourceProcessor processor, ChangepointEvent change)
        {
            foreach (var converter in processor.Converters)
                if (converter.Behaviour is KoffeeResearchPowerBehaviour behaviour)
                    behaviour.Publish(processor, change);
        }

        private void OnRestore(BackgroundResourceProcessor processor)
        {
            // BRP emits the final state update before applying inventories and
            // this restore callback. Live PartModules may already have loaded
            // their older snapshot; refresh the scientific state once here.
            foreach (var snapshot in processor.Vessel.protoVessel.protoPartSnapshots)
            {
                var saved = snapshot.modules.FirstOrDefault(m => m.moduleName == nameof(ModuleKoffeeExperiment));
                if (saved == null) continue;
                Part? live = processor.Vessel.parts.FirstOrDefault(p => p.flightID == snapshot.flightID);
                foreach (var module in live?.Modules.OfType<ModuleKoffeeExperiment>() ?? Enumerable.Empty<ModuleKoffeeExperiment>())
                    module.OnLoad(saved.moduleValues);
            }
        }

        internal static List<IResourceAccount> Accounts(BackgroundResourceProcessor processor) => processor.Inventories
            .Where(i => i.ModuleId == null && i.ResourceName == KoffeeIdentifiers.ResourceName && i.Snapshot != null)
            .Select(i => (IResourceAccount)new InventoryAccount(i, processor.Vessel.protoVessel.protoPartSnapshots
                .FirstOrDefault(p => p.flightID == i.FlightId)?.resourcePriorityOffset ?? 0)).ToList();

        private sealed class InventoryAccount : IResourceAccount
        {
            private readonly ResourceInventory inventory;
            public int Priority { get; }
            internal InventoryAccount(ResourceInventory inventory, int priority) { this.inventory = inventory; Priority = priority; }
            public double Amount
            {
                get => inventory.Amount;
                set { inventory.Amount = value; inventory.OriginalAmount = value; if (inventory.Snapshot != null) inventory.Snapshot.amount = value; }
            }
            public double Capacity => inventory.MaxAmount;
            public bool CanWithdraw => inventory.Snapshot?.flowState == true;
            public uint SourceId => inventory.FlightId;
        }

        public void Dispose()
        {
            BackgroundResourceProcessor.onVesselRecord.Remove(OnRecord);
            BackgroundResourceProcessor.onStateUpdate.Remove(OnStateUpdate);
            BackgroundResourceProcessor.onVesselRestore.Remove(OnRestore);
        }
    }
}

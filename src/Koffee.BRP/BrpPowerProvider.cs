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
    public sealed class BrpPowerProvider : IStationBackgroundElectricity, IStationBackgroundSaveFlush
    {
        private readonly HashSet<Guid> missing = new HashSet<Guid>();
        private readonly BrpRestoreQueue restores = new BrpRestoreQueue();
        private static readonly HashSet<BrpPowerProvider> instances = new HashSet<BrpPowerProvider>();
        internal static void AfterInitialize(Vessel __instance)
        { foreach (var instance in instances.ToArray()) instance.restores.AfterInitialize(__instance); }
        internal static void ContinueRestores()
        { foreach (var instance in instances.ToArray()) instance.restores.Tick(); }
        internal static void CancelRestores()
        { foreach (var instance in instances.ToArray()) instance.restores.Clear(); EngineeringPowerPatches.ClearDiagnostics(); }
        private BrpPowerProvider()
        {
            EngineeringPowerPatches.Install();
            EmergencyGeneration.Install();
            instances.Add(this);
            BackgroundResourceProcessor.onVesselRecord.Add(OnRecord);
            BackgroundResourceProcessor.onStateUpdate.Add(OnStateUpdate);
            BackgroundResourceProcessor.onVesselRestore.Add(OnRestore);
        }

        // Called reflectively only after BRP's API has been checked. The core
        // assembly never acquires a reference to any BRP type.
        public static object Create() => new BrpPowerProvider();
        public void FlushBeforeSave(Vessel vessel, double now)
        {
            Ensure(vessel, now);
            // Let BRP's own authority settle to UT before KSP serializes crew,
            // inventories and scenarios. Do not replay research after Save.
            vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>()?.UpdateBackgroundState();
        }

        public void Ensure(Vessel vessel, double now)
        {
            if (StationVesselScope.IsNaturalObject(vessel)) return;
            var processor = vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
            if (processor == null)
            {
                if (missing.Add(vessel.id)) Debug.LogError("[Koffee] BRP selected at startup, but this vessel has no BRP processor; background research is waiting: " + vessel.id);
                return;
            }
            missing.Remove(vessel.id);
            Ensure(processor, now);
            // Publish current reserve between solver changepoints. BRP must
            // finish due boundaries before extending its authorized rate.
            if (!vessel.loaded && processor.NextChangepoint > now &&
                processor.Converters.All(c => c.NextChangepoint > now))
                processor.UpdateBackgroundState();
        }

        private static void Ensure(BackgroundResourceProcessor processor, double now)
        {
            if (StationVesselScope.IsNaturalObject(processor.Vessel)) return;
            EnsureKomfort(processor, now);
            EnsureEmergencyGeneration(processor, now);
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

        private static void EnsureKomfort(BackgroundResourceProcessor processor, double now)
        {
            var existing = processor.Converters.FirstOrDefault(c => c.Behaviour is KomfortPowerBehaviour);
            if (existing?.Behaviour is KomfortPowerBehaviour current)
            {
                bool primed = current.Prime(processor.LastChangepoint);
                bool optionsChanged = current.OptionsChanged(now);
                double boundary = current.PendingBoundary(now);
                if (primed || optionsChanged || !double.IsPositiveInfinity(boundary))
                {
                    foreach (var recipe in processor.Converters.Where(c => c.Behaviour is KomfortPowerBehaviour || c.Behaviour is KoffeeResearchPowerBehaviour || c.Behaviour is EmergencyGenerationBehaviour || c.Behaviour is EmergencyReactorGenerationBehaviour))
                        recipe.NextChangepoint = primed ? current.AppliedUT : Math.Min(now, boundary);
                    processor.MarkDirty(); processor.SuppressNoProgressError();
                }
                return;
            }
            var behaviour = new KomfortPowerBehaviour { Vessel = processor.Vessel, AppliedUT = now };
            behaviour.Prime(now);
            var converter = new BrpConverter(behaviour) { Priority = -10 };
            processor.AddConverter(converter, new AddConverterOptions { LinkToAll = true });
            converter.Inputs.Clear(); converter.NextChangepoint = now; processor.MarkDirty();
        }

        private static void EnsureEmergencyGeneration(BackgroundResourceProcessor processor, double now)
        {
            if (processor.Converters.Any(c => c.Behaviour is EmergencyGenerationBehaviour) ||
                EmergencySupport.Capture(processor.Vessel).Count == 0) return;
            var behaviour = new EmergencyGenerationBehaviour { Vessel = processor.Vessel };
            var converter = new BrpConverter(behaviour);
            processor.AddConverter(converter, new AddConverterOptions { LinkToAll = true });
            converter.NextChangepoint = now; processor.MarkDirty();
        }

        private void OnRecord(BackgroundResourceProcessor processor)
        {
            if (StationVesselScope.IsNaturalObject(processor.Vessel)) return;
            restores.BeforeRecord(processor);
            BrpNativeIdentity.Preserve(processor);
            // Snapshot creation can precede the last loaded coordinator tick.
            // Copy the currently published modules without manufacturing work.
            foreach (var snapshot in processor.Vessel.protoVessel.protoPartSnapshots)
            {
                Part? live = processor.Vessel.parts.FirstOrDefault(p => p.flightID == snapshot.flightID);
                if (live == null) continue;
                foreach (var service in live.Modules.OfType<ModuleKomfortService>())
                {
                    int index = live.Modules.OfType<ModuleKomfortService>().ToList().IndexOf(service);
                    var target = snapshot.modules.Where(m => m.moduleName == nameof(ModuleKomfortService)).ElementAtOrDefault(index);
                    if (target != null) { var node = target.moduleValues.CreateCopy(); service.OnSave(node); target.moduleValues = node; }
                }
                foreach (var emergency in live.Modules.OfType<ModuleEmergencySupport>())
                {
                    var target = snapshot.modules.FirstOrDefault(m => m.moduleName == nameof(ModuleEmergencySupport));
                    if (target != null) { var node = target.moduleValues.CreateCopy(); emergency.OnSave(node); target.moduleValues = node; }
                    if (emergency.UsesNfeReactor)
                    {
                        var reactor = EmergencyNfeReactor.Find(live);
                        var saved = snapshot.modules.FirstOrDefault(m => m.moduleName == EmergencyNfeReactor.ModuleName);
                        if (reactor != null && saved != null)
                        { var node = saved.moduleValues.CreateCopy(); reactor.OnSave(node); saved.moduleValues = node; }
                    }
                }
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
            if (StationVesselScope.IsNaturalObject(processor.Vessel)) return;
            restores.NewPublication(processor);
            // Publish service capacity before integrating the science interval.
            foreach (var converter in processor.Converters)
                if (converter.Behaviour is KomfortPowerBehaviour service) service.Publish(processor, change);
            foreach (var converter in processor.Converters)
                if (converter.Behaviour is KoffeeResearchPowerBehaviour behaviour)
                    behaviour.Publish(processor, change);
            EngineeringPowerPatches.Publish(processor, change);
        }

        private void OnRestore(BackgroundResourceProcessor processor)
        {
            if (!StationVesselScope.IsNaturalObject(processor.Vessel)) restores.Enqueue(processor);
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
            instances.Remove(this); restores.Clear();
            BackgroundResourceProcessor.onVesselRecord.Remove(OnRecord);
            BackgroundResourceProcessor.onStateUpdate.Remove(OnStateUpdate);
            BackgroundResourceProcessor.onVesselRestore.Remove(OnRestore);
        }
    }
}

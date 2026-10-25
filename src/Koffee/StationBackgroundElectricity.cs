using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("Koffee.BRP")]

namespace ShoAerospace.Koffee
{
    internal interface IStationBackgroundElectricity : IDisposable
    {
        void Ensure(Vessel vessel, double now);
    }

    internal static class StationBackgroundElectricity
    {
        private static readonly StationPowerProviderSelection selection = new StationPowerProviderSelection();
        internal static IStationBackgroundElectricity? Provider { get; private set; }
        internal static StationPowerProviderKind Kind => selection.Initialized ? selection.Provider : StationPowerProviderKind.Stock;
        internal static string ProviderId => Kind == StationPowerProviderKind.Brp ? "brp" : "stock";
        internal const string SchemaKey = "koffeePowerSchema";
        internal const string ProviderKey = "koffeePowerProvider";

        internal static void Initialize()
        {
            selection.Initialize(() =>
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                if (!StationBrpApi.Compatible(assemblies, out string detail))
                { Debug.Log("[Koffee] Background electricity: stock. " + detail); return false; }
                Assembly? adapter = assemblies.SingleOrDefault(a => a.GetName().Name == "Koffee.BRP");
                MethodInfo? factory = adapter?.GetType("ShoAerospace.Koffee.Brp.BrpPowerProvider")?
                    .GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
                Provider = factory?.Invoke(null, null) as IStationBackgroundElectricity;
                if (Provider == null) throw new InvalidOperationException("BRP is loaded but the Koffee.BRP adapter could not initialize.");
                Debug.Log("[Koffee] Background electricity: BRP; selection fixed until KSP exits. " + detail);
                return true;
            });
            if (selection.Failure != "") Debug.LogError("[Koffee] Background electricity: stock selected at startup. " + selection.Failure);
        }

        internal static bool Migrate(ProtoPartSnapshot part, ExperimentContainerState state, double now,
            out ExperimentContainerState updated)
        {
            updated = state;
            ProtoPartModuleSnapshot? module = Module(part);
            if (module?.moduleValues == null) return false;
            ConfigNode node = module.moduleValues;
            string? schema = node.GetValues(SchemaKey).Length > 1 || node.GetValues(ProviderKey).Length > 1 ?
                "invalid" : node.GetValue(SchemaKey);
            var continuity = new StationPowerSaveContinuity(schema, node.GetValue(ProviderKey), Kind, now);
            updated = continuity.Rebase(state);
            if (ReferenceEquals(updated, state)) return true;
            if (!ExperimentContainerSnapshotAdapter.Write(part, updated, out _)) return false;
            // Write the marker only after publishing the migrated checkpoint.
            Stamp(part);
            return true;
        }

        internal static void Stamp(ProtoPartSnapshot part)
        {
            var node = Module(part)!.moduleValues;
            node.SetValue(SchemaKey, StationPowerSaveMarker.Schema, true);
            node.SetValue(ProviderKey, ProviderId, true);
        }

        private static ProtoPartModuleSnapshot? Module(ProtoPartSnapshot part) => part.modules.FirstOrDefault(m =>
            m.moduleName == nameof(ModuleKoffeeExperiment) || m.moduleValues?.GetValue("name") == nameof(ModuleKoffeeExperiment));
    }
}

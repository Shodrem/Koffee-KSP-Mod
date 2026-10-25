using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // No CLR dependency on DBS. Fields/CFG provide the data; optional hooks
    // suppress duplicate stock lab rows only for explicitly opted-in converters.
    internal static class StationDbsIntegration
    {
        private static FieldInfo? enabled, handlers;
        private static PropertyInfo? handlerModule;
        private static bool installed;
        internal static bool Active
        {
            get
            {
                if (!installed || !(enabled?.GetValue(null) is bool on) || !on) return false;
                var entries = handlers?.GetValue(null) as IEnumerable;
                if (entries == null) return false; // Settings.Load has not run yet.
                object[] ours = entries.Cast<object>().Where(e => Field(e, "handledModule") as string == nameof(ModuleKoffeePowerConsumer)).ToArray();
                if (ours.Length != 1) return false;
                object entry = ours[0];
                object? config = Field(entry, "config");
                return Field(entry, "handlerModuleName") as string == "GenericFieldDataHandler" &&
                    Field(entry, "resourceType")?.ToString() == "Power" &&
                    Equals(Field(entry, "consumer"), true) && Equals(Field(entry, "producer"), false) &&
                    Equals(Field(entry, "continuous"), true) && Equals(Field(entry, "simulated"), true) &&
                    config != null && Field(config, "editorFieldName") as string == "dbsEditorDemand" &&
                    Field(config, "flightFieldName") as string == "dbsFlightDemand" &&
                    Equals(Field(config, "editorValueScalar"), -1d) && Equals(Field(config, "flightValueScalar"), -1d);
            }
        }

        internal static void Shutdown()
        {
            installed = false; enabled = handlers = null; handlerModule = null;
        }

        private static object? Field(object value, string name) => value.GetType().GetField(name)?.GetValue(value);

        internal static void Initialize(Harmony harmony)
        {
            if (installed) return;
            Assembly[] matches = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "DynamicBatteryStorage").ToArray();
            if (matches.Length == 0) { Debug.Log("[Koffee][DBS] Absent; stock warp policy retained."); return; }
            try
            {
                if (matches.Length != 1) throw new InvalidOperationException("Multiple DBS assemblies.");
                Assembly assembly = matches[0];
                Type settings = assembly.GetType("DynamicBatteryStorage.Settings", true)!;
                Type handler = assembly.GetType("DynamicBatteryStorage.ModuleScienceConverterPowerHandler", true)!;
                assembly.GetType("DynamicBatteryStorage.GenericFieldDataHandler", true);
                enabled = settings.GetField("Enabled");
                handlers = settings.GetField("HandlerPartModuleData");
                handlerModule = handler.GetProperty("PM");
                if (enabled?.FieldType != typeof(bool) || handlers == null || handlerModule?.PropertyType != typeof(PartModule))
                    throw new InvalidOperationException("Unsupported DBS settings/handler API.");
                foreach (string name in new[] { "GetValueEditor", "GetValueFlight" })
                {
                    MethodInfo target = handler.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) ??
                        throw new MissingMethodException(handler.FullName, name);
                    if (target.ReturnType != typeof(double) || target.GetParameters().Length != 0)
                        throw new InvalidOperationException("Unsupported DBS lab reader.");
                    harmony.Patch(target, postfix: new HarmonyMethod(typeof(StationDbsIntegration), nameof(LaboratoryValue)));
                }
                installed = true;
                Debug.Log("[Koffee][DBS] Optional handlers ready: " + assembly.GetName().FullName +
                    "; loaded warp billing requires Enabled and the effective Koffee CFG handler.");
            }
            catch (Exception error)
            {
                installed = false;
                Debug.LogError("[Koffee][DBS] Compatibility unavailable; stock warp policy retained. " + error);
            }
        }

        private static void LaboratoryValue(object __instance, ref double __result)
        {
            // Also suppress in the editor. Never offset this row with a positive
            // generic value: DBS would classify that offset as a generator.
            var module = handlerModule?.GetValue(__instance, null) as PartModule;
            if (Active && StationLaboratoryPower.Enabled(module) &&
                module!.part.Modules.OfType<ModuleKoffeePowerConsumer>().Any()) __result = 0;
        }
    }

    internal sealed class StationDbsResourceBroker : IResourceBroker
    {
        internal double Requested { get; private set; }
        internal double Received { get; private set; }
        private readonly ResourceBroker stock = new ResourceBroker();
        // A broker delta of .02 bypasses only EC time scaling, while keeping the
        // real stock connectivity, priorities and flow locks. No time is simulated.
        public double AmountAvailable(Part part, string name, double deltaTime, ResourceFlowMode flowMode) =>
            stock.AmountAvailable(part, name, .02, flowMode);
        public double AmountAvailable(Part part, int resID, double deltaTime, ResourceFlowMode flowMode) =>
            stock.AmountAvailable(part, resID, .02, flowMode);
        public double RequestResource(Part part, string name, double amount, double deltaTime, ResourceFlowMode flowMode) =>
            Record(amount, stock.RequestResource(part, name, amount, .02, flowMode));
        public double RequestResource(Part part, int resID, double amount, double deltaTime, ResourceFlowMode flowMode) =>
            Record(amount, stock.RequestResource(part, resID, amount, .02, flowMode));
        // Unused by the EC-only recipe; preserve stock output semantics.
        public double StorageAvailable(Part part, string name, double deltaTime, ResourceFlowMode mode, double fillAmount) =>
            stock.StorageAvailable(part, name, deltaTime, mode, fillAmount);
        public double StorageAvailable(Part part, int id, double deltaTime, ResourceFlowMode mode, double fillAmount) =>
            stock.StorageAvailable(part, id, deltaTime, mode, fillAmount);
        public double StoreResource(Part part, string name, double amount, double deltaTime, ResourceFlowMode mode) =>
            stock.StoreResource(part, name, amount, deltaTime, mode);
        public double StoreResource(Part part, int id, double amount, double deltaTime, ResourceFlowMode mode) =>
            stock.StoreResource(part, id, amount, deltaTime, mode);
        private double Record(double requested, double received)
        {
            Requested += requested;
            Received += received;
            return received;
        }
    }
}

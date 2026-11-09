using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace ShoAerospace.Koffee
{
    // Optional native SystemHeat/NFE surface. No CLR reference to either mod.
    internal static class EmergencyNfeReactor
    {
        internal const string ModuleName = "ModuleSystemHeatFissionReactor";
        internal const string HeatId = "KoffeeEmergencyReactor";
        internal const double TenKerbinYears = 92016000;
        internal const double FuelReserve = 1e-6;
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static PartModule? Find(Part part) => part.Modules.Cast<PartModule>()
            .SingleOrDefault(m => m.GetType().FullName == "SystemHeat." + ModuleName);
        private static object? Read(object module, string field) => module.GetType().GetField(field, Flags)?.GetValue(module);
        private static double Number(object? value) => Convert.ToDouble(value, CultureInfo.InvariantCulture);
        private static void Write(object module, string field, object value) => module.GetType().GetField(field, Flags)!.SetValue(module, value);
        private static bool Near(double a, double b) => EngineeringMath.Finite(a) && EngineeringMath.Finite(b) && Math.Abs(a - b) <= Math.Max(1e-18, Math.Abs(b) * 1e-5);

        internal static bool Valid(ModuleEmergencySupport owner)
        {
            try
            {
                var reactor = Find(owner.part);
                if (reactor == null || owner.part.Modules.OfType<ModuleGenerator>().Any() ||
                    Read(reactor, "moduleID") as string != HeatId || Read(reactor, "systemHeatModuleID") as string != HeatId ||
                    owner.Service?.heatKilowattsPerKerbal != 0 || !(owner.reactorFuelPerSecond > 0) ||
                    !EngineeringMath.Finite(owner.reactorFuelPerSecond) ||
                    !Equals(Read(reactor, "GeneratesElectricity"), true) ||
                    reactor.GetType().GetMethod("EnableReactor", Type.EmptyTypes) == null ||
                    reactor.GetType().GetMethod("DisableReactor", Type.EmptyTypes) == null) return false;
                var heats = owner.part.Modules.Cast<PartModule>().Where(m => m.GetType().FullName == "SystemHeat.ModuleSystemHeat").ToArray();
                if (heats.Length != 1 || Read(heats[0], "moduleID") as string != HeatId) return false;
                double Curve(string name)
                {
                    object curve = Read(reactor, name)!;
                    return Number(curve.GetType().GetMethod("Evaluate", new[] { typeof(float) })!.Invoke(curve, new object[] { 100f }));
                }
                bool Ratio(string field, string resource)
                {
                    var entries = ((IEnumerable)Read(reactor, field)!).Cast<object>().ToArray();
                    return entries.Length == 1 && Read(entries[0], "ResourceName") as string == resource &&
                        Near(Number(Read(entries[0], "Ratio")), owner.reactorFuelPerSecond) &&
                        Read(entries[0], "FlowMode")?.ToString() == "NO_FLOW" &&
                        (field != "outputs" || Equals(Read(entries[0], "DumpExcess"), false));
                }
                return Near(Curve("ElectricalGeneration"), owner.electricChargePerSecond) &&
                    Near(Curve("HeatGeneration"), owner.heatKilowatts + owner.electricChargePerSecond) &&
                    Read(reactor, "FuelName") as string == "EnrichedUranium" &&
                    Ratio("inputs", "EnrichedUranium") && Ratio("outputs", "DepletedFuel");
            }
            catch { return false; } // Unknown API/configuration cannot grant emergency coverage.
        }

        internal static void Switch(Part part, bool on)
        {
            var reactor = Find(part) ?? throw new InvalidOperationException("Missing emergency NFE reactor.");
            if (Equals(Read(reactor, "Enabled"), on)) return;
            Write(reactor, "LastUpdateTime", Planetarium.GetUniversalTime());
            if (on)
            {
                Write(reactor, "ManualControl", true);
                Write(reactor, "CurrentReactorThrottle", 100f);
                Write(reactor, "CurrentThrottle", 100f);
            }
            reactor.GetType().GetMethod(on ? "EnableReactor" : "DisableReactor", Type.EmptyTypes)!.Invoke(reactor, null);
            if (!on)
            {
                Write(reactor, "CurrentThrottle", 0f);
                Write(reactor, "CurrentElectricalGeneration", 0f);
            }
        }

        internal static void SwitchSnapshot(ConfigNode node, bool on, double now)
        {
            node.SetValue("Enabled", on.ToString(), true);
            node.SetValue("ManualControl", "True", true);
            node.SetValue("HibernateOnWarp", "False", true);
            node.SetValue("Hibernating", "False", true);
            node.SetValue("FirstLoad", "False", true);
            node.SetValue("CurrentReactorThrottle", "100", true);
            node.SetValue("CurrentThrottle", on ? "100" : "0", true);
            node.SetValue("LastUpdateTime", now.ToString("R", CultureInfo.InvariantCulture), true);
            if (!on) node.SetValue("CurrentElectricalGeneration", "0", true);
        }

        internal static void HideControls(Part part)
        {
            var reactor = Find(part);
            if (reactor == null) return;
            foreach (string name in new[] { "EnableReactor", "DisableReactor", "EnableManual", "DisableManual", "EnableHibernate", "DisableHibernate" })
                if (reactor.Events[name] != null) reactor.Events[name].guiActive = reactor.Events[name].guiActiveEditor = false;
            foreach (BaseAction action in reactor.Actions) action.active = false;
        }
    }
}

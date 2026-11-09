using System;
using System.Linq;
using BackgroundResourceProcessing;
using BackgroundResourceProcessing.Behaviour;
using BackgroundResourceProcessing.Converter;
using HarmonyLib;

namespace ShoAerospace.Koffee.Brp
{
    internal static class EmergencyGeneration
    {
        private static Harmony? harmony;
        internal static void Install()
        {
            if (harmony != null) return;
            harmony = new Harmony("ShoAerospace.Koffee.Brp.EmergencyGeneration");
            // BRP 0.3.2 evaluates ModuleGenerator's ActiveCondition only when
            // recording native modules. Its constant recipe cannot follow a
            // later automatic activation on an unloaded vessel.
            harmony.Patch(AccessTools.DeclaredMethod(typeof(BackgroundGenericConverter), "GetBehaviour"),
                prefix: new HarmonyMethod(typeof(EmergencyGeneration), nameof(NativeGenerator)));
            var adapter = AccessTools.TypeByName("BackgroundResourceProcessing.Integration.SystemHeat.BackgroundSystemHeatFissionReactor");
            var reactor = AccessTools.TypeByName("SystemHeat.ModuleSystemHeatFissionReactor");
            if (adapter != null && reactor != null)
                harmony.Patch(adapter.GetMethod("GetBehaviour", new[] { reactor }),
                    prefix: new HarmonyMethod(typeof(EmergencyGeneration), nameof(NativeReactor)));
        }
        private static bool NativeReactor(PartModule __0, ref ModuleBehaviour? __result)
        {
            var owners = __0.part.Modules.OfType<ModuleEmergencySupport>().ToArray();
            if (owners.Length != 1 || !owners[0].UsesNfeReactor || !owners[0].Configured) return true;
            var owner = owners[0];
            var heat = __0.part.Modules.Cast<PartModule>().Single(m => m.GetType().FullName == "SystemHeat.ModuleSystemHeat");
            if (!(heat.GetType().GetField("currentLoopID")?.GetValue(heat) is int loopId)) return true;
            __result = new ModuleBehaviour(new EmergencyReactorGenerationBehaviour {
                PartId = __0.part.persistentId, ElectricalRate = owner.electricChargePerSecond,
                WasteHeatRate = owner.heatKilowatts, FuelRate = owner.reactorFuelPerSecond,
                LoopId = loopId });
            var marker = __0.vessel?.rootPart?.Modules.Cast<PartModule>()
                .FirstOrDefault(m => m.GetType().Name == "BRPSystemHeatMarker");
            if (marker != null) __result.AddPushModule(marker);
            return false;
        }
        private static bool NativeGenerator(PartModule __0, ref ModuleBehaviour? __result)
        {
            if (!(__0 is ModuleGenerator) || !__0.part.Modules.OfType<ModuleEmergencySupport>().Any()) return true;
            // Exactly this apparatus uses the dynamic BRP output recipe below.
            // Other generators and every loaded native ModuleGenerator remain stock.
            __result = null; return false;
        }
    }

    public sealed class EmergencyGenerationBehaviour : ConverterBehaviour
    {
        public override ConverterResources GetResources(VesselState state)
        {
            double now = state.CurrentTime;
            var result = new ConverterResources { NextChangepoint = KomfortRuntime.NextBoundary(Vessel, now) };
            var reserve = DeadlyScenario.Read(Vessel.id);
            if (reserve == null || !reserve.Initialized) return result;
            if (now < reserve.UT) { result.NextChangepoint = reserve.UT; return result; }
            if (!reserve.Emergency || !DeadlyScenario.OptionsAt(now).Enabled || !KomfortRuntime.OptionsAt(now).Enabled ||
                DeadlyScenario.Members(Vessel).Length == 0) return result;
            double rate = EmergencySupport.Capture(Vessel).Where(d => !d.NativeReactor).Sum(d => d.GenerationRate);
            if (rate > 0) result.Outputs.Add(new ResourceRatio("ElectricCharge", rate, true, ResourceFlowMode.STAGE_PRIORITY_FLOW));
            return result;
        }
    }
}

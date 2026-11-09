using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Optional API only: Koffee must load without SystemHeat's assembly.
    internal static partial class KomfortSystemHeat
    {
        internal const string ModuleId = "KoffeeClimate";
        private const string SourceId = "Koffee.Climate";
        internal static bool Present { get; private set; }
        private static bool active, failed;
        private static Type? heatType;
        private static FieldInfo id = null!, used = null!;
        private static FieldInfo totalFlux = null!, loopTemperature = null!, loopId = null!;
        private static PropertyInfo loop = null!, loops = null!, members = null!;
        private static MethodInfo addFlux = null!;
        private sealed class Pending
        {
            internal double Energy, UT, Step;
        }
        private static ConditionalWeakTable<PartModule, Pending> pending = new ConditionalWeakTable<PartModule, Pending>();

        internal static void Initialize(Harmony harmony)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "SystemHeat").ToArray();
            Present = assemblies.Length != 0;
            if (!Present) { Debug.Log("[Koffee][SystemHeat] Absent; stock climate heat retained."); return; }
            try
            {
                if (assemblies.Length != 1) throw new InvalidOperationException("Multiple SystemHeat assemblies.");
                Configure(assemblies[0]);
                var simulator = assemblies[0].GetType("SystemHeat.SystemHeatSimulator", true)!;
                foreach (string method in new[] { "Simulate", "SimulateEditor" })
                {
                    var target = simulator.GetMethod(method, Type.EmptyTypes);
                    if (target == null || target.ReturnType != typeof(void)) throw new MissingMethodException(simulator.FullName, method);
                    if (method == "Simulate")
                        harmony.Patch(target,
                            prefix: new HarmonyMethod(typeof(KomfortSystemHeat), nameof(BeforeFlightSimulation)),
                            postfix: new HarmonyMethod(typeof(KomfortSystemHeat), nameof(AfterFlightSimulation)));
                    else harmony.Patch(target, prefix: new HarmonyMethod(typeof(KomfortSystemHeat), nameof(BeforeSimulation)));
                }
                Debug.Log("[Koffee][SystemHeat] Climate loop backend ready: " + assemblies[0].FullName);
            }
            catch (Exception error) { active = false; Failure(error); }
        }

        // Validate the complete boundary before any paid heat can enter it.
        internal static void Configure(Assembly assembly)
        {
            Present = true; active = false;
            heatType = assembly.GetType("SystemHeat.ModuleSystemHeat", true)!;
            var simulator = assembly.GetType("SystemHeat.SystemHeatSimulator", true)!;
            var heatLoop = assembly.GetType("SystemHeat.HeatLoop", true)!;
            if (!typeof(PartModule).IsAssignableFrom(heatType)) throw new InvalidOperationException("Invalid SystemHeat module.");
            id = Field("moduleID", typeof(string)); used = Field("moduleUsed", typeof(bool));
            totalFlux = Field("totalSystemFlux", typeof(float));
            loopTemperature = Field("currentLoopTemperature", typeof(float)); loopId = Field("currentLoopID", typeof(int));
            loop = heatType.GetProperty("Loop") ?? throw new MissingMemberException("SystemHeat.ModuleSystemHeat.Loop");
            loops = simulator.GetProperty("HeatLoops") ?? throw new MissingMemberException("SystemHeatSimulator.HeatLoops");
            members = heatLoop.GetProperty("LoopModules") ?? throw new MissingMemberException("HeatLoop.LoopModules");
            addFlux = heatType.GetMethod("AddFlux", new[] { typeof(string), typeof(float), typeof(float), typeof(bool) }) ??
                throw new MissingMethodException("SystemHeat.ModuleSystemHeat.AddFlux");
            overheatMarker = typeof(Part).GetField("explodeOverheat", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ??
                throw new MissingFieldException("Part.explodeOverheat");
            if (overheatMarker.FieldType != typeof(bool)) throw new InvalidOperationException("Unsupported stock overheat marker.");
            if (loop.PropertyType != heatLoop || !typeof(IEnumerable).IsAssignableFrom(loops.PropertyType) ||
                !typeof(IEnumerable).IsAssignableFrom(members.PropertyType) || addFlux.ReturnType != typeof(void))
                throw new InvalidOperationException("Unsupported SystemHeat API.");
            active = true; failed = false;
        }
        private static FieldInfo Field(string name, Type type)
        {
            var field = heatType!.GetField(name);
            if (field?.FieldType != type) throw new MissingFieldException(heatType.FullName, name);
            return field;
        }
        internal static void Clear()
        {
            pending = new ConditionalWeakTable<PartModule, Pending>();
            ClearFailureHistory();
        }
        internal static void Shutdown() { Clear(); Present = active = failed = false; heatType = null; }

        private static PartModule? Module(Part part)
        {
            if (!active) return null;
            foreach (PartModule module in part.Modules)
                if (heatType!.IsInstanceOfType(module) && id.GetValue(module) as string == ModuleId) return module;
            return null;
        }
        internal static string Readiness(Part part)
        {
            try
            {
                if (!active) return "systemheat-unavailable";
                var module = Module(part);
                if (module == null) return "systemheat-missing-module";
                return used.GetValue(module) is bool enabled && enabled && loop.GetValue(module) != null ? "ready" : "initializing";
            }
            catch (Exception error) { Failure(error); return "systemheat-unavailable"; }
        }
        internal static bool Queue(Part part, double energy)
        {
            try
            {
                var module = Module(part);
                if (module == null) return false;
                double now = Planetarium.GetUniversalTime(), step = TimeWarp.fixedDeltaTime;
                if (!ExperimentContainerState.Finite(now) || !ExperimentContainerState.Finite(step) || step <= 0 ||
                    !ExperimentContainerState.Finite(energy) || energy < 0) return false;
                var entry = pending.GetOrCreateValue(module);
                // Only slices closed at this same UT can be combined. Old debt expires.
                if (entry.UT != now) entry.Energy = 0;
                entry.UT = now; entry.Step = step; entry.Energy += energy;
                return true;
            }
            catch (Exception error) { Failure(error); return false; }
        }

        // AddFlux REPLACES a persistent source; it does not add energy. Publish
        // the sum of paid slices once immediately before each native integration.
        // Unity may close the paid interval after this callback: allow one fixed
        // step of latency, never replay a gap or retain last frame's power.
        internal static void BeforeSimulation(object __instance)
        {
            if (!active) return;
            try
            {
                var heatLoops = loops.GetValue(__instance) as IEnumerable;
                if (heatLoops == null) return;
                double now = Planetarium.GetUniversalTime(), step = TimeWarp.fixedDeltaTime;
                foreach (object heatLoop in heatLoops)
                    foreach (PartModule module in (IEnumerable)members.GetValue(heatLoop)!)
                    {
                        if (id.GetValue(module) as string != ModuleId) continue;
                        double power = 0;
                        if (HighLogic.LoadedSceneIsEditor) power = EditorPower(module.part);
                        else if (pending.TryGetValue(module, out var entry))
                        {
                            double age = now - entry.UT;
                            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && module.part.vessel?.loaded == true &&
                                module.part.State != PartStates.DEAD && used.GetValue(module) is bool enabled && enabled &&
                                KomfortRuntime.OptionsAt(now).Enabled && ExperimentContainerState.Finite(step) && step > 0 &&
                                age >= -1e-6 && age <= Math.Max(step, entry.Step) * 1.1 + 1e-6)
                                power = entry.Energy / step;
                            entry.Energy = 0;
                        }
                        if (!ExperimentContainerState.Finite(power) || power < 0 || power > float.MaxValue) power = 0;
                        // 350 K is a configured coolant outlet target, not a cabin reading.
                        addFlux.Invoke(module, new object[] { SourceId, 350f, (float)power, power > 0 });
                        if (HighLogic.LoadedSceneIsFlight && KomfortThermalDiagnostics.Enabled)
                            KomfortThermalDiagnostics.SystemHeatPublished(module.part, power,
                                (float)totalFlux.GetValue(module)!, (float)loopTemperature.GetValue(module)!, (int)loopId.GetValue(module)!);
                    }
            }
            catch (Exception error) { Clear(); Failure(error); }
        }
        private static double EditorPower(Part part)
        {
            var parts = EditorLogic.fetch?.ship?.parts;
            int? crew = KoffeeEditorKomfort.Capture().CrewCount;
            if (parts == null || !crew.HasValue || !KomfortRuntime.OptionsAt(Planetarium.GetUniversalTime()).Enabled) return 0;
            double capacity = parts.Where(p => p != null && p.State != PartStates.DEAD)
                .SelectMany(p => p.Modules.OfType<ModuleKomfortService>())
                .Where(m => m.Valid && m.serviceEnabled && m.service == "Climate").Sum(m => m.capacity);
            double coverage = capacity > 0 ? Math.Min(1, crew.Value / capacity) : 0;
            return part.Modules.OfType<ModuleKomfortService>().Where(m => m.Valid && m.serviceEnabled && m.service == "Climate")
                .Sum(m => m.capacity * coverage * Math.Max(0, m.heatKilowattsPerKerbal));
        }
        private static void Failure(Exception error)
        {
            if (failed) return;
            failed = true;
            Debug.LogError("[Koffee][SystemHeat] Climate thermal integration error; stock heat remains excluded: " + error);
        }
    }
}

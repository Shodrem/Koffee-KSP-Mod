using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static partial class KomfortSystemHeat
    {
        private static FieldInfo overheatMarker = null!;
        internal sealed class FlightPass
        {
            internal object Simulator = null!;
            internal double UT, Step;
            internal bool Consumed;
            internal readonly List<LoopMember> Members = new List<LoopMember>();
        }
        internal sealed class LoopMember
        {
            internal PartModule Module = null!;
            internal Part Part = null!;
            internal Vessel Vessel = null!;
            internal object Loop = null!;
            internal int Id;
        }
        private sealed class ThermalObservation
        {
            internal object Simulator = null!, Loop = null!;
            internal Vessel Vessel = null!;
            internal double UT, Step;
            internal int Id;
            internal bool FailureIssued;
        }
        private static ConditionalWeakTable<PartModule, ThermalObservation> thermalObservations =
            new ConditionalWeakTable<PartModule, ThermalObservation>();
        private static void ClearFailureHistory() => thermalObservations = new ConditionalWeakTable<PartModule, ThermalObservation>();

        // State belongs to this particular native call. The postfix is not run
        // when the native simulator throws, and the editor has no damage hook.
        internal static void BeforeFlightSimulation(object __instance, out FlightPass? __state)
        {
            __state = null;
            BeforeSimulation(__instance);
            if (!active || !HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready) return;
            double now = Planetarium.GetUniversalTime(), step = TimeWarp.fixedDeltaTime;
            if (!ExperimentContainerState.Finite(now) || !ExperimentContainerState.Finite(step) || step <= 0) return;
            try
            {
                var heatLoops = loops.GetValue(__instance) as IEnumerable;
                if (heatLoops == null) return;
                var pass = new FlightPass { Simulator = __instance, UT = now, Step = step };
                foreach (object heatLoop in heatLoops)
                    foreach (PartModule module in (IEnumerable)members.GetValue(heatLoop)!)
                    {
                        if (id.GetValue(module) as string != ModuleId) continue;
                        var part = module.part;
                        if (part == null || part.State == PartStates.DEAD || part.vessel?.loaded != true ||
                            !(used.GetValue(module) is bool enabled) || !enabled ||
                            !ReferenceEquals(loop.GetValue(module), heatLoop))
                        { thermalObservations.Remove(module); continue; }
                        pass.Members.Add(new LoopMember { Module = module, Part = part, Vessel = part.vessel,
                            Loop = heatLoop, Id = (int)loopId.GetValue(module)! });
                    }
                __state = pass;
            }
            catch (Exception error) { ClearFailureHistory(); Failure(error); }
        }

        internal static void AfterFlightSimulation(object __instance, FlightPass? __state, bool __runOriginal = true)
        {
            if (__state == null || __state.Consumed) return;
            __state.Consumed = true;
            double now = Planetarium.GetUniversalTime();
            if (!__runOriginal || !active || !ReferenceEquals(__instance, __state.Simulator) || !HighLogic.LoadedSceneIsFlight ||
                !FlightGlobals.ready || !ExperimentContainerState.Finite(now) || Math.Abs(now - __state.UT) > 1e-6) return;
            try
            {
                // This is a copy captured before integration. Destroying a part
                // here cannot invalidate the native loop/module enumerators.
                foreach (var member in __state.Members)
                {
                    var part = member.Part;
                    if (part == null || part.State == PartStates.DEAD || part.vessel?.loaded != true ||
                        !ReferenceEquals(part.vessel, member.Vessel) ||
                        !(used.GetValue(member.Module) is bool enabled) || !enabled ||
                        !ReferenceEquals(loop.GetValue(member.Module), member.Loop) ||
                        (int)loopId.GetValue(member.Module)! != member.Id || !StillInSimulation(__instance, member))
                    { thermalObservations.Remove(member.Module); continue; }
                    double temperature = (float)loopTemperature.GetValue(member.Module)!;
                    if (!ExperimentContainerState.Finite(temperature) || temperature <= 0 ||
                        !ExperimentContainerState.Finite(part.maxTemp) || part.maxTemp <= 0)
                    { thermalObservations.Remove(member.Module); continue; }

                    var previous = thermalObservations.GetOrCreateValue(member.Module);
                    double elapsed = __state.UT - previous.UT;
                    bool continuous = ReferenceEquals(previous.Simulator, __instance) &&
                        ReferenceEquals(previous.Loop, member.Loop) && ReferenceEquals(previous.Vessel, part.vessel) &&
                        previous.Id == member.Id && elapsed > 0 &&
                        elapsed <= Math.Max(previous.Step, __state.Step) * 1.1 + 1e-6;
                    bool alreadyFailed = previous.FailureIssued;
                    previous.Simulator = __instance; previous.Loop = member.Loop; previous.Vessel = part.vessel;
                    previous.UT = __state.UT; previous.Step = __state.Step; previous.Id = member.Id;
                    // The first valid pass after load, a gap or a circuit change
                    // establishes a baseline. Saved temperatures never cause a
                    // retrospective explosion before fresh native observation.
                    if (!continuous || alreadyFailed || CheatOptions.IgnoreMaxTemperature || temperature <= part.maxTemp) continue;
                    previous.FailureIssued = true;
                    DestroyOverheatedPart(part, temperature, member.Id);
                }
            }
            catch (Exception error) { ClearFailureHistory(); Failure(error); }
        }

        private static bool StillInSimulation(object simulator, LoopMember member)
        {
            foreach (object candidate in (IEnumerable)loops.GetValue(simulator)!)
                if (ReferenceEquals(candidate, member.Loop))
                    foreach (object module in (IEnumerable)members.GetValue(candidate)!)
                        if (ReferenceEquals(module, member.Module)) return true;
            return false;
        }

        private static void DestroyOverheatedPart(Part part, double temperature, int circuit)
        {
            try
            {
                Debug.Log("[Koffee][ClimateHeat] backend=SystemHeat event=overheat part=" + part.persistentId +
                    " sampleUT=" + Planetarium.GetUniversalTime().ToString("G9", CultureInfo.InvariantCulture) +
                    " loopID=" + circuit + " loopK=" + temperature.ToString("G9", CultureInfo.InvariantCulture) +
                    " limitK=" + part.maxTemp.ToString("G9", CultureInfo.InvariantCulture));
            }
            catch (Exception) { } // Diagnostics must not suppress thermal failure.
            overheatMarker.SetValue(part, true);
            try
            {
                // Preserve stock flight-report and destruction paths. The label
                // explicitly identifies the circuit rather than a stock reading.
                GameEvents.onOverheat.Fire(new EventReport(FlightEvents.OVERHEAT, part,
                    part.partInfo?.title ?? part.name, "", 0,
                    "System Heat: " + temperature.ToString("F0", CultureInfo.CurrentCulture) + " / " +
                    part.maxTemp.ToString("F0", CultureInfo.CurrentCulture) + " K", part.explosionPotential));
            }
            finally { if (part != null && part.State != PartStates.DEAD) part.explode(); }
        }
    }
}

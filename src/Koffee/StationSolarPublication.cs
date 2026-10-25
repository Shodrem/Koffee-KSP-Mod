using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Observe stock rather than calling its thermal methods to manufacture a reading.
    // Update prepares the next solar input; FixedUpdate publishes it to Vessel.
    // Keep the last completed publication while a coherent render input is pending.
    internal static class StationSolarPublication
    {
        internal sealed class Sample
        {
            public Sample() { }
            internal Vessel Vessel = null!;
            internal FlightIntegrator Integrator = null!;
            internal object? Proto, Game;
            internal CelestialBody Body = null!;
            internal string Situation = "";
            internal double SunUT, UT, Flux;
            internal bool Sunlight, Active, SunCalculated, Published, Complete;
            internal bool StockSunlight;
            internal string Source = "StockFlightIntegrator";
            internal double SolarFlux;
            internal int PartCount;
            internal int SunFrame;
            internal float PhysicsTime;
            internal Sample Snapshot() => (Sample)MemberwiseClone();
        }
        private static ConditionalWeakTable<FlightIntegrator, Sample> integrators = new ConditionalWeakTable<FlightIntegrator, Sample>();
        private static ConditionalWeakTable<Vessel, Sample> publications = new ConditionalWeakTable<Vessel, Sample>();
        internal static void Clear()
        { integrators = new ConditionalWeakTable<FlightIntegrator, Sample>(); publications = new ConditionalWeakTable<Vessel, Sample>(); }
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(FlightIntegrator), "FixedUpdate"),
                prefix: new HarmonyMethod(typeof(StationSolarPublication), nameof(BeginHook)),
                finalizer: new HarmonyMethod(typeof(StationSolarPublication), nameof(EndHook)));
            harmony.Patch(AccessTools.Method(typeof(FlightIntegrator), "CalculateSunBodyFlux"),
                postfix: new HarmonyMethod(typeof(StationSolarPublication), nameof(SunHook)));
            foreach (string name in new[] { "CalculateConstantsVacuum", "CalculateConstantsAtmosphere" })
                harmony.Patch(AccessTools.Method(typeof(FlightIntegrator), name),
                    postfix: new HarmonyMethod(typeof(StationSolarPublication), nameof(ConstantsHook)));
            Type? modular = AccessTools.TypeByName("ModularFI.ModularFlightIntegrator");
            if (modular != null)
            {
                harmony.Patch(AccessTools.DeclaredMethod(modular, "CalculateSunBodyFlux"),
                    prefix: new HarmonyMethod(typeof(StationSolarPublication), nameof(ModularBeginHook)),
                    finalizer: new HarmonyMethod(typeof(StationSolarPublication), nameof(ModularEndHook)));
                foreach (string name in new[] { "CalculateConstantsVacuum", "CalculateConstantsAtmosphere" })
                    harmony.Patch(AccessTools.DeclaredMethod(modular, name),
                        postfix: new HarmonyMethod(typeof(StationSolarPublication), nameof(ConstantsHook)));
            }
            Type? star = AccessTools.TypeByName("Kopernicus.Components.KopernicusStar");
            if (star != null)
                harmony.Patch(AccessTools.Method(star, "SunBodyFlux"),
                    postfix: new HarmonyMethod(typeof(StationSolarPublication), nameof(KopernicusHook)));
        }
        private static bool reportedFailure;
        private static void Guard(Action observe)
        {
            try { observe(); }
            catch (Exception error)
            {
                Clear();
                if (!reportedFailure) Debug.LogError("[Koffee] Solar publication unavailable: " + error);
                reportedFailure = true;
            }
        }
        private static void BeginHook(FlightIntegrator __instance) => Guard(() => Begin(__instance));
        private static void SunHook(FlightIntegrator __instance) => Guard(() => SunCalculated(__instance));
        private static void ConstantsHook(FlightIntegrator __instance) => Guard(() => ConstantsPublished(__instance));
        private static void ModularBeginHook(FlightIntegrator __instance) => Guard(() => ModularSunBegin(__instance));
        private static void KopernicusHook(object __0) => Guard(() => KopernicusCalculated(__0));
        private static Exception? EndHook(FlightIntegrator __instance, Exception? __exception)
        { Guard(() => End(__instance, __exception)); return __exception; }
        private static Exception? ModularEndHook(FlightIntegrator __instance, Exception? __exception)
        { Guard(() => ModularSunEnd(__instance, __exception)); return __exception; }
        internal static void Begin(FlightIntegrator __instance)
        {
            var sample = integrators.GetOrCreateValue(__instance);
            sample.Active = true; sample.Published = false; sample.Complete = false;
            if (__instance.Vessel != null) publications.Remove(__instance.Vessel);
        }
        internal static void SunCalculated(FlightIntegrator __instance)
        {
            if (__instance.GetType().FullName == "ModularFI.ModularFlightIntegrator") return;
            RecordSun(__instance, "StockFlightIntegrator", __instance.Vessel.directSunlight);
        }
        internal static void ModularSunBegin(FlightIntegrator __instance)
        {
            var s = integrators.GetOrCreateValue(__instance);
            s.SunCalculated = false; s.Source = ""; s.Complete = false;
        }
        internal static Exception? ModularSunEnd(FlightIntegrator __instance, Exception? __exception)
        {
            var s = integrators.GetOrCreateValue(__instance);
            if (__exception == null && s.Source != "KopernicusAggregate")
            {
                // If Kopernicus is present, a missing/invalid aggregate must not
                // silently fall back to its primary-star boolean.
                if (AccessTools.TypeByName("Kopernicus.Components.KopernicusStar") == null)
                    RecordSun(__instance, "StockFlightIntegrator", __instance.Vessel.directSunlight);
            }
            if ((__exception != null || !s.SunCalculated) && __instance.Vessel != null) publications.Remove(__instance.Vessel);
            return __exception;
        }
        internal static void KopernicusCalculated(object __0)
        {
            if (!(__0 is FlightIntegrator fi)) return;
            Type? type = AccessTools.TypeByName("Kopernicus.Components.KopernicusStar");
            var stars = type == null ? null : AccessTools.Field(type, "Stars")?.GetValue(null) as IEnumerable;
            var fluxes = type == null ? null : AccessTools.Field(type, "SolarFlux")?.GetValue(null) as IDictionary;
            if (stars == null || fluxes == null) return;
            double sum = 0; var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (object star in stars)
            {
                string? name = AccessTools.Field(type!, "StarName")?.GetValue(star) as string;
                if (name == null || !names.Add(name) || !fluxes.Contains(name) || !(fluxes[name] is double value) ||
                    !ExperimentContainerState.Finite(value) || value < 0) return;
                sum += value;
            }
            double total = IntegratorFlux(fi);
            if (names.Count == 0 || !ExperimentContainerState.Finite(sum) || !ExperimentContainerState.Finite(total) ||
                Math.Abs(total - sum) > Math.Max(1e-6, Math.Abs(sum) * 1e-9)) return;
            RecordSun(fi, "KopernicusAggregate", sum > 0);
        }
        private static double IntegratorFlux(FlightIntegrator fi) => (double)AccessTools.Field(typeof(FlightIntegrator), "solarFlux").GetValue(fi)!;
        private static bool BodyMatches(FlightIntegrator fi) => ReferenceEquals(
            AccessTools.Field(typeof(FlightIntegrator), "currentMainBody").GetValue(fi), fi.Vessel?.mainBody);
        private static void RecordSun(FlightIntegrator __instance, string source, bool illuminated)
        {
            Vessel vessel = __instance.Vessel;
            if (vessel == null) return;
            if (!BodyMatches(__instance))
            {
                publications.Remove(vessel);
                var invalid = integrators.GetOrCreateValue(__instance);
                invalid.SunCalculated = false; invalid.Published = false; invalid.Complete = false;
                return;
            }
            if (!publications.TryGetValue(vessel, out var prior) ||
                prior.StockSunlight != vessel.directSunlight || prior.Sunlight != illuminated || prior.Source != source ||
                !ReferenceEquals(prior.Body, vessel.mainBody) ||
                !ReferenceEquals(prior.Proto, vessel.protoVessel) || prior.PartCount != vessel.parts.Count)
                publications.Remove(vessel);
            var sample = integrators.GetOrCreateValue(__instance);
            sample.Complete = false; sample.Published = false; sample.SunCalculated = true;
            sample.Vessel = vessel; sample.Integrator = __instance; sample.Proto = vessel.protoVessel; sample.Game = HighLogic.CurrentGame;
            sample.Body = vessel.mainBody; sample.Situation = StationExperimentRuntime.CurrentSituation(vessel);
            sample.SunUT = Planetarium.GetUniversalTime(); sample.Sunlight = illuminated;
            sample.SunFrame = Time.frameCount;
            sample.StockSunlight = vessel.directSunlight; sample.Source = source; sample.SolarFlux = IntegratorFlux(__instance);
            sample.Flux = vessel.solarFlux; sample.PartCount = vessel.parts.Count;
        }
        internal static void ConstantsPublished(FlightIntegrator __instance)
        {
            if (!integrators.TryGetValue(__instance, out var sample) || !sample.Active || !sample.SunCalculated) return;
            Vessel vessel = __instance.Vessel;
            double now = Planetarium.GetUniversalTime();
            if (!Physical(vessel) || !BodyMatches(__instance) || !ReferenceEquals(vessel, sample.Vessel) || !ReferenceEquals(vessel.protoVessel, sample.Proto) ||
                !ReferenceEquals(vessel.mainBody, sample.Body) || sample.Situation != StationExperimentRuntime.CurrentSituation(vessel) ||
                sample.StockSunlight != vessel.directSunlight || now < sample.SunUT ||
                Time.frameCount < sample.SunFrame || Time.frameCount - sample.SunFrame > 1 ||
                sample.PartCount != vessel.parts.Count || !ReferenceEquals(sample.Game, HighLogic.CurrentGame) ||
                sample.SolarFlux != IntegratorFlux(__instance) || vessel.solarFlux != IntegratorFlux(__instance) ||
                !ExperimentContainerState.Finite(vessel.solarFlux)) return;
            sample.UT = now; sample.PhysicsTime = Time.fixedTime; sample.Flux = vessel.solarFlux; sample.Published = true;
        }
        internal static Exception? End(FlightIntegrator __instance, Exception? __exception)
        {
            if (integrators.TryGetValue(__instance, out var sample))
            {
                sample.Active = false;
                if (__exception == null && sample.Published && __instance.isRunning && Physical(__instance.Vessel))
                {
                    sample.Complete = true;
                    publications.Remove(sample.Vessel); publications.Add(sample.Vessel, sample.Snapshot());
                }
            }
            return __exception;
        }
        private static bool Physical(Vessel vessel) => vessel != null && HighLogic.LoadedSceneIsFlight && FlightGlobals.ready &&
            vessel.loaded && !vessel.packed && TimeWarp.CurrentRateIndex == 0 && TimeWarp.CurrentRate == 1;
        internal static bool TryRead(Vessel vessel, double now, out double flux, out bool sunlight) =>
            TryRead(vessel, now, out flux, out sunlight, out _, out _);
        internal static bool TryRead(Vessel vessel, double now, out double flux, out bool sunlight, out string source, out bool stockSunlight)
        {
            flux = 0; sunlight = false; source = ""; stockSunlight = false;
            if (!Physical(vessel) || now != Planetarium.GetUniversalTime() || !publications.TryGetValue(vessel, out var s) || !s.Complete ||
                !ReferenceEquals(s.Game, HighLogic.CurrentGame) || !ReferenceEquals(s.Proto, vessel.protoVessel) ||
                !ReferenceEquals(s.Body, vessel.mainBody) || s.PartCount != vessel.parts.Count ||
                s.PhysicsTime != Time.fixedTime || now < s.UT || now - s.UT > TimeWarp.fixedDeltaTime * 1.01 ||
                s.Situation != StationExperimentRuntime.CurrentSituation(vessel) || s.Flux != vessel.solarFlux ||
                !s.Integrator.isRunning || !BodyMatches(s.Integrator) || !integrators.TryGetValue(s.Integrator, out var input) ||
                !input.SunCalculated || input.Source != s.Source || input.Sunlight != s.Sunlight ||
                !ExperimentContainerState.Finite(input.SolarFlux) || input.SolarFlux < 0 ||
                (input.SolarFlux > 0) != input.Sunlight || IntegratorFlux(s.Integrator) != input.SolarFlux ||
                s.StockSunlight != vessel.directSunlight) return false;
            flux = s.Flux; sunlight = s.Sunlight; source = s.Source; stockSunlight = s.StockSunlight; return true;
        }
    }
}

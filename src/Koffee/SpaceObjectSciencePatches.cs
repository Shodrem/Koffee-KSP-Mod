using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Asteroids/comets create ScienceData synchronously and hand it directly to the EVA container.
    internal static class SpaceObjectSciencePatches
    {
        private static readonly HashSet<PartModule> Running = new HashSet<PartModule>();

        internal static void Install(Harmony harmony)
        {
            foreach (Type type in new[] { typeof(ModuleAsteroid), typeof(ModuleComet) })
            {
                MethodInfo target = AccessTools.DeclaredMethod(type, "performSampleExperiment")
                    ?? throw new MissingMethodException(type.FullName, "performSampleExperiment");
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(SpaceObjectSciencePatches), nameof(Begin)),
                    finalizer: new HarmonyMethod(typeof(SpaceObjectSciencePatches), nameof(Finish)));
            }
        }

        private static bool Begin(PartModule __instance, ModuleScienceContainer __0, out Attempt? __state)
        {
            __state = null;
            string? id = AccessTools.Field(__instance.GetType(), "sampleExperimentId").GetValue(__instance) as string;
            if (id == null) return true;
            if (!ScienceExperimentKoffeeRegistry.Contains(id)) return true;
            if (Running.Contains(__instance)) return false;
            // Use the collecting kerbal's vessel/origin, never the uncrewed space object's vessel.
            if (__0 == null) return false;
            KaffeinationAttempt? work = ScienceExperimentKoffeeService.TryBegin(id, __0.part);
            if (work == null) return false;
            try
            {
                __state = new Attempt(__instance, work);
                Running.Add(__instance);
                GameEvents.OnExperimentDeployed.Add(__state.OnCreated);
                return true;
            }
            catch
            {
                work.Dispose();
                Running.Remove(__instance);
                throw;
            }
        }

        private static Exception? Finish(PartModule __instance, Attempt? __state, Exception? __exception)
        {
            if (__state == null) return __exception;
            try
            {
                GameEvents.OnExperimentDeployed.Remove(__state.OnCreated);
                if (!__state.Created) __state.Restore();
            }
            finally
            {
                __state.Work.Dispose();
                Running.Remove(__instance);
            }
            return __exception;
        }

        private sealed class Attempt
        {
            private readonly PartModule module;
            private readonly FieldInfo data;
            private readonly FieldInfo subject;
            private readonly object? originalData;
            private readonly object? originalSubject;
            internal readonly KaffeinationAttempt Work;
            internal bool Created;

            internal Attempt(PartModule module, KaffeinationAttempt work)
            {
                this.module = module;
                Work = work;
                data = AccessTools.Field(module.GetType(), "experimentData") ?? throw new MissingFieldException("experimentData");
                subject = AccessTools.Field(module.GetType(), "subject") ?? throw new MissingFieldException("subject");
                originalData = data.GetValue(module);
                originalSubject = subject.GetValue(module);
            }

            internal void OnCreated(ScienceData report)
            {
                if (Created || report == null || !ReferenceEquals(report, data.GetValue(module))) return;
                Created = true;
                Work.Commit();
            }

            internal void Restore()
            {
                if (module == null) return;
                data.SetValue(module, originalData);
                subject.SetValue(module, originalSubject);
            }
        }
    }
}

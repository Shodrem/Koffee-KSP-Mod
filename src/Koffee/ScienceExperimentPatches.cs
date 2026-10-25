using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class ScienceExperimentPatches
    {
        private static readonly FieldInfo Data = RequiredField("experimentData");
        private static readonly FieldInfo Subject = RequiredField("subject");
        private static readonly FieldInfo Fx = RequiredField("fxModules");
        private static readonly FieldInfo OverwriteDialog = RequiredField("overwriteDialog");
        private static readonly MethodInfo UpdateUI = RequiredMethod("updateModuleUI");
        private static ConditionalWeakTable<ModuleScienceExperiment, Attempt> attempts =
            new ConditionalWeakTable<ModuleScienceExperiment, Attempt>();

        internal static void Install(Harmony harmony)
        {
            foreach (string method in new[] { "DeployExperiment", "DeployAction", "DeployExperimentExternal" })
                Apply(harmony, method, nameof(EntryPrefix), null);
            Apply(harmony, "OnActive", nameof(StageEntryPrefix), null);
            Apply(harmony, "gatherData", null, nameof(GatherPostfix));
            Apply(harmony, "OnScienceCompleteDelay", null, nameof(CompletionPostfix));
            foreach (string method in new[] { "PlayGolf", "Banana", "Dzhanibekov" })
                harmony.Patch(AccessTools.DeclaredMethod(typeof(KerbalEVA), method),
                    prefix: new HarmonyMethod(typeof(ScienceExperimentPatches), nameof(EvaAnimationPrefix)));
        }

        internal static void Clear() => attempts = new ConditionalWeakTable<ModuleScienceExperiment, Attempt>();

        private static FieldInfo RequiredField(string name) =>
            AccessTools.Field(typeof(ModuleScienceExperiment), name) ?? throw new MissingFieldException(name);

        private static MethodInfo RequiredMethod(string name) =>
            AccessTools.DeclaredMethod(typeof(ModuleScienceExperiment), name) ?? throw new MissingMethodException(name);

        private static void Apply(Harmony harmony, string name, string? prefix, string? postfix)
        {
            MethodInfo target = RequiredMethod(name);
            try
            {
                harmony.Patch(target,
                    prefix == null ? null : new HarmonyMethod(typeof(ScienceExperimentPatches), prefix),
                    postfix == null ? null : new HarmonyMethod(typeof(ScienceExperimentPatches), postfix));
                Debug.Log("[Koffee] Patched " + target.DeclaringType?.FullName + "." + target.Name);
            }
            catch (Exception error)
            {
                Debug.LogError("[Koffee] Failed to patch " + name + ": " + error);
                throw;
            }
        }

        private static void EvaAnimationPrefix(Callback __0)
        {
            if (__0?.Target is ModuleScienceExperiment module && attempts.TryGetValue(module, out Attempt? attempt) && attempt != null)
                attempt.WaitingForCompletion = true;
        }

        private static bool StageEntryPrefix(ModuleScienceExperiment __instance)
        {
            // Stock ignores staging when either flag is off. Do not report a
            // missing scientist for an experiment that this stage will not run.
            if (!__instance.useStaging || !__instance.stagingEnabled) return true;
            return EntryPrefix(__instance);
        }

        private static bool EntryPrefix(ModuleScienceExperiment __instance)
        {
            if (!ScienceExperimentKoffeeRegistry.Contains(__instance.experimentID)) return true;
            if (attempts.TryGetValue(__instance, out Attempt? attempt) && attempt != null &&
                (attempt.Running || attempt.Completing || attempt.WaitingForCompletion ||
                (attempt.WaitingForOverwrite && HasOverwriteDialog(__instance)))) return false;
            // The stock entry point has not disabled its event, started FX or discarded any data yet.
            return ScienceExperimentKoffeeService.CanRun(__instance.experimentID, __instance.part);
        }

        private static bool HasOverwriteDialog(ModuleScienceExperiment module) =>
            OverwriteDialog.GetValue(module) is UnityEngine.Object dialog && dialog != null;

        private static void GatherPostfix(ModuleScienceExperiment __instance, ref IEnumerator __result)
        {
            if (!ScienceExperimentKoffeeRegistry.Contains(__instance.experimentID)) return;
            attempts.TryGetValue(__instance, out Attempt? previous);
            if (previous != null && (previous.Running || previous.Completing || previous.WaitingForCompletion))
            {
                (__result as IDisposable)?.Dispose();
                __result = Empty();
                return;
            }

            // The overwrite callback calls dumpData before invoking gatherData a second time.
            // Keep the original report until that replacement has actually succeeded.
            Attempt attempt = previous != null && previous.WaitingForOverwrite && Data.GetValue(__instance) == null
                ? previous : new Attempt(__instance);
            attempts.Remove(__instance);
            attempts.Add(__instance, attempt);
            attempt.WaitingForOverwrite = false;
            // Recheck when confirming overwrite: supplies may have changed while the dialog was open.
            if (!ScienceExperimentKoffeeService.CanRun(__instance.experimentID, __instance.part))
            {
                attempt.Handled = true;
                attempt.Restore();
                Forget(__instance, attempt);
                (__result as IDisposable)?.Dispose();
                __result = Empty();
                return;
            }
            attempt.Running = true;
            __result = Gather(__instance, __result, attempt);
        }

        private static IEnumerator Empty() { yield break; }

        private static bool Advance(IEnumerator original, out Exception? error)
        {
            try { error = null; return original.MoveNext(); }
            catch (Exception exception) { error = exception; return false; }
        }

        private static IEnumerator Gather(ModuleScienceExperiment module, IEnumerator original, Attempt attempt)
        {
            try
            {
                while (true)
                {
                    bool moved = Advance(original, out Exception? error);
                    if (error != null)
                    {
                        Debug.LogError("[Koffee] Stock gathering failed: " + error);
                        attempt.Restore();
                        yield break;
                    }
                    if (!moved) break;
                    yield return original.Current;
                }
            }
            finally
            {
                (original as IDisposable)?.Dispose();
                attempt.Running = false;
                attempt.WaitingForOverwrite = !attempt.Handled && HasOverwriteDialog(module) &&
                    ReferenceEquals(Data.GetValue(module), attempt.OriginalData);
                if (!attempt.WaitingForOverwrite && !attempt.WaitingForCompletion && !attempt.Completing)
                {
                    if (!attempt.Handled && attempt.OriginalData != null && Data.GetValue(module) == null)
                        attempt.Restore();
                    Forget(module, attempt);
                }
            }
        }

        private static void CompletionPostfix(ModuleScienceExperiment __instance, ref IEnumerator __result)
        {
            if (!ScienceExperimentKoffeeRegistry.Contains(__instance.experimentID)) return;
            __result = Complete(__instance, __result);
        }

        private static IEnumerator Complete(ModuleScienceExperiment module, IEnumerator original)
        {
            if (!attempts.TryGetValue(module, out Attempt? attempt) || attempt == null)
            {
                attempt = new Attempt(module);
                attempts.Add(module, attempt);
            }
            if (attempt.Completing || attempt.Handled)
            {
                (original as IDisposable)?.Dispose();
                yield break;
            }
            attempt.Completing = true;
            attempt.WaitingForCompletion = false;
            KaffeinationAttempt? work = null;
            bool created = false;
            // Stock fires this synchronously in its first MoveNext, before the dialog delay.
            // Also recognizes a report immediately collected by an event listener.
            void OnCreated(ScienceData data)
            {
                if (data != null && data.container == module.part.flightID &&
                    data.subjectID.StartsWith(module.experimentID + "@", StringComparison.Ordinal)) created = true;
            }

            try
            {
                work = ScienceExperimentKoffeeService.TryBegin(module.experimentID, module.part);
                if (work == null)
                {
                    attempt.Handled = true;
                    attempt.Restore();
                    yield break;
                }

                bool moved;
                Exception? error;
                GameEvents.OnExperimentDeployed.Add(OnCreated);
                try { moved = Advance(original, out error); }
                finally { GameEvents.OnExperimentDeployed.Remove(OnCreated); }
                created |= Data.GetValue(module) is ScienceData data && !ReferenceEquals(data, attempt.OriginalData);
                attempt.Handled = true;
                if (created)
                {
                    work.Commit();
                }
                else
                {
                    work.Dispose();
                    attempt.Restore();
                    Debug.LogError("[Koffee] No ScienceData created; resource payment refunded.");
                }
                // A failure after report creation must not refund a successfully produced report.
                if (error != null) Debug.LogError("[Koffee] Stock science completion failed: " + error);
                if (!created || error != null) yield break;

                // No new wait: forward precisely the stock yields after settling the payment.
                while (moved)
                {
                    yield return original.Current;
                    moved = Advance(original, out error);
                    if (error != null) Debug.LogError("[Koffee] Stock result dialog failed: " + error);
                }
            }
            finally
            {
                work?.Dispose();
                (original as IDisposable)?.Dispose();
                Forget(module, attempt);
            }
        }

        private static void Forget(ModuleScienceExperiment module, Attempt attempt)
        {
            if (attempts.TryGetValue(module, out Attempt? current) && ReferenceEquals(current, attempt))
                attempts.Remove(module);
        }

        private sealed class Attempt
        {
            private readonly ModuleScienceExperiment module;
            private readonly object? originalSubject;
            private readonly bool deployed;
            private readonly bool inoperable;
            private readonly bool eventDisabled;
            private readonly List<KeyValuePair<IScalarModule, float>> scalars = new List<KeyValuePair<IScalarModule, float>>();
            internal readonly object? OriginalData;
            internal bool Running;
            internal bool WaitingForOverwrite;
            internal bool Handled;
            internal bool WaitingForCompletion;
            internal bool Completing;

            internal Attempt(ModuleScienceExperiment module)
            {
                this.module = module;
                OriginalData = Data.GetValue(module);
                originalSubject = Subject.GetValue(module);
                deployed = module.Deployed;
                inoperable = module.Inoperable;
                eventDisabled = module.DeployEventDisabled;
                if (Fx.GetValue(module) is List<IScalarModule> modules)
                    foreach (IScalarModule fx in modules)
                        scalars.Add(new KeyValuePair<IScalarModule, float>(fx, fx.GetScalar));
            }

            internal void Restore()
            {
                if (module == null) return;
                // Restore references and flags, never edit an existing ScienceData object.
                Data.SetValue(module, OriginalData);
                Subject.SetValue(module, originalSubject);
                module.Deployed = deployed;
                module.Inoperable = inoperable;
                module.DeployEventDisabled = eventDisabled;
                foreach (var scalar in scalars) scalar.Key.SetScalar(scalar.Value);
                UpdateUI.Invoke(module, null);
            }
        }
    }
}

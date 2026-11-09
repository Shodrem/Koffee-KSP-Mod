using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ShoAerospace.Koffee
{
    internal static class ScienceReportExtraction
    {
        private static readonly MethodInfo Dump = AccessTools.DeclaredMethod(typeof(ModuleScienceExperiment), "dumpData");
        private static readonly MethodInfo End = AccessTools.DeclaredMethod(typeof(ModuleScienceExperiment), "endExperiment");
        private static readonly FieldInfo ResettingField = AccessTools.Field(typeof(ModuleScienceExperiment), "resettingExperiment");

        internal static bool Resetting(ModuleScienceExperiment instrument) => instrument.GetType() == typeof(ModuleScienceExperiment) &&
            (bool)ResettingField.GetValue(instrument)!;

        internal static Action? Remove(IScienceDataContainer source, ScienceData data)
        {
            if (source.GetType() == typeof(ModuleScienceExperiment) && source is ModuleScienceExperiment instrument &&
                instrument.rerunnable && instrument.resettable && !instrument.Inoperable)
            {
                if (Resetting(instrument)) throw new InvalidOperationException("Stock instrument is already resetting.");
                // Stock DumpData starts resetExperiment, which yields FX BEFORE
                // removing the report. Detach through its pure synchronous stock
                // primitive; start its presentation/reset path only after commit.
                Dump.Invoke(instrument, null);
                instrument.Deployed = false;
                return () =>
                {
                    if (instrument != null && instrument.GetScienceCount() == 0)
                        End.Invoke(instrument, new object[] { data });
                };
            }
            source.DumpData(data);
            return null;
        }

        internal static void Complete(Action? completion)
        {
            try { completion?.Invoke(); }
            catch (Exception error)
            {
                // A presentation failure must not refund a committed delivery,
                // duplicate the report or clear a report restored by rollback.
                UnityEngine.Debug.LogError("[Koffee] Report committed; stock extraction presentation failed: " + error);
            }
        }
    }

    // Snapshot only the known mutation surface, never OnLoad/Reset/ReturnData:
    // those APIs can restore a spent Goo/Jr instrument as a side effect.
    internal sealed class ScienceReportSnapshot
    {
        private readonly IScienceDataContainer source;
        private readonly List<KeyValuePair<FieldInfo, object?>> fields = new List<KeyValuePair<FieldInfo, object?>>();
        private readonly ScienceData[] reports;
        private readonly uint[] owners;
        internal ScienceReportSnapshot(IScienceDataContainer source)
        {
            this.source = source;
            reports = source.GetData().ToArray();
            owners = reports.Select(d => d.container).ToArray();
            string[] names;
            if (source is ModuleScienceContainer) names = new[] { "dataStore", "pagesWithDialogs" };
            else if (source is ModuleScienceExperiment) names = new[] { "experimentData", "subject", "Deployed", "Inoperable", "DeployEventDisabled", "resultsDialog" };
            else if (source is ModuleKoffeeExperiment) names = new[] { "scienceData", "state", "resultsDialog", "checkpointRebasePending" };
            else throw new NotSupportedException("Science source has no reversible extraction contract.");
            foreach (string name in names)
            {
                FieldInfo field = AccessTools.Field(source.GetType(), name) ?? throw new MissingFieldException(name);
                object? value = field.GetValue(source);
                if (value is List<ScienceData> list) value = new List<ScienceData>(list);
                fields.Add(new KeyValuePair<FieldInfo, object?>(field, value));
            }
        }
        internal void Restore()
        {
            foreach (var entry in fields)
            {
                if (entry.Value is List<ScienceData> saved && entry.Key.GetValue(source) is List<ScienceData> list)
                { list.Clear(); list.AddRange(saved); }
                else entry.Key.SetValue(source, entry.Value);
            }
            for (int i = 0; i < reports.Length; i++) reports[i].container = owners[i];
        }
    }

    // A synchronous, refundable charge. Uses the stock flow/access checks, the
    // same contract as KoffeeSources; it never creates continuous demand/debt.
    internal sealed class ScienceOperationCharge : IDisposable
    {
        private readonly List<KeyValuePair<PartResource, double>> debits = new List<KeyValuePair<PartResource, double>>();
        private bool committed;
        private static PartResource[]? Accessible(Part consumer)
        {
            var definition = PartResourceLibrary.Instance.GetDefinition("ElectricCharge");
            if (definition == null || consumer?.vessel == null) return null;
            return consumer.vessel.parts.Where(p => p != null && p.State != PartStates.DEAD &&
                consumer.CanCrossfeed(p, definition.id, definition.resourceFlowMode))
                .OrderByDescending(p => p.GetResourcePriority())
                .Select(p => p.Resources.GetFlowing(definition.id, true)).Where(r => r != null).ToArray();
        }
        private static bool Sufficient(PartResource[]? resources) => resources != null &&
            resources.All(r => ExperimentContainerState.Finite(r.amount) && r.amount >= 0) && resources.Sum(r => r.amount) >= 20;
        internal static bool Available(Part consumer) => Sufficient(Accessible(consumer));
        internal static ScienceOperationCharge? Take(Part consumer)
        {
            var charge = new ScienceOperationCharge();
            var resources = Accessible(consumer);
            if (!Sufficient(resources)) return null;
            double left = 20;
            try
            {
                foreach (PartResource resource in resources!)
                {
                    double take = Math.Min(left, resource.amount);
                    if (take <= 0) continue;
                    charge.debits.Add(new KeyValuePair<PartResource, double>(resource, resource.amount));
                    resource.amount -= take;
                    left -= take;
                    if (left <= 0) return charge;
                }
            }
            catch { charge.Dispose(); throw; }
            charge.Dispose();
            return null;
        }
        internal void Commit() => committed = true;
        public void Dispose()
        {
            if (committed) return;
            foreach (var debit in debits) debit.Key.amount = debit.Value;
            committed = true;
        }
    }

    internal static class ScienceReportTransfer
    {
        internal static bool Mutating { get; private set; }
        internal static bool Live(Part part) => HighLogic.LoadedSceneIsFlight && part != null &&
            part.State != PartStates.DEAD && part.vessel != null && part.vessel.loaded;
        internal static IScienceDataContainer? Source(Vessel vessel, ScienceData data) => vessel.parts
            .Where(p => p != null && p.State != PartStates.DEAD)
            .SelectMany(p => p.Modules.OfType<IScienceDataContainer>())
            .FirstOrDefault(c => c.GetData().Any(d => ReferenceEquals(d, data)));

        internal static int Move(IEnumerable<IScienceDataContainer> sources, ModuleKoffeeScienceStore receiver,
            ScienceData? only = null, bool notify = true)
        {
            if (Mutating || ScienceLabAcceptance.Busy || receiver == null || !Live(receiver.part) || receiver.Container == null) return 0;
            var destination = receiver.Container;
            var offered = sources.Where(s => s != null && !ReferenceEquals(s, destination)).Distinct().ToArray();
            var subjects = new HashSet<string>(destination.GetData().Select(d => d.subjectID), StringComparer.Ordinal);
            var accepted = new List<KeyValuePair<IScienceDataContainer, ScienceData>>();
            int total = 0, managerExcluded = 0;
            foreach (var source in offered)
            {
                if (!(source is PartModule module) || !Live(module.part) || module.vessel != receiver.vessel ||
                    !(source is ModuleScienceExperiment || source is ModuleScienceContainer || source is ModuleKoffeeExperiment)) continue;
                foreach (ScienceData data in source.GetData())
                {
                    if (only != null && !ReferenceEquals(only, data)) continue;
                    total++;
                    if (!ScienceReportPolicy.CanReceiveInManager(data))
                    {
                        if (!ScienceReportPolicy.Physical(data)) managerExcluded++;
                        continue;
                    }
                    if ((source is ModuleKoffeeExperiment k && ScienceReportPolicy.Physical(k)) ||
                        (!destination.allowRepeatedSubjects && !subjects.Add(data.subjectID))) continue;
                    accepted.Add(new KeyValuePair<IScienceDataContainer, ScienceData>(source, data));
                }
            }
            if (accepted.Count == 0)
            {
                if (notify)
                {
                    if (total > 0 && managerExcluded == total) ScienceReportUI.Message("ManagerExcluded");
                    else ScienceReportUI.Message("Moved", "0", total.ToString());
                }
                return 0;
            }
            var snapshots = offered.Where(s => accepted.Any(a => ReferenceEquals(a.Key, s)))
                .Concat(new[] { (IScienceDataContainer)destination }).Select(s => new ScienceReportSnapshot(s)).ToArray();
            var completions = new List<Action>();
            Mutating = true;
            try
            {
                using (var charge = ScienceOperationCharge.Take(receiver.part))
                {
                    if (charge == null) { if (notify) ScienceReportUI.Message("NoEC"); return 0; }
                    try
                    {
                        foreach (var entry in accepted)
                        {
                            if (!entry.Key.GetData().Any(d => ReferenceEquals(d, entry.Value)) ||
                                !destination.AddData(entry.Value) || !destination.GetData().Any(d => ReferenceEquals(d, entry.Value)))
                                throw new InvalidOperationException("Destination did not accept report.");
                            Action? completion = ScienceReportExtraction.Remove(entry.Key, entry.Value);
                            if (completion != null) completions.Add(completion);
                            if (entry.Key.GetData().Any(d => ReferenceEquals(d, entry.Value)))
                                throw new InvalidOperationException("Source retained report: " + entry.Key.GetType().Name + "/" +
                                    ((PartModule)entry.Key).part.persistentId + "/" + entry.Value.subjectID);
                        }
                        charge.Commit();
                    }
                    catch
                    {
                        foreach (var snapshot in snapshots) snapshot.Restore();
                        throw;
                    }
                }
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogError("[Koffee] Report transfer rolled back: " + error);
                if (notify) ScienceReportUI.Message("Failed");
                return 0;
            }
            finally { Mutating = false; }
            foreach (Action completion in completions) ScienceReportExtraction.Complete(completion);
            if (notify) ScienceReportUI.Message("Moved", accepted.Count.ToString(), (total - accepted.Count).ToString());
            return accepted.Count;
        }
    }
}

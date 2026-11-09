using System;
using System.Collections.Generic;
using System.Linq;
using BackgroundResourceProcessing;
using UnityEngine;

namespace ShoAerospace.Koffee.Brp
{
    // Only copies a captured, already-published state. Never runs publication
    // again, integrates work or reads a later live state as background history.
    internal sealed class BrpRestoreQueue
    {
        internal static Func<double> Clock = () => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
        private sealed class Item
        {
            internal uint FlightId;
            internal string Name = "";
            internal int Index;
            internal ConfigNode Node = null!;
            internal PartModule? Live;
            internal string Before = "";
        }
        private sealed class Pending
        {
            internal Game? Game;
            internal Vessel Vessel = null!;
            internal Guid Id;
            internal object? Proto;
            internal double Deadline;
            internal Item[] Items = Array.Empty<Item>();
            internal bool EmptyLaunch;
        }
        private readonly Dictionary<BackgroundResourceProcessor, Pending> pending = new Dictionary<BackgroundResourceProcessor, Pending>();
        private readonly Dictionary<BackgroundResourceProcessor, string> completed = new Dictionary<BackgroundResourceProcessor, string>();
        private Game? owner;
        internal int Count => pending.Count;
        private static string Saved(PartModule module)
        { var node = new ConfigNode("MODULE"); module.OnSave(node); return node.ToString(); }
        private static IEnumerable<PartModule> Modules(Part part, string name) => part.Modules.Cast<PartModule>().Where(m => m.GetType().Name == name);

        internal void Enqueue(BackgroundResourceProcessor processor)
        {
            if (!ReferenceEquals(owner, HighLogic.CurrentGame)) { Clear(); owner = HighLogic.CurrentGame; }
            var vessel = processor.Vessel;
            if (vessel == null) { Debug.LogWarning("[Koffee][BRPRestore] Restore has no vessel identity; no state can be associated safely."); return; }
            var items = new List<Item>();
            var snapshots = vessel.protoVessel?.protoPartSnapshots;
            if (snapshots != null)
                foreach (var part in snapshots)
                    foreach (string name in new[] { nameof(ModuleKoffeeExperiment), nameof(ModuleKomfortService), nameof(ModuleEmergencySupport), nameof(ModuleKoffeeExperimentManager), nameof(ModuleKoffeeScienceStore) })
                    {
                        int index = 0;
                        var livePart = vessel.parts.FirstOrDefault(p => p.flightID == part.flightID);
                        foreach (var module in part.modules.Where(m => m.moduleName == name))
                        {
                            var live = livePart == null ? null : Modules(livePart, name).ElementAtOrDefault(index);
                            items.Add(new Item { FlightId = part.flightID, Name = name, Index = index++, Node = module.moduleValues.CreateCopy(),
                                Live = live, Before = live == null ? "" : Saved(live) });
                        }
                    }
            string identity = vessel.id + "/" + string.Join("|", items.Select(i => i.FlightId + "/" + i.Name + "/" + i.Index + "/" + i.Node));
            if (completed.TryGetValue(processor, out string? previous) && previous == identity) return;
            if (pending.Count >= 128 && !pending.ContainsKey(processor))
            { Debug.LogWarning("[Koffee][BRPRestore] Continuation limit reached; restore was not applied."); return; }
            pending[processor] = new Pending { Game = HighLogic.CurrentGame, Vessel = vessel, Id = vessel.id, Proto = vessel.protoVessel,
                Items = items.ToArray(), Deadline = Clock() + 3,
                EmptyLaunch = snapshots == null && !processor.Converters.Any(c => c.Behaviour is KoffeeResearchPowerBehaviour || c.Behaviour is KomfortPowerBehaviour) };
            TryApply(processor, false);
        }

        internal void AfterInitialize(Vessel vessel)
        {
            foreach (var pair in pending.ToArray())
                if (ReferenceEquals(pair.Value.Vessel, vessel)) TryApply(pair.Key, true);
        }
        internal void Tick()
        {
            foreach (var processor in pending.Keys.ToArray()) TryApply(processor, false);
        }
        private void Reject(BackgroundResourceProcessor processor, string reason)
        {
            pending.Remove(processor);
            Debug.LogWarning("[Koffee][BRPRestore] Continuation discarded: " + reason + "; no older state will be restored over newer work.");
        }
        private void TryApply(BackgroundResourceProcessor processor, bool initialized)
        {
            if (!pending.TryGetValue(processor, out var state)) return;
            var vessel = state.Vessel;
            if (!ReferenceEquals(state.Game, HighLogic.CurrentGame) || !ReferenceEquals(vessel, processor.Vessel) || vessel == null || vessel.id != state.Id)
            { Reject(processor, "game/vessel identity changed"); return; }
            if (Clock() > state.Deadline) { Reject(processor, "three-second deadline"); return; }
            if (vessel.protoVessel?.protoPartSnapshots == null) return;
            if (state.Proto != null && !ReferenceEquals(state.Proto, vessel.protoVessel) && !initialized)
            { Reject(processor, "snapshot replaced outside the checked Initialize boundary"); return; }
            if (state.Proto == null && !initialized) return;
            if (state.Items.Length == 0)
            {
                if (state.EmptyLaunch || state.Proto != null) pending.Remove(processor);
                else Reject(processor, "background converters exist but no published snapshot was available");
                return;
            }
            var targets = new List<PartModule>();
            foreach (var item in state.Items)
            {
                var part = vessel.parts.FirstOrDefault(p => p.flightID == item.FlightId);
                var live = part == null ? null : Modules(part, item.Name).ElementAtOrDefault(item.Index);
                if (live == null) return; // Wait for PartModules, without partially restoring the vessel.
                if (item.Live != null && (!ReferenceEquals(item.Live, live) || Saved(live) != item.Before))
                { Reject(processor, "live module changed after capture"); return; }
                targets.Add(live);
            }
            for (int index = 0; index < targets.Count; index++)
            {
                var item = state.Items[index]; targets[index].OnLoad(item.Node.CreateCopy());
                // Initialize may just have replaced protoVessel from the old live modules.
                var savedPart = vessel.protoVessel.protoPartSnapshots.FirstOrDefault(p => p.flightID == item.FlightId);
                var saved = savedPart?.modules.Where(m => m.moduleName == item.Name).ElementAtOrDefault(item.Index);
                if (saved != null) saved.moduleValues = item.Node.CreateCopy();
            }
            completed[processor] = vessel.id + "/" + string.Join("|", state.Items.Select(i => i.FlightId + "/" + i.Name + "/" + i.Index + "/" + i.Node));
            pending.Remove(processor);
        }
        internal void BeforeRecord(BackgroundResourceProcessor processor)
        {
            TryApply(processor, false);
            if (pending.ContainsKey(processor)) Reject(processor, "new vessel record supersedes the pending restore");
            completed.Remove(processor);
        }
        internal void NewPublication(BackgroundResourceProcessor processor)
        {
            if (pending.ContainsKey(processor)) Reject(processor, "new state publication");
            completed.Remove(processor);
        }
        internal void Clear() { pending.Clear(); completed.Clear(); owner = null; }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class BrpRestoreContinuation : MonoBehaviour
    {
        private void Update() => BrpPowerProvider.ContinueRestores();
        private void OnDestroy() => BrpPowerProvider.CancelRestores();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    internal sealed class StationEquipmentSnapshot
    {
        private sealed class Entry
        {
            internal object Source = null!;
            internal uint Id;
        }
        private readonly Dictionary<AvailablePart, List<Entry>> installed = new Dictionary<AvailablePart, List<Entry>>();
        internal void Add(AvailablePart? part, object source, uint id)
        {
            if (part == null) return;
            if (!installed.TryGetValue(part, out var entries)) installed.Add(part, entries = new List<Entry>());
            entries.Add(new Entry { Source = source, Id = id });
        }
        internal bool Contains(AvailablePart part, object container) => installed.TryGetValue(part, out var entries) &&
            entries.Any(e => !ReferenceEquals(e.Source, container));
        internal string Signature(AvailablePart? part, object container) => part != null && installed.TryGetValue(part, out var entries) ?
            string.Join(",", entries.Where(e => !ReferenceEquals(e.Source, container)).Select(e => e.Id).OrderBy(id => id)) : "";
    }

    internal static class StationExperimentEquipment
    {
        internal static StationEquipmentSnapshot Capture(Vessel vessel)
        {
            var result = new StationEquipmentSnapshot();
            if (!StationExperimentCatalogLoader.Current.Definitions.Any(d => !string.IsNullOrEmpty(d.RequiredPartName))) return result;
            if (vessel.loaded)
            {
                foreach (Part part in vessel.parts)
                    if (part != null && part.State != PartStates.DEAD && ReferenceEquals(part.vessel, vessel))
                        result.Add(part.partInfo, part, part.persistentId);
            }
            else if (vessel.protoVessel?.protoPartSnapshots != null)
                foreach (ProtoPartSnapshot part in vessel.protoVessel.protoPartSnapshots)
                    if (part != null) result.Add(part.partInfo ?? part.partPrefab?.partInfo, part, part.persistentId);
            return result;
        }

        internal static StationExperimentPauseReason Evaluate(StationExperimentDefinition definition, Vessel vessel,
            object? container, StationEquipmentSnapshot? equipment = null)
        {
            if (string.IsNullOrEmpty(definition.RequiredPartName)) return StationExperimentPauseReason.None;
            AvailablePart? required = StationExperimentCatalogLoader.EquipmentPart(definition.RequiredPartName);
            if (required == null) return StationExperimentPauseReason.RequiredEquipmentUnresolved;
            return container != null && (equipment ?? Capture(vessel)).Contains(required, container) ?
                StationExperimentPauseReason.None : StationExperimentPauseReason.MissingEquipment;
        }

        internal static string Name(string name)
        {
            AvailablePart? part = StationExperimentCatalogLoader.EquipmentPart(name);
            return Localizer.Format(string.IsNullOrEmpty(part?.title) ? name : part!.title);
        }
        internal static string Description(StationExperimentDefinition? definition) => definition == null ? KoffeeResearchDisplay.Unknown :
            string.IsNullOrEmpty(definition.RequiredPartName) ? Localizer.Format("#Koffee_Equipment_None") : Name(definition.RequiredPartName);
        internal static string PauseText(StationExperimentPauseReason reason, StationExperimentDefinition? definition) =>
            PauseText(reason.ToString(), definition?.RequiredPartName ?? "");
        internal static string PauseText(string reason, string equipmentName) =>
            reason == nameof(StationExperimentPauseReason.MissingEquipment) ||
            reason == nameof(StationExperimentPauseReason.RequiredEquipmentUnresolved) ?
                Localizer.Format("#Koffee_Station_Pause_" + reason, string.IsNullOrEmpty(equipmentName) ?
                    KoffeeResearchDisplay.Unknown : Name(equipmentName)) : Localizer.Format("#Koffee_Station_Pause_" + reason);
    }

    // Presence at the endpoint is insufficient after detach/reattach, reload or a failed snapshot publication.
    // One observation per restricted run; the installed-parts snapshot is shared by a vessel's experiments.
    internal static class StationEquipmentContinuity
    {
        private sealed class Observation
        {
            internal Vessel Vessel = null!;
            internal object? Proto;
            internal bool Loaded;
            internal GameScenes Scene;
            internal string Signature = "";
            internal double UT;
        }
        private static readonly Dictionary<Guid, Observation> observations = new Dictionary<Guid, Observation>();
        private static Game? game;
        internal static void Clear() => observations.Clear();
        internal static bool Observe(ExperimentContainerState state, StationExperimentDefinition definition,
            Vessel vessel, object container, StationEquipmentSnapshot equipment, double? observedUT = null, bool? loadedOverride = null)
        {
            if (string.IsNullOrEmpty(definition.RequiredPartName) || !state.RunId.HasValue) return true;
            if (!ReferenceEquals(game, HighLogic.CurrentGame)) { Clear(); game = HighLogic.CurrentGame; }
            string signature = equipment.Signature(StationExperimentCatalogLoader.EquipmentPart(definition.RequiredPartName), container);
            double now = observedUT ?? Planetarium.GetUniversalTime();
            bool loaded = loadedOverride ?? vessel.loaded;
            observations.TryGetValue(state.RunId.Value, out var previous);
            bool continuous = previous != null && ReferenceEquals(previous.Vessel, vessel) &&
                ReferenceEquals(previous.Proto, vessel.protoVessel) && previous.Loaded == loaded &&
                previous.Scene == HighLogic.LoadedScene && previous.Signature == signature &&
                now >= previous.UT && state.LastCheckpointUT.HasValue && state.LastCheckpointUT.Value >= previous.UT;
            observations[state.RunId.Value] = new Observation { Vessel = vessel, Proto = vessel.protoVessel,
                Loaded = loaded, Scene = HighLogic.LoadedScene, Signature = signature, UT = now };
            return continuous;
        }

        internal static void Invalidate(Vessel vessel)
        {
            if (vessel == null) return;
            double now = Planetarium.GetUniversalTime();
            if (vessel.loaded)
            {
                foreach (var module in vessel.parts.SelectMany(p => p.Modules.OfType<ModuleKoffeeExperiment>()))
                    if (Restricted(module.State))
                    {
                        observations.Remove(module.State!.RunId!.Value);
                        module.RebaseCheckpoint(now, StationExperimentRuntime.CurrentBody(vessel), StationExperimentRuntime.CurrentSituation(vessel));
                    }
            }
            else if (vessel.protoVessel?.protoPartSnapshots != null)
                foreach (var part in vessel.protoVessel.protoPartSnapshots)
                {
                    ModuleKoffeeExperiment? module = part.partPrefab?.Modules?.OfType<ModuleKoffeeExperiment>().FirstOrDefault();
                    if (module != null && ExperimentContainerSnapshotAdapter.Read(part, ExperimentContainerSnapshotAdapter.ExperimentId(part), out var state, out _) ==
                        ExperimentContainerReadResult.Valid && Restricted(state))
                    {
                        observations.Remove(state!.RunId!.Value);
                        ExperimentContainerSnapshotAdapter.Write(part, state.RebaseCheckpoint(now,
                            StationExperimentRuntime.CurrentBody(vessel), StationExperimentRuntime.CurrentSituation(vessel)), out _);
                    }
                }
        }
        private static bool Restricted(ExperimentContainerState? state) => state?.Status == ExperimentContainerStatus.Running &&
            state.RunId.HasValue && !state.WorkComplete() && StationExperimentCatalogLoader.Current.TryGet(state.ExperimentId, out var definition) &&
            !string.IsNullOrEmpty(definition.RequiredPartName);
    }
}

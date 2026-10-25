using System;
using System.Collections.Generic;
using System.Linq;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static partial class StationExperimentBackground
    {
        private static long publicationFailures;
        private static readonly HashSet<Guid> warnedRuns = new HashSet<Guid>();
        private static readonly HashSet<Guid> loggedContainers = new HashSet<Guid>();

        internal static void TickAll(double now)
        {
            if (!ExperimentContainerState.Finite(now)) return;
            foreach (Vessel vessel in FlightGlobals.Vessels)
                if (vessel != null && !vessel.loaded) Tick(vessel, now);
        }

        internal static void Tick(Vessel vessel, double now)
        {
            if (vessel == null || vessel.loaded || !ExperimentContainerState.Finite(now)) return;
            if (StationBackgroundElectricity.Provider != null)
            {
                StationBackgroundElectricity.Provider.Ensure(vessel, now);
                return;
            }
            TickCore(vessel, now);
        }

        internal static bool TickPowered(Vessel vessel, double now, List<IResourceAccount> sources,
            Func<Guid, double, double, double> authorize)
        {
            long before = publicationFailures;
            TickCore(vessel, now, sources, authorize);
            return publicationFailures == before;
        }

        private static void TickCore(Vessel vessel, double now, List<IResourceAccount>? accounts = null,
            Func<Guid, double, double, double>? authorize = null)
        {
            if (vessel == null || (vessel.loaded && authorize == null) ||
                !ExperimentContainerState.Finite(now)) return;
            StationPowerPolicy.Observe(vessel, now);
            StationStockPower.Forget(vessel);
            ProtoVessel? proto = vessel.protoVessel;
            if (proto?.protoPartSnapshots == null) return;

            List<ProtoPartSnapshot> parts = proto.protoPartSnapshots
                .Where(part => part != null).ToList();
            List<BackgroundLab> labs = parts.Select(ReadLab)
                .Where(lab => lab != null).Cast<BackgroundLab>().ToList();
            List<BackgroundExperiment> experiments = parts.Select(ReadExperiment)
                .Where(experiment => experiment != null)
                .Cast<BackgroundExperiment>()
                .OrderBy(experiment => experiment.State.StartUT ?? double.MaxValue)
                .ThenBy(experiment => experiment.State.ContainerId).ToList();
            if (experiments.Count == 0) return;
            experiments = experiments.Where(experiment =>
            {
                if (!StationBackgroundElectricity.Migrate(experiment.Part, experiment.State, now, out var migrated))
                { publicationFailures++; return false; }
                experiment.State = migrated;
                return true;
            }).ToList();

            Dictionary<Guid, StationSlotAllocation> slots = Allocate(experiments, labs);
            var koffee = new StationKoffeeSession();
            List<IResourceAccount> sources = accounts ?? KoffeeAccounts(parts);
            ExperimentManagerContext? manager = ExperimentManagerService.SelectSnapshots(parts, now, out var managerReason);
            string body = StationExperimentRuntime.CurrentBody(vessel);
            string situation = StationExperimentRuntime.CurrentSituation(vessel);
            var equipment = authorize == null ? StationExperimentEquipment.Capture(vessel) : SnapshotEquipment(parts);
            foreach (BackgroundExperiment experiment in experiments)
                Process(vessel, experiment, labs, slots, koffee, sources, manager, managerReason, body, situation, now, equipment, authorize);
        }

        private static void Process(Vessel vessel, BackgroundExperiment experiment,
            IReadOnlyList<BackgroundLab> labs, IReadOnlyDictionary<Guid, StationSlotAllocation> slots,
            StationKoffeeSession koffee, List<IResourceAccount> sources, ExperimentManagerContext? manager,
            StationExperimentPauseReason managerReason,
            string body, string situation, double now, StationEquipmentSnapshot equipment,
            Func<Guid, double, double, double>? authorize = null)
        {
            ExperimentContainerState state = experiment.State;
            if (state.Status != ExperimentContainerStatus.Running) return;
            if (state.ResearchCategory != experiment.Definition.ScienceCategory)
            {
                LogOnce(state.ContainerId, "Background research category does not match its catalog definition.");
                return;
            }
            if (state.WorkComplete())
            {
                Finalize(vessel, experiment, state, now, null);
                return;
            }
            BackgroundLab? lab = labs.FirstOrDefault(candidate => candidate.PersistentId == state.AssignedLaboratoryPersistentId);
            ProtoCrewMember? scientist = lab?.Part.protoModuleCrew?.FirstOrDefault(candidate =>
                candidate != null && string.Equals(candidate.name, state.AssignedKerbalName, StringComparison.Ordinal));
            ScienceExperiment? science = ResearchAndDevelopment.GetExperiment(experiment.Definition.Id);
            bool manuallyPaused = ManuallyPaused(experiment.Part);
            StationExperimentPauseReason environment = StationExperimentEnvironment.Evaluate(experiment.Definition, state, vessel, situation, experiment.Part, equipment);
            bool equipmentContinuous = StationEquipmentContinuity.Observe(state, experiment.Definition, vessel, experiment.Part, equipment, now, authorize == null ? (bool?)null : false);
            bool continuous = StationSurfaceContinuity.Observe(state, vessel, now, authorize == null ? (bool?)null : false) && equipmentContinuous;
            bool valid = Eligible(experiment, lab, scientist, slots, manager, vessel, body, situation, equipment);
            if (!valid)
            {
                if (Write(experiment, state.CheckpointResearch(now, 0, 0, false, body, situation, state.CheckpointLevel), null))
                {
                    ForgetWarning(state);
                    StationExperimentPauseReason reason = manuallyPaused ? StationExperimentPauseReason.ManuallyPaused :
                        StationExecutionRules.PrimaryReason(true, state.Status, false, state.HasAssignment,
                            scientist != null, scientist != null && StationExperimentRuntime.ValidScientist(scientist, experiment.Definition),
                            state.HasSlotBinding, lab != null, lab != null, (lab != null && lab.Supports(experiment.Definition.LabCapability)),
                            scientist != null, true, slots.ContainsKey(state.ContainerId),
                            experiment.Definition.AllowsBody(body),
                            science != null && StationSituationRules.Allowed(science.situationMask, situation));
                    if (reason == StationExperimentPauseReason.None) reason = environment;
                    if (reason == StationExperimentPauseReason.None) reason = managerReason;
                    StationExperimentNotifications.Observe(experiment.State, vessel.vesselName, reason);
                }
                return;
            }
            double start = state.LastCheckpointUT ?? now;
            bool rebasing = !continuous || !state.CheckpointVerified || manager!.ValidSince > start ||
                state.CheckpointBody != body || state.CheckpointSituation != situation;
            KerbalSpecialtyScenario.ObserveScientistLevelAtCheckpoint(scientist!);
            if (!KerbalSpecialtyScenario.TryGetScientistProfile(scientist!.name, out var profile) ||
                profile.Score(state.ResearchCategory!.Value) != state.ResearchAffinity ||
                !KerbalSpecialtyScenario.TryGetScientistLevelSegments(scientist.name, rebasing ? now : start, now, out var segments))
            {
                if (Write(experiment, state.CheckpointResearch(now, 0, 0, false, body, situation, state.CheckpointLevel), null))
                    StationExperimentNotifications.Observe(experiment.State, vessel.vesselName, StationExperimentPauseReason.ResearchContextUnverified);
                return;
            }
            using (var engineer = koffee.Begin(manager!.EngineerName, sources))
            using (var activations = koffee.Begin(state.AssignedKerbalName, sources))
            {
                StationResearchCredit credit = rebasing ? default : StationResearchInterval.Process(
                    state.ResearchPointsRequired, state.AccumulatedResearchPoints, state.ResearchAffinity, segments,
                    (from, to, remainingSeconds) =>
                    {
                        if (authorize == null) return engineer.TeamCoverage(activations, from, to, remainingSeconds);
                        double powered = authorize(state.ContainerId, from, to);
                        if (!ExperimentContainerState.Finite(powered) || powered < 0 || powered > to - from + 1e-8)
                            throw new InvalidOperationException("Invalid background power coverage.");
                        if (!(powered > 0) || !(to > from)) return 0;
                        double fraction = Math.Min(1, powered / (to - from));
                        double realLimit = Math.Min(to - from, remainingSeconds / fraction);
                        return engineer.TeamCoverage(activations, from, to, realLimit,
                            (a, b) => Math.Min(remainingSeconds, authorize(state.ContainerId, a, b)));
                    }, KoffeeResearchRates.At, KoffeeResearchRates.NextBoundary);
                bool verified = authorize != null || credit.Complete || ((rebasing || credit.FullyCovered) && engineer.EnsureTeam(activations, now));
                ExperimentContainerState updated = state.CheckpointResearch(now, credit.Points, credit.CoveredSeconds,
                    verified, body, situation, scientist.experienceLevel);
                if (ReferenceEquals(updated, state) && credit.Points > 0) { publicationFailures++; return; }
                bool powerBlocked = authorize != null && !rebasing && credit.CoveredSeconds == 0 && !credit.Complete;
                bool teamReady = !powerBlocked || engineer.EnsureTeam(activations, now);
                bool written = updated.WorkComplete()
                    ? Finalize(vessel, experiment, updated, now, activations, engineer)
                    : Write(experiment, updated, null);
                if (!written) return;
                if (credit.CoveredSeconds > 0 || (verified && authorize == null)) { activations.Commit(); engineer.Commit(); }
                if (verified || updated.WorkComplete()) ForgetWarning(state);
                else WarnKoffee(vessel, updated);
                StationExperimentNotifications.Observe(experiment.State, vessel.vesselName,
                    powerBlocked
                        ? teamReady ? StationExperimentPauseReason.NoElectricity : StationExperimentPauseReason.TeamNeedsKoffee
                        : verified ? StationExperimentPauseReason.None : StationExperimentPauseReason.TeamNeedsKoffee);
            }
        }

        private static bool Finalize(Vessel vessel, BackgroundExperiment experiment,
            ExperimentContainerState state, double now,
            StationKoffeeSession.Transaction? activations, StationKoffeeSession.Transaction? engineer = null)
        {
            try
            {
                StationScienceResult result = StationScienceResultFactory.Create(state,
                    experiment.Definition, state.HasContextBinding ? FlightGlobals.GetBodyByName(state.BoundBody) : vessel.mainBody,
                    vessel.latitude, vessel.longitude,
                    experiment.Part.flightID, now);
                var dataNode = new ConfigNode("ScienceData");
                result.Data.Save(dataNode);
                if (!Write(experiment, result.State, dataNode)) return false;
                StationExperimentNotifications.Observe(result.State, vessel.vesselName, StationExperimentPauseReason.ResultAvailable);
                activations?.Commit();
                engineer?.Commit();
                ForgetWarning(state);
                try { GameEvents.OnExperimentDeployed.Fire(result.Data); }
                catch (Exception eventError)
                {
                    Debug.LogError("[Koffee] Background station ScienceData was finalized, " +
                        "but an OnExperimentDeployed listener failed: " + eventError);
                }
                return true;
            }
            catch (Exception error)
            {
                publicationFailures++;
                LogOnce(state.ContainerId, "Background station experiment could not be " +
                    "finalized: " + error);
                return false;
            }
        }

        private static bool Write(BackgroundExperiment experiment,
            ExperimentContainerState state, ConfigNode? scienceData)
        {
            if (ExperimentContainerSnapshotAdapter.Write(experiment.Part, state,
                scienceData, out string error))
            {
                StationBackgroundElectricity.Stamp(experiment.Part);
                experiment.State = state;
                return true;
            }
            publicationFailures++;
            LogOnce(state.ContainerId, "Background station state was not written: " + error);
            return false;
        }

        private static BackgroundExperiment? ReadExperiment(ProtoPartSnapshot part)
        {
            try
            {
                ModuleKoffeeExperiment[] prefabModules = part.partPrefab?.Modules?
                    .OfType<ModuleKoffeeExperiment>().ToArray() ??
                    Array.Empty<ModuleKoffeeExperiment>();
                if (prefabModules.Length != 1 ||
                    string.IsNullOrWhiteSpace(ExperimentContainerSnapshotAdapter.ExperimentId(part)) ||
                    !StationExperimentCatalogLoader.Current.TryGet(
                        ExperimentContainerSnapshotAdapter.ExperimentId(part), out StationExperimentDefinition definition))
                    return null;
                ExperimentContainerReadResult result = ExperimentContainerSnapshotAdapter.Read(
                    part, definition.Id, out ExperimentContainerState? state, out string error);
                if (result != ExperimentContainerReadResult.Valid || state == null)
                {
                    if (result == ExperimentContainerReadResult.Invalid)
                        Debug.LogError("[Koffee] Background station container is disabled: " + error);
                    return null;
                }
                return new BackgroundExperiment(part, definition, state);
            }
            catch (Exception error)
            {
                Debug.LogError("[Koffee] Background station container could not be read: " + error);
                return null;
            }
        }

        private static BackgroundLab? ReadLab(ProtoPartSnapshot part)
        {
            Part? prefabPart = part.partPrefab;
            if (prefabPart == null) return null;
            ProtoPartModuleSnapshot[] snapshots = part.modules?.Where(module => module != null &&
                (string.Equals(module.moduleName, nameof(ModuleKoffeeLab), StringComparison.Ordinal) ||
                 string.Equals(module.moduleValues?.GetValue("name"), nameof(ModuleKoffeeLab),
                    StringComparison.Ordinal))).ToArray() ??
                Array.Empty<ProtoPartModuleSnapshot>();
            ModuleKoffeeLab[] prefabs = prefabPart.Modules?
                .OfType<ModuleKoffeeLab>().ToArray() ?? Array.Empty<ModuleKoffeeLab>();
            if (snapshots.Length != 1 || prefabs.Length != 1 || part.persistentId == 0)
                return null;
            return new BackgroundLab(part, prefabPart.CrewCapacity,
                prefabs[0].SnapshotExecutionAvailable(snapshots[0].moduleValues)
                    ? prefabs[0].stationCapabilities : "");
        }

        private static Dictionary<Guid, StationSlotAllocation> Allocate(
            IEnumerable<BackgroundExperiment> experiments, IEnumerable<BackgroundLab> labs)
        {
            StationSlotSource[] sources = labs.Select(lab => new StationSlotSource(
                lab.PersistentId, lab.SlotCapacity,
                Enum.GetValues(typeof(StationLabCapability)).Cast<StationLabCapability>()
                    .Where(lab.Supports))).ToArray();
            StationSlotRequest[] requests = experiments.Where(experiment =>
                    experiment.State.Status == ExperimentContainerStatus.Running &&
                    experiment.State.HasSlotBinding &&
                    !experiment.State.WorkComplete())
                .Select(experiment => new StationSlotRequest(experiment.State.ContainerId,
                    experiment.Definition.LabCapability,
                    experiment.State.AssignedLaboratoryPersistentId,
                    experiment.State.AssignedLaboratorySlot)).ToArray();
            var result = new Dictionary<Guid, StationSlotAllocation>();
            IReadOnlyDictionary<Guid, StationSlotAllocation> allocated =
                StationSlotAllocator.Allocate(requests, sources);
            var reservedScientists = new HashSet<string>(StringComparer.Ordinal);
            foreach (BackgroundExperiment experiment in experiments)
                if (allocated.TryGetValue(experiment.State.ContainerId,
                    out StationSlotAllocation allocation) &&
                    reservedScientists.Add(experiment.State.AssignedKerbalName))
                    result.Add(experiment.State.ContainerId, allocation);
            return result;
        }

        private static List<IResourceAccount> KoffeeAccounts(
            IEnumerable<ProtoPartSnapshot> parts) => parts.SelectMany(part =>
                part.resources?.Where(resource => resource != null &&
                    string.Equals(resource.resourceName, KoffeeIdentifiers.ResourceName,
                        StringComparison.Ordinal)).Select(resource =>
                        (IResourceAccount)new ProtoResourceAccount(part, resource)) ??
                    Enumerable.Empty<IResourceAccount>()).ToList();

        private static void WarnKoffee(Vessel vessel, ExperimentContainerState state)
        {
            if (!state.RunId.HasValue || !warnedRuns.Add(state.RunId.Value)) return;
            string scientist = string.IsNullOrEmpty(state.AssignedKerbalDisplayName)
                ? state.AssignedKerbalName : state.AssignedKerbalDisplayName;
            string message = Localizer.Format("#Koffee_Station_BackgroundKoffeeDepleted",
                scientist, vessel.vesselName ?? "");
            try { ScreenMessages.PostScreenMessage(message, 8f, ScreenMessageStyle.UPPER_CENTER); }
            catch (Exception error)
            {
                Debug.LogError("[Koffee] Could not display background Koffee warning: " + error);
            }
        }

        private static void ForgetWarning(ExperimentContainerState state)
        {
            if (state.RunId.HasValue) warnedRuns.Remove(state.RunId.Value);
        }

        private static void LogOnce(Guid containerId, string message)
        {
            if (loggedContainers.Add(containerId)) Debug.LogError("[Koffee] " + message);
        }

        private sealed class BackgroundExperiment
        {
            internal ProtoPartSnapshot Part { get; }
            internal StationExperimentDefinition Definition { get; }
            internal ExperimentContainerState State { get; set; }

            internal BackgroundExperiment(ProtoPartSnapshot part,
                StationExperimentDefinition definition, ExperimentContainerState state)
            {
                Part = part;
                Definition = definition;
                State = state;
            }
        }

        private sealed class BackgroundLab
        {
            internal ProtoPartSnapshot Part { get; }
            internal uint PersistentId => Part.persistentId;
            internal int SlotCapacity { get; }
            private readonly string capabilities;

            internal BackgroundLab(ProtoPartSnapshot part, int crewCapacity,
                string capabilities)
            {
                Part = part;
                SlotCapacity = StationLabRules.SlotCapacity(crewCapacity);
                this.capabilities = capabilities ?? "";
            }

            internal bool Supports(StationLabCapability capability) => capabilities
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(value => string.Equals(value, capability.ToString(),
                    StringComparison.Ordinal));
        }

        private sealed class ProtoResourceAccount : IResourceAccount
        {
            private readonly ProtoPartSnapshot part;
            private readonly ProtoPartResourceSnapshot resource;

            internal ProtoResourceAccount(ProtoPartSnapshot part,
                ProtoPartResourceSnapshot resource)
            {
                this.part = part;
                this.resource = resource;
            }

            public double Amount { get => resource.amount; set => resource.amount = value; }
            public double Capacity => resource.maxAmount;
            public bool CanWithdraw => resource.flowState;
            public int Priority => part.resourcePriorityOffset;
            public uint SourceId => part.flightID;
        }

    }

    internal static class StationExperimentBackgroundDriver
    {
        private static float nextUpdate;

        internal static void Update()
        {
            if (Time.realtimeSinceStartup < nextUpdate) return;
            nextUpdate = Time.realtimeSinceStartup + 0.5f;
            StationExperimentBackground.TickAll(Planetarium.GetUniversalTime());
        }
    }

    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public sealed class StationExperimentTrackingCoordinator : MonoBehaviour
    {
        private void Update() => StationExperimentBackgroundDriver.Update();
    }

    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public sealed class StationExperimentSpaceCenterCoordinator : MonoBehaviour
    {
        private void Update() => StationExperimentBackgroundDriver.Update();
    }
}

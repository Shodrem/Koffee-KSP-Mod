using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal readonly struct StationExperimentReservation
    {
        internal readonly ModuleKoffeeLab Laboratory;
        internal readonly int Slot;
        internal StationExperimentReservation(ModuleKoffeeLab laboratory, int slot)
        {
            Laboratory = laboratory;
            Slot = slot;
        }
    }

    internal readonly struct StationScientistCandidate
    {
        internal readonly ProtoCrewMember Scientist;
        internal readonly ModuleKoffeeLab? Laboratory;
        internal readonly Part Seat;

        internal StationScientistCandidate(ProtoCrewMember scientist,
            ModuleKoffeeLab laboratory)
        {
            Scientist = scientist;
            Laboratory = laboratory;
            Seat = laboratory.part;
        }

    }

    internal sealed class StationExperimentRuntimeSnapshot
    {
        internal ModuleKoffeeExperiment Module { get; }
        internal StationExperimentDefinition? Definition { get; }
        internal ExperimentContainerState? State { get; }
        internal StationResearchPresentation Research { get; }
        internal StationExperimentPauseReason PauseReason { get; }
        internal StationExperimentReservation? Reservation { get; }
        internal string Body { get; }
        internal string Situation { get; }
        internal IReadOnlyList<StationScientistCandidate> SelectableScientists { get; }
        internal ModuleKoffeeLab? AssignedLaboratory { get; }
        internal double RequiredPoints => Research.RequiredPoints;
        internal double AccumulatedPoints => Research.AccumulatedPoints;
        internal double Remaining => Research.RemainingSeconds ?? 0;
        internal double RatePerHour => Research.RatePerMinute * 60;
        internal bool WorkComplete => State != null && Definition != null &&
            State.WorkComplete();

        internal StationExperimentRuntimeSnapshot(ModuleKoffeeExperiment module,
            StationExperimentDefinition? definition, StationExperimentPauseReason reason,
            StationExperimentReservation? reservation, string body, string situation,
            IReadOnlyList<StationScientistCandidate> selectableScientists,
            ModuleKoffeeLab? assignedLaboratory)
        {
            Module = module;
            State = module.State;
            Research = new StationResearchPresentation(module, definition, State);
            Definition = definition;
            PauseReason = reason;
            Reservation = reservation;
            Body = body;
            Situation = situation;
            SelectableScientists = selectableScientists;
            AssignedLaboratory = assignedLaboratory;
        }
    }

    internal static class StationExperimentRuntime
    {
        internal static IReadOnlyList<StationExperimentRuntimeSnapshot> Tick(Vessel vessel, double now, bool? electricalMode = null)
        {
            if (vessel == null || !vessel.loaded || !ExperimentContainerState.Finite(now))
                return Array.Empty<StationExperimentRuntimeSnapshot>();
            List<ModuleKoffeeLab> labs = Modules<ModuleKoffeeLab>(vessel).ToList();
            List<ModuleKoffeeExperiment> experiments = Modules<ModuleKoffeeExperiment>(vessel)
                .OrderBy(module => module.State?.StartUT ?? double.MaxValue)
                .ThenBy(module => module.State?.ContainerId ?? Guid.Empty).ToList();
            Dictionary<Guid, StationExperimentReservation> reservations =
                BuildReservations(experiments, labs);
            string body = CurrentBody(vessel);
            string situation = CurrentSituation(vessel);
            var equipment = StationExperimentEquipment.Capture(vessel);
            var eligible = new Dictionary<Guid, StationExperimentPauseReason>();
            foreach (ModuleKoffeeExperiment module in experiments)
            {
                ExperimentContainerState? candidateState = module.State;
                StationExperimentDefinition? candidateDefinition = Definition(module);
                StationExperimentReservation? candidateReservation = candidateState != null &&
                    reservations.TryGetValue(candidateState.ContainerId,
                        out StationExperimentReservation allocated) ? allocated :
                        (StationExperimentReservation?)null;
                if (candidateState != null)
                    eligible[candidateState.ContainerId] = Reason(module, candidateDefinition, labs,
                        candidateReservation, vessel, body, situation, equipment: equipment);
            }
            var session = new StationKoffeeSession();
            ExperimentManagerContext? manager = ExperimentManagerService.Select(vessel, now, true, out _);
            bool intervalApplies = StationPowerPolicy.Observe(vessel, now, electricalMode);
            StationStockPower? power = manager?.Part == null ? null : StationStockPower.For(
                manager.Part.Modules.OfType<ModuleKoffeeExperimentManager>().First(), intervalApplies, now);
            var snapshots = new List<StationExperimentRuntimeSnapshot>(experiments.Count);
            foreach (ModuleKoffeeExperiment module in experiments)
            {
                StationExperimentDefinition? definition = Definition(module);
                ExperimentContainerState? state = module.State;
                StationExperimentReservation? reservation = state != null &&
                    reservations.TryGetValue(state.ContainerId, out StationExperimentReservation found)
                        ? found : (StationExperimentReservation?)null;
                StationExperimentPauseReason reason = state != null &&
                    eligible.TryGetValue(state.ContainerId, out StationExperimentPauseReason foundReason)
                        ? foundReason : StationExperimentPauseReason.IncompatibleState;
                if (state?.Status == ExperimentContainerStatus.Running && definition != null)
                {
                    StationExperimentPauseReason progressReason = StationExperimentPauseReason.None;
                    bool rebased = module.CheckpointRebasePending;
                    bool equipmentContinuous = state.WorkComplete() || StationEquipmentContinuity.Observe(state, definition, vessel, module.part, equipment);
                    bool surfaceContinuous = state.WorkComplete() || StationSurfaceContinuity.Observe(state, vessel);
                    if (!state.WorkComplete() && (module.CheckpointRebasePending ||
                        !surfaceContinuous || !equipmentContinuous))
                    {
                        module.RebaseCheckpoint(now, body, situation);
                        rebased = true;
                    }
                    else if (!state.WorkComplete())
                        progressReason = Advance(module, definition, reason, reservation,
                            vessel, now, body, situation, session, manager, power!);
                    module.ElectricityBlocked = progressReason == StationExperimentPauseReason.NoElectricity;
                    if (module.State?.WorkComplete() == true)
                        module.TryFinalize(definition, now, out _);
                    reason = Reason(module, definition, labs, reservation, vessel, body,
                        situation, equipment: equipment);
                    if (reason == StationExperimentPauseReason.None && progressReason != StationExperimentPauseReason.None)
                        reason = progressReason;
                    if (!rebased || reason != StationExperimentPauseReason.None)
                        StationExperimentNotifications.Observe(module.State!, vessel.vesselName, reason);
                }
                module.ElectricalWorkReady = module.State?.Status == ExperimentContainerStatus.Running &&
                    module.State.CheckpointVerified && !module.State.WorkComplete() &&
                    (reason == StationExperimentPauseReason.None || reason == StationExperimentPauseReason.NoElectricity);
                snapshots.Add(new StationExperimentRuntimeSnapshot(module, definition, reason,
                    reservation, body, situation, Scientists(vessel, labs, definition, AssignedScientists(experiments, module)),
                    AssignedLaboratory(state)));
            }
            StationElectricalDemand.Refresh(vessel);
            return snapshots;
        }

        internal static IReadOnlyList<StationExperimentRuntimeSnapshot> Capture(Vessel vessel)
        {
            if (vessel == null || !vessel.loaded)
                return Array.Empty<StationExperimentRuntimeSnapshot>();
            List<ModuleKoffeeLab> labs = Modules<ModuleKoffeeLab>(vessel).ToList();
            List<ModuleKoffeeExperiment> experiments = Modules<ModuleKoffeeExperiment>(vessel)
                .OrderBy(module => module.State?.StartUT ?? double.MaxValue)
                .ThenBy(module => module.State?.ContainerId ?? Guid.Empty).ToList();
            Dictionary<Guid, StationExperimentReservation> reservations =
                BuildReservations(experiments, labs);
            string body = CurrentBody(vessel);
            string situation = CurrentSituation(vessel);
            var equipment = StationExperimentEquipment.Capture(vessel);
            return experiments.Select(module =>
            {
                StationExperimentDefinition? definition = Definition(module);
                StationExperimentReservation? reservation = module.State != null &&
                    reservations.TryGetValue(module.State.ContainerId,
                        out StationExperimentReservation found) ? found :
                        (StationExperimentReservation?)null;
                return new StationExperimentRuntimeSnapshot(module, definition,
                    Reason(module, definition, labs, reservation, vessel, body, situation, true, equipment),
                    reservation, body, situation, Scientists(vessel, labs, definition, AssignedScientists(experiments, module)),
                    AssignedLaboratory(module.State));
            }).ToArray();
        }

        private static StationExperimentPauseReason Advance(ModuleKoffeeExperiment module,
            StationExperimentDefinition definition, StationExperimentPauseReason reason,
            StationExperimentReservation? reservation,
            Vessel vessel, double now, string body, string situation,
            StationKoffeeSession session, ExperimentManagerContext? manager, StationStockPower power)
        {
            ExperimentContainerState state = module.State!;
            if (reason != StationExperimentPauseReason.None || manager?.Part == null)
            {
                module.ApplyResearchCheckpoint(now, default, false, body, situation, state.CheckpointLevel);
                return reason;
            }
            double start = state.LastCheckpointUT ?? now;
            bool rebasing = !state.CheckpointVerified || manager!.ValidSince > start ||
                state.CheckpointBody != body || state.CheckpointSituation != situation;
            ProtoCrewMember worker = reservation!.Value.Laboratory.part.protoModuleCrew
                .First(c => c.name == state.AssignedKerbalName);
            KerbalSpecialtyScenario.ObserveScientistLevelAtCheckpoint(worker);
            if (!KerbalSpecialtyScenario.TryGetScientistProfile(worker.name, out var profile) ||
                !state.ResearchCategory.HasValue || state.ResearchCategory != definition.ScienceCategory ||
                profile.Score(state.ResearchCategory.Value) != state.ResearchAffinity ||
                !KerbalSpecialtyScenario.TryGetScientistLevelSegments(worker.name, rebasing ? now : start, now, out var segments))
            {
                module.ApplyResearchCheckpoint(now, default, false, body, situation, state.CheckpointLevel);
                return StationExperimentPauseReason.ResearchContextUnverified;
            }
            using (var electricity = power.Begin(module, definition, reservation!.Value.Laboratory.part))
            using (var scientist = session.Begin(worker, module.part))
            using (var engineer = session.Begin(vessel.GetVesselCrew().First(c => c.name == manager!.EngineerName), manager!.Part!))
            {
                StationResearchCredit credit = rebasing ? default : StationResearchInterval.Process(
                    state.ResearchPointsRequired, state.AccumulatedResearchPoints, state.ResearchAffinity, segments,
                    (from, to, remainingSeconds) => electricity.Blocked ? 0 :
                        engineer.TeamCoverage(scientist, from, to, remainingSeconds, electricity.Authorize),
                    KoffeeResearchRates.At, KoffeeResearchRates.NextBoundary);
                // Electrical starvation does not invalidate the scientific context.
                // Advance the checkpoint to now and retry new work on the next tick.
                bool verified = electricity.ControlsWork || credit.Complete || ((rebasing || credit.FullyCovered) &&
                    engineer.EnsureTeam(scientist, now));
                if (!module.ApplyResearchCheckpoint(now, credit, verified, body, situation, worker.experienceLevel)) return StationExperimentPauseReason.None;
                electricity.Commit();
                if (credit.CoveredSeconds > 0 || (verified && !electricity.ControlsWork)) { scientist.Commit(); engineer.Commit(); }
                if (electricity.Blocked) return StationExperimentPauseReason.NoElectricity;
                return verified && (rebasing || credit.FullyCovered) ? StationExperimentPauseReason.None : StationExperimentPauseReason.TeamNeedsKoffee;
            }
        }

        internal static bool ValidScientist(ProtoCrewMember? kerbal,
            StationExperimentDefinition definition) => kerbal != null &&
            StationPersonnelRules.Valid(kerbal.name, kerbal.trait, kerbal.experienceLevel,
                definition.Personnel.Trait, definition.Personnel.MinimumLevel);

        internal static string CurrentBody(Vessel vessel) => vessel?.mainBody?.bodyName ?? "";
        internal static string CurrentSituation(Vessel vessel)
        {
            try { return ScienceUtil.GetExperimentSituation(vessel).ToString(); }
            catch { return ""; }
        }

        internal static IReadOnlyList<string> ValidSituations(string experimentId, object? container = null)
        {
            if (string.IsNullOrWhiteSpace(experimentId)) return Array.Empty<string>();
            ScienceExperiment experiment = ResearchAndDevelopment.GetExperiment(experimentId);
            uint mask = experiment?.situationMask ?? 0;
            return new[] { "SrfLanded", "SrfSplashed", "FlyingLow", "FlyingHigh",
                "InSpaceLow", "InSpaceHigh" }.Where(value =>
                    StationSituationRules.Allowed(mask, value) &&
                    StationExperimentEnvironment.SurfaceAllowed(container, value)).ToArray();
        }

        private static StationExperimentPauseReason Reason(ModuleKoffeeExperiment module,
            StationExperimentDefinition? definition, IReadOnlyList<ModuleKoffeeLab> labs,
            StationExperimentReservation? reservation, Vessel vessel, string body,
            string situation, bool readOnly = false, StationEquipmentSnapshot? equipment = null)
        {
            bool compatibleState = !module.HasIncompatibleState && module.State != null &&
                definition != null && (module.State.Status == ExperimentContainerStatus.Available ||
                    module.State.ResearchCategory == definition.ScienceCategory);
            if (!compatibleState)
                return StationExperimentPauseReason.IncompatibleState;
            ExperimentContainerState state = module.State!;
            if (state.Status == ExperimentContainerStatus.Available)
            {
                var availableEnvironment = StationExperimentEnvironment.Evaluate(definition!, state, vessel, situation, module.part, equipment);
                if (availableEnvironment != StationExperimentPauseReason.None) return availableEnvironment;
            }
            if (state.Status == ExperimentContainerStatus.Running && module.manuallyPaused &&
                !state.WorkComplete())
                return StationExperimentPauseReason.ManuallyPaused;
            ProtoCrewMember? scientist = Crew(vessel).FirstOrDefault(kerbal =>
                string.Equals(kerbal.name, state.AssignedKerbalName, StringComparison.Ordinal));
            ModuleKoffeeLab? assignedLab = AssignedLaboratory(state);
            bool sameVessel = assignedLab != null && ReferenceEquals(assignedLab.part.vessel, vessel);
            bool compatible = assignedLab != null && assignedLab.Supports(definition!.LabCapability);
            bool scientistInLab = assignedLab != null && assignedLab.part.protoModuleCrew.Any(kerbal =>
                kerbal != null && string.Equals(kerbal.name, state.AssignedKerbalName,
                    StringComparison.Ordinal));
            ScienceExperiment science = ResearchAndDevelopment.GetExperiment(definition!.Id);
            bool koffee = scientist != null &&
                ((readOnly ? EngineeringRenewalCoordinator.CaptureWindows(scientist) :
                    EngineeringRenewalCoordinator.Windows(scientist)).Any(window => window.Contains(
                    state.LastCheckpointUT ?? Planetarium.GetUniversalTime()) ||
                    (window.StartUT < Planetarium.GetUniversalTime() && window.EndUT > (state.LastCheckpointUT ?? Planetarium.GetUniversalTime()))) ||
                 KoffeeSources.Available(module.part) >= KoffeeSettingsLoader.Current.ActivationCost);
            StationExperimentPauseReason scientificReason = StationExecutionRules.PrimaryReason(true, state.Status,
                state.WorkComplete(), state.HasAssignment,
                scientist != null, scientist != null && ValidScientist(scientist, definition),
                state.HasSlotBinding, assignedLab != null, sameVessel, compatible,
                scientistInLab, koffee, reservation.HasValue,
                definition.AllowsBody(body),
                science != null && StationSituationRules.Allowed(science.situationMask, situation));
            if (scientificReason != StationExperimentPauseReason.None) return scientificReason;
            StationExperimentPauseReason environment = StationExperimentEnvironment.Evaluate(definition, state, vessel, situation, module.part, equipment);
            if (environment != StationExperimentPauseReason.None) return environment;
            ExperimentManagerService.Select(vessel, Planetarium.GetUniversalTime(), false, out var managerReason, readOnly);
            if (managerReason == StationExperimentPauseReason.None && readOnly && module.ElectricityBlocked &&
                StationPowerPolicy.Applies(vessel)) return StationExperimentPauseReason.NoElectricity;
            return managerReason;
        }

        private static Dictionary<Guid, StationExperimentReservation> BuildReservations(
            IEnumerable<ModuleKoffeeExperiment> experiments, IEnumerable<ModuleKoffeeLab> labs)
        {
            ModuleKoffeeLab[] available = labs.ToArray();
            var sources = available.Select(lab => new StationSlotSource(lab.part.persistentId,
                lab.StationSlotCapacity, Enum.GetValues(typeof(StationLabCapability))
                    .Cast<StationLabCapability>().Where(lab.Supports))).ToArray();
            var requests = new List<StationSlotRequest>();
            foreach (ModuleKoffeeExperiment module in experiments)
            {
                ExperimentContainerState? state = module.State;
                StationExperimentDefinition? definition = Definition(module);
                if (state == null || definition == null ||
                    state.Status != ExperimentContainerStatus.Running ||
                    state.WorkComplete()) continue;
                if (!state.HasSlotBinding) continue;
                requests.Add(new StationSlotRequest(state.ContainerId,
                    definition.LabCapability, state.AssignedLaboratoryPersistentId,
                    state.AssignedLaboratorySlot));
            }
            IReadOnlyDictionary<Guid, StationSlotAllocation> allocated =
                StationSlotAllocator.Allocate(requests, sources);
            var result = new Dictionary<Guid, StationExperimentReservation>();
            var reservedScientists = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModuleKoffeeExperiment module in experiments)
            {
                ExperimentContainerState? state = module.State;
                if (state == null || !allocated.TryGetValue(state.ContainerId,
                    out StationSlotAllocation allocation) ||
                    !reservedScientists.Add(state.AssignedKerbalName)) continue;
                ModuleKoffeeLab? lab = available.FirstOrDefault(candidate =>
                    candidate.part.persistentId == allocation.LaboratoryId);
                if (lab != null) result[state.ContainerId] = new StationExperimentReservation(
                    lab, allocation.Slot);
            }
            return result;
        }

        private static StationExperimentDefinition? Definition(ModuleKoffeeExperiment module) =>
            StationExperimentCatalogLoader.Current.TryGet(module.experimentID,
                out StationExperimentDefinition found) ? found : null;

        internal static bool ScientistAssignedElsewhere(ModuleKoffeeExperiment requester, string name) =>
            AssignedScientists(Modules<ModuleKoffeeExperiment>(requester.vessel), requester).Contains(name);

        private static HashSet<string> AssignedScientists(
            IEnumerable<ModuleKoffeeExperiment> experiments, ModuleKoffeeExperiment requester) =>
            new HashSet<string>(experiments.Where(module => !ReferenceEquals(module, requester) &&
                module.State?.HasAssignment == true &&
                (module.State.Status == ExperimentContainerStatus.Available ||
                 (module.State.Status == ExperimentContainerStatus.Running &&
                  (!(Definition(module) is StationExperimentDefinition definition) ||
                   !module.State.WorkComplete()))))
                .Select(module => module.State!.AssignedKerbalName), StringComparer.Ordinal);

        private static IReadOnlyList<StationScientistCandidate> Scientists(Vessel vessel,
            IEnumerable<ModuleKoffeeLab> labs, StationExperimentDefinition? definition,
            ISet<string> busyScientists)
        {
            if (definition == null) return Array.Empty<StationScientistCandidate>();
            bool Eligible(ProtoCrewMember c) => c != null && !busyScientists.Contains(c.name) && ValidScientist(c, definition);
            var candidates = labs.Where(l => l.part != null && l.part.State != PartStates.DEAD &&
                    l.part.persistentId != 0 && l.Supports(definition.LabCapability))
                .SelectMany(l => l.part.protoModuleCrew.Where(Eligible).Select(c => new StationScientistCandidate(c, l)));
            return candidates.OrderBy(c => c.Scientist.name, StringComparer.Ordinal).ThenBy(c => c.Seat.persistentId).ToArray();
        }

        private static ModuleKoffeeLab? AssignedLaboratory(ExperimentContainerState? state)
        {
            if (state == null || !state.HasAssignedLaboratory) return null;
            return FlightGlobals.Vessels.Where(vessel => vessel != null && vessel.loaded)
                .SelectMany(vessel => Modules<ModuleKoffeeLab>(vessel))
                .FirstOrDefault(lab => lab.part.persistentId ==
                    state.AssignedLaboratoryPersistentId);
        }

        private static IEnumerable<ProtoCrewMember> Crew(Vessel vessel) =>
            vessel.GetVesselCrew().Where(kerbal => kerbal != null);

        private static IEnumerable<T> Modules<T>(Vessel vessel) where T : PartModule =>
            vessel.parts.Where(part => part != null && part.State != PartStates.DEAD)
                .SelectMany(part => part.Modules.OfType<T>());

    }

    internal static class StationExperimentCommands
    {
        internal static bool Assign(PartModule requester, Guid containerId, string kerbalName,
            uint laboratoryPersistentId, out string error)
        {
            error = Localizer.Format("#Koffee_Station_Error_ContainerOrScientistNotFound");
            if (!TryContainer(requester, containerId, out ModuleKoffeeExperiment? module,
                out StationExperimentDefinition? definition)) return false;
            ModuleKoffeeLab? lab = module.part.vessel.parts
                .Where(part => part.State != PartStates.DEAD && part.persistentId == laboratoryPersistentId)
                .SelectMany(part => part.Modules.OfType<ModuleKoffeeLab>())
                .FirstOrDefault(candidate => candidate.Supports(definition.LabCapability));
            Part? seat = lab?.part;
            ProtoCrewMember? kerbal = seat?.protoModuleCrew.FirstOrDefault(candidate =>
                candidate != null && string.Equals(candidate.name, kerbalName,
                    StringComparison.Ordinal));
            if (!StationExperimentRuntime.ValidScientist(kerbal, definition))
            {
                error = Localizer.Format("#Koffee_Station_Error_ScientistNotInLab");
                return false;
            }
            if (StationExperimentRuntime.ScientistAssignedElsewhere(module, kerbalName))
            {
                error = Localizer.Format("#Koffee_Station_Error_ScientistBusy");
                return false;
            }
            return module.TryAssignScientist(kerbal!, definition,
                laboratoryPersistentId, out error);
        }

        internal static bool SetPaused(PartModule requester, Guid containerId, bool paused, out string error)
        {
            error = Localizer.Format("#Koffee_Station_Error_ContainerNotFound");
            if (!TryContainer(requester, containerId, out ModuleKoffeeExperiment? module, out _)) return false;
            if (module.State?.Status != ExperimentContainerStatus.Running)
            {
                error = Localizer.Format("#Koffee_Station_Error_ContainerUnavailable");
                return false;
            }
            double now = Planetarium.GetUniversalTime();
            if (paused && !module.manuallyPaused) StationExperimentRuntime.Tick(module.vessel, now);
            return module.TrySetPaused(paused, now, out error);
        }

        internal static bool ClearAssignment(PartModule requester, Guid containerId, out string error)
        {
            error = Localizer.Format("#Koffee_Station_Error_ContainerNotFound");
            return TryContainer(requester, containerId, out ModuleKoffeeExperiment? module, out _) &&
                module.TryClearScientist(out error);
        }

        internal static bool Start(PartModule requester, Guid containerId, out string error, Part? preparedInstrument = null)
        {
            error = Localizer.Format("#Koffee_Station_Error_ContainerNotFound");
            if (!TryContainer(requester, containerId, out ModuleKoffeeExperiment? module,
                out StationExperimentDefinition? definition)) return false;
            Vessel vessel = module.part.vessel;
            ExperimentContainerState? state = module.State;
            if (state == null || !state.HasAssignment)
            {
                error = Localizer.Format("#Koffee_Station_Error_AssignScientist");
                return false;
            }
            ModuleKoffeeLab? assignedLab = vessel.parts
                .Where(part => part.State != PartStates.DEAD && part.persistentId == state.AssignedLaboratoryPersistentId)
                .SelectMany(part => part.Modules.OfType<ModuleKoffeeLab>())
                .FirstOrDefault();
            ProtoCrewMember? scientist = assignedLab?.part.protoModuleCrew?.FirstOrDefault(kerbal =>
                kerbal != null && string.Equals(kerbal.name, state.AssignedKerbalName,
                    StringComparison.Ordinal));
            if (!StationExperimentRuntime.ValidScientist(scientist, definition))
            {
                error = Localizer.Format("#Koffee_Station_Error_AssignedScientistInvalid");
                return false;
            }
            string body = StationExperimentRuntime.CurrentBody(vessel);
            string situation = StationExperimentRuntime.CurrentSituation(vessel);
            StationExperimentPauseReason environment = StationExperimentEnvironment.Evaluate(definition, state, vessel, situation, module.part);
            if (environment != StationExperimentPauseReason.None)
            {
                error = StationExperimentEquipment.PauseText(environment, definition);
                return false;
            }
            if (assignedLab == null || !assignedLab.Supports(definition.LabCapability))
            {
                error = Localizer.Format("#Koffee_Station_Error_AssignedLabUnavailable");
                return false;
            }
            var occupied = new HashSet<int>();
            foreach (ModuleKoffeeExperiment existing in vessel.parts.SelectMany(part =>
                part.Modules.OfType<ModuleKoffeeExperiment>()))
            {
                ExperimentContainerState? existingState = existing.State;
                if (existingState?.Status != ExperimentContainerStatus.Running ||
                    !StationExperimentCatalogLoader.Current.TryGet(existing.experimentID,
                        out StationExperimentDefinition existingDefinition) ||
                    existingState.WorkComplete()) continue;
                if (string.Equals(existingState.AssignedKerbalName,
                    state.AssignedKerbalName, StringComparison.Ordinal))
                {
                    error = Localizer.Format("#Koffee_Station_Error_ScientistBusy");
                    return false;
                }
                if (existingState.AssignedLaboratoryPersistentId ==
                    assignedLab!.part.persistentId && existingState.AssignedLaboratorySlot >= 0)
                    occupied.Add(existingState.AssignedLaboratorySlot);
            }
            int slot = Enumerable.Range(0, assignedLab.StationSlotCapacity)
                .FirstOrDefault(candidate => !occupied.Contains(candidate));
            if (assignedLab.StationSlotCapacity <= 0 || occupied.Contains(slot))
            {
                error = Localizer.Format("#Koffee_Station_Error_NoAssignedLabSlot");
                return false;
            }
            double now = Planetarium.GetUniversalTime();
            if (!module.TryPrepareResearch(now, out _, out _, out _, out _, readOnly: StationExperimentAnimationPreparation.Clip(definition) != ""))
            {
                error = Localizer.Format("#Koffee_Station_Error_ContainerNotReady");
                return false;
            }
            if (StationExperimentAnimationPreparation.Clip(definition) != "")
            {
                if (state.Status != ExperimentContainerStatus.Available)
                { error = Localizer.Format("#Koffee_Station_Error_ContainerUnavailable"); return false; }
                if (preparedInstrument == null && StationExperimentAnimationPreparation.Pending(module)) { error = ""; return true; }
                string readiness = StationSolarAcquisition.Readiness(definition,vessel,module.part,now,false);
                if (readiness != "") { error = StationSolarAcquisition.Error(readiness); return false; }
                if (preparedInstrument == null)
                {
                    if (ExperimentManagerService.Select(vessel,now,false,out _) == null) return false;
                    var instrument = StationExperimentReadings.RequiredInstrument(definition,vessel,module.part);
                    if (ModuleKoffeeExperimentAnimation.Find(instrument,StationExperimentAnimationPreparation.Clip(definition))?.Open != true)
                        return StationExperimentAnimationPreparation.Begin(requester,module,definition,out error);
                    preparedInstrument = instrument;
                }
                if (!ReferenceEquals(preparedInstrument,StationExperimentReadings.RequiredInstrument(definition,vessel,module.part)) ||
                    ModuleKoffeeExperimentAnimation.Find(preparedInstrument,StationExperimentAnimationPreparation.Clip(definition))?.Open != true)
                { error = StationSolarAcquisition.Error("LidClosed"); return false; }
            }
            var session = new StationKoffeeSession();
            ExperimentManagerContext? manager = ExperimentManagerService.Select(vessel, now, true, out var reason);
            if (manager?.Part == null)
            {
                error = Localizer.Format("#Koffee_Station_Pause_" + reason);
                return false;
            }
            // Configured hardware reserves cups only for actual authorized work.
            if (new[] { manager.Part, module.part, StationExperimentReadings.RequiredInstrument(definition, vessel, module.part) }
                .Where(p => p != null).Any(p => StationStockPower.Rate(p!) > 0))
            {
                StationPowerPolicy.Observe(vessel, now);
                return module.TryStartRun(now, body, situation, slot, out error);
            }
            using (var engineerActivation = session.Begin(vessel.GetVesselCrew().First(c => c.name == manager.EngineerName), manager.Part))
            using (var scientistActivation = session.Begin(scientist!, module.part))
            {
                if (!engineerActivation.EnsureTeam(scientistActivation, now))
                {
                    error = Localizer.Format("#Koffee_Manager_TeamNeedsKoffee");
                    return false;
                }
                if (!module.TryStartRun(now, body, situation, slot, out error)) return false;
                scientistActivation.Commit(); engineerActivation.Commit();
                return true;
            }
        }

        private static bool TryContainer(PartModule requester, Guid id, out ModuleKoffeeExperiment module,
            out StationExperimentDefinition definition)
        {
            module = null!;
            definition = null!;
            Vessel vessel = requester?.part?.vessel!;
            if (vessel == null || !vessel.loaded) return false;
            // An experiment PAW can only address its own installed container.
            // Manager callers remain supported for the shared command service.
            if (requester is ModuleKoffeeExperiment experiment)
            {
                if (experiment.State?.ContainerId != id || !vessel.parts.Contains(experiment.part) ||
                    experiment.part.State == PartStates.DEAD ||
                    !experiment.part.Modules.OfType<ModuleKoffeeExperiment>().Contains(experiment)) return false;
                module = experiment;
            }
            else
            {
                if (!(requester is ModuleKoffeeExperimentManager manager) ||
                    !ExperimentManagerService.Managers(vessel).Contains(manager)) return false;
                module = vessel.parts.SelectMany(part => part.Modules.OfType<ModuleKoffeeExperiment>())
                    .FirstOrDefault(candidate => candidate.State?.ContainerId == id)!;
            }
            return module != null && StationExperimentCatalogLoader.Current.TryGet(
                module.experimentID, out definition);
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class StationExperimentCoordinator : MonoBehaviour
    {
        private bool wasHighWarp;
        private void Awake()
        {
            wasHighWarp = StationPowerPolicy.HighWarp;
            GameEvents.onVesselSituationChange.Add(OnSituation);
            GameEvents.onVesselSOIChanged.Add(OnBody);
            GameEvents.onGameStateLoad.Add(OnLoad);
            GameEvents.onVesselWasModified.Add(OnComposition);
            GameEvents.onPartDie.Add(OnPartChanged);
            GameEvents.onPartUndock.Add(OnPartChanged);
            GameEvents.onPartCouple.Add(OnCouple);
            GameEvents.onPartActionUIDismiss.Add(OnDismiss);
            GameEvents.onTimeWarpRateChanged.Add(OnWarp);
        }
        private void OnDestroy()
        {
            StationExperimentAnimationPreparation.Clear();
            StationPowerPolicy.Clear(); StationStockPower.Clear(); StationLaboratoryPower.Clear();
            GameEvents.onVesselSituationChange.Remove(OnSituation);
            GameEvents.onVesselSOIChanged.Remove(OnBody);
            GameEvents.onGameStateLoad.Remove(OnLoad);
            GameEvents.onVesselWasModified.Remove(OnComposition);
            GameEvents.onPartDie.Remove(OnPartChanged);
            GameEvents.onPartUndock.Remove(OnPartChanged);
            GameEvents.onPartCouple.Remove(OnCouple);
            GameEvents.onPartActionUIDismiss.Remove(OnDismiss);
            GameEvents.onTimeWarpRateChanged.Remove(OnWarp);
        }
        // Stock EvtDelegate reads callback.Target.GetType() when subscribing.
        // Keep instance handlers for both registration and removal.
        private void OnDismiss(Part part) => StationExperimentAnimationPreparation.Dismiss(part);
        private void OnWarp()
        {
            StationExperimentAnimationPreparation.Clear();
            double now = Planetarium.GetUniversalTime();
            // The event is emitted after the rate changes. Close the old mode's
            // interval first, then Observe records the new mode for subsequent work.
            foreach (Vessel vessel in FlightGlobals.Vessels)
                if (vessel != null && vessel.loaded)
                { StationExperimentRuntime.Tick(vessel, now, !wasHighWarp || StationDbsIntegration.Active); StationLaboratoryPower.OnWarp(vessel); }
            wasHighWarp = StationPowerPolicy.HighWarp;
        }
        private void OnSituation(GameEvents.HostedFromToAction<Vessel, Vessel.Situations> change) =>
            StationSurfaceContinuity.Invalidate(change.host);
        private void OnBody(GameEvents.HostedFromToAction<Vessel, CelestialBody> change) =>
            StationSurfaceContinuity.Invalidate(change.host);
        private void OnComposition(Vessel vessel)
        { StationExperimentAnimationPreparation.Invalidate(vessel); StationEquipmentContinuity.Invalidate(vessel); StationStockPower.Forget(vessel); }
        private void OnPartChanged(Part? part) { if (part?.vessel != null) OnComposition(part.vessel); }
        private void OnCouple(GameEvents.FromToAction<Part, Part> change)
        {
            OnPartChanged(change.from);
            if (!ReferenceEquals(change.from?.vessel, change.to?.vessel)) OnPartChanged(change.to);
        }
        private void OnLoad(ConfigNode node) { StationExperimentAnimationPreparation.Clear(); StationSurfaceContinuity.Clear(); StationEquipmentContinuity.Clear(); StationPowerPolicy.Clear(); StationStockPower.Clear(); StationLaboratoryPower.Clear(); }

        private void Update() => StationExperimentAnimationPreparation.Update();

        private void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready) return;
            double now = Planetarium.GetUniversalTime();
            foreach (Vessel vessel in FlightGlobals.Vessels)
                if (vessel != null && vessel.loaded) StationExperimentRuntime.Tick(vessel, now);
            StationExperimentBackgroundDriver.Update();
        }
    }
}

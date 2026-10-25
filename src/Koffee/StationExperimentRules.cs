using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal static class StationLabRules
    {
        internal static int SlotCapacity(int crewCapacity) => Math.Max(0, crewCapacity);

    }

    internal enum StationExperimentPauseReason
    {
        None,
        NoManager,
        NoEngineerAssigned,
        EngineerAbsentOrInvalid,
        EngineerNeedsKoffee,
        NotStarted,
        ManuallyPaused,
        NoScientistAssigned,
        ScientistAbsent,
        ScientistInvalid,
        AssignedLaboratoryUnresolved,
        AssignedLaboratoryAbsent,
        AssignedLaboratorySeparated,
        AssignedLaboratoryIncompatible,
        ScientistNotInAssignedLab,
        ScientistNeedsKoffee,
        NoCompatibleLaboratory,
        NoLaboratorySlot,
        WrongBody,
        WrongSituation,
        WrongAtmosphere,
        EnvironmentConfigurationInvalid,
        ExecutionContextChanged,
        SurfaceContextChanged,
        SurfaceContextUnverified,
        WorkComplete,
        ResultAvailable,
        ResultDelivered,
        IncompatibleState,
        ResearchContextUnverified,
        TeamNeedsKoffee,
        MissingEquipment,
        RequiredEquipmentUnresolved,
        NoSolidSurface,
        InitialCaptureUnavailable,
        NoElectricity
    }

    internal static class StationExecutionRules
    {
        internal static StationExperimentPauseReason PrimaryReason(
            bool compatibleState, ExperimentContainerStatus status, bool complete,
            bool hasAssignment, bool scientistPresent, bool scientistValid,
            bool hasLaboratoryBinding, bool laboratoryExists, bool sameVessel,
            bool laboratoryCompatible, bool scientistInAssignedLaboratory,
            bool scientistKaffeinatedOrRenewable, bool hasSlot, bool validBody,
            bool validSituation)
        {
            if (!compatibleState) return StationExperimentPauseReason.IncompatibleState;
            if (status == ExperimentContainerStatus.Available)
                return hasAssignment ? StationExperimentPauseReason.NotStarted :
                    StationExperimentPauseReason.NoScientistAssigned;
            if (status == ExperimentContainerStatus.Finalized)
                return StationExperimentPauseReason.ResultAvailable;
            if (status == ExperimentContainerStatus.Spent)
                return StationExperimentPauseReason.ResultDelivered;
            if (status != ExperimentContainerStatus.Running)
                return StationExperimentPauseReason.IncompatibleState;
            if (complete) return StationExperimentPauseReason.WorkComplete;
            if (!hasAssignment) return StationExperimentPauseReason.NoScientistAssigned;
            if (!hasLaboratoryBinding)
                return StationExperimentPauseReason.AssignedLaboratoryUnresolved;
            if (!laboratoryExists)
                return StationExperimentPauseReason.AssignedLaboratoryAbsent;
            if (!sameVessel)
                return StationExperimentPauseReason.AssignedLaboratorySeparated;
            if (!laboratoryCompatible)
                return StationExperimentPauseReason.AssignedLaboratoryIncompatible;
            if (!scientistPresent) return StationExperimentPauseReason.ScientistAbsent;
            if (!scientistValid) return StationExperimentPauseReason.ScientistInvalid;
            if (!scientistInAssignedLaboratory)
                return StationExperimentPauseReason.ScientistNotInAssignedLab;
            if (!scientistKaffeinatedOrRenewable)
                return StationExperimentPauseReason.ScientistNeedsKoffee;
            if (!hasSlot) return StationExperimentPauseReason.NoLaboratorySlot;
            if (!validBody) return StationExperimentPauseReason.WrongBody;
            if (!validSituation) return StationExperimentPauseReason.WrongSituation;
            return StationExperimentPauseReason.None;
        }
    }

    internal static class StationPersonnelRules
    {
        internal static bool Valid(string? name, string? trait, int level,
            string requiredTrait, int minimumLevel) => !string.IsNullOrEmpty(name) &&
            string.Equals(trait, requiredTrait, StringComparison.Ordinal) &&
            level >= minimumLevel;

        internal static bool Selectable(string? name, string? trait, int level,
            string requiredTrait, int minimumLevel, bool seatedInCompatibleLaboratory) =>
            seatedInCompatibleLaboratory && Valid(name, trait, level, requiredTrait, minimumLevel);
    }

    internal static class StationSituationRules
    {
        internal static bool Allowed(uint mask, string situation)
        {
            uint bit = situation == "SrfLanded" ? 1u : situation == "SrfSplashed" ? 2u :
                situation == "FlyingLow" ? 4u : situation == "FlyingHigh" ? 8u :
                situation == "InSpaceLow" ? 16u : situation == "InSpaceHigh" ? 32u : 0u;
            return bit != 0 && (mask & bit) != 0;
        }
    }

    internal readonly struct StationSlotRequest
    {
        internal readonly Guid ContainerId;
        internal readonly StationLabCapability Capability;
        internal readonly uint LaboratoryId;
        internal readonly int Slot;
        internal StationSlotRequest(Guid containerId, StationLabCapability capability,
            uint laboratoryId, int slot)
        {
            ContainerId = containerId;
            Capability = capability;
            LaboratoryId = laboratoryId;
            Slot = slot;
        }
    }

    internal sealed class StationSlotSource
    {
        internal uint LaboratoryId { get; }
        internal int Capacity { get; }
        internal IReadOnlyCollection<StationLabCapability> Capabilities { get; }

        internal StationSlotSource(uint laboratoryId, int capacity,
            IEnumerable<StationLabCapability> capabilities)
        {
            LaboratoryId = laboratoryId;
            Capacity = Math.Max(0, capacity);
            Capabilities = capabilities.Distinct().ToArray();
        }
    }

    internal readonly struct StationSlotAllocation
    {
        internal readonly uint LaboratoryId;
        internal readonly int Slot;
        internal StationSlotAllocation(uint laboratoryId, int slot)
        {
            LaboratoryId = laboratoryId;
            Slot = slot;
        }
    }

    internal static class StationSlotAllocator
    {
        internal static IReadOnlyDictionary<Guid, StationSlotAllocation> Allocate(
            IEnumerable<StationSlotRequest> requests, IEnumerable<StationSlotSource> sources)
        {
            var free = sources.OrderBy(source => source.LaboratoryId)
                .SelectMany(source => Enumerable.Range(0, source.Capacity)
                    .Select(slot => new { Source = source, Slot = slot })).ToList();
            var result = new Dictionary<Guid, StationSlotAllocation>();
            foreach (StationSlotRequest request in requests)
            {
                if (result.ContainsKey(request.ContainerId)) continue;
                int index = free.FindIndex(candidate =>
                    candidate.Source.LaboratoryId == request.LaboratoryId &&
                    candidate.Slot == request.Slot &&
                    candidate.Source.Capabilities.Contains(request.Capability));
                if (index < 0) continue;
                var selected = free[index];
                free.RemoveAt(index);
                result.Add(request.ContainerId, new StationSlotAllocation(
                    selected.Source.LaboratoryId, selected.Slot));
            }
            return result;
        }
    }
}

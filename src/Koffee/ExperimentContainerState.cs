using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal enum ExperimentContainerStatus
    {
        Available,
        Running,
        Finalized,
        Spent
    }

    internal sealed class ExperimentContainerState
    {
        internal const int CurrentSchemaVersion = 5;
        internal int SchemaVersion { get; }
        internal Guid ContainerId { get; }
        internal string ExperimentId { get; }
        internal ExperimentContainerStatus Status { get; }
        internal long Revision { get; }
        internal Guid? RunId { get; }
        internal string AssignedKerbalName { get; }
        internal string AssignedKerbalDisplayName { get; }
        internal uint AssignedLaboratoryPersistentId { get; }
        internal int AssignedLaboratorySlot { get; }
        internal double? StartUT { get; }
        internal double AccumulatedUT { get; }
        internal ScienceCategory? ResearchCategory { get; }
        internal double ResearchPointsRequired { get; }
        internal double AccumulatedResearchPoints { get; }
        internal int ResearchAffinity { get; }
        internal int CheckpointLevel { get; }
        internal double ResearchPointsPerHour => StationResearchMath.RatePerHour(ResearchAffinity, CheckpointLevel);
        internal double? LastCheckpointUT { get; }
        internal bool CheckpointVerified { get; }
        internal string CheckpointBody { get; }
        internal string CheckpointSituation { get; }
        internal string ResultSubjectId { get; }
        internal string ResultBody { get; }
        internal string ResultSituation { get; }
        internal string ResultBiome { get; }
        internal string ResultBiomeDisplay { get; }
        internal double? FinalizedUT { get; }
        internal Guid? ExtractionReceiptId { get; }
        internal string ExtractionKind { get; }
        internal double? ExtractionUT { get; }
        internal StationTemperatureReading? TemperatureReading { get; }
        internal StationPressureReading? PressureReading { get; }
        internal StationSurfaceCapture? SurfaceCapture { get; }
        internal StationSolarCapture? SolarCapture { get; }
        internal IReadOnlyList<TelescopeCapture> TelescopeObservations { get; }
        // Only the first acquisition is the input to this single-image science run.
        internal TelescopeCapture? TelescopeCapture => TelescopeObservations.FirstOrDefault();

        internal ExperimentContainerState RecordTelescopeCapture(TelescopeCapture capture)
        {
            if ((TelescopeCapture == null && Status != ExperimentContainerStatus.Available) ||
                TelescopeObservations.Any(c => (c.Pair != null) != (capture.Pair != null) ||
                    (capture.Pair != null && c.Target == capture.Target) || c.ObservationId == capture.ObservationId ||
                    (c.UT == capture.UT && c.InstrumentId == capture.InstrumentId && c.VesselId == capture.VesselId)) ||
                TelescopeObservations.Any(c => c.UT > capture.UT))
                throw new InvalidOperationException("Duplicate, reversed or unbound telescope acquisition.");
            return Copy(Revision + 1, telescopeObservations: TelescopeObservations.Concat(new[] { capture }));
        }

        internal ExperimentContainerState DiscardTelescopeCapture()
        {
            if (Status != ExperimentContainerStatus.Available)
                throw new InvalidOperationException("Research has already started.");
            return new ExperimentContainerState(CurrentSchemaVersion, ContainerId, ExperimentId,
                Status, Revision + 1, assignedKerbalName: AssignedKerbalName,
                assignedKerbalDisplayName: AssignedKerbalDisplayName, assignedLaboratoryPersistentId: AssignedLaboratoryPersistentId);
        }

        internal string SurfaceBody { get; }
        internal string SurfaceBiome { get; }
        internal bool SurfaceHasBiome { get; }
        internal bool HasSurfaceBinding => !string.IsNullOrEmpty(SurfaceBody);
        internal string ContextBody { get; }
        internal string ContextSituation { get; }
        internal bool HasContextBinding => HasSurfaceBinding || !string.IsNullOrEmpty(ContextBody);
        internal string BoundBody => HasSurfaceBinding ? SurfaceBody : ContextBody;
        internal string BoundSituation => HasSurfaceBinding ? "SrfLanded" : ContextSituation;
        internal static bool CanBindSituation(string situation) => situation == "InSpaceLow" ||
            situation == "InSpaceHigh" || situation == "FlyingLow" || situation == "FlyingHigh";

        internal ExperimentContainerState BindSituation(string body, string situation)
        {
            if (Status != ExperimentContainerStatus.Running || HasContextBinding ||
                !CanBindSituation(situation) || string.IsNullOrWhiteSpace(body) ||
                body != CheckpointBody || situation != CheckpointSituation)
                throw new InvalidOperationException("Invalid initial scientific context binding.");
            return Copy(Revision + 1, contextBody: body, contextSituation: situation);
        }

        internal ExperimentContainerState BindSurface(string body, string biome, bool hasBiome)
        {
            if (Status != ExperimentContainerStatus.Running || HasContextBinding || CheckpointSituation != "SrfLanded" ||
                string.IsNullOrWhiteSpace(body) || body != CheckpointBody || hasBiome != !string.IsNullOrWhiteSpace(biome))
                throw new InvalidOperationException("Invalid initial surface binding.");
            return Copy(Revision + 1, surfaceBody: body, surfaceBiome: biome, surfaceHasBiome: hasBiome);
        }

        internal ExperimentContainerState RecordTemperature(StationTemperatureReading reading)
        {
            if (Status != ExperimentContainerStatus.Running || TemperatureReading != null || PressureReading != null || SurfaceCapture != null || SolarCapture != null || reading.UT != StartUT)
                throw new InvalidOperationException("Temperature reading must be captured once at run start.");
            return Copy(Revision + 1, temperatureReading: reading);
        }

        internal ExperimentContainerState RecordPressure(StationPressureReading reading)
        {
            if (Status != ExperimentContainerStatus.Running || PressureReading != null || TemperatureReading != null || SurfaceCapture != null || SolarCapture != null || reading.UT != StartUT)
                throw new InvalidOperationException("Pressure reading must be captured once at run start.");
            return Copy(Revision + 1, pressureReading: reading);
        }

        internal ExperimentContainerState RecordSurfaceCapture(StationSurfaceCapture capture)
        {
            if (Status != ExperimentContainerStatus.Running || SurfaceCapture != null || TemperatureReading != null ||
                PressureReading != null || SolarCapture != null || capture.UT != StartUT || capture.Body != CheckpointBody ||
                CheckpointSituation != "InSpaceLow")
                throw new InvalidOperationException("Surface data must be captured once at a valid run start.");
            return Copy(Revision + 1, surfaceCapture: capture);
        }

        internal ExperimentContainerState RecordSolarCapture(StationSolarCapture capture)
        {
            if (Status != ExperimentContainerStatus.Running || SolarCapture != null || TemperatureReading != null ||
                PressureReading != null || SurfaceCapture != null || TelescopeCapture != null ||
                capture.RunId != RunId || capture.UT != StartUT || capture.Body != CheckpointBody ||
                capture.Situation != CheckpointSituation || HasContextBinding)
                throw new InvalidOperationException("Solar data must be captured once at run start.");
            return Copy(Revision + 1, solarCapture: capture);
        }

        internal bool HasAssignment => !string.IsNullOrEmpty(AssignedKerbalName);
        internal bool HasAssignedLaboratory => AssignedLaboratoryPersistentId != 0;
        internal bool HasSlotBinding => HasAssignedLaboratory && AssignedLaboratorySlot >= 0;

        internal ExperimentContainerState(int schemaVersion, Guid containerId, string experimentId,
            ExperimentContainerStatus status, long revision, Guid? runId = null,
            string? assignedKerbalName = null, string? assignedKerbalDisplayName = null,
            uint assignedLaboratoryPersistentId = 0, int assignedLaboratorySlot = -1,
            double? startUT = null, double accumulatedUT = 0, double? lastCheckpointUT = null,
            bool checkpointVerified = false, string? checkpointBody = null,
            string? checkpointSituation = null, string? resultSubjectId = null,
            string? resultBody = null, string? resultSituation = null,
            string? resultBiome = null, string? resultBiomeDisplay = null,
            double? finalizedUT = null, Guid? extractionReceiptId = null,
            string? extractionKind = null, double? extractionUT = null,
            ScienceCategory? researchCategory = null, double researchPointsRequired = 0,
            double accumulatedResearchPoints = 0, int researchAffinity = 0, int checkpointLevel = 0,
            string? surfaceBody = null, string? surfaceBiome = null, bool surfaceHasBiome = false,
            string? contextBody = null, string? contextSituation = null, StationTemperatureReading? temperatureReading = null,
            StationPressureReading? pressureReading = null, StationSurfaceCapture? surfaceCapture = null,
            IEnumerable<TelescopeCapture>? telescopeObservations = null, StationSolarCapture? solarCapture = null)
        {
            TemperatureReading = temperatureReading;
            PressureReading = pressureReading;
            SurfaceCapture = surfaceCapture;
            SolarCapture = solarCapture;
            TelescopeObservations = new ReadOnlyCollection<TelescopeCapture>((telescopeObservations ?? Array.Empty<TelescopeCapture>()).ToArray());
            ContextBody = contextBody ?? "";
            ContextSituation = contextSituation ?? "";
            SurfaceBody = surfaceBody ?? "";
            SurfaceBiome = surfaceBiome ?? "";
            SurfaceHasBiome = surfaceHasBiome;
            SchemaVersion = schemaVersion;
            ContainerId = containerId;
            ExperimentId = experimentId;
            Status = status;
            Revision = revision;
            RunId = runId;
            AssignedKerbalName = assignedKerbalName ?? "";
            AssignedKerbalDisplayName = assignedKerbalDisplayName ?? "";
            AssignedLaboratoryPersistentId = assignedLaboratoryPersistentId;
            AssignedLaboratorySlot = assignedLaboratorySlot;
            StartUT = startUT;
            AccumulatedUT = accumulatedUT;
            ResearchCategory = researchCategory;
            ResearchPointsRequired = researchPointsRequired;
            AccumulatedResearchPoints = accumulatedResearchPoints;
            ResearchAffinity = researchAffinity;
            CheckpointLevel = checkpointLevel;
            LastCheckpointUT = lastCheckpointUT;
            CheckpointVerified = checkpointVerified;
            CheckpointBody = checkpointBody ?? "";
            CheckpointSituation = checkpointSituation ?? "";
            ResultSubjectId = resultSubjectId ?? "";
            ResultBody = resultBody ?? "";
            ResultSituation = resultSituation ?? "";
            ResultBiome = resultBiome ?? "";
            ResultBiomeDisplay = resultBiomeDisplay ?? "";
            FinalizedUT = finalizedUT;
            ExtractionReceiptId = extractionReceiptId;
            ExtractionKind = extractionKind ?? "";
            ExtractionUT = extractionUT;
        }

        internal static ExperimentContainerState CreateAvailable(string experimentId, Guid containerId)
        {
            if (string.IsNullOrWhiteSpace(experimentId)) throw new ArgumentException(nameof(experimentId));
            if (containerId == Guid.Empty) throw new ArgumentException(nameof(containerId));
            return new ExperimentContainerState(CurrentSchemaVersion, containerId, experimentId,
                ExperimentContainerStatus.Available, 1);
        }

        internal ExperimentContainerState Assign(string name, string displayName,
            uint laboratoryPersistentId)
        {
            if (Status != ExperimentContainerStatus.Available)
                throw new InvalidOperationException("The scientist cannot be changed after a run starts.");
            if (string.IsNullOrEmpty(name)) throw new ArgumentException(nameof(name));
            if (laboratoryPersistentId == 0) throw new ArgumentException(nameof(laboratoryPersistentId));
            if (string.Equals(AssignedKerbalName, name, StringComparison.Ordinal) &&
                string.Equals(AssignedKerbalDisplayName, displayName ?? "", StringComparison.Ordinal) &&
                AssignedLaboratoryPersistentId == laboratoryPersistentId) return this;
            return Copy(Revision + 1, assignedName: name,
                assignedDisplayName: displayName ?? name,
                assignedLaboratoryPersistentId: laboratoryPersistentId,
                assignedLaboratorySlot: -1);
        }

        internal ExperimentContainerState ClearAssignment()
        {
            if (Status != ExperimentContainerStatus.Available)
                throw new InvalidOperationException("The scientist cannot be changed after a run starts.");
            return HasAssignment ? Copy(Revision + 1, assignedName: "",
                assignedDisplayName: "", assignedLaboratoryPersistentId: 0,
                assignedLaboratorySlot: -1) : this;
        }

        internal ExperimentContainerState Start(Guid runId, double startUT,
            string body, string situation, int laboratorySlot, ScienceCategory category,
            double required, int affinity, int level)
        {
            if (Status != ExperimentContainerStatus.Available || !HasAssignment ||
                !HasAssignedLaboratory || laboratorySlot < 0)
                throw new InvalidOperationException("The container is not ready to start.");
            if (runId == Guid.Empty) throw new ArgumentException(nameof(runId));
            if (!Finite(startUT) || startUT < 0) throw new ArgumentOutOfRangeException(nameof(startUT));
            if (!ScienceCategories.IsValid(category)) throw new ArgumentOutOfRangeException(nameof(category));
            StationResearchMath.RemainingSeconds(required, 0, StationResearchMath.RatePerHour(affinity, level));
            return new ExperimentContainerState(CurrentSchemaVersion, ContainerId, ExperimentId,
                ExperimentContainerStatus.Running, Revision + 1, runId,
                AssignedKerbalName, AssignedKerbalDisplayName,
                AssignedLaboratoryPersistentId, laboratorySlot, startUT, 0, startUT, true,
                body, situation, researchCategory: category, researchPointsRequired: required,
                researchAffinity: affinity, checkpointLevel: level, telescopeObservations: TelescopeObservations);
        }

        internal ExperimentContainerState Checkpoint(double now, double creditedUT, bool verified,
            string body, string situation)
        {
            // Convenience for a certified constant-level interval. Varying rates
            // use CheckpointResearch with separately calculated points/seconds.
            if (Status != ExperimentContainerStatus.Running || !Finite(now) || now < 0 ||
                !Finite(creditedUT) || creditedUT < 0) return this;
            double interval = LastCheckpointUT.HasValue ? Math.Max(0, now - LastCheckpointUT.Value) : 0;
            double seconds = Math.Min(interval, Math.Min(creditedUT,
                StationResearchMath.RemainingSeconds(ResearchPointsRequired, AccumulatedResearchPoints, ResearchPointsPerHour)));
            double points = StationResearchMath.Credit(ResearchPointsRequired, AccumulatedResearchPoints,
                ResearchPointsPerHour, seconds) - AccumulatedResearchPoints;
            return CheckpointResearch(now, points, seconds, verified, body, situation, CheckpointLevel);
        }

        // The shared interval processor supplies certified points and covered seconds
        // separately; the endpoint level never reprices an earlier interval.
        internal ExperimentContainerState CheckpointResearch(double now, double points, double seconds,
            bool verified, string body, string situation, int level)
        {
            if (Status != ExperimentContainerStatus.Running || !Finite(now) || now < 0 ||
                !Finite(points) || points < 0 || !Finite(seconds) || seconds < 0 || level < 0 || level > 5) return this;
            if (LastCheckpointUT.HasValue && now < LastCheckpointUT.Value)
                return Copy(Revision + 1, checkpointUT: now, checkpointVerified: false,
                    checkpointBody: body ?? "", checkpointSituation: situation ?? "");
            double accumulated = AccumulatedUT;
            double research = AccumulatedResearchPoints;
            double interval = LastCheckpointUT.HasValue ? Math.Max(0, now - LastCheckpointUT.Value) : 0;
            if ((seconds == 0 && points != 0) || seconds > interval + 1e-8 || points > seconds * StationResearchMath.MaximumRatePerHour / 3600 + 1e-8) return this;
            if (CheckpointVerified && interval > 0)
            {
                accumulated += Math.Min(interval, seconds);
                research = Math.Min(ResearchPointsRequired, research + points);
                if (ResearchPointsRequired - research < 1e-9) research = ResearchPointsRequired;
            }
            if (LastCheckpointUT == now && CheckpointVerified == verified &&
                accumulated == AccumulatedUT && research == AccumulatedResearchPoints && level == CheckpointLevel && string.Equals(CheckpointBody, body ?? "",
                    StringComparison.Ordinal) && string.Equals(CheckpointSituation,
                    situation ?? "", StringComparison.Ordinal)) return this;
            return Copy(Revision + 1, accumulatedUT: accumulated, accumulatedResearchPoints: research, checkpointLevel: level,
                checkpointUT: now, checkpointVerified: verified,
                checkpointBody: body ?? "", checkpointSituation: situation ?? "");
        }

        internal ExperimentContainerState RebaseCheckpoint(double now, string body, string situation)
        {
            if (Status != ExperimentContainerStatus.Running || !Finite(now)) return this;
            return Copy(Revision + 1, checkpointUT: now,
                checkpointVerified: false, checkpointBody: body ?? "",
                checkpointSituation: situation ?? "");
        }

        internal bool WorkComplete() => Finite(ResearchPointsRequired) && ResearchPointsRequired > 0 &&
            AccumulatedResearchPoints >= ResearchPointsRequired;

        internal ExperimentContainerState FinalizeRun(string subjectId, string body,
            string situation, string biome, string biomeDisplay, double finalizedUT)
        {
            if (Status != ExperimentContainerStatus.Running || !RunId.HasValue || !WorkComplete())
                throw new InvalidOperationException("Only a running execution can be finalized.");
            if (string.IsNullOrWhiteSpace(subjectId) || string.IsNullOrWhiteSpace(body) ||
                string.IsNullOrWhiteSpace(situation) || !Finite(finalizedUT) || finalizedUT < 0)
                throw new ArgumentException("The certified science context is incomplete.");
            return new ExperimentContainerState(CurrentSchemaVersion, ContainerId, ExperimentId,
                ExperimentContainerStatus.Finalized, Revision + 1, RunId,
                AssignedKerbalName, AssignedKerbalDisplayName,
                AssignedLaboratoryPersistentId, AssignedLaboratorySlot,
                StartUT, AccumulatedUT,
                LastCheckpointUT, false, CheckpointBody, CheckpointSituation, subjectId,
                body, situation, biome, biomeDisplay, finalizedUT,
                researchCategory: ResearchCategory, researchPointsRequired: ResearchPointsRequired,
                accumulatedResearchPoints: AccumulatedResearchPoints, researchAffinity: ResearchAffinity, checkpointLevel: CheckpointLevel,
                surfaceBody: SurfaceBody, surfaceBiome: SurfaceBiome, surfaceHasBiome: SurfaceHasBiome,
                contextBody: ContextBody, contextSituation: ContextSituation, temperatureReading: TemperatureReading, pressureReading: PressureReading, surfaceCapture: SurfaceCapture, telescopeObservations: TelescopeObservations, solarCapture: SolarCapture);
        }

        internal ExperimentContainerState MarkSpent(Guid receiptId, string extractionKind,
            double extractionUT)
        {
            if (Status != ExperimentContainerStatus.Finalized)
                throw new InvalidOperationException("Only a finalized execution can be extracted.");
            if (receiptId == Guid.Empty || string.IsNullOrWhiteSpace(extractionKind) ||
                !Finite(extractionUT) || extractionUT < 0)
                throw new ArgumentException("Invalid extraction receipt.");
            return new ExperimentContainerState(CurrentSchemaVersion, ContainerId, ExperimentId,
                ExperimentContainerStatus.Spent, Revision + 1, RunId,
                AssignedKerbalName, AssignedKerbalDisplayName,
                AssignedLaboratoryPersistentId, AssignedLaboratorySlot,
                StartUT, AccumulatedUT,
                LastCheckpointUT, false, CheckpointBody, CheckpointSituation, ResultSubjectId,
                ResultBody, ResultSituation, ResultBiome, ResultBiomeDisplay, FinalizedUT,
                receiptId, extractionKind, extractionUT, ResearchCategory, ResearchPointsRequired,
                AccumulatedResearchPoints, ResearchAffinity, CheckpointLevel, SurfaceBody, SurfaceBiome, SurfaceHasBiome, ContextBody, ContextSituation, TemperatureReading, PressureReading, SurfaceCapture, telescopeObservations: TelescopeObservations, solarCapture: SolarCapture);
        }

        internal ExperimentContainerState ResetResult()
        {
            if (Status != ExperimentContainerStatus.Finalized)
                throw new InvalidOperationException("Only a finalized result can be reset.");
            return new ExperimentContainerState(CurrentSchemaVersion, ContainerId, ExperimentId,
                ExperimentContainerStatus.Available, Revision + 1, telescopeObservations: TelescopeObservations);
        }

        private ExperimentContainerState Copy(long revision, string? assignedName = null,
            string? assignedDisplayName = null, double? accumulatedUT = null,
            uint? assignedLaboratoryPersistentId = null,
            int? assignedLaboratorySlot = null,
            double? accumulatedResearchPoints = null, int? checkpointLevel = null,
            double? checkpointUT = null, bool? checkpointVerified = null,
            string? checkpointBody = null, string? checkpointSituation = null,
            string? surfaceBody = null, string? surfaceBiome = null, bool? surfaceHasBiome = null,
            string? contextBody = null, string? contextSituation = null, StationTemperatureReading? temperatureReading = null,
            StationPressureReading? pressureReading = null, StationSurfaceCapture? surfaceCapture = null,
            IEnumerable<TelescopeCapture>? telescopeObservations = null, StationSolarCapture? solarCapture = null) =>
            new ExperimentContainerState(CurrentSchemaVersion, ContainerId, ExperimentId, Status,
                revision, RunId, assignedName ?? AssignedKerbalName,
                assignedDisplayName ?? AssignedKerbalDisplayName,
                assignedLaboratoryPersistentId ?? AssignedLaboratoryPersistentId,
                assignedLaboratorySlot ?? AssignedLaboratorySlot, StartUT,
                accumulatedUT ?? AccumulatedUT, checkpointUT ?? LastCheckpointUT,
                checkpointVerified ?? CheckpointVerified, checkpointBody ?? CheckpointBody,
                checkpointSituation ?? CheckpointSituation, ResultSubjectId, ResultBody,
                ResultSituation, ResultBiome, ResultBiomeDisplay, FinalizedUT,
                ExtractionReceiptId, ExtractionKind, ExtractionUT, ResearchCategory, ResearchPointsRequired,
                accumulatedResearchPoints ?? AccumulatedResearchPoints, ResearchAffinity, checkpointLevel ?? CheckpointLevel,
                surfaceBody ?? SurfaceBody, surfaceBiome ?? SurfaceBiome, surfaceHasBiome ?? SurfaceHasBiome,
                contextBody ?? ContextBody, contextSituation ?? ContextSituation, temperatureReading ?? TemperatureReading, pressureReading ?? PressureReading, surfaceCapture ?? SurfaceCapture, telescopeObservations ?? TelescopeObservations, solarCapture ?? SolarCapture);

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    internal static class ExperimentContainerRegistration
    {
        internal static ExperimentContainerState Register(ExperimentContainerState? existing,
            string experimentId, Func<Guid> createId)
        {
            if (existing != null) return existing;
            Guid id = createId();
            if (id == Guid.Empty) throw new InvalidOperationException("Container ID generator returned an empty ID.");
            return ExperimentContainerState.CreateAvailable(experimentId, id);
        }
    }
}

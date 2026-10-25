using System;
using System.Globalization;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal enum ExperimentContainerReadResult
    {
        Missing,
        Valid,
        Invalid
    }

    internal static class ExperimentContainerStateCodec
    {
        private static readonly string[] PressureKeys =
        { "pressureReadingVersion", "pressureReadingPart", "pressureReadingPartId", "pressureReadingUT", "pressureReadingKpa" };
        private static readonly string[] TemperatureKeys =
        { "temperatureReadingVersion", "temperatureReadingPart", "temperatureReadingPartId", "temperatureReadingUT", "temperatureReadingKelvin" };
        private static readonly string[] ContextKeys =
        { "contextBindingVersion", "contextBody", "contextSituation" };
        private static readonly string[] SurfaceKeys =
        { "surfaceBindingVersion", "surfaceBody", "surfaceBiome", "surfaceHasBiome" };
        private static readonly string[] BaseKeys =
        {
            KoffeeIdentifiers.SchemaVersionValue,
            KoffeeIdentifiers.ContainerIdValue,
            KoffeeIdentifiers.ExperimentIdValue,
            KoffeeIdentifiers.ContainerStatusValue,
            KoffeeIdentifiers.RevisionValue
        };

        private static readonly string[] RuntimeKeys =
        {
            KoffeeIdentifiers.RunIdValue,
            KoffeeIdentifiers.AssignedKerbalNameValue,
            KoffeeIdentifiers.AssignedKerbalDisplayNameValue,
            KoffeeIdentifiers.RunStartUtValue,
            KoffeeIdentifiers.EffectiveWorkUtValue,
            KoffeeIdentifiers.LastCheckpointUtValue,
            KoffeeIdentifiers.CheckpointVerifiedValue,
            KoffeeIdentifiers.CheckpointBodyValue,
            KoffeeIdentifiers.CheckpointSituationValue
        };

        private static readonly string[] ResearchKeys =
        {
            KoffeeIdentifiers.ResearchCategoryValue,
            KoffeeIdentifiers.ResearchPointsRequiredValue,
            KoffeeIdentifiers.AccumulatedResearchPointsValue,
            KoffeeIdentifiers.ResearchAffinityValue,
            KoffeeIdentifiers.CheckpointLevelValue,
            KoffeeIdentifiers.ResearchPointsPerHourValue
        };

        private static readonly string[] LaboratoryKeys =
        {
            KoffeeIdentifiers.AssignedLaboratoryPersistentIdValue,
            KoffeeIdentifiers.AssignedLaboratorySlotValue
        };

        private static readonly string[] ResultKeys =
        {
            KoffeeIdentifiers.ResultSubjectIdValue,
            KoffeeIdentifiers.ResultBodyValue,
            KoffeeIdentifiers.ResultSituationValue,
            KoffeeIdentifiers.ResultBiomeValue,
            KoffeeIdentifiers.ResultBiomeDisplayValue,
            KoffeeIdentifiers.FinalizedUtValue
        };

        private static readonly string[] ExtractionKeys =
        {
            KoffeeIdentifiers.ExtractionReceiptIdValue,
            KoffeeIdentifiers.ExtractionKindValue,
            KoffeeIdentifiers.ExtractionUtValue
        };

        internal static ExperimentContainerReadResult Read(ConfigNode node, string expectedExperimentId,
            out ExperimentContainerState? state, out string error)
        {
            state = null;
            error = "";
            bool hasPersistedState = node.GetNodes(StationSolarCaptureCodec.NodeName).Length != 0 || node.GetNodes(TelescopeCaptureCodec.NodeName).Length != 0 || node.GetNodes(StationSurfaceCaptureCodec.NodeName).Length != 0 || node.HasValue(KoffeeIdentifiers.SchemaVersionValue) ||
                node.HasValue(KoffeeIdentifiers.ContainerIdValue) ||
                node.HasValue(KoffeeIdentifiers.ContainerStatusValue) ||
                node.HasValue(KoffeeIdentifiers.RevisionValue) || node.HasValue(KoffeeIdentifiers.AccumulatedUtValue) ||
                RuntimeKeys.Concat(ResearchKeys).Concat(LaboratoryKeys).Concat(ResultKeys).Concat(ExtractionKeys).Concat(SurfaceKeys).Concat(ContextKeys).Concat(TemperatureKeys).Concat(PressureKeys).Any(node.HasValue);
            if (!hasPersistedState) return ExperimentContainerReadResult.Missing;
            foreach (string key in BaseKeys)
                if (node.GetValues(key).Length != 1)
                    return Invalid("Field '" + key + "' must occur exactly once.", out error);

            string schemaText = node.GetValue(KoffeeIdentifiers.SchemaVersionValue);
            if (!int.TryParse(schemaText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int schema) || schema != ExperimentContainerState.CurrentSchemaVersion)
                return Invalid("Unsupported container schemaVersion '" + schemaText + "'.", out error);

            string idText = node.GetValue(KoffeeIdentifiers.ContainerIdValue);
            if (!Guid.TryParseExact(idText, "N", out Guid id) || id == Guid.Empty)
                return Invalid("Invalid containerId '" + idText + "'.", out error);

            string experimentId = node.GetValue(KoffeeIdentifiers.ExperimentIdValue);
            if (string.IsNullOrWhiteSpace(experimentId) ||
                !string.Equals(experimentId, expectedExperimentId, StringComparison.Ordinal))
                return Invalid("Persisted experimentID '" + experimentId +
                    "' does not match fixed PART experimentID '" + expectedExperimentId + "'.", out error);

            string statusText = node.GetValue(KoffeeIdentifiers.ContainerStatusValue);
            if (!Enum.TryParse(statusText, false, out ExperimentContainerStatus status) ||
                !string.Equals(status.ToString(), statusText, StringComparison.Ordinal))
                return Invalid("Invalid container state '" + statusText + "'.", out error);

            string revisionText = node.GetValue(KoffeeIdentifiers.RevisionValue);
            if (!long.TryParse(revisionText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out long revision) || revision < 1)
                return Invalid("Invalid container revision '" + revisionText + "'.", out error);

            foreach (string key in RuntimeKeys)
                if (node.GetValues(key).Length > 1)
                    return Invalid("Field '" + key + "' may occur at most once.", out error);
            foreach (string key in ResearchKeys)
                if (node.GetValues(key).Length > 1)
                    return Invalid("Field '" + key + "' may occur at most once.", out error);
            if (node.HasValue(KoffeeIdentifiers.AccumulatedUtValue))
                return Invalid("Legacy accumulatedUT cannot be interpreted as researchPoints.", out error);
            foreach (string key in LaboratoryKeys)
                if (node.GetValues(key).Length > 1)
                    return Invalid("Field '" + key + "' may occur at most once.", out error);
            foreach (string key in ResultKeys.Concat(ExtractionKeys))
                if (node.GetValues(key).Length > 1)
                    return Invalid("Field '" + key + "' may occur at most once.", out error);

            string surfaceBody = "", surfaceBiome = "";
            bool surfaceHasBiome = false;
            if (SurfaceKeys.Any(node.HasValue))
            {
                if (status == ExperimentContainerStatus.Available || SurfaceKeys.Any(k => node.GetValues(k).Length != 1) ||
                    Value(node, "surfaceBindingVersion") != "1" || string.IsNullOrWhiteSpace(Value(node, "surfaceBody")) ||
                    !bool.TryParse(Value(node, "surfaceHasBiome"), out surfaceHasBiome))
                    return Invalid("Invalid or unsupported surface binding.", out error);
                surfaceBody = Value(node, "surfaceBody"); surfaceBiome = Value(node, "surfaceBiome");
                if (surfaceHasBiome != !string.IsNullOrWhiteSpace(surfaceBiome))
                    return Invalid("Surface biome identity does not match its map flag.", out error);
            }
            string contextBody = "", contextSituation = "";
            if (ContextKeys.Any(node.HasValue))
            {
                if (status == ExperimentContainerStatus.Available || !string.IsNullOrEmpty(surfaceBody) ||
                    ContextKeys.Any(k => node.GetValues(k).Length != 1) ||
                    Value(node, "contextBindingVersion") != "1" ||
                    string.IsNullOrWhiteSpace(Value(node, "contextBody")) ||
                    !ExperimentContainerState.CanBindSituation(Value(node, "contextSituation")))
                    return Invalid("Invalid or unsupported scientific context binding.", out error);
                contextBody = Value(node, "contextBody"); contextSituation = Value(node, "contextSituation");
            }
            if (!TelescopeCaptureCodec.Read(node, out var telescopeObservations))
                return Invalid("Invalid telescope observation archive.", out error);
            var telescopeCapture = telescopeObservations.FirstOrDefault();
            if (telescopeCapture != null && (TemperatureKeys.Concat(PressureKeys).Concat(SurfaceKeys).Any(node.HasValue) ||
                    node.GetNodes(StationSurfaceCaptureCodec.NodeName).Length != 0))
                return Invalid("Invalid or conflicting telescope acquisition.", out error);
            string assigned = Value(node, KoffeeIdentifiers.AssignedKerbalNameValue);
            string display = Value(node, KoffeeIdentifiers.AssignedKerbalDisplayNameValue);
            uint laboratoryId = 0;
            int laboratorySlot = -1;
            bool hasLaboratoryId = uint.TryParse(Value(node,
                KoffeeIdentifiers.AssignedLaboratoryPersistentIdValue), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out laboratoryId) && laboratoryId != 0;
            bool hasLaboratorySlot = int.TryParse(Value(node,
                KoffeeIdentifiers.AssignedLaboratorySlotValue), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out laboratorySlot) && laboratorySlot >= 0;
            if (status == ExperimentContainerStatus.Available)
            {
                if (HasRunFields(node))
                    return Invalid("Available container contains active run fields.", out error);
                if (schema == ExperimentContainerState.CurrentSchemaVersion &&
                    ((!string.IsNullOrEmpty(assigned) && !hasLaboratoryId) ||
                     (string.IsNullOrEmpty(assigned) && node.HasValue(
                         KoffeeIdentifiers.AssignedLaboratoryPersistentIdValue))))
                    return Invalid("Available assignment has no matching laboratory.", out error);
                if (node.HasValue(KoffeeIdentifiers.AssignedLaboratorySlotValue))
                    return Invalid("Available container contains a laboratory slot.", out error);
                state = new ExperimentContainerState(ExperimentContainerState.CurrentSchemaVersion,
                    id, experimentId, status, revision,
                    assignedKerbalName: assigned, assignedKerbalDisplayName: display,
                    assignedLaboratoryPersistentId: laboratoryId, telescopeObservations: telescopeObservations);
                return ExperimentContainerReadResult.Valid;
            }

            foreach (string key in RuntimeKeys)
                if (node.GetValues(key).Length != 1)
                    return Invalid("Running field '" + key + "' must occur exactly once.", out error);
            foreach (string key in ResearchKeys)
                if (node.GetValues(key).Length != 1)
                    return Invalid("Research field '" + key + "' must occur exactly once.", out error);
            if (!ScienceCategories.TryParse(Value(node, KoffeeIdentifiers.ResearchCategoryValue), out ScienceCategory category) ||
                !TryFinite(node, KoffeeIdentifiers.ResearchPointsRequiredValue, out double required) || required <= 0 ||
                !TryFinite(node, KoffeeIdentifiers.AccumulatedResearchPointsValue, out double points) || points < 0 || points > required ||
                !int.TryParse(Value(node, KoffeeIdentifiers.ResearchAffinityValue), NumberStyles.Integer, CultureInfo.InvariantCulture, out int affinity) || affinity < 1 || affinity > 10 ||
                !int.TryParse(Value(node, KoffeeIdentifiers.CheckpointLevelValue), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) || level < 0 || level > 5 ||
                !TryFinite(node, KoffeeIdentifiers.ResearchPointsPerHourValue, out double rate) || rate != StationResearchMath.RatePerHour(affinity, level))
                return Invalid("Invalid research budget, progress, category or worker rate.", out error);
            if (string.IsNullOrEmpty(assigned))
                return Invalid("Running container has no assigned scientist name.", out error);
            if (!hasLaboratoryId || !hasLaboratorySlot)
                return Invalid("Execution has no valid laboratory binding.", out error);
            if (!Guid.TryParseExact(Value(node, KoffeeIdentifiers.RunIdValue), "N", out Guid runId) ||
                runId == Guid.Empty)
                return Invalid("Invalid runId.", out error);
            if (!TryFinite(node, KoffeeIdentifiers.RunStartUtValue, out double startUT) || startUT < 0 ||
                !TryFinite(node, KoffeeIdentifiers.EffectiveWorkUtValue, out double accumulated) || accumulated < 0 ||
                !TryFinite(node, KoffeeIdentifiers.LastCheckpointUtValue, out double checkpointUT) || checkpointUT < 0)
                return Invalid("Invalid run timing fields.", out error);
            if (telescopeCapture != null && telescopeCapture.UT > startUT)
                return Invalid("Telescope acquisition is later than research start.", out error);
            StationTemperatureReading? temperature = null;
            if (TemperatureKeys.Any(node.HasValue))
            {
                if (TemperatureKeys.Any(k => node.GetValues(k).Length != 1) ||
                    Value(node, "temperatureReadingVersion") != "1" ||
                    string.IsNullOrWhiteSpace(Value(node, "temperatureReadingPart")) ||
                    !uint.TryParse(Value(node, "temperatureReadingPartId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint partId) ||
                    !TryFinite(node, "temperatureReadingUT", out double readingUT) || readingUT != startUT ||
                    !TryFinite(node, "temperatureReadingKelvin", out double kelvin) || kelvin <= 0)
                    return Invalid("Invalid or unsupported initial temperature reading.", out error);
                temperature = new StationTemperatureReading(Value(node, "temperatureReadingPart"), partId, readingUT, kelvin);
            }
            StationPressureReading? pressure = null;
            if (PressureKeys.Any(node.HasValue))
            {
                if (temperature != null || PressureKeys.Any(k => node.GetValues(k).Length != 1) ||
                    Value(node, "pressureReadingVersion") != "1" ||
                    string.IsNullOrWhiteSpace(Value(node, "pressureReadingPart")) ||
                    !uint.TryParse(Value(node, "pressureReadingPartId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint partId) ||
                    !TryFinite(node, "pressureReadingUT", out double readingUT) || readingUT != startUT ||
                    !TryFinite(node, "pressureReadingKpa", out double kpa) || kpa < 0)
                    return Invalid("Invalid or unsupported initial pressure reading.", out error);
                pressure = new StationPressureReading(Value(node, "pressureReadingPart"), partId, readingUT, kpa);
            }
            if (!StationSurfaceCaptureCodec.Read(node, startUT, contextBody, out var surfaceCapture) ||
                (surfaceCapture != null && (temperature != null || pressure != null || contextSituation != "InSpaceLow")))
                return Invalid("Invalid or unsupported initial surface capture.", out error);
            if (!StationSolarCaptureCodec.Read(node, runId, startUT, out var solarCapture) ||
                (solarCapture != null && (temperature != null || pressure != null || surfaceCapture != null || telescopeCapture != null ||
                    !string.IsNullOrEmpty(contextBody) || !string.IsNullOrEmpty(surfaceBody))) ||
                (experimentId == "solarRadiometry" && (solarCapture == null || solarCapture.RadiationPressure != null)) ||
                (experimentId == "solarRadiationPressure" && solarCapture?.RadiationPressure == null))
                return Invalid("Missing, incompatible or invalid initial solar capture.", out error);
            string verifiedText = Value(node, KoffeeIdentifiers.CheckpointVerifiedValue);
            if (!bool.TryParse(verifiedText, out bool verified) ||
                !string.Equals(verified.ToString(), verifiedText, StringComparison.OrdinalIgnoreCase))
                return Invalid("Invalid checkpointVerified value '" + verifiedText + "'.", out error);

            string checkpointBody = Value(node, KoffeeIdentifiers.CheckpointBodyValue);
            string checkpointSituation = Value(node, KoffeeIdentifiers.CheckpointSituationValue);
            if (status == ExperimentContainerStatus.Running)
            {
                if (ResultKeys.Concat(ExtractionKeys).Any(node.HasValue))
                    return Invalid("Running container contains completed-result fields.", out error);
                state = new ExperimentContainerState(ExperimentContainerState.CurrentSchemaVersion,
                    id, experimentId, status, revision, runId, assigned, display,
                    assignedLaboratoryPersistentId: laboratoryId,
                    assignedLaboratorySlot: laboratorySlot, startUT: startUT,
                    accumulatedUT: accumulated, lastCheckpointUT: checkpointUT,
                    checkpointVerified: verified, checkpointBody: checkpointBody,
                    checkpointSituation: checkpointSituation, researchCategory: category, researchPointsRequired: required,
                    accumulatedResearchPoints: points, researchAffinity: affinity, checkpointLevel: level,
                    surfaceBody: surfaceBody, surfaceBiome: surfaceBiome, surfaceHasBiome: surfaceHasBiome,
                    contextBody: contextBody, contextSituation: contextSituation, temperatureReading: temperature, pressureReading: pressure, surfaceCapture: surfaceCapture, telescopeObservations: telescopeObservations, solarCapture: solarCapture);
                return ExperimentContainerReadResult.Valid;
            }

            if (status != ExperimentContainerStatus.Finalized &&
                status != ExperimentContainerStatus.Spent)
                return Invalid("Unsupported container state '" + status + "'.", out error);
            foreach (string key in ResultKeys)
                if (node.GetValues(key).Length != 1)
                    return Invalid("Finalized field '" + key + "' must occur exactly once.", out error);
            if (points != required) return Invalid("Completed result has unfinished research work.", out error);
            string subjectId = Value(node, KoffeeIdentifiers.ResultSubjectIdValue);
            string resultBody = Value(node, KoffeeIdentifiers.ResultBodyValue);
            string resultSituation = Value(node, KoffeeIdentifiers.ResultSituationValue);
            if (string.IsNullOrWhiteSpace(subjectId) || string.IsNullOrWhiteSpace(resultBody) ||
                string.IsNullOrWhiteSpace(resultSituation) ||
                !TryFinite(node, KoffeeIdentifiers.FinalizedUtValue, out double finalizedUT) ||
                finalizedUT < 0)
                return Invalid("Invalid certified science context.", out error);
            if (!string.IsNullOrEmpty(surfaceBody) && (resultBody != surfaceBody || resultSituation != "SrfLanded" ||
                (!string.IsNullOrEmpty(Value(node, KoffeeIdentifiers.ResultBiomeValue)) &&
                    Value(node, KoffeeIdentifiers.ResultBiomeValue) != surfaceBiome)))
                return Invalid("Completed science context disagrees with its initial surface binding.", out error);

            if (telescopeCapture == null && !string.IsNullOrEmpty(contextBody) && (resultBody != contextBody || resultSituation != contextSituation))
                return Invalid("Completed science context disagrees with its initial situation binding.", out error);

            if (telescopeCapture != null && (resultBody != telescopeCapture.Target ||
                resultSituation != TelescopeCapture.ScienceSituation || !string.IsNullOrEmpty(Value(node, KoffeeIdentifiers.ResultBiomeValue))))
                return Invalid("Result disagrees with the telescope acquisition target.", out error);
            if (solarCapture != null && (resultBody != solarCapture.Body || resultSituation != solarCapture.Situation ||
                !string.IsNullOrEmpty(Value(node, KoffeeIdentifiers.ResultBiomeValue)) ||
                subjectId != experimentId + "@" + solarCapture.Body + solarCapture.Situation))
                return Invalid("Result disagrees with its initial solar capture.", out error);
            Guid? receipt = null;
            string extractionKind = "";
            double? extractionUT = null;
            if (status == ExperimentContainerStatus.Spent)
            {
                foreach (string key in ExtractionKeys)
                    if (node.GetValues(key).Length != 1)
                        return Invalid("Extraction field '" + key + "' must occur exactly once.", out error);
                if (!Guid.TryParseExact(Value(node, KoffeeIdentifiers.ExtractionReceiptIdValue),
                    "N", out Guid parsedReceipt) || parsedReceipt == Guid.Empty ||
                    string.IsNullOrWhiteSpace(Value(node, KoffeeIdentifiers.ExtractionKindValue)) ||
                    !TryFinite(node, KoffeeIdentifiers.ExtractionUtValue, out double parsedExtractionUT) ||
                    parsedExtractionUT < 0)
                    return Invalid("Invalid extraction receipt.", out error);
                receipt = parsedReceipt;
                extractionKind = Value(node, KoffeeIdentifiers.ExtractionKindValue);
                extractionUT = parsedExtractionUT;
            }
            else if (ExtractionKeys.Any(node.HasValue))
                return Invalid("Finalized container contains extraction fields.", out error);

            state = new ExperimentContainerState(ExperimentContainerState.CurrentSchemaVersion,
                id, experimentId, status, revision, runId, assigned, display,
                assignedLaboratoryPersistentId: laboratoryId,
                assignedLaboratorySlot: laboratorySlot, startUT: startUT,
                accumulatedUT: accumulated, lastCheckpointUT: checkpointUT,
                checkpointVerified: false, checkpointBody: checkpointBody,
                checkpointSituation: checkpointSituation, resultSubjectId: subjectId,
                resultBody: resultBody, resultSituation: resultSituation,
                resultBiome: Value(node, KoffeeIdentifiers.ResultBiomeValue),
                resultBiomeDisplay: Value(node, KoffeeIdentifiers.ResultBiomeDisplayValue),
                finalizedUT: finalizedUT, extractionReceiptId: receipt,
                extractionKind: extractionKind, extractionUT: extractionUT, researchCategory: category, researchPointsRequired: required,
                accumulatedResearchPoints: points, researchAffinity: affinity, checkpointLevel: level,
                    surfaceBody: surfaceBody, surfaceBiome: surfaceBiome, surfaceHasBiome: surfaceHasBiome,
                    contextBody: contextBody, contextSituation: contextSituation, temperatureReading: temperature, pressureReading: pressure, surfaceCapture: surfaceCapture, telescopeObservations: telescopeObservations, solarCapture: solarCapture);
            return ExperimentContainerReadResult.Valid;
        }

        internal static void Write(ConfigNode node, ExperimentContainerState state)
        {
            Remove(node);
            node.AddValue(KoffeeIdentifiers.SchemaVersionValue,
                ExperimentContainerState.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));
            node.AddValue(KoffeeIdentifiers.ContainerIdValue, state.ContainerId.ToString("N"));
            node.AddValue(KoffeeIdentifiers.ExperimentIdValue, state.ExperimentId);
            node.AddValue(KoffeeIdentifiers.ContainerStatusValue, state.Status.ToString());
            node.AddValue(KoffeeIdentifiers.RevisionValue,
                state.Revision.ToString(CultureInfo.InvariantCulture));
            if (state.HasAssignment)
            {
                node.AddValue(KoffeeIdentifiers.AssignedKerbalNameValue, state.AssignedKerbalName);
                node.AddValue(KoffeeIdentifiers.AssignedKerbalDisplayNameValue,
                    state.AssignedKerbalDisplayName);
                if (state.HasAssignedLaboratory)
                    node.AddValue(KoffeeIdentifiers.AssignedLaboratoryPersistentIdValue,
                        state.AssignedLaboratoryPersistentId.ToString(CultureInfo.InvariantCulture));
            }
            if (!string.IsNullOrEmpty(state.ContextBody))
            {
                node.AddValue("contextBindingVersion", "1");
                node.AddValue("contextBody", state.ContextBody);
                node.AddValue("contextSituation", state.ContextSituation);
            }
            if (state.HasSurfaceBinding)
            {
                node.AddValue("surfaceBindingVersion", "1");
                node.AddValue("surfaceBody", state.SurfaceBody);
                node.AddValue("surfaceBiome", state.SurfaceBiome);
                node.AddValue("surfaceHasBiome", state.SurfaceHasBiome.ToString());
            }
            StationSolarCaptureCodec.Write(node, state.SolarCapture);
            StationSurfaceCaptureCodec.Write(node, state.SurfaceCapture);
            TelescopeCaptureCodec.Write(node, state.TelescopeObservations);
            if (state.Status == ExperimentContainerStatus.Available) return;
            if (state.TemperatureReading != null)
            {
                var reading = state.TemperatureReading;
                node.AddValue("temperatureReadingVersion", "1");
                node.AddValue("temperatureReadingPart", reading.PartName);
                node.AddValue("temperatureReadingPartId", reading.PartId.ToString(CultureInfo.InvariantCulture));
                node.AddValue("temperatureReadingUT", reading.UT.ToString("R", CultureInfo.InvariantCulture));
                node.AddValue("temperatureReadingKelvin", reading.Kelvin.ToString("R", CultureInfo.InvariantCulture));
            }
            if (state.PressureReading != null)
            {
                var reading = state.PressureReading;
                node.AddValue("pressureReadingVersion", "1");
                node.AddValue("pressureReadingPart", reading.PartName);
                node.AddValue("pressureReadingPartId", reading.PartId.ToString(CultureInfo.InvariantCulture));
                node.AddValue("pressureReadingUT", reading.UT.ToString("R", CultureInfo.InvariantCulture));
                node.AddValue("pressureReadingKpa", reading.Kpa.ToString("R", CultureInfo.InvariantCulture));
            }
            node.AddValue(KoffeeIdentifiers.RunIdValue, state.RunId!.Value.ToString("N"));
            if (state.HasSlotBinding)
                node.AddValue(KoffeeIdentifiers.AssignedLaboratorySlotValue,
                    state.AssignedLaboratorySlot.ToString(CultureInfo.InvariantCulture));
            node.AddValue(KoffeeIdentifiers.RunStartUtValue,
                state.StartUT!.Value.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue(KoffeeIdentifiers.EffectiveWorkUtValue,
                state.AccumulatedUT.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue(KoffeeIdentifiers.ResearchCategoryValue, state.ResearchCategory!.Value.ToString());
            node.AddValue(KoffeeIdentifiers.ResearchPointsRequiredValue, state.ResearchPointsRequired.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue(KoffeeIdentifiers.AccumulatedResearchPointsValue, state.AccumulatedResearchPoints.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue(KoffeeIdentifiers.ResearchAffinityValue, state.ResearchAffinity.ToString(CultureInfo.InvariantCulture));
            node.AddValue(KoffeeIdentifiers.CheckpointLevelValue, state.CheckpointLevel.ToString(CultureInfo.InvariantCulture));
            node.AddValue(KoffeeIdentifiers.ResearchPointsPerHourValue, state.ResearchPointsPerHour.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue(KoffeeIdentifiers.LastCheckpointUtValue,
                state.LastCheckpointUT!.Value.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue(KoffeeIdentifiers.CheckpointVerifiedValue,
                state.CheckpointVerified.ToString());
            node.AddValue(KoffeeIdentifiers.CheckpointBodyValue, state.CheckpointBody);
            node.AddValue(KoffeeIdentifiers.CheckpointSituationValue, state.CheckpointSituation);
            if (state.Status == ExperimentContainerStatus.Running) return;
            node.AddValue(KoffeeIdentifiers.ResultSubjectIdValue, state.ResultSubjectId);
            node.AddValue(KoffeeIdentifiers.ResultBodyValue, state.ResultBody);
            node.AddValue(KoffeeIdentifiers.ResultSituationValue, state.ResultSituation);
            node.AddValue(KoffeeIdentifiers.ResultBiomeValue, state.ResultBiome);
            node.AddValue(KoffeeIdentifiers.ResultBiomeDisplayValue, state.ResultBiomeDisplay);
            node.AddValue(KoffeeIdentifiers.FinalizedUtValue,
                state.FinalizedUT!.Value.ToString("R", CultureInfo.InvariantCulture));
            if (state.Status != ExperimentContainerStatus.Spent) return;
            node.AddValue(KoffeeIdentifiers.ExtractionReceiptIdValue,
                state.ExtractionReceiptId!.Value.ToString("N"));
            node.AddValue(KoffeeIdentifiers.ExtractionKindValue, state.ExtractionKind);
            node.AddValue(KoffeeIdentifiers.ExtractionUtValue,
                state.ExtractionUT!.Value.ToString("R", CultureInfo.InvariantCulture));
        }

        internal static void CopyRaw(ConfigNode source, ConfigNode destination)
        {
            destination.ClearData();
            source.CopyTo(destination);
        }

        internal static void Remove(ConfigNode node)
        {
            node.RemoveNodes(StationSolarCaptureCodec.NodeName);
            node.RemoveNodes(StationSurfaceCaptureCodec.NodeName);
            node.RemoveNodes(TelescopeCaptureCodec.NodeName);
            foreach (string key in SurfaceKeys) node.RemoveValues(key);
            foreach (string key in ContextKeys) node.RemoveValues(key);
            foreach (string key in TemperatureKeys) node.RemoveValues(key);
            foreach (string key in PressureKeys) node.RemoveValues(key);
            foreach (string key in BaseKeys) node.RemoveValues(key);
            foreach (string key in RuntimeKeys) node.RemoveValues(key);
            foreach (string key in ResearchKeys) node.RemoveValues(key);
            node.RemoveValues(KoffeeIdentifiers.AccumulatedUtValue);
            foreach (string key in LaboratoryKeys) node.RemoveValues(key);
            foreach (string key in ResultKeys) node.RemoveValues(key);
            foreach (string key in ExtractionKeys) node.RemoveValues(key);
        }

        private static bool HasRunFields(ConfigNode node) =>
            node.GetNodes(StationSolarCaptureCodec.NodeName).Length != 0 ||
            node.GetNodes(StationSurfaceCaptureCodec.NodeName).Length != 0 ||
            node.HasValue(KoffeeIdentifiers.RunIdValue) ||
            node.HasValue(KoffeeIdentifiers.RunStartUtValue) ||
            node.HasValue(KoffeeIdentifiers.AccumulatedUtValue) ||
            node.HasValue(KoffeeIdentifiers.EffectiveWorkUtValue) || ResearchKeys.Any(node.HasValue) ||
            node.HasValue(KoffeeIdentifiers.LastCheckpointUtValue) ||
            node.HasValue(KoffeeIdentifiers.CheckpointVerifiedValue) ||
            node.HasValue(KoffeeIdentifiers.CheckpointBodyValue) ||
            node.HasValue(KoffeeIdentifiers.CheckpointSituationValue) ||
            ResultKeys.Any(node.HasValue) || ExtractionKeys.Any(node.HasValue) || TemperatureKeys.Any(node.HasValue) || PressureKeys.Any(node.HasValue);

        private static string Value(ConfigNode node, string key) => node.GetValue(key) ?? "";

        private static bool TryFinite(ConfigNode node, string key, out double value) =>
            double.TryParse(Value(node, key), NumberStyles.Float, CultureInfo.InvariantCulture,
                out value) && ExperimentContainerState.Finite(value);

        private static ExperimentContainerReadResult Invalid(string message, out string error)
        {
            error = message;
            return ExperimentContainerReadResult.Invalid;
        }
    }
}

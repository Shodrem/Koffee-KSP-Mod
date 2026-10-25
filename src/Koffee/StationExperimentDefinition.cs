using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal enum StationExperimentBodyMode { Listed, Any }
    internal enum StationResultReading { None, RequiredPartTemperature, RequiredPartPressure, SurfaceBiomeObservation, SurfaceTerrainAltitude, TelescopeImage, TelescopePairImage, SolarIrradiance, SolarRadiationPressure }

    internal enum StationExperimentExecutionModel
    {
        SinglePhase
    }

    internal enum StationLabCapability
    {
        StationBasic,
        BiGEn,
        MaPhyTec,
        Astronomy
    }

    internal sealed class StationExperimentPersonnel
    {
        internal string Trait { get; }
        internal int MinimumLevel { get; }
        internal int WorkerCount { get; }

        internal StationExperimentPersonnel(string trait, int minimumLevel, int workerCount)
        {
            Trait = trait;
            MinimumLevel = minimumLevel;
            WorkerCount = workerCount;
        }
    }

    internal sealed class StationExperimentDefinition
    {
        private static bool FiniteBudget(double points) => !double.IsNaN(points) && !double.IsInfinity(points) && points > 0;

        internal string Id { get; }
        internal int SchemaVersion { get; }
        internal string Description { get; }
        internal StationExperimentExecutionModel ExecutionModel { get; }
        internal ScienceCategory ScienceCategory { get; }
        internal double ResearchPointsRequired { get; }
        internal double XmitDataScalar { get; }
        internal double RecoveryScienceBonusPercent { get; }
        internal IReadOnlyList<string> Bodies { get; }
        internal StationExperimentBodyMode BodyMode { get; }
        internal bool AllowsBody(string body) => !string.IsNullOrWhiteSpace(body) &&
            (BodyMode == StationExperimentBodyMode.Any || Bodies.Contains(body, StringComparer.Ordinal));
        internal StationExperimentPersonnel Personnel { get; }
        internal StationLabCapability LabCapability { get; }
        internal string RequiredPartName { get; }
        internal StationResultReading ResultReading { get; }

        internal StationExperimentDefinition(string id, int schemaVersion, string description,
            StationExperimentExecutionModel executionModel, ScienceCategory scienceCategory,
            double researchPointsRequired, double xmitDataScalar, IEnumerable<string> bodies,
            StationExperimentPersonnel personnel, StationLabCapability labCapability,
            double recoveryScienceBonusPercent = 0,
            StationExperimentBodyMode bodyMode = StationExperimentBodyMode.Listed, string? requiredPartName = null,
            StationResultReading resultReading = StationResultReading.None)
        {
            Id = id;
            SchemaVersion = schemaVersion;
            Description = description;
            ExecutionModel = executionModel;
            ScienceCategory = scienceCategory;
            if (!FiniteBudget(researchPointsRequired)) throw new ArgumentOutOfRangeException(nameof(researchPointsRequired));
            ResearchPointsRequired = researchPointsRequired;
            XmitDataScalar = xmitDataScalar;
            RecoveryScienceBonusPercent = recoveryScienceBonusPercent;
            Bodies = new ReadOnlyCollection<string>(bodies.ToArray());
            BodyMode = bodyMode;
            Personnel = personnel;
            LabCapability = labCapability;
            RequiredPartName = requiredPartName ?? "";
            ResultReading = resultReading;
        }
    }

    internal sealed class StationExperimentConfig
    {
        internal const string DuplicateValue = "<KOFFEE_DUPLICATE_VALUE>";
        internal IReadOnlyDictionary<string, string?> Fields { get; }
        internal IReadOnlyList<IReadOnlyDictionary<string, string?>> Bodies { get; }
        internal IReadOnlyList<IReadOnlyDictionary<string, string?>> Personnel { get; }
        internal IReadOnlyList<IReadOnlyDictionary<string, string?>> LabRequirements { get; }
        internal IReadOnlyList<IReadOnlyDictionary<string, string?>> PartRequirements { get; }

        internal StationExperimentConfig(IReadOnlyDictionary<string, string?> fields,
            IReadOnlyList<IReadOnlyDictionary<string, string?>> bodies,
            IReadOnlyList<IReadOnlyDictionary<string, string?>> personnel,
            IReadOnlyList<IReadOnlyDictionary<string, string?>> labRequirements,
            IReadOnlyList<IReadOnlyDictionary<string, string?>>? partRequirements = null)
        {
            Fields = fields;
            Bodies = bodies;
            Personnel = personnel;
            LabRequirements = labRequirements;
            PartRequirements = partRequirements ?? Array.Empty<IReadOnlyDictionary<string, string?>>();
        }
    }

    internal sealed class StationExperimentCatalog
    {
        private readonly IReadOnlyDictionary<string, StationExperimentDefinition> definitions;
        internal static readonly StationExperimentCatalog Empty = new StationExperimentCatalog(
            new Dictionary<string, StationExperimentDefinition>(StringComparer.Ordinal));
        internal int Count => definitions.Count;
        internal IEnumerable<StationExperimentDefinition> Definitions => definitions.Values;

        private StationExperimentCatalog(
            IReadOnlyDictionary<string, StationExperimentDefinition> definitions)
        {
            this.definitions = definitions;
        }

        internal bool TryGet(string? id, out StationExperimentDefinition definition)
        {
            if (id != null && definitions.TryGetValue(id, out StationExperimentDefinition? found) &&
                found != null)
            {
                definition = found;
                return true;
            }
            definition = null!;
            return false;
        }

        internal static StationExperimentCatalog Parse(IEnumerable<StationExperimentConfig> configs,
            out IReadOnlyList<string> diagnostics)
        {
            var messages = new List<string>();
            var parsed = new List<StationExperimentDefinition>();
            foreach (StationExperimentConfig config in configs)
            {
                if (TryParse(config, messages, out StationExperimentDefinition? definition))
                    parsed.Add(definition!);
            }

            var result = new Dictionary<string, StationExperimentDefinition>(StringComparer.Ordinal);
            foreach (IGrouping<string, StationExperimentDefinition> group in parsed.GroupBy(
                definition => definition.Id, StringComparer.Ordinal))
            {
                if (group.Count() != 1)
                {
                    messages.Add("Station experiment '" + group.Key +
                        "' is defined more than once; every duplicate was ignored.");
                    continue;
                }
                result.Add(group.Key, group.Single());
            }
            diagnostics = messages;
            return result.Count == 0 ? Empty : new StationExperimentCatalog(result);
        }

        internal StationExperimentCatalog Resolve(Func<string, bool> scienceExists,
            Func<string, bool>? bodyExists, Func<string, int>? linkedPartCount,
            out IReadOnlyList<string> diagnostics)
        {
            var messages = new List<string>();
            var resolved = new Dictionary<string, StationExperimentDefinition>(StringComparer.Ordinal);
            foreach (StationExperimentDefinition definition in definitions.Values)
            {
                bool valid = true;
                if (!scienceExists(definition.Id))
                {
                    messages.Add("Station experiment '" + definition.Id +
                        "' has no matching EXPERIMENT_DEFINITION; entry ignored.");
                    valid = false;
                }
                if (bodyExists != null)
                {
                    foreach (string body in definition.Bodies)
                        if (!bodyExists(body))
                        {
                            messages.Add("Station experiment '" + definition.Id +
                                "' references unknown body '" + body + "'; entry ignored.");
                            valid = false;
                        }
                }
                if (linkedPartCount != null)
                {
                    int count = linkedPartCount(definition.Id);
                    if (count != 1)
                    {
                        messages.Add("Station experiment '" + definition.Id + "' is linked by " +
                            count.ToString(CultureInfo.InvariantCulture) +
                            " PART definitions; exactly one is required; entry ignored.");
                        valid = false;
                    }
                }
                if (valid) resolved.Add(definition.Id, definition);
            }
            diagnostics = messages;
            return resolved.Count == 0 ? Empty : new StationExperimentCatalog(resolved);
        }

        internal StationExperimentCatalog ResolveEquipment(Func<string, bool>? partExists,
            out IReadOnlyList<string> diagnostics)
        {
            var messages = new List<string>();
            var resolved = new Dictionary<string, StationExperimentDefinition>(StringComparer.Ordinal);
            foreach (var definition in definitions.Values)
            {
                if (!string.IsNullOrEmpty(definition.RequiredPartName) && partExists != null && !partExists(definition.RequiredPartName))
                    messages.Add("Station experiment '" + definition.Id + "' requires unresolved PART '" +
                        definition.RequiredPartName + "'; entry ignored.");
                else resolved.Add(definition.Id, definition);
            }
            diagnostics = messages;
            return resolved.Count == 0 ? Empty : new StationExperimentCatalog(resolved);
        }

        private static bool TryParse(StationExperimentConfig config, ICollection<string> diagnostics,
            out StationExperimentDefinition? definition)
        {
            definition = null;
            string label = Value(config.Fields, "name") ?? "<missing>";
            bool valid = true;
            string? id = Required(config.Fields, "name", label, diagnostics, ref valid);
            string? description = Required(config.Fields, "description", label, diagnostics, ref valid);
            string? schemaText = Required(config.Fields, "schemaVersion", label, diagnostics, ref valid);
            string? executionText = Required(config.Fields, "executionModel", label, diagnostics, ref valid);
            string? categoryText = Required(config.Fields, "scienceCategory", label, diagnostics, ref valid);
            string? transmitText = Required(config.Fields, "xmitDataScalar", label, diagnostics, ref valid);
            string? requiredText = Required(config.Fields, "researchPointsRequired", label, diagnostics, ref valid);

            if (!int.TryParse(schemaText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int schemaVersion) || schemaVersion != 3)
            {
                Invalid(label, "schemaVersion", schemaText, diagnostics); valid = false;
                diagnostics.Add("Station experiment '" + label + "' requires catalog schemaVersion 3 with explicit researchPointsRequired; older catalogs are not converted.");
            }
            if (!double.TryParse(requiredText, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double required) || !Finite(required) || required <= 0)
            {
                Invalid(label, "researchPointsRequired", requiredText, diagnostics); valid = false;
            }
            StationExperimentExecutionModel executionModel = StationExperimentExecutionModel.SinglePhase;
            if (!string.Equals(executionText, nameof(StationExperimentExecutionModel.SinglePhase),
                StringComparison.Ordinal))
            {
                Invalid(label, "executionModel", executionText, diagnostics); valid = false;
            }
            if (!ScienceCategories.TryParse(categoryText, out ScienceCategory category))
            {
                Invalid(label, "scienceCategory", categoryText, diagnostics); valid = false;
            }
            if (Value(config.Fields, "durationSeconds") != null)
            {
                Invalid(label, "durationSeconds", Value(config.Fields, "durationSeconds"), diagnostics); valid = false;
            }
            if (!double.TryParse(transmitText, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double transmit) || !Finite(transmit) || transmit < 0 || transmit > 1)
            {
                Invalid(label, "xmitDataScalar", transmitText, diagnostics); valid = false;
            }

            string? bonusText = Value(config.Fields, "recoveryScienceBonusPercent");
            double bonus = 0;
            if (bonusText != null && (!double.TryParse(bonusText, NumberStyles.Float,
                CultureInfo.InvariantCulture, out bonus) || !Finite(bonus) || bonus < 0))
            {
                Invalid(label, "recoveryScienceBonusPercent", bonusText, diagnostics); valid = false;
            }
            string? modeText = Value(config.Fields, "bodyMode");
            StationExperimentBodyMode mode = StationExperimentBodyMode.Listed;
            if (modeText != null && (!Enum.TryParse(modeText, false, out mode) ||
                !string.Equals(mode.ToString(), modeText, StringComparison.Ordinal) ||
                (mode != StationExperimentBodyMode.Listed && mode != StationExperimentBodyMode.Any)))
            {
                Invalid(label, "bodyMode", modeText, diagnostics); valid = false;
            }
            if (mode == StationExperimentBodyMode.Any && config.Bodies.Count != 0)
            {
                diagnostics.Add("Station experiment '" + label + "' with bodyMode Any must not contain BODY nodes.");
                valid = false;
            }
            var bodies = new List<string>();
            if (mode == StationExperimentBodyMode.Listed && config.Bodies.Count == 0)
            {
                diagnostics.Add("Station experiment '" + label + "' requires at least one BODY node.");
                valid = false;
            }
            foreach (IReadOnlyDictionary<string, string?> body in config.Bodies)
            {
                string? name = Required(body, "name", label + ".BODY", diagnostics, ref valid);
                if (!string.IsNullOrWhiteSpace(name)) bodies.Add(name!);
            }
            if (bodies.Count != bodies.Distinct(StringComparer.Ordinal).Count())
            {
                diagnostics.Add("Station experiment '" + label + "' contains duplicate BODY names.");
                valid = false;
            }

            StationExperimentPersonnel? personnel = null;
            if (config.Personnel.Count != 1)
            {
                diagnostics.Add("Station experiment '" + label +
                    "' requires exactly one PERSONNEL node.");
                valid = false;
            }
            else
            {
                IReadOnlyDictionary<string, string?> fields = config.Personnel[0];
                string? trait = Required(fields, "trait", label + ".PERSONNEL", diagnostics, ref valid);
                string? levelText = Required(fields, "minimumLevel", label + ".PERSONNEL", diagnostics, ref valid);
                string? countText = Required(fields, "workerCount", label + ".PERSONNEL", diagnostics, ref valid);
                if (!string.Equals(trait, "Scientist", StringComparison.Ordinal))
                {
                    Invalid(label + ".PERSONNEL", "trait", trait, diagnostics); valid = false;
                }
                if (!int.TryParse(levelText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int level) || level < 0 || level > 5)
                {
                    Invalid(label + ".PERSONNEL", "minimumLevel", levelText, diagnostics); valid = false;
                }
                if (!int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int count) || count != 1)
                {
                    Invalid(label + ".PERSONNEL", "workerCount", countText, diagnostics); valid = false;
                }
                if (valid && trait != null) personnel = new StationExperimentPersonnel(trait, level, count);
            }

            StationLabCapability capability = default;
            if (config.LabRequirements.Count != 1)
            {
                diagnostics.Add("Station experiment '" + label +
                    "' requires exactly one LAB_REQUIREMENT node.");
                valid = false;
            }
            else
            {
                string? capabilityText = Required(config.LabRequirements[0], "capability",
                    label + ".LAB_REQUIREMENT", diagnostics, ref valid);
                if (!Enum.TryParse(capabilityText, false, out capability) ||
                    !Enum.GetNames(typeof(StationLabCapability)).Contains(capabilityText, StringComparer.Ordinal))
                {
                    Invalid(label + ".LAB_REQUIREMENT", "capability", capabilityText, diagnostics);
                    valid = false;
                }
            }

            string? requiredPart = null;
            if (config.PartRequirements.Count > 1)
            {
                diagnostics.Add("Station experiment '" + label + "' permits at most one PART_REQUIREMENT node.");
                valid = false;
            }
            else if (config.PartRequirements.Count == 1)
            {
                requiredPart = Required(config.PartRequirements[0], "name", label + ".PART_REQUIREMENT", diagnostics, ref valid);
            }
            StationResultReading reading = StationResultReading.None;
            string? readingText = Value(config.Fields, "resultReading");
            if (readingText != null && (!Enum.TryParse(readingText, false, out reading) ||
                !string.Equals(reading.ToString(), readingText, StringComparison.Ordinal) ||
                (reading != StationResultReading.None && reading != StationResultReading.RequiredPartTemperature &&
                 reading != StationResultReading.SolarRadiationPressure && reading != StationResultReading.SolarIrradiance && reading != StationResultReading.RequiredPartPressure && reading != StationResultReading.TelescopeImage && reading != StationResultReading.TelescopePairImage && !StationSurfaceCapture.Required(reading))))
            { Invalid(label, "resultReading", readingText, diagnostics); valid = false; }
            if (reading != StationResultReading.None && string.IsNullOrWhiteSpace(requiredPart))
            { diagnostics.Add("Station experiment '" + label + "' requires PART_REQUIREMENT for resultReading " + reading + "."); valid = false; }
            if (!valid || id == null || description == null || personnel == null) return false;
            definition = new StationExperimentDefinition(id, schemaVersion, description,
                executionModel, category, required, transmit, bodies, personnel, capability, bonus, mode, requiredPart, reading);
            return true;
        }

        private static string? Required(IReadOnlyDictionary<string, string?> fields, string key,
            string label, ICollection<string> diagnostics, ref bool valid)
        {
            string? value = Value(fields, key);
            if (!string.IsNullOrWhiteSpace(value) && value != StationExperimentConfig.DuplicateValue)
                return value;
            Invalid(label, key, value, diagnostics);
            valid = false;
            return null;
        }

        private static string? Value(IReadOnlyDictionary<string, string?> fields, string key) =>
            fields.TryGetValue(key, out string? value) ? value : null;

        private static void Invalid(string label, string field, string? value,
            ICollection<string> diagnostics) => diagnostics.Add("Station experiment '" + label +
                "' has invalid field '" + field + "' with value '" + (value ?? "<missing>") + "'.");

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

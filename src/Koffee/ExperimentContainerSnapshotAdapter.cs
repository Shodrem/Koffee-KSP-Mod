using System;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal static class ExperimentContainerSnapshotAdapter
    {
        // Fixed parts retain their prefab identity; configurable hosts use the saved selection.
        internal static string ExperimentId(ProtoPartSnapshot part)
        {
            var prefabs = part.partPrefab?.Modules?.OfType<ModuleKoffeeExperiment>().ToArray();
            if (prefabs == null || prefabs.Length != 1) return "";
            var prefab = prefabs[0];
            if (string.IsNullOrEmpty(prefab.editorExperimentFamily)) return prefab.experimentID;
            var modules = Modules(part);
            return modules.Length == 1 ? modules[0].moduleValues.GetValue(KoffeeIdentifiers.ExperimentIdValue) ?? "" : "";
        }

        internal static ExperimentContainerReadResult Read(ProtoPartSnapshot part,
            string expectedExperimentId, out ExperimentContainerState? state, out string error)
        {
            state = null;
            error = "";
            ProtoPartModuleSnapshot[] modules = Modules(part);
            if (modules.Length == 0) return ExperimentContainerReadResult.Missing;
            if (modules.Length != 1)
            {
                error = "ProtoPartSnapshot contains more than one ModuleKoffeeExperiment.";
                return ExperimentContainerReadResult.Invalid;
            }
            return ExperimentContainerStateCodec.Read(modules[0].moduleValues,
                expectedExperimentId, out state, out error);
        }

        internal static bool Write(ProtoPartSnapshot part, ExperimentContainerState state,
            out string error)
            => WriteState(part, state, null, false, out error);

        private static bool WriteState(ProtoPartSnapshot part, ExperimentContainerState state,
            ConfigNode? scienceData, bool replaceScienceData, out string error)
        {
            error = "";
            ProtoPartModuleSnapshot[] modules = Modules(part);
            if (modules.Length != 1)
            {
                error = modules.Length == 0
                    ? "ProtoPartSnapshot has no ModuleKoffeeExperiment."
                    : "ProtoPartSnapshot contains more than one ModuleKoffeeExperiment.";
                return false;
            }
            string? persistedExperimentId = modules[0].moduleValues.GetValue(
                KoffeeIdentifiers.ExperimentIdValue);
            if (!string.IsNullOrEmpty(persistedExperimentId) &&
                !string.Equals(persistedExperimentId, state.ExperimentId, StringComparison.Ordinal))
            {
                error = "ProtoPartSnapshot experimentID does not match the state being written.";
                return false;
            }
            try
            {
                // Publish state and result together only after every copy/write succeeds.
                // A failed write leaves the original node available for retry and refund.
                ConfigNode candidate = modules[0].moduleValues.CreateCopy();
                ExperimentContainerStateCodec.Write(candidate, state);
                if (replaceScienceData || state.Status != ExperimentContainerStatus.Finalized)
                    candidate.RemoveNodes("ScienceData");
                if (replaceScienceData && scienceData != null) candidate.AddNode(scienceData.CreateCopy());
                modules[0].moduleValues = candidate;
                return true;
            }
            catch (Exception exception)
            {
                error = "ProtoPartSnapshot state/result write failed: " + exception.Message;
                return false;
            }
        }

        internal static bool ReadScienceData(ProtoPartSnapshot part, out ConfigNode? scienceData,
            out string error)
        {
            scienceData = null;
            error = "";
            ProtoPartModuleSnapshot[] modules = Modules(part);
            if (modules.Length != 1)
            {
                error = modules.Length == 0
                    ? "ProtoPartSnapshot has no ModuleKoffeeExperiment."
                    : "ProtoPartSnapshot contains more than one ModuleKoffeeExperiment.";
                return false;
            }
            ConfigNode[] nodes = modules[0].moduleValues.GetNodes("ScienceData");
            if (nodes.Length > 1)
            {
                error = "ProtoPartSnapshot contains more than one ScienceData result.";
                return false;
            }
            scienceData = nodes.Length == 1 ? nodes[0].CreateCopy() : null;
            return true;
        }

        internal static bool Write(ProtoPartSnapshot part, ExperimentContainerState state,
            ConfigNode? scienceData, out string error)
            => WriteState(part, state, scienceData, true, out error);

        internal static ProtoPartModuleSnapshot[] Modules(ProtoPartSnapshot part)
        {
            if (part?.modules == null) return new ProtoPartModuleSnapshot[0];
            return part.modules.Where(module => module != null &&
                (string.Equals(module.moduleName, nameof(ModuleKoffeeExperiment), StringComparison.Ordinal) ||
                 string.Equals(module.moduleValues?.GetValue("name"), nameof(ModuleKoffeeExperiment),
                    StringComparison.Ordinal))).ToArray();
        }
    }
}

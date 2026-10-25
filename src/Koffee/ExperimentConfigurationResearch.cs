using System;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal static class ExperimentConfigurationResearch
    {
        internal static void OnResearched(RDTech tech, RDTech.OperationResult result)
        {
            if (tech == null || result != RDTech.OperationResult.Successful || tech.state != RDTech.State.Available ||
                HighLogic.CurrentGame == null || ReferenceEquals(ResearchAndDevelopment.Instance, null) ||
                !ReferenceEquals(tech.host, ResearchAndDevelopment.Instance) ||
                ResearchAndDevelopment.GetTechnologyState(tech.techID) != RDTech.State.Available) return;

            // Stock PurchasePart records ownership, updates ProtoTechNode and
            // fires OnPartPurchased. It does not debit funds. Only our zero-cost
            // configuration records qualify; hardware purchases stay stock.
            foreach (AvailablePart part in tech.partsAssigned.ToArray())
                if (part?.partPrefab != null && part.TechRequired == tech.techID && part.entryCost == 0 &&
                    part.partPrefab.FindModuleImplementing<ModuleKoffeeExperimentConfiguration>() != null &&
                    part.partPrefab.FindModuleImplementing<ModuleKoffeeExperiment>() is ModuleKoffeeExperiment experiment &&
                    !string.IsNullOrEmpty(experiment.experimentID) && string.IsNullOrEmpty(experiment.editorExperimentFamily) &&
                    !tech.PartIsPurchased(part))
                    tech.PurchasePart(part);
        }
    }
}

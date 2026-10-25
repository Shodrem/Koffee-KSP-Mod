using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class ScienceExperimentRegistryLoader
    {
        internal static void Load()
        {
            ScienceExperimentKoffeeRegistry.Clear();
            foreach (ConfigNode settings in GameDatabase.Instance.GetConfigNodes(KoffeeIdentifiers.ExperimentsNode))
            {
                foreach (ConfigNode node in settings.GetNodes(KoffeeIdentifiers.ExperimentNode))
                {
                    string id = node.GetValue("experimentID");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        Debug.LogError("[Koffee] Invalid " + KoffeeIdentifiers.ExperimentNode +
                            " configuration; field 'experimentID' has value '" +
                            (id ?? "<missing>") + "'; entry ignored.");
                        continue;
                    }
                    if (ResearchAndDevelopment.GetExperiment(id) == null)
                    {
                        Debug.LogWarning("[Koffee] Experiment definition not found; entry ignored: " + id);
                        continue;
                    }
                    ScienceExperimentKoffeeRegistry.Register(id);
                }
            }
        }
    }
}

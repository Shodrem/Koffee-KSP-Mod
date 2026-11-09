using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    // Permissions are explicit per experiment. Unknown third-party science keeps
    // its execution rules; it never inherits the physical Koffee policy.
    internal static class ScienceReportPolicy
    {
        private static readonly HashSet<string> stock = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> physical = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> managerExcluded = new HashSet<string>(StringComparer.Ordinal);
        internal static void Load()
        {
            stock.Clear(); physical.Clear(); managerExcluded.Clear();
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("KOFFEE_REPORT_POLICY"))
            {
                foreach (string id in node.GetValues("stockInstrument")) stock.Add(id);
                foreach (string id in node.GetValues("physicalContainer")) physical.Add(id);
                foreach (string id in node.GetValues("managerExcluded")) managerExcluded.Add(id);
            }
        }
        internal static bool StockInstrument(string id) => stock.Contains(id);
        internal static string Experiment(string subject) => (subject ?? "").Split('@')[0];
        internal static bool Physical(ScienceData data) => data != null && physical.Contains(Experiment(data.subjectID));
        internal static bool CanReceiveInManager(ScienceData data) => data != null && !Physical(data) &&
            !managerExcluded.Contains(Experiment(data.subjectID));
        internal static bool Physical(ModuleKoffeeExperiment module) =>
            module.editorExperimentFamily == "Container" || physical.Contains(module.experimentID);
    }
}

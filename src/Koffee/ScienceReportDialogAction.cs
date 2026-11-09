using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    // Stock ScienceDefs.cfg identifiers, independent of the execution gate.
    // This action never redirects the stock Keep callback or Koffee completion.
    internal static class ScienceReportDialogAction
    {
        private static readonly HashSet<string> stockReports = new HashSet<string>(StringComparer.Ordinal)
        {
            "crewReport", "evaReport", "mysteryGoo", "surfaceSample", "mobileMaterialsLab",
            "temperatureScan", "barometerScan", "seismicScan", "gravityScan", "atmosphereAnalysis",
            "asteroidSample", "cometSample_short", "cometSample_intermediate", "cometSample_long",
            "cometSample_interstellar", "infraredTelescope", "magnetometer", "evaScience"
        };

        internal static string Preview(ExperimentResultDialogPage? page,
            out IScienceDataContainer? source, out ModuleKoffeeScienceStore? receiver)
        {
            source = null; receiver = null;
            var data = page?.pageData;
            if (data == null || !stockReports.Contains(ScienceReportPolicy.Experiment(data.subjectID))) return "DialogHidden";
            var host = page!.host;
            if (host == null || !ScienceReportTransfer.Live(host)) return "DialogUnavailable";
            source = ScienceReportTransfer.Source(host.vessel, data);
            if (source is ModuleKoffeeExperiment) return "DialogHidden";
            if (!ScienceReportPolicy.CanReceiveInManager(data)) return "ManagerExcluded";
            if (!(source is PartModule module) || !ScienceReportTransfer.Live(module.part) ||
                !(source is ModuleScienceExperiment || source is ModuleScienceContainer) ||
                (source is ModuleScienceExperiment instrument && ScienceReportExtraction.Resetting(instrument)) ||
                ScienceReportTransfer.Mutating || ScienceLabAcceptance.Busy) return "DialogUnavailable";
            receiver = ModuleKoffeeScienceStore.Preferred(host.vessel);
            if (receiver?.Container == null || !ScienceReportTransfer.Live(receiver.part)) return "DialogNoReceiver";
            if (ReferenceEquals(source, receiver.Container)) return "DialogAlreadyStored";
            if (!receiver.Container.allowRepeatedSubjects && receiver.Container.GetData().Any(d => d.subjectID == data.subjectID))
                return "DialogDuplicate";
            if (!ScienceOperationCharge.Available(receiver.part)) return "NoEC";
            return "DialogReady";
        }

        internal static bool Save(ExperimentResultDialogPage page)
        {
            string state = Preview(page, out var source, out var receiver);
            if (state != "DialogReady")
            {
                if (state != "DialogHidden") ScienceReportUI.Message(state);
                return false;
            }
            // Revalidate on click; a preview never reserves EC or changes owners.
            return ScienceReportTransfer.Move(new[] { source! }, receiver!, page.pageData) == 1;
        }
    }
}

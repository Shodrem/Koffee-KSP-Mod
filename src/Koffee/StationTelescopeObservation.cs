using System;
using System.Linq;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    internal static class StationTelescopeObservation
    {
        internal static bool Required(StationExperimentDefinition definition) => definition.ResultReading == StationResultReading.TelescopeImage || IsPair(definition);

        internal static bool IsPair(StationExperimentDefinition definition) => definition.ResultReading == StationResultReading.TelescopePairImage;
        internal static bool CaptureMatches(TelescopeCapture capture, StationExperimentDefinition definition) =>
            (capture.Pair != null) == IsPair(definition);

        internal static bool Matches(ExperimentContainerState state, StationExperimentDefinition definition) =>
            Required(definition) ? state.TelescopeCapture != null &&
                CaptureMatches(state.TelescopeCapture, definition) && definition.AllowsBody(state.TelescopeCapture.Target) &&
                (!state.StartUT.HasValue || state.TelescopeCapture.UT <= state.StartUT.Value) : state.TelescopeCapture == null;

        internal static string ResultText(TelescopeCapture capture) => capture.Pair != null ?
            Localizer.Format("#Koffee_Pair_Report", TelescopeBodyNames.Display(FlightGlobals.GetBodyByName(capture.Target),capture.Target),
                TelescopeBodyNames.Display(FlightGlobals.GetBodyByName(capture.CompanionPlanet),capture.CompanionPlanet),
                capture.UT.ToString("F3"), (capture.Pair.Sample.Separation*180/Math.PI).ToString("F5"),
                capture.FieldOfView.ToString("F3"), capture.Pair.Sample.PlanetPixels.ToString("F1"), capture.Pair.Sample.MoonPixels.ToString("F1")) :
            Localizer.Format("#Koffee_Telescope_Report", TelescopeBodyNames.Display(FlightGlobals.GetBodyByName(capture.Target),capture.Target),
                capture.UT.ToString("F3"), capture.Distance.ToString("G9"), capture.PointingError.ToString("F5"),
                capture.FieldOfView.ToString("F3"), capture.ObserverBody, capture.ObserverSituation, capture.InstrumentId.ToString());

        internal static bool HasScientist(ModuleKoffeeExperiment experiment) => experiment.State?.HasAssignment == true &&
            StationExperimentCatalogLoader.Current.TryGet(experiment.experimentID, out var definition) &&
            experiment.vessel?.parts.Where(p => p != null && p.State != PartStates.DEAD &&
                p.persistentId == experiment.State.AssignedLaboratoryPersistentId && p.CrewCapacity > 0 &&
                p.Modules.OfType<ModuleKoffeeLab>().Any(l => l.Supports(definition.LabCapability)))
                .SelectMany(p => p.protoModuleCrew).Any(c => c != null && c.name == experiment.State.AssignedKerbalName &&
                    StationExperimentRuntime.ValidScientist(c, definition)) == true;

        // Read-only preflight for both viewer entry points and every acquisition.
        // Capturing never buys Koffee or allocates another laboratory slot.
        internal static string Readiness(ModuleKoffeeExperiment experiment, ModuleKoffeeTelescope? telescope = null)
        {
            if (!HasScientist(experiment)) return "Scientist";
            if (telescope != null && (telescope.vessel != experiment.vessel ||
                telescope.part.persistentId != experiment.State!.AssignedLaboratoryPersistentId)) return "InstrumentUnavailable";
            var snapshots = StationExperimentRuntime.Capture(experiment.vessel);
            var snapshot = snapshots.FirstOrDefault(e => e.Module == experiment);
            if (snapshot?.Definition == null || !Required(snapshot.Definition)) return "NoExperiment";
            if (snapshot.State?.Status == ExperimentContainerStatus.Running && !snapshot.WorkComplete)
            {
                if (!snapshot.Reservation.HasValue) return "Workplace";
            }
            else
            {
                var lab = snapshot.AssignedLaboratory;
                if (lab == null || !snapshot.SelectableScientists.Any(c =>
                    c.Scientist.name == experiment.State!.AssignedKerbalName && c.Laboratory == lab)) return "Workplace";
                int occupied = snapshots.Count(e =>
                    e.Module != experiment && e.Reservation?.Laboratory == lab);
                if (occupied >= lab.StationSlotCapacity) return "Workplace";
            }
            ExperimentManagerService.Select(experiment.vessel, Planetarium.GetUniversalTime(), false, out var reason, true);
            return reason == StationExperimentPauseReason.None ? "" : "Team";
        }

        internal static void Open(ModuleKoffeeExperiment experiment)
        {
            string reason = Readiness(experiment);
            if (reason != "")
            { ScreenMessages.PostScreenMessage(Localizer.Format("#Koffee_Telescope_Reason_" + reason), 5f, ScreenMessageStyle.UPPER_CENTER); return; }
            var instrument = experiment.vessel?.parts.Where(p => p != null && p.State != PartStates.DEAD)
                .Where(p => p.persistentId == experiment.State!.AssignedLaboratoryPersistentId)
                .SelectMany(p => p.Modules.OfType<ModuleKoffeeTelescope>()).FirstOrDefault();
            if (instrument != null) instrument.OpenForExperiment(experiment);
            else ScreenMessages.PostScreenMessage(Localizer.Format("#Koffee_Telescope_NoInstrument"), 5f, ScreenMessageStyle.UPPER_CENTER);
        }
    }
}

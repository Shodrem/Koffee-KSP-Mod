using System;
using System.Linq;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    internal enum KoffeeFlightTab { Summary, Experiments, Scientists }
    internal enum KoffeeFlightExperimentGroup { Running, Stopped, Completed, Ready, Unknown }

    // Presentation only: never advance work, reconcile profiles or renew coverage here.
    internal sealed class KoffeeFlightExperiment
    {
        internal StationExperimentRuntimeSnapshot Runtime { get; }
        internal KoffeeFlightExperimentGroup Group { get; }
        internal bool HasPendingResult { get; }
        internal double? Progress { get; }
        internal string Title { get; }
        internal string Status { get; }
        internal string Attention { get; }
        internal double? RemainingSeconds { get; }

        internal KoffeeFlightExperiment(StationExperimentRuntimeSnapshot runtime)
        {
            Runtime = runtime;
            string id = runtime.Module.experimentID;
            Title = Localizer.Format(string.IsNullOrWhiteSpace(id) ? "#Koffee_Selector_Unconfigured" :
                ResearchAndDevelopment.GetExperiment(id)?.experimentTitle ?? id);
            HasPendingResult = runtime.Module.GetScienceCount() > 0;
            ExperimentContainerState? state = runtime.State;
            if (state == null || runtime.Definition == null ||
                runtime.PauseReason == StationExperimentPauseReason.IncompatibleState)
                Group = KoffeeFlightExperimentGroup.Unknown;
            else if (state.Status == ExperimentContainerStatus.Finalized ||
                state.Status == ExperimentContainerStatus.Spent || runtime.WorkComplete)
                Group = KoffeeFlightExperimentGroup.Completed;
            else if (state.Status == ExperimentContainerStatus.Available)
                Group = KoffeeFlightExperimentGroup.Ready;
            else if (state.Status == ExperimentContainerStatus.Running)
                Group = runtime.PauseReason == StationExperimentPauseReason.None ?
                    KoffeeFlightExperimentGroup.Running : KoffeeFlightExperimentGroup.Stopped;
            else Group = KoffeeFlightExperimentGroup.Unknown;

            if (runtime.RequiredPoints > 0 && ExperimentContainerState.Finite(runtime.RequiredPoints) &&
                ExperimentContainerState.Finite(runtime.AccumulatedPoints))
                Progress = Math.Max(0, Math.Min(1, runtime.AccumulatedPoints / runtime.RequiredPoints));
            // Completed containers never carry an active countdown, including absent workers.
            double? remaining = runtime.Research.RemainingSeconds;
            if ((Group == KoffeeFlightExperimentGroup.Running || Group == KoffeeFlightExperimentGroup.Stopped ||
                Group == KoffeeFlightExperimentGroup.Ready) && remaining.HasValue && remaining.Value >= 0 &&
                ExperimentContainerState.Finite(remaining.Value)) RemainingSeconds = remaining;

            string pause = StationExperimentEquipment.PauseText(runtime.PauseReason, runtime.Definition);
            Status = Group == KoffeeFlightExperimentGroup.Stopped ? pause :
                Localizer.Format("#Koffee_Flight_State_" + (Group == KoffeeFlightExperimentGroup.Completed ?
                    (state!.Status == ExperimentContainerStatus.Spent ? "Spent" :
                    state.Status == ExperimentContainerStatus.Finalized ? "Finalized" : "WorkComplete") : Group.ToString()));
            Attention = HasPendingResult ? Localizer.Format("#Koffee_Flight_ResultPending") :
                Group == KoffeeFlightExperimentGroup.Completed && state!.Status == ExperimentContainerStatus.Running ?
                    Localizer.Format("#Koffee_Flight_AwaitingResult") :
                Group != KoffeeFlightExperimentGroup.Completed &&
                    runtime.PauseReason != StationExperimentPauseReason.None &&
                    runtime.PauseReason != StationExperimentPauseReason.NotStarted ? pause : "";
        }

        internal static string Body(string name) => string.IsNullOrWhiteSpace(name) ? KoffeeResearchDisplay.Unknown :
            TelescopeBodyNames.Display(FlightGlobals.GetBodyByName(name), name);

        internal static string Situation(string value) =>
            Enum.TryParse(value, out ExperimentSituations situation) && Enum.IsDefined(typeof(ExperimentSituations), situation) ?
                Localizer.Format(situation == ExperimentSituations.SrfSplashed ? "#Koffee_Flight_Situation_SrfSplashed" :
                    "#Koffee_Environment_Situation_" + situation) : KoffeeResearchDisplay.Unknown;
    }

    internal sealed class KoffeeFlightNavigation
    {
        internal KoffeeFlightTab Tab { get; set; }
        internal ModuleKoffeeExperiment? SelectedExperiment { get; private set; }
        internal string SelectedScientist { get; set; } = "";
        internal bool SupplyOpen, PersonnelOpen, AffinitiesOpen;
        internal bool ResearchOpen = true, ConditionsOpen = true;
        private Guid vesselId;

        // Return true only for a changed vessel; refreshes of the same ship preserve navigation.
        internal bool Reconcile(KoffeeStatusSnapshot snapshot)
        {
            bool changed = vesselId != snapshot.VesselId;
            if (changed)
            {
                vesselId = snapshot.VesselId;
                SelectedExperiment = null;
                SelectedScientist = "";
                SupplyOpen = PersonnelOpen = AffinitiesOpen = false;
                ResearchOpen = ConditionsOpen = true;
            }
            if (!snapshot.Experiments.Any(e => ReferenceEquals(e.Module, SelectedExperiment)))
                SelectedExperiment = null;
            if (!snapshot.Scientists.Any(s => s.Name == SelectedScientist))
            {
                SelectedScientist = snapshot.Scientists.FirstOrDefault()?.Name ?? "";
                AffinitiesOpen = false;
            }
            return changed;
        }

        internal void SelectExperiment(ModuleKoffeeExperiment module, bool fromAttention = false)
        {
            SelectedExperiment = !fromAttention && ReferenceEquals(SelectedExperiment, module) ? null : module;
            Tab = KoffeeFlightTab.Experiments;
            ResearchOpen = ConditionsOpen = true;
        }
    }
}

using System.Linq;

namespace ShoAerospace.Koffee
{
    internal sealed class StationResearchPresentation
    {
        internal ScienceCategory? Category { get; }
        internal ScientistSpecialtyProfile? Profile { get; }
        internal int Affinity { get; }
        internal int Level { get; }
        internal double RequiredPoints { get; }
        internal double AccumulatedPoints { get; }
        internal double RatePerMinute { get; }
        internal double? RemainingSeconds { get; }
        internal bool Complete { get; }

        internal StationResearchPresentation(ModuleKoffeeExperiment module,
            StationExperimentDefinition? definition, ExperimentContainerState? state,
            ProtoCrewMember? candidate = null)
        {
            if (state == null || definition == null || module.HasIncompatibleState) return;
            bool started = state.Status != ExperimentContainerStatus.Available;
            Category = started ? state.ResearchCategory : definition.ScienceCategory;
            if (!Category.HasValue || Category != definition.ScienceCategory) return;
            AccumulatedPoints = state.AccumulatedResearchPoints;
            if (started) RequiredPoints = state.ResearchPointsRequired;
            else RequiredPoints = module.ResearchBudget(definition);
            if (RequiredPoints <= 0) return;
            Complete = started && state.WorkComplete();
            if (Complete) RemainingSeconds = 0;
            ProtoCrewMember? worker = started ? module.vessel?.GetVesselCrew()
                .FirstOrDefault(c => c.name == state.AssignedKerbalName) : candidate ?? module.vessel?.GetVesselCrew()
                .FirstOrDefault(c => c.name == state.AssignedKerbalName);
            if (worker == null || worker.trait != ScientistSpecialtyProfile.ProfessionId ||
                worker.experienceLevel < 0 || worker.experienceLevel > 5 ||
                !KerbalSpecialtyScenario.TryGetScientistProfile(worker.name, out var profile)) return;
            int affinity = profile.Score(Category.Value);
            if (started && affinity != state.ResearchAffinity) return;
            Profile = profile;
            Affinity = affinity;
            Level = worker.experienceLevel;
            RatePerMinute = KoffeeResearchRates.RatePerMinute(affinity, Level);
            RemainingSeconds = Complete ? 0 : StationResearchMath.RemainingSeconds(
                RequiredPoints, AccumulatedPoints, KoffeeResearchRates.RatePerHour(affinity, Level));
        }
    }
}

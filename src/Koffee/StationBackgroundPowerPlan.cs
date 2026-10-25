using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal sealed class StationBackgroundWork
    {
        internal Guid ContainerId;
        internal Guid RunId;
        internal uint LaboratoryFlightId;
        internal double RemainingSeconds;
        internal bool SharedStockLaboratory;
    }

    internal sealed class StationBackgroundPowerPlan
    {
        internal readonly List<StationBackgroundWork> Work = new List<StationBackgroundWork>();
        internal readonly Dictionary<uint, double> Consumers = new Dictionary<uint, double>();
        internal double NextUT = double.PositiveInfinity;
        internal double Rate => Consumers.Values.Sum();
        internal void Add(ProtoPartSnapshot? part, bool laboratory = false)
        {
            if (part == null) return;
            var consumer = part.partPrefab?.Modules?.OfType<ModuleKoffeePowerConsumer>().FirstOrDefault();
            double rate = laboratory ? consumer?.laboratoryElectricChargePerSecond ?? 0 : consumer?.electricChargePerSecond ?? 0;
            if (!ExperimentContainerState.Finite(rate) || rate < 0) throw new InvalidOperationException("Invalid background electrical tariff.");
            Consumers[part.persistentId] = Math.Max(Consumers.TryGetValue(part.persistentId, out double previous) ? previous : 0, rate);
        }
    }

    internal static partial class StationExperimentBackground
    {
        // Called at a BRP changepoint. Temporary cup reservations are rolled back
        // in reverse order; only the post-solver activation can commit them.
        internal static StationBackgroundPowerPlan PowerPlan(Vessel vessel, double now,
            List<IResourceAccount> sources, Func<uint, bool> stockLaboratory)
        {
            var result = new StationBackgroundPowerPlan { NextUT = KoffeeResearchRates.NextBoundary(now) };
            var parts = vessel.protoVessel?.protoPartSnapshots?.Where(p => p != null).ToList();
            if (parts == null) return result;
            var labs = parts.Select(ReadLab).Where(l => l != null).Cast<BackgroundLab>().ToList();
            var experiments = parts.Select(ReadExperiment).Where(e => e != null).Cast<BackgroundExperiment>()
                .OrderBy(e => e.State.StartUT ?? double.MaxValue).ThenBy(e => e.State.ContainerId).ToList();
            var slots = Allocate(experiments, labs);
            var manager = ExperimentManagerService.SelectSnapshots(parts, now, out _);
            if (manager == null) return result;
            string body = StationExperimentRuntime.CurrentBody(vessel), situation = StationExperimentRuntime.CurrentSituation(vessel);
            var equipment = SnapshotEquipment(parts);
            var session = new StationKoffeeSession();
            var reservations = new List<StationKoffeeSession.Transaction>();
            var sharedParts = new HashSet<uint>();
            try
            {
                foreach (var experiment in experiments)
                {
                    var state = experiment.State;
                    var lab = labs.FirstOrDefault(l => l.PersistentId == state.AssignedLaboratoryPersistentId);
                    var scientist = lab?.Part.protoModuleCrew?.FirstOrDefault(c => c?.name == state.AssignedKerbalName);
                    if (!Eligible(experiment, lab, scientist, slots, manager, vessel, body, situation, equipment) ||
                        state.ResearchCategory != experiment.Definition.ScienceCategory || state.WorkComplete()) continue;
                    if (!KerbalSpecialtyScenario.TryGetScientistProfile(scientist!.name, out var profile) ||
                        profile.Score(state.ResearchCategory!.Value) != state.ResearchAffinity) continue;
                    var engineer = session.Begin(manager.EngineerName, sources); reservations.Add(engineer);
                    var worker = session.Begin(state.AssignedKerbalName, sources); reservations.Add(worker);
                    if (!engineer.EnsureTeam(worker, now)) continue;
                    double remaining = StationResearchMath.RemainingSeconds(state.ResearchPointsRequired,
                        state.AccumulatedResearchPoints, KoffeeResearchRates.RatePerHourAt(state.ResearchAffinity, scientist!.experienceLevel, now));
                    if (!(remaining > 0)) continue;
                    bool shared = stockLaboratory(lab!.Part.flightID);
                    result.Work.Add(new StationBackgroundWork { ContainerId = state.ContainerId, RunId = state.RunId!.Value,
                        LaboratoryFlightId = lab.Part.flightID, RemainingSeconds = remaining, SharedStockLaboratory = shared });
                    result.Add(parts.FirstOrDefault(p => p.persistentId == manager.Id));
                    result.Add(experiment.Part);
                    AvailablePart? required = StationExperimentCatalogLoader.EquipmentPart(experiment.Definition.RequiredPartName);
                    result.Add(parts.Where(p => !ReferenceEquals(p, experiment.Part) && required != null &&
                        ReferenceEquals(p.partInfo ?? p.partPrefab?.partInfo, required)).OrderBy(p => p.persistentId).ThenBy(p => p.flightID).FirstOrDefault());
                    if (!shared) result.Add(lab.Part, true);
                    else sharedParts.Add(lab.Part.persistentId);
                    foreach (string name in new[] { manager.EngineerName, state.AssignedKerbalName })
                        if (KerbalKoffeeScenario.Store.TryGetStart(name, out double activation))
                            result.NextUT = Math.Min(result.NextUT, activation + KerbalWorkState.KaffeinatedDuration);
                }
            }
            finally { for (int i = reservations.Count - 1; i >= 0; i--) reservations[i].Dispose(); }
            foreach (uint part in sharedParts) result.Consumers.Remove(part);
            return result;
        }

        internal static void ActivatePoweredTeam(Vessel vessel, double now, List<IResourceAccount> sources,
            IReadOnlyCollection<Guid> powered)
        {
            var parts = vessel.protoVessel.protoPartSnapshots;
            var manager = ExperimentManagerService.SelectSnapshots(parts, now, out _);
            if (manager == null) return;
            var session = new StationKoffeeSession();
            foreach (var experiment in parts.Select(ReadExperiment).Where(e => e != null).Cast<BackgroundExperiment>()
                .OrderBy(e => e.State.StartUT ?? double.MaxValue).ThenBy(e => e.State.ContainerId))
            {
                if (!powered.Contains(experiment.State.ContainerId)) continue;
                using (var engineer = session.Begin(manager.EngineerName, sources))
                using (var scientist = session.Begin(experiment.State.AssignedKerbalName, sources))
                    if (engineer.EnsureTeam(scientist, now)) { engineer.Commit(); scientist.Commit(); }
            }
        }

        private static StationEquipmentSnapshot SnapshotEquipment(IEnumerable<ProtoPartSnapshot> parts)
        {
            var snapshot = new StationEquipmentSnapshot();
            foreach (var part in parts) snapshot.Add(part.partInfo ?? part.partPrefab?.partInfo, part, part.persistentId);
            return snapshot;
        }

        private static bool Eligible(BackgroundExperiment experiment, BackgroundLab? lab, ProtoCrewMember? scientist,
            IReadOnlyDictionary<Guid, StationSlotAllocation> slots, ExperimentManagerContext? manager,
            Vessel vessel, string body, string situation, StationEquipmentSnapshot equipment)
        {
            var state = experiment.State;
            var definition = experiment.Definition;
            var science = ResearchAndDevelopment.GetExperiment(definition.Id);
            return state.Status == ExperimentContainerStatus.Running && !ManuallyPaused(experiment.Part) &&
                scientist != null && StationExperimentRuntime.ValidScientist(scientist, definition) && manager != null &&
                lab != null && lab.Supports(definition.LabCapability) && slots.TryGetValue(state.ContainerId, out var slot) &&
                slot.LaboratoryId == state.AssignedLaboratoryPersistentId && slot.Slot == state.AssignedLaboratorySlot &&
                definition.AllowsBody(body) && science != null && StationSituationRules.Allowed(science.situationMask, situation) &&
                StationExperimentEnvironment.Evaluate(definition, state, vessel, situation, experiment.Part, equipment) == StationExperimentPauseReason.None;
        }

        private static bool ManuallyPaused(ProtoPartSnapshot part) => part.modules.Any(module =>
            (module.moduleName == nameof(ModuleKoffeeExperiment) || module.moduleValues?.GetValue("name") == nameof(ModuleKoffeeExperiment)) &&
            bool.TryParse(module.moduleValues?.GetValue(nameof(ModuleKoffeeExperiment.manuallyPaused)), out bool paused) && paused);
    }
}

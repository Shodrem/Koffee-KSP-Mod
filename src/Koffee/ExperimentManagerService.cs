using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal sealed class ExperimentManagerContext
    {
        internal uint Id;
        internal string EngineerName = "";
        internal Part? Part;
        internal double ValidSince;
    }

    internal static class ExperimentManagerService
    {
        internal static bool ValidEngineer(ProtoCrewMember? crew) => crew != null &&
            StationPersonnelRules.Valid(crew.name, crew.trait, crew.experienceLevel, "Engineer", 0);

        internal static IEnumerable<ModuleKoffeeExperimentManager> Managers(Vessel vessel) =>
            vessel.parts.Where(part => part != null && part.State != PartStates.DEAD)
                .SelectMany(part => part.Modules.OfType<ModuleKoffeeExperimentManager>())
                .OrderBy(manager => manager.part.persistentId);

        internal static string Signature(IEnumerable<uint> parts, IEnumerable<ProtoCrewMember> crew,
            IEnumerable<string> assignments) => string.Join(",", parts.OrderBy(id => id)) + "|" +
            string.Join(",", crew.Where(member => member != null)
                .Select(member => member.name + ":" + member.trait).OrderBy(name => name, StringComparer.Ordinal)) +
            "|" + string.Join(",", assignments);

        internal static ExperimentManagerContext? Select(Vessel vessel, double now,
            bool update, out StationExperimentPauseReason reason, bool readOnly = false)
        {
            ModuleKoffeeExperimentManager[] managers = Managers(vessel).ToArray();
            ProtoCrewMember[] crew = vessel.GetVesselCrew().Where(member => member != null).ToArray();
            string signature = Signature(vessel.parts.Where(p => p.State != PartStates.DEAD)
                .Select(p => p.persistentId), crew,
                managers.Select(m => m.part.persistentId + ":" + m.assignedEngineer)
                    .Concat(vessel.parts.SelectMany(p => p.protoModuleCrew.Where(c => c != null).Select(c => p.persistentId + ":" + c.name))
                        .OrderBy(value => value, StringComparer.Ordinal)));
            reason = managers.Length == 0 ? StationExperimentPauseReason.NoManager :
                StationExperimentPauseReason.NoEngineerAssigned;
            foreach (ModuleKoffeeExperimentManager manager in managers)
            {
                if (update && manager.topologySignature != signature)
                {
                    manager.topologySignature = signature;
                    manager.validSinceUT = now;
                }
            }
            double workUT = vessel.parts.SelectMany(p => p.Modules.OfType<ModuleKoffeeExperiment>())
                .Where(e => e.State?.Status == ExperimentContainerStatus.Running && e.State.CheckpointVerified)
                .Select(e => e.State!.LastCheckpointUT ?? now).DefaultIfEmpty(now).Min();
            var assignedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModuleKoffeeExperimentManager manager in managers)
            {
                if (string.IsNullOrEmpty(manager.assignedEngineer)) continue;
                if (!assignedNames.Add(manager.assignedEngineer)) continue;
                ProtoCrewMember? engineer = crew.FirstOrDefault(c => c.name == manager.assignedEngineer);
                if (!ValidEngineer(engineer))
                { reason = StationExperimentPauseReason.EngineerAbsentOrInvalid; continue; }
                if (!(readOnly ? EngineeringRenewalCoordinator.CaptureWindows(engineer!) :
                    EngineeringRenewalCoordinator.Windows(engineer!)).Any(w => w.Contains(workUT) || (w.StartUT < now && w.EndUT > workUT)) &&
                    !KerbalWorkState.IsKaffeinatedAt(engineer!, now) &&
                    KoffeeSources.Available(manager.part) < KoffeeSettingsLoader.Current.ActivationCost)
                { reason = StationExperimentPauseReason.EngineerNeedsKoffee; continue; }
                bool switched = managers.Any(m => m.activeManagerId != manager.part.persistentId);
                if (update && switched)
                {
                    manager.validSinceUT = now;
                    foreach (var entry in managers) entry.activeManagerId = manager.part.persistentId;
                }
                reason = StationExperimentPauseReason.None;
                return new ExperimentManagerContext { Id = manager.part.persistentId,
                    EngineerName = manager.assignedEngineer, Part = manager.part,
                    ValidSince = manager.topologySignature == signature && (update || !switched) ? manager.validSinceUT : now };
            }
            return null;
        }

        internal static ExperimentManagerContext? SelectSnapshots(List<ProtoPartSnapshot> parts,
            double now, out StationExperimentPauseReason reason)
        {
            var managers = parts.OrderBy(p => p.persistentId).SelectMany(part =>
                part.modules.Where(module => module != null && module.moduleValues != null &&
                    module.moduleName == nameof(ModuleKoffeeExperimentManager))
                    .Select(module => new { Part = part, Node = module.moduleValues })).ToArray();
            ProtoCrewMember[] crew = parts.SelectMany(p => p.protoModuleCrew ??
                new List<ProtoCrewMember>()).Where(c => c != null).ToArray();
            string signature = Signature(parts.Select(p => p.persistentId), crew,
                managers.Select(m => m.Part.persistentId + ":" + m.Node.GetValue("assignedEngineer"))
                    .Concat(parts.SelectMany(p => p.protoModuleCrew.Where(c => c != null).Select(c => p.persistentId + ":" + c.name))
                        .OrderBy(value => value, StringComparer.Ordinal)));
            reason = managers.Length == 0 ? StationExperimentPauseReason.NoManager :
                StationExperimentPauseReason.NoEngineerAssigned;
            foreach (var manager in managers)
            {
                if (manager.Node.GetValue("topologySignature") != signature)
                {
                    manager.Node.SetValue("topologySignature", signature, true);
                    manager.Node.SetValue("validSinceUT", now.ToString("R", CultureInfo.InvariantCulture), true);
                }
            }
            var assignedNames = new HashSet<string>(StringComparer.Ordinal);
            double workUT = parts.SelectMany(p => p.modules)
                .Where(m => m.moduleName == nameof(ModuleKoffeeExperiment) &&
                    m.moduleValues.GetValue(KoffeeIdentifiers.ContainerStatusValue) == "Running" &&
                    string.Equals(m.moduleValues.GetValue(KoffeeIdentifiers.CheckpointVerifiedValue), "True", StringComparison.OrdinalIgnoreCase))
                .Select(m => double.TryParse(m.moduleValues.GetValue(KoffeeIdentifiers.LastCheckpointUtValue),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double checkpoint) ? checkpoint : now)
                .DefaultIfEmpty(now).Min();
            double cups = parts.SelectMany(p => p.resources).Where(r =>
                r.resourceName == KoffeeIdentifiers.ResourceName).Sum(r => ResourcePayment.WholeCups(r.amount, r.flowState));
            foreach (var manager in managers)
            {
                string name = manager.Node.GetValue("assignedEngineer") ?? "";
                if (string.IsNullOrEmpty(name)) continue;
                if (!assignedNames.Add(name)) continue;
                if (!ValidEngineer(crew.FirstOrDefault(c => c.name == name)))
                { reason = StationExperimentPauseReason.EngineerAbsentOrInvalid; continue; }
                bool activeAtWork = KerbalKoffeeScenario.Store.TryGetStart(name, out double activation) &&
                    (new KaffeinatedWindow(activation, activation + KerbalWorkState.KaffeinatedDuration).Contains(workUT) ||
                     new KaffeinatedWindow(activation, activation + KerbalWorkState.KaffeinatedDuration).Contains(now) ||
                     (activation < now && activation + KerbalWorkState.KaffeinatedDuration > workUT));
                if (!activeAtWork && cups < KoffeeSettingsLoader.Current.ActivationCost)
                { reason = StationExperimentPauseReason.EngineerNeedsKoffee; continue; }
                if (managers.Any(m => m.Node.GetValue("activeManagerId") != manager.Part.persistentId.ToString(CultureInfo.InvariantCulture)))
                {
                    manager.Node.SetValue("validSinceUT", now.ToString("R", CultureInfo.InvariantCulture), true);
                    foreach (var entry in managers)
                        entry.Node.SetValue("activeManagerId", manager.Part.persistentId.ToString(CultureInfo.InvariantCulture), true);
                }
                double.TryParse(manager.Node.GetValue("validSinceUT"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double since);
                if (!ExperimentContainerState.Finite(since)) since = now;
                reason = StationExperimentPauseReason.None;
                return new ExperimentManagerContext { Id = manager.Part.persistentId,
                    EngineerName = name, ValidSince = since };
            }
            return null;
        }
    }
}

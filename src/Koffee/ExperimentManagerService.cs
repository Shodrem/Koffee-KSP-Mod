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
            if (DeadlyScenario.Active(vessel))
            { reason = StationExperimentPauseReason.ManagerOff; return null; }
            if (managers.Length == 0)
            {
                reason = StationExperimentPauseReason.NoManager;
                return null;
            }
            ProtoCrewMember[] crew = vessel.GetVesselCrew().Where(member => member != null).ToArray();
            string signature = Signature(vessel.parts.Where(p => p.State != PartStates.DEAD)
                .Select(p => p.persistentId), crew,
                managers.Select(m => m.part.persistentId + ":" + m.assignedEngineer)
                    .Concat(vessel.parts.SelectMany(p => p.protoModuleCrew.Where(c => c != null).Select(c => p.persistentId + ":" + c.name))
                        .OrderBy(value => value, StringComparer.Ordinal)));
            reason = StationExperimentPauseReason.NoEngineerAssigned;
            foreach (ModuleKoffeeExperimentManager manager in managers)
            {
                if (update && manager.topologySignature != signature)
                {
                    manager.topologySignature = signature;
                    manager.validSinceUT = now;
                }
            }
            var assignedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModuleKoffeeExperimentManager manager in managers)
            {
                if (!manager.managerEnabled) { reason = StationExperimentPauseReason.ManagerOff; continue; }
                if (string.IsNullOrEmpty(manager.assignedEngineer)) continue;
                if (!assignedNames.Add(manager.assignedEngineer)) continue;
                ProtoCrewMember? engineer = crew.FirstOrDefault(c => c.name == manager.assignedEngineer);
                if (!ValidEngineer(engineer))
                { reason = StationExperimentPauseReason.EngineerAbsentOrInvalid; continue; }
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
            double now, out StationExperimentPauseReason reason, bool update = true)
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
                if (update && manager.Node.GetValue("topologySignature") != signature)
                {
                    manager.Node.SetValue("topologySignature", signature, true);
                    manager.Node.SetValue("validSinceUT", now.ToString("R", CultureInfo.InvariantCulture), true);
                }
            }
            var assignedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var manager in managers)
            {
                if (bool.TryParse(manager.Node.GetValue("managerEnabled"), out bool on) && !on)
                { reason = StationExperimentPauseReason.ManagerOff; continue; }
                string name = manager.Node.GetValue("assignedEngineer") ?? "";
                if (string.IsNullOrEmpty(name)) continue;
                if (!assignedNames.Add(name)) continue;
                if (!ValidEngineer(crew.FirstOrDefault(c => c.name == name)))
                { reason = StationExperimentPauseReason.EngineerAbsentOrInvalid; continue; }
                if (update && managers.Any(m => m.Node.GetValue("activeManagerId") != manager.Part.persistentId.ToString(CultureInfo.InvariantCulture)))
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

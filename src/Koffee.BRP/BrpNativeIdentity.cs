using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BackgroundResourceProcessing;
using UnityEngine;

namespace ShoAerospace.Koffee.Brp
{
    internal static class BrpNativeIdentity
    {
        private static Game? game;
        private static readonly HashSet<string> warnings = new HashSet<string>(StringComparer.Ordinal);

        // KSP can snapshot PartModules before BRP records converters and calls
        // GetPersistentId. Copy the ID BRP has already assigned into that exact
        // module's snapshot. Otherwise ApplyInventories cannot find the module
        // on reload and its native OnRestore never rebases lastUpdateTime.
        // This does not generate IDs, integrate work or change stock timing.
        internal static void Preserve(BackgroundResourceProcessor processor)
        {
            if (!ReferenceEquals(game, HighLogic.CurrentGame)) { warnings.Clear(); game = HighLogic.CurrentGame; }
            var vessel = processor.Vessel;
            if (vessel?.protoVessel?.protoPartSnapshots == null) return;
            foreach (var converter in processor.Converters)
            {
                if (!converter.FlightId.HasValue || !converter.ModuleId.HasValue || converter.ModuleId.Value == 0) continue;
                var parts = vessel.parts.Where(p => p != null && p.State != PartStates.DEAD && p.flightID == converter.FlightId.Value).ToArray();
                if (parts.Length != 1) continue;
                var part = parts[0];
                var modules = part.Modules.Cast<PartModule>().Where(m => m.PersistentId == converter.ModuleId.Value).ToArray();
                if (modules.Length != 1 || !(modules[0] is BaseConverter live) ||
                    !EngineeringModuleFilter.Matches(live.GetType(), part.partInfo?.name ?? "", live.UseSpecialistBonus, live.ExperienceEffect)) continue;
                string type = live.GetType().Name;
                var liveModes = part.Modules.Cast<PartModule>().Where(m => m.GetType() == live.GetType()).ToArray();
                int ordinal = Array.IndexOf(liveModes, live);
                var snapshots = vessel.protoVessel.protoPartSnapshots.Where(p => p.flightID == part.flightID).ToArray();
                if (snapshots.Length != 1) continue;
                var savedModes = snapshots[0].modules.Where(m => m.moduleName == type).ToArray();
                if (ordinal < 0 || savedModes.Length != liveModes.Length) continue;
                var target = savedModes[ordinal];
                if (target?.moduleValues == null) continue;
                string[] saved = target.moduleValues.GetValues("persistentId");
                uint previous = 0;
                if (saved.Length > 1 || (saved.Length == 1 && !uint.TryParse(saved[0], NumberStyles.None, CultureInfo.InvariantCulture, out previous)) ||
                    (previous != 0 && previous != live.PersistentId))
                {
                    string identity = vessel.id + "/" + part.flightID + "/" + live.PersistentId;
                    if (warnings.Count < 2048 && warnings.Add(identity))
                        Debug.LogWarning("[Koffee][BRPIdentity] Conflicting saved module identity left untouched: " + identity);
                    continue;
                }
                if (previous == live.PersistentId) continue;
                var copy = target.moduleValues.CreateCopy();
                copy.SetValue("persistentId", live.PersistentId.ToString(CultureInfo.InvariantCulture), true);
                target.moduleValues = copy;
                if (EngineeringPowerDiagnostics.Enabled)
                    Debug.Log("[Koffee][BRPIdentity] Preserved recorded native module ID=" + live.PersistentId.ToString(CultureInfo.InvariantCulture) +
                        " vessel=" + vessel.id + " part=" + part.flightID.ToString(CultureInfo.InvariantCulture) + " module=" + type + "#" + ordinal.ToString(CultureInfo.InvariantCulture) +
                        "; stock/BRP restore retains authority over the processing timestamp.");
            }
        }
    }
}

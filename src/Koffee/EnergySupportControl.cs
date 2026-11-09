using System;
using System.Linq;

namespace ShoAerospace.Koffee
{
    // Restoration belongs to each physical module, so docking, splits and
    // proto snapshots retain exactly which devices this blackout switched off.
    internal static class EnergySupportControl
    {
        internal static bool Suppress(Vessel vessel)
        {
            bool changed = false;
            if (vessel.loaded)
            {
                foreach (var p in vessel.parts.Where(p => p.State != PartStates.DEAD))
                {
                    foreach (var s in p.Modules.OfType<ModuleKomfortService>().Where(s => s.service != "Emergency"))
                        if (s.serviceEnabled)
                        { s.energySupportRestore = true; s.SetServiceEnabled(false, coordinated: true); changed = true; }
                    foreach (var store in p.Modules.OfType<ModuleKoffeeScienceStore>())
                        if (store.managerEnabled)
                        { store.energySupportRestore = true; store.managerEnabled = false; changed = true; }
                    foreach (var m in p.Modules.OfType<ModuleKoffeeExperimentManager>())
                        if (m.managerEnabled)
                        { m.energySupportRestore = true; m.SetManagerEnabled(false, coordinated: true); changed = true; }
                }
            }
            else foreach (var p in vessel.protoVessel?.protoPartSnapshots ?? new System.Collections.Generic.List<ProtoPartSnapshot>())
            {
                var definitions = (p.partPrefab ?? p.partInfo?.partPrefab)?.Modules.OfType<ModuleKomfortService>().ToArray() ?? Array.Empty<ModuleKomfortService>();
                int index = 0;
                foreach (var s in p.modules.Where(m => m.moduleName == nameof(ModuleKomfortService)))
                {
                    var def = index < definitions.Length ? definitions[index] : null; index++;
                    if (def?.service == "Emergency") continue;
                    bool on = bool.TryParse(s.moduleValues.GetValue("serviceEnabled"), out bool enabled) ? enabled : def?.serviceEnabled == true;
                    if (on) { Off(s.moduleValues, "serviceEnabled"); changed = true; }
                }
                foreach (var m in p.modules.Where(m => m.moduleName == nameof(ModuleKoffeeExperimentManager) || m.moduleName == nameof(ModuleKoffeeScienceStore)))
                    if (!bool.TryParse(m.moduleValues.GetValue("managerEnabled"), out bool on) || on)
                    { Off(m.moduleValues, "managerEnabled"); changed = true; }
            }
            return changed;
        }
        private static void Off(ConfigNode n, string key)
        { n.SetValue("energySupportRestore", "True", true); n.SetValue(key, "False", true); }

        internal static bool Restore(Vessel vessel, double now)
        {
            bool changed = false;
            if (vessel.loaded)
            {
                foreach (var p in vessel.parts.Where(p => p.State != PartStates.DEAD))
                {
                    foreach (var s in p.Modules.OfType<ModuleKomfortService>().Where(s => s.energySupportRestore))
                    { changed = true; s.energySupportRestore = false; s.SetServiceEnabled(true, coordinated: true); }
                    foreach (var store in p.Modules.OfType<ModuleKoffeeScienceStore>().Where(s => s.energySupportRestore))
                    { changed = true; store.energySupportRestore = false; store.managerEnabled = true; }
                    foreach (var m in p.Modules.OfType<ModuleKoffeeExperimentManager>().Where(m => m.energySupportRestore))
                    { changed = true; m.energySupportRestore = false; m.SetManagerEnabled(true, coordinated: true); }
                }
            }
            else foreach (var p in vessel.protoVessel?.protoPartSnapshots ?? new System.Collections.Generic.List<ProtoPartSnapshot>())
                foreach (var m in p.modules.Where(m => m.moduleName == nameof(ModuleKomfortService) || m.moduleName == nameof(ModuleKoffeeExperimentManager) || m.moduleName == nameof(ModuleKoffeeScienceStore)))
                    if (bool.TryParse(m.moduleValues.GetValue("energySupportRestore"), out bool restore) && restore)
                    {
                        changed = true; m.moduleValues.SetValue("energySupportRestore", "False", true);
                        m.moduleValues.SetValue(m.moduleName == nameof(ModuleKomfortService) ? "serviceEnabled" : "managerEnabled", "True", true);
                        if (m.moduleName == nameof(ModuleKoffeeExperimentManager))
                        {
                            m.moduleValues.SetValue("topologySignature", "", true);
                            m.moduleValues.SetValue("validSinceUT", now.ToString("R", System.Globalization.CultureInfo.InvariantCulture), true);
                        }
                    }
            return changed;
        }
    }
}

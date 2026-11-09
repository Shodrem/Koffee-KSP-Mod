using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal static class EmergencySupport
    {
        internal sealed class Device
        {
            internal uint Id;
            internal double GenerationRate;
            internal bool NativeReactor;
            internal bool Enabled;
            internal Action<bool>? Switch;
        }
        internal static List<Device> Capture(Vessel vessel)
        {
            var result = new List<Device>();
            if (vessel.loaded)
            {
                foreach (var part in vessel.parts.Where(p => p.State != PartStates.DEAD))
                    foreach (var m in part.Modules.OfType<ModuleEmergencySupport>().Where(m => m.Configured))
                        result.Add(new Device { Id = part.persistentId, GenerationRate = m.electricChargePerSecond, NativeReactor = m.UsesNfeReactor, Enabled = m.Service!.serviceEnabled,
                            Switch = on =>
                            {
                                m.Service!.serviceEnabled = on;
                                if (m.UsesNfeReactor) EmergencyNfeReactor.Switch(part, on);
                                else
                                {
                                    var generator = part.Modules.OfType<ModuleGenerator>().Single();
                                    if (on) generator.Activate(); else generator.Shutdown();
                                }
                            } });
            }
            else
            {
                foreach (var part in vessel.protoVessel?.protoPartSnapshots ?? new List<ProtoPartSnapshot>())
                {
                    // Ordinary parts may have several Komfort services or generators.
                    // Only inspect emergency hardware, and reject ambiguous snapshots
                    // without interrupting survival processing for the whole fleet.
                    var modules = part.modules.Where(m => m.moduleName == nameof(ModuleEmergencySupport)).ToArray();
                    if (modules.Length != 1) continue;
                    var prefab = part.partPrefab ?? part.partInfo?.partPrefab;
                    var definitions = prefab?.Modules.OfType<ModuleEmergencySupport>().ToArray();
                    if (definitions?.Length != 1 || !definitions[0].Configured) continue;
                    var services = part.modules.Where(m => m.moduleName == nameof(ModuleKomfortService)).ToArray();
                    bool nuclear = definitions[0].UsesNfeReactor;
                    var generators = part.modules.Where(m => m.moduleName == (nuclear ? EmergencyNfeReactor.ModuleName : nameof(ModuleGenerator))).ToArray();
                    if (services.Length != 1 || generators.Length != 1) continue;
                    var module = modules[0];
                    var service = services[0];
                    var generator = generators[0];
                    result.Add(new Device { Id = part.persistentId,
                        GenerationRate = definitions[0].electricChargePerSecond, NativeReactor = nuclear,
                        Enabled = bool.TryParse(service.moduleValues.GetValue("serviceEnabled"), out bool enabled) && enabled,
                        Switch = on =>
                        {
                            service.moduleValues.SetValue("serviceEnabled", on.ToString(), true);
                            if (nuclear) EmergencyNfeReactor.SwitchSnapshot(generator.moduleValues, on, Planetarium.GetUniversalTime());
                            else generator.moduleValues.SetValue("generatorIsActive", on.ToString(), true);
                        } });
                }
            }
            return result.OrderBy(d => d.Id).ToList();
        }
    }
}

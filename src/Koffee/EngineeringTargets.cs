using System;
using System.Collections.Generic;

namespace ShoAerospace.Koffee
{
    internal enum EngineeringModuleFamily
    {
        ResourceHarvester,
        ResourceConverter
    }

    internal sealed class EngineeringTarget
    {
        internal string Name { get; }
        internal string PartName { get; }
        internal EngineeringModuleFamily ModuleFamily { get; }
        internal string ExperienceEffect { get; }

        internal EngineeringTarget(string name, string partName,
            EngineeringModuleFamily moduleFamily, string experienceEffect)
        {
            Name = name;
            PartName = partName;
            ModuleFamily = moduleFamily;
            ExperienceEffect = experienceEffect;
        }
    }

    internal sealed class EngineeringTargetRegistry
    {
        private readonly EngineeringTarget[] targets;

        internal int Count => targets.Length;
        internal static EngineeringTargetRegistry Empty { get; } =
            new EngineeringTargetRegistry(Array.Empty<EngineeringTarget>());

        private EngineeringTargetRegistry(EngineeringTarget[] targets)
        {
            this.targets = (EngineeringTarget[])targets.Clone();
        }

        internal bool Matches(EngineeringModuleFamily family, string partName,
            string experienceEffect)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                EngineeringTarget target = targets[i];
                if (target.ModuleFamily == family &&
                    string.Equals(target.PartName, partName, StringComparison.Ordinal) &&
                    string.Equals(target.ExperienceEffect, experienceEffect, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        internal static EngineeringTargetRegistry Parse(
            IReadOnlyList<IReadOnlyDictionary<string, string?>> nodes,
            out IReadOnlyList<string> warnings)
        {
            var messages = new List<string>();
            var accepted = new List<EngineeringTarget>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (nodes == null)
            {
                warnings = messages;
                return Empty;
            }

            for (int index = 0; index < nodes.Count; index++)
            {
                IReadOnlyDictionary<string, string?> node = nodes[index];
                string? name = Value(node, "name")?.Trim();
                string label = string.IsNullOrEmpty(name) ? "#" + index : "'" + name + "'";
                if (string.IsNullOrEmpty(name))
                {
                    messages.Add("Ignoring " + KoffeeIdentifiers.EngineeringTargetNode + " " + label +
                        ": field 'name' has invalid value '" + (Value(node, "name") ?? "<missing>") + "'.");
                    continue;
                }
                if (!names.Add(name!))
                {
                    messages.Add("Ignoring " + KoffeeIdentifiers.EngineeringTargetNode + " '" + name +
                        "': duplicate name.");
                    continue;
                }

                string? enabledText = Value(node, "enabled");
                bool enabled = true;
                if (enabledText != null && !bool.TryParse(enabledText.Trim(), out enabled))
                {
                    messages.Add("Ignoring " + KoffeeIdentifiers.EngineeringTargetNode + " '" + name +
                        "': field 'enabled' has invalid value '" + enabledText + "'.");
                    continue;
                }
                if (!enabled) continue;

                string? part = Value(node, "part")?.Trim();
                if (string.IsNullOrEmpty(part))
                {
                    messages.Add(Invalid(name!, "part", Value(node, "part")));
                    continue;
                }
                string? module = Value(node, "module")?.Trim();
                if (!TryModule(module, out EngineeringModuleFamily family))
                {
                    messages.Add("Ignoring engineering target '" + name +
                        "': unsupported module type in field 'module': '" +
                        (module ?? "<missing>") + "'.");
                    continue;
                }
                string? effect = Value(node, "experienceEffect")?.Trim();
                if (string.IsNullOrEmpty(effect))
                {
                    messages.Add(Invalid(name!, "experienceEffect", Value(node, "experienceEffect")));
                    continue;
                }
                accepted.Add(new EngineeringTarget(name!, part!, family, effect!));
            }

            warnings = messages;
            return accepted.Count == 0 ? Empty :
                new EngineeringTargetRegistry(accepted.ToArray());
        }

        private static string Invalid(string name, string field, string? value) =>
            "Ignoring " + KoffeeIdentifiers.EngineeringTargetNode + " '" + name +
            "': field '" + field + "' has invalid value '" + (value ?? "<missing>") + "'.";

        private static bool TryModule(string? value, out EngineeringModuleFamily family)
        {
            if (value == "ModuleResourceHarvester")
            {
                family = EngineeringModuleFamily.ResourceHarvester;
                return true;
            }
            if (value == "ModuleResourceConverter")
            {
                family = EngineeringModuleFamily.ResourceConverter;
                return true;
            }
            family = default;
            return false;
        }

        private static string? Value(IReadOnlyDictionary<string, string?> node, string key)
        {
            if (node.TryGetValue(key, out string? value)) return value;
            foreach (KeyValuePair<string, string?> entry in node)
                if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)) return entry.Value;
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace ShoAerospace.Koffee
{
    internal static class EngineerAffinityProfileCodec
    {
        internal const string NodeName = "ENGINEERING_PROFILE";

        internal static bool TryRead(ConfigNode node, out EngineerAffinityRecord record,
            out string error)
        {
            record = default;
            error = "";
            if (!Single(node, "kerbalName", out string name) || string.IsNullOrEmpty(name) ||
                !Single(node, "professionId", out string profession) ||
                profession != EngineerAffinityProfile.ProfessionId ||
                !Integer(node, "schemaVersion", out int schema) || schema != EngineerAffinityProfile.CurrentSchemaVersion ||
                !Integer(node, "generatorVersion", out int generator) || generator < 1 ||
                !Single(node, "primaryCategory", out string primaryId) ||
                !EngineeringAffinities.TryParse(primaryId, out EngineeringAffinity primary) ||
                !Single(node, "secondaryCategory", out string secondaryId) ||
                !EngineeringAffinities.TryParse(secondaryId, out EngineeringAffinity secondary))
            {
                error = "Invalid or unsupported specialty profile identity, version or categories.";
                return false;
            }
            var scores = new Dictionary<EngineeringAffinity, int>();
            foreach (ConfigNode affinity in node.GetNodes("AFFINITY"))
            {
                if (!Single(affinity, "category", out string id) ||
                    !EngineeringAffinities.TryParse(id, out EngineeringAffinity category) || scores.ContainsKey(category) ||
                    !Integer(affinity, "score", out int score) || !Single(affinity, "tier", out string tier) ||
                    tier != (category == primary ? "High" : category == secondary ? "Medium" : "Low"))
                {
                    error = "Invalid, duplicate or inconsistent affinity entry.";
                    return false;
                }
                scores.Add(category, score);
            }
            try
            {
                record = new EngineerAffinityRecord(name, new EngineerAffinityProfile(primary, secondary, scores));
                return true;
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        internal static ConfigNode Write(EngineerAffinityRecord record)
        {
            var node = new ConfigNode(NodeName);
            node.AddValue("schemaVersion", EngineerAffinityProfile.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));
            node.AddValue("kerbalName", record.KerbalName);
            node.AddValue("professionId", EngineerAffinityProfile.ProfessionId);
            node.AddValue("generatorVersion", EngineerAffinityProfile.CurrentGeneratorVersion.ToString(CultureInfo.InvariantCulture));
            node.AddValue("primaryCategory", record.Profile.PrimaryCategory.ToString());
            node.AddValue("secondaryCategory", record.Profile.SecondaryCategory.ToString());
            foreach (EngineeringAffinity category in EngineeringAffinities.All)
            {
                ConfigNode affinity = node.AddNode("AFFINITY");
                affinity.AddValue("category", category.ToString());
                affinity.AddValue("tier", category == record.Profile.PrimaryCategory ? "High" :
                    category == record.Profile.SecondaryCategory ? "Medium" : "Low");
                affinity.AddValue("score", record.Profile.Score(category).ToString(CultureInfo.InvariantCulture));
            }
            return node;
        }

        private static bool Single(ConfigNode node, string key, out string value)
        {
            string[] values = node.GetValues(key);
            value = values.Length == 1 ? values[0] : "";
            return values.Length == 1;
        }

        private static bool Integer(ConfigNode node, string key, out int value)
        {
            value = 0;
            return Single(node, key, out string text) &&
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}

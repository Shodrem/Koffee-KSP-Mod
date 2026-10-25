using System;
using System.Collections.Generic;
using System.Globalization;

namespace ShoAerospace.Koffee
{
    internal static class ScientistSpecialtyProfileCodec
    {
        internal const string NodeName = "SPECIALTY_PROFILE";

        internal static bool TryRead(ConfigNode node, out ScientistSpecialtyRecord record,
            out string error)
        {
            record = default;
            error = "";
            if (!Single(node, "kerbalName", out string name) || string.IsNullOrEmpty(name) ||
                !Single(node, "professionId", out string profession) ||
                profession != ScientistSpecialtyProfile.ProfessionId ||
                !Integer(node, "schemaVersion", out int schema) || schema != ScientistSpecialtyProfile.CurrentSchemaVersion ||
                !Integer(node, "generatorVersion", out int generator) || generator < 1 ||
                !Single(node, "primaryCategory", out string primaryId) ||
                !ScienceCategories.TryParse(primaryId, out ScienceCategory primary) ||
                !Single(node, "secondaryCategory", out string secondaryId) ||
                !ScienceCategories.TryParse(secondaryId, out ScienceCategory secondary))
            {
                error = "Invalid or unsupported specialty profile identity, version or categories.";
                return false;
            }
            var scores = new Dictionary<ScienceCategory, int>();
            foreach (ConfigNode affinity in node.GetNodes("AFFINITY"))
            {
                if (!Single(affinity, "category", out string id) ||
                    !ScienceCategories.TryParse(id, out ScienceCategory category) || scores.ContainsKey(category) ||
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
                record = new ScientistSpecialtyRecord(name, new ScientistSpecialtyProfile(primary, secondary, scores));
                return true;
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        internal static ConfigNode Write(ScientistSpecialtyRecord record)
        {
            var node = new ConfigNode(NodeName);
            node.AddValue("schemaVersion", ScientistSpecialtyProfile.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));
            node.AddValue("kerbalName", record.KerbalName);
            node.AddValue("professionId", ScientistSpecialtyProfile.ProfessionId);
            node.AddValue("generatorVersion", ScientistSpecialtyProfile.CurrentGeneratorVersion.ToString(CultureInfo.InvariantCulture));
            node.AddValue("primaryCategory", record.Profile.PrimaryCategory.ToString());
            node.AddValue("secondaryCategory", record.Profile.SecondaryCategory.ToString());
            foreach (ScienceCategory category in ScienceCategories.All)
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

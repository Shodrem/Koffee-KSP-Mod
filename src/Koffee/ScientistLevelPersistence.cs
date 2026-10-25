using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal sealed class ScientistLevelPersistence
    {
        internal const string NodeName = "SCIENTIST_LEVEL_HISTORY";
        private readonly Dictionary<string, ScientistLevelHistory> histories =
            new Dictionary<string, ScientistLevelHistory>(StringComparer.Ordinal);
        private readonly HashSet<string> blocked = new HashSet<string>(StringComparer.Ordinal);
        private ConfigNode[] originals = Array.Empty<ConfigNode>();
        private bool unidentified;
        private bool loaded;

        internal IReadOnlyList<string> Load(ConfigNode root)
        {
            histories.Clear(); blocked.Clear(); unidentified = false; loaded = false;
            originals = root.GetNodes(NodeName).Select(node => node.CreateCopy()).ToArray();
            var diagnostics = new List<string>();
            foreach (var node in originals)
            {
                string[] names = node.GetValues("kerbalName");
                if (names.Length != 1 || string.IsNullOrEmpty(names[0]))
                {
                    foreach (string name in names.Where(name => !string.IsNullOrEmpty(name))) blocked.Add(name);
                    if (!names.Any(name => !string.IsNullOrEmpty(name))) unidentified = true;
                    diagnostics.Add("Ambiguous scientist level history identity; original retained.");
                    continue;
                }
                string identity = names[0];
                if (histories.ContainsKey(identity) || !TryRead(node, out var history))
                {
                    blocked.Add(identity);
                    diagnostics.Add("Invalid, duplicate or unsupported level history for '" + identity + "'; original retained.");
                }
                else histories.Add(identity, history);
            }
            foreach (string name in blocked) histories.Remove(name);
            loaded = true;
            return diagnostics;
        }

        // Only loading a snapshot can establish a different timeline. Ordinary
        // reversed observations remain invalid and cannot silently rewrite history.
        internal string? ObserveLoaded(string name, int level, double ut)
        {
            if (loaded && level >= 0 && level <= 5 && ScientistLevelHistory.Time(ut) &&
                histories.TryGetValue(name, out var history) && history.LastObservedUT > ut)
            {
                var past = history.Boundaries.TakeWhile(boundary => boundary.UT <= ut).ToList();
                if (past.Count == 0)
                    past.Add(new ScientistLevelBoundary(ut, level, ut));
                else
                {
                    var last = past[past.Count - 1];
                    // Keep a known gap even if its far boundary is in the
                    // discarded future and the restored roster has the old level.
                    double through = past.Count < history.Boundaries.Count
                        ? Math.Min(ut, history.Boundaries[past.Count].KnownThroughUT) : ut;
                    // The roster proves today's level, not when a missed change
                    // happened. Never backdate a mismatching loaded level.
                    if (last.Level != level) through = last.UT;
                    if (last.Level != level || through < ut)
                        past.Add(new ScientistLevelBoundary(ut, level, through));
                }
                histories[name] = new ScientistLevelHistory(past, ut);
                return "Level history for '" + name + "' extended beyond loaded game UT; future observations discarded.";
            }
            return Observe(name, level, ut, false);
        }

        internal string? Observe(string name, int level, double ut, bool effectiveEvent)
        {
            if (!loaded || string.IsNullOrEmpty(name) || blocked.Contains(name)) return null;
            if (!histories.TryGetValue(name, out var history))
            {
                if (unidentified) return null;
                if (level < 0 || level > 5 || !ScientistLevelHistory.Time(ut))
                    return "Invalid effective level or UT for '" + name + "'; no history created.";
                histories.Add(name, new ScientistLevelHistory(level, ut));
                return null; // No earlier rate can be inferred without a baseline.
            }
            var observation = history.Observe(level, ut, effectiveEvent);
            if (observation == ScientistLevelObservation.Gap)
                return "Level changed without a captured event for '" + name + "'; uncertain interval excluded.";
            if (observation == ScientistLevelObservation.Rejected)
                return "Invalid level or reversed UT for '" + name + "'; history left untouched.";
            return null;
        }

        internal bool TryGetSegments(string? name, double start, double end,
            out IReadOnlyList<ScientistLevelSegment> segments)
        {
            segments = Array.Empty<ScientistLevelSegment>();
            return loaded && name != null && histories.TryGetValue(name, out var history) &&
                history.TryGetSegments(start, end, out segments);
        }

        // A read-only query must not extend a stale cached level beyond the last
        // observation when stock has changed without a captured event.
        internal bool CanReadUntil(string name, int stockLevel, double end) =>
            histories.TryGetValue(name, out var history) &&
            (stockLevel == history.CurrentLevel || end <= history.LastObservedUT);

        internal void Save(ConfigNode root)
        {
            if (!loaded) throw new InvalidOperationException("Level histories must load before saving.");
            root.RemoveNodes(NodeName);
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var original in originals)
            {
                string name = original.GetValue("kerbalName");
                var copy = original.CreateCopy();
                if (name != null && histories.TryGetValue(name, out var history))
                {
                    Write(copy, name, history); written.Add(name);
                }
                root.AddNode(copy);
            }
            foreach (var pair in histories)
                if (!written.Contains(pair.Key))
                {
                    var node = new ConfigNode(NodeName); Write(node, pair.Key, pair.Value); root.AddNode(node);
                }
        }

        private static bool TryRead(ConfigNode node, out ScientistLevelHistory history)
        {
            history = null!;
            if (!Single(node, "schemaVersion", out string schema) || schema != "1" ||
                !Number(node, "lastObservedUT", out double observed)) return false;
            var entries = new List<ScientistLevelBoundary>();
            foreach (var change in node.GetNodes("LEVEL"))
            {
                if (!Number(change, "ut", out double ut) || !Number(change, "knownThroughUT", out double through) ||
                    !Single(change, "level", out string text) ||
                    !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int level)) return false;
                entries.Add(new ScientistLevelBoundary(ut, level, through));
            }
            try { history = new ScientistLevelHistory(entries, observed); return true; }
            catch (ArgumentException) { return false; }
        }

        private static void Write(ConfigNode node, string name, ScientistLevelHistory history)
        {
            node.SetValue("schemaVersion", "1", true); node.SetValue("kerbalName", name, true);
            node.SetValue("lastObservedUT", history.LastObservedUT.ToString("R", CultureInfo.InvariantCulture), true);
            // Keep metadata on both the enclosing node and existing boundaries.
            var previous = node.GetNodes("LEVEL"); node.RemoveNodes("LEVEL");
            for (int i = 0; i < history.Boundaries.Count; i++)
            {
                var boundary = history.Boundaries[i];
                // A replacement baseline must not inherit a discarded future
                // event's metadata merely because it occupies the same index.
                bool same = i < previous.Length && Number(previous[i], "ut", out double oldUT) &&
                    oldUT == boundary.UT && previous[i].GetValue("level") == boundary.Level.ToString(CultureInfo.InvariantCulture);
                var change = same ? previous[i].CreateCopy() : new ConfigNode("LEVEL");
                change.SetValue("ut", boundary.UT.ToString("R", CultureInfo.InvariantCulture), true);
                change.SetValue("level", boundary.Level.ToString(CultureInfo.InvariantCulture), true);
                change.SetValue("knownThroughUT", boundary.KnownThroughUT.ToString("R", CultureInfo.InvariantCulture), true);
                node.AddNode(change);
            }
        }

        private static bool Single(ConfigNode node, string key, out string text)
        {
            var values = node.GetValues(key); text = values.Length == 1 ? values[0] : "";
            return values.Length == 1;
        }
        private static bool Number(ConfigNode node, string key, out double number)
        {
            number = 0;
            return Single(node, key, out string text) &&
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }
    }
}

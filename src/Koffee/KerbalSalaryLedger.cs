using System;
using System.Collections.Generic;
using System.Globalization;

namespace ShoAerospace.Koffee
{
    internal sealed class KerbalSalaryEntry
    {
        internal readonly int DepartureLevel;
        internal readonly double StartUT, DailyRate;
        internal double LastUT, AccruedSeconds;
        internal bool Earning, Dead;
        internal KerbalSalaryEntry(int level, double start, double rate, bool earning)
        { DepartureLevel = level; StartUT = LastUT = start; DailyRate = rate; Earning = earning; }
        private double Seconds(double now) => AccruedSeconds + (Earning && !Dead ? Math.Max(0, now - LastUT) : 0);
        // Round the total active mission time once, never each observation or pause.
        internal double Amount(double now) => Dead ? 0 : Math.Ceiling(Seconds(now) / 21600d) * DailyRate;
        internal void Advance(double now)
        {
            if (!Valid(now) || now < LastUT) return;
            AccruedSeconds = Seconds(now); LastUT = now;
        }
        internal static bool Valid(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
    }

    internal sealed class KerbalSalaryLedger
    {
        internal readonly Dictionary<string, KerbalSalaryEntry> Entries = new Dictionary<string, KerbalSalaryEntry>(StringComparer.Ordinal);
        internal void Start(string name, int level, double now, double rate, bool enabled)
        {
            if (!KerbalSalaryEntry.Valid(now) || !KerbalSalaryEntry.Valid(rate) || string.IsNullOrEmpty(name)) return;
            if (Entries.ContainsKey(name)) return;
            Entries.Add(name, new KerbalSalaryEntry(level, now, rate, enabled));
        }
        internal void SetEnabled(bool enabled, double now)
        {
            foreach (var entry in Entries.Values) { entry.Advance(now); entry.Earning = enabled && !entry.Dead; }
        }
        internal bool Die(string name)
        {
            if (!Entries.TryGetValue(name, out var entry) || entry.Dead) return false;
            entry.Dead = true; entry.Earning = false; entry.AccruedSeconds = 0; return true;
        }
        internal void Rename(string oldName, string newName)
        {
            if (!Entries.TryGetValue(oldName, out var entry) || oldName == newName) return;
            if (Entries.ContainsKey(newName)) throw new InvalidOperationException("Ambiguous salary crew rename.");
            Entries.Remove(oldName); Entries.Add(newName, entry);
        }
        internal void Save(ConfigNode node)
        {
            node.RemoveNodes("SALARY");
            foreach (var pair in Entries)
            {
                var saved = node.AddNode("SALARY");
                saved.AddValue("name", pair.Key);
                saved.AddValue("departureLevel", pair.Value.DepartureLevel.ToString(CultureInfo.InvariantCulture));
                Number(saved, "startUT", pair.Value.StartUT); Number(saved, "lastUT", pair.Value.LastUT);
                Number(saved, "dailyRate", pair.Value.DailyRate);
                Number(saved, "elapsedSeconds", pair.Value.AccruedSeconds);
                // Keep the old proportional amount for older versions of the mod.
                Number(saved, "accrued", pair.Value.AccruedSeconds / 21600d * pair.Value.DailyRate);
                saved.AddValue("earning", pair.Value.Earning.ToString()); saved.AddValue("dead", pair.Value.Dead.ToString());
            }
        }
        internal void Load(ConfigNode node)
        {
            var loaded = new Dictionary<string, KerbalSalaryEntry>(StringComparer.Ordinal);
            foreach (var saved in node.GetNodes("SALARY"))
            {
                string name = saved.GetValue("name");
                if (string.IsNullOrEmpty(name) || loaded.ContainsKey(name) ||
                    !int.TryParse(saved.GetValue("departureLevel"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) || level < 0 ||
                    !bool.TryParse(saved.GetValue("earning"), out bool earning) || !bool.TryParse(saved.GetValue("dead"), out bool dead))
                    throw new InvalidOperationException("Invalid saved Koffee salary.");
                var entry = new KerbalSalaryEntry(level, Read(saved, "startUT"), Read(saved, "dailyRate"), earning);
                entry.LastUT = Read(saved, "lastUT"); entry.Dead = dead;
                double accrued = Read(saved, "accrued");
                entry.AccruedSeconds = saved.HasValue("elapsedSeconds") ? Read(saved, "elapsedSeconds")
                    : (entry.DailyRate > 0 ? accrued / entry.DailyRate * 21600d : 0);
                if (!KerbalSalaryEntry.Valid(entry.AccruedSeconds) || entry.LastUT < entry.StartUT ||
                    (dead && (earning || accrued != 0 || entry.AccruedSeconds != 0)))
                    throw new InvalidOperationException("Invalid saved Koffee salary interval.");
                loaded.Add(name, entry);
            }
            Entries.Clear(); foreach (var pair in loaded) Entries.Add(pair.Key, pair.Value);
        }
        private static void Number(ConfigNode node, string key, double value) => node.AddValue(key, value.ToString("R", CultureInfo.InvariantCulture));
        private static double Read(ConfigNode node, string key)
        {
            if (!double.TryParse(node.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !KerbalSalaryEntry.Valid(value))
                throw new InvalidOperationException("Invalid saved Koffee salary " + key);
            return value;
        }
    }
}

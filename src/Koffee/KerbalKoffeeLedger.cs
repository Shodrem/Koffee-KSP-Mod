using System;
using System.Collections.Generic;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal sealed class KerbalKoffeeLedger
    {
        private readonly Dictionary<string, KoffeeStateRecord> records =
            new Dictionary<string, KoffeeStateRecord>(StringComparer.Ordinal);
        private readonly HashSet<string> expiredObserved =
            new HashSet<string>(StringComparer.Ordinal);

        internal bool TryGetRemaining(string? name, double now,
            double duration, out double remaining, out bool expired)
        {
            expired = false;
            remaining = 0;
            if (!ValidName(name) ||
                !records.TryGetValue(name!, out KoffeeStateRecord record)) return false;
            double untilUT = record.StartUT + duration;
            if (!ValidDuration(duration) || !ValidStart(record.StartUT) ||
                !Finite(now) || now < record.StartUT) return false;
            if (!Finite(untilUT) || now >= untilUT)
            {
                expired = expiredObserved.Add(name!);
                return false;
            }
            expiredObserved.Remove(name!);
            remaining = untilUT - now;
            return true;
        }

        internal bool TryGetStart(string? name, out double startUT)
        {
            if (ValidName(name) && records.TryGetValue(name!,
                out KoffeeStateRecord record) && ValidStart(record.StartUT))
            {
                startUT = record.StartUT;
                return true;
            }
            startUT = 0;
            return false;
        }

        internal double GetUntil(string? name, double duration)
        {
            if (!ValidDuration(duration) ||
                !TryGetStart(name, out double startUT)) return 0;
            double untilUT = startUT + duration;
            return Finite(untilUT) ? untilUT : 0;
        }

        internal void SetStart(string? name, double startUT)
        {
            if (!ValidName(name))
                throw new ArgumentException("Kerbal name must not be null or empty.", nameof(name));
            if (!ValidStart(startUT)) throw new ArgumentOutOfRangeException(nameof(startUT));
            records[name!] = new KoffeeStateRecord(name!, startUT);
            expiredObserved.Remove(name!);
        }

        internal void RestoreStart(string? name, double? startUT)
        {
            if (!ValidName(name)) return;
            if (startUT.HasValue && ValidStart(startUT.Value))
                records[name!] = new KoffeeStateRecord(name!, startUT.Value);
            else records.Remove(name!);
            expiredObserved.Remove(name!);
        }

        internal void Load(IEnumerable<KoffeeStateRecord> saved)
        {
            records.Clear();
            expiredObserved.Clear();
            foreach (KoffeeStateRecord record in saved)
            {
                if (!ValidName(record.Name) || !ValidStart(record.StartUT)) continue;
                if (!records.TryGetValue(record.Name, out KoffeeStateRecord existing) ||
                    existing.StartUT < record.StartUT) records[record.Name] = record;
            }
        }

        internal IReadOnlyList<KoffeeStateRecord> Save() => records.Values
            .Where(record => ValidName(record.Name) && ValidStart(record.StartUT))
            .OrderBy(record => record.Name, StringComparer.Ordinal)
            .ToArray();

        internal void Clear()
        {
            records.Clear();
            expiredObserved.Clear();
        }

        private static bool ValidName(string? value) => !string.IsNullOrEmpty(value);
        private static bool ValidStart(double value) => Finite(value);
        private static bool ValidDuration(double value) => value > 0 && Finite(value);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    internal readonly struct KoffeeStateRecord
    {
        internal readonly string Name;
        internal readonly double StartUT;

        internal KoffeeStateRecord(string name, double startUT)
        {
            Name = name;
            StartUT = startUT;
        }
    }
}

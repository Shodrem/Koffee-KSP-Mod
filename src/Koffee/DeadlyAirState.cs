using System;

namespace ShoAerospace.Koffee
{
    internal enum DeadlyNotice { None, Shortage, Critical, FirstDeath }

    // Capacity is authorized by Komfort's electrical coordinator, never inferred
    // from a final battery. Atmospheric oxygen is the existing stock exemption.
    internal readonly struct DeadlyConditions
    {
        internal readonly int Crew;
        internal readonly double AirCapacity;
        internal readonly bool Oxygen, Valid;
        internal DeadlyConditions(int crew, double airCapacity, bool oxygen = false)
        {
            Crew = crew; AirCapacity = airCapacity; Oxygen = oxygen;
            Valid = crew >= 0 && ExperimentContainerState.Finite(airCapacity) && airCapacity >= 0;
        }
        internal bool Sufficient => Oxygen || AirCapacity + 1e-9 >= Crew;
    }

    // Vessel air only; neither Komfort nor individual health is stored here.
    internal sealed class DeadlyAirState
    {
        internal const double AirMaximum = 64800;
        internal bool Initialized, Episode, Emergency;
        internal double UT, Air = AirMaximum;
        internal void Start(double now) { Initialized = true; UT = now; }
        internal void Rebase(double now) { UT = now; }
        internal DeadlyNotice Advance(double from, double to, bool enabled, bool sufficient,
            Action<double, double, bool> exposure)
        {
            if (!ScientistLevelHistory.Time(from) || !ScientistLevelHistory.Time(to) || to < from) return DeadlyNotice.None;
            if (!Initialized) Start(from);
            double begin = Math.Max(from, UT);
            if (to <= begin) return DeadlyNotice.None;
            UT = to;
            if (!enabled) return DeadlyNotice.None;
            if (sufficient)
            {
                Episode = false;
                Air = Math.Min(AirMaximum, Air + to - begin);
                exposure(begin, to, true);
                return DeadlyNotice.None;
            }
            bool shortage = !Episode;
            Episode = true;
            double safeEnd = Math.Min(to, begin + Air);
            Air = Math.Max(0, Air - (to - begin));
            // No damage before the exact instant at which the air reaches zero.
            if (safeEnd < to) exposure(safeEnd, to, false);
            return shortage ? DeadlyNotice.Shortage : DeadlyNotice.None;
        }
    }

    internal sealed class DeadlyHealth
    {
        internal const double Maximum = 21600, Critical = 3600;
        internal double Points = Maximum, UT;
        internal bool Recovering, DeathApplied, CriticalAnnounced;
        internal void Apply(double from, double to, bool recovery)
        {
            double seconds = Math.Max(0, to - Math.Max(from, UT));
            UT = Math.Max(UT, to);
            if (DeathApplied || Points <= 0) return;
            Points = Math.Max(0, Math.Min(Maximum, Points + (recovery ? seconds : -seconds)));
            Recovering = recovery && Points < Maximum;
            if (Points > Critical) CriticalAnnounced = false;
        }
        internal string Status => Points >= Maximum ? "Stable" : Recovering ? "Recovering" :
            Points <= Critical ? "Critical" : "Worsening";
    }

    internal sealed class DeadlyEva
    {
        internal const double FilterMaximum = 21600;
        internal readonly DeadlyAirState Air = new DeadlyAirState();
        internal double Filter = FilterMaximum;
    }
}

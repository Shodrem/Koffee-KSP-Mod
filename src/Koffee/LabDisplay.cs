using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal static class LabDisplay
    {
        internal static double Autonomy(IEnumerable<double> remainingTimes, double cups, double duration)
        {
            if (!LabWork.Positive(duration)) return 0;
            double[] ends = remainingTimes.Select(value => Math.Max(0, value)).ToArray();
            if (ends.Length == 0) return double.NaN;
            long available = (long)Math.Min(int.MaxValue, Math.Floor(Math.Max(0, cups)));

            for (int i = 0; i < ends.Length && available > 0; i++)
                if (ends[i] <= 0) { ends[i] = duration; available--; }

            while (available > 0)
            {
                double next = ends.Where(value => value > 0).DefaultIfEmpty(0).Min();
                if (next <= 0) break;
                for (int i = 0; i < ends.Length && available > 0; i++)
                    if (ends[i] == next) { ends[i] += duration; available--; }
            }
            return ends.Max();
        }

        internal static string TimeKey(double seconds, out string hours, out string minutes)
        {
            hours = minutes = "";
            if (double.IsNaN(seconds)) return "#Koffee_Lab_Time_NoCrew";
            if (seconds <= 0) return "#Koffee_Lab_Time_Zero";
            if (seconds < 60) return "#Koffee_Lab_Time_Less";
            double total = Math.Floor(seconds / 60);
            hours = Math.Floor(total / 60).ToString("0", CultureInfo.InvariantCulture);
            minutes = (total % 60).ToString(total >= 60 ? "00" : "0", CultureInfo.InvariantCulture);
            if (total < 60) return "#Koffee_Lab_Time_Minutes";
            return total % 60 == 0 ? "#Koffee_Lab_Time_Hours" : "#Koffee_Lab_Time_Both";
        }
    }

    internal enum LabState { Inactive, NoScientists, NoData, Ready, Investigating }

    internal sealed class LabTransitions
    {
        internal LabState State { get; private set; } = LabState.Ready;

        internal void SetCondition(LabState condition)
        {
            if (condition != LabState.Ready) State = condition;
            else if (State == LabState.Inactive || State == LabState.NoScientists || State == LabState.NoData)
                State = LabState.Ready;
        }

        internal void ConfirmProgress() => State = LabState.Investigating;

        internal string StatusKey(LabState condition)
        {
            if (condition == LabState.NoScientists) return "#Koffee_Lab_NoScientists";
            if (condition == LabState.NoData) return "#Koffee_Lab_NoData";
            if (condition == LabState.Inactive) return "#Koffee_Lab_Inactive";
            return State == LabState.Investigating ? "#Koffee_Lab_Researching" : "#Koffee_Lab_Available";
        }
    }
}

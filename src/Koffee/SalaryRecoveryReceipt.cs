using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    internal sealed class SalaryRecoveryRow
    {
        internal readonly string Id = System.Guid.NewGuid().ToString("N");
        internal readonly string Name;
        internal readonly double Days, Rate, Salary;
        internal double Charged;
        internal SalaryRecoveryRow(string name, double days, double rate, double salary)
        { Name = name; Days = days; Rate = rate; Salary = salary; }
        internal string Text => Localizer.Format("#Koffee_Salary_RecoveryCrew", Number(Days), Number(Rate), Number(Salary)) +
            (System.Math.Abs(Charged - Salary) > .000001 ? "\n" + Localizer.Format("#Koffee_Salary_RecoveryActual", Number(Charged)) : "");
        internal static string Number(double value) => value.ToString("N2", CultureInfo.CurrentCulture);
    }

    // Presentation only: a receipt never grants or repeats a payment.
    internal sealed class SalaryRecoveryReceipt
    {
        internal readonly List<SalaryRecoveryRow> Crew = new List<SalaryRecoveryRow>();
        internal double Charged => Crew.Sum(c => c.Charged);
        internal string TotalText => Localizer.Format("#Koffee_Salary_RecoveryTotal", SalaryRecoveryRow.Number(-Charged));
        internal double RecoveredFunds, NetFunds, TotalFunds;
    }
}

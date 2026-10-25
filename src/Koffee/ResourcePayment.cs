using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ShoAerospace.Koffee
{
    internal interface IResourceAccount
    {
        double Amount { get; set; }
        double Capacity { get; }
        bool CanWithdraw { get; }
        int Priority { get; }
        uint SourceId { get; }
    }

    // A synchronous payment: settle or refund before yielding back to Unity.
    internal sealed class ResourcePayment : IDisposable
    {
        private readonly List<IResourceAccount> accounts;
        private readonly double[] withdrawn;
        private bool settled;

        private ResourcePayment(List<IResourceAccount> accounts)
        {
            this.accounts = accounts;
            withdrawn = new double[accounts.Count];
        }

        internal static double Available(IEnumerable<IResourceAccount> accounts) =>
            accounts.Distinct().Sum(WholeCups);

        private static double WholeCups(IResourceAccount account) =>
            WholeCups(account.Amount, account.CanWithdraw);

        internal static double WholeCups(double amount, bool canWithdraw) =>
            canWithdraw && amount >= 1 && !double.IsInfinity(amount) ? Math.Floor(amount) : 0;

        internal static ResourcePayment? TryTake(double cost, List<IResourceAccount> sources)
        {
            if (double.IsNaN(cost) || double.IsInfinity(cost) || cost <= 0 || cost != Math.Floor(cost))
                throw new ArgumentOutOfRangeException(nameof(cost));
            // Stable ordering: descending stock priority, ascending flightID, then input order.
            var accounts = sources.Distinct().OrderByDescending(a => a.Priority)
                .ThenBy(a => a.SourceId).ToList();
            if (Available(accounts) < cost) return null;

            var payment = new ResourcePayment(accounts);
            try
            {
                double remaining = cost;
                for (int i = 0; i < accounts.Count && remaining > 0; i++)
                {
                    double take = Math.Min(WholeCups(accounts[i]), remaining);
                    if (take == 0) continue;
                    double before = accounts[i].Amount;
                    try { accounts[i].Amount = before - take; }
                    finally { payment.withdrawn[i] = Math.Max(0, before - accounts[i].Amount); }
                    if (payment.withdrawn[i] != take)
                    {
                        payment.Dispose();
                        return null;
                    }
                    remaining -= take;
                }
                if (remaining > 0)
                {
                    payment.Dispose();
                    return null;
                }
                return payment;
            }
            catch
            {
                payment.Dispose();
                throw;
            }
        }

        internal string Summary => withdrawn.Sum().ToString(CultureInfo.InvariantCulture) +
            " whole cup(s) from " + withdrawn.Count(amount => amount > 0) + " container(s): " +
            string.Join(", ", accounts.Select((account, i) => new { account, i })
                .Where(entry => withdrawn[entry.i] > 0)
                .Select(entry => "flightID=" + entry.account.SourceId + " take=" +
                    withdrawn[entry.i].ToString(CultureInfo.InvariantCulture)));

        internal void Commit() => settled = true;

        public void Dispose()
        {
            if (settled) return;
            for (int i = 0; i < accounts.Count; i++)
                if (withdrawn[i] > 0)
                    accounts[i].Amount = Math.Min(accounts[i].Capacity, accounts[i].Amount + withdrawn[i]);
            settled = true;
        }
    }
}

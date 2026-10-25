using System;
using System.Collections.Generic;

namespace ShoAerospace.Koffee
{
    internal sealed class RecoveryBonusReceipt
    {
        internal string Key { get; }
        internal string Title { get; }
        internal float Amount { get; }
        internal bool Paid { get; set; }
        internal RecoveryBonusReceipt(string key, string title, float amount, bool paid = false)
        { Key = key; Title = title; Amount = amount; Paid = paid; }
    }

    internal sealed class RecoveryBonusLedger
    {
        internal readonly Dictionary<string, RecoveryBonusReceipt> Receipts =
            new Dictionary<string, RecoveryBonusReceipt>(StringComparer.Ordinal);
        internal static float Calculate(float stockEffective, double percent)
        {
            double value = stockEffective * (percent / 100);
            return stockEffective > 0 && ExperimentContainerState.Finite(value) &&
                value >= 0 && value <= float.MaxValue ? (float)value : 0;
        }
        internal RecoveryBonusReceipt Stage(Guid container, Guid run, string title, float amount)
        {
            string key = container.ToString("N") + ":" + run.ToString("N");
            if (container == Guid.Empty || run == Guid.Empty || amount < 0 ||
                !ExperimentContainerState.Finite(amount)) throw new ArgumentException("Invalid recovery receipt.");
            if (!Receipts.TryGetValue(key, out RecoveryBonusReceipt? receipt))
            {
                receipt = new RecoveryBonusReceipt(key, title, amount);
                Receipts.Add(key, receipt);
            }
            return receipt;
        }
        // The mutation and receipt commit contain no callbacks. Events run after both commit.
        internal bool Pay(RecoveryBonusReceipt receipt, Action<float> atomicCredit)
        {
            if (receipt.Paid) return false;
            atomicCredit(receipt.Amount);
            receipt.Paid = true;
            return true;
        }
    }
}

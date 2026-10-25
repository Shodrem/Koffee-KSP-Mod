using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames,
        GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    public sealed class StationRecoveryScenario : ScenarioModule
    {
        private static StationRecoveryScenario? current;
        private Game? game;
        internal readonly RecoveryBonusLedger Ledger = new RecoveryBonusLedger();
        private readonly System.Collections.Generic.List<ConfigNode> opaque = new System.Collections.Generic.List<ConfigNode>();
        private bool blocked;
        private sealed class View
        {
            internal WeakReference<KSP.UI.Screens.MissionRecoveryDialog> Dialog = null!;
            internal bool Accounted;
        }
        private readonly System.Collections.Generic.Dictionary<string, View> views = new System.Collections.Generic.Dictionary<string, View>();
        private static readonly FieldInfo Science = AccessTools.Field(typeof(ResearchAndDevelopment), "science");
        internal static StationRecoveryScenario? Current => current != null &&
            ReferenceEquals(current.game, HighLogic.CurrentGame) ? current : null;
        public override void OnAwake()
        { base.OnAwake(); game = HighLogic.CurrentGame; current = this; }
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node); game = HighLogic.CurrentGame; current = this;
            Ledger.Receipts.Clear(); opaque.Clear(); views.Clear(); blocked = false;
            foreach (ConfigNode child in node.GetNodes("RECOVERY_BONUS"))
            {
                string key = child.GetValue("key") ?? "";
                string[] ids = key.Split(':');
                if (new[] { "schema", "key", "title", "amount", "paid" }.Any(k => child.GetValues(k).Length != 1) ||
                    child.GetValue("schema") != "1" || ids.Length != 2 ||
                    !Guid.TryParseExact(ids[0], "N", out Guid container) || container == Guid.Empty ||
                    !Guid.TryParseExact(ids[1], "N", out Guid run) || run == Guid.Empty ||
                    !float.TryParse(child.GetValue("amount"), NumberStyles.Float, CultureInfo.InvariantCulture, out float amount) ||
                    amount < 0 || !ExperimentContainerState.Finite(amount) ||
                    !bool.TryParse(child.GetValue("paid"), out bool paid) || Ledger.Receipts.ContainsKey(key))
                {
                    opaque.Add(child.CreateCopy()); blocked = true;
                    Debug.LogError("[Koffee] Recovery receipt preserved; bonus payments disabled for this save.");
                    continue;
                }
                Ledger.Receipts.Add(key, new RecoveryBonusReceipt(key, child.GetValue("title") ?? "", amount, paid));
            }
        }
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node); node.RemoveNodes("RECOVERY_BONUS");
            foreach (ConfigNode child in opaque) node.AddNode(child.CreateCopy());
            foreach (RecoveryBonusReceipt receipt in Ledger.Receipts.Values)
            {
                var child = node.AddNode("RECOVERY_BONUS"); child.AddValue("schema", "1");
                child.AddValue("key", receipt.Key); child.AddValue("title", receipt.Title);
                child.AddValue("amount", receipt.Amount.ToString("R", CultureInfo.InvariantCulture));
                child.AddValue("paid", receipt.Paid.ToString());
            }
        }
        internal bool CanPay => !blocked;
        internal bool Pay(RecoveryBonusReceipt receipt)
        {
            ResearchAndDevelopment rnd = ResearchAndDevelopment.Instance;
            if (blocked || rnd == null || !ReferenceEquals(game, HighLogic.CurrentGame) || game?.Mode == Game.Modes.SANDBOX) return false;
            bool paid = Ledger.Pay(receipt, amount =>
            {
                float balance = (float)Science.GetValue(rnd)!;
                float next = balance + amount;
                if (!ExperimentContainerState.Finite(next)) throw new InvalidOperationException("Non-finite science balance.");
                // The base already includes difficulty and strategy conversions. Do not submit
                // a second CurrencyModifierQuery for the additional percentage.
                Science.SetValue(rnd, next);
            });
            if (paid)
                try { GameEvents.OnScienceChanged.Fire(rnd.Science, TransactionReasons.VesselRecovery); }
                catch (Exception e) { Debug.LogError("[Koffee] Bonus credited; science listener failed: " + e); }
            return paid;
        }
        internal void Attach(RecoveryBonusReceipt receipt, KSP.UI.Screens.MissionRecoveryDialog dialog, bool accounted)
        { views[receipt.Key] = new View { Dialog = new WeakReference<KSP.UI.Screens.MissionRecoveryDialog>(dialog), Accounted = accounted }; }
        internal void Present(RecoveryBonusReceipt receipt)
        {
            if (!receipt.Paid || !views.TryGetValue(receipt.Key, out View? view)) return;
            if (!view.Dialog.TryGetTarget(out var dialog) || dialog == null) { views.Remove(receipt.Key); return; }
            if (!view.Accounted)
            {
                dialog.scienceEarned += receipt.Amount; view.Accounted = true;
                dialog.totalScience = ResearchAndDevelopment.Instance.Science;
            }
            if (receipt.Amount > 0) StationRecoveryPatches.AddBonusWidget(dialog, receipt);
            views.Remove(receipt.Key);
        }
        public void Update()
        {
            if (!ReferenceEquals(Current, this) || blocked) return;
            foreach (RecoveryBonusReceipt receipt in Ledger.Receipts.Values.ToArray())
                try
                {
                    Pay(receipt);
                    Present(receipt);
                }
                catch (Exception e) { Debug.LogError("[Koffee] Recovery bonus remains pending: " + e); }
        }
        public void OnDestroy() { if (ReferenceEquals(current, this)) current = null; }
    }
}

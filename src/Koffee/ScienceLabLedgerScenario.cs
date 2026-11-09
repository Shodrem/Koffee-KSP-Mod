using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ShoAerospace.Koffee
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames,
        GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION, GameScenes.EDITOR)]
    public sealed class ScienceLabLedgerScenario : ScenarioModule
    {
        private static ScienceLabLedgerScenario? current;
        private Game? owner;
        private ConfigNode? opaque;
        private bool loaded;
        private readonly Dictionary<string, ConfigNode> receipts = new Dictionary<string, ConfigNode>(StringComparer.Ordinal);
        internal static ScienceLabLedgerScenario? Current => current != null && current.loaded &&
            current.opaque == null && ReferenceEquals(current.owner, HighLogic.CurrentGame) ? current : null;
        public override void OnAwake() { base.OnAwake(); owner = HighLogic.CurrentGame; current = this; }
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node); owner = HighLogic.CurrentGame; current = this;
            receipts.Clear(); opaque = null; loaded = true;
            if ((node.HasValue("schema") && node.GetValue("schema") != "1") || node.GetValues("schema").Length > 1)
                opaque = node.CreateCopy();
            foreach (ConfigNode receipt in node.GetNodes("ACCEPTED"))
            {
                string id = receipt.GetValue("subjectID") ?? "";
                if (receipt.GetValues("subjectID").Length != 1 || !id.Contains("@") || receipts.ContainsKey(id))
                    opaque = node.CreateCopy();
                else receipts.Add(id, receipt.CreateCopy());
            }
            if (opaque != null) UnityEngine.Debug.LogError("[Koffee] Invalid lab ledger preserved; lab acceptance disabled.");
        }
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (!loaded) return;
            if (opaque != null) { opaque.CopyTo(node); return; }
            node.SetValue("schema", "1", true); node.RemoveNodes("ACCEPTED");
            foreach (var receipt in receipts.OrderBy(p => p.Key, StringComparer.Ordinal)) node.AddNode(receipt.Value.CreateCopy());
        }
        internal bool Contains(string subject) => receipts.ContainsKey(subject);
        internal void Remove(string subject) => receipts.Remove(subject);
        internal void Add(ScienceData data, Part source, ModuleScienceLab lab, float remaining, float amount)
        {
            var node = new ConfigNode("ACCEPTED");
            node.AddValue("subjectID", data.subjectID); node.AddValue("token", Guid.NewGuid().ToString("N"));
            node.AddValue("ut", Planetarium.GetUniversalTime().ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("source", source.persistentId); node.AddValue("lab", lab.part.persistentId);
            node.AddValue("remaining", remaining.ToString("R", CultureInfo.InvariantCulture));
            node.AddValue("data", amount.ToString("R", CultureInfo.InvariantCulture));
            receipts.Add(data.subjectID, node);
        }
        public void OnDestroy() { if (ReferenceEquals(current, this)) current = null; }
    }
}

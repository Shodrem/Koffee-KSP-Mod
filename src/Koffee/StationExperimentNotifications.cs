using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP.Localization;
using KSP.UI.Screens;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames,
        GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    public sealed class StationExperimentNotifications : ScenarioModule
    {
        private sealed class Transition
        {
            internal string Kind = "";
            internal long Sequence;
        }
        private sealed class Notice
        {
            internal string Id = "", Experiment = "", Vessel = "", Kind = "", Equipment = "";
            internal bool Delivered;
        }
        private static StationExperimentNotifications? current;
        private Game? game;
        private readonly Dictionary<string, Transition> transitions = new Dictionary<string, Transition>(StringComparer.Ordinal);
        private readonly Dictionary<string, Notice> notices = new Dictionary<string, Notice>(StringComparer.Ordinal);
        private readonly List<ConfigNode> opaque = new List<ConfigNode>();
        private bool blocked;
        public override void OnAwake() { base.OnAwake(); game = HighLogic.CurrentGame; current = this; }
        internal static void Observe(ExperimentContainerState state, string vessel, StationExperimentPauseReason reason)
        {
            try
            {
                if (current == null || !ReferenceEquals(current.game, HighLogic.CurrentGame) || current.blocked ||
                    !state.RunId.HasValue || state.ContainerId == Guid.Empty) return;
                string key = state.ContainerId.ToString("N") + ":" + state.RunId.Value.ToString("N");
                string kind = state.Status == ExperimentContainerStatus.Finalized ? "Completed" :
                    state.Status == ExperimentContainerStatus.Running ?
                        reason == StationExperimentPauseReason.None || reason == StationExperimentPauseReason.WorkComplete ? "Active" : reason.ToString() : "Inactive";
                if (!current.transitions.TryGetValue(key, out Transition? transition))
                { transition = new Transition(); current.transitions.Add(key, transition); }
                if (transition.Kind == kind || transition.Kind == "Completed") return;
                long sequence = checked(transition.Sequence + 1);
                if (kind != "Active" && kind != "Inactive")
                {
                    string id = key + ":" + sequence.ToString(CultureInfo.InvariantCulture);
                    string equipment = StationExperimentCatalogLoader.Current.TryGet(state.ExperimentId, out var definition) ? definition.RequiredPartName : "";
                    current.notices.Add(id, new Notice { Id = id, Experiment = state.ExperimentId, Vessel = vessel ?? "", Kind = kind, Equipment = equipment });
                }
                transition.Kind = kind; transition.Sequence = sequence;
            }
            catch (Exception e) { Debug.LogError("[Koffee] Experiment notification failed without changing research: " + e); }
        }
        public void Update()
        {
            if (blocked || !ReferenceEquals(game, HighLogic.CurrentGame) || !ReferenceEquals(current, this) ||
                (HighLogic.LoadedScene != GameScenes.FLIGHT && HighLogic.LoadedScene != GameScenes.SPACECENTER && HighLogic.LoadedScene != GameScenes.TRACKSTATION) ||
                MessageSystem.Instance == null || !MessageSystem.Ready) return;
            foreach (Notice notice in notices.Values.Where(n => !n.Delivered).ToArray())
                try
                {
                    // The stock Message has no persisted ID. A hidden rich-text marker survives
                    // its stock Save/Load and identifies a partially accepted delivery on retry.
                    string marker = "<size=0>KoffeeNotice:" + notice.Id + "</size>";
                    if (MessageSystem.Instance.FindMessages(m => m.message.Contains(marker)).Count != 0)
                    { notice.Delivered = true; continue; }
                    bool complete = notice.Kind == "Completed";
                    string experiment = Localizer.Format(ResearchAndDevelopment.GetExperiment(notice.Experiment)?.experimentTitle ?? notice.Experiment);
                    string reason = complete ? Localizer.Format("#Koffee_Station_Notice_Result") : StationExperimentEquipment.PauseText(notice.Kind, notice.Equipment);
                    var message = new MessageSystem.Message(
                        Localizer.Format(complete ? "#Koffee_Station_Notice_Completed" : "#Koffee_Station_Notice_Paused"),
                        Localizer.Format("#Koffee_Station_Notice_Body", experiment, notice.Vessel, reason) + marker,
                        complete ? MessageSystemButton.MessageButtonColor.GREEN : MessageSystemButton.MessageButtonColor.ORANGE,
                        complete ? MessageSystemButton.ButtonIcons.COMPLETE : MessageSystemButton.ButtonIcons.ALERT);
                    MessageSystem.Instance.AddMessage(message, true);
                    notice.Delivered = true;
                }
                catch (Exception e) { Debug.LogError("[Koffee] Stock notification remains pending: " + e); }
        }
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node); node.RemoveNodes("RESEARCH_TRANSITION"); node.RemoveNodes("RESEARCH_NOTICE");
            foreach (ConfigNode child in opaque) node.AddNode(child.CreateCopy());
            foreach (var pair in transitions)
            {
                ConfigNode child = node.AddNode("RESEARCH_TRANSITION"); child.AddValue("schema", "1");
                child.AddValue("key", pair.Key); child.AddValue("kind", pair.Value.Kind);
                child.AddValue("sequence", pair.Value.Sequence.ToString(CultureInfo.InvariantCulture));
            }
            foreach (Notice notice in notices.Values)
            {
                ConfigNode child = node.AddNode("RESEARCH_NOTICE"); child.AddValue("schema", "1");
                child.AddValue("id", notice.Id); child.AddValue("kind", notice.Kind);
                child.AddValue("experiment", notice.Experiment); child.AddValue("vessel", notice.Vessel);
                child.AddValue("delivered", notice.Delivered.ToString());
                if (!string.IsNullOrEmpty(notice.Equipment)) child.AddValue("equipment", notice.Equipment);
            }
        }
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node); game = HighLogic.CurrentGame; current = this;
            transitions.Clear(); notices.Clear(); opaque.Clear(); blocked = false;
            foreach (ConfigNode child in node.GetNodes("RESEARCH_TRANSITION"))
            {
                string key = child.GetValue("key") ?? "";
                if (!Single(child, "schema", "key", "kind", "sequence") || child.GetValue("schema") != "1" || !Identity(key, false) ||
                    !long.TryParse(child.GetValue("sequence"), NumberStyles.None, CultureInfo.InvariantCulture, out long seq) ||
                    seq <= 0 || !ValidKind(child.GetValue("kind")) || transitions.ContainsKey(key))
                { Preserve(child); continue; }
                transitions.Add(key, new Transition { Kind = child.GetValue("kind"), Sequence = seq });
            }
            foreach (ConfigNode child in node.GetNodes("RESEARCH_NOTICE"))
            {
                string id = child.GetValue("id") ?? "";
                if (!Single(child, "schema", "id", "kind", "experiment", "vessel", "delivered") || child.GetValues("equipment").Length > 1 ||
                    child.GetValue("schema") != "1" || !Identity(id, true) || !ValidKind(child.GetValue("kind")) ||
                    string.IsNullOrWhiteSpace(child.GetValue("experiment")) ||
                    !bool.TryParse(child.GetValue("delivered"), out bool delivered) || notices.ContainsKey(id))
                { Preserve(child); continue; }
                notices.Add(id, new Notice { Id = id, Kind = child.GetValue("kind"), Experiment = child.GetValue("experiment"),
                    Vessel = child.GetValue("vessel"), Equipment = child.GetValue("equipment") ?? "", Delivered = delivered });
            }
        }
        private void Preserve(ConfigNode node)
        { opaque.Add(node.CreateCopy()); blocked = true; Debug.LogError("[Koffee] Notification record preserved; delivery disabled for ambiguous save."); }
        private static bool Single(ConfigNode node, params string[] keys) => keys.All(k => node.GetValues(k).Length == 1);
        private static bool ValidKind(string kind) => kind == "Active" || kind == "Inactive" || kind == "Completed" ||
            Enum.TryParse(kind, false, out StationExperimentPauseReason reason) && Enum.IsDefined(typeof(StationExperimentPauseReason), reason);
        private static bool Identity(string key, bool sequence)
        {
            string[] values = key.Split(':');
            return values.Length == (sequence ? 3 : 2) && Guid.TryParseExact(values[0], "N", out Guid c) && c != Guid.Empty &&
                Guid.TryParseExact(values[1], "N", out Guid r) && r != Guid.Empty &&
                (!sequence || long.TryParse(values[2], NumberStyles.None, CultureInfo.InvariantCulture, out long s) && s > 0);
        }
        public void OnDestroy() { if (ReferenceEquals(current, this)) current = null; }
    }
}

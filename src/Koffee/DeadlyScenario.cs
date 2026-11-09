using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    public sealed class DeadlyScenario : ScenarioModule
    {
        private sealed class Entry
        {
            internal DeadlyAirState State = new DeadlyAirState();
            internal string[] Members = Array.Empty<string>();
            internal uint[] Parts = Array.Empty<uint>();
            internal string Name = "";
            internal DeadlyNotice Pending;
            internal string FirstDeath = "";
            internal bool CrewDirty, ShortagePending;
        }
        private static DeadlyScenario? current;
        private Game? game;
        private readonly Dictionary<Guid, Entry> vessels = new Dictionary<Guid, Entry>();
        // Stock persists crew by its unique roster name, not by the displayName.
        // ChangeName fires onKerbalNameChanged; move all personal state with it.
        private readonly Dictionary<string, DeadlyHealth> health = new Dictionary<string, DeadlyHealth>(StringComparer.Ordinal);
        private readonly Dictionary<string, DeadlyEva> evas = new Dictionary<string, DeadlyEva>(StringComparer.Ordinal);
        private readonly Dictionary<uint, Guid> partOrigins = new Dictionary<uint, Guid>();
        private readonly List<ConfigNode> opaque = new List<ConfigNode>();
        private bool blocked, applying;
        internal static long Revision { get; private set; }
        private static DeadlyScenario? Current => current != null && ReferenceEquals(current.game, HighLogic.CurrentGame) &&
            !current.blocked ? current : null;
        internal static DeadlyGameParameters? Settings => HighLogic.CurrentGame?.Parameters.CustomParams<DeadlyGameParameters>();
        internal static DeadlyOptions OptionsAt(double ut) => Settings?.At(ut) ?? new DeadlyOptions(false, 43200);
        internal static double Boundary(double ut) => Settings?.NextBoundary(ut) ?? double.PositiveInfinity;
        public override void OnAwake()
        {
            base.OnAwake(); game = HighLogic.CurrentGame; current = this;
            GameEvents.onPartCouple.Add(BeforeCouple);
            GameEvents.onPartUndock.Add(BeforeUndock);
            GameEvents.onPartDeCouple.Add(BeforeUndock);
            GameEvents.onGameSceneLoadRequested.Add(BeforeScene);
            GameEvents.onCrewOnEva.Add(CrewEnvironmentChanged);
            GameEvents.onCrewBoardVessel.Add(CrewEnvironmentChanged);
            GameEvents.onCrewTransferred.Add(CrewTransferred);
            GameEvents.onKerbalNameChanged.Add(Renamed);
        }
        private void BeforeCouple(GameEvents.FromToAction<Part, Part> change)
        {
            Close(change.from?.vessel);
            if (!ReferenceEquals(change.from?.vessel, change.to?.vessel)) Close(change.to?.vessel);
        }
        private void BeforeUndock(Part part) => Close(part?.vessel);
        private void BeforeScene(GameScenes scene) => FlushAll();
        // Called by the stock RemoveCrewmember prefix while the old membership
        // still exists (transfer, EVA and boarding). Deaths already close inside Advance.
        internal static void BeforeCrewRemoval(Part part) => Current?.Close(part?.vessel);
        private void CrewTransferred(GameEvents.HostedFromToAction<ProtoCrewMember, Part> change) =>
            CrewEnvironmentChanged(new GameEvents.FromToAction<Part, Part>(change.from, change.to));
        private void CrewEnvironmentChanged(GameEvents.FromToAction<Part, Part> change)
        {
            double now = Planetarium.GetUniversalTime();
            foreach (var v in new[] { change.from?.vessel, change.to?.vessel }.Where(v => v != null).Distinct())
            {
                Baseline(v!, now); KomfortRuntime.Invalidate(v!); KomfortRuntime.Observe(v!, now);
            }
        }
        private void Renamed(ProtoCrewMember crew, string oldName, string newName)
        {
            if (health.ContainsKey(newName) || evas.ContainsKey(newName))
            { blocked = true; Debug.LogError("[Koffee] Ambiguous renamed crew; Deadly processing disabled."); return; }
            if (health.TryGetValue(oldName, out var hp)) { health.Remove(oldName); health.Add(newName, hp); }
            if (evas.TryGetValue(oldName, out var eva)) { evas.Remove(oldName); evas.Add(newName, eva); }
            foreach (var e in vessels.Values)
                e.Members = e.Members.Select(n => n == oldName ? newName : n).ToArray();
        }
        internal static void FlushAll()
        {
            var owner = Current;
            if (owner == null || owner.applying || (HighLogic.LoadedScene != GameScenes.FLIGHT &&
                HighLogic.LoadedScene != GameScenes.SPACECENTER && HighLogic.LoadedScene != GameScenes.TRACKSTATION)) return;
            double now = Planetarium.GetUniversalTime();
            if (HighLogic.LoadedScene == GameScenes.FLIGHT && !FlightGlobals.ready) return;
            if (!OptionsAt(now).Enabled || !KomfortRuntime.OptionsAt(now).Enabled) return;
            foreach (Vessel vessel in FlightGlobals.Vessels.ToArray())
            {
                if (vessel == null) continue;
                if (vessel.loaded) StationExperimentRuntime.Tick(vessel, now, capturePresentation: false);
                else if (StationBackgroundElectricity.Provider is IStationBackgroundSaveFlush flush) flush.FlushBeforeSave(vessel, now);
                else StationExperimentBackground.Tick(vessel, now);
            }
        }
        private void Close(Vessel? vessel)
        {
            if (applying || vessel == null || Current != this || !OptionsAt(Planetarium.GetUniversalTime()).Enabled) return;
            double now = Planetarium.GetUniversalTime();
            if (vessel.loaded) StationExperimentRuntime.Tick(vessel, now, capturePresentation: false);
            else StationExperimentBackground.Tick(vessel, now);
        }
        public void OnDestroy()
        {
            GameEvents.onPartCouple.Remove(BeforeCouple); GameEvents.onPartUndock.Remove(BeforeUndock);
            GameEvents.onPartDeCouple.Remove(BeforeUndock); GameEvents.onGameSceneLoadRequested.Remove(BeforeScene);
            GameEvents.onCrewOnEva.Remove(CrewEnvironmentChanged); GameEvents.onCrewBoardVessel.Remove(CrewEnvironmentChanged);
            GameEvents.onCrewTransferred.Remove(CrewTransferred); GameEvents.onKerbalNameChanged.Remove(Renamed);
            if (ReferenceEquals(current, this)) current = null;
        }
        internal static string[] Members(Vessel v) => (v.loaded ?
            v.parts.Where(p => p != null && p.State != PartStates.DEAD).SelectMany(p => p.protoModuleCrew) :
            (v.protoVessel?.protoPartSnapshots ?? new List<ProtoPartSnapshot>()).SelectMany(p => p.protoModuleCrew))
            .Where(c => c != null && c.rosterStatus != ProtoCrewMember.RosterStatus.Dead).Select(c => c.name)
            .Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        private static uint[] Parts(Vessel v) => (v.loaded ? v.parts.Where(p => p != null && p.State != PartStates.DEAD).Select(p => p.persistentId) :
            (v.protoVessel?.protoPartSnapshots ?? new List<ProtoPartSnapshot>()).Select(p => p.persistentId)).Where(p => p != 0).Distinct().ToArray();

        internal static void Baseline(Vessel vessel, double now)
        {
            var owner = Current;
            if (owner == null || (!OptionsAt(now).Enabled && !owner.vessels.ContainsKey(vessel.id))) return;
            string[] crew = Members(vessel);
            uint[] parts = Parts(vessel);
            if (!owner.vessels.TryGetValue(vessel.id, out var entry))
            { entry = new Entry(); owner.vessels.Add(vessel.id, entry); }
            if (!vessel.isEVA)
            {
                // Physical provenance, independent of crew transfers. A split
                // inherits its atmosphere; a docking uses the lowest known air
                // of its occupied donors. No stock cabin volume is available.
                var donors = parts.Where(owner.partOrigins.ContainsKey).Select(p => owner.partOrigins[p]).Distinct()
                    .Where(owner.vessels.ContainsKey).Select(id => owner.vessels[id])
                    .Where(e => e.State.Initialized && (e.Members.Length > 0 || ReferenceEquals(e, entry))).ToArray();
                if (donors.Length > 0) entry.State.Air = donors.Min(e => e.State.Air);
                foreach (string name in crew) owner.evas.Remove(name);
                foreach (uint part in parts) owner.partOrigins[part] = vessel.id;
            }
            else foreach (string name in crew)
                if (!owner.evas.ContainsKey(name))
                { var suit = new DeadlyEva(); suit.Air.Start(now); owner.evas.Add(name, suit); }
            foreach (string name in crew)
            {
                if (!owner.health.TryGetValue(name, out var hp)) owner.health.Add(name, hp = new DeadlyHealth());
                hp.UT = Math.Max(hp.UT, now);
            }
            entry.Members = crew; entry.Parts = parts; entry.Name = vessel.vesselName;
            entry.State.Start(now);
            foreach (string name in crew)
                if (vessel.isEVA) owner.evas[name].Air.Rebase(now);
            var hardware = EmergencySupport.Capture(vessel);
            bool on = !vessel.isEVA && hardware.Any(d => d.Enabled) && OptionsAt(now).Enabled && KomfortRuntime.OptionsAt(now).Enabled;
            entry.State.Emergency = on;
            if (on) entry.ShortagePending = false;
            if (on) { if (EnergySupportControl.Suppress(vessel)) Revision++; }
            else
            {
                foreach (var device in hardware.Where(d => d.Enabled)) { device.Switch?.Invoke(false); Revision++; }
                if (EnergySupportControl.Restore(vessel, now)) Revision++;
            }
        }

        internal static double SafeBoundary(Vessel vessel, double from, double to)
        {
            var owner = Current;
            if (owner == null || !OptionsAt(from).Enabled || !KomfortRuntime.OptionsAt(from).Enabled) return to;
            if (!owner.vessels.TryGetValue(vessel.id, out var e)) return Math.Min(to, from + 1);
            double minHealth = e.Members.Where(owner.health.ContainsKey).Select(n => owner.health[n].Points)
                .Where(p => p > 0).DefaultIfEmpty(DeadlyHealth.Maximum).Min();
            if (vessel.isEVA)
            {
                foreach (string name in e.Members)
                    if (owner.evas.TryGetValue(name, out var eva))
                        to = Math.Min(to, from + Math.Max(1e-7, eva.Filter > 0 ? eva.Filter : eva.Air.Air > 0 ? eva.Air.Air : minHealth));
            }
            else to = Math.Min(to, from + Math.Max(1e-7, e.State.Air > 0 ? e.State.Air : minHealth));
            return to;
        }
        internal static bool Active(Vessel? vessel) => vessel != null && !vessel.isEVA && Read(vessel.id)?.Emergency == true &&
            OptionsAt(Planetarium.GetUniversalTime()).Enabled && KomfortRuntime.OptionsAt(Planetarium.GetUniversalTime()).Enabled;
        internal static void SettingsChanged(Vessel vessel, double now)
        {
            var state = Read(vessel.id);
            if (state == null) return;
            if (!OptionsAt(now).Enabled || !KomfortRuntime.OptionsAt(now).Enabled)
            {
                foreach (var device in EmergencySupport.Capture(vessel)) device.Switch?.Invoke(false);
                state.Emergency = false; EnergySupportControl.Restore(vessel, now); Revision++;
            }
            KomfortRuntime.Invalidate(vessel); KomfortRuntime.Observe(vessel, now);
        }
        internal static void SetEmergency(Vessel vessel, bool enabled)
        {
            double now = Planetarium.GetUniversalTime();
            if (vessel?.loaded != true || !OptionsAt(now).Enabled || !KomfortRuntime.OptionsAt(now).Enabled) return;
            // Close science as well as service accounting before changing flags.
            StationExperimentRuntime.Tick(vessel, now, capturePresentation: false);
            Baseline(vessel, now);
            var state = Read(vessel.id); var devices = EmergencySupport.Capture(vessel);
            if (state == null || enabled && devices.Count == 0 || state.Emergency == enabled) return;
            state.Emergency = enabled;
            if (enabled) Current!.vessels[vessel.id].ShortagePending = false;
            else state.Episode = false; // Rearm only after the user leaves Energy Support.
            if (enabled) EnergySupportControl.Suppress(vessel);
            foreach (var d in devices) d.Switch?.Invoke(enabled);
            if (!enabled) EnergySupportControl.Restore(vessel, now);
            Revision++;
            KomfortRuntime.Invalidate(vessel); KomfortRuntime.Observe(vessel, now);
            StationExperimentRuntime.Tick(vessel, now, capturePresentation: false);
            KoffeeLonapse.EmergencyChanged(vessel, enabled);
        }

        internal static void Advance(Vessel vessel, double from, double to, KomfortOptions komfort,
            Func<DeadlyConditions> conditions, Action<int> crewChanged, Func<uint, bool>? suppliedDevice = null)
        {
            var owner = Current;
            if (owner == null || owner.applying) return;
            if (!owner.vessels.TryGetValue(vessel.id, out var entry))
            { if (!OptionsAt(from).Enabled) return; Baseline(vessel, from); entry = owner.vessels[vessel.id]; }
            bool enabled = OptionsAt(from).Enabled && komfort.Enabled;
            owner.applying = true;
            try
            {
                double cursor = Math.Max(from, entry.State.UT);
                while (cursor < to)
                {
                    var c = conditions();
                    if (!c.Valid) { entry.State.Rebase(to); break; }
                    string[] crew = Members(vessel);
                    if (crew.Length == 0) { entry.State.Rebase(to); break; }
                    bool support = entry.State.Emergency && EmergencySupport.Capture(vessel).Any(d => d.Enabled && suppliedDevice?.Invoke(d.Id) == true);
                    bool sufficient = c.Sufficient || support;
                    double end = to;
                    foreach (string name in crew)
                    {
                        if (!owner.health.ContainsKey(name)) owner.health.Add(name, new DeadlyHealth { UT = cursor });
                        var hp = owner.health[name];
                        var air = vessel.isEVA ? owner.evas[name].Air : entry.State;
                        bool filtered = vessel.isEVA ? owner.evas[name].Filter > 0 || c.Oxygen : sufficient;
                        if (enabled && !filtered) { hp.Recovering = false; end = Math.Min(end, cursor + air.Air + hp.Points); }
                        if (enabled && vessel.isEVA && owner.evas[name].Filter > 0)
                            end = Math.Min(end, cursor + owner.evas[name].Filter);
                    }
                    // Zero-health survivors retry stock cleanup, with no random
                    // victim selection and no repeated native Die call.
                    if (enabled) foreach (string name in crew.Where(n => owner.health[n].Points <= 0)) Remove(name);
                    if (end <= cursor) { if (Members(vessel).Length == crew.Length) break; continue; }
                    if (vessel.isEVA)
                    {
                        foreach (string name in crew.Where(n => owner.health[n].Points > 0))
                        {
                            var eva = owner.evas[name];
                            bool filtered = eva.Filter > 0 || c.Oxygen;
                            AdvanceAir(eva.Air, new[] { name }, filtered);
                            if (enabled) eva.Filter = Math.Max(0, eva.Filter - (end - cursor));
                        }
                        entry.State.Rebase(end);
                    }
                    else AdvanceAir(entry.State, crew, sufficient);
                    foreach (string name in crew)
                    {
                        var hp = owner.health[name];
                        hp.UT = Math.Max(hp.UT, end);
                        if (enabled && hp.Points <= DeadlyHealth.Critical && hp.Points > 0 && !hp.Recovering && !hp.CriticalAnnounced)
                        { hp.CriticalAnnounced = true; Notice(DeadlyNotice.Critical); }
                        if (enabled && hp.Points <= 0) Remove(name);
                    }
                    cursor = end;

                    void AdvanceAir(DeadlyAirState air, string[] exposed, bool filtered)
                    {
                        Notice(air.Advance(cursor, end, enabled, filtered, (a, b, recovery) =>
                        { foreach (string name in exposed) owner.health[name].Apply(a, b, recovery); }));
                    }
                }
                void Notice(DeadlyNotice notice)
                {
                    if (notice == DeadlyNotice.Shortage) entry.ShortagePending = !entry.State.Emergency;
                    else if (notice > entry.Pending) entry.Pending = notice;
                }
                void Remove(string name)
                {
                    var hp = owner.health[name];
                    if (!Kill(vessel, name)) return;
                    if (!hp.DeathApplied)
                    {
                        entry.FirstDeath = string.IsNullOrEmpty(entry.FirstDeath) ? name : entry.FirstDeath + ", " + name;
                        Notice(DeadlyNotice.FirstDeath);
                    }
                    hp.DeathApplied = true;
                    owner.evas.Remove(name); entry.Members = Members(vessel); entry.CrewDirty = true;
                    crewChanged(entry.Members.Length);
                }
            }
            finally { owner.applying = false; }
        }
        private static bool Kill(Vessel vessel, string name)
        {
            ProtoCrewMember? crew;
            if (vessel.loaded)
            {
                Part? part = vessel.parts.FirstOrDefault(p => p.protoModuleCrew.Any(c => c.name == name));
                crew = part?.protoModuleCrew.FirstOrDefault(c => c.name == name);
                if (part == null || crew == null) return false;
                // Die must see the IVA KerbalRef to update its portrait. Removing
                // the seat first can destroy that reference. RemoveCrewmember
                // sets Available, so restore Dead after its trait/seat cleanup.
                if (crew.rosterStatus != ProtoCrewMember.RosterStatus.Dead) crew.Die();
                part.RemoveCrewmember(crew); crew.rosterStatus = ProtoCrewMember.RosterStatus.Dead;
                // An empty EVA proto cannot be loaded by stock (LoadEVAInfoBySuitGender
                // indexes its first crew member). Remove the dead suit through stock.
                if (vessel.isEVA && part.State != PartStates.DEAD) part.Die();
            }
            else
            {
                ProtoPartSnapshot? part = vessel.protoVessel?.protoPartSnapshots.FirstOrDefault(p => p.protoModuleCrew.Any(c => c.name == name));
                crew = part?.protoModuleCrew.FirstOrDefault(c => c.name == name);
                if (part == null || crew == null) return false;
                part.RemoveCrew(crew); vessel.RemoveCrew(crew);
                if (crew.rosterStatus != ProtoCrewMember.RosterStatus.Dead) crew.Die();
                if (vessel.isEVA) vessel.Die();
            }
            return true;
        }
        internal static bool Tracking(Guid vesselId) => Current?.vessels.ContainsKey(vesselId) == true;
        internal static DeadlyAirState? Read(Guid vesselId) => Current?.vessels.TryGetValue(vesselId, out var e) == true ? e.State : null;
        internal static DeadlyHealth? Health(string name) => Current?.health.TryGetValue(name, out var hp) == true ? hp : null;
        internal static DeadlyEva? Eva(string name) => Current?.evas.TryGetValue(name, out var eva) == true ? eva : null;
        internal static double? HealthFraction(string name) => OptionsAt(Planetarium.GetUniversalTime()).Enabled && Health(name) != null ?
            Health(name)!.Points / DeadlyHealth.Maximum : (double?)null;
        internal static double? AirFraction(Guid id, out bool lastKnown)
        {
            lastKnown = false;
            var owner = Current;
            if (!OptionsAt(Planetarium.GetUniversalTime()).Enabled || owner == null || !owner.vessels.TryGetValue(id, out var e)) return null;
            var air = e.Members.Select(Eva).FirstOrDefault(s => s != null)?.Air ?? e.State;
            if (!air.Initialized) return null;
            lastKnown = air.UT < Planetarium.GetUniversalTime();
            return air.Air / DeadlyAirState.AirMaximum;
        }
        internal static string Warning(Guid vesselId) => Current?.vessels.TryGetValue(vesselId, out var e) == true &&
            OptionsAt(Planetarium.GetUniversalTime()).Enabled && KomfortRuntime.OptionsAt(Planetarium.GetUniversalTime()).Enabled &&
            (!e.State.Emergency && e.State.Episode || e.Members.Any(n => Eva(n)?.Air.Episode == true)) ? Localizer.Format("#Koffee_Deadly_Warning") : "";
        internal static string Display(Guid vesselId)
        {
            if (!OptionsAt(Planetarium.GetUniversalTime()).Enabled || Read(vesselId) == null) return "";
            var state = Read(vesselId)!;
            return Localizer.Format("#Koffee_Health_Air" + (state.Air <= 0 ? "Dangerous" : state.Air >= DeadlyAirState.AirMaximum ? "Clean" : "Deteriorating"));
        }
        public void Update()
        {
            if (Current != this || HighLogic.LoadedScene == GameScenes.FLIGHT && !FlightGlobals.ready ||
                (HighLogic.LoadedScene != GameScenes.FLIGHT && HighLogic.LoadedScene != GameScenes.SPACECENTER &&
                HighLogic.LoadedScene != GameScenes.TRACKSTATION)) return;
            foreach (var pair in vessels.Where(p => p.Value.CrewDirty).ToArray())
            {
                pair.Value.CrewDirty = false;
                Vessel? changed = FlightGlobals.Vessels.FirstOrDefault(v => v != null && v.id == pair.Key);
                if (changed != null) GameEvents.onVesselWasModified.Fire(changed);
            }
            foreach (var pair in vessels.Where(p => p.Value.Pending != DeadlyNotice.None || p.Value.ShortagePending).ToArray())
            {
                var e = pair.Value;
                if (e.State.Emergency) e.ShortagePending = false;
                if (!e.ShortagePending && e.Pending == DeadlyNotice.None) continue;
                string message = e.ShortagePending ? Localizer.Format("#Koffee_Health_NoticeShortage", e.Name) : "";
                if (e.Pending != DeadlyNotice.None)
                    message += (message == "" ? "" : "\n") + Localizer.Format("#Koffee_Health_Notice" + e.Pending, e.Name, e.FirstDeath);
                // Warp advances the whole fleet, including unloaded vessels.
                if (TimeWarp.CurrentRateIndex > 0 || TimeWarp.CurrentRate > 1) TimeWarp.SetRate(0, true);
                PopupDialog.SpawnPopupDialog(new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                    new MultiOptionDialog("KoffeeDeadly" + pair.Key.ToString("N") + e.Pending, message,
                        Localizer.Format("#Koffee_Deadly_Settings"), HighLogic.UISkin,
                        new DialogGUIButton(Localizer.Format("#autoLOC_190905"), () => { })), false, HighLogic.UISkin, false);
                e.Pending = DeadlyNotice.None; e.FirstDeath = ""; e.ShortagePending = false;
            }
        }
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            foreach (string key in new[] { "DEADLY_AIR", "DEADLY_HEALTH", "DEADLY_EVA" }) node.RemoveNodes(key);
            foreach (var item in opaque) node.AddNode(item.CreateCopy());
            foreach (var pair in vessels)
            {
                var n = node.AddNode("DEADLY_AIR"); n.AddValue("id", pair.Key.ToString("N"));
                n.AddValue("name", pair.Value.Name); n.AddValue("pending", pair.Value.Pending.ToString());
                n.AddValue("deaths", pair.Value.FirstDeath); n.AddValue("shortagePending", pair.Value.ShortagePending.ToString());
                foreach (string member in pair.Value.Members) n.AddValue("crew", member);
                foreach (uint part in pair.Value.Parts.Where(p => partOrigins.TryGetValue(p, out Guid id) && id == pair.Key)) n.AddValue("part", part.ToString(CultureInfo.InvariantCulture));
                DeadlyStateCodec.Write(n, pair.Value.State);
            }
            foreach (var pair in health)
            {
                var n = node.AddNode("DEADLY_HEALTH"); n.AddValue("crew", pair.Key); n.AddValue("schema", "2");
                DeadlyStateCodec.Number(n, "hp", pair.Value.Points); DeadlyStateCodec.Number(n, "ut", pair.Value.UT);
                n.AddValue("recovering", pair.Value.Recovering.ToString()); n.AddValue("dead", pair.Value.DeathApplied.ToString());
                n.AddValue("criticalAnnounced", pair.Value.CriticalAnnounced.ToString());
            }
            foreach (var pair in evas)
            {
                var n = node.AddNode("DEADLY_EVA"); n.AddValue("crew", pair.Key);
                DeadlyStateCodec.Number(n, "filter", pair.Value.Filter); DeadlyStateCodec.Write(n, pair.Value.Air);
            }
        }
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node); game = HighLogic.CurrentGame; current = this;
            vessels.Clear(); health.Clear(); evas.Clear(); partOrigins.Clear(); opaque.Clear(); blocked = false;
            // Legacy DEADLY_VESSEL/ORIGIN are intentionally outside schema 2.
            // No migration or conversion of common reserves into personal HP.
            foreach (var n in node.GetNodes("DEADLY_AIR"))
            {
                var parts = new List<uint>();
                bool validParts = true;
                foreach (string value in n.GetValues("part"))
                    if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint idPart) || idPart == 0 || parts.Contains(idPart)) validParts = false;
                    else parts.Add(idPart);
                string[] members = n.GetValues("crew");
                if (!validParts || parts.Any(partOrigins.ContainsKey) || !DeadlyStateCodec.One(n, "id") || !Guid.TryParseExact(n.GetValue("id"), "N", out Guid id) || id == Guid.Empty ||
                    vessels.ContainsKey(id) || !DeadlyStateCodec.TryRead(n, out var air) || !DeadlyStateCodec.One(n, "name") ||
                    !DeadlyStateCodec.One(n, "pending") || !Enum.TryParse(n.GetValue("pending"), out DeadlyNotice pending) || !Enum.IsDefined(typeof(DeadlyNotice), pending) ||
                    !DeadlyStateCodec.One(n, "deaths") || pending == DeadlyNotice.FirstDeath && string.IsNullOrWhiteSpace(n.GetValue("deaths")) ||
                    !DeadlyStateCodec.Boolean(n, "shortagePending", out bool shortagePending) || members.Any(string.IsNullOrWhiteSpace) || members.Distinct(StringComparer.Ordinal).Count() != members.Length)
                { Preserve(n); continue; }
                vessels.Add(id, new Entry { State = air, Parts = parts.ToArray(), Members = members, Name = n.GetValue("name"),
                    Pending = pending, FirstDeath = n.GetValue("deaths"), ShortagePending = shortagePending });
                foreach (uint part in parts) partOrigins.Add(part, id);
            }
            // Old empty donor entries can still list parts that have since moved;
            // only the last saved physical owner is authoritative.
            foreach (var n in node.GetNodes("DEADLY_HEALTH"))
            {
                string name = n.GetValue("crew") ?? ""; var hp = new DeadlyHealth();
                if (!DeadlyStateCodec.One(n, "crew") || string.IsNullOrWhiteSpace(name) || health.ContainsKey(name) ||
                    !DeadlyStateCodec.One(n, "schema") || n.GetValue("schema") != "2" ||
                    !DeadlyStateCodec.ReadNumber(n, "hp", DeadlyHealth.Maximum, out hp.Points) || !DeadlyStateCodec.ReadNumber(n, "ut", double.MaxValue, out hp.UT) ||
                    !DeadlyStateCodec.Boolean(n, "recovering", out hp.Recovering) || !DeadlyStateCodec.Boolean(n, "dead", out hp.DeathApplied) ||
                    !DeadlyStateCodec.Boolean(n, "criticalAnnounced", out hp.CriticalAnnounced) || hp.DeathApplied && hp.Points != 0)
                { Preserve(n); continue; }
                health.Add(name, hp);
            }
            foreach (var n in node.GetNodes("DEADLY_EVA"))
            {
                string name = n.GetValue("crew") ?? "";
                if (!DeadlyStateCodec.One(n, "crew") || !health.ContainsKey(name) || evas.ContainsKey(name) ||
                    !DeadlyStateCodec.TryRead(n, out var air) || !DeadlyStateCodec.ReadNumber(n, "filter", DeadlyEva.FilterMaximum, out double filter))
                { Preserve(n); continue; }
                var suit = new DeadlyEva { Filter = filter }; suit.Air.Start(air.UT); suit.Air.Air = air.Air; suit.Air.Episode = air.Episode;
                evas.Add(name, suit);
            }
            if (vessels.Values.SelectMany(e => e.Members).Any(n => !health.ContainsKey(n)))
            { blocked = true; Debug.LogError("[Koffee] Missing personal Deadly state; processing disabled."); }
        }
        private void Preserve(ConfigNode child)
        { opaque.Add(child.CreateCopy()); blocked = true; Debug.LogError("[Koffee] Ambiguous Deadly save preserved; processing disabled."); }
    }
}

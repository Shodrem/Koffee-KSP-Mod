using System;
using System.Linq;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    public sealed class KerbalSalaryScenario : ScenarioModule
    {
        private static KerbalSalaryScenario? current;
        private Game? game;
        private readonly KerbalSalaryLedger ledger = new KerbalSalaryLedger();
        private double nextObservation;
        private readonly System.Collections.Generic.HashSet<ProtoVessel> recovering = new System.Collections.Generic.HashSet<ProtoVessel>();
        private static KerbalSalaryScenario? Current => current != null && ReferenceEquals(current.game, HighLogic.CurrentGame) ? current : null;
        private KoffeeGameParameters Settings => game!.Parameters.CustomParams<KoffeeGameParameters>();
        private bool Enabled => game?.Mode == Game.Modes.CAREER && Settings.SalaryEnabled;

        public override void OnAwake()
        {
            base.OnAwake(); game = HighLogic.CurrentGame; current = this;
            GameEvents.onLaunch.Add(Launched); GameEvents.onCrewKilled.Add(Killed);
            GameEvents.onVesselRecoveryProcessingComplete.Add(Recovered); GameEvents.onKerbalNameChanged.Add(Renamed);
        }
        public void OnDestroy()
        {
            GameEvents.onLaunch.Remove(Launched); GameEvents.onCrewKilled.Remove(Killed);
            GameEvents.onVesselRecoveryProcessingComplete.Remove(Recovered); GameEvents.onKerbalNameChanged.Remove(Renamed);
            if (ReferenceEquals(current, this)) current = null;
        }
        public override void OnLoad(ConfigNode node) { base.OnLoad(node); ledger.Load(node); nextObservation = 0; }
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (Current == this) ledger.SetEnabled(Enabled, Planetarium.GetUniversalTime());
            ledger.Save(node);
        }
        internal static void SettingsChanged(double now) => Current?.ledger.SetEnabled(Current.Enabled, now);
        internal static bool TryRead(string name, out double amount, out double dailyRate, out int level)
        {
            amount = dailyRate = 0; level = 0;
            var owner = Current;
            if (owner == null || owner.game?.Mode != Game.Modes.CAREER ||
                !owner.ledger.Entries.TryGetValue(name, out var entry) || entry.Dead) return false;
            amount = entry.Amount(Planetarium.GetUniversalTime()); dailyRate = entry.DailyRate; level = entry.DepartureLevel;
            return true;
        }
        private void BeginMission(ProtoCrewMember crew, double now)
        {
            if (crew == null || crew.type != ProtoCrewMember.KerbalType.Crew || crew.rosterStatus == ProtoCrewMember.RosterStatus.Dead) return;
            ledger.Start(crew.name, crew.experienceLevel, now,
                Settings.SalaryBaseValue + Settings.SalaryPerLevelValue * crew.experienceLevel, Enabled);
        }
        private void Launched(EventReport report)
        {
            if (Current != this || game?.Mode != Game.Modes.CAREER) return;
            var vessel = report?.origin?.vessel ?? FlightGlobals.ActiveVessel;
            if (vessel == null) return;
            foreach (var crew in vessel.GetVesselCrew())
            {
                // A stock respawn starts a new contract, never resurrects the forfeited salary.
                if (crew.rosterStatus != ProtoCrewMember.RosterStatus.Dead && ledger.Entries.TryGetValue(crew.name, out var old) && old.Dead) ledger.Entries.Remove(crew.name);
                BeginMission(crew, Planetarium.GetUniversalTime());
            }
        }
        public void Update()
        {
            if (Current != this || game?.Mode != Game.Modes.CAREER || (HighLogic.LoadedSceneIsFlight && !FlightGlobals.ready)) return;
            double now = Planetarium.GetUniversalTime();
            if (now < nextObservation && now >= nextObservation - 1) return;
            nextObservation = now + 1;
            foreach (var crew in game.CrewRoster?.Kerbals() ?? Enumerable.Empty<ProtoCrewMember>())
                if (crew.rosterStatus == ProtoCrewMember.RosterStatus.Dead && ledger.Entries.ContainsKey(crew.name)) Death(crew.name);
            ledger.SetEnabled(Enabled, now);
        }
        private void Killed(EventReport report)
        {
            if (Current != this || game?.Mode != Game.Modes.CAREER || report == null) return;
            var crew = game.CrewRoster?.Kerbals().FirstOrDefault(c => c.name == report.sender);
            if (crew == null || crew.type != ProtoCrewMember.KerbalType.Crew) return;
            ledger.Start(crew.name, crew.experienceLevel, Planetarium.GetUniversalTime(), 0, false);
            Death(crew.name);
        }
        private void Death(string name)
        {
            if (!ledger.Die(name)) return;
            if (Enabled && Reputation.Instance != null)
            {
                float rep = Reputation.Instance.reputation;
                Reputation.Instance.SetReputation(rep - Math.Abs(rep) * .05f, TransactionReasons.VesselLoss);
            }
            Debug.Log("[Koffee] Salary forfeited on death: " + name);
        }
        private void Recovered(ProtoVessel vessel, KSP.UI.Screens.MissionRecoveryDialog? dialog, float recoveryFactor)
        {
            if (vessel == null || !recovering.Add(vessel)) return;
            try { Settle(vessel, dialog); }
            finally { recovering.Remove(vessel); }
        }
        private void Settle(ProtoVessel vessel, KSP.UI.Screens.MissionRecoveryDialog? dialog)
        {
            if (Current != this || vessel == null || game?.Mode != Game.Modes.CAREER) return;
            var body = FlightGlobals.Bodies.FirstOrDefault(b => b.flightGlobalsIndex == vessel.orbitSnapShot?.ReferenceBodyIndex);
            if (body == null || body.bodyName != "Kerbin") return;
            var receipt = new SalaryRecoveryReceipt();
            double now = Planetarium.GetUniversalTime();
            foreach (var crew in vessel.GetVesselCrew())
            {
                if (crew == null || !ledger.Entries.TryGetValue(crew.name, out var entry) || entry.Dead) continue;
                if (crew.rosterStatus == ProtoCrewMember.RosterStatus.Dead) { Death(crew.name); continue; }
                if (Enabled && Funding.Instance == null) continue;
                if (Enabled) receipt.Crew.Add(new SalaryRecoveryRow(crew.name, entry.Days(now), entry.DailyRate, entry.Amount(now)));
                else
                {
                    ledger.Entries.Remove(crew.name);
                    Debug.Log("[Koffee] Salary cleared while disabled: " + crew.name);
                }
            }
            if (receipt.Crew.Count == 0) return;
            bool captured = true;
            foreach (var row in receipt.Crew)
            {
                // Consume only this payment before its callbacks. Unprocessed
                // crew keep their records if a currency listener throws.
                ledger.Entries.Remove(row.Name);
                bool observed = true;
                row.Charged = row.Salary == 0 ? 0 : SalaryPaymentCapture.Charge(row.Salary, out observed);
                captured &= observed;
                Debug.Log("[Koffee] Salary settled on Kerbin recovery: " + row.Name + " = " + row.Charged);
            }
            if (!captured) Debug.LogWarning("[Koffee] Salary paid; effective currency capture unavailable, receipt omitted.");
            if (dialog != null) dialog.totalFunds = Funding.Instance!.Funds;
            if (dialog != null && captured)
            {
                receipt.RecoveredFunds = dialog.fundsEarned;
                receipt.NetFunds = receipt.RecoveredFunds - receipt.Charged;
                receipt.TotalFunds = Funding.Instance!.Funds;
                SalaryRecoveryUI.Attach(dialog, receipt);
            }
        }
        private void Renamed(ProtoCrewMember crew, string oldName, string newName)
        { if (Current == this) ledger.Rename(oldName, newName); }
    }
}

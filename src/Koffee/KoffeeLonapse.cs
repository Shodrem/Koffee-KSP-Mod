using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class KoffeeLonapse
    {
        internal const string ExperimentStart = "koffee.experiment_started";
        internal const string EmergencyOn = "koffee.emergency_support";
        internal const string EmergencyOff = "koffee.emergency_stopped";
        internal const string CoffeeDrank = "koffee.coffee_drank";
        internal const string CoffeeEmpty = "koffee.coffee_empty";
        internal const string MaintenanceDone = "koffee.maintenance_done";
        internal const string RepairDone = "koffee.repair_done";
        private static readonly Dictionary<string, double> pending = new Dictionary<string, double>();
        private static Vessel? context;
        private static Game? game;
        private static double lastCoffee = double.NegativeInfinity, lastMaintenance = double.NegativeInfinity;
        private static double nextCoffeeCheck;

        // No catch-up or background chatter. Lonapse's MC context is the active vessel.
        private static bool Audible(Vessel? vessel, bool maintenanceEva = false) => HighLogic.LoadedSceneIsFlight &&
            vessel?.loaded == true && (!vessel.isEVA || maintenanceEva) && ReferenceEquals(vessel, FlightGlobals.ActiveVessel) &&
            vessel.GetVesselCrew().Count > 0;

        private static void Cancel(string id) { pending.Remove(id); LonapseBridge.Cancel(id); }

        internal static void Reset()
        {
            foreach (string id in pending.Keys.ToArray()) Cancel(id);
            context = null; game = HighLogic.CurrentGame;
            lastCoffee = lastMaintenance = double.NegativeInfinity;
            nextCoffeeCheck = 0;
        }

        private static void Offer(string id, Vessel vessel, ProtoCrewMember? kerbal)
        {
            if (!ReferenceEquals(game, HighLogic.CurrentGame)) Reset();
            if (context != null && !ReferenceEquals(context, vessel))
                foreach (string old in pending.Keys.ToArray()) Cancel(old);
            context = vessel;
            if (LonapseBridge.Trigger(id, vessel, kerbal)) pending[id] = Time.realtimeSinceStartup + 90;
        }

        // Only cancel stale offers here. Loading, polling and changing vessels never emit dialogue.
        internal static void Update()
        {
            try
            {
                if (!ReferenceEquals(game, HighLogic.CurrentGame)) { Reset(); return; }
                if (pending.Count == 0) return;
                foreach (var offer in pending.ToArray())
                {
                    string id = offer.Key;
                    bool eva = id == MaintenanceDone || id == RepairDone;
                    if (Time.realtimeSinceStartup >= offer.Value || !Audible(context, eva) ||
                        id == EmergencyOff && DeadlyScenario.Active(context)) Cancel(id);
                    else if (id == CoffeeEmpty && Time.realtimeSinceStartup >= nextCoffeeCheck)
                    {
                        nextCoffeeCheck = Time.realtimeSinceStartup + 1;
                        if (KoffeeSources.Available(context!) > 0) Cancel(id);
                    }
                }
            }
            catch (Exception) { } // Optional radio observations never affect resource accounting.
        }

        internal sealed class CoffeeObservation
        {
            private readonly Vessel vessel;
            private readonly ProtoCrewMember kerbal;
            private readonly Game? owner;
            private readonly Part[] parts;
            private readonly double at, before;
            private double after = double.NaN;
            internal CoffeeObservation(Vessel vessel, ProtoCrewMember kerbal, double at, double before)
            {
                this.vessel = vessel; this.kerbal = kerbal; this.at = at; this.before = before;
                owner = HighLogic.CurrentGame; parts = vessel.parts.ToArray();
            }
            internal void Debited(double cost)
            {
                try
                {
                    double remaining = KoffeeSources.Available(vessel);
                    if (before - remaining == cost) after = remaining;
                }
                catch (Exception) { }
            }
            internal void Committed()
            {
                try
                {
                    if (!ReferenceEquals(owner, HighLogic.CurrentGame) || at != Planetarium.GetUniversalTime() ||
                        !Audible(vessel) || !vessel.GetVesselCrew().Contains(kerbal) || !parts.SequenceEqual(vessel.parts)) return;
                    if (!ReferenceEquals(game, HighLogic.CurrentGame)) Reset();
                    bool empty = before > 0 && after == 0 && KoffeeSources.Available(vessel) == 0;
                    if (empty) Cancel(CoffeeDrank);
                    if (Time.realtimeSinceStartup - lastCoffee < 900) return;
                    // Stamp attempts too: a rejected radio offer is not retried by the next worker.
                    lastCoffee = Time.realtimeSinceStartup;
                    Offer(empty ? CoffeeEmpty : CoffeeDrank, vessel, kerbal);
                }
                catch (Exception) { }
            }
        }

        internal static CoffeeObservation? BeforeCoffee(ProtoCrewMember kerbal, Part consumer, double at)
        {
            try
            {
                var vessel = consumer.vessel;
                if (at != Planetarium.GetUniversalTime() || !Audible(vessel) || !vessel.GetVesselCrew().Contains(kerbal)) return null;
                return new CoffeeObservation(vessel, kerbal, at, KoffeeSources.Available(vessel));
            }
            catch (Exception) { return null; }
        }

        internal static void MaintenanceCompleted(Part serviced, ProtoCrewMember engineer, bool preventive, double at)
        {
            try
            {
                var eva = FlightGlobals.ActiveVessel;
                double age = Planetarium.GetUniversalTime() - at;
                if (!Audible(eva, maintenanceEva: true) || !eva.isEVA || age < 0 || age > 1 ||
                    !ReferenceEquals(MaintenanceEva.Nearby(serviced, engineer.name), engineer)) return;
                if (!ReferenceEquals(game, HighLogic.CurrentGame)) Reset();
                if (Time.realtimeSinceStartup - lastMaintenance < 600) return;
                lastMaintenance = Time.realtimeSinceStartup;
                Offer(preventive ? MaintenanceDone : RepairDone, eva, engineer);
            }
            catch (Exception) { }
        }

        internal static void ExperimentStarted(ModuleKoffeeExperiment experiment)
        {
            Vessel vessel = experiment.vessel;
            if (!Audible(vessel)) return;
            var scientist = vessel.GetVesselCrew().FirstOrDefault(k => k.name == experiment.State?.AssignedKerbalName);
            Offer(ExperimentStart, vessel, scientist);
        }

        internal static void EmergencyChanged(Vessel vessel, bool enabled)
        {
            if (!Audible(vessel)) return;
            if (enabled)
            {
                Cancel(ExperimentStart);
                Cancel(EmergencyOff);
                Cancel(CoffeeDrank);
                Cancel(CoffeeEmpty);
                Offer(EmergencyOn, vessel, null);
            }
            else
            {
                Cancel(EmergencyOn);
                Offer(EmergencyOff, vessel, null);
            }
        }
    }
}

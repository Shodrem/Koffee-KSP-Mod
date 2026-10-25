using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames,
        GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    public sealed class KerbalKoffeeScenario : ScenarioModule
    {
        internal static readonly KerbalKoffeeLedger Store = new KerbalKoffeeLedger();
        private Game? owner;
        private bool subscribed;

        public override void OnAwake()
        {
            base.OnAwake();
            owner = HighLogic.CurrentGame;
            if (!subscribed)
            {
                GameEvents.onVesselRecovered.Add(OnVesselRecovered);
                subscribed = true;
            }
            EngineeringRenewalCoordinator.Clear();
            Store.Clear();
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            owner = HighLogic.CurrentGame;
            EngineeringRenewalCoordinator.Clear();
            var records = new List<KoffeeStateRecord>();
            foreach (ConfigNode kerbal in node.GetNodes(KoffeeIdentifiers.SavedKerbalNode))
            {
                string name = kerbal.GetValue(KoffeeIdentifiers.NameValue) ?? "";
                string startText = kerbal.GetValue(KoffeeIdentifiers.StartUtValue);
                double startUT = 0;
                bool hasStart = !string.IsNullOrEmpty(name) &&
                    double.TryParse(startText, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out startUT) &&
                    !double.IsNaN(startUT) && !double.IsInfinity(startUT);
                if (!hasStart) continue;
                records.Add(new KoffeeStateRecord(name, startUT));
            }
            Store.Load(records);
            Debug.Log("[Koffee] Loaded " + records.Count + " saved Kaffeinated state(s).");
        }

        private void OnVesselRecovered(ProtoVessel vessel, bool quick)
        {
            if (vessel == null || owner == null || !ReferenceEquals(owner, HighLogic.CurrentGame)) return;
            // Use the same proto crew list as stock recovery, including unloaded vessels and EVA.
            foreach (ProtoCrewMember crew in vessel.GetVesselCrew())
            {
                if (crew == null || !Store.TryGetStart(crew.name, out _)) continue;
                Store.RestoreStart(crew.name, null);
                Debug.Log("[Koffee] " + crew.name + " Kaffeinated cleared on vessel recovery.");
            }
        }

        internal void OnDestroy()
        {
            if (!subscribed) return;
            GameEvents.onVesselRecovered.Remove(OnVesselRecovered);
            subscribed = false;
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            node.RemoveNodes(KoffeeIdentifiers.SavedKerbalNode);
            foreach (KoffeeStateRecord record in Store.Save())
            {
                ConfigNode kerbal = node.AddNode(KoffeeIdentifiers.SavedKerbalNode);
                kerbal.AddValue(KoffeeIdentifiers.NameValue, record.Name);
                kerbal.AddValue(KoffeeIdentifiers.StartUtValue,
                    record.StartUT.ToString("R", CultureInfo.InvariantCulture));
            }
        }
    }
}

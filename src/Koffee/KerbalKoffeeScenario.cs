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

        public override void OnAwake()
        {
            base.OnAwake();
            EngineeringRenewalCoordinator.Clear();
            Store.Clear();
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
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

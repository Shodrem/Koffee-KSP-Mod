using System;
using System.Globalization;
using System.Linq;
using ContractConfigurator;
using ContractConfigurator.ExpressionParser;
using ContractConfigurator.Parameters;
using Contracts;
using KSP.Localization;
using ShoAerospace.Koffee;
using UnityEngine;

[assembly: KSPAssemblyDependency("ContractConfigurator", 2, 0)]
[assembly: KSPAssemblyDependency("Koffee", 1, 0)]

namespace ShoAerospace.Koffee.Contracts
{
    // CC discovers factories and expression registrars by type, without a startup addon.
    public sealed class KoffeeExpressions : IExpressionParserRegistrer
    {
        public void RegisterExpressionParsers()
        {
            ExpressionParser<Vessel>.RegisterMethod(new Method<Vessel, string, bool>("KoffeeCanExpand",
                (vessel, capability) => StationContractChecks.CanExpand(vessel, capability) &&
                    TutorialComplete(vessel.mainBody), false));
        }
        private static bool TutorialComplete(CelestialBody body) => body != null &&
            ConfiguredContract.CompletedContractsByName.TryGetValue("KoffeeStationTutorial", out var completed) &&
            completed.Any(c => c.targetBody == body);
    }

    public sealed class KoffeeHardwareRequirement : ContractRequirement
    {
        private string capability = "Any";
        public override bool LoadFromConfig(ConfigNode node)
        {
            bool valid = base.LoadFromConfig(node);
            valid &= ConfigNodeUtil.ParseValue<string>(node, "capability", v => capability = v, this, "Any",
                v => StationContractChecks.Capability(v) >= 0);
            return valid;
        }
        public override bool RequirementMet(ConfiguredContract contract) => StationContractChecks.HardwareAvailable(capability);
        protected override string RequirementText() => Localizer.Format("#Koffee_Contract_Hardware");
        public override void OnSave(ConfigNode node) { node.AddValue("capability", capability); }
        public override void OnLoad(ConfigNode node) { capability = node.GetValue("capability") ?? "Invalid"; }
    }

    public sealed class KoffeeLaboratoryFactory : ParameterFactory
    {
        private string capability = "Any";
        public override bool Load(ConfigNode node)
        {
            bool valid = base.Load(node);
            valid &= ConfigNodeUtil.ParseValue<string>(node, "capability", v => capability = v, this, "Any",
                v => StationContractChecks.Capability(v) >= 0);
            return valid;
        }
        public override ContractParameter Generate(Contract contract) => new KoffeeStationParameter(title, capability, false, 0);
    }

    public sealed class KoffeeAssignedEngineerFactory : ParameterFactory
    {
        public override ContractParameter Generate(Contract contract) => new KoffeeStationParameter(title, "Any", true, 0);
    }

    public sealed class KoffeeStationAnchorFactory : ParameterFactory
    {
        private Vessel? targetStation;
        public override bool Load(ConfigNode node)
        {
            bool valid = base.Load(node);
            valid &= ConfigNodeUtil.ParseValue<Vessel>(node, "targetStation", v => targetStation = v, this);
            return valid;
        }
        public override ContractParameter Generate(Contract contract) => new KoffeeStationParameter(title, "Any", false,
            StationContractChecks.LaboratoryAnchor(targetStation!), true);
    }

    public sealed class KoffeeStationParameter : VesselParameter
    {
        private string capability = "Invalid";
        private bool engineer, anchored;
        private bool configurationValid;
        private uint anchor;
        private float nextCheck;
        public KoffeeStationParameter() { }
        public KoffeeStationParameter(string title, string capability, bool engineer, uint anchor, bool anchored = false) : base(title)
        {
            this.capability = capability; this.engineer = engineer; this.anchor = anchor;
            this.anchored = anchored;
            configurationValid = ValidConfiguration();
        }
        private bool ValidConfiguration() => StationContractChecks.Capability(capability) >= 0 &&
            !(engineer && anchored) && (anchored ? anchor != 0 : anchor == 0);

        protected override bool VesselMeetsCondition(Vessel vessel) => configurationValid && (anchored
            ? StationContractChecks.ContainsAnchor(vessel, anchor)
            : engineer ? StationContractChecks.HasAssignedEngineer(vessel)
            : StationContractChecks.HasLaboratory(vessel, capability));

        public override void SetState(Vessel vessel)
        {
            base.SetState(vessel);
            // CC can restore or inherit a cached success while choosing a vessel for the group.
            // Its base setter copies that cache without calling VesselMeetsCondition again.
            if (state == ParameterState.Complete && !VesselMeetsCondition(vessel)) state = ParameterState.Incomplete;
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + 1f;
            // Assignment has no stock crew-transfer event. Poll actual state, including unloaded vessels.
            if (HighLogic.CurrentGame == null || FlightGlobals.Vessels == null) return;
            foreach (Vessel vessel in FlightGlobals.Vessels.Where(v => v != null).ToArray()) CheckVessel(vessel);
        }
        protected override void OnParameterSave(ConfigNode node)
        {
            base.OnParameterSave(node);
            node.AddValue("capability", capability); node.AddValue("engineer", engineer);
            node.AddValue("anchored", anchored); node.AddValue("anchor", anchor);
        }
        protected override void OnParameterLoad(ConfigNode node)
        {
            base.OnParameterLoad(node);
            capability = node.GetValue("capability") ?? "Invalid";
            bool validEngineer = bool.TryParse(node.GetValue("engineer"), out engineer);
            bool validAnchored = bool.TryParse(node.GetValue("anchored"), out anchored);
            bool validAnchor = uint.TryParse(node.GetValue("anchor"), NumberStyles.None, CultureInfo.InvariantCulture, out anchor);
            configurationValid = validEngineer && validAnchored && validAnchor && ValidConfiguration() &&
                new[] { "capability", "engineer", "anchored", "anchor" }.All(key => node.GetValues(key).Length == 1);
        }
    }
}

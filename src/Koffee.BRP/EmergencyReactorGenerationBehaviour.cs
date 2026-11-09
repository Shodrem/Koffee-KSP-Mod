using System;
using System.Globalization;
using System.Linq;
using BackgroundResourceProcessing;
using BackgroundResourceProcessing.Behaviour;

namespace ShoAerospace.Koffee.Brp
{
    // Returned by the native SystemHeat adapter so BRP retains its part-local
    // NO_FLOW inventories and native OnRestore clock reset. The recipe remains
    // registered while off, allowing a later emergency activation without reload.
    public sealed class EmergencyReactorGenerationBehaviour : ConverterBehaviour
    {
        [KSPField(isPersistant = true)] public uint PartId;
        [KSPField(isPersistant = true)] public double ElectricalRate, WasteHeatRate, FuelRate;
        [KSPField(isPersistant = true)] public int LoopId;
        private bool active;

        private bool CanRun()
        {
            var parts = Vessel.protoVessel?.protoPartSnapshots.Where(p => p.persistentId == PartId).ToArray();
            if (parts?.Length != 1) return false;
            var reactors = parts[0].modules.Where(m => m.moduleName == EmergencyNfeReactor.ModuleName).ToArray();
            if (reactors.Length != 1 || !bool.TryParse(reactors[0].moduleValues.GetValue("Enabled"), out bool enabled) || !enabled) return false;
            var node = reactors[0].moduleValues;
            if (node.HasValue("CoreIntegrity") && (!double.TryParse(node.GetValue("CoreIntegrity"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double integrity) || !EngineeringMath.Finite(integrity) || integrity <= 0)) return false;
            var processor = Vessel.FindVesselModuleImplementing<BackgroundResourceProcessor>();
            if (processor == null) return false;
            var local = processor.Inventories.Where(i => i.FlightId == parts[0].flightID && i.ModuleId == null).ToArray();
            // Explicitly gate tiny fuel rates as well as letting the native solver
            // debit them. Never let solver tolerances turn an empty core into EC.
            return local.Any(i => i.ResourceName == "EnrichedUranium" && i.Amount > EmergencyNfeReactor.FuelReserve) &&
                local.Any(i => i.ResourceName == "DepletedFuel" && i.Available > EmergencyNfeReactor.FuelReserve);
        }

        public override ConverterResources GetResources(VesselState state)
        {
            var result = new ConverterResources { NextChangepoint = KomfortRuntime.NextBoundary(Vessel, state.CurrentTime) };
            if (!EngineeringMath.Finite(ElectricalRate) || ElectricalRate <= 0 || !EngineeringMath.Finite(WasteHeatRate) || WasteHeatRate < 0 ||
                !EngineeringMath.Finite(FuelRate) || FuelRate <= 0) return result;
            active = CanRun();
            double scale = active ? 1 : 0;
            // Full manual output matches the loaded native reactor, including its
            // fuel debit even when surplus electricity has to be discarded.
            // Retain resource keys while off so BRP links NO_FLOW fuel to this
            // part at record time, before a later automatic emergency start.
            result.Inputs.Add(new ResourceRatio("EnrichedUranium", FuelRate * scale, false, ResourceFlowMode.NO_FLOW));
            result.Outputs.Add(new ResourceRatio("DepletedFuel", FuelRate * scale, false, ResourceFlowMode.NO_FLOW));
            result.Outputs.Add(new ResourceRatio("ElectricCharge", ElectricalRate * scale, true, ResourceFlowMode.ALL_VESSEL));
            if (WasteHeatRate > 0)
                result.Outputs.Add(new ResourceRatio("BRPSystemHeatFlux" + LoopId.ToString(CultureInfo.InvariantCulture), WasteHeatRate * scale, true, ResourceFlowMode.ALL_VESSEL));
            return result;
        }

        public override void OnRatesComputed(BackgroundResourceProcessor processor, BackgroundResourceProcessing.Core.ResourceConverter converter, RateCalculatedEvent evt)
        {
            if (active == CanRun()) return;
            converter.NextChangepoint = evt.CurrentTime;
            processor.MarkDirty(); processor.SuppressNoProgressError();
        }
    }
}

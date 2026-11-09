using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class StationElectricalDemand
    {
        internal static double Editor(Part part) => Math.Max(StationStockPower.Rate(part), StationStockPower.Rate(part, true)) + KomfortRuntime.ServiceRate(part, true);

        // Read-only current intent, never received EC or a historical average.
        // The same consumer map is used to charge, including physical-part dedup.
        internal static Dictionary<Part, double> Capture(Vessel vessel)
        {
            var demand = new Dictionary<Part, double>();
            if (vessel?.loaded != true || !StationPowerPolicy.Applies(vessel)) return demand;
            double now = Planetarium.GetUniversalTime();
            var manager = ExperimentManagerService.Select(vessel, now, false, out _, true);
            if (manager?.Part != null)
            {
                var context = manager.Part.Modules.OfType<ModuleKoffeeExperimentManager>().First();
                foreach (var snapshot in StationExperimentRuntime.Capture(vessel, includeCandidates: false))
                {
                    if (!snapshot.Module.ElectricalWorkReady || snapshot.State?.Status != ExperimentContainerStatus.Running || snapshot.WorkComplete ||
                        snapshot.Definition == null || snapshot.Reservation == null ||
                        (snapshot.PauseReason != StationExperimentPauseReason.None && snapshot.PauseReason != StationExperimentPauseReason.NoElectricity)) continue;
                    foreach (var entry in StationStockPower.Consumers(context, snapshot.Module, snapshot.Definition,
                        snapshot.Reservation.Value.Laboratory.part)) Add(demand, entry.Key, entry.Value * EngineeringContribution.ManagementFactor(vessel, manager.EngineerName, now, true));
                }
            }
            foreach (Part part in vessel.parts.Where(p => p != null && p.State != PartStates.DEAD))
                if (part.Modules.OfType<ModuleKoffeeLab>().Any(l => l.NativePowerActive()))
                    Add(demand, part, StationStockPower.Rate(part, true) * (manager == null ? 1 :
                        EngineeringContribution.ManagementFactor(vessel, manager.EngineerName, now, true)));
            bool hasCrew = vessel.GetVesselCrew().Count > 0;
            foreach (Part part in vessel.parts.Where(p => p != null && p.State != PartStates.DEAD))
            {
                double service = KomfortRuntime.ServiceRate(part, hasCrew: hasCrew);
                if (service > 0) demand[part] = (demand.TryGetValue(part, out double rate) ? rate : 0) + service;
            }
            return demand;
        }

        private static void Add(Dictionary<Part, double> demand, Part part, double rate) =>
            demand[part] = Math.Max(demand.TryGetValue(part, out double previous) ? previous : 0, rate);

        internal static void Refresh(Vessel vessel)
        {
            var demand = StationDbsIntegration.Active ? Capture(vessel) : new Dictionary<Part, double>();
            foreach (Part part in vessel.parts.Where(p => p != null))
                foreach (var marker in part.Modules.OfType<ModuleKoffeePowerConsumer>())
                {
                    if (HighLogic.LoadedSceneIsEditor) marker.dbsEditorDemand = Editor(part);
                    marker.dbsFlightDemand = demand.TryGetValue(part, out double rate) ? rate : 0;
                }
        }
    }

}

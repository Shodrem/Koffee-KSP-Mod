using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class StationElectricalDemand
    {
        internal static double Editor(Part part) => Math.Max(StationStockPower.Rate(part), StationStockPower.Rate(part, true));

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
                foreach (var snapshot in StationExperimentRuntime.Capture(vessel))
                {
                    if (!snapshot.Module.ElectricalWorkReady || snapshot.State?.Status != ExperimentContainerStatus.Running || snapshot.WorkComplete ||
                        snapshot.Definition == null || snapshot.Reservation == null ||
                        (snapshot.PauseReason != StationExperimentPauseReason.None && snapshot.PauseReason != StationExperimentPauseReason.NoElectricity)) continue;
                    foreach (var entry in StationStockPower.Consumers(context, snapshot.Module, snapshot.Definition,
                        snapshot.Reservation.Value.Laboratory.part)) Add(demand, entry.Key, entry.Value);
                }
            }
            foreach (Part part in vessel.parts.Where(p => p != null && p.State != PartStates.DEAD))
                if (part.Modules.OfType<ModuleKoffeeLab>().Any(l => l.NativePowerActive()))
                    Add(demand, part, StationStockPower.Rate(part, true));
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
                    marker.dbsEditorDemand = Editor(part);
                    marker.dbsFlightDemand = demand.TryGetValue(part, out double rate) ? rate : 0;
                }
        }
    }

    internal static class StationPowerDiagnostics
    {
        private static readonly Dictionary<Guid, float> next = new Dictionary<Guid, float>();
        internal static void Clear() => next.Clear();
        internal static void Report(PartModule context, double seconds, double rate, double requested,
            double withdrawn, double authorized, double capacity)
        {
            if (!StationPowerPolicy.HighWarp || !StationDbsIntegration.Active) return;
            float now = Time.realtimeSinceStartup;
            if (next.TryGetValue(context.vessel.id, out float at) && now < at) return;
            next[context.vessel.id] = now + 5;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[Koffee][Power] UT={0:R} vessel={1} loaded={2} packed={3} warp={4}/{5} step={6:R}s demand={7:R}EC/s requested={8:R}EC withdrawn={9:R}EC capacityWithBuffer={10:R}EC authorized={11:R}s (provisional until publication)",
                Planetarium.GetUniversalTime(), context.vessel.id, context.vessel.loaded, context.vessel.packed,
                TimeWarp.WarpMode, TimeWarp.CurrentRate, seconds, rate, requested, withdrawn, capacity, authorized));
        }
    }
}

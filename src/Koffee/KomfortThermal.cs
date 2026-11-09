namespace ShoAerospace.Koffee
{
    // Exclusive thermal backend. Crew allocation/payment belong to KomfortRuntime.
    internal static class KomfortThermal
    {
        // Native lastUT is advanced by UpdateThermodynamics before completing
        // the unloaded-time interpolation. Unity callbacks do not interleave
        // that synchronous pass. A recent timestamp AND initialized part data
        // are required; packed alone cannot distinguish loading from high warp.
        // SystemHeat uses its own readiness; stock guards apply only to stock.
        internal static string Readiness(Part? part)
        {
            if (part == null || part.State == PartStates.DEAD || part.vessel?.loaded != true ||
                !HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready) return "inactive";
            if (KomfortSystemHeat.Present) return KomfortSystemHeat.Readiness(part);
            if (part.ptd == null || !ExperimentContainerState.Finite(part.thermalMass) || part.thermalMass <= 0 ||
                !ExperimentContainerState.Finite(part.temperature) || part.temperature <= 0 ||
                !ExperimentContainerState.Finite(part.skinTemperature) || part.skinTemperature <= 0) return "initializing";
            double now = Planetarium.GetUniversalTime(), last = part.vessel.lastUT, step = TimeWarp.fixedDeltaTime;
            double minStep = PhysicsGlobals.ThermalIntegrationMinStep;
            // Stock may collect several fixed steps before exceeding minStep.
            double window = System.Math.Max(step, minStep + step);
            if (!ExperimentContainerState.Finite(now) || !ExperimentContainerState.Finite(last) || last < 0 ||
                !ExperimentContainerState.Finite(step) || step <= 0 || !ExperimentContainerState.Finite(window) ||
                now < last - 1e-6 || now - last > window * 1.1 + 1e-6) return "catchup";
            return "ready";
        }

        internal static void Apply(Part? part, double served, double kilowattsPerKerbal, double seconds,
            bool readyAtStart = true)
        {
            if (part == null || part.State == PartStates.DEAD || part.vessel?.loaded != true ||
                !HighLogic.LoadedSceneIsFlight || seconds <= 0) return;
            double step = TimeWarp.fixedDeltaTime;
            double energy = served * kilowattsPerKerbal * seconds; // kJ, already paid interval
            string state = Readiness(part);
            if (state == "ready" && !readyAtStart) state = "baseline";
            bool applied = state == "ready" && served > 0 && ExperimentContainerState.Finite(served) && kilowattsPerKerbal > 0 &&
                ExperimentContainerState.Finite(kilowattsPerKerbal) &&
                step > 0 && ExperimentContainerState.Finite(energy) && ExperimentContainerState.Finite(step);
            if (applied && KomfortSystemHeat.Present)
                applied = seconds <= step * 1.1 + 1e-6 && KomfortSystemHeat.Queue(part, energy);
            else if (applied)
                // Stock multiplies by fixedDeltaTime internally. Preserve paid
                // energy across partial-payment and settings boundaries.
                part.AddThermalFlux(energy / step);
            // Rejected intervals are discarded, never queued or replayed.
            KomfortThermalDiagnostics.Report(part, served, kilowattsPerKerbal, seconds, applied ? energy : 0, state);
        }
    }
}

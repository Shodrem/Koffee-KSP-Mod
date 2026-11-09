using System.Linq;

namespace ShoAerospace.Koffee
{
    internal static class KoffeeLonapse
    {
        internal const string ExperimentStart = "koffee.experiment_started";
        internal const string EmergencyOn = "koffee.emergency_support";

        // No catch-up or background chatter. Lonapse's MC context is the active vessel.
        private static bool Audible(Vessel? vessel) => HighLogic.LoadedSceneIsFlight &&
            vessel?.loaded == true && !vessel.isEVA && ReferenceEquals(vessel, FlightGlobals.ActiveVessel) &&
            vessel.GetVesselCrew().Count > 0;

        internal static void ExperimentStarted(ModuleKoffeeExperiment experiment)
        {
            Vessel vessel = experiment.vessel;
            if (!Audible(vessel)) return;
            var scientist = vessel.GetVesselCrew().FirstOrDefault(k => k.name == experiment.State?.AssignedKerbalName);
            LonapseBridge.Trigger(ExperimentStart, vessel, scientist);
        }

        internal static void EmergencyChanged(Vessel vessel, bool enabled)
        {
            if (!Audible(vessel)) return;
            if (enabled)
            {
                LonapseBridge.Cancel(ExperimentStart);
                LonapseBridge.Trigger(EmergencyOn, vessel, null);
            }
            else LonapseBridge.Cancel(EmergencyOn);
        }
    }
}

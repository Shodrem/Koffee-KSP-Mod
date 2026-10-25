using System;
using System.Diagnostics;
using KSP.Localization;
using Debug = UnityEngine.Debug;

namespace ShoAerospace.Koffee
{
    internal static class ScienceExperimentKoffeeService
    {
        internal static bool CanRun(string id, Part consumer)
        {
            if (!ScienceExperimentKoffeeRegistry.Contains(id)) return true;
            ProtoCrewMember? scientist = KerbalWorkAssignment.ExperimentScientist(consumer);
            if (scientist == null)
            {
                ShowNoScientist();
                Trace(id, "blocked: no scientist");
                return false;
            }
            if (KerbalWorkState.IsKaffeinated(scientist))
            {
                Trace(id, "ready: " + scientist.name + " already Kaffeinated");
                return true;
            }
            Part? source = EvaKoffeeSources.Consumer(consumer);
            int cost = KoffeeSettingsLoader.Current.ActivationCost;
            if (source != null && KoffeeSources.Available(source) >= cost)
            {
                Trace(id, "ready: activation cost available for " + scientist.name);
                return true;
            }
            ShowNeedsKoffee(scientist);
            Trace(id, "blocked: " + scientist.name + " needs Koffee");
            return false;
        }

        // Called immediately before stock creates data; commit/refund before the next yield.
        internal static KaffeinationAttempt? TryBegin(string id, Part consumer)
        {
            try
            {
                ProtoCrewMember? scientist = KerbalWorkAssignment.ExperimentScientist(consumer);
                if (scientist == null)
                {
                    ShowNoScientist();
                    return null;
                }
                Part? source = EvaKoffeeSources.Consumer(consumer);
                KaffeinationAttempt? attempt = KerbalWorkState.TryBeginKaffeination(scientist, source, true);
                if (attempt == null) ShowNeedsKoffee(scientist);
                return attempt;
            }
            catch (Exception error)
            {
                Debug.LogError("[Koffee] Kaffeination failed and was rolled back: " + error);
                return null;
            }
        }

        private static void ShowNeedsKoffee(ProtoCrewMember scientist) =>
            ScreenMessages.PostScreenMessage(Localizer.Format("#Koffee_Experiment_NeedsKoffee", scientist.name),
                5f, ScreenMessageStyle.UPPER_CENTER);

        private static void ShowNoScientist() =>
            ScreenMessages.PostScreenMessage(Localizer.Format("#Koffee_Experiment_NoScientist"),
                5f, ScreenMessageStyle.UPPER_CENTER);

        [Conditional("DEBUG")]
        private static void Trace(string id, string outcome) =>
            Debug.Log("[Koffee] Science experiment " + id + ": " + outcome);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP.Localization;
using PreFlightTests;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class ExperimentSelectionLaunchChecks
    {
        // KSP 1.12.5 launchChecks sets these before constructing/running its preflight.
        private static readonly FieldInfo FacilityFile = AccessTools.Field(typeof(LaunchSiteFacility), "fileName");
        private static readonly FieldInfo FacilityManifest = AccessTools.Field(typeof(LaunchSiteFacility), "crewManifest");

        internal static void Install(Harmony harmony)
        {
            if (FacilityFile == null || FacilityManifest == null)
                throw new MissingFieldException("KSP launch-site preflight context is unavailable.");
            harmony.Patch(AccessTools.Method(typeof(PreFlightCheck), nameof(PreFlightCheck.RunTests)),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(ExperimentSelectionLaunchChecks), nameof(BeforeChecks))));
        }

        private static void BeforeChecks(PreFlightCheck __instance, List<IPreFlightTest> ___tests, Callback ___OnComplete)
        {
            if (___tests.Any(t => t is SelectionCheck)) return;
            Callback complete = ___OnComplete;
            if (complete?.Target is EditorLogic && complete.Method.Name == "proceedWithVesselLaunch")
                // launchVessel has already saved this exact node before running preflight.
                __instance.AddTest(new SelectionCheck(() => ShipConstruction.ShipConfig, () => ShipConstruction.ShipManifest));
            else if (complete?.Target is LaunchSiteFacility facility)
                __instance.AddTest(new SelectionCheck(
                    () => ConfigNode.Load((string)FacilityFile.GetValue(facility)),
                    () => (VesselCrewManifest)FacilityManifest.GetValue(facility)));
        }

        private sealed class SelectionCheck : IPreFlightTest
        {
            private readonly Func<ConfigNode> craft;
            private readonly Func<VesselCrewManifest> manifest;
            private string[] errors = Array.Empty<string>();

            internal SelectionCheck(Func<ConfigNode> craft, Func<VesselCrewManifest> manifest)
            { this.craft = craft; this.manifest = manifest; }

            public bool Test()
            {
                try
                {
                    StationExperimentCatalogLoader.ResolvePendingReferences();
                    ConfigNode node = craft();
                    if (node == null) throw new InvalidOperationException("Launch craft is unavailable.");
                    var crew = manifest()?.GetAllCrew(false) ?? new List<ProtoCrewMember>();
                    errors = ExperimentSelectionRules.LaunchErrors(node, crew.Select(c => c.InventoryNode));
                }
                catch (Exception error)
                {
                    Debug.LogError("[Koffee] Experiment launch validation failed: " + error);
                    errors = new[] { Localizer.Format("#Koffee_Selector_LaunchValidationFailed") };
                }
                return errors.Length == 0;
            }

            public string GetWarningTitle() => Localizer.Format("#Koffee_Selector_LaunchTitle");
            public string GetWarningDescription() => string.Join("\n", errors);
            public string GetProceedOption() => null!;
            public string GetAbortOption() => Localizer.Format("#autoLOC_253243");
        }
    }
}

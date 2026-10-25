using System;
using HarmonyLib;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KoffeeBootstrap : MonoBehaviour
    {
        private const string HarmonyRoot = "ShoAerospace.Koffee";
        private static KoffeeBootstrap? instance;
        private Harmony? scienceHarmony;
        private Harmony? solarHarmony;
        private Harmony? labHarmony;
        private Harmony? engineeringHarmony;
        private Harmony? tooltipHarmony;
        private Harmony? recoveryHarmony;
        private Harmony? selectionHarmony;
        private Harmony? dbsHarmony;
        private Harmony? parameterHarmony;
        private Harmony? brpLogHarmony;
        private bool coreInitialized;
        private bool scienceInitialized;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("[Koffee] Mod cargado correctamente.");
            parameterHarmony = Install("GameParameters", KoffeeParameterDialog.Install);
            LoadConfigurations();
            InitializeCore();
            brpLogHarmony = Install("BrpLogging", StationBrpLogGuard.Install);
            StationBackgroundElectricity.Initialize();
            dbsHarmony = Install("DynamicBatteryStorage", StationDbsIntegration.Initialize);
            InitializeScience();
            solarHarmony = Install("SolarAcquisition", StationSolarPublication.Install);
            InitializeLab();
            InitializeEngineering();
            tooltipHarmony = Install("ScientistTooltip", ScientistTooltipPatches.Install);
            recoveryHarmony = Install("StationRecovery", StationRecoveryPatches.Install);
            selectionHarmony = Install("ExperimentSelection", ExperimentSelectionLaunchChecks.Install);
        }

        private void Update() => StationBrpLogGuard.Flush();

        private static void LoadConfigurations()
        {
            Load("core settings", KoffeeSettingsLoader.Load);
            Load("science experiment registry", ScienceExperimentRegistryLoader.Load);
            Load("science settings", ScienceSettingsLoader.Load);
            Load("engineering settings", EngineeringSettingsLoader.Load);
            Load("engineering targets", EngineeringTargetLoader.Load);
            Load("station experiment catalog", StationExperimentCatalogLoader.Load);
        }

        private static void Load(string name, Action load)
        {
            try { load(); }
            catch (Exception error)
            {
                Debug.LogError("[Koffee] Failed to load " + name + "; safe defaults remain active: " + error);
            }
        }

        private void InitializeCore()
        {
            try
            {
                GameEvents.onGameSceneLoadRequested.Add(OnSceneLoadRequested);
                GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelReady);
                GameEvents.OnTechnologyResearched.Add(OnTechnologyResearched);
                coreInitialized = true;
                Debug.Log("[Koffee] Core services initialized.");
            }
            catch (Exception error)
            {
                GameEvents.onGameSceneLoadRequested.Remove(OnSceneLoadRequested);
                GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelReady);
                GameEvents.OnTechnologyResearched.Remove(OnTechnologyResearched);
                Debug.LogError("[Koffee] Failed to initialize core services: " + error);
            }
        }

        private void InitializeScience()
        {
            scienceHarmony = Install("Science", harmony =>
            {
                ScienceExperimentPatches.Install(harmony);
                SpaceObjectSciencePatches.Install(harmony);
                EvaKoffeeSources.Install(harmony);
            });
            if (scienceHarmony == null) return;
            try
            {
                GameEvents.onCrewOnEva.Add(OnCrewOnEva);
                scienceInitialized = true;
                Debug.Log("[Koffee] Science services initialized.");
            }
            catch (Exception error)
            {
                Debug.LogError("[Koffee] Failed to subscribe science events: " + error);
                scienceHarmony.UnpatchAll(HarmonyRoot + ".Science");
                scienceHarmony = null;
            }
        }

        private void InitializeLab() => labHarmony = Install("Lab", LabPatches.Install);

        private void InitializeEngineering() => engineeringHarmony =
            Install("Engineering", EngineeringPatches.Install);

        private static Harmony? Install(string subsystem, Action<Harmony> install)
        {
            string id = HarmonyRoot + "." + subsystem;
            var harmony = new Harmony(id);
            harmony.UnpatchAll(id);
            try
            {
                install(harmony);
                Debug.Log("[Koffee] " + subsystem + " adapters initialized.");
                return harmony;
            }
            catch (Exception error)
            {
                harmony.UnpatchAll(id);
                Debug.LogError("[Koffee] Failed to initialize " + subsystem +
                    " adapters; other subsystems remain active: " + error);
                return null;
            }
        }

        // KSP EventData inspects the delegate target; static handlers have no target.
        private void OnCrewOnEva(GameEvents.FromToAction<Part, Part> change) => EvaKoffeeSources.OnEva(change);

        private void OnSceneLoadRequested(GameScenes scene)
        {
            ScienceExperimentPatches.Clear();
            StationSolarPublication.Clear();
            EngineeringKoffeeService.Clear();
        }

        private void OnTechnologyResearched(GameEvents.HostTargetAction<RDTech, RDTech.OperationResult> change) =>
            ExperimentConfigurationResearch.OnResearched(change.host, change.target);

        private void OnLevelReady(GameScenes scene) =>
            StationExperimentCatalogLoader.ResolvePendingReferences();

        private void OnDestroy()
        {
            if (instance != this) return;
            if (coreInitialized)
            {
                GameEvents.onGameSceneLoadRequested.Remove(OnSceneLoadRequested);
                GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelReady);
                GameEvents.OnTechnologyResearched.Remove(OnTechnologyResearched);
            }
            if (scienceInitialized) GameEvents.onCrewOnEva.Remove(OnCrewOnEva);
            scienceHarmony?.UnpatchAll(HarmonyRoot + ".Science");
            solarHarmony?.UnpatchAll(HarmonyRoot + ".SolarAcquisition");
            labHarmony?.UnpatchAll(HarmonyRoot + ".Lab");
            engineeringHarmony?.UnpatchAll(HarmonyRoot + ".Engineering");
            tooltipHarmony?.UnpatchAll(HarmonyRoot + ".ScientistTooltip");
            recoveryHarmony?.UnpatchAll(HarmonyRoot + ".StationRecovery");
            selectionHarmony?.UnpatchAll(HarmonyRoot + ".ExperimentSelection");
            dbsHarmony?.UnpatchAll(HarmonyRoot + ".DynamicBatteryStorage");
            KoffeeParameterDialog.Shutdown();
            parameterHarmony?.UnpatchAll(HarmonyRoot + ".GameParameters");
            StationDbsIntegration.Shutdown();
            brpLogHarmony?.UnpatchAll(HarmonyRoot + ".BrpLogging");
            ScienceExperimentPatches.Clear();
            StationSolarPublication.Clear();
            EngineeringKoffeeService.Clear();
            coreInitialized = false;
            scienceInitialized = false;
            instance = null;
        }
    }
}

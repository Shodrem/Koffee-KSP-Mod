using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Presentation and editor commands only. Flight execution stays in ModuleKoffeeExperiment.
    public sealed class ModuleKoffeeExperimentSelector : PartModule, IPartCostModifier, IPartMassModifier
    {
        private const string LockId = "KoffeeExperimentSelector";
        private const string TextureRoot = "ShoAerospace/Koffee/Parts/Experiment_Container_62/";
        private const string DeviceTextureRoot = "ShoAerospace/Koffee/Parts/Experiments_Device_ED-01/";
        private static ModuleKoffeeExperimentSelector? opened;
        private Rect window = new Rect(120, 80, 760, 520);
        private Vector2 listScroll;
        private Vector2 detailScroll;
        private ScienceCategory category;
        private string previewId = "";
        private bool initialized;
        private string presentedId = "";
        private AvailablePart? ownInfo;
        private GUIStyle? wrapped;
        private GUIStyle? entryButton;
        private GUISkin? styledSkin;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "#Koffee_Selector_Selection")]
        public string selectionDisplay = "";

        private ModuleKoffeeExperiment? Experiment => part?.FindModuleImplementing<ModuleKoffeeExperiment>();

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            initialized = true;
            RefreshPresentation();
        }

        public void Update()
        {
            if (!initialized) return;
            if (presentedId != Experiment?.experimentID) RefreshPresentation();
            if (opened == this && (!HighLogic.LoadedSceneIsEditor || part == null)) Close();
            if (Events != null) Events[nameof(ConfigureExperiment)].active = HighLogic.LoadedSceneIsEditor;
        }

        [KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "#Koffee_Selector_Configure")]
        public void ConfigureExperiment()
        {
            if (!HighLogic.LoadedSceneIsEditor || Experiment == null) return;
            if (opened != null) opened.Close();
            opened = this;
            previewId = Experiment.experimentID;
            if (StationExperimentCatalogLoader.Current.TryGet(previewId, out var selected)) category = selected.ScienceCategory;
            else category = ScienceCategories.All.FirstOrDefault(c => Entries(c).Length != 0);
        }

        private AvailablePart? Source(string id) => ExperimentSelectionRules.Source(id, Experiment?.editorExperimentFamily ?? "");
        private static bool Unlocked(AvailablePart source) => ExperimentSelectionRules.Unlocked(source);

        private StationExperimentDefinition[] Entries(ScienceCategory selectedCategory) =>
            StationExperimentCatalogLoader.Current.Definitions.Where(d => d.ScienceCategory == selectedCategory && Source(d.Id) != null)
                .OrderBy(d => Source(d.Id)!.title, StringComparer.CurrentCultureIgnoreCase).ToArray();

        private void Apply(string id)
        {
            if (!HighLogic.LoadedSceneIsEditor || Experiment == null ||
                !StationExperimentCatalogLoader.Current.TryGet(id, out _) || !(Source(id) is AvailablePart source) || !Unlocked(source)) return;
            ApplyToPart(this, source, id);
            // Symmetry copies have independent modules and independent presentation metadata.
            foreach (Part counterpart in part.symmetryCounterparts)
            {
                var selector = counterpart.FindModuleImplementing<ModuleKoffeeExperimentSelector>();
                if (selector != null && selector.Experiment?.editorExperimentFamily == Experiment.editorExperimentFamily)
                    ApplyToPart(selector, source, id);
            }
            GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            EditorLogic.fetch.SetBackup();
            Close();
        }

        private static void ApplyToPart(ModuleKoffeeExperimentSelector target, AvailablePart source, string id)
        {
            var module = target.Experiment!;
            module.experimentID = id;
            module.surfaceEnabled = SurfaceCapability(source);
            target.RefreshPresentation();
        }

        private static bool SurfaceCapability(AvailablePart source) => ExperimentSelectionRules.SurfaceCapability(source);

        private void RefreshPresentation()
        {
            var module = Experiment;
            if (module == null) return;
            presentedId = module.experimentID;
            var source = Source(presentedId);
            selectionDisplay = source?.title ?? Localizer.Format("#Koffee_Selector_Unconfigured");
            if (part.partInfo != null)
            {
                if (ownInfo == null)
                {
                    // AvailablePart is shared by every instance; clone before changing visible text.
                    ownInfo = (AvailablePart)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(part.partInfo, null);
                    part.partInfo = ownInfo;
                }
                ownInfo.title = source?.title ?? Localizer.Format(module.editorExperimentFamily == "Container" ?
                    "#Koffee_Selector_ContainerTitle" : "#LOC_Koffee_ExperimentDeviceED01_Title");
                if (source != null) ownInfo.description = source.description;
            }
            if (source != null && StationExperimentCatalogLoader.Current.TryGet(presentedId, out var definition))
                ApplyTextures(definition.ScienceCategory, module.editorExperimentFamily);
            foreach (var paw in UnityEngine.Object.FindObjectsOfType<UIPartActionWindow>())
                if (paw.part == part)
                {
                    if (paw.titleText != null) paw.titleText.text = ownInfo?.title ?? selectionDisplay;
                    paw.displayDirty = true;
                }
        }

        private void ApplyTextures(ScienceCategory value, string family)
        {
            string label = value == ScienceCategory.MaterialsChemistry ? "Materials" :
                value == ScienceCategory.Physics ? "SpacePhysics" : value.ToString();
            Texture2D texture = GameDatabase.Instance.GetTexture((family == "Device" ? DeviceTextureRoot : TextureRoot) + "Label_" + label, false);
            string color = value == ScienceCategory.Biology ? "Light_green" : value == ScienceCategory.Geology ? "Orange" :
                value == ScienceCategory.MaterialsChemistry ? "Yellow" : value == ScienceCategory.Technology ? "Light_red" :
                value == ScienceCategory.Environment ? "Light_blue" : value == ScienceCategory.Astronomy ? "Dark_blue" : "Purple";
            Texture2D body = GameDatabase.Instance.GetTexture(TextureRoot + "Experiment_Container_" + color + "_Base_color", false);
            foreach (Renderer renderer in part.FindModelComponents<Renderer>())
                foreach (Material material in renderer.materials)
                {
                    if (material == null) continue;
                    string current = material.mainTexture?.name ?? "";
                    int separator = Math.Max(current.LastIndexOf('/'), current.LastIndexOf('\\'));
                    if (separator >= 0) current = current.Substring(separator + 1);
                    if (texture != null && (family == "Device" ? material.name.StartsWith("Category_Mat", StringComparison.Ordinal) :
                        current.StartsWith("Label_", StringComparison.Ordinal))) material.mainTexture = texture;
                    else if (family == "Container" && body != null && current.StartsWith("Experiment_Container_", StringComparison.Ordinal))
                        material.mainTexture = body;
                }
        }

        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit)
        {
            var source = Source(Experiment?.experimentID ?? "");
            return source == null ? 0 : source.cost - defaultCost;
        }
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.CONSTANTLY;
        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) =>
            Source(Experiment?.experimentID ?? "")?.partPrefab.mass - defaultMass ?? 0;
        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.CONSTANTLY;

        public void OnGUI()
        {
            if (opened != this || !HighLogic.LoadedSceneIsEditor) return;
            GUISkin previous = GUI.skin;
            Matrix4x4 matrix = GUI.matrix;
            float scale = KoffeeStatusWindow.Scale;
            try
            {
                GUI.skin = HighLogic.Skin;
                if (styledSkin != GUI.skin)
                {
                    styledSkin = GUI.skin;
                    wrapped = new GUIStyle(GUI.skin.label) { wordWrap = true };
                    entryButton = new GUIStyle(GUI.skin.button) { wordWrap = true, alignment = TextAnchor.MiddleLeft };
                }
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
                window.width = Math.Min(900, Screen.width / scale - 24);
                window.height = Math.Min(600, Screen.height / scale - 24);
                window.x = Mathf.Clamp(window.x, 0, Math.Max(0, Screen.width / scale - window.width));
                window.y = Mathf.Clamp(window.y, 0, Math.Max(0, Screen.height / scale - window.height));
                window = GUI.Window(GetInstanceID(), window, DrawWindow, Localizer.Format("#Koffee_Selector_Configure"));
                if (opened != this) { InputLockManager.RemoveControlLock(LockId); return; }
                var mouse = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);
                if (window.Contains(mouse)) InputLockManager.SetControlLock(ControlTypes.EDITOR_LOCK, LockId);
                else InputLockManager.RemoveControlLock(LockId);
            }
            finally { GUI.skin = previous; GUI.matrix = matrix; }
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginArea(new Rect(12, 28, window.width - 24, window.height - 40));
            GUILayout.Label(Localizer.Format("#Koffee_Selector_LockedAtLaunch"), wrapped!);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(Math.Min(155, window.width * 0.20f)));
            foreach (var candidate in ScienceCategories.All)
                if (Entries(candidate).Length > 0 && GUILayout.Toggle(category == candidate, KoffeeResearchDisplay.Category(candidate), entryButton!))
                { if (category != candidate) { category = candidate; previewId = ""; listScroll = Vector2.zero; } }
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUILayout.Width(Math.Min(240, window.width * 0.30f)));
            listScroll = GUILayout.BeginScrollView(listScroll);
            var entries = Entries(category);
            if (entries.Length == 0) GUILayout.Label(Localizer.Format("#Koffee_Selector_Empty"), wrapped!);
            foreach (var entry in entries)
            {
                var source = Source(entry.Id)!;
                string title = source.title + (Unlocked(source) ? "" : " (" + Localizer.Format("#Koffee_Selector_Locked") + ")");
                if (GUILayout.Toggle(previewId == entry.Id, title, entryButton!)) previewId = entry.Id;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            detailScroll = GUILayout.BeginScrollView(detailScroll);
            if (StationExperimentCatalogLoader.Current.TryGet(previewId, out var definition) && Source(previewId) is AvailablePart selected)
            {
                GUILayout.Label(selected.title, wrapped!);
                GUILayout.Label(selected.description, wrapped!);
                Line("#Koffee_Research_CategoryLabel", KoffeeResearchDisplay.Category(definition.ScienceCategory));
                Line("#Koffee_Selector_Technology", ExperimentSelectionRules.TechnologyTitle(selected));
                Line("#Koffee_Selector_Cost", selected.cost.ToString("0", CultureInfo.CurrentCulture));
                Line("#Koffee_Selector_Mass", selected.partPrefab.mass.ToString("0.###", CultureInfo.CurrentCulture) + " t");
                Line("#Koffee_Selector_Points", KoffeeResearchDisplay.EditorRequiredWork(definition.ResearchPointsRequired));
                GUILayout.Label(Localizer.Format("#Koffee_Selector_Time") + " " + Localizer.Format("#Koffee_Selector_TimeReference"), wrapped!);
                Line("#Koffee_Selector_Personnel", definition.Personnel.WorkerCount + " × " + Localizer.Format("#Koffee_Selector_Scientist") +
                    " (" + Localizer.Format("#Koffee_Selector_MinLevel") + " " + definition.Personnel.MinimumLevel + ")");
                Line("#Koffee_Selector_Lab", KoffeeResearchDisplay.Laboratory(definition.LabCapability));
                Line("#Koffee_Equipment_Required", StationExperimentEquipment.Description(definition));
                Line("#Koffee_Environment_Bodies", StationExperimentEnvironment.Bodies(definition));
                Line("#Koffee_Environment_Atmosphere", StationExperimentEnvironment.Atmosphere(definition.Id));
                var science = ResearchAndDevelopment.GetExperiment(definition.Id);
                bool surface = SurfaceCapability(selected);
                if (science != null && (science.situationMask & ((uint)ExperimentSituations.SrfLanded | (uint)ExperimentSituations.SrfSplashed)) != 0)
                {
                    string[] required = ExperimentSelectionRules.SurfaceTechnologyTitles(selected);
                    Line("#Koffee_Selector_SurfaceCapability", Localizer.Format(surface ? "#Koffee_Selector_SurfaceAvailable" :
                        "#Koffee_Selector_SurfaceLocked") + (required.Length == 0 ? "" : " (" + string.Join(", ", required) + ")"));
                }
                string[] situations = StationExperimentRuntime.ValidSituations(definition.Id)
                    .Where(s => surface || (s != "SrfLanded" && s != "SrfSplashed"))
                    .Select(s => Localizer.Format(s == "SrfSplashed" ? "#Koffee_Selector_Splashed" : "#Koffee_Environment_Situation_" + s)).ToArray();
                GUILayout.Label(Localizer.Format("#Koffee_Station_ValidSituations", string.Join(", ", situations)), wrapped!);
                if (science != null) Line("#Koffee_Selector_BaseScience", science.baseValue.ToString("0.##", CultureInfo.CurrentCulture));
                Line("#Koffee_Selector_Transmission", (definition.XmitDataScalar * 100).ToString("0.#", CultureInfo.CurrentCulture) + " %");
                Line("#Koffee_Selector_Recovery", definition.RecoveryScienceBonusPercent.ToString("0.#", CultureInfo.CurrentCulture) + " %");
                if (!Unlocked(selected)) GUILayout.Label(Localizer.Format("#Koffee_Selector_UnlockRequired"), wrapped!);
            }
            GUILayout.EndScrollView();
            bool previous = GUI.enabled;
            GUI.enabled = Source(previewId) is AvailablePart available && Unlocked(available);
            if (GUILayout.Button(Localizer.Format("#Koffee_Selector_Apply"))) Apply(previewId);
            GUI.enabled = previous;
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            if (GUILayout.Button(Localizer.Format("#Koffee_Selector_Close"))) Close();
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, window.width, 26));
        }

        private void Line(string key, string value) => GUILayout.Label(Localizer.Format(key) + ": " + value, wrapped!);
        private void Close()
        {
            if (opened != this) return;
            opened = null;
            InputLockManager.RemoveControlLock(LockId);
        }
        public void OnDisable() => Close();
        public void OnDestroy() => Close();
    }
}

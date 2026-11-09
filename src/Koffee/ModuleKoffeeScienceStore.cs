using System;
using System.Collections.Generic;
using System.Linq;
using KSP.Localization;

namespace ShoAerospace.Koffee
{
    // Separate from the advanced Engineer/continuity module. Lite has only this
    // module and its physical, unlimited ModuleScienceContainer.
    public sealed class ModuleKoffeeScienceStore : PartModule
    {
        [KSPField] public bool lite;
        [KSPField(isPersistant = true)] public bool managerEnabled = true;
        [KSPField(isPersistant = true)] public bool energySupportRestore;
        internal bool Operating => managerEnabled && !DeadlyScenario.Active(vessel) &&
            part.FindModuleImplementing<ModuleKoffeeExperimentManager>()?.managerEnabled != false;
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (bool.TryParse(node.GetValue(nameof(managerEnabled)), out bool enabled)) managerEnabled = enabled;
            if (bool.TryParse(node.GetValue(nameof(energySupportRestore)), out bool restore)) energySupportRestore = restore;
        }
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            node.SetValue(nameof(managerEnabled), managerEnabled.ToString(), true);
            node.SetValue(nameof(energySupportRestore), energySupportRestore.ToString(), true);
        }
        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "#Koffee_Health_ManagerToggle")]
        public void ToggleManager()
        {
            if (!lite || !managerEnabled && DeadlyScenario.Active(vessel)) return;
            managerEnabled = !managerEnabled;
        }
        [KSPField(isPersistant = true)] public string storeId = "";
        [KSPField(isPersistant = true)] public int installationOrder;
        [KSPField(isPersistant = true)] public int familyNumber;
        [KSPField(isPersistant = true)] public bool customName;
        [KSPField(isPersistant = true)] public bool preferred;
        [KSPField(isPersistant = true)] public double preferenceStamp;
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#Koffee_Reports_Name")]
        public string displayName = "";
        private bool wasInstalled;
        private static readonly List<WeakReference<ModuleKoffeeScienceStore>> instances = new List<WeakReference<ModuleKoffeeScienceStore>>();
        internal ModuleScienceContainer? Container => part?.FindModuleImplementing<ModuleScienceContainer>();
        internal string Context => displayName + " · " + (vessel?.vesselName ?? EditorLogic.fetch?.ship?.shipName ?? "") +
            " [" + storeId.Substring(0, Math.Min(6, storeId.Length)) + "]";
        internal static IEnumerable<ModuleKoffeeScienceStore> Stores(Vessel vessel) => vessel == null
            ? Enumerable.Empty<ModuleKoffeeScienceStore>() : vessel.parts.Where(p => p != null && p.State != PartStates.DEAD)
                .SelectMany(p => p.Modules.OfType<ModuleKoffeeScienceStore>()).OrderBy(m => m.installationOrder).ThenBy(m => m.storeId, StringComparer.Ordinal);
        private static IEnumerable<ModuleKoffeeScienceStore> EditorStores() => EditorLogic.fetch?.ship?.parts?
            .SelectMany(p => p.Modules.OfType<ModuleKoffeeScienceStore>()) ?? Enumerable.Empty<ModuleKoffeeScienceStore>();
        internal static ModuleKoffeeScienceStore? Preferred(Vessel vessel) => Stores(vessel)
            .OrderByDescending(m => m.preferred).ThenByDescending(m => m.preferred ? m.preferenceStamp : 0)
            .ThenBy(m => m.installationOrder).ThenBy(m => m.storeId, StringComparer.Ordinal).FirstOrDefault();

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (Events[nameof(ToggleManager)] != null) Events[nameof(ToggleManager)].active = lite;
            instances.RemoveAll(w => !w.TryGetTarget(out var m) || m == null);
            bool copy = instances.Any(w => w.TryGetTarget(out var m) && m != null && !ReferenceEquals(m, this) && m.storeId == storeId && storeId != "");
            if (copy) { storeId = ""; installationOrder = familyNumber = 0; preferred = false; preferenceStamp = 0; if (!customName) displayName = ""; }
            instances.Add(new WeakReference<ModuleKoffeeScienceStore>(this));
            Initialize(HighLogic.LoadedSceneIsEditor ? EditorStores() : Stores(vessel));
            wasInstalled = HighLogic.LoadedSceneIsEditor && EditorStores().Contains(this);
            if (Container != null) Container.capacity = 0;
            if (HighLogic.LoadedSceneIsEditor) GameEvents.onEditorShipModified.Add(EditorChanged);
        }
        private void Initialize(IEnumerable<ModuleKoffeeScienceStore> collection)
        {
            var others = collection.Where(m => !ReferenceEquals(m, this)).ToArray();
            if (string.IsNullOrEmpty(storeId)) storeId = Guid.NewGuid().ToString("N");
            if (installationOrder <= 0) installationOrder = others.Select(m => m.installationOrder).DefaultIfEmpty(0).Max() + 1;
            if (familyNumber <= 0) familyNumber = others.Where(m => m.lite == lite).Select(m => m.familyNumber).DefaultIfEmpty(0).Max() + 1;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = (lite ? "Manager Lite " : "Manager ") + familyNumber;
        }
        private void EditorChanged(ShipConstruct ship)
        {
            var stores = EditorStores().ToArray();
            bool installed = stores.Contains(this);
            if (installed && !wasInstalled) installationOrder = 0;
            if (installed) Initialize(stores);
            wasInstalled = installed;
        }
        public void OnDestroy() { GameEvents.onEditorShipModified.Remove(EditorChanged); }
        internal void Rename(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0) return;
            displayName = value.Substring(0, Math.Min(80, value.Length)); customName = true;
        }
        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "#Koffee_Reports_ReceiveHere")]
        public void ReceiveHere()
        {
            var peers = HighLogic.LoadedSceneIsEditor ? EditorStores() : Stores(vessel);
            foreach (var peer in peers) peer.preferred = ReferenceEquals(peer, this);
            preferred = true; preferenceStamp = DateTime.UtcNow.Ticks;
        }
        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "#Koffee_Reports_Rename")]
        public void RenameStore() => ScienceReportUI.Open(this, true);
        [KSPEvent(guiActive = true, guiName = "#Koffee_Reports_Review")]
        public void ReviewReports() => ScienceReportUI.Open(this);
        [KSPEvent(guiActive = true, guiName = "#Koffee_Reports_TransferAll")]
        public void TransferAll() => ScienceReportUI.SelectReceiver(this);
    }
}

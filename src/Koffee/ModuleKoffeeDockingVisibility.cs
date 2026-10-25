using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Stock docking keeps searching even when its model transform is inactive.
    // Empty compatibility sets prevent acquisition in both directions while
    // retaining the stock FSM and docking persistence for each independent port.
    public sealed class ModuleKoffeeDockingVisibility : PartModule
    {
        [KSPField] public string visibilityTransform = "ExperimentConnectors";
        [KSPField] public string nodeType = "koffeeExperiment62";
        [KSPField] public string controlledDockingTransform = "";
        [KSPField] public bool controlDockingPorts = true;
        [KSPField] public string pawGroupName = "";
        [KSPField] public string pawGroupDisplayName = "";
        [KSPField] public string controlledAttachNode = "";
        [KSPField] public string disableAttachNodeTransform = "";

        private Transform? visibility;
        private Transform? attachNodeBlocker;
        private readonly List<PortVisibility> ports = new List<PortVisibility>();
        private bool started;
        private AttachNode? attachment;
        private AttachNode.NodeType originalNodeType;
        private float originalNodeRadius;
        private bool nodeHidden;
        private GameObject? originalNodeIcon;
        private bool originalIconActive;

        public override void OnStartFinished(StartState state)
        {
            base.OnStartFinished(state);
            if (started) GameEvents.onVariantApplied.Remove(OnVariantApplied);
            visibility = part.FindModelTransform(visibilityTransform);
            attachNodeBlocker = string.IsNullOrEmpty(disableAttachNodeTransform)
                ? null : part.FindModelTransform(disableAttachNodeTransform);
            attachment = string.IsNullOrEmpty(controlledAttachNode)
                ? null : part.FindAttachNode(controlledAttachNode);
            ports.Clear();
            foreach (PartModule module in part.Modules)
            {
                if (controlDockingPorts && module is ModuleDockingNode dock &&
                    string.Equals(dock.nodeType, nodeType, StringComparison.Ordinal) &&
                    (string.IsNullOrEmpty(controlledDockingTransform) ||
                     string.Equals(dock.nodeTransformName, controlledDockingTransform, StringComparison.Ordinal)))
                    ports.Add(new PortVisibility(dock, pawGroupName, pawGroupDisplayName));
            }
            started = true;
            GameEvents.onVariantApplied.Add(OnVariantApplied);
            ApplyVisibility();
            if (visibility == null)
                Debug.LogError("[Koffee] Missing docking visibility transform: " + visibilityTransform);
            if (!string.IsNullOrEmpty(disableAttachNodeTransform) && attachNodeBlocker == null)
                Debug.LogError("[Koffee] Missing attach-node disabling transform: " + disableAttachNodeTransform);
        }

        public void LateUpdate()
        {
            if (started && (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight))
                ApplyVisibility();
        }

        private void OnVariantApplied(Part changedPart, PartVariant variant)
        {
            if (started && changedPart == part) ApplyVisibility();
        }

        public void OnDestroy()
        {
            if (started) GameEvents.onVariantApplied.Remove(OnVariantApplied);
        }

        private void ApplyVisibility()
        {
            bool visible = visibility != null && visibility.gameObject.activeInHierarchy;
            foreach (PortVisibility port in ports) port.Apply(visible);
            // A plain stack variant can keep its node while hiding the docking port.
            bool nodeVisible = string.IsNullOrEmpty(disableAttachNodeTransform)
                ? visible : attachNodeBlocker != null && !attachNodeBlocker.gameObject.activeInHierarchy;
            ApplyAttachNode(nodeVisible);
        }

        private void ApplyAttachNode(bool visible)
        {
            if (attachment == null) return;
            if (visible)
            {
                if (!nodeHidden) return;
                attachment.nodeType = originalNodeType;
                attachment.radius = originalNodeRadius;
                if (attachment.icon != null && attachment.icon == originalNodeIcon)
                    attachment.icon.SetActive(originalIconActive);
                nodeHidden = false;
                return;
            }

            if (!nodeHidden)
            {
                originalNodeType = attachment.nodeType;
                originalNodeRadius = attachment.radius;
                originalNodeIcon = attachment.icon;
                originalIconActive = attachment.icon != null && attachment.icon.activeSelf;
                nodeHidden = true;
            }
            // Same disabling convention as stock ModuleDynamicNodes. Retain the
            // node object, referenceNode and attachedPart for docking/save state.
            attachment.nodeType = AttachNode.NodeType.Dock;
            attachment.radius = 0.0001f;
            if (attachment.icon != null)
            {
                // The editor can replace its node icon after module startup.
                if (attachment.icon != originalNodeIcon)
                {
                    originalNodeIcon = attachment.icon;
                    originalIconActive = attachment.icon.activeSelf;
                }
                attachment.icon.SetActive(false);
            }
        }

        private sealed class PortVisibility
        {
            private readonly ModuleDockingNode dock;
            private readonly List<Action<bool>> uiVisibility = new List<Action<bool>>();
            private HashSet<string>? originalTypes;
            private bool hidden;

            internal PortVisibility(ModuleDockingNode dock, string groupName, string displayName)
            {
                this.dock = dock;
                if (string.IsNullOrEmpty(groupName)) return;
                var group = new BasePAWGroup(groupName,
                    KSP.Localization.Localizer.Format(displayName), false);
                foreach (BaseEvent evt in dock.Events) evt.group = group;
                foreach (BaseField field in dock.Fields) field.group = group;
            }

            internal void Apply(bool visible)
            {
                if (dock.nodeTypes == null) return;
                if (visible)
                {
                    if (!hidden) return;
                    dock.nodeTypes.Clear();
                    if (originalTypes != null) dock.nodeTypes.UnionWith(originalTypes);
                    foreach (Action<bool> apply in uiVisibility) apply(true);
                    uiVisibility.Clear();
                    hidden = false;
                    return;
                }

                if (!hidden)
                {
                    originalTypes = new HashSet<string>(dock.nodeTypes);
                    foreach (BaseEvent evt in dock.Events)
                    {
                        bool flight = evt.guiActive;
                        bool editor = evt.guiActiveEditor;
                        bool eva = evt.guiActiveUnfocused;
                        uiVisibility.Add(show =>
                        {
                            evt.guiActive = show && flight;
                            evt.guiActiveEditor = show && editor;
                            evt.guiActiveUnfocused = show && eva;
                        });
                    }
                    foreach (BaseField field in dock.Fields)
                    {
                        bool flight = field.guiActive;
                        bool editor = field.guiActiveEditor;
                        uiVisibility.Add(show =>
                        {
                            field.guiActive = show && flight;
                            field.guiActiveEditor = show && editor;
                        });
                    }
                    foreach (BaseAction action in dock.Actions)
                    {
                        bool active = action.active;
                        uiVisibility.Add(show => action.active = show && active);
                    }
                    hidden = true;
                }

                dock.nodeTypes.Clear();
                foreach (Action<bool> apply in uiVisibility) apply(false);
            }
        }
    }
}

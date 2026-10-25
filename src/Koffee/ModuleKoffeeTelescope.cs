using System;
using System.Collections;
using System.Linq;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    public enum TelescopeViewState { NoTarget, UnsupportedTarget, MissingOptics, Ready, OutsideField, Occluded, Unavailable }

    public sealed class ModuleKoffeeTelescope : PartModule
    {
        [KSPField] public string cameraTransformName = "TelescopeCameraTransform";
        // Test-installation CFG opt-in; resource loading only, not complete visual compatibility.
        [KSPField] public bool parallaxResourceExperiment = false;
        [KSPField] public double captureMaximumErrorDegrees = 0.5;
        [KSPField] public double captureFieldFraction = 0.2;
        [KSPField] public double captureMinimumDiameterPixels = 8;
        [KSPField] public double pairMaximumSeparationRadii = 32;
        [KSPField] public double pairMinimumPlanetDiameterPixels = 24;
        [KSPField] public double pairMinimumMoonDiameterPixels = 4;
        private ModuleKoffeeExperiment? observationExperiment;
        private TelescopePairSession? pair;
        private ModuleKoffeeExperiment? pendingExperiment;
        private CelestialBody? pendingTarget;
        private Guid pendingContainer;
        private long pendingRevision;
        private string captureReason = "NoExperiment";
        private static ModuleKoffeeTelescope? active;
        private TelescopeStockRenderer? renderer;
        private Coroutine? renderLoop;
        private Rect window = new Rect(100, 80, 544, 812);
        private readonly Rect imageRect = new Rect(16, 54, 512, 512);
        private readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();
        private CelestialBody? renderedTarget;
        private bool hasFrame;
        private float fieldOfView = TelescopeOptics.MaxFieldOfView;
        private string status = "";
        private string targetName = "";
        private string lockId = "";
        private string windowTitle = "";
        private string zoomOut = "", zoomIn = "", close = "";

        // Live geometry only. No observation, image or campaign is persisted by this module.
        public CelestialBody? TargetBody { get; private set; }
        public Transform? OpticalReference { get; private set; }
        public TelescopeViewState ViewState { get; private set; }
        public float FieldOfView => fieldOfView;
        public double SampleUniversalTime { get; private set; }
        public bool HasCurrentImage => active == this && CanObserve() && !vessel.packed &&
            !MapView.MapIsEnabled && hasFrame && renderedTarget == ResolveTarget();

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            pair = new TelescopePairSession(this);
            OpticalReference = part.FindModelTransform(cameraTransformName);
            lockId = "Koffee.STS25." + GetInstanceID();
            windowTitle = Localizer.Format("#LOC_Koffee_STS25_Viewer_Title") + " - " + part.partInfo.title + " [" + part.flightID + "]";
            zoomOut = Localizer.Format("#LOC_Koffee_STS25_Viewer_ZoomOut");
            zoomIn = Localizer.Format("#LOC_Koffee_STS25_Viewer_ZoomIn");
            close = Localizer.Format("#LOC_Koffee_STS25_Viewer_Close");
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "#LOC_Koffee_STS25_Viewer_Open")]
        public void OpenViewer()
        {
            if (!CanObserve()) return;
            var candidates = ObservationExperiments();
            if (!candidates.Any(m => StationTelescopeObservation.Readiness(m, this) == ""))
            { ScreenMessages.PostScreenMessage(Localizer.Format("#Koffee_Telescope_Requirements"), 5f, ScreenMessageStyle.UPPER_CENTER); return; }
            if (active == this) return;
            if (active != null) active.CloseViewer();
            active = this;
            var experiments = ObservationExperiments();
            observationExperiment = experiments.FirstOrDefault(m => StationTelescopeObservation.Readiness(m, this) == "");
            pendingExperiment = null;
            captureReason = "Image";
            hasFrame = false;
            status = Localizer.Format("#LOC_Koffee_STS25_Viewer_Waiting");
            renderLoop = StartCoroutine(RenderFrames());
        }

        internal void OpenForExperiment(ModuleKoffeeExperiment experiment)
        {
            if (StationTelescopeObservation.Readiness(experiment, this) != "")
            { ScreenMessages.PostScreenMessage(Localizer.Format("#Koffee_Telescope_Requirements"), 5f, ScreenMessageStyle.UPPER_CENTER); return; }
            OpenViewer();
            if (active != this) return;
            pair?.Reset();
            observationExperiment = experiment;
            pendingExperiment = null;
            captureReason = "Image";
        }

        private ModuleKoffeeExperiment[] ObservationExperiments() => vessel.parts
            .Where(p => p != null && p.State != PartStates.DEAD)
            .SelectMany(p => p.Modules.OfType<ModuleKoffeeExperiment>())
            .Where(m => StationExperimentCatalogLoader.Current.TryGet(m.experimentID, out var definition) &&
                StationTelescopeObservation.Required(definition) && StationTelescopeObservation.Readiness(m, this) == "").OrderBy(m => m.part.persistentId).ToArray();

        private bool CanObserve() => HighLogic.LoadedSceneIsFlight && part != null &&
            vessel != null && vessel == FlightGlobals.ActiveVessel && vessel.loaded && part.State != PartStates.DEAD;

        private static CelestialBody? ResolveTarget()
        {
            var target = FlightGlobals.fetch == null ? null : FlightGlobals.fetch.VesselTarget;
            var body = target as CelestialBody;
            // Resolve through the actual catalog, never through a hard-coded stock name list.
            if (body == null || FlightGlobals.Bodies == null || !FlightGlobals.Bodies.Contains(body)) return null;
            // The root star is outside this delivery. Extra star systems/mod bodies remain unvalidated.
            return Planetarium.fetch == null || body == Planetarium.fetch.Sun ? null : body;
        }

        private void RefreshGeometry()
        {
            TargetBody = ResolveTarget();
            SampleUniversalTime = Planetarium.GetUniversalTime();
            targetName = TargetBody == null ? "" : TelescopeBodyNames.Display(TargetBody);
            if (OpticalReference == null) { SetState(TelescopeViewState.MissingOptics); return; }
            if (TargetBody == null)
            {
                SetState(FlightGlobals.fetch.VesselTarget == null ? TelescopeViewState.NoTarget : TelescopeViewState.UnsupportedTarget);
                return;
            }
            Vector3d offset = TargetBody.position - (Vector3d)OpticalReference.position;
            double distance = offset.magnitude;
            if (distance <= TargetBody.Radius) { SetState(TelescopeViewState.Unavailable); return; }
            Vector3d direction = offset / distance;
            foreach (var body in FlightGlobals.Bodies)
            {
                if (body == TargetBody) continue;
                Vector3d foreground = body.position - (Vector3d)OpticalReference.position;
                if (TelescopeOptics.Occludes(direction.x, direction.y, direction.z, distance - TargetBody.Radius,
                    foreground.x, foreground.y, foreground.z, body.Radius))
                { SetState(TelescopeViewState.Occluded); return; }
            }
            Vector3 local = OpticalReference.InverseTransformDirection((Vector3)direction);
            double extent = Math.Tan(fieldOfView * Math.PI / 360);
            bool inside = local.z > 0 && Math.Abs(local.x / local.z) <= extent && Math.Abs(local.y / local.z) <= extent;
            SetState(inside ? TelescopeViewState.Ready : TelescopeViewState.OutsideField);
            if (!inside)
            {
                double horizontal = Math.Atan2(local.x, local.z) * 180 / Math.PI;
                double vertical = Math.Atan2(local.y, Math.Sqrt(local.x*local.x + local.z*local.z)) * 180 / Math.PI;
                status = Localizer.Format("#LOC_Koffee_STS25_Viewer_OutsideAngles", horizontal.ToString("F1"), vertical.ToString("F1"));
            }
        }

        private void SetState(TelescopeViewState state)
        {
            ViewState = state;
            status = Localizer.Format("#LOC_Koffee_STS25_Viewer_" + state);
        }

        private IEnumerator RenderFrames()
        {
            while (active == this)
            {
                // After stock LateUpdate has shifted floating/scaled origins and updated cameras.
                yield return endOfFrame;
                if (!CanObserve()) { CloseViewer(); yield break; }
                hasFrame = false;
                if (MapView.MapIsEnabled || FlightGlobals.ActiveVessel.packed)
                {
                    SetState(TelescopeViewState.Unavailable);
                    pair?.Suspend();
                    pendingExperiment = null;
                    captureReason = "Unavailable";
                    renderer?.Dispose(); renderer = null;
                    continue;
                }
                try
                {
                    RefreshGeometry();
                    renderedTarget = TargetBody;
                    if (ViewState != TelescopeViewState.Ready && ViewState != TelescopeViewState.OutsideField)
                    {
                        pair?.Suspend();
                        pendingExperiment = null;
                        captureReason = ViewState == TelescopeViewState.Occluded ? "Occluded" :
                            ViewState == TelescopeViewState.MissingOptics ? "Optics" : "Target";
                        renderer?.Dispose(); renderer = null;
                        continue;
                    }
                    if (renderer == null) renderer = new TelescopeStockRenderer(parallaxResourceExperiment);
                    bool complete = renderer.Render(OpticalReference!, fieldOfView,
                        ViewState == TelescopeViewState.Ready ? TargetBody : null, pair?.CompanionFor(TargetBody));
                    renderedTarget = TargetBody;
                    hasFrame = complete;
                    pair?.Frame(observationExperiment);
                    captureReason = TelescopeObservationAcquisition.Evaluate(this, observationExperiment, out var capture);
                    if (pendingExperiment != null)
                    {
                        // The selected ED, its revision and stock target must still
                        // be the ones requested. Capture only this rendered frame.
                        bool accepted = captureReason == "" && capture != null && pendingExperiment == observationExperiment &&
                            pendingTarget == TargetBody && pendingExperiment.TryRecordTelescopeCapture(pendingContainer, pendingRevision, capture);
                        if (!accepted) ScreenMessages.PostScreenMessage(Localizer.Format("#Koffee_Telescope_RequestRejected"), 5f, ScreenMessageStyle.UPPER_CENTER);
                        pendingExperiment = null;
                        captureReason = TelescopeObservationAcquisition.Evaluate(this, observationExperiment, out _);
                    }
                }
                catch (Exception e)
                {
                    pair?.Suspend();
                    Debug.LogError("[Koffee STS25] Viewer stopped: " + e);
                    renderer?.Dispose(); renderer = null;
                    SetState(TelescopeViewState.Unavailable);
                    pendingExperiment = null;
                    captureReason = "Image";
                    // One report per open, no retry/log storm every frame.
                    renderLoop = null;
                    yield break;
                }
            }
        }

        public void Update()
        {
            if (Events?[nameof(OpenViewer)] != null)
                Events[nameof(OpenViewer)].active = CanObserve() && ObservationExperiments().Length > 0;
            if (active != this) return;
            if (!CanObserve() || observationExperiment == null || StationTelescopeObservation.Readiness(observationExperiment, this) != "") { CloseViewer(); return; }
            Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (!MapView.MapIsEnabled && window.Contains(mouse))
                InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS | ControlTypes.MAP_UI | ControlTypes.TWEAKABLES, lockId);
            else InputLockManager.RemoveControlLock(lockId);
        }

        public void OnGUI()
        {
            if (active != this || !CanObserve() || MapView.MapIsEnabled) return;
            var previousSkin = GUI.skin;
            GUI.skin = HighLogic.Skin;
            window.width = pair?.Applies(observationExperiment) == true ? 866 : 544;
            window.x = Mathf.Clamp(window.x, 0, Math.Max(0, Screen.width - window.width));
            window.y = Mathf.Clamp(window.y, 0, Math.Max(0, Screen.height - window.height));
            try { window = GUI.Window(GetInstanceID(), window, DrawWindow, windowTitle); }
            finally { GUI.skin = previousSkin; }
        }

        private void DrawWindow(int id)
        {
            bool pairMode = pair?.Applies(observationExperiment) == true;
            if (pairMode) pair!.Draw(new Rect(540, 28, 310, 780));
            // A target switch between the render coroutine and OnGUI must never relabel an old image.
            bool current = renderedTarget == ResolveTarget();
            GUI.Label(new Rect(16, 28, 512, 24), current ? targetName : "");
            GUI.DrawTexture(imageRect, Texture2D.blackTexture);
            if (hasFrame && current && renderer != null)
                GUI.DrawTexture(imageRect, renderer.Image, ScaleMode.StretchToFill, false);
            float cx = imageRect.center.x, cy = imageRect.center.y;
            GUI.DrawTexture(new Rect(cx - 9, cy, 19, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx, cy - 9, 1, 19), Texture2D.whiteTexture);
            GUI.Label(new Rect(16, 570, 512, 38), current ? status : Localizer.Format("#LOC_Koffee_STS25_Viewer_Waiting"));
            GUI.Label(new Rect(16, 608, 512, 22), Localizer.Format("#LOC_Koffee_STS25_Viewer_Fov",
                fieldOfView.ToString("F3"), TelescopeOptics.Magnification(fieldOfView).ToString("F1")));
            if (GUI.Button(new Rect(16, 634, 160, 24), zoomOut)) ChangeZoom(2);
            if (GUI.Button(new Rect(184, 634, 160, 24), zoomIn)) ChangeZoom(0.5f);
            if (GUI.Button(new Rect(352, 634, 176, 24), close)) CloseViewer();
            var experiments = ObservationExperiments();
            bool installed = observationExperiment != null && experiments.Contains(observationExperiment);
            string association = installed ? observationExperiment!.part.partInfo.title + " / ED-01 [" +
                observationExperiment.part.flightID + "] / " + (observationExperiment.State?.ContainerId.ToString("N") ?? "") :
                Localizer.Format("#Koffee_Telescope_NoExperiment");
            GUI.Label(new Rect(16, 664, 512, 42), association);
            if (experiments.Length > 1 || (!installed && experiments.Length > 0))
                if (GUI.Button(new Rect(16, 708, 512, 22), Localizer.Format("#Koffee_Telescope_NextExperiment")))
                {
                    int index = Array.IndexOf(experiments, observationExperiment);
                    pair?.Reset();
                    observationExperiment = experiments[(index + 1) % experiments.Length];
                    pendingExperiment = null;
                    captureReason = "Image";
                }
            var saved = installed ? observationExperiment!.State?.TelescopeCapture : null;
            string detail = saved == null ? Localizer.Format("#Koffee_Telescope_Reason_" +
                (current ? captureReason == "" ? "Ready" : captureReason : "Image")) :
                Localizer.Format("#Koffee_Telescope_Saved", TelescopeBodyNames.Display(FlightGlobals.GetBodyByName(saved.Target), saved.Target), saved.UT.ToString("F3")) + " " +
                Localizer.Format("#Koffee_Telescope_Reason_" + (captureReason == "" ? "ArchiveOnly" : captureReason));
            GUI.Label(new Rect(16, 734, 512, 48), pairMode ? Localizer.Format(saved == null ? "#Koffee_Pair_ViewerHint" : "#Koffee_Pair_Reason_Saved") : detail);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && !pairMode && installed && current && hasFrame && captureReason == "" && pendingExperiment == null;
            if (!pairMode && GUI.Button(new Rect(16, 784, 512, 24), Localizer.Format("#Koffee_Telescope_Capture")))
            {
                pendingExperiment = observationExperiment;
                pendingContainer = observationExperiment!.State!.ContainerId;
                pendingRevision = observationExperiment.State.Revision;
                pendingTarget = TargetBody;
            }
            GUI.enabled = previousEnabled;
            GUI.DragWindow(new Rect(0, 0, window.width, 26));
        }

        private void ChangeZoom(float factor)
        {
            pair?.Suspend();
            fieldOfView = TelescopeOptics.ClampFieldOfView(fieldOfView * factor);
            hasFrame = false;
            pendingExperiment = null;
            captureReason = "Image";
        }

        public void CloseViewer()
        {
            pair?.Reset();
            if (active == this) active = null;
            if (renderLoop != null) { StopCoroutine(renderLoop); renderLoop = null; }
            hasFrame = false; renderedTarget = null; TargetBody = null;
            pendingExperiment = null; pendingTarget = null; observationExperiment = null;
            captureReason = "NoExperiment";
            ViewState = TelescopeViewState.Unavailable;
            renderer?.Dispose(); renderer = null;
            if (!string.IsNullOrEmpty(lockId)) InputLockManager.RemoveControlLock(lockId);
        }

        public void OnDisable() => CloseViewer();
        public void OnDestroy() => CloseViewer();
    }
}

using System;
using System.Linq;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Visual adapter only. Stock owns playback and its persisted pose. Automatic
    // requests are derived from read-only snapshots, never from another Tick.
    public sealed class ModuleKoffeeExperimentAnimation : PartModule
    {
        [KSPField] public string animationName = "";
        private bool automatic, manualAllowed, diagnosed, broken;
        private float nextVisualRefresh;

        internal bool TryState(out ModuleAnimateGeneric stock, out AnimationState state)
        {
            stock = null!; state = null!;
            if (broken) return false;
            try { return Resolve(out stock,out state); }
            catch (Exception error)
            {
                broken = true;
                if (!diagnosed) Debug.LogError("[Koffee] Experiment animation disabled: " + error);
                diagnosed = true; return false;
            }
        }
        private bool Resolve(out ModuleAnimateGeneric stock, out AnimationState state)
        {
            stock = null!; state = null!;
            if (part == null || string.IsNullOrWhiteSpace(animationName)) return false;
            var adapters = part.Modules.OfType<ModuleKoffeeExperimentAnimation>().Where(m => m.animationName == animationName).ToArray();
            var modules = part.Modules.OfType<ModuleAnimateGeneric>().Where(m => m.animationName == animationName).ToArray();
            var animators = part.FindModelAnimators(animationName);
            if (adapters.Length == 1 && modules.Length == 1 && animators != null && animators.Length == 1 && animators[0] != null &&
                ReferenceEquals(modules[0].GetAnimation(),animators[0]))
            {
                var found = animators[0][animationName];
                if (found != null && found.length > 0 && !float.IsNaN(found.length) && !float.IsInfinity(found.length))
                { stock = modules[0]; state = found; return true; }
            }
            if (!diagnosed) Debug.LogError("[Koffee] Unavailable or ambiguous experiment animation '" + animationName + "', part " + part.flightID + ".");
            diagnosed = true; return false;
        }
        // Stock animTime (Progress) is the settled/persisted endpoint. Unity's
        // transient AnimationState clock can reset or overshoot after playback.
        internal bool Open => TryState(out var stock, out _) && !stock.IsMoving() &&
            !float.IsNaN(stock.Progress) && !float.IsInfinity(stock.Progress) && stock.Progress >= 1;

        internal static ModuleKoffeeExperimentAnimation? Find(Part? part, string clip)
        {
            var found = part?.Modules.OfType<ModuleKoffeeExperimentAnimation>().Where(m => m.animationName == clip).ToArray();
            return found?.Length == 1 ? found[0] : null;
        }
        // Solar preparation retains its explicit physical clip contract. Narrative
        // instruments advertise a single adapter on the deterministically selected PART.
        internal static ModuleKoffeeExperimentAnimation? For(StationExperimentDefinition definition, Part? instrument)
        {
            string physicalClip = StationExperimentAnimationPreparation.Clip(definition);
            if (physicalClip != "") return Find(instrument, physicalClip);
            if (definition.ResultReading != StationResultReading.None) return null;
            var configured = instrument?.Modules.OfType<ModuleKoffeeExperimentAnimation>().ToArray();
            return configured?.Length == 1 ? configured[0] : null;
        }
        // No Toggle per frame: inspect endpoint/direction before issuing an order.
        private void Order(bool open)
        {
            if (!TryState(out var stock, out var state)) return;
            bool reverse = stock.IsMoving() ? (state.speed > 0) != open : open ? stock.Progress < 1 : stock.Progress > 0;
            if (!reverse) return;
            bool manual = stock.allowManualControl, shield = stock.allowAnimationWhileShielded;
            try
            {
                stock.allowManualControl = true;
                if (!open) stock.allowAnimationWhileShielded = true; // Always permit safe visual closure.
                stock.Toggle();
            }
            finally { stock.allowManualControl = manual; stock.allowAnimationWhileShielded = shield; }
        }
        internal void Refresh()
        {
            if (!HighLogic.LoadedSceneIsFlight || part?.vessel == null || !part.vessel.loaded || part.vessel.packed) return;
            bool desired = StationExperimentAnimationPreparation.Requests(this);
            if (!desired)
                desired = StationExperimentRuntime.Capture(part.vessel).Any(s => s.Definition != null &&
                    s.State?.Status == ExperimentContainerStatus.Running && !s.WorkComplete &&
                    !s.Module.manuallyPaused && s.PauseReason == StationExperimentPauseReason.None &&
                    ReferenceEquals(For(s.Definition,
                        StationExperimentReadings.RequiredInstrument(s.Definition,part.vessel,s.Module.part)),this));
            if (!TryState(out var stock, out _)) return;
            if (desired && !automatic) { manualAllowed = stock.allowManualControl; automatic = true; }
            if (automatic) stock.allowManualControl = false;
            if (desired || automatic) Order(desired);
            if (!desired && automatic) { stock.allowManualControl = manualAllowed; automatic = false; }
        }
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            // A loaded stock pose is merely visual; it cannot resume a pending Start.
            if (HighLogic.LoadedSceneIsFlight) { Refresh(); if (!automatic) Order(false); }
        }
        private void Update()
        {
            if (Time.realtimeSinceStartup < nextVisualRefresh) return;
            nextVisualRefresh = Time.realtimeSinceStartup + .2f;
            Refresh();
        }
        private void OnDestroy()
        {
            if (automatic && TryState(out var stock,out _)) stock.allowManualControl = manualAllowed;
        }
    }
}

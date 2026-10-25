using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames,
        GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION, GameScenes.EDITOR)]
    public sealed class KerbalSpecialtyScenario : ScenarioModule
    {
        private static KerbalSpecialtyScenario? current;
        private KerbalSpecialtyPersistence persistence = new KerbalSpecialtyPersistence(new ScientistSpecialtyGenerator());
        private ScientistLevelPersistence levels = new ScientistLevelPersistence();
        private Game? owner;
        private bool reconciliationPending;
        private bool subscribed;

        public override void OnAwake()
        {
            base.OnAwake();
            current = this;
            owner = HighLogic.CurrentGame;
            persistence = new KerbalSpecialtyPersistence(new ScientistSpecialtyGenerator());
            levels = new ScientistLevelPersistence();
            reconciliationPending = true;
            if (!subscribed)
            {
                GameEvents.onKerbalAddComplete.Add(OnKerbalAddComplete);
                GameEvents.onKerbalLevelUp.Add(OnKerbalLevelUp);
                subscribed = true;
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            owner = HighLogic.CurrentGame;
            foreach (string diagnostic in persistence.Load(node)) Debug.LogWarning("[Koffee] " + diagnostic);
            foreach (string diagnostic in levels.Load(node)) Debug.LogWarning("[Koffee] " + diagnostic);
            // Seed the previous level as soon as a real loaded roster is available.
            // Use the loaded game's UT: Planetarium may still belong to the old scene.
            // Profile generation remains deferred until reconciliation.
            if (owner != null && owner.CrewRoster != null)
                foreach (ProtoCrewMember crew in owner.CrewRoster.Kerbals()) ObserveLevel(crew, false, owner.UniversalTime);
            // Reconcile on the next Unity update, after the serialized roster/Scenarios.
            reconciliationPending = true;
        }

        internal void Update()
        {
            if (reconciliationPending) Reconcile();
        }

        private void Reconcile()
        {
            if (!persistence.IsLoaded || !ReferenceEquals(current, this)) return;
            if (owner == null) owner = HighLogic.CurrentGame;
            if (owner == null || !ReferenceEquals(owner, HighLogic.CurrentGame) || owner.CrewRoster == null) return;
            foreach (ProtoCrewMember crew in owner.CrewRoster.Kerbals())
                if (crew != null)
                {
                    persistence.EnsureScientistProfile(crew.name, crew.trait, out _);
                    ObserveLevel(crew, false);
                }
            reconciliationPending = false;
        }

        private void OnKerbalAddComplete(ProtoCrewMember crew)
        {
            if (!persistence.IsLoaded || !ReferenceEquals(current, this) || owner == null ||
                !ReferenceEquals(owner, HighLogic.CurrentGame) || owner.CrewRoster == null ||
                crew == null || string.IsNullOrEmpty(crew.name)) return;
            if (owner.CrewRoster.Exists(crew.name) && ReferenceEquals(owner.CrewRoster[crew.name], crew))
            {
                persistence.EnsureScientistProfile(crew.name, crew.trait, out _);
                ObserveLevel(crew, false);
            }
        }

        private void OnKerbalLevelUp(ProtoCrewMember crew)
        {
            if (!persistence.IsLoaded || !ReferenceEquals(current, this) || owner == null ||
                !ReferenceEquals(owner, HighLogic.CurrentGame) || owner.CrewRoster == null ||
                crew == null || string.IsNullOrEmpty(crew.name)) return;
            if (owner.CrewRoster.Exists(crew.name) && ReferenceEquals(owner.CrewRoster[crew.name], crew))
                ObserveLevel(crew, true);
        }

        private void ObserveLevel(ProtoCrewMember crew, bool effectiveEvent, double? loadedUT = null)
        {
            if (crew == null || crew.trait != ScientistSpecialtyProfile.ProfessionId) return;
            string? diagnostic = loadedUT.HasValue
                ? levels.ObserveLoaded(crew.name, crew.experienceLevel, loadedUT.Value)
                : levels.Observe(crew.name, crew.experienceLevel, ObservationUT(), effectiveEvent);
            if (diagnostic != null) Debug.LogWarning("[Koffee] " + diagnostic);
        }

        // This Scenario runs only in Flight, Space Center, Tracking and Editor.
        // Editor has no active Planetarium; a surviving instance can retain the
        // discarded flight's clock after revert. Its game time is authoritative.
        private double ObservationUT() => HighLogic.LoadedSceneIsEditor
            ? owner?.UniversalTime ?? 0 : Planetarium.GetUniversalTime();

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (!persistence.IsLoaded)
            {
                Debug.LogWarning("[Koffee] Specialty Scenario save requested before load; destination left untouched.");
                return;
            }
            // Also reconcile scientists added without an event, without consulting UI.
            Reconcile();
            persistence.Save(node);
            levels.Save(node);
        }

        internal static bool TryGetScientistProfile(string? name, out ScientistSpecialtyProfile profile)
        {
            profile = null!;
            return current != null && current.persistence.IsLoaded && current.owner != null &&
                ReferenceEquals(current.owner, HighLogic.CurrentGame) &&
                current.persistence.TryGetScientistProfile(name, out profile);
        }

        // Shared read-only boundary source for the later live/proto research runtimes.
        // A successful query certifies levels only, never context or Koffee coverage.
        internal static bool TryGetScientistLevelSegments(string? name, double start, double end,
            out IReadOnlyList<ScientistLevelSegment> segments)
        {
            segments = Array.Empty<ScientistLevelSegment>();
            double now = current?.ObservationUT() ?? double.NaN;
            if (!TryGetScientistProfile(name, out _) || !ScientistLevelHistory.Time(now) || end > now ||
                current!.owner!.CrewRoster == null || !current.owner.CrewRoster.Exists(name!)) return false;
            ProtoCrewMember crew = current.owner.CrewRoster[name!];
            return crew.trait == ScientistSpecialtyProfile.ProfessionId &&
                current.levels.CanReadUntil(name!, crew.experienceLevel, end) &&
                current.levels.TryGetSegments(name, start, end, out segments);
        }

        // Explicit checkpoint operation for the later runtimes, separate from UI
        // queries. Missing notifications create a gap, never a retroactive rate.
        internal static void ObserveScientistLevelAtCheckpoint(ProtoCrewMember crew)
        {
            if (current == null || !current.persistence.IsLoaded || current.owner == null ||
                !ReferenceEquals(current.owner, HighLogic.CurrentGame) || current.owner.CrewRoster == null ||
                crew == null || string.IsNullOrEmpty(crew.name)) return;
            if (current.owner.CrewRoster.Exists(crew.name) && ReferenceEquals(current.owner.CrewRoster[crew.name], crew))
                current.ObserveLevel(crew, false);
        }

        internal void OnDestroy()
        {
            if (subscribed)
            {
                GameEvents.onKerbalAddComplete.Remove(OnKerbalAddComplete);
                GameEvents.onKerbalLevelUp.Remove(OnKerbalLevelUp);
                subscribed = false;
            }
            if (ReferenceEquals(current, this)) current = null;
        }
    }
}

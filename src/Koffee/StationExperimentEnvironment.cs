using System;
using System.Collections.Generic;
using System.Linq;
using KSP.Localization;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // One scientific policy for commands, loaded work, snapshots and presentation.
    internal static class StationExperimentEnvironment
    {
        internal static StationExperimentPauseReason Evaluate(StationExperimentDefinition definition,
            ExperimentContainerState? state, Vessel vessel, string situation,
            object? container = null, StationEquipmentSnapshot? equipment = null)
        {
            if (vessel == null) return StationExperimentPauseReason.WrongBody;
            if (state == null || (state.Status != ExperimentContainerStatus.Finalized && state.Status != ExperimentContainerStatus.Spent &&
                !(state.Status == ExperimentContainerStatus.Running && state.WorkComplete())))
            {
                var equipmentReason = StationExperimentEquipment.Evaluate(definition, vessel, container, equipment);
                if (equipmentReason != StationExperimentPauseReason.None) return equipmentReason;
            }
            if (StationTelescopeObservation.Required(definition) && state?.Status == ExperimentContainerStatus.Running &&
                !StationTelescopeObservation.Matches(state, definition)) return StationExperimentPauseReason.InitialCaptureUnavailable;
            if (StationSolarAcquisition.Required(definition) && state?.Status == ExperimentContainerStatus.Running &&
                !StationSolarAcquisition.Matches(state, definition)) return StationExperimentPauseReason.InitialCaptureUnavailable;
            CelestialBody body = vessel.mainBody;
            if (body == null || FlightGlobals.Bodies == null || !FlightGlobals.Bodies.Contains(body) ||
                !definition.AllowsBody(body.bodyName)) return StationExperimentPauseReason.WrongBody;
            if (StationSurfaceCapture.Required(definition.ResultReading) || state?.SurfaceCapture != null)
            {
                if (!body.hasSolidSurface || body.isStar) return StationExperimentPauseReason.NoSolidSurface;
                if (state?.Status == ExperimentContainerStatus.Running && !StationSurfaceAcquisition.Matches(state, definition))
                    return StationExperimentPauseReason.InitialCaptureUnavailable;
            }
            ScienceExperiment science = ResearchAndDevelopment.GetExperiment(definition.Id);
            if (science == null || !Enum.TryParse(situation, false, out ExperimentSituations sit) ||
                !StationSituationRules.Allowed(science.situationMask, situation) ||
                !SurfaceAllowed(container, situation))
                return StationExperimentPauseReason.WrongSituation;
            if (science.requireAtmosphere && science.requireNoAtmosphere)
                return StationExperimentPauseReason.EnvironmentConfigurationInvalid;
            if (!science.IsAvailableWhile(sit, body)) return StationExperimentPauseReason.WrongAtmosphere;
            if (state?.HasContextBinding == true &&
                (body.bodyName != state.BoundBody || situation != state.BoundSituation))
                return state.HasSurfaceBinding ? StationExperimentPauseReason.SurfaceContextChanged :
                    StationExperimentPauseReason.ExecutionContextChanged;
            if (state?.HasSurfaceBinding == true)
            {
                if (situation != "SrfLanded" || body.bodyName != state.SurfaceBody)
                    return StationExperimentPauseReason.SurfaceContextChanged;
                if (!TryBiome(body, vessel.latitude, vessel.longitude, out string biome, out bool hasBiome))
                    return StationExperimentPauseReason.SurfaceContextUnverified;
                if (hasBiome != state.SurfaceHasBiome || biome != state.SurfaceBiome)
                    return StationExperimentPauseReason.SurfaceContextChanged;
            }
            else if (state?.Status == ExperimentContainerStatus.Available && situation == "SrfLanded" &&
                !TryBiome(body, vessel.latitude, vessel.longitude, out _, out _))
                return StationExperimentPauseReason.SurfaceContextUnverified;
            return StationExperimentPauseReason.None;
        }

        internal static bool SurfaceAllowed(object? container, string situation)
        {
            if (situation != "SrfLanded" && situation != "SrfSplashed") return true;
            if (container is Part part)
                return part.FindModuleImplementing<ModuleKoffeeExperiment>()?.surfaceEnabled ?? true;
            if (container is ProtoPartSnapshot snapshot)
            {
                var modules = ExperimentContainerSnapshotAdapter.Modules(snapshot);
                if (modules.Length > 1) return false;
                ConfigNode? values = modules.FirstOrDefault()?.moduleValues;
                if (values?.HasValue(nameof(ModuleKoffeeExperiment.surfaceEnabled)) == true)
                    return SurfaceCapability(values, false);
                // Older snapshots have no capability field: use the effective part default.
                return snapshot.partInfo?.partPrefab?.FindModuleImplementing<ModuleKoffeeExperiment>()
                    ?.surfaceEnabled ?? true;
            }
            return true;
        }

        // Missing fields retain the installation default; ambiguous or malformed
        // saved capabilities never grant surface access, loaded or unloaded.
        internal static bool SurfaceCapability(ConfigNode node, bool defaultValue)
        {
            string[] values = node.GetValues(nameof(ModuleKoffeeExperiment.surfaceEnabled));
            return values.Length == 0 ? defaultValue : values.Length == 1 &&
                bool.TryParse(values[0], out bool enabled) && enabled;
        }

        internal static bool TryBiome(CelestialBody body, double latitude, double longitude,
            out string biome, out bool hasBiome)
        {
            biome = ""; hasBiome = false;
            try
            {
                if (body == null || !ExperimentContainerState.Finite(latitude) ||
                    !ExperimentContainerState.Finite(longitude)) return false;
                // No map is a legitimate explicit context, not an invented biome.
                if (body.BiomeMap == null) return true;
                var attributes = body.BiomeMap.Attributes;
                if (attributes == null || attributes.Length == 0) return false;
                biome = ScienceUtil.GetExperimentBiome(body, latitude, longitude);
                string identity = biome;
                hasBiome = !string.IsNullOrWhiteSpace(identity) && attributes.Any(a => a != null && a.name == identity);
                return hasBiome;
            }
            catch (Exception error)
            {
                Debug.LogError("[Koffee] Surface biome could not be certified: " + error.Message);
                return false;
            }
        }

        internal static string Bodies(StationExperimentDefinition? definition) => definition == null ?
            KoffeeResearchDisplay.Unknown : definition.BodyMode == StationExperimentBodyMode.Any ?
            Localizer.Format("#Koffee_Environment_AnyBody") : string.Join(", ", definition.Bodies.Select(BodyName));

        private static string BodyName(string name) =>
            (FlightGlobals.GetBodyByName(name)?.bodyDisplayName ?? name).LocalizeRemoveGender();

        internal static string Atmosphere(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return KoffeeResearchDisplay.Unknown;
            ScienceExperiment science = ResearchAndDevelopment.GetExperiment(id);
            return Localizer.Format(science == null || (science.requireAtmosphere && science.requireNoAtmosphere) ?
                "#Koffee_Environment_Invalid" : science.requireAtmosphere ? "#Koffee_Environment_Required" :
                science.requireNoAtmosphere ? "#Koffee_Environment_Absent" : "#Koffee_Environment_Unrestricted");
        }

        internal static string Context(ExperimentContainerState? state)
        {
            if (state?.SolarCapture != null)
                return Localizer.Format(FlightGlobals.GetBodyByName(state.SolarCapture.Body)?.bodyDisplayName ??
                    state.SolarCapture.BodyDisplayName).LocalizeRemoveGender() + " / " +
                    Localizer.Format("#Koffee_Environment_Situation_" + state.SolarCapture.Situation);
            if (state?.HasContextBinding != true) return Localizer.Format("#Koffee_Environment_NoContext");
            string body = BodyName(state.BoundBody);
            return body + " / " + Localizer.Format("#Koffee_Environment_Situation_" + state.BoundSituation);
        }

        internal static string Surface(ExperimentContainerState? state)
        {
            if (state?.HasSurfaceBinding != true) return Localizer.Format("#Koffee_Environment_NoBinding");
            CelestialBody body = FlightGlobals.GetBodyByName(state.SurfaceBody);
            string biome = state.SurfaceHasBiome ? (body == null ? state.SurfaceBiome : ScienceUtil.GetBiomedisplayName(body, state.SurfaceBiome)) :
                Localizer.Format("#Koffee_Environment_NoBiome");
            return BodyName(state.SurfaceBody) + " / " + biome;
        }
    }

    // A new snapshot, scene, loaded/background transition or movement cannot certify
    // the interval before its first observation. Unloaded landed vessels must be stationary.
    internal static class StationSurfaceContinuity
    {
        private sealed class Observation
        {
            internal object? Vessel, Proto;
            internal bool Loaded;
            internal GameScenes Scene;
            internal double Latitude, Longitude, UT;
        }
        private static readonly Dictionary<Guid, Observation> observations = new Dictionary<Guid, Observation>();
        private static object? game;
        internal static void Clear() => observations.Clear();
        internal static void Invalidate(Vessel vessel)
        {
            // Events cover leave/return between polling checkpoints; biome changes
            // are observed by the module Update and by the background processor.
            if (vessel == null) return;
            double now = Planetarium.GetUniversalTime();
            if (vessel.loaded)
            {
                foreach (ModuleKoffeeExperiment module in vessel.parts.SelectMany(p => p.Modules.OfType<ModuleKoffeeExperiment>()))
                    if (module.State?.HasContextBinding == true && module.State.Status == ExperimentContainerStatus.Running && !module.State.WorkComplete())
                    {
                        observations.Remove(module.State.RunId!.Value);
                        module.RebaseCheckpoint(now, StationExperimentRuntime.CurrentBody(vessel), StationExperimentRuntime.CurrentSituation(vessel));
                    }
            }
            else if (vessel.protoVessel?.protoPartSnapshots != null)
                foreach (ProtoPartSnapshot part in vessel.protoVessel.protoPartSnapshots)
                {
                    ModuleKoffeeExperiment module = part.partPrefab?.Modules?.OfType<ModuleKoffeeExperiment>().FirstOrDefault()!;
                    if (module != null && ExperimentContainerSnapshotAdapter.Read(part, ExperimentContainerSnapshotAdapter.ExperimentId(part), out var state, out _) == ExperimentContainerReadResult.Valid &&
                        state?.HasContextBinding == true && state.Status == ExperimentContainerStatus.Running && !state.WorkComplete())
                    {
                        observations.Remove(state.RunId!.Value);
                        ExperimentContainerSnapshotAdapter.Write(part, state.RebaseCheckpoint(now,
                            StationExperimentRuntime.CurrentBody(vessel), StationExperimentRuntime.CurrentSituation(vessel)), out _);
                    }
                }
        }
        internal static bool Observe(ExperimentContainerState state, Vessel vessel, double? observedUT = null, bool? loadedOverride = null)
        {
            if (!state.HasContextBinding || !state.RunId.HasValue) return true;
            if (!ReferenceEquals(game, HighLogic.CurrentGame))
            { observations.Clear(); game = HighLogic.CurrentGame; }
            observations.TryGetValue(state.RunId.Value, out Observation? previous);
            double now = observedUT ?? Planetarium.GetUniversalTime();
            bool loaded = loadedOverride ?? vessel.loaded;
            bool continuous = previous != null && ReferenceEquals(previous.Vessel, vessel) &&
                ReferenceEquals(previous.Proto, vessel.protoVessel) && previous.Loaded == loaded &&
                now >= previous.UT && state.LastCheckpointUT.HasValue && state.LastCheckpointUT.Value >= previous.UT &&
                previous.Scene == HighLogic.LoadedScene && (!state.HasSurfaceBinding || loaded ||
                    (previous.Latitude == vessel.latitude && previous.Longitude == vessel.longitude));
            observations[state.RunId.Value] = new Observation { Vessel = vessel, UT = now,
                Proto = vessel.protoVessel, Loaded = loaded, Scene = HighLogic.LoadedScene,
                Latitude = vessel.latitude, Longitude = vessel.longitude };
            return continuous;
        }
    }
}

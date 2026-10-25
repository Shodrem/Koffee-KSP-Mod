using System;

namespace ShoAerospace.Koffee
{
    // This marker belongs to Koffee, not BRP. Its wire format uses only strings
    // so a save remains readable when the optional assembly has been removed.
    // Persist it only when the corresponding provider actually owns the work.
    internal readonly struct StationPowerSaveMarker
    {
        internal const string Schema = "1";
        internal StationPowerProviderKind Provider { get; }
        internal string ProviderId => Provider == StationPowerProviderKind.Brp ? "brp" : "stock";

        internal StationPowerSaveMarker(StationPowerProviderKind provider)
        {
            if (provider != StationPowerProviderKind.Stock && provider != StationPowerProviderKind.Brp)
                throw new ArgumentOutOfRangeException(nameof(provider));
            Provider = provider;
        }

        internal static bool TryRead(string? schema, string? provider, out StationPowerSaveMarker marker)
        {
            marker = default;
            // Pre-integration saves used the stock policy. A partly missing or
            // future marker is unknown, not permission to reuse its intervals.
            if (schema == null && provider == null) return true;
            if (schema != Schema || (provider != "stock" && provider != "brp")) return false;
            marker = new StationPowerSaveMarker(provider == "brp" ?
                StationPowerProviderKind.Brp : StationPowerProviderKind.Stock);
            return true;
        }
    }

    internal sealed class StationPowerSaveContinuity
    {
        internal bool RequiresRebase { get; }
        internal double LoadedUT { get; }

        internal StationPowerSaveContinuity(string? savedSchema, string? savedProvider,
            StationPowerProviderKind selected, double loadedUT)
        {
            if (!ExperimentContainerState.Finite(loadedUT) || loadedUT < 0)
                throw new ArgumentOutOfRangeException(nameof(loadedUT));
            var current = new StationPowerSaveMarker(selected);
            RequiresRebase = !StationPowerSaveMarker.TryRead(savedSchema, savedProvider, out var previous) ||
                previous.Provider != current.Provider;
            LoadedUT = loadedUT;
        }

        // The caller must publish this state through the existing live/snapshot
        // transaction before replacing its old provider marker. No EC, tazas or
        // research are charged for the unobserved interval up to LoadedUT.
        internal ExperimentContainerState Rebase(ExperimentContainerState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (!RequiresRebase || state.Status != ExperimentContainerStatus.Running || state.WorkComplete()) return state;
            if (state.LastCheckpointUT == LoadedUT && !state.CheckpointVerified) return state;
            return state.RebaseCheckpoint(LoadedUT, state.CheckpointBody, state.CheckpointSituation);
        }
    }
}

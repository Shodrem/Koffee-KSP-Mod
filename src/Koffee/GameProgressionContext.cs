using System;

namespace ShoAerospace.Koffee
{
    internal interface IGameProgressionContext
    {
        bool IsSandbox { get; }
        bool HasTechnologyProgression { get; }
        bool CanQueryResearchAndDevelopment { get; }
        bool IsTechnologyUnlocked(string techId);
    }

    internal enum KoffeeGameMode
    {
        Other,
        Sandbox,
        Career,
        Science
    }

    internal sealed class GameProgressionContext : IGameProgressionContext
    {
        private readonly KoffeeGameMode mode;
        private readonly Func<string, bool>? technologyLookup;

        internal bool IsSandbox => mode == KoffeeGameMode.Sandbox;
        internal bool HasTechnologyProgression =>
            mode == KoffeeGameMode.Career || mode == KoffeeGameMode.Science;
        internal bool CanQueryResearchAndDevelopment =>
            HasTechnologyProgression && technologyLookup != null;

        bool IGameProgressionContext.IsSandbox => IsSandbox;
        bool IGameProgressionContext.HasTechnologyProgression => HasTechnologyProgression;
        bool IGameProgressionContext.CanQueryResearchAndDevelopment => CanQueryResearchAndDevelopment;

        internal GameProgressionContext(KoffeeGameMode mode,
            Func<string, bool>? technologyLookup = null)
        {
            this.mode = mode;
            this.technologyLookup = technologyLookup;
        }

        public bool IsTechnologyUnlocked(string techId)
        {
            if (string.IsNullOrWhiteSpace(techId)) return false;
            if (IsSandbox) return true;
            if (!CanQueryResearchAndDevelopment) return false;
            try { return technologyLookup!(techId); }
            catch { return false; }
        }
    }

}

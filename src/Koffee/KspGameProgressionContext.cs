namespace ShoAerospace.Koffee
{
    internal static class KspGameProgressionContext
    {
        internal static IGameProgressionContext Capture()
        {
            Game? game = HighLogic.CurrentGame;
            if (game == null) return new GameProgressionContext(KoffeeGameMode.Other);
            if (game.Mode == Game.Modes.SANDBOX)
                return new GameProgressionContext(KoffeeGameMode.Sandbox);

            KoffeeGameMode mode = game.Mode == Game.Modes.CAREER
                ? KoffeeGameMode.Career
                : game.Mode == Game.Modes.SCIENCE_SANDBOX
                    ? KoffeeGameMode.Science : KoffeeGameMode.Other;
            if (mode == KoffeeGameMode.Other || ResearchAndDevelopment.Instance == null)
                return new GameProgressionContext(mode);

            return new GameProgressionContext(mode, techId =>
                ResearchAndDevelopment.GetTechnologyState(techId) == RDTech.State.Available);
        }
    }
}

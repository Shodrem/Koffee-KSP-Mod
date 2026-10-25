namespace ShoAerospace.Koffee
{
    internal static class KoffeeServices
    {
        internal static IGameProgressionContext Progression =>
            KspGameProgressionContext.Capture();
    }
}

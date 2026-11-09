using HarmonyLib;

namespace ShoAerospace.Koffee
{
    internal static class DeadlyRestrictions
    {
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Game), "Updated", new[] { typeof(GameScenes) }),
                prefix: new HarmonyMethod(typeof(DeadlyRestrictions), nameof(BeforeSnapshot)));
            harmony.Patch(AccessTools.Method(typeof(Part), "RemoveCrewmember"),
                prefix: new HarmonyMethod(typeof(DeadlyRestrictions), nameof(BeforeCrewRemoval)));
        }
        private static void BeforeSnapshot() => DeadlyScenario.FlushAll();
        private static void BeforeCrewRemoval(Part __instance) => DeadlyScenario.BeforeCrewRemoval(__instance);
    }
}

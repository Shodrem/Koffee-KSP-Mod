using System;
using HarmonyLib;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class KoffeeParameterDialog
    {
        private sealed class Listener
        {
            internal void Dismissed(DifficultyOptionsMenu menu, bool accepted) => Accepted(menu, accepted);
        }
        private static readonly Listener listener = new Listener();
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(DifficultyOptionsMenu), "Create", new[] {
                typeof(Game.Modes), typeof(GameParameters), typeof(bool),
                typeof(Callback<GameParameters, bool>), typeof(GameObject) }),
                prefix: new HarmonyMethod(typeof(KoffeeParameterDialog), nameof(EditCopy)));
            harmony.Patch(AccessTools.Method(typeof(GamePersistence), "LoadGameCfg", new[] {
                typeof(ConfigNode), typeof(string), typeof(bool), typeof(bool) }),
                postfix: new HarmonyMethod(typeof(KoffeeParameterDialog), nameof(Loaded)));
            GameEvents.OnDifficultySettingsDismiss.Add(listener.Dismissed);
        }
        internal static void Shutdown() => GameEvents.OnDifficultySettingsDismiss.Remove(listener.Dismissed);

        // KSP 1.12.5 edits gPars directly. Its backup is only used at Cancel.
        // Hand stock a working copy so all research readers keep seeing the save's
        // authority until Dismiss invokes the stock callback and replaces Parameters.
        private static void EditCopy(ref GameParameters initialParams, bool newGame)
        {
            if (newGame || !ReferenceEquals(initialParams, HighLogic.CurrentGame?.Parameters)) return;
            var saved = new ConfigNode();
            initialParams.Save(saved);
            initialParams = new GameParameters(saved);
        }

        // LoadGameCfg constructs a detached Game with its saved UT. Parameter-only
        // clones used by the difficulty dialog never pass through this hook.
        private static void Loaded(Game __result)
        {
            __result?.Parameters.CustomParams<KoffeeGameParameters>().ActivateAfterLoad(__result.UniversalTime);
        }

        // Fired AFTER OnDismiss(gPars, true) has installed Game.Parameters, and
        // BEFORE the input lock is removed. OnGameSettingsApplied belongs to the
        // outer audio/video/settings window, not difficulty confirmation.
        private static void Accepted(DifficultyOptionsMenu menu, bool accepted)
        {
            if (!accepted || HighLogic.CurrentGame == null) return;
            if (AccessTools.Field(typeof(DifficultyOptionsMenu), "isNewGame").GetValue(menu) is true) return;
            var edited = AccessTools.Field(typeof(DifficultyOptionsMenu), "gPars").GetValue(menu) as GameParameters;
            var backup = AccessTools.Field(typeof(DifficultyOptionsMenu), "backupPars").GetValue(menu) as GameParameters;
            if (!ReferenceEquals(edited, HighLogic.CurrentGame.Parameters) || backup == null) return;
            edited!.CustomParams<KoffeeGameParameters>().Accept(backup.CustomParams<KoffeeGameParameters>(), Planetarium.GetUniversalTime());
        }
    }
}

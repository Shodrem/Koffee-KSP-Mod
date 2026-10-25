using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSP.Localization;
using KSP.UI.TooltipTypes;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    internal static class ScientistTooltipPatches
    {
        private sealed class StockTooltip
        {
            internal readonly ProtoCrewMember Crew;
            internal readonly Game? Game;
            internal readonly string Description;
            internal readonly bool Show;
            internal StockTooltip(ProtoCrewMember crew, TooltipController_CrewAC tooltip)
            {
                Crew = crew;
                Game = HighLogic.CurrentGame;
                Description = tooltip.descriptionString;
                Show = tooltip.showTooltip;
            }
        }

        // Keep the stock text, not an appended result, so hover refreshes cannot duplicate our lines.
        // Weak keys release entries when KSP destroys/rebuilds its UI; no roster/profile is owned here.
        private static readonly ConditionalWeakTable<TooltipController_CrewAC, StockTooltip> stock =
            new ConditionalWeakTable<TooltipController_CrewAC, StockTooltip>();
        private static bool errorLogged;

        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.DeclaredMethod(typeof(TooltipController_CrewAC), "SetTooltip"),
                postfix: new HarmonyMethod(typeof(ScientistTooltipPatches), nameof(AfterSetTooltip)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(TooltipController_CrewAC), "OnTooltipAboutToSpawn"),
                prefix: new HarmonyMethod(typeof(ScientistTooltipPatches), nameof(BeforeSpawn)));
        }

        internal static void AfterSetTooltip(TooltipController_CrewAC __instance, ProtoCrewMember __0)
        {
            stock.Remove(__instance);
            if (__0 == null || __0.trait != ScientistSpecialtyProfile.ProfessionId) return;
            var original = new StockTooltip(__0, __instance);
            stock.Add(__instance, original);
            Refresh(__instance, original);
        }

        internal static void BeforeSpawn(TooltipController_CrewAC __instance)
        {
            if (stock.TryGetValue(__instance, out var original)) Refresh(__instance, original);
        }

        private static void Refresh(TooltipController_CrewAC tooltip, StockTooltip original)
        {
            try
            {
                string description = original.Description;
                bool show = original.Show;
                // A reused UI object must never display another game's profile with the same name.
                if (original.Game != null && ReferenceEquals(original.Game, HighLogic.CurrentGame) &&
                    original.Crew.trait == ScientistSpecialtyProfile.ProfessionId)
                {
                    string lines = KoffeeResearchDisplay.Specialties(null);
                    if (KerbalSpecialtyScenario.TryGetScientistProfile(original.Crew.name, out var profile))
                        lines = Localizer.Format("#Koffee_Research_TooltipPrimary",
                            KoffeeResearchDisplay.Category(profile.PrimaryCategory),
                            profile.Score(profile.PrimaryCategory).ToString(CultureInfo.InvariantCulture)) + "\n" +
                            Localizer.Format("#Koffee_Research_TooltipSecondary",
                            KoffeeResearchDisplay.Category(profile.SecondaryCategory),
                            profile.Score(profile.SecondaryCategory).ToString(CultureInfo.InvariantCulture));
                    description = string.IsNullOrEmpty(description) ? lines : description + "\n\n" + lines;
                    // Affinities also exist in Sandbox / with stock experience disabled.
                    show = true;
                }
                tooltip.descriptionString = description;
                tooltip.showTooltip = show;
            }
            catch (Exception error)
            {
                // Compute the whole addition before writing; stock UI remains usable on a formatting failure.
                if (errorLogged) return;
                errorLogged = true;
                Debug.LogWarning("[Koffee] Scientist tooltip could not be updated: " + error);
            }
        }
    }
}

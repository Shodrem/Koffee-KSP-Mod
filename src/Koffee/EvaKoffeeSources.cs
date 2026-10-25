using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace ShoAerospace.Koffee
{
    // Remember the actual airlock part, so docking/undocking and vessel renaming are harmless.
    internal static class EvaKoffeeSources
    {
        private static readonly ConditionalWeakTable<KerbalEVA, Origin> Origins =
            new ConditionalWeakTable<KerbalEVA, Origin>();

        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.DeclaredMethod(typeof(KerbalEVA), "OnLoad"),
                postfix: new HarmonyMethod(typeof(EvaKoffeeSources), nameof(Load)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(KerbalEVA), "OnSave"),
                postfix: new HarmonyMethod(typeof(EvaKoffeeSources), nameof(Save)));
        }

        internal static void OnEva(GameEvents.FromToAction<Part, Part> change)
        {
            if (change.from == null || change.to == null) return; // rescue/spawn has no origin
            KerbalEVA eva = change.to.FindModuleImplementing<KerbalEVA>();
            if (eva == null) return;
            Origins.Remove(eva);
            Origins.Add(eva, new Origin(change.from.flightID));
        }

        internal static Part? Consumer(Part part)
        {
            if (part == null || part.vessel == null) return null;
            if (!part.vessel.isEVA) return part;
            KerbalEVA eva = part.vessel.evaController;
            if (eva == null || !Origins.TryGetValue(eva, out Origin? origin) || origin == null) return null;
            foreach (Vessel vessel in FlightGlobals.Vessels)
                if (vessel != null && vessel.loaded && !vessel.isEVA)
                    foreach (Part source in vessel.parts)
                        if (source != null && source.flightID == origin.PartId) return source;
            return null;
        }

        private static void Load(KerbalEVA __instance, ConfigNode __0)
        {
            Origins.Remove(__instance);
            ConfigNode node = __0.GetNode(KoffeeIdentifiers.EvaOriginNode);
            if (node != null && uint.TryParse(node.GetValue(KoffeeIdentifiers.PartIdValue), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out uint id)) Origins.Add(__instance, new Origin(id));
        }

        private static void Save(KerbalEVA __instance, ConfigNode __0)
        {
            __0.RemoveNodes(KoffeeIdentifiers.EvaOriginNode);
            if (Origins.TryGetValue(__instance, out Origin? origin) && origin != null)
                __0.AddNode(KoffeeIdentifiers.EvaOriginNode).AddValue(
                    KoffeeIdentifiers.PartIdValue, origin.PartId.ToString(CultureInfo.InvariantCulture));
        }

        private sealed class Origin
        {
            internal readonly uint PartId;
            internal Origin(uint id) { PartId = id; }
        }
    }
}

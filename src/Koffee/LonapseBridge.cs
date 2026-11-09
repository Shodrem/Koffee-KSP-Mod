using System;
using System.Collections.Generic;
using System.Reflection;

namespace ShoAerospace.Koffee
{
    // Adapted from Lonapse's CC0 docs/api/LonapseWrapper.cs (API version 1).
    // Resolve only loaded assemblies: Lonapse is never a CLR dependency of Koffee.
    internal static class LonapseBridge
    {
        private static bool searched;
        private static MethodInfo? trigger, cancel;
        private static PropertyInfo? available;

        private static void Find()
        {
            if (searched) return;
            searched = true;
            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name != "Lonapse") continue;
                    Type? api = assembly.GetType("Lonapse.LonapseApi", false);
                    if (api == null || !(api.GetProperty("ApiVersion")?.GetValue(null, null) is int version) || version < 1) return;
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                    MethodInfo? offer = api.GetMethod("Trigger", flags, null,
                        new[] { typeof(string), typeof(Vessel), typeof(ProtoCrewMember),
                            typeof(IDictionary<string, double>), typeof(IDictionary<string, string>) }, null);
                    MethodInfo? drop = api.GetMethod("Cancel", flags, null, new[] { typeof(string) }, null);
                    PropertyInfo? ready = api.GetProperty("Available", flags);
                    if (offer?.ReturnType != typeof(bool) || drop?.ReturnType != typeof(bool) || ready?.PropertyType != typeof(bool)) return;
                    trigger = offer; cancel = drop; available = ready;
                    return;
                }
            }
            catch (Exception) { trigger = cancel = null; available = null; }
        }

        internal static bool Trigger(string eventId, Vessel vessel, ProtoCrewMember? kerbal)
        {
            Find();
            try
            {
                return available?.GetValue(null, null) is bool on && on &&
                    trigger?.Invoke(null, new object?[] { eventId, vessel, kerbal, null, null }) is bool accepted && accepted;
            }
            catch (Exception) { return false; } // Radio failure cannot roll back a successful gameplay action.
        }

        internal static void Cancel(string eventId)
        {
            Find();
            try { cancel?.Invoke(null, new object[] { eventId }); }
            catch (Exception) { }
        }
    }
}

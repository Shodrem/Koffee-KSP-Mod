using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Experimental resource lease only. Does not implement Parallax's camera-specific shadows.
    // Parallax Continued, detected by its required API. No CLR dependency.
    internal sealed class TelescopeParallaxResources : IDisposable
    {
        private const string VerifiedSha256 = "7b51d40f9759f0ef8e7f067dff60a26ad67345c8a93265c1a1f39206c00602b8";
        private const string HarmonyId = "ShoAerospace.Koffee.Telescope.ParallaxResources";
        private static TelescopeParallaxResources? owner;
        private readonly Type componentType;
        private readonly FieldInfo bodyField;
        private readonly PropertyInfo loaded, loading;
        private readonly MethodInfo load, visibility;
        private readonly Harmony harmony = new Harmony(HarmonyId);
        private object? pinnedBody;
        private object? pinnedCompanion;
        private int visibilitySkips, loadRequests;
        private bool unexpectedReloadReported;
        private bool disposed;

        private TelescopeParallaxResources(Type type)
        {
            if (owner != null) throw new InvalidOperationException("Parallax telescope lease already owned");
            componentType = type;
            bodyField = type.GetField("scaledBody", BindingFlags.Instance | BindingFlags.Public)!;
            var bodyType = bodyField.FieldType;
            loaded = bodyType.GetProperty("Loaded", BindingFlags.Instance | BindingFlags.Public)!;
            loading = bodyType.GetProperty("IsLoading", BindingFlags.Instance | BindingFlags.Public)!;
            load = bodyType.GetMethod("Load", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)!;
            visibility = type.GetMethod("UpdateVisibility", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!;
            // API shape is checked before installing this scoped visibility prefix.
            try
            {
                harmony.Patch(visibility, prefix: new HarmonyMethod(typeof(TelescopeParallaxResources), nameof(AllowVisibility)));
                owner = this;
            }
            catch
            {
                // A failed Patch can already have registered metadata. Roll back our prefix only.
                RemovePatch();
                throw;
            }
        }

        public static TelescopeParallaxResources? TryCreate(bool enabled)
        {
            if (!enabled) return null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "ParallaxContinued") continue;
                if (assembly.IsDynamic) return null;
                var type = assembly.GetType("Parallax.Scaled_System.ScaledOnDemandComponent", false);
                if (type == null || !ApiCompatible(type, out _))
                {
                    Debug.LogWarning("[Koffee STS25] Parallax Continued integration skipped: required resource API is missing or incompatible");
                    return null;
                }
                string? hash = null;
                try
                {
                    string location = assembly.Location;
                    if (!string.IsNullOrEmpty(location))
                        using (var sha = SHA256.Create())
                        using (var stream = File.OpenRead(location))
                            hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException ||
                    e is System.Security.SecurityException || e is NotSupportedException)
                {
                    Debug.LogWarning("[Koffee STS25] Parallax Continued binary fingerprint unavailable: " + e.Message);
                }
                if (hash != VerifiedSha256)
                    Debug.LogWarning("[Koffee STS25] Parallax Continued differs from tested 1.0.6 binary; required API is present, continuing integration. " +
                        "Assembly=" + assembly.FullName + " SHA256=" + (hash ?? "unavailable") + "; visual compatibility is unverified");
                Debug.Log("[Koffee STS25] Parallax Continued resource lease; required API present; camera-specific shadows remain unadapted");
                return new TelescopeParallaxResources(type);
            }
            return null;
        }

        internal static bool ApiCompatible(Type type, out string reason)
        {
            reason = "required resource API is missing or incompatible";
            const BindingFlags publicInstance = BindingFlags.Instance | BindingFlags.Public;
            const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                bool component = false;
                for (Type? current = type; current != null; current = current.BaseType)
                    if (current.FullName == "UnityEngine.Component") { component = true; break; }
                if (!component) return false;
                var field = type.GetField("scaledBody", publicInstance);
                if (field == null || !field.FieldType.IsClass || type.ContainsGenericParameters) return false;
                var body = field.FieldType;
                foreach (string name in new[] { "Loaded", "IsLoading" })
                {
                    var property = body.GetProperty(name, publicInstance);
                    var getter = property?.GetGetMethod();
                    if (property == null || property.PropertyType != typeof(bool) || property.GetIndexParameters().Length != 0 ||
                        getter == null || getter.IsStatic || getter.ContainsGenericParameters) return false;
                }
                var loadMethod = body.GetMethod("Load", publicInstance, null, Type.EmptyTypes, null);
                var visibilityMethod = type.GetMethod("UpdateVisibility", privateInstance, null, Type.EmptyTypes, null);
                if (loadMethod == null || loadMethod.ReturnType != typeof(void) || loadMethod.ContainsGenericParameters ||
                    visibilityMethod == null || visibilityMethod.ReturnType != typeof(void) || visibilityMethod.ContainsGenericParameters ||
                    visibilityMethod.GetMethodBody() == null) return false;
                reason = "";
                return true;
            }
            catch (Exception e) when (e is AmbiguousMatchException || e is TypeLoadException || e is NotSupportedException ||
                e is System.Security.SecurityException || e is FileNotFoundException)
            { reason = e.GetType().Name; return false; }
        }

        private static bool AllowVisibility(object __instance)
        {
            var lease = owner;
            if (lease == null || (lease.pinnedBody == null && lease.pinnedCompanion == null)) return true;
            // Multiple on-demand components can share one ParallaxScaledBody across scenes.
            // Retain the resource for all of them, without disabling their UpdateShadows.
            object? body = lease.bodyField.GetValue(__instance);
            if ((lease.pinnedBody == null || !ReferenceEquals(lease.pinnedBody, body)) &&
                (lease.pinnedCompanion == null || !ReferenceEquals(lease.pinnedCompanion, body))) return true;
            lease.visibilitySkips++;
            return false;
        }

        public bool Prepare(CelestialBody? target, CelestialBody? companion = null)
        {
            bool primaryReady = PrepareBody(target, false);
            bool companionReady = PrepareBody(companion, true);
            return primaryReady && companionReady;
        }

        private bool PrepareBody(CelestialBody? target, bool companion)
        {
            if (disposed) throw new ObjectDisposedException(nameof(TelescopeParallaxResources));
            var components = target == null || target.scaledBody == null ? new Component[0] :
                target.scaledBody.GetComponents(componentType);
            object? body = null;
            foreach (var component in components)
            {
                // Respect OnDisable; a disabled first component must not hide an active duplicate.
                if (component is Behaviour behaviour && !behaviour.isActiveAndEnabled) continue;
                body = bodyField.GetValue(component);
                if (body != null) break;
            }
            bool changed = !ReferenceEquals(body, companion ? pinnedCompanion : pinnedBody);
            if (companion) pinnedCompanion = body;
            else pinnedBody = body; // Releases the former body to its own policy; never Unload it.
            if (body == null) return true;
            bool wasLoaded = (bool)loaded.GetValue(body, null)!;
            bool isLoading = (bool)loading.GetValue(body, null)!;
            if (changed)
                Debug.Log("[Koffee STS25] Parallax target=" + target!.bodyName +
                    " Loaded=" + wasLoaded + " IsLoading=" + isLoading + " components=" + components.Length);
            // Do not race a coroutine already owned by Parallax. It keeps running normally.
            if (!wasLoaded && !isLoading)
            {
                if (!changed && !unexpectedReloadReported)
                {
                    unexpectedReloadReported = true;
                    Debug.LogWarning("[Koffee STS25] Parallax resource became unloaded while retained; visibilitySkips=" +
                        visibilitySkips + " loadRequests=" + loadRequests);
                }
                loadRequests++;
                load.Invoke(body, null);
            }
            bool ready = (bool)loaded.GetValue(body, null)!;
            if (changed)
            {
                var textures = body.GetType().GetField("loadedTextures")?.GetValue(body) as System.Collections.IDictionary;
                Debug.Log("[Koffee STS25] Parallax after request: Loaded=" + ready +
                    " textures=" + (textures == null ? "unknown" : textures.Count.ToString()));
            }
            return ready;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            pinnedBody = null; pinnedCompanion = null;
            if (owner != this) return;
            // Even if unpatching fails, any remaining prefix becomes a pass-through.
            owner = null;
            RemovePatch();
            Debug.Log("[Koffee STS25] Parallax lease released: visibilitySkips=" + visibilitySkips +
                " loadRequests=" + loadRequests);
            // Do not disable the component: OnDisable unloads textures, including shared terrain.
        }

        private void RemovePatch()
        {
            try { harmony.Unpatch(visibility, HarmonyPatchType.Prefix, HarmonyId); }
            catch (Exception e)
            {
                // Never prevent the caller from releasing cameras, RTs or input locks.
                Debug.LogError("[Koffee STS25] Could not remove Parallax resource prefix (no body retained): " + e);
            }
        }
    }
}

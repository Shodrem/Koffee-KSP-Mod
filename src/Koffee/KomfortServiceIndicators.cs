using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Only explicitly configured indicator meshes are touched; compatibility parts opt in.
    internal sealed class KomfortServiceIndicators : IDisposable
    {
        private readonly List<(Material Material, Color Base, int Role)> lights =
            new List<(Material, Color, int)>();
        private bool? enabled;
        private bool overloaded, wornOut, fuelEmpty;
        private readonly ModuleEmergencySupport? emergency;

        internal KomfortServiceIndicators(Part part, string greenNames, string orangeNames, string darkNames)
        {
            emergency = part.Modules.OfType<ModuleEmergencySupport>().FirstOrDefault();
            var bound = new HashSet<Renderer>();
            Bind(part, greenNames, 0, bound);
            Bind(part, orangeNames, 1, bound);
            Bind(part, darkNames, 2, bound);
        }

        private void Bind(Part part, string names, int role, HashSet<Renderer> bound)
        {
            foreach (string name in names.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Transform target = part.FindModelTransform(name.Trim());
                if (target == null) continue;
                foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
                {
                    if (!bound.Add(renderer)) continue;
                    // Renderer-owned copies prevent one service from changing another's lights.
                    foreach (Material material in renderer.materials)
                    {
                        if (!material.HasProperty("_EmissiveColor"))
                        {
                            Shader shader = Shader.Find("KSP/Emissive/Specular");
                            if (shader == null) continue;
                            material.shader = shader;
                        }
                        Color color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                        // A neutral mask lets the chosen status color control the glow independently of the authored mask.
                        material.SetTexture("_Emissive", Texture2D.whiteTexture);
                        lights.Add((material, color, role));
                    }
                }
            }
        }

        internal void SetEnabled(bool value, bool overCapacity, bool force = false, bool exhausted = false)
        {
            bool noFuel = emergency?.NuclearFuelExhausted == true;
            if (!force && enabled == value && overloaded == overCapacity && wornOut == exhausted && fuelEmpty == noFuel) return;
            foreach (var light in lights)
            {
                if (light.Material == null) continue;
                bool lit = light.Role == 2 ? noFuel : value &&
                    ((light.Role == 0 && !exhausted && !noFuel) || (light.Role == 1 && (overCapacity || exhausted)));
                light.Material.SetColor("_EmissiveColor", lit ? (light.Role == 2 ? new Color(1f, 0f, 0f, 1f) :
                    light.Role == 1 ? new Color(1f, .35f, .02f, 1f) : new Color(.15f, 1f, .05f, 1f)) : Color.black);
                if (light.Material.HasProperty("_Color"))
                    light.Material.SetColor("_Color", lit ? light.Base :
                        new Color(light.Base.r * .04f, light.Base.g * .04f, light.Base.b * .04f, light.Base.a));
            }
            enabled = value;
            overloaded = overCapacity; wornOut = exhausted; fuelEmpty = noFuel;
        }

        public void Dispose()
        {
            foreach (var light in lights)
                if (light.Material != null) UnityEngine.Object.Destroy(light.Material);
            lights.Clear();
        }
    }
}

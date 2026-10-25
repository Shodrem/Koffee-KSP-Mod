using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Keep stock PAW/actions, persistence, symmetry and resource handling. The
    // masked control light carries stock's actual (including power loss) output
    // across the separate exterior and InternalModel hierarchies.
    public sealed class ModuleKoffeeCabinLights : ModuleLight
    {
        public override void OnStart(StartState state)
        {
            var control = new GameObject(lightName);
            control.transform.SetParent(part.FindModelTransform("model") ?? part.transform, false);
            var light = control.AddComponent<Light>();
            light.type = LightType.Point;
            light.intensity = 1;
            light.cullingMask = 0;
            light.enabled = false;
            var visuals = control.AddComponent<KoffeeCabinLightVisuals>();
            visuals.Initialize(part, light);
            base.OnStart(state);
        }
    }

    public sealed class KoffeeCabinLightVisuals : MonoBehaviour
    {
        private Part owner = null!;
        private Light control = null!;
        private InternalModel? interior;
        private readonly List<Light> emitters = new List<Light>();
        private readonly List<MeshRenderer> windows = new List<MeshRenderer>();
        private readonly HashSet<Material> windowMaterials = new HashSet<Material>();
        private Shader windowShader = null!;
        private readonly List<Material> diffusers = new List<Material>();
        private static readonly Color WindowGlow = new Color(1f, .86f, .64f, 1f);

        public void Initialize(Part part, Light stockOutput)
        {
            owner = part;
            control = stockOutput;
            foreach (var renderer in part.FindModelComponents<MeshRenderer>())
            {
                // Blender truncates these instanced names at 63 bytes.
                if (renderer.name.IndexOf("__KF01_Window_Glass_Op", StringComparison.Ordinal) < 0) continue;
                windows.Add(renderer);
            }
            if (windows.Count == 0) return;
            windowShader = Shader.Find("KSP/Emissive/Diffuse");
            if (windowShader == null)
                Debug.LogError("[Koffee] Missing KSP/Emissive/Diffuse cabin window shader");
            UpdateWindows(0);
        }

        public void LateUpdate()
        {
            if (owner == null || control == null) return;
            if (interior != owner.internalModel)
            {
                ReleaseInterior();
                interior = owner.internalModel;
                if (interior != null) BindInterior();
            }
            float output = control.enabled ? Mathf.Clamp01(control.intensity) : 0;
            foreach (var light in emitters)
            {
                if (light == null) continue;
                light.enabled = output > .001f;
                light.intensity = output * .95f;
                light.color = control.color;
            }
            foreach (var material in diffusers)
                material.SetColor("_EmissiveColor", control.color * (2.5f * output));
            UpdateWindows(output);
        }

        private void UpdateWindows(float output)
        {
            if (windowShader == null) return;
            foreach (var renderer in windows)
            {
                if (renderer == null) continue;
                // Use Unity's private renderer instance, not a sharedMaterial
                // cached before KSP's render/variant lifecycle instantiates it.
                var material = renderer.material;
                windowMaterials.Add(material);
                if (material.shader != windowShader) material.shader = windowShader;
                material.SetTexture("_Emissive", Texture2D.whiteTexture);
                material.SetColor("_EmissiveColor", WindowGlow * output);
            }
        }

        private void BindInterior()
        {
            foreach (var renderer in interior!.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.name.EndsWith("__Console_Light_Diffuser", StringComparison.Ordinal)) continue;
                var material = new Material(renderer.sharedMaterial);
                renderer.sharedMaterial = material;
                diffusers.Add(material);
                var fixture = new GameObject("KoffeeCabinEmitter");
                fixture.transform.SetParent(renderer.transform, false);
                // The actual diffuser centre plus 9 cm into the cabin. Geometry
                // can be baked into mesh vertices; its transform need not mark it.
                Vector3 centre = renderer.bounds.center;
                Vector3 local = interior.transform.InverseTransformPoint(centre);
                fixture.transform.position = centre + interior.transform.TransformDirection(
                    new Vector3(-Mathf.Sign(local.x) * .09f, 0, 0));
                var light = fixture.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 3.5f;
                light.cullingMask = (1 << 16) | (1 << 20);
                light.renderMode = LightRenderMode.ForcePixel;
                light.shadows = LightShadows.None;
                light.enabled = false;
                emitters.Add(light);
            }
        }

        private void ReleaseInterior()
        {
            foreach (var light in emitters)
                if (light != null)
                {
                    light.enabled = false;
                    Release(light.gameObject);
                }
            emitters.Clear();
            foreach (var material in diffusers) Release(material);
            diffusers.Clear();
        }

        private static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        public void OnDestroy()
        {
            ReleaseInterior();
            foreach (var material in windowMaterials) Release(material);
            windowMaterials.Clear();
            windows.Clear();
        }
    }
}

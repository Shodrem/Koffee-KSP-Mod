using System;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Apply a part-local albedo without PartLoader.ReplaceTextures walking
    // unrelated shader slots that may legitimately have no assigned texture.
    public sealed class ModuleKoffeeModelTexture : PartModule
    {
        [KSPField] public string rendererName = "";
        [KSPField] public string textureURL = "";
        [NonSerialized] private Material? instanceMaterial;

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            Apply();
        }
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            Apply();
        }
        private void Apply()
        {
            try
            {
                if (part == null || rendererName.Length == 0 || textureURL.Length == 0) return;
                var renderer = part.FindModelTransform(rendererName)?.GetComponent<Renderer>();
                var texture = GameDatabase.Instance.GetTexture(textureURL, false);
                var material = renderer == null ? null : renderer.sharedMaterial;
                if (renderer == null || material == null || texture == null || !material.HasProperty("_MainTex"))
                {
                    Debug.LogError("[Koffee] Model texture unavailable: " + rendererName + " / " + textureURL);
                    return;
                }
                if (material.GetTexture("_MainTex") == texture) return;
                // Never modify the normal Manager's shared model material.
                var replacement = new Material(material) { name = material.name + " (Koffee texture)" };
                replacement.SetTexture("_MainTex", texture);
                renderer.sharedMaterial = replacement;
                Release();
                instanceMaterial = replacement;
            }
            catch (Exception error)
            {
                // A cosmetic override must not prevent the part from compiling.
                Debug.LogError("[Koffee] Model texture override failed: " + error);
            }
        }
        private void Release()
        {
            if (instanceMaterial == null) return;
            if (Application.isPlaying) Destroy(instanceMaterial);
            else DestroyImmediate(instanceMaterial);
            instanceMaterial = null;
        }
        public void OnDestroy() => Release();
    }
}

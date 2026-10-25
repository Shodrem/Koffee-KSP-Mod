using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShoAerospace.Koffee
{
    // Owns only our cameras/RT. Stock cameras are read-only templates; never cloned GameObjects.
    // Optional Parallax access uses reflection; no visual-mod assembly dependency.
    internal sealed class TelescopeStockRenderer : IDisposable
    {
        private sealed class Layer
        {
            public Camera Source = null!;
            public Camera Camera = null!;
            public bool Scaled;
            public bool Galaxy;
        }
        private sealed class BodyFade
        {
            public ScaledSpaceFader Fader = null!;
            public Renderer Renderer = null!;
            public MaterialPropertyBlock Original = new MaterialPropertyBlock();
            public MaterialPropertyBlock Temporary = new MaterialPropertyBlock();
            public bool Enabled;
            public int Opacity;
        }
        private readonly List<Layer> layers = new List<Layer>();
        private readonly List<BodyFade> fades = new List<BodyFade>();
        private TelescopeParallaxResources? parallax;
        private CelestialBody? reportedResourceTarget;
        private float reportedFieldOfView = float.NaN;
        public RenderTexture Image { get; private set; } = null!;

        public TelescopeStockRenderer(bool parallaxResourceExperiment = false)
        {
            try
            {
                parallax = TelescopeParallaxResources.TryCreate(parallaxResourceExperiment);
                Image = new RenderTexture(512, 512, 24) { name = "Koffee STS25 image" };
                if (!Image.Create()) throw new InvalidOperationException("RenderTexture creation failed");
                // Camera 01 is the far local camera on split-camera platforms; 00 is near/unified.
                var sources = Camera.allCameras;
                foreach (string name in new[] { "GalaxyCamera", "Camera ScaledSpace", "Camera 01", "Camera 00" })
                {
                    Camera? source = Array.Find(sources, c => c.name == name);
                    if (source == null)
                    {
                        if (name == "Camera 01") continue;
                        throw new InvalidOperationException("Missing stock camera: " + name);
                    }
                    var obj = new GameObject("Koffee STS25 " + name);
                    var camera = obj.AddComponent<Camera>();
                    camera.enabled = false;
                    layers.Add(new Layer { Source = source, Camera = camera,
                        Galaxy = name == "GalaxyCamera", Scaled = name == "Camera ScaledSpace" });
                }
                foreach (var body in FlightGlobals.Bodies)
                {
                    if (body.scaledBody == null) continue;
                    foreach (var fader in body.scaledBody.GetComponentsInChildren<ScaledSpaceFader>(true))
                    {
                        if (fader.r == null || string.IsNullOrEmpty(fader.floatName)) continue;
                        fades.Add(new BodyFade { Fader = fader, Renderer = fader.r,
                            Opacity = Shader.PropertyToID(fader.floatName) });
                    }
                }
            }
            catch { Dispose(); throw; }
        }

        public bool Render(Transform optical, float fieldOfView, CelestialBody? resourceTarget, CelestialBody? companion = null)
        {
            if (resourceTarget == null) reportedResourceTarget = null;
            if (parallax != null && !parallax.Prepare(resourceTarget, resourceTarget == null ? null : companion)) return false;
            var previous = RenderTexture.active;
            try
            {
                // Clear explicitly so even a missing/destroyed source cannot leave an old frame.
                RenderTexture.active = Image;
                GL.Clear(true, true, Color.black);
                foreach (var layer in layers)
                {
                    if (layer.Source == null) throw new InvalidOperationException("Stock camera unloaded");
                    var camera = layer.Camera;
                    camera.CopyFrom(layer.Source);
                    camera.enabled = false;
                    camera.targetTexture = Image;
                    camera.rect = new Rect(0, 0, 1, 1);
                    camera.aspect = 1;
                    // The galaxy is a finite-resolution background; magnifying it only blurs texels.
                    // Keep its decorative scale while bodies and local geometry retain the optical FOV.
                    camera.fieldOfView = layer.Galaxy ? TelescopeOptics.MaxFieldOfView :
                        TelescopeOptics.ClampFieldOfView(fieldOfView);
                    camera.ResetProjectionMatrix();
                    camera.transform.rotation = optical.rotation;
                    camera.transform.position = layer.Galaxy ? layer.Source.transform.position :
                        layer.Scaled ? (Vector3)ScaledSpace.LocalToScaledSpace((Vector3d)optical.position) : optical.position;
                    camera.clearFlags = layer.Galaxy ? CameraClearFlags.Color : CameraClearFlags.Depth;
                    camera.backgroundColor = Color.black;
                    if (layer.Scaled)
                    {
                        // Do not paint the galaxy again over its already rendered image.
                        camera.cullingMask &= ~layers[0].Source.cullingMask;
                        camera.nearClipPlane = 0.001f;
                        camera.farClipPlane = Math.Max(layer.Source.farClipPlane, 3e8f);
                        if (parallax != null && resourceTarget != null &&
                            (resourceTarget != reportedResourceTarget || camera.fieldOfView != reportedFieldOfView))
                        {
                            reportedResourceTarget = resourceTarget;
                            reportedFieldOfView = camera.fieldOfView;
                            Debug.Log("[Koffee STS25] Parallax cameras: target=" + resourceTarget.bodyName +
                                " mainFov=" + layer.Source.fieldOfView + " telescopeFov=" + camera.fieldOfView +
                                " mainHeight=" + Screen.height + " telescopeHeight=" + camera.pixelHeight +
                                " mainBuffers=" + layer.Source.commandBufferCount + " telescopeBuffers=" + camera.commandBufferCount);
                        }
                        RenderScaled(camera);
                    }
                    else camera.Render();
                }
            }
            finally { RenderTexture.active = previous; }
            return true;
        }

        private void RenderScaled(Camera camera)
        {
            int applied = 0;
            try
            {
                foreach (var fade in fades)
                {
                    var renderer = fade.Renderer;
                    if (renderer == null) { applied++; continue; }
                    fade.Enabled = renderer.enabled;
                    renderer.GetPropertyBlock(fade.Original);
                    renderer.GetPropertyBlock(fade.Temporary);
                    applied++;
                    // Stock Update culls small angular bodies using the MAIN camera FOV.
                    // Preserve the main body's PQS transition; local layers provide its terrain.
                    if (fade.Fader.celestialBody == FlightGlobals.currentMainBody) continue;
                    renderer.enabled = true;
                    fade.Temporary.SetFloat(fade.Opacity, 1);
                    renderer.SetPropertyBlock(fade.Temporary);
                }
                camera.Render();
            }
            finally
            {
                for (int i = 0; i < applied; i++)
                {
                    var fade = fades[i];
                    if (fade.Renderer == null) continue;
                    fade.Renderer.SetPropertyBlock(fade.Original);
                    fade.Renderer.enabled = fade.Enabled;
                }
            }
        }

        public void Dispose()
        {
            parallax?.Dispose();
            parallax = null;
            foreach (var layer in layers)
                if (layer.Camera != null)
                {
                    layer.Camera.enabled = false;
                    layer.Camera.targetTexture = null;
                    UnityEngine.Object.Destroy(layer.Camera.gameObject);
                }
            layers.Clear();
            fades.Clear();
            if (Image != null)
            {
                Image.Release();
                UnityEngine.Object.Destroy(Image);
                Image = null!;
            }
        }
    }
}

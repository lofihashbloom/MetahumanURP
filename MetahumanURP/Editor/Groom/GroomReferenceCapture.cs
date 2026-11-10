using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MetaHuman.Editor.Groom
{
    /// <summary>Static reference capture with genuine spatial supersampling and linear area resolve.</summary>
    public static class GroomReferenceCapture
    {
        /// <summary>
        /// Render at spatialScale times the requested dimensions, then average each square of samples.
        /// The caller owns the returned linear RGBAFloat texture. Camera pose, post processing and
        /// renderer selection are used as configured. Call outside an active render callback.
        /// This is an Editor capture utility, not temporal antialiasing or a live quality setting.
        /// </summary>
        public static Texture2D Capture(Camera camera, int width, int height, int spatialScale = 2)
        {
            if (!camera) throw new ArgumentNullException(nameof(camera));
            if (width < 1 || height < 1 || (spatialScale != 1 && spatialScale != 2 && spatialScale != 4) ||
                (long)width * spatialScale > Math.Min(SystemInfo.maxTextureSize, 8192) ||
                (long)height * spatialScale > Math.Min(SystemInfo.maxTextureSize, 8192))
                throw new ArgumentOutOfRangeException(nameof(spatialScale), "Use scale 1, 2 or 4 and render dimensions no larger than 8192.");
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp))
                throw new InvalidOperationException("Groom reference capture requires URP.");
            if (!Mathf.Approximately(urp.renderScale, 1))
                throw new NotSupportedException("Use URP render scale 1 for exact spatial reference dimensions.");
            var additional = camera.GetComponent<UniversalAdditionalCameraData>();
            if (camera.stereoEnabled || (additional && additional.renderType != CameraRenderType.Base))
                throw new NotSupportedException("Use a base, mono camera for the groom reference capture.");
            if (camera.allowDynamicResolution || (additional && additional.antialiasing == AntialiasingMode.TemporalAntiAliasing))
                throw new NotSupportedException("Disable dynamic resolution and TAA for a deterministic spatial reference capture.");

            int rw = width * spatialScale, rh = height * spatialScale;
            var descriptor = new RenderTextureDescriptor(rw, rh, RenderTextureFormat.ARGBHalf, 24)
            { sRGB = false, msaaSamples = camera.allowMSAA ? urp.msaaSampleCount : 1 };
            descriptor.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
            RenderTexture target = null, resolved = null;
            Texture2D readback = null;
            Texture2D output = null;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                target = new RenderTexture(descriptor) { name = "MetaHuman reference supersamples" };
                resolved = new RenderTexture(rw, rh, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                readback = new Texture2D(rw, rh, TextureFormat.RGBAFloat, false, true);
                if (!target.Create() || !resolved.Create()) throw new InvalidOperationException("Unable to allocate reference targets.");
                camera.targetTexture = target;
                camera.Render();
                camera.Render();
                if (descriptor.msaaSamples > 1) target.ResolveAntiAliasedSurface(resolved);
                else Graphics.Blit(target, resolved);
                RenderTexture.active = resolved;
                readback.ReadPixels(new Rect(0, 0, rw, rh), 0, 0);
                readback.Apply(false, false);
                output = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
                { name = "MetaHuman linear area-resolved reference" };
                var source = readback.GetRawTextureData<Color>();
                var pixels = output.GetRawTextureData<Color>();
                double divisor = spatialScale * spatialScale;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        double r = 0, g = 0, b = 0, a = 0;
                        for (int sy = 0; sy < spatialScale; sy++)
                            for (int sx = 0; sx < spatialScale; sx++)
                            {
                                var c = source[(y * spatialScale + sy) * rw + x * spatialScale + sx];
                                r += c.r; g += c.g; b += c.b; a += c.a;
                            }
                        pixels[y * width + x] = new Color((float)(r / divisor), (float)(g / divisor),
                            (float)(b / divisor), (float)(a / divisor));
                    }
                output.Apply(false, false);
                return output;
            }
            catch
            {
                if (output) UnityEngine.Object.DestroyImmediate(output);
                throw;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (resolved) { resolved.Release(); UnityEngine.Object.DestroyImmediate(resolved); }
                if (readback) UnityEngine.Object.DestroyImmediate(readback);
            }
        }
    }
}

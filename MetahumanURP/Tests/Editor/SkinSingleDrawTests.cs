using System.Linq;
using MetaHuman.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace MetaHuman.Tests
{
    /// <summary>
    /// GPU checks of the single-draw skin diffusion path against the two-draw path (forward pass plus diffuse-only
    /// extraction draw): same image, the foreground untouched, and the forward pass back for cameras without it.
    /// </summary>
    public sealed class SkinSingleDrawTests
    {
        GameObject root;
        HostRendererIsolation isolation;
        Camera camera;
        SkinDiffusionFeature feature;
        bool wasActive, wasSingle, wasAdditive, wasRadial;
        Material skin, unlit;
        Texture2D pattern;

        [SetUp] public void Setup()
        {
            isolation = new HostRendererIsolation();
            root = new GameObject("MetaHuman skin single-draw test");
            camera = new GameObject("Skin test camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.transform.position = new Vector3(0, 0, -0.2f);
            camera.orthographic = true; camera.orthographicSize = 0.025f;
            camera.nearClipPlane = 0.01f; camera.farClipPlane = 1;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.allowHDR = true; camera.allowMSAA = false; camera.cullingMask = 1 << 30;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.requiresColorTexture = true;
            var light = new GameObject("Skin test light").AddComponent<Light>();
            light.transform.SetParent(root.transform);
            light.transform.rotation = Quaternion.Euler(20, 30, 0);
            light.type = LightType.Directional; light.intensity = 1; light.cullingMask = 1 << 30;

            feature = CameraFeature();
            if (!feature) return;
            wasActive = feature.isActive; wasSingle = feature.settings.singleDraw;
            wasAdditive = feature.settings.additiveComposite; wasRadial = feature.settings.normalizedDiffusion;
            if (feature.settings.layers.value != -1) return;

            pattern = new Texture2D(64, 64, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            var colors = new Color[4096];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = ((i % 64 / 4 + i / 64 / 4) % 2 == 0) ? new Color(0.6f, 0.2f, 0.1f) : new Color(0.08f, 0.025f, 0.015f);
            pattern.SetPixels(colors); pattern.Apply();
            skin = new Material(Shader.Find("MetaHuman/URP/Skin"));
            skin.SetTexture("_BaseMap", pattern); skin.SetFloat("_DiffusionAmount", 1);
            skin.SetFloat("_Scatter", 0.5f); skin.SetFloat("_Fuzz", 0.5f);
            unlit = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            unlit.SetColor("_BaseColor", new Color(0, 0, 0.8f, 1));
            // A lit, specular sphere over a diffusing quad, half covered by an opaque foreground strip.
            Place(PrimitiveType.Quad, skin, Vector3.zero, Vector3.one * 0.08f);
            Place(PrimitiveType.Sphere, skin, new Vector3(-0.008f, 0, -0.02f), Vector3.one * 0.03f);
            Place(PrimitiveType.Quad, unlit, new Vector3(0.017f, 0, -0.05f), new Vector3(0.03f, 0.08f, 1));
        }

        [TearDown] public void Teardown()
        {
            isolation?.Dispose(); isolation = null;
            if (feature)
            {
                feature.settings.singleDraw = wasSingle; feature.settings.additiveComposite = wasAdditive;
                feature.settings.normalizedDiffusion = wasRadial; feature.SetActive(wasActive);
            }
            if (root) Object.DestroyImmediate(root);
            if (skin) Object.DestroyImmediate(skin);
            if (unlit) Object.DestroyImmediate(unlit);
            if (pattern) Object.DestroyImmediate(pattern);
        }

        SkinDiffusionFeature CameraFeature()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!pipeline) return null;
            var serialized = new SerializedObject(pipeline);
            int index = serialized.FindProperty("m_DefaultRendererIndex").intValue;
            camera.GetUniversalAdditionalCameraData().SetRenderer(index);
            var data = serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(index).objectReferenceValue as UniversalRendererData;
            return data ? data.rendererFeatures.OfType<SkinDiffusionFeature>().FirstOrDefault() : null;
        }

        void RequireFeature()
        {
            if (!feature) Assert.Ignore("Install skin diffusion on the host renderer for the GPU integration test.");
            if (feature.settings.layers.value != -1) Assert.Ignore("The single draw needs the feature's layers set to Everything.");
        }

        void Place(PrimitiveType type, Material material, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(root.transform);
            go.layer = 30; go.transform.position = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        Color[] Render(int samples)
        {
            var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = samples };
            var active = RenderTexture.active;
            var image = new Texture2D(256, 256, TextureFormat.RGBAFloat, false, true);
            try
            {
                target.Create(); camera.allowMSAA = samples > 1; camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); image.Apply();
                return image.GetPixels();
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = active;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
            }
        }

        static float MaxDifference(Color[] a, Color[] b, out float peak)
        {
            float max = 0; peak = 0;
            for (int i = 0; i < a.Length; i++)
            {
                peak = Mathf.Max(peak, a[i].r, a[i].g, a[i].b);
                max = Mathf.Max(max, Mathf.Abs(a[i].r - b[i].r), Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b));
            }
            return max;
        }

        [TestCase(false, false, 1)] [TestCase(true, false, 1)] [TestCase(false, true, 1)] [TestCase(true, true, 1)]
        [TestCase(true, false, 4)] [TestCase(true, true, 4)]
        public void SingleDrawMatchesTwoDraws(bool radial, bool desktop, int samples)
        {
            RequireFeature();
            skin.SetFloat("_DesktopSkinLighting", desktop ? 1 : 0);
            feature.SetActive(true);
            feature.settings.normalizedDiffusion = radial;
            feature.settings.additiveComposite = true;
            feature.settings.singleDraw = false;
            var twoDraws = Render(samples);
            Assert.That(feature.lastRecordedSingleDraw, Is.False);
            feature.settings.singleDraw = true;
            var single = Render(samples);
            Assert.That(feature.lastRecordedSingleDraw, Is.True, "The single draw must be recorded for this camera.");
            feature.SetActive(false);
            var plain = Render(samples);

            float diffusion = MaxDifference(plain, twoDraws, out float peak);
            Assert.That(peak, Is.GreaterThan(0.05f), "The fixture must be lit.");
            Assert.That(diffusion, Is.GreaterThan(0.005f), "Diffusion must change the image for the comparison to mean anything.");
            // Both paths take the diffuse from the same light loop (MHSkinLighting): equal up to half-float rounding.
            float tolerance = Mathf.Max(0.002f, peak * 0.005f);
            float difference = MaxDifference(twoDraws, single, out _);
            if (samples == 1)
                Assert.That(difference, Is.LessThan(tolerance), $"Single draw differs from the two-draw path by {difference} (peak {peak}).");
            else
            {
                // With MSAA a few silhouette pixels differ: the two-draw extraction tests its samples against the depth the
                // lit pass wrote, the single draw writes both at once, so partly covered pixels can keep different samples.
                // Measured: 3 of 65536 pixels, 1% of the peak. Anything broader or larger is a real difference.
                int over = 0;
                for (int i = 0; i < twoDraws.Length; i++)
                    if (Mathf.Max(Mathf.Abs(twoDraws[i].r - single[i].r), Mathf.Abs(twoDraws[i].g - single[i].g), Mathf.Abs(twoDraws[i].b - single[i].b)) > tolerance) over++;
                Assert.That(over, Is.LessThan(twoDraws.Length / 2000), $"{over} pixels differ between the single draw and the two-draw path.");
                Assert.That(difference, Is.LessThan(peak * 0.03f), $"Single draw differs from the two-draw path by {difference} at an edge (peak {peak}).");
            }
        }

        [Test] public void AdditiveCompositeMatchesCopyComposite()
        {
            RequireFeature();
            feature.SetActive(true);
            feature.settings.singleDraw = true;
            feature.settings.additiveComposite = false;
            var copy = Render(1);
            feature.settings.additiveComposite = true;
            var additive = Render(1);
            float difference = MaxDifference(copy, additive, out float peak);
            Assert.That(difference, Is.LessThan(Mathf.Max(0.002f, peak * 0.005f)), $"Additive composite differs from the copy by {difference} (peak {peak}).");
        }

        [Test] public void ForwardPassReturnsWithoutTheFeature()
        {
            RequireFeature();
            feature.SetActive(true); feature.settings.singleDraw = true;
            Render(1);
            Assert.That(feature.lastRecordedSingleDraw, Is.True);
            feature.SetActive(false);
            var image = Render(1);
            float peak = image.Max(c => Mathf.Max(c.r, c.g, c.b));
            Assert.That(peak, Is.GreaterThan(0.05f), "Without the feature, skin must draw in its forward pass.");
        }
    }
}

using System;
using System.Linq;
using MetaHuman.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MetaHuman.Tests
{
    public sealed class RenderingTests
    {
        GameObject root;
        Camera camera;

        T CameraFeature<T>() where T : UnityEngine.Rendering.Universal.ScriptableRendererFeature
        {
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!pipeline) return null;
            var serialized = new SerializedObject(pipeline);
            int index = serialized.FindProperty("m_DefaultRendererIndex").intValue;
            camera.GetUniversalAdditionalCameraData().SetRenderer(index);
            var data = serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(index).objectReferenceValue as UniversalRendererData;
            return data ? data.rendererFeatures.OfType<T>().FirstOrDefault() : null;
        }

        [SetUp] public void Setup()
        {
            // Use the runner's temporary scene; keep every fixture object below
            // one root and on an exclusive camera layer for deterministic cleanup.
            root = new GameObject("MetaHuman GPU test");
            camera = new GameObject("Rendering test camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.transform.position = new Vector3(0, 0, -0.2f);
            camera.orthographic = true; camera.orthographicSize = 0.025f;
            camera.nearClipPlane = 0.01f; camera.farClipPlane = 1;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.allowHDR = true; camera.allowMSAA = false; camera.cullingMask = 1 << 30;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            // Keep URP's HDR intermediate format identical with the feature on/off.
            // Otherwise the baseline may render directly into the half-float target
            // while the feature uses URP's packed HDR format, confounding the test.
            cameraData.requiresColorTexture = true;
            var light = new GameObject("Rendering test light").AddComponent<Light>();
            light.transform.SetParent(root.transform);
            light.type = LightType.Directional; light.intensity = 1; light.cullingMask = 1 << 30;
        }

        [TearDown] public void Teardown()
        {
            if (root) Object.DestroyImmediate(root);
        }

        GameObject Quad(Material material, Vector3 position, Vector3 scale)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.SetParent(root.transform);
            quad.layer = 30; quad.transform.position = position; quad.transform.localScale = scale;
            quad.GetComponent<Renderer>().sharedMaterial = material;
            return quad;
        }

        Color[] Render(int samples = 1)
        {
            var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            target.antiAliasing = samples;
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

        [TestCase(false, false)] [TestCase(true, false)] [TestCase(false, true)] [TestCase(true, true)]
        public void DiffusionChangesSkinLightingButPreservesForegroundOccluder(bool radial, bool desktop)
        {
            var feature = CameraFeature<SkinDiffusionFeature>();
            if (!feature) Assert.Ignore("Install the diffusion feature on the host renderer for the GPU integration test.");
            bool enabled = feature.isActive;
            bool previousRadial = feature.settings.normalizedDiffusion;
            var previousDistances = feature.settings.scatterDistanceMillimetres;
            feature.settings.normalizedDiffusion = radial;
            feature.settings.scatterDistanceMillimetres = new Vector3(.3f, .15f, .08f);
            float preservation = feature.settings.albedoPreservation; feature.settings.albedoPreservation = 0;
            var skin = new Material(Shader.Find("MetaHuman/URP/Skin"));
            skin.SetFloat("_DesktopSkinLighting", desktop ? 1 : 0);
            var unlit = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var pattern = new Texture2D(64, 64, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            try
            {
                var colors = new Color[4096];
                for (int i = 0; i < colors.Length; i++) colors[i] = ((i % 64 / 4 + i / 64 / 4) % 2 == 0) ? new Color(0.6f, 0.2f, 0.1f) : new Color(0.08f, 0.025f, 0.015f);
                pattern.SetPixels(colors); pattern.Apply();
                skin.SetTexture("_BaseMap", pattern); skin.SetFloat("_DiffusionAmount", 1);
                skin.SetFloat("_Scatter", 0); skin.SetFloat("_Fuzz", 0);
                var skinQuad = Quad(skin, Vector3.zero, Vector3.one * 0.08f);
                unlit.SetColor("_BaseColor", new Color(0, 0, 0.8f, 1));
                var occluder = Quad(unlit, new Vector3(0.017f, 0, -0.01f), new Vector3(0.03f, 0.08f, 1));
                feature.SetActive(false); var before = Render();
                feature.SetActive(true); var after = Render();
                float maxSkinChange = 0, maxOccluderChange = 0; int occluderPixels = 0;
                for (int y = 8; y < 248; y++) for (int x = 8; x < 248; x++)
                {
                    int i = y * 256 + x;
                    float change = Mathf.Max(Mathf.Abs(before[i].r - after[i].r), Mathf.Abs(before[i].g - after[i].g), Mathf.Abs(before[i].b - after[i].b));
                    // Material color properties are decoded from sRGB in a linear project.
                    if (before[i].b > 0.1f && before[i].r < 0.001f && before[i].g < 0.001f) { maxOccluderChange = Mathf.Max(maxOccluderChange, change); occluderPixels++; }
                    else if (x < 100) maxSkinChange = Mathf.Max(maxSkinChange, change);
                }
                Assert.That(occluderPixels, Is.GreaterThan(1000), "The opaque blue foreground must be visible.");
                Assert.That(maxSkinChange, Is.GreaterThan(0.005f), "A real diffuse-lighting change must reach the camera color.");
                Assert.That(maxOccluderChange, Is.LessThan(0.001f), "Depth-tested skin extraction must not alter the foreground.");
                // With zero albedo, the sphere contains only a spatially varying
                // specular highlight. Diffusion must leave it entirely unchanged.
                skinQuad.SetActive(false); skin.SetTexture("_BaseMap", Texture2D.blackTexture);
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.SetParent(root.transform); sphere.layer = 30;
                sphere.transform.localScale = Vector3.one * 0.04f;
                sphere.GetComponent<Renderer>().sharedMaterial = skin;
                feature.SetActive(false); var specBefore = Render();
                feature.SetActive(true); var specAfter = Render();
                float specChange = 0, specPeak = 0;
                for (int y = 40; y < 216; y++) for (int x = 40; x < 120; x++)
                {
                    int i = y * 256 + x;
                    specPeak = Mathf.Max(specPeak, specBefore[i].r);
                    specChange = Mathf.Max(specChange, Mathf.Abs(specBefore[i].r - specAfter[i].r),
                        Mathf.Abs(specBefore[i].g - specAfter[i].g), Mathf.Abs(specBefore[i].b - specAfter[i].b));
                }
                Assert.That(specPeak, Is.GreaterThan(0.005f), "The specular highlight must be visible.");
                Assert.That(specChange, Is.LessThan(0.001f), "Specular-only skin must bypass diffusion.");
                // A uniform lit surface has nothing to diffuse, including its
                // partially covered MSAA silhouette. This catches resolve halos.
                sphere.SetActive(false); occluder.SetActive(false); skinQuad.SetActive(true);
                skinQuad.transform.localScale = Vector3.one * 0.0301f;
                skin.SetTexture("_BaseMap", Texture2D.whiteTexture);
                skin.SetFloat("_Specular", 0); skin.SetFloat("_SecondaryWeight", 0);
                feature.SetActive(false); var edgeBefore = Render(4);
                feature.SetActive(true); var edgeAfter = Render(4);
                float edgeChange = 0, referencePeak = 0; int peakPixel = 0;
                for (int i = 0; i < edgeBefore.Length; i++)
                {
                    float delta = Mathf.Max(Mathf.Abs(edgeBefore[i].r - edgeAfter[i].r), Mathf.Abs(edgeBefore[i].g - edgeAfter[i].g), Mathf.Abs(edgeBefore[i].b - edgeAfter[i].b));
                    referencePeak = Mathf.Max(referencePeak, edgeBefore[i].r, edgeBefore[i].g, edgeBefore[i].b);
                    if (delta > edgeChange) { edgeChange = delta; peakPixel = i; }
                }
                // Packed HDR/half-float intermediates introduce small rounding
                // differences. Bound error to 0.5% of the HDR reference range;
                // the pre-coverage implementation exceeds this by over 4x.
                float tolerance = Mathf.Max(0.002f, referencePeak * 0.005f);
                Assert.That(edgeChange, Is.LessThan(tolerance), $"MSAA silhouette energy loss exceeds HDR precision tolerance. Peak ({peakPixel % 256},{peakPixel / 256}): {edgeBefore[peakPixel]} -> {edgeAfter[peakPixel]}");
            }
            finally { feature.settings.normalizedDiffusion = previousRadial; feature.settings.scatterDistanceMillimetres = previousDistances; feature.settings.albedoPreservation = preservation; feature.SetActive(enabled); Object.DestroyImmediate(pattern); Object.DestroyImmediate(skin); Object.DestroyImmediate(unlit); }
        }

        [Test] public void ReflectanceAwareDiffusionKeepsAlbedoDetailButSpreadsLightingVariation()
        {
            var feature = CameraFeature<SkinDiffusionFeature>();
            if (!feature) Assert.Ignore("Install skin diffusion on the host renderer.");
            bool enabled = feature.isActive;
            float preservation = feature.settings.albedoPreservation;
            var skin = new Material(Shader.Find("MetaHuman/URP/Skin"));
            var pattern = new Texture2D(64, 64, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            var normals = new Texture2D(64, 64, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            try
            {
                feature.settings.albedoPreservation = 1;
                var colors = new Color[4096]; var normalColors = new Color[4096];
                for (int i = 0; i < colors.Length; i++)
                {
                    bool light = ((i % 64 / 4 + i / 64 / 4) % 2 == 0);
                    colors[i] = light ? new Color(.6f,.2f,.1f) : new Color(.08f,.04f,.03f);
                    normalColors[i] = new Color(light ? .825f : .175f,.5f,.88f,1);
                }
                pattern.SetPixels(colors); pattern.Apply(); normals.SetPixels(normalColors); normals.Apply();
                skin.SetTexture("_BaseMap", pattern); skin.SetFloat("_Scatter", 0); skin.SetFloat("_Fuzz", 0);
                skin.SetFloat("_Specular", 0); skin.SetFloat("_SecondaryWeight", 0); skin.SetFloat("_DiffusionAmount", 1);
                Quad(skin, Vector3.zero, Vector3.one * .08f);
                feature.SetActive(false); var original = Render(); feature.SetActive(true); var preserved = Render();
                float error = 0, contrast = 0;
                for (int y = 30; y < 226; y++) for (int x = 30; x < 226; x++)
                {
                    int i = y * 256 + x;
                    error = Mathf.Max(error, Mathf.Abs(original[i].r-preserved[i].r));
                    contrast = Mathf.Max(contrast, original[i].r);
                }
                Assert.That(contrast, Is.GreaterThan(.1f), "The reflectance fixture must be visible.");
                Assert.That(error, Is.LessThan(.005f), "Uniform irradiance must preserve sharp albedo detail.");
                skin.SetTexture("_BaseMap", Texture2D.whiteTexture); skin.SetTexture("_BumpMap", normals);
                root.GetComponentInChildren<Light>().transform.rotation = Quaternion.Euler(0,40,0);
                feature.SetActive(false); var lit = Render(); feature.SetActive(true); var diffused = Render();
                float lightingChange = 0;
                for (int i = 0; i < lit.Length; i++) lightingChange = Mathf.Max(lightingChange, Mathf.Abs(lit[i].r-diffused[i].r));
                Assert.That(lightingChange, Is.GreaterThan(.005f), "Spatial lighting variation must still diffuse.");
            }
            finally
            {
                feature.settings.albedoPreservation = preservation; feature.SetActive(enabled);
                Object.DestroyImmediate(skin); Object.DestroyImmediate(pattern); Object.DestroyImmediate(normals);
            }
        }

        [TestCase(0, 1), TestCase(0, 4), TestCase(1, 1), TestCase(1, 4)]
        public void OverlappingSubpixelGroomStrandsAccumulateCoverageAndRespectForegroundDepth(int mode, int samples)
        {
            var feature = CameraFeature<MetaHuman.Rendering.GroomTransparencyFeature>();
            if (mode == 1 && !feature) Assert.Ignore("Install continuous groom transparency on the host renderer for this integration test.");
            bool featureEnabled = feature && feature.isActive;
            if (feature) feature.SetActive(true);
            var background = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var foreground = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var hair = new Material(Shader.Find("MetaHuman/URP/GroomStrands"));
            MetaHuman.Editor.Groom.GroomRibbonBaker.Result single = null, layered = null;
            try
            {
                background.SetColor("_BaseColor", Color.white); Quad(background, Vector3.zero, Vector3.one * 0.08f);
                foreground.SetColor("_BaseColor", Color.blue); Quad(foreground, new Vector3(0.017f, 0, -0.05f), new Vector3(0.015f, 0.08f, 1));
                hair.SetColor("_BaseColor", Color.black); hair.SetFloat("_Specular", 0); hair.SetFloat("_CoverageMode", mode);
                var baseline = Render(samples);
                MetaHuman.Editor.Groom.GroomCurveData Source(int layers)
                {
                    int count = 100 * layers;
                    var data = new MetaHuman.Editor.Groom.GroomCurveData { strands = new MetaHuman.Editor.Groom.GroomCurveData.Strand[count], positions = new Vector3[count * 2], widths = new float[count * 2] };
                    for (int i = 0; i < count; i++)
                    {
                        data.strands[i] = new MetaHuman.Editor.Groom.GroomCurveData.Strand { first = i * 2, count = 2 };
                        float x = -0.015f + (i % 100) * 0.0003f, z = -0.005f - (i / 100) * 0.0001f;
                        data.positions[i * 2] = new Vector3(x, -0.02f, z); data.positions[i * 2 + 1] = new Vector3(x, 0.02f, z);
                    }
                    return data;
                }
                var settings = new[] { new MetaHuman.Editor.Groom.GroomRibbonBaker.GroupSettings { widthCentimetres = 0.005f, rootScale = 1, tipScale = 1 } };
                single = MetaHuman.Editor.Groom.GroomRibbonBaker.Build(Source(1), Matrix4x4.identity, settings);
                layered = MetaHuman.Editor.Groom.GroomRibbonBaker.Build(Source(16), Matrix4x4.identity, settings);
                var go = new GameObject("Subpixel groom layers"); go.transform.SetParent(root.transform); go.layer = 30;
                var filter = go.AddComponent<MeshFilter>(); go.AddComponent<MeshRenderer>().sharedMaterial = hair;
                filter.sharedMesh = single.meshes[0]; var one = Render(samples);
                filter.sharedMesh = layered.meshes[0]; var many = Render(samples);
                float oneLoss = 0, manyLoss = 0, foregroundChange = 0;
                for (int y = 32; y < 224; y++) for (int x = 54; x < 160; x++)
                {
                    int i = y * 256 + x;
                    oneLoss += baseline[i].r - one[i].r; manyLoss += baseline[i].r - many[i].r;
                }
                for (int y = 32; y < 224; y++) for (int x = 190; x < 240; x++)
                {
                    int i = y * 256 + x;
                    foregroundChange = Mathf.Max(foregroundChange, Mathf.Abs(baseline[i].b - many[i].b), Mathf.Abs(baseline[i].r - many[i].r));
                }
                if (mode == 1)
                {
                    float transmissionError = 0, verticalNoise = 0;
                    int compared = 0;
                    for (int y = 40; y < 216; y++) for (int x = 54; x < 160; x++)
                    {
                        int i = y * 256 + x;
                        float prediction = baseline[i].r * Mathf.Pow(Mathf.Clamp01(one[i].r / Mathf.Max(baseline[i].r, 0.0001f)), 16);
                        transmissionError += Mathf.Abs(prediction - many[i].r);
                        verticalNoise += Mathf.Abs(many[i].r - many[i + 256].r);
                        compared++;
                    }
                    Assert.That(transmissionError / compared, Is.LessThan(0.015f), "Optical depths must accumulate the product of layer transmission.");
                    Assert.That(verticalNoise / compared, Is.LessThan(0.003f), "A constant straight strand must not acquire stochastic pixel noise.");
                }
                Assert.That(oneLoss, Is.GreaterThan(100), "Subpixel fibres must reach the camera.");
                Assert.That(manyLoss, Is.GreaterThan(oneLoss * 1.8f), "Independent strand layers must fill additional covered samples.");
                Assert.That(foregroundChange, Is.LessThan(0.001f), "Hair behind the opaque foreground must stay occluded.");
            }
            finally
            {
                if (feature) feature.SetActive(featureEnabled);
                if (single != null) foreach (var mesh in single.meshes) Object.DestroyImmediate(mesh);
                if (layered != null) foreach (var mesh in layered.meshes) Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(background); Object.DestroyImmediate(foreground); Object.DestroyImmediate(hair);
            }
        }

        [TestCase(1), TestCase(4)]
        public void StrandFractionDropsStrandsButKeepsCoverage(int samples)
        {
            // 1600 sub-pixel black strands over white: drawing half of them, widened by 2, must keep the groom's
            // total coverage (transmission loss) close to full detail while changing which pixels are covered.
            var feature = CameraFeature<MetaHuman.Rendering.GroomTransparencyFeature>();
            if (!feature) Assert.Ignore("Install continuous groom transparency on the host renderer for this integration test.");
            bool featureEnabled = feature.isActive;
            feature.SetActive(true);
            var background = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var hair = new Material(Shader.Find("MetaHuman/URP/GroomStrands"));
            MetaHuman.Editor.Groom.GroomRibbonBaker.Result groom = null;
            try
            {
                background.SetColor("_BaseColor", Color.white); Quad(background, Vector3.zero, Vector3.one * 0.08f);
                hair.SetColor("_BaseColor", Color.black); hair.SetFloat("_Specular", 0); hair.SetFloat("_CoverageMode", 1);
                var baseline = Render(samples);
                var data = new MetaHuman.Editor.Groom.GroomCurveData { strands = new MetaHuman.Editor.Groom.GroomCurveData.Strand[1600], positions = new Vector3[3200], widths = new float[3200] };
                for (int i = 0; i < 1600; i++)
                {
                    data.strands[i] = new MetaHuman.Editor.Groom.GroomCurveData.Strand { first = i * 2, count = 2 };
                    float x = -0.015f + (i % 100) * 0.0003f, z = -0.005f - (i / 100) * 0.0001f;
                    data.positions[i * 2] = new Vector3(x, -0.02f, z); data.positions[i * 2 + 1] = new Vector3(x, 0.02f, z);
                }
                var settings = new[] { new MetaHuman.Editor.Groom.GroomRibbonBaker.GroupSettings { widthCentimetres = 0.002f, rootScale = 1, tipScale = 1 } };
                groom = MetaHuman.Editor.Groom.GroomRibbonBaker.Build(data, Matrix4x4.identity, settings);
                foreach (var mesh in groom.meshes)
                {
                    var go = new GameObject("Strand LOD groom"); go.transform.SetParent(root.transform); go.layer = 30;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = hair;
                }
                hair.SetFloat("_StrandFraction", 1); var full = Render(samples);
                hair.SetFloat("_StrandFraction", .5f); var half = Render(samples);
                float fullLoss = 0, halfLoss = 0, changed = 0;
                for (int y = 40; y < 216; y++) for (int x = 54; x < 160; x++)
                {
                    int i = y * 256 + x;
                    fullLoss += baseline[i].r - full[i].r; halfLoss += baseline[i].r - half[i].r;
                    changed += Mathf.Abs(full[i].r - half[i].r);
                }
                Assert.That(fullLoss, Is.GreaterThan(100), "The fixture must cover the background.");
                Assert.That(halfLoss, Is.EqualTo(fullLoss).Within(fullLoss * .15f), "Half the strands, widened, must keep the coverage.");
                Assert.That(changed, Is.GreaterThan(fullLoss * .01f), "The fraction must change which strands are drawn.");
            }
            finally
            {
                feature.SetActive(featureEnabled);
                if (groom != null) foreach (var mesh in groom.meshes) Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(background); Object.DestroyImmediate(hair);
            }
        }

        [TestCase(1, 0), TestCase(4, 0), TestCase(8, 0), TestCase(1, 1), TestCase(4, 1), TestCase(8, 1), TestCase(1, 2), TestCase(4, 2), TestCase(8, 2)]
        public void NearestLayerShadesOnlyTheFrontStrandsAtEveryMsaaRate(int samples, int selection)
        {
            // selection: GroomTransparencyFeature.NearestSelection (Auto, Shader, DepthTest).
            // A wide green front layer fully covers an identical red layer behind it. Nearest-layer visibility
            // must shade only the front layer (the back layer contributes transmittance, not radiance), whether
            // selection runs per pixel (MSAA1) or through the pixel-rate SV_Coverage path (MSAA4/8).
            var feature = CameraFeature<MetaHuman.Rendering.GroomTransparencyFeature>();
            if (!feature) Assert.Ignore("Install continuous groom transparency on the host renderer for this integration test.");
            bool featureEnabled = feature.isActive;
            var previousMode = feature.settings.visibilityMode;
            var previousSelection = feature.settings.nearestSelection;
            feature.SetActive(true);
            feature.settings.nearestSelection = (MetaHuman.Rendering.GroomTransparencyFeature.NearestSelection)selection;
            feature.settings.visibilityMode = MetaHuman.Rendering.GroomTransparencyFeature.VisibilityMode.NearestLayer;
            Material Hair(Color color)
            {
                var m = new Material(Shader.Find("MetaHuman/URP/GroomStrands"));
                m.SetColor("_BaseColor", color); m.SetFloat("_Specular", 0); m.SetFloat("_CoverageMode", 1);
                return m;
            }
            var front = Hair(Color.green);
            var back = Hair(Color.red);
            MetaHuman.Editor.Groom.GroomRibbonBaker.Result frontMesh = null, backMesh = null;
            try
            {
                MetaHuman.Editor.Groom.GroomRibbonBaker.Result Layer(float z)
                {
                    var data = new MetaHuman.Editor.Groom.GroomCurveData { strands = new MetaHuman.Editor.Groom.GroomCurveData.Strand[60], positions = new Vector3[120], widths = new float[120] };
                    for (int i = 0; i < 60; i++)
                    {
                        data.strands[i] = new MetaHuman.Editor.Groom.GroomCurveData.Strand { first = i * 2, count = 2 };
                        float x = -0.015f + i * 0.0005f;
                        data.positions[i * 2] = new Vector3(x, -0.02f, z); data.positions[i * 2 + 1] = new Vector3(x, 0.02f, z);
                    }
                    var settings = new[] { new MetaHuman.Editor.Groom.GroomRibbonBaker.GroupSettings { widthCentimetres = 0.04f, rootScale = 1, tipScale = 1 } };
                    return MetaHuman.Editor.Groom.GroomRibbonBaker.Build(data, Matrix4x4.identity, settings);
                }
                frontMesh = Layer(-0.006f); backMesh = Layer(-0.004f);
                void Add(string name, Mesh mesh, Material material)
                {
                    var go = new GameObject(name); go.transform.SetParent(root.transform); go.layer = 30;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = material;
                }
                Add("Front green strands", frontMesh.meshes[0], front);
                Add("Back red strands", backMesh.meshes[0], back);
                var image = Render(samples);
                float red = 0, green = 0;
                for (int y = 40; y < 216; y++) for (int x = 54; x < 140; x++)
                {
                    var c = image[y * 256 + x];
                    Assert.That(float.IsFinite(c.r) && float.IsFinite(c.g) && float.IsFinite(c.b), "Composite must stay finite.");
                    red += c.r; green += c.g;
                }
                Assert.That(green, Is.GreaterThan(1), "The front strands must be shaded.");
                Assert.That(red, Is.LessThan(green * .02f), "The occluded back layer must not contribute radiance.");
                bool expectDepthTest = selection == 2 || (selection == 0 && samples > 1);
                Assert.That(feature.lastRecordedHardwareSelection, Is.EqualTo(expectDepthTest), "Recorded selection path.");
                // Control: weighted-all visibility mixes both layers, so the fixture really discriminates.
                feature.settings.visibilityMode = MetaHuman.Rendering.GroomTransparencyFeature.VisibilityMode.WeightedAll;
                var mixed = Render(samples);
                float mixedRed = 0;
                for (int y = 40; y < 216; y++) for (int x = 54; x < 140; x++) mixedRed += mixed[y * 256 + x].r;
                Assert.That(mixedRed, Is.GreaterThan(green * .1f), "Control: weighted-all must show the back layer.");
            }
            finally
            {
                feature.settings.visibilityMode = previousMode;
                feature.settings.nearestSelection = previousSelection;
                feature.SetActive(featureEnabled);
                if (frontMesh != null) foreach (var mesh in frontMesh.meshes) Object.DestroyImmediate(mesh);
                if (backMesh != null) foreach (var mesh in backMesh.meshes) Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(front); Object.DestroyImmediate(back);
            }
        }

        [Test] public void EyeOcclusionAttenuatesWithoutAddingLight()
        {
            var background = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var overlay = new Material(Shader.Find("MetaHuman/URP/EyeOverlay"));
            try
            {
                background.SetColor("_BaseColor", new Color(0.5f, 0.4f, 0.3f, 1));
                Quad(background, Vector3.zero, Vector3.one * 0.08f);
                var before = Render();
                overlay.SetFloat("_Mode", 0); overlay.SetFloat("_Strength", 0.5f);
                Quad(overlay, new Vector3(0, 0, -0.01f), Vector3.one * 0.05f);
                var after = Render(); int attenuated = 0; float added = 0;
                for (int i = 0; i < before.Length; i++)
                {
                    added = Mathf.Max(added, after[i].r - before[i].r, after[i].g - before[i].g, after[i].b - before[i].b);
                    if (before[i].grayscale - after[i].grayscale > 0.005f) attenuated++;
                }
                Assert.That(attenuated, Is.GreaterThan(1000));
                Assert.That(added, Is.LessThan(0.001f), "An occlusion shell must never emit a highlight.");
            }
            finally { Object.DestroyImmediate(background); Object.DestroyImmediate(overlay); }
        }

        [Test] public void ZeroOpacityCardsDoNotWriteColorOrOccludeBackground()
        {
            var background = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var cards = new Material(Shader.Find("MetaHuman/URP/HairCards"));
            try
            {
                background.SetColor("_BaseColor", Color.white);
                Quad(background, Vector3.zero, Vector3.one * .08f);
                var before = Render();
                cards.SetColor("_BaseColor", new Color(.01f, .01f, .01f, 1));
                cards.SetFloat("_OpacityStrength", 0);
                Quad(cards, new Vector3(0, 0, -.01f), Vector3.one * .05f);
                var hidden = Render();
                cards.SetFloat("_OpacityStrength", 1);
                var visible = Render();
                float error = 0; int changed = 0;
                for (int i = 0; i < before.Length; i++)
                {
                    error = Mathf.Max(error, Mathf.Abs(before[i].grayscale - hidden[i].grayscale));
                    if (before[i].grayscale - visible[i].grayscale > .01f) changed++;
                }
                Assert.That(error, Is.LessThan(.001f));
                Assert.That(changed, Is.GreaterThan(1000), "The control must also permit visible cards.");
            }
            finally { Object.DestroyImmediate(background); Object.DestroyImmediate(cards); }
        }

        [TestCase(1)] [TestCase(4)]
        public void EyeSceneCopyLimitsHighlightsAndUpdatesForEachCameraRender(int samples)
        {
            var feature = CameraFeature<EyeSceneColorFeature>();
            Assert.That(feature, Is.Not.Null, "Install the optical snapshot for this GPU integration test.");
            bool enabled = feature.isActive;
            float ceiling = feature.radianceCeiling, exponent = feature.encodingExponent;
            var background = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var optical = new Material(Shader.Find("MetaHuman/URP/EyeOverlay"));
            try
            {
                feature.SetActive(true); feature.radianceCeiling = 10; feature.encodingExponent = 4;
                background.SetColor("_BaseColor", new Color(40, 1, 4, 1));
                Quad(background, Vector3.zero, Vector3.one * .08f);
                optical.SetFloat("_UseSceneFilter", 1); optical.SetFloat("_BlurRadiusMillimetres", 0);
                optical.SetTexture("_ShadowTransmissionMap", Texture2D.whiteTexture);
                var shell = Quad(optical, new Vector3(0, 0, -.01f), Vector3.one * .05f);
                shell.SetActive(false); var baseline = Render(samples)[128 * 256 + 128];
                shell.SetActive(true); var first = Render(samples)[128 * 256 + 128];
                Assert.That(baseline.r, Is.GreaterThan(20), "The input must contain an HDR highlight.");
                Assert.That(first.r, Is.EqualTo(10).Within(.1f), "Only the sampled optical layer uses the legacy scene-copy range.");
                Assert.That(first.g, Is.EqualTo(baseline.g).Within(.04f));
                background.SetColor("_BaseColor", new Color(1, 40, 4, 1));
                // A second camera has its own render graph and must not sample the first snapshot.
                var secondCamera = new GameObject("Second optical camera").AddComponent<Camera>();
                secondCamera.transform.SetParent(root.transform); secondCamera.CopyFrom(camera);
                var firstCamera = camera;
                try
                {
                    camera = secondCamera;
                    camera.GetUniversalAdditionalCameraData().requiresColorTexture = true;
                    var second = Render(samples)[128 * 256 + 128];
                    Assert.That(second.g, Is.EqualTo(10).Within(.1f));
                    Assert.That(second.r, Is.EqualTo(first.g).Within(.04f));
                }
                finally { camera = firstCamera; }
                feature.SetActive(false); optical.SetFloat("_Strength", 0);
                var withoutSnapshot = Render(samples)[128 * 256 + 128];
                Assert.That(withoutSnapshot.g, Is.GreaterThan(20), "A disabled feature must not reuse another camera's stale snapshot.");
            }
            finally
            {
                feature.radianceCeiling = ceiling; feature.encodingExponent = exponent; feature.SetActive(enabled);
                Object.DestroyImmediate(background); Object.DestroyImmediate(optical);
            }
        }

        [Test] public void OrthographicEyeBlurKeepsItsPhysicalFootprintWhenCameraDistanceChanges()
        {
            var feature = CameraFeature<EyeSceneColorFeature>();
            Assert.That(feature, Is.Not.Null);
            bool enabled = feature.isActive;
            float ceiling = feature.radianceCeiling;
            var background = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var optical = new Material(Shader.Find("MetaHuman/URP/EyeOverlay"));
            var pattern = new Texture2D(64, 64, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            try
            {
                var colors = new Color[4096];
                for (int i = 0; i < colors.Length; i++) colors[i] = i % 64 < 32 ? Color.black : Color.white;
                pattern.SetPixels(colors); pattern.Apply();
                background.SetTexture("_BaseMap", pattern);
                Quad(background, Vector3.zero, Vector3.one * .08f);
                optical.SetFloat("_UseSceneFilter", 1); optical.SetFloat("_BlurRadiusMillimetres", 2);
                optical.SetTexture("_BlurWeightMap", Texture2D.whiteTexture);
                optical.SetTexture("_ShadowTransmissionMap", Texture2D.whiteTexture);
                var shell = Quad(optical, new Vector3(0, 0, -.01f), Vector3.one * .05f);
                feature.SetActive(true); feature.radianceCeiling = 0;
                shell.SetActive(false); var sharp = Render();
                shell.SetActive(true); var near = Render();
                camera.transform.position = new Vector3(0, 0, -.4f);
                var far = Render();
                float blurChange = 0, distanceChange = 0;
                for (int y = 80; y < 176; y++) for (int x = 112; x < 144; x++)
                {
                    int i = y * 256 + x;
                    blurChange = Mathf.Max(blurChange, Mathf.Abs(near[i].r - sharp[i].r));
                    distanceChange = Mathf.Max(distanceChange, Mathf.Abs(near[i].r - far[i].r));
                }
                Assert.That(blurChange, Is.GreaterThan(.1f), "A nonzero blur must spread the sharp boundary.");
                Assert.That(distanceChange, Is.LessThan(.002f), "Orthographic projected millimetres must be independent of camera distance.");
            }
            finally
            {
                feature.radianceCeiling = ceiling; feature.SetActive(enabled);
                Object.DestroyImmediate(background); Object.DestroyImmediate(optical); Object.DestroyImmediate(pattern);
            }
        }

        [Test] public void EyePupilAndOffAxisRefractionControlsChangeTheRenderedIris()
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/MetaHumanGenerated/Kellan/Materials/MI_MetaHumanEye_Simplified.mat");
            if (!source) Assert.Ignore("Local Kellan eye fixture is absent.");
            var eye = new Material(source);
            try
            {
                Quad(eye, Vector3.zero, Vector3.one * 0.08f);
                camera.transform.position = new Vector3(0.04f, 0, -0.2f); camera.transform.LookAt(Vector3.zero);
                eye.SetFloat("_IrisDepth", 0); eye.SetFloat("_PupilScale", 0.5f); var small = Render();
                eye.SetFloat("_PupilScale", 1.8f); var dilated = Render();
                eye.SetFloat("_IrisDepth", 0.04f); var refracted = Render();
                int pupilChanged = 0, refractedChanged = 0;
                for (int i = 0; i < small.Length; i++)
                {
                    if (Mathf.Abs(small[i].grayscale - dilated[i].grayscale) > 0.001f) pupilChanged++;
                    if (Mathf.Abs(dilated[i].grayscale - refracted[i].grayscale) > 0.001f) refractedChanged++;
                }
                Assert.That(pupilChanged, Is.GreaterThan(20));
                Assert.That(refractedChanged, Is.GreaterThan(20));
                Assert.That(source.GetFloat("_PupilScale"), Is.EqualTo(1.12f).Within(0.001));
            }
            finally { Object.DestroyImmediate(eye); }
        }
    }
}

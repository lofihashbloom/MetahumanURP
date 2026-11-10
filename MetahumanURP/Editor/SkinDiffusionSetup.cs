using System;
using System.Linq;
using MetaHuman.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MetaHuman.Editor
{
    public static class SkinDiffusionSetup
    {
        [MenuItem("Tools/MetaHuman/Enable Skin Diffusion On Active Renderer")]
        public static void Enable()
        {
            var feature = GetOrCreate();
            feature.SetActive(true);
            EditorUtility.SetDirty(feature);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Convert source profile lengths to the independent radial diffusion model.</summary>
        public static SkinDiffusionFeature ApplyProfile(SubsurfaceProfileRecord profile)
        {
            if (profile == null || profile.meanFreePathColor == null || profile.meanFreePathColor.Length < 3 ||
                !(profile.meanFreePathDistanceCentimetres > 0)) throw new ArgumentException("A source profile with positive declared lengths is required.");
            var feature = GetOrCreate();
            feature.settings.useProfileLengths = true;
            feature.settings.meanFreePathMillimetres = new Vector3(profile.meanFreePathColor[0], profile.meanFreePathColor[1], profile.meanFreePathColor[2])
                * profile.meanFreePathDistanceCentimetres * 10;
            feature.settings.normalizedDiffusion = profile.burley;
            if (profile.burley) feature.settings.scatterDistanceMillimetres = GetScatterDistances(profile);
            feature.settings.albedoPreservation = 1;
            feature.settings.strength = 1;
            feature.SetActive(true); EditorUtility.SetDirty(feature);
            return feature;
        }

        /// <summary>Burley shape distances d (real millimetres) for the normalized diffusion pass.</summary>
        /// <remarks>Matches Unreal 5.8 (checked against the engine source, pinned by SkinDiffusionProfileTests):
        /// SubsurfaceProfile.cpp stores DMFP (mm) = MFP x SearchLight(A) / (Perpendicular(A) x 0.6) with
        /// MFP = MeanFreePathColor x MeanFreePathDistance (cm) x 10; the default Burley mode
        /// (LIGHT_PERPENDICULAR_DIFFUSE_SURFACE) samples D = DMFP / SearchLight(A); CalculateBurleyScale with
        /// SSSScaleX = P00 / SUBSURFACE_KERNEL_SIZE x 0.5 maps profile millimetres to real millimetres x WorldUnitScale
        /// (the kernel-size factors cancel). Epic's SSP_Head gives red d of about 0.11 mm: sub-pixel at portrait
        /// framings in Unreal as here, so a visibly wider diffusion is a look choice, not parity.</remarks>
        public static Vector3 GetScatterDistances(SubsurfaceProfileRecord profile)
        {
            if (profile == null || profile.surfaceAlbedo == null || profile.surfaceAlbedo.Length < 3
                || profile.meanFreePathColor == null || profile.meanFreePathColor.Length < 3
                || !(profile.meanFreePathDistanceCentimetres > 0))
                throw new ArgumentException("Normalized diffusion requires positive profile lengths and RGB surface albedo.");
            var result = Vector3.zero;
            float scale = profile.worldUnitScale > 0 ? profile.worldUnitScale : 1;
            for (int channel = 0; channel < 3; channel++)
            {
                float a = Mathf.Clamp01(profile.surfaceAlbedo[channel]);
                // Published normalized-diffusion shape fits for perpendicular
                // illumination and diffuse mean free path. The 0.6 conversion
                // belongs to the source legacy MFP parameter convention.
                float shape = profile.meanFreePath
                    ? .6f * (1.85f - a + 7 * Mathf.Pow(Mathf.Abs(a - .8f), 3))
                    : 3.5f + 100 * Mathf.Pow(a - .33f, 4);
                result[channel] = Mathf.Max(.0001f, profile.meanFreePathColor[channel]
                    * profile.meanFreePathDistanceCentimetres * 10 * scale / shape);
            }
            return result;
        }

        public static SkinDiffusionFeature GetOrCreate()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!urp) throw new InvalidOperationException("Skin diffusion requires URP 17.6 with Render Graph enabled.");
            var serialized = new SerializedObject(urp);
            var list = serialized.FindProperty("m_RendererDataList");
            int index = serialized.FindProperty("m_DefaultRendererIndex").intValue;
            var renderer = list.GetArrayElementAtIndex(index).objectReferenceValue as UniversalRendererData;
            if (!renderer) throw new InvalidOperationException("The active renderer must be a Universal Renderer.");
            var feature = renderer.rendererFeatures.OfType<SkinDiffusionFeature>().FirstOrDefault();
            if (!feature)
            {
                feature = ScriptableObject.CreateInstance<SkinDiffusionFeature>();
                feature.name = "MetaHuman Skin Diffusion";
                AssetDatabase.AddObjectToAsset(feature, renderer);
                renderer.rendererFeatures.Add(feature);
            }
            feature.settings.shader = Shader.Find("Hidden/MetaHuman/SkinDiffusion");
            if (!feature.settings.shader) throw new InvalidOperationException("Skin diffusion shader is unavailable.");
            feature.Create();
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(renderer);
            renderer.SetDirty();
            return feature;
        }
    }
}

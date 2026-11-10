using System;
using System.Linq;
using MetaHuman.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MetaHuman.Editor
{
    public static class EyeOpticsSetup
    {
        public static EyeSceneColorFeature GetOrCreate()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!pipeline) throw new InvalidOperationException("Eye optics requires URP with Render Graph enabled.");
            var serialized = new SerializedObject(pipeline);
            var renderer = serialized.FindProperty("m_RendererDataList")
                .GetArrayElementAtIndex(serialized.FindProperty("m_DefaultRendererIndex").intValue)
                .objectReferenceValue as UniversalRendererData;
            if (!renderer) throw new InvalidOperationException("Eye optics requires a Universal Renderer.");
            var feature = renderer.rendererFeatures.OfType<EyeSceneColorFeature>().FirstOrDefault();
            if (!feature)
            {
                feature = ScriptableObject.CreateInstance<EyeSceneColorFeature>();
                feature.name = "MetaHuman Eye Optical Snapshot";
                AssetDatabase.AddObjectToAsset(feature, renderer);
                renderer.rendererFeatures.Add(feature);
            }
            feature.shader = Shader.Find("Hidden/MetaHuman/EyeSceneColor");
            if (!feature.shader) throw new InvalidOperationException("Eye scene-color shader is unavailable.");
            feature.Create(); feature.SetActive(true);
            EditorUtility.SetDirty(feature); EditorUtility.SetDirty(renderer); renderer.SetDirty();
            return feature;
        }

        /// <summary>The feature on the active pipeline's default renderer, or null. Changes nothing.</summary>
        public static EyeSceneColorFeature Find()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!pipeline) return null;
            var serialized = new SerializedObject(pipeline);
            var renderer = serialized.FindProperty("m_RendererDataList")
                .GetArrayElementAtIndex(serialized.FindProperty("m_DefaultRendererIndex").intValue)
                .objectReferenceValue as UniversalRendererData;
            return renderer ? renderer.rendererFeatures.OfType<EyeSceneColorFeature>().FirstOrDefault() : null;
        }

        [MenuItem("Tools/MetaHuman/Enable Eye Optics On Active Renderer")]
        static void EnableMenu() => GetOrCreate();

        /// <summary>
        /// Used by the importer: the eye overlay needs the scene-colour feature, but adding or re-enabling a renderer
        /// feature changes the user's renderer for every scene. Ask first; in batch mode, or when declined, only report.
        /// </summary>
        public static void EnsureForImport()
        {
            var feature = Find();
            if (feature && feature.isActive) return;
            string what = feature ? "is disabled on" : "is missing from";
            string message = "The eye overlay (blur and shadow at the eyelids) needs the 'MetaHuman Eye Optical Snapshot' renderer feature, which " + what + " the active URP renderer.";
            if (!Application.isBatchMode && EditorUtility.DisplayDialog("MetaHuman eye optics", message + "\n\nEnable it now? This changes the renderer asset for every scene that uses it.", "Enable", "Not now"))
            { GetOrCreate(); return; }
            Debug.LogWarning(message + " Enable it with Tools > MetaHuman > Enable Eye Optics On Active Renderer.");
        }

        public static void ApplyLookups(Material material, Texture2D blurWeight, Texture2D transmission,
            Color blurTint, float radiusMillimetres)
        {
            if (!material || material.shader.name != "MetaHuman/URP/EyeOverlay" || !blurWeight || !transmission)
                throw new ArgumentException("An eye overlay material and both linear source lookups are required.");
            EnsureForImport();
            material.SetFloat("_Mode", 0);
            material.SetTexture("_BlurWeightMap", blurWeight);
            material.SetTexture("_ShadowTransmissionMap", transmission);
            material.SetColor("_BlurTint", blurTint);
            material.SetFloat("_BlurRadiusMillimetres", Mathf.Max(0, radiusMillimetres));
            material.SetFloat("_UseSceneFilter", 1);
            EditorUtility.SetDirty(material);
        }
    }
}

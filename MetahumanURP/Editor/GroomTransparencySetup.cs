using System;
using System.Linq;
using MetaHuman.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MetaHuman.Editor
{
    public static class GroomTransparencySetup
    {
        [MenuItem("Tools/MetaHuman/Enable Continuous Groom Transparency On Active Renderer")]
        public static void Enable()
        {
            var feature = GetOrCreate();
            feature.SetActive(true);
            EditorUtility.SetDirty(feature);
            foreach (var id in AssetDatabase.FindAssets("t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(id));
                if (m && m.shader && m.shader.name == "MetaHuman/URP/GroomStrands")
                { m.SetFloat("_CoverageMode", 1); EditorUtility.SetDirty(m); }
            }
            AssetDatabase.SaveAssets();
        }

        public static GroomTransparencyFeature GetOrCreate()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!urp) throw new InvalidOperationException("Groom transparency requires URP 17.6 with Render Graph enabled.");
            var serialized = new SerializedObject(urp);
            var list = serialized.FindProperty("m_RendererDataList");
            int index = serialized.FindProperty("m_DefaultRendererIndex").intValue;
            var renderer = list.GetArrayElementAtIndex(index).objectReferenceValue as UniversalRendererData;
            if (!renderer) throw new InvalidOperationException("The active renderer must be a Universal Renderer.");
            var feature = renderer.rendererFeatures.OfType<GroomTransparencyFeature>().FirstOrDefault();
            if (!feature)
            {
                feature = ScriptableObject.CreateInstance<GroomTransparencyFeature>();
                feature.name = "MetaHuman Continuous Groom Transparency";
                AssetDatabase.AddObjectToAsset(feature, renderer);
                renderer.rendererFeatures.Add(feature);
            }
            feature.settings.shader = Shader.Find("Hidden/MetaHuman/GroomTransparency");
            if (!feature.settings.shader) throw new InvalidOperationException("Groom transparency shader is unavailable.");
            feature.Create();
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(renderer);
            renderer.SetDirty();
            return feature;
        }
    }
}

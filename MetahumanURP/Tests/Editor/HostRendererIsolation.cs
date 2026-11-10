using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MetaHuman.Tests
{
    /// <summary>
    /// The GPU tests render through the host project's default renderer and compare pixel values. Any renderer feature
    /// that is not this package's (a film grade, ambient occlusion, a full-screen effect) changes those values and fails
    /// tests that have nothing to do with it. This switches such features off for the duration of a test and restores
    /// them on Dispose. The package's own features stay as the host configured them.
    /// </summary>
    sealed class HostRendererIsolation : IDisposable
    {
        readonly List<ScriptableRendererFeature> disabled = new List<ScriptableRendererFeature>();

        public HostRendererIsolation()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!pipeline) return;
            var serialized = new SerializedObject(pipeline);
            var list = serialized.FindProperty("m_RendererDataList");
            int index = serialized.FindProperty("m_DefaultRendererIndex").intValue;
            if (list == null || index < 0 || index >= list.arraySize) return;
            var data = list.GetArrayElementAtIndex(index).objectReferenceValue as ScriptableRendererData;
            if (!data) return;
            var package = typeof(MetaHuman.Rendering.SkinDiffusionFeature).Assembly;
            foreach (var feature in data.rendererFeatures)
            {
                if (!feature || !feature.isActive || feature.GetType().Assembly == package) continue;
                feature.SetActive(false);
                disabled.Add(feature);
            }
        }

        public void Dispose()
        {
            foreach (var feature in disabled) if (feature) feature.SetActive(true);
            disabled.Clear();
        }
    }
}

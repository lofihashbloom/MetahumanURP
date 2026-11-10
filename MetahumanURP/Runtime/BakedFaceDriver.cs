using System;
using System.Collections.Generic;
using UnityEngine;

namespace MetaHuman
{
    /// <summary>Named 0..1 inputs for exported blend shapes; full joint solving belongs to a DNA backend.</summary>
    [DisallowMultipleComponent]
    public sealed class BakedFaceDriver : MonoBehaviour
    {
        public SkinnedMeshRenderer face;
        public FaceMapping mapping;
        public Renderer[] wrinkleRenderers = Array.Empty<Renderer>();
        public bool applyInLateUpdate = true;
        readonly Dictionary<string, float> controls = new Dictionary<string, float>(StringComparer.Ordinal);
        readonly Dictionary<int, float> weights = new Dictionary<int, float>();
        readonly List<int> driven = new List<int>();
        MaterialPropertyBlock block;
        static readonly int WrinkleWeights = Shader.PropertyToID("_WrinkleWeights");

        public void SetControl(string control, float value)
        {
            if (string.IsNullOrEmpty(control)) throw new ArgumentException("A control name is required.", nameof(control));
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
            controls[control] = Mathf.Clamp01(value);
        }

        public void ClearControls() => controls.Clear();
        void LateUpdate() { if (applyInLateUpdate) Apply(); }

        public void Apply()
        {
            if (!face || !face.sharedMesh || !mapping) return;
            // Clear only outputs owned by this driver, including bindings removed since the last frame.
            foreach (int index in driven)
                if (index < face.sharedMesh.blendShapeCount) face.SetBlendShapeWeight(index, 0);
            driven.Clear();
            weights.Clear();
            foreach (var binding in mapping.blendShapes)
            {
                int index = face.sharedMesh.GetBlendShapeIndex(binding.blendShape ?? "");
                if (index < 0) continue;
                controls.TryGetValue(binding.control ?? "", out float value);
                weights.TryGetValue(index, out float weight);
                weights[index] = weight + value * binding.scale;
            }
            foreach (var pair in weights)
            {
                face.SetBlendShapeWeight(pair.Key, Mathf.Clamp(pair.Value, 0, 100));
                driven.Add(pair.Key);
            }
            Vector4 wrinkle = Vector4.zero;
            foreach (var binding in mapping.wrinkles)
                if (binding.map >= 0 && binding.map < 3 && controls.TryGetValue(binding.control ?? "", out float value))
                    wrinkle[binding.map] += value * binding.scale;
            for (int i = 0; i < 3; i++) wrinkle[i] = Mathf.Clamp01(wrinkle[i]);
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var surface in wrinkleRenderers)
            {
                if (!surface) continue;
                surface.GetPropertyBlock(block);
                block.SetVector(WrinkleWeights, wrinkle);
                surface.SetPropertyBlock(block);
            }
        }

        void OnDisable()
        {
            ClearControls();
            Apply();
        }
    }
}

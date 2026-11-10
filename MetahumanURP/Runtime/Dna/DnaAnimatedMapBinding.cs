using System;
using System.Collections.Generic;
using UnityEngine;

namespace MetaHuman.Dna
{
    /// <summary>One rig animated-map output feeding one region of a wrinkle mask slice.</summary>
    [Serializable]
    public struct AnimatedMapRegion
    {
        [Tooltip("Rig animated map name, e.g. head_wm1_normal.head_wm1_browsRaiseInner_L (or the Unreal material form with an underscore).")]
        public string name;
        [Tooltip("Wrinkle map: 0, 1 or 2 (WM1-3 / CM1-3).")]
        public int map;
        [Tooltip("Slice of the region mask array (0-9).")]
        public int slice;
        [Tooltip("Mask channel within the slice: 0 R, 1 G, 2 B, 3 A.")]
        public int channel;
    }

    /// <summary>
    /// Copies a DnaRigDriver's animated-map outputs into the skin shader's per-region wrinkle weights
    /// (_AnimatedMapWeights) on one renderer, through a MaterialPropertyBlock. The skin material needs the
    /// MH_ANIMATED_MAPS keyword, the three wrinkle normal/colour maps and the region mask array. Runs after
    /// the driver; call Apply() yourself after DnaRigDriver.EvaluateNow when driving the rig manually.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class DnaAnimatedMapBinding : MonoBehaviour
    {
        public const int Maps = 3, Slices = 10;
        static readonly int WeightsId = Shader.PropertyToID("_AnimatedMapWeights");

        public DnaRigDriver driver;
        public Renderer target;
        [Tooltip("Material slot of the head skin on the target renderer.")]
        public int materialIndex;
        public AnimatedMapRegion[] regions = Array.Empty<AnimatedMapRegion>();

        readonly Vector4[] weights = new Vector4[Maps * Slices];
        MaterialPropertyBlock block;
        int[] rigIndex;
        NativeRig boundRig;

        /// <summary>Regions whose names the rig does not provide (they stay at zero weight).</summary>
        public IReadOnlyList<string> UnmatchedRegions { get; private set; } = Array.Empty<string>();

        public Vector4[] Weights => weights;

        void LateUpdate()
        {
            if (Application.isPlaying) Apply();
        }

        void OnDisable()
        {
            if (!target) return;
            Array.Clear(weights, 0, weights.Length);
            block ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(block, materialIndex);
            block.SetVectorArray(WeightsId, weights);
            target.SetPropertyBlock(block, materialIndex);
        }

        /// <summary>Writes the current rig outputs to the target renderer.</summary>
        public void Apply()
        {
            var rig = driver ? driver.Rig : null;
            if (rig == null || !target) return;
            if (rig != boundRig)
            {
                rigIndex = MatchRegions(rig.AnimatedMapNames, regions, out var unmatched);
                UnmatchedRegions = unmatched;
                boundRig = rig;
            }
            Pack(rig.AnimatedMaps, rigIndex, regions, weights);
            block ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(block, materialIndex);
            block.SetVectorArray(WeightsId, weights);
            target.SetPropertyBlock(block, materialIndex);
        }

        /// <summary>
        /// Rig output index for each region, or -1. DNA names are "head_wm1_normal.head_wm1_blink_L"; Unreal material
        /// parameters use the same name with the dot replaced by an underscore. Either form matches.
        /// </summary>
        public static int[] MatchRegions(IReadOnlyList<string> rigNames, IReadOnlyList<AnimatedMapRegion> regions, out List<string> unmatched)
        {
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < rigNames.Count; i++)
            {
                byName[rigNames[i]] = i;
                byName[rigNames[i].Replace('.', '_')] = i;
            }
            var index = new int[regions.Count];
            unmatched = new List<string>();
            for (int i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                if (r.map < 0 || r.map >= Maps || r.slice < 0 || r.slice >= Slices || r.channel < 0 || r.channel > 3)
                    throw new ArgumentOutOfRangeException(nameof(regions), "Invalid animated map region: " + r.name);
                if (!byName.TryGetValue(r.name ?? "", out index[i])) { index[i] = -1; unmatched.Add(r.name); }
            }
            return index;
        }

        /// <summary>Packs rig outputs into the shader layout: weights[map * Slices + slice][channel].</summary>
        public static void Pack(IReadOnlyList<float> values, IReadOnlyList<int> rigIndex, IReadOnlyList<AnimatedMapRegion> regions, Vector4[] weights)
        {
            if (weights.Length != Maps * Slices) throw new ArgumentException("Expected " + Maps * Slices + " weight slots.", nameof(weights));
            Array.Clear(weights, 0, weights.Length);
            for (int i = 0; i < regions.Count; i++)
            {
                int index = rigIndex[i];
                if (index < 0) continue;
                var r = regions[i];
                int slot = r.map * Slices + r.slice;
                var w = weights[slot];
                w[r.channel] = values[index];
                weights[slot] = w;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MetaHuman.Groom
{
    /// <summary>
    /// Screen-size strand LOD for continuous-coverage strand materials (GroomStrands and shaders with the same
    /// <c>_StrandFraction</c> property). Below <see cref="fullDetailScreenHeight"/> it draws a fraction of the strands,
    /// chosen by each strand's stable random value, and widens the kept ones by 1/fraction so the coverage of the
    /// groom is preserved. Dropped strands exit at the top of the vertex shader, so cost falls roughly with the
    /// fraction. Fewer strands also mean fewer specular glints: measured on Ada, half the strands at 2.5x the
    /// portrait distance darkened the hair by about 10%. Keep close-ups at full detail.
    /// The fraction is written to per-component copies of the renderers' materials (assets are never modified);
    /// the original materials are restored on disable. List only the strand renderers to thin (for example the
    /// scalp groom, not eyebrows). One fraction applies to every camera; the reference camera chooses it.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(1150)]
    public sealed class GroomStrandLod : MonoBehaviour
    {
        static readonly int FractionId = Shader.PropertyToID("_StrandFraction");

        [Tooltip("Camera that chooses the fraction; Camera.main when empty.")]
        public Camera referenceCamera;
        public Renderer[] renderers = Array.Empty<Renderer>();
        [Tooltip("Groom bounds in this transform's space, projected to measure the on-screen height.")]
        public Bounds localReferenceBounds = new Bounds(Vector3.zero, Vector3.one * .3f);
        [Tooltip("At or above this fraction of the screen height every strand is drawn.")]
        [Range(.01f, 1)] public float fullDetailScreenHeight = .35f;
        [Tooltip("Smallest fraction of strands drawn.")]
        [Range(.05f, 1)] public float minimumFraction = .25f;
        [Tooltip("Fraction = (screen height / full-detail height)^exponent, in 1/8 steps rounded up.")]
        [Range(.5f, 3)] public float exponent = 1;
        [Tooltip("Relative change of the ideal fraction needed before the drawn fraction moves (avoids popping back and forth).")]
        [Range(0, .5f)] public float hysteresis = .1f;

        public float CurrentFraction { get; private set; } = 1;
        public float LastScreenHeight { get; private set; }

        readonly Dictionary<Material, Material> instances = new Dictionary<Material, Material>();
        readonly List<(Renderer renderer, Material[] original)> assigned = new List<(Renderer, Material[])>();

        void OnEnable()
        {
            CurrentFraction = 1;
            foreach (var renderer in renderers)
            {
                if (!renderer) continue;
                var original = renderer.sharedMaterials;
                var copies = new Material[original.Length];
                bool any = false;
                for (int i = 0; i < original.Length; i++)
                {
                    var m = original[i];
                    if (!m || !m.HasFloat(FractionId)) { copies[i] = m; continue; }
                    if (!instances.TryGetValue(m, out var copy))
                    {
                        instances[m] = copy = new Material(m) { name = m.name + " (strand LOD)", hideFlags = HideFlags.DontSave };
                        copy.SetFloat(FractionId, 1);
                    }
                    copies[i] = copy; any = true;
                }
                if (!any) continue;
                assigned.Add((renderer, original));
                renderer.sharedMaterials = copies;
            }
        }

        void OnDisable()
        {
            foreach (var (renderer, original) in assigned) if (renderer) renderer.sharedMaterials = original;
            assigned.Clear();
            foreach (var copy in instances.Values) if (copy) Destroy(copy);
            instances.Clear();
            CurrentFraction = 1;
        }

        void LateUpdate()
        {
            var camera = referenceCamera ? referenceCamera : Camera.main;
            if (!camera || !GroomLodMath.TryScreenHeight(GroomLodMath.TransformBounds(localReferenceBounds, transform.localToWorldMatrix), camera, out float height))
                return;
            LastScreenHeight = height;
            Apply(Fraction(height, CurrentFraction, fullDetailScreenHeight, minimumFraction, exponent, hysteresis));
        }

        /// <summary>Sets the drawn fraction on this component's material copies (1 = every strand).</summary>
        public void Apply(float fraction)
        {
            fraction = Mathf.Clamp(fraction, .01f, 1);
            if (Mathf.Approximately(fraction, CurrentFraction)) return;
            CurrentFraction = fraction;
            foreach (var copy in instances.Values) if (copy) copy.SetFloat(FractionId, fraction);
        }

        /// <summary>
        /// Drawn fraction for an on-screen height: (height / fullDetail)^exponent clamped to [minimum, 1] and rounded up
        /// to 1/8 steps; the current fraction is kept while the ideal one stays within <paramref name="hysteresis"/>.
        /// </summary>
        public static float Fraction(float height, float current, float fullDetail, float minimum, float exponent, float hysteresis)
        {
            if (!GroomLodMath.IsFinite(height) || fullDetail <= 0) return 1;
            minimum = Mathf.Clamp(minimum, .01f, 1);
            float ideal = Mathf.Clamp(Mathf.Pow(Mathf.Max(height, 0) / fullDetail, Mathf.Max(exponent, .01f)), minimum, 1);
            if (current > 0 && current <= 1 && Mathf.Abs(ideal - current) <= hysteresis * current) return current;
            return Mathf.Max(minimum, Mathf.Min(1, Mathf.Ceil(ideal * 8 - 1e-4f) / 8));
        }
    }
}

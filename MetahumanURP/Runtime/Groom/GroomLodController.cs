using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MetaHuman.Groom
{
    /// <summary>Opt-in shared-camera strand/card switching. Never fabricates cards or activates imported hierarchy roots.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(1200)]
    public sealed class GroomLodController : MonoBehaviour
    {
        public bool automaticSelection;
        public Camera referenceCamera;
        public Camera[] additionalCameras = Array.Empty<Camera>();
        public Renderer[] strandRenderers = Array.Empty<Renderer>();
        public Renderer[] cardRenderers = Array.Empty<Renderer>();
        public Bounds localReferenceBounds = new Bounds(Vector3.zero, Vector3.one * .3f);
        [Range(.001f, 1)] public float strandScreenHeight = .2f;
        [Range(0, .49f)] public float hysteresis = .1f;
        public bool allowCulling;
        [Range(0, .5f)] public float cullScreenHeight = .01f;
        public bool invalidateTemporalHistory = true;
        public GroomRepresentation CurrentRepresentation { get; private set; } = GroomRepresentation.Unmanaged;
        public float LastScreenHeight { get; private set; }
        public Camera LastDrivingCamera { get; private set; }
        public string LastError { get; private set; }
        public event Action<GroomRepresentation, GroomRepresentation> LevelChanged;
        static readonly Dictionary<Renderer, GroomLodController> Owners = new Dictionary<Renderer, GroomLodController>();
        readonly Dictionary<Renderer, bool> original = new Dictionary<Renderer, bool>();
        readonly HashSet<Renderer> requested = new HashSet<Renderer>();
        // Groups validated by the last successful Acquire; ownership is rechecked only when they change.
        Renderer[] acquiredStrands, acquiredCards;
        bool autoWasActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearOwners() => Owners.Clear();

        void LateUpdate()
        {
            if (!automaticSelection) { if (autoWasActive) Restore(); autoWasActive = false; return; }
            autoWasActive = true;
            Camera best = null; float bestHeight = -1;
            Consider(referenceCamera ? referenceCamera : Camera.main, ref best, ref bestHeight);
            if (additionalCameras != null) foreach (var camera in additionalCameras) Consider(camera, ref best, ref bestHeight);
            if (!best) { Restore(); LastError = "No active mono Game camera; original renderer states restored."; return; }
            Evaluate(best, bestHeight);
        }

        void Consider(Camera camera, ref Camera best, ref float height)
        {
            if (!camera || !camera.isActiveAndEnabled) return;
            if (GroomLodMath.TryScreenHeight(GroomLodMath.TransformBounds(localReferenceBounds, transform.localToWorldMatrix), camera, out float current) && current > height)
            { best = camera; height = current; }
        }

        /// <summary>Explicit offline/game-camera evaluation; disabled Game cameras are accepted. Global state affects every camera.</summary>
        public bool EvaluateForCamera(Camera camera)
        {
            if (!isActiveAndEnabled) { LastError = "Enable the controller before explicit evaluation."; return false; }
            if (!GroomLodMath.TryScreenHeight(GroomLodMath.TransformBounds(localReferenceBounds, transform.localToWorldMatrix), camera, out float height))
            { Restore(); LastError = "A finite mono Game-camera projection is required."; return false; }
            return Evaluate(camera, height);
        }

        bool Evaluate(Camera camera, float height)
        {
            if (!GroomLodMath.IsFinite(strandScreenHeight) || strandScreenHeight <= 0 || strandScreenHeight > 1 ||
                !GroomLodMath.IsFinite(cullScreenHeight) || cullScreenHeight < 0 ||
                !GroomLodMath.IsFinite(hysteresis) || hysteresis < 0 || hysteresis >= .5f ||
                (allowCulling && cullScreenHeight * (1 + hysteresis) >= strandScreenHeight * (1 - hysteresis)))
            { Restore(); LastError = "LOD thresholds must be finite, ordered and have separated hysteresis bands."; return false; }
            if (!Acquire()) { string error = LastError; Restore(); LastError = error; return false; }
            bool strands = Available(strandRenderers), cards = Available(cardRenderers);
            var next = GroomLodMath.Select(height, CurrentRepresentation, strandScreenHeight, cullScreenHeight, hysteresis, allowCulling, strands, cards);
            if (next == GroomRepresentation.Unmanaged) { Restore(); LastError = "No active renderer representation is available."; return false; }
            Apply(strandRenderers, next == GroomRepresentation.Strands);
            Apply(cardRenderers, next == GroomRepresentation.Cards);
            LastScreenHeight = height; LastDrivingCamera = camera; LastError = null;
            Change(next);
            return true;
        }

        bool Acquire()
        {
            // Per-frame fast path: unchanged groups keep their validated ownership without allocating.
            // An LODGroup enabled over owned renderers afterwards is detected on the next group change.
            if (original.Count > 0 && SameGroup(strandRenderers, acquiredStrands) && SameGroup(cardRenderers, acquiredCards)) return true;
            requested.Clear();
            if (!Request(strandRenderers) || !Request(cardRenderers)) return false;
            // Configuration changes during ownership restore the previous set before taking the new set.
            bool same = requested.Count == original.Count;
            if (same) foreach (var renderer in requested) if (!original.ContainsKey(renderer)) { same = false; break; }
            if (!same) Restore();
            foreach (var renderer in requested) if (!original.ContainsKey(renderer)) { original.Add(renderer, renderer.enabled); Owners[renderer] = this; }
            acquiredStrands = strandRenderers == null ? null : (Renderer[])strandRenderers.Clone();
            acquiredCards = cardRenderers == null ? null : (Renderer[])cardRenderers.Clone();
            return true;
        }

        bool Request(Renderer[] group)
        {
            if (group == null) return true;
            foreach (var renderer in group)
            {
                if (!renderer) continue;
                if (!requested.Add(renderer)) { LastError = "A renderer occurs more than once in LOD groups."; return false; }
                if (Owners.TryGetValue(renderer, out var owner) && owner && owner != this)
                { LastError = "A renderer is already managed by another groom LOD controller."; return false; }
                foreach (var lodGroup in renderer.GetComponentsInParent<LODGroup>(true))
                    foreach (var lod in lodGroup.GetLODs()) foreach (var managed in lod.renderers)
                        if (managed == renderer && lodGroup.enabled) { LastError = "Disable the overlapping LODGroup before groom LOD ownership."; return false; }
            }
            return true;
        }

        static bool SameGroup(Renderer[] current, Renderer[] acquired)
        {
            if (current == null || acquired == null) return current == acquired;
            if (current.Length != acquired.Length) return false;
            for (int i = 0; i < current.Length; i++) if (!ReferenceEquals(current[i], acquired[i])) return false;
            return true;
        }

        static bool Available(Renderer[] group)
        {
            if (group == null || group.Length == 0) return false;
            foreach (var renderer in group)
            {
                if (!renderer || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff) return false;
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    if (skin) mesh = skin.sharedMesh;
                }
                else
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter) mesh = filter.sharedMesh;
                }
                if (!mesh || mesh.vertexCount < 3 || mesh.subMeshCount < 1) return false;
                bool hasIndices = false;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++) if (mesh.GetIndexCount(submesh) >= 3) { hasIndices = true; break; }
                if (!hasIndices) return false;
            }
            return true;
        }
        static void Apply(Renderer[] group, bool value) { if (group != null) foreach (var renderer in group) if (renderer) renderer.enabled = value; }

        void Change(GroomRepresentation next)
        {
            var previous = CurrentRepresentation; CurrentRepresentation = next;
            if (previous == next) return;
            if (invalidateTemporalHistory)
            {
                Invalidate(LastDrivingCamera);
                if (referenceCamera != LastDrivingCamera) Invalidate(referenceCamera);
                if (additionalCameras != null) foreach (var camera in additionalCameras) if (camera != LastDrivingCamera && camera != referenceCamera) Invalidate(camera);
            }
            LevelChanged?.Invoke(previous, next);
        }
        static void Invalidate(Camera camera)
        {
            if (!camera) return;
            if (camera.TryGetComponent<GroomTemporalHistory>(out var history) && history.isActiveAndEnabled) history.InvalidateHistory();
            else if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var additional) && additional.antialiasing == AntialiasingMode.TemporalAntiAliasing)
                additional.resetHistory = true;
        }

        /// <summary>Restores exact pre-ownership Renderer.enabled values. Does not modify hierarchy activation or assets.</summary>
        public void Restore()
        {
            foreach (var pair in original)
            {
                if (Owners.TryGetValue(pair.Key, out var owner) && owner == this) Owners.Remove(pair.Key);
                if (pair.Key) pair.Key.enabled = pair.Value;
            }
            original.Clear(); Change(GroomRepresentation.Unmanaged); LastDrivingCamera = null;
        }
        void OnDisable() => Restore();
        void OnDestroy() => Restore();
    }
}

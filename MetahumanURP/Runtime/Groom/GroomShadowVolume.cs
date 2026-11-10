using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetaHuman.Groom
{
    /// <summary>
    /// Hair self-shadowing for <c>MetaHuman/URP/GroomStrands</c> renderers. Builds a fibre extinction volume from the
    /// strand meshes in the <see cref="space"/> transform's local frame (usually the head bone, so it follows head
    /// motion), then precomputes on the GPU, for up to four lights, the optical depth from every voxel toward each light.
    /// Strand fragments sample it once per light. Transmittance is recomputed only when a light moves relative to the
    /// volume by more than <see cref="lightTolerance"/>; voxels with no fibres nearby are then marched at a quarter of
    /// the resolution (<see cref="coarseEmptySpace"/>). Requires <c>GroomShadowFeature</c> on the URP renderer; without
    /// it nothing changes.
    /// The strands are treated as rigid in <see cref="space"/>: no simulation or per-strand deformation.
    /// Fibres are gray occluders (no fibre colour, no multiple scattering).
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class GroomShadowVolume : MonoBehaviour
    {
        public const int SlotCount = 4;
        const string StrandShaderName = "MetaHuman/URP/GroomStrands";

        [Tooltip("Frame the volume is built in and follows (e.g. the head bone). Defaults to this transform.")]
        public Transform space;
        [Tooltip("Strand renderers. Empty: every child renderer with a GroomStrands material.")]
        public Renderer[] renderers = new Renderer[0];
        [Tooltip("Shadowed lights. Empty: up to four enabled realtime lights affecting the volume, directional first, then by intensity / distance squared.")]
        public Light[] lights = new Light[0];
        [Tooltip("Voxels along the volume's longest axis.")]
        [Range(16, 192)] public int resolution = 96;
        [Tooltip("Optical depth per unit of mean projected fibre area. 0.7 matches the receiver shadows' validated Ada setup.")]
        [Min(0)] public float density = .7f;
        [Tooltip("Distance, in voxels, the march skips before accumulating, so a fibre does not shadow itself.")]
        [Range(0, 3)] public float startOffsetVoxels = 1;
        [Tooltip("March step in voxels.")]
        [Range(.25f, 2)] public float stepVoxels = 1;
        [Tooltip("March voxels with no fibres nearby at a quarter of the resolution. Strands sample those only when displaced out of the groom (sway). Off: the full march everywhere.")]
        public bool coarseEmptySpace = true;
        [Tooltip("Voxels around the fibres that keep the full march when coarseEmptySpace is on.")]
        [Range(1, 4)] public int fullMarchMargin = 2;
        [Tooltip("Rotation of a light, as seen from the volume, tolerated before its transmittance is recomputed (degrees). 0: any motion.")]
        [Range(0, 1)] public float lightTolerance = DefaultLightTolerance;
        public const float DefaultLightTolerance = .1f;
        /// <summary>Edge, in voxels, of the cells of the coarse density the empty-space march steps through.</summary>
        public const int CoarseFactor = 4;

        /// <summary>Last build result or the reason the volume is inactive.</summary>
        public string Status { get; private set; } = "Not built";
        public Texture3D Density => built;
        public Vector3 BoundsMin { get; private set; }
        public Vector3 BoundsSize { get; private set; }
        public Vector3Int Cells { get; private set; }
        /// <summary>Share of the voxels that get the full march when <see cref="coarseEmptySpace"/> is on.</summary>
        public float FullMarchFraction { get; private set; }
        /// <summary>Transmittance targets, one per slot (null when the slot is empty).</summary>
        public RenderTexture GetTransmittance(int slot) => slots[slot].tau;
        public Light GetSlotLight(int slot) => slots[slot].light;

        static readonly List<GroomShadowVolume> active = new List<GroomShadowVolume>();
        public static IReadOnlyList<GroomShadowVolume> Active => active;

        struct Slot { public Light light; public RenderTexture tau; public Vector4 lightLocal, march; public bool valid, coarse; }
        readonly Slot[] slots = new Slot[SlotCount];
        [NonSerialized] Texture3D built, builtCoarse, builtMask;
        [NonSerialized] Vector3Int coarseCells;
        [NonSerialized] int builtVersion, dispatchedVersion = -1, builtResolution, builtMargin;
        [NonSerialized] Transform builtSpace;
        MaterialPropertyBlock block;

        Transform Space => space ? space : transform;

        void OnEnable()
        {
            active.Remove(this); active.Add(this);
            RenderPipelineManager.beginCameraRendering -= ResetCameraLights;
            RenderPipelineManager.beginCameraRendering += ResetCameraLights;
            // The density survives disable (a character switched off and on again), so only the first enable builds.
            if (!built) Rebuild();
        }

        void OnDisable()
        {
            active.Remove(this);
            if (active.Count == 0) { RenderPipelineManager.beginCameraRendering -= ResetCameraLights; ResetCameraLights(default, null); }
            ClearBlocks();
            for (int i = 0; i < SlotCount; i++) ReleaseSlot(i);
        }

        void OnDestroy() => DestroyBuilt();


        /// <summary>Rebuilds the extinction volume from the strand meshes (CPU, once; meshes must be readable).</summary>
        [ContextMenu("Rebuild Density")]
        public void Rebuild()
        {
            DestroyBuilt();
            if (renderers == null || renderers.Length == 0 || Array.TrueForAll(renderers, r => !r)) renderers = CollectStrandRenderers(transform);
            var sources = new List<(Mesh, Matrix4x4)>();
            var toLocal = Space.worldToLocalMatrix;
            foreach (var r in renderers)
            {
                if (!r) continue;
                var mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (!mesh) continue;
                if (!mesh.isReadable) { Status = $"Mesh '{mesh.name}' is not readable"; return; }
                sources.Add((mesh, toLocal * r.localToWorldMatrix));
            }
            if (sources.Count == 0) { Status = "No strand meshes"; return; }
            var grid = BuildDensity(sources, resolution);
            if (grid.segments == 0) { Status = "No strand segments"; return; }
            var half = new ushort[grid.values.Length];
            for (int i = 0; i < half.Length; i++) half[i] = Mathf.FloatToHalf(grid.values[i]);
            built = new Texture3D(grid.cells.x, grid.cells.y, grid.cells.z, TextureFormat.RHalf, false)
            {
                name = name + " groom extinction", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
            };
            built.SetPixelData(half, 0);
            built.Apply(false, true);
            var coarse = BuildCoarse(grid, out coarseCells);
            half = new ushort[coarse.Length];
            for (int i = 0; i < half.Length; i++) half[i] = Mathf.FloatToHalf(coarse[i]);
            builtCoarse = new Texture3D(coarseCells.x, coarseCells.y, coarseCells.z, TextureFormat.RHalf, false)
            {
                name = name + " groom extinction (coarse)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
            };
            builtCoarse.SetPixelData(half, 0);
            builtCoarse.Apply(false, true);
            var mask = BuildFullMarchMask(grid, fullMarchMargin);
            int full = 0;
            foreach (byte m in mask) if (m != 0) full++;
            FullMarchFraction = full / (float)mask.Length;
            builtMask = new Texture3D(grid.cells.x, grid.cells.y, grid.cells.z, TextureFormat.R8, false)
            {
                name = name + " groom full-march mask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave
            };
            builtMask.SetPixelData(mask, 0);
            builtMask.Apply(false, true);
            BoundsMin = grid.min; BoundsSize = (Vector3)grid.cells * grid.voxel; Cells = grid.cells;
            builtSpace = Space; builtResolution = resolution; builtMargin = fullMarchMargin; builtVersion++;
            var scale = Space.lossyScale;
            float worldVoxel = grid.voxel * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Status = $"{grid.segments} segments, {grid.cells.x}x{grid.cells.y}x{grid.cells.z} voxels of {worldVoxel * 1000:0.##} mm, {FullMarchFraction * 100:0}% near fibres";
        }

        void DestroyBuilt()
        {
            foreach (var texture in new[] { built, builtCoarse, builtMask })
                if (texture) { if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture); }
            built = builtCoarse = builtMask = null;
        }

        public static Renderer[] CollectStrandRenderers(Transform root)
        {
            var found = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m && m.shader && m.shader.name == StrandShaderName) { found.Add(r); break; }
            return found.ToArray();
        }

        // ---------------------------------------------------------------- density

        public struct DensityGrid
        {
            /// <summary>Optical depth per voxel length (sigma * voxel), x fastest.</summary>
            public float[] values;
            public Vector3Int cells;
            public Vector3 min;
            public float voxel;
            public int segments;
        }

        /// <summary>
        /// Deposits each ribbon segment's isotropic mean projected area (pi/4 * diameter * length, the Cauchy mean for
        /// a thin cylinder) into a voxel grid in the target frame, trilinearly. The sum of values * voxel^2 equals the
        /// total projected area. Ribbon meshes are GroomRibbonBaker quads: indices (a, a+2, a+1, a+1, a+2, a+3) per
        /// segment, with both vertices of a point at the strand centre and the diameter in UV1.x (object units).
        /// </summary>
        public static DensityGrid BuildDensity(IReadOnlyList<(Mesh mesh, Matrix4x4 toTarget)> sources, int resolution)
        {
            var a = new List<Vector3>(); var b = new List<Vector3>(); var d = new List<float>();
            var vertices = new List<Vector3>(); var uv1 = new List<Vector2>(); var indices = new List<int>();
            foreach (var (mesh, m) in sources)
            {
                mesh.GetVertices(vertices); mesh.GetUVs(1, uv1);
                if (uv1.Count != vertices.Count) continue;
                float det = Mathf.Abs(m.m00 * (m.m11 * m.m22 - m.m12 * m.m21) - m.m01 * (m.m10 * m.m22 - m.m12 * m.m20) + m.m02 * (m.m10 * m.m21 - m.m11 * m.m20));
                float widthScale = Mathf.Pow(det, 1f / 3);
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    mesh.GetIndices(indices, sub);
                    for (int k = 0; k + 5 < indices.Count; k += 6)
                    {
                        int i0 = indices[k], i1 = indices[k + 1];
                        if (i1 != i0 + 2 || indices[k + 5] != i0 + 3) continue;
                        a.Add(m.MultiplyPoint3x4(vertices[i0])); b.Add(m.MultiplyPoint3x4(vertices[i1]));
                        d.Add(Mathf.Max(0, .5f * (uv1[i0].x + uv1[i1].x)) * widthScale);
                    }
                }
            }
            var grid = new DensityGrid { segments = a.Count };
            if (a.Count == 0) { grid.values = new float[0]; return grid; }
            Vector3 lo = a[0], hi = a[0];
            for (int i = 0; i < a.Count; i++) { lo = Vector3.Min(lo, Vector3.Min(a[i], b[i])); hi = Vector3.Max(hi, Vector3.Max(a[i], b[i])); }
            const int margin = 2;
            var extent = hi - lo;
            float voxel = Mathf.Max(Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z)), 1e-4f) / Mathf.Max(resolution - 2 * margin, 1);
            var cells = new Vector3Int(Mathf.CeilToInt(extent.x / voxel) + 2 * margin, Mathf.CeilToInt(extent.y / voxel) + 2 * margin, Mathf.CeilToInt(extent.z / voxel) + 2 * margin);
            var centre = (lo + hi) * .5f;
            var min = centre - (Vector3)cells * (voxel * .5f);
            var values = new float[cells.x * cells.y * cells.z];
            float inverseVoxel = 1 / voxel, areaToValue = 1 / (voxel * voxel);
            int sx = cells.x, sxy = cells.x * cells.y;
            for (int i = 0; i < a.Count; i++)
            {
                var p0 = a[i]; var p1 = b[i];
                float length = Vector3.Distance(p0, p1);
                if (length <= 0 || d[i] <= 0) continue;
                int pieces = Mathf.Max(1, Mathf.CeilToInt(length * inverseVoxel * 2));
                float amount = Mathf.PI * .25f * d[i] * length / pieces * areaToValue;
                for (int k = 0; k < pieces; k++)
                {
                    var q = (Vector3.Lerp(p0, p1, (k + .5f) / pieces) - min) * inverseVoxel - new Vector3(.5f, .5f, .5f);
                    int x = Mathf.FloorToInt(q.x), y = Mathf.FloorToInt(q.y), z = Mathf.FloorToInt(q.z);
                    float fx = q.x - x, fy = q.y - y, fz = q.z - z;
                    // Margins keep every segment at least one cell inside, so all eight neighbours exist.
                    int c = z * sxy + y * sx + x;
                    values[c] += amount * (1 - fx) * (1 - fy) * (1 - fz);
                    values[c + 1] += amount * fx * (1 - fy) * (1 - fz);
                    values[c + sx] += amount * (1 - fx) * fy * (1 - fz);
                    values[c + sx + 1] += amount * fx * fy * (1 - fz);
                    values[c + sxy] += amount * (1 - fx) * (1 - fy) * fz;
                    values[c + sxy + 1] += amount * fx * (1 - fy) * fz;
                    values[c + sxy + sx] += amount * (1 - fx) * fy * fz;
                    values[c + sxy + sx + 1] += amount * fx * fy * fz;
                }
            }
            grid.values = values; grid.cells = cells; grid.min = min; grid.voxel = voxel;
            return grid;
        }

        /// <summary>
        /// 1 for every voxel within <paramref name="margin"/> voxels (per axis) of a voxel that holds fibres, else 0.
        /// Strands at rest sample only those: a fragment's trilinear fetch reaches one voxel beyond its deposits.
        /// </summary>
        public static byte[] BuildFullMarchMask(DensityGrid grid, int margin)
        {
            int sx = grid.cells.x, sy = grid.cells.y, sz = grid.cells.z;
            var mask = new byte[grid.values.Length]; var next = new byte[mask.Length];
            for (int i = 0; i < mask.Length; i++) mask[i] = grid.values[i] > 0 ? (byte)1 : (byte)0;
            // Box dilation, one axis at a time.
            for (int axis = 0; axis < 3; axis++)
            {
                int stride = axis == 0 ? 1 : axis == 1 ? sx : sx * sy, length = axis == 0 ? sx : axis == 1 ? sy : sz;
                for (int z = 0; z < sz; z++) for (int y = 0; y < sy; y++) for (int x = 0; x < sx; x++)
                {
                    int c = (z * sy + y) * sx + x, along = axis == 0 ? x : axis == 1 ? y : z;
                    byte any = 0;
                    for (int k = Mathf.Max(-margin, -along); k <= margin && along + k < length && any == 0; k++) any = mask[c + k * stride];
                    next[c] = any;
                }
                (mask, next) = (next, mask);
            }
            return mask;
        }

        /// <summary>Mean of the density over cells of <see cref="CoarseFactor"/> voxels a side (voxels past the grid count as empty).</summary>
        public static float[] BuildCoarse(DensityGrid grid, out Vector3Int cells)
        {
            const int f = CoarseFactor;
            cells = new Vector3Int((grid.cells.x + f - 1) / f, (grid.cells.y + f - 1) / f, (grid.cells.z + f - 1) / f);
            var coarse = new float[cells.x * cells.y * cells.z];
            int sx = grid.cells.x, sy = grid.cells.y, sz = grid.cells.z;
            for (int z = 0; z < sz; z++) for (int y = 0; y < sy; y++) for (int x = 0; x < sx; x++)
                coarse[((z / f) * cells.y + y / f) * cells.x + x / f] += grid.values[(z * sy + y) * sx + x];
            for (int i = 0; i < coarse.Length; i++) coarse[i] /= f * f * f;
            return coarse;
        }

        /// <summary>
        /// Whether a light left the pose its transmittance was marched for. Poses are in the volume's local frame: a
        /// unit direction (w 0) or a position relative to the volume minimum (w 1). With a tolerance, a light counts as
        /// moved once its direction, seen from the nearest point of the volume's bounding sphere, turned by more than
        /// <paramref name="toleranceDegrees"/>; with none, on any change.
        /// </summary>
        public static bool LightMoved(Vector4 now, Vector4 marched, Vector3 size, float toleranceDegrees)
        {
            if (now.w != marched.w) return true;
            float change = ((Vector3)now - (Vector3)marched).sqrMagnitude;
            if (change <= 1e-12f * Mathf.Max(1, ((Vector3)now).sqrMagnitude)) return false;
            if (toleranceDegrees <= 0) return true;
            float radians = toleranceDegrees * Mathf.Deg2Rad;
            if (now.w < .5f) { float chord = 2 * Mathf.Sin(radians * .5f); return change > chord * chord; }
            // A light inside or touching the bounding sphere has voxels arbitrarily close: any motion counts.
            float near = Mathf.Min(((Vector3)now - size * .5f).magnitude, ((Vector3)marched - size * .5f).magnitude) - size.magnitude * .5f;
            if (near <= 0) return true;
            float allowed = Mathf.Tan(radians) * near;
            return change > allowed * allowed;
        }

        // ---------------------------------------------------------------- lights

        static readonly Dictionary<Light, int> lightIds = new Dictionary<Light, int>();

        /// <summary>Stable small identity (from 1) shared by the shader's per-camera and per-renderer light tables.</summary>
        public static int LightId(Light light)
        {
            if (!light) return 0;
            if (!lightIds.TryGetValue(light, out int id))
            {
                if (lightIds.Count > 4096) lightIds.Clear();
                lightIds.Add(light, id = lightIds.Count + 1);
            }
            return id;
        }

        static Light[] sceneLights = new Light[0];
        static int sceneLightsFrame = -1;

        static Light[] SceneLights()
        {
            // Cached per rendered frame in play mode; edit mode (and tests) may not advance the frame count.
            if (sceneLightsFrame != Time.renderedFrameCount || !Application.isPlaying)
            {
                // FindObjectsByType skips HideFlags.DontSave objects (tool-created rig lights); keep scene lights only.
                var all = Resources.FindObjectsOfTypeAll<Light>();
                var inScene = new List<Light>(all.Length);
                foreach (var l in all) if (l && l.gameObject.scene.IsValid()) inScene.Add(l);
                sceneLights = inScene.ToArray();
                sceneLightsFrame = Time.renderedFrameCount;
            }
            return sceneLights;
        }

        /// <summary>Lights to shadow, at most four: the explicit list, else the most important realtime lights.</summary>
        public static List<Light> SelectLights(IReadOnlyList<Light> explicitLights, IEnumerable<Light> candidates, Vector3 centre, float radius)
        {
            var result = new List<Light>();
            bool anyExplicit = false;
            if (explicitLights != null)
                foreach (var l in explicitLights)
                {
                    if (!l) continue;
                    anyExplicit = true;
                    if (l.isActiveAndEnabled && !result.Contains(l) && result.Count < SlotCount) result.Add(l);
                }
            if (anyExplicit) return result;
            var scored = new List<(Light light, float score)>();
            foreach (var l in candidates)
            {
                if (!l || !l.isActiveAndEnabled || l.intensity <= 0 || l.lightmapBakeType == LightmapBakeType.Baked) continue;
                if (l.type == LightType.Directional) { scored.Add((l, float.MaxValue / 2 + l.intensity)); continue; }
                if (l.type != LightType.Point && l.type != LightType.Spot) continue;
                float distance = Vector3.Distance(l.transform.position, centre);
                if (distance > l.range + radius) continue;
                scored.Add((l, l.intensity / Mathf.Max(distance * distance, 1e-4f)));
            }
            scored.Sort((x, y) => y.score.CompareTo(x.score));
            for (int i = 0; i < scored.Count && result.Count < SlotCount; i++) result.Add(scored[i].light);
            return result;
        }

        // ---------------------------------------------------------------- per frame

        /// <summary>One transmittance dispatch: march every voxel of <see cref="target"/> toward one light.</summary>
        public struct Dispatch
        {
            public Texture3D density;
            public RenderTexture target;
            public Vector4 resolution, size, light, march;
            /// <summary>Coarse density and full-march mask; unset (or coarseScale.w 0): the full march everywhere.</summary>
            public Texture3D coarse, fullMarch;
            /// <summary>xyz: local position (from the volume minimum) to coarse texture coordinates, w: empty-space step in voxels (0 = off).</summary>
            public Vector4 coarseScale;
        }

        /// <summary>
        /// Main-thread update for this frame: picks lights, keeps one transmittance target per light, binds the renderers,
        /// and appends a dispatch for every slot whose light moved relative to the volume (or whose density changed).
        /// </summary>
        public void Prepare(List<Dispatch> dispatches)
        {
            if (built && (builtSpace != Space || builtResolution != resolution || builtMargin != fullMarchMargin)) Rebuild();
            if (!built) { ClearBlocks(); return; }
            var toLocal = Space.worldToLocalMatrix;
            var centre = Space.TransformPoint(BoundsMin + BoundsSize * .5f);
            float radius = Space.TransformVector(BoundsSize).magnitude * .5f;
            bool hasExplicit = lights != null && Array.Exists(lights, l => l);
            var chosen = SelectLights(lights, hasExplicit ? Array.Empty<Light>() : SceneLights(), centre, radius);
            float voxel = Mathf.Min(BoundsSize.x / Cells.x, Mathf.Min(BoundsSize.y / Cells.y, BoundsSize.z / Cells.z));
            var march = new Vector4(startOffsetVoxels, density, Mathf.Ceil(2 * (Cells.x + Cells.y + Cells.z) / Mathf.Max(stepVoxels, .25f)), stepVoxels);
            for (int s = 0; s < SlotCount; s++)
            {
                var light = s < chosen.Count ? chosen[s] : null;
                if (!light) { ReleaseSlot(s); continue; }
                ref var slot = ref slots[s];
                Vector4 local = light.type == LightType.Directional
                    ? (Vector4)toLocal.MultiplyVector(-light.transform.forward).normalized
                    : (Vector4)(toLocal.MultiplyPoint3x4(light.transform.position) - BoundsMin);
                local.w = light.type == LightType.Directional ? 0 : 1;
                bool moved = slot.light != light || !slot.valid || dispatchedVersion != builtVersion || slot.march != march
                    || slot.coarse != coarseEmptySpace || LightMoved(local, slot.lightLocal, BoundsSize, lightTolerance);
                if (!slot.tau || slot.tau.width != Cells.x || slot.tau.height != Cells.y || slot.tau.volumeDepth != Cells.z)
                {
                    if (slot.tau) DestroyTarget(slot.tau);
                    slot.tau = new RenderTexture(Cells.x, Cells.y, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear)
                    {
                        dimension = TextureDimension.Tex3D, volumeDepth = Cells.z, enableRandomWrite = true,
                        filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = name + " groom transmittance " + s,
                        hideFlags = HideFlags.DontSave
                    };
                    slot.tau.Create();
                    moved = true;
                }
                slot.light = light;
                if (!moved) continue;
                slot.lightLocal = local; slot.march = march; slot.valid = true; slot.coarse = coarseEmptySpace;
                float coarseCell = CoarseFactor * BoundsSize.x / Cells.x;
                dispatches.Add(new Dispatch
                {
                    density = built, target = slot.tau, coarse = builtCoarse, fullMarch = builtMask,
                    coarseScale = new Vector4(1 / (coarseCells.x * coarseCell), 1 / (coarseCells.y * coarseCell), 1 / (coarseCells.z * coarseCell),
                        coarseEmptySpace ? CoarseFactor : 0),
                    resolution = new Vector4(Cells.x, Cells.y, Cells.z, stepVoxels),
                    size = new Vector4(BoundsSize.x, BoundsSize.y, BoundsSize.z, voxel),
                    light = local,
                    march = march,
                });
            }
            dispatchedVersion = builtVersion;
            Bind();
        }

        static readonly int MainLightId = Shader.PropertyToID("_MHGroomMainLightId");
        static readonly int AdditionalCountId = Shader.PropertyToID("_MHGroomAdditionalLightCount");

        // Every camera starts with no light identities. Only GroomShadowFeature's pass publishes them, so a camera whose
        // renderer lacks the feature (or has it disabled) shades strands unshadowed instead of with stale bindings.
        static void ResetCameraLights(ScriptableRenderContext context, Camera camera)
        {
            Shader.SetGlobalFloat(MainLightId, 0);
            Shader.SetGlobalFloat(AdditionalCountId, 0);
        }

        static readonly int EnabledId = Shader.PropertyToID("_MHGroomShadowEnabled");
        static readonly int WorldToVolumeId = Shader.PropertyToID("_MHGroomShadowWorldToVolume");
        static readonly int SlotIdsId = Shader.PropertyToID("_MHGroomShadowSlotIds");
        static readonly int[] TauIds =
        {
            Shader.PropertyToID("_MHGroomShadowTau0"), Shader.PropertyToID("_MHGroomShadowTau1"),
            Shader.PropertyToID("_MHGroomShadowTau2"), Shader.PropertyToID("_MHGroomShadowTau3"),
        };
        static Texture3D empty;

        /// <summary>World position to the volume's unit cube.</summary>
        public Matrix4x4 WorldToVolume =>
            Matrix4x4.Scale(new Vector3(1 / BoundsSize.x, 1 / BoundsSize.y, 1 / BoundsSize.z)) * Matrix4x4.Translate(-BoundsMin) * Space.worldToLocalMatrix;

        void Bind()
        {
            if (!empty)
            {
                empty = new Texture3D(1, 1, 1, TextureFormat.RHalf, false) { hideFlags = HideFlags.HideAndDontSave, name = "Groom shadow empty" };
                empty.SetPixelData(new ushort[] { 0 }, 0); empty.Apply(false, true);
            }
            var ids = Vector4.zero;
            for (int s = 0; s < SlotCount; s++) ids[s] = slots[s].valid && slots[s].light ? LightId(slots[s].light) : 0;
            var matrix = WorldToVolume;
            Apply(b =>
            {
                b.SetFloat(EnabledId, 1);
                b.SetMatrix(WorldToVolumeId, matrix);
                b.SetVector(SlotIdsId, ids);
                for (int s = 0; s < SlotCount; s++) b.SetTexture(TauIds[s], slots[s].valid && slots[s].tau ? slots[s].tau : (Texture)empty);
            });
        }

        void ClearBlocks() => Apply(b => b.SetFloat(EnabledId, 0));

        // Per material index, preserving other components' properties (a per-index block overrides the renderer block).
        void Apply(Action<MaterialPropertyBlock> set)
        {
            if (renderers == null) return;
            block ??= new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (!r) continue;
                int count = r.sharedMaterials.Length;
                for (int i = 0; i < count; i++) { r.GetPropertyBlock(block, i); set(block); r.SetPropertyBlock(block, i); }
            }
        }

        void ReleaseSlot(int s)
        {
            if (slots[s].tau) DestroyTarget(slots[s].tau);
            slots[s] = default;
        }

        static void DestroyTarget(RenderTexture texture)
        {
            texture.Release();
            if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace MetaHuman.Rendering
{
    /// <summary>
    /// Prototype deferred groom renderer. Grooms it draws must be excluded from GroomTransparencyFeature (its layers),
    /// or their forward passes must cull themselves while _MHGroomDeferredActive is set (as the v27 LookDev shader does).
    /// Hardware raster mode:
    /// 1. Attribute pass: the strand ribbons drawn opaque, without discard, into a copy of the opaque depth: the depth
    ///    test keeps the nearest expanded ribbon per pixel (on tile-based GPUs, hidden-surface removal shades only it),
    ///    which writes its shading inputs (tangent, position along the strand, random, material frame, material id).
    /// 2. Optical depth: the all-strand optical depth, as GroomTransparencyFeature accumulates it.
    /// Compute raster mode: the same ribbon quads rasterized by compute (one thread per segment, pixel-centre tests),
    /// optical depth by atomic add, the nearest segment by atomic max of its depth; from a segment buffer built once on
    /// the GPU from the ribbon meshes (compensated-rectangle profile). Raster parameters are per renderer: its material
    /// with the renderer's property-block overrides (for example a per-renderer _StrandFraction). Ribbon meshes whose mean
    /// segment length projects to settings.longSegmentPixels or more (long strands with few points) are drawn by the GPU's
    /// rasterizer instead (Hidden/MetaHuman/GroomLongSegments, same corner placement), into their own optical depth and
    /// nearest targets: a compute thread pays a long thin ribbon's length serially, the rasterizer only its pixels.
    /// Both: shading evaluates each hair pixel once with the material's own lighting, then blends it over the camera
    /// colour with opacity 1 - exp(-tau). Hardware mode draws one full-screen pass per material. Compute mode draws one
    /// screen rectangle per shade group: the renderers sharing a material and a GroomShadowVolume, shaded with the
    /// property block of the group's first renderer. Single-sample cameras only for now.
    /// </summary>
    public sealed class GroomDeferredFeature : ScriptableRendererFeature
    {
        public enum RasterMode { Hardware = 0, Compute = 1 }

        [System.Serializable] public sealed class Settings
        {
            public LayerMask layers = -1;
            [Tooltip("For the cameras this feature draws, GroomTransparencyFeature skips these layers entirely. Put the deferred grooms on their own layer: otherwise its forward-path passes still run their vertex stage over every strand to cull it (LookDev cast, M5 1080p: 1.1 ms per frame median). Grooms on other layers then stay on the forward path even when their shader has the deferred passes.")]
            public bool exclusiveLayers;
            public RasterMode rasterMode = RasterMode.Compute;
            public ComputeShader computeShader;
            [Tooltip("Compute raster: one pass with 64-bit atomics (exact nearest strand) where the GPU supports them.")]
            public bool singlePassInt64 = true;
            [Tooltip("Compute raster diagnostics: 1 = stop after the ribbon corners, 2 = pixel tests without atomics, 3 = dispatch the hardware-path meshes' segments too, 4 = id pass without the first-pass winner test, 5 = skip the long-segment optical depth draw, 6 = skip the long-segment nearest draw, 7 = skip the compute raster dispatches.")]
            public int computeDiagnostic;
            [Tooltip("Compute raster: ribbon meshes whose mean segment length projects to at least this many pixels are drawn by the GPU's rasterizer instead (long strand segments; 0 = all in compute).")]
            [Min(0)] public float longSegmentPixels = 12;
            [Tooltip("Hardware draw of long-segment meshes (Hidden/MetaHuman/GroomLongSegments when empty).")]
            public Shader longSegmentShader;
            [Tooltip("Frames between rescans of the scene's groom renderers and materials.")]
            [Min(1)] public int rescanFrames = 120;
            [Header("Diagnostics (cost split; the image is wrong with any of these on)")]
            public bool skipAttributes;
            public bool skipOpticalDepth;
            public bool skipShade;
        }

        public Settings settings = new Settings();
        /// <summary>Groom materials shaded by the last recorded camera (diagnostics and tests).</summary>
        public int lastRecordedMaterials { get; private set; }
        public bool lastRecordedSkippedMultisample { get; private set; }
        /// <summary>Raster mode actually used by the last recorded camera (Compute falls back to Hardware when unsupported).</summary>
        public RasterMode lastRecordedRasterMode { get; private set; }
        public int segmentCount => geometry.segmentCount;
        /// <summary>Segments dispatched to the compute rasterizer for the last camera (meshes on the hardware path excluded).</summary>
        public int computeSegmentCount => geometry.activeSegments;
        /// <summary>Compute mode: shade groups (material and self-shadow volume) of the scanned renderers.</summary>
        public int shadeGroupCount => groups.Count;
        /// <summary>Compute mode: ribbon meshes the last prepared camera draws with the hardware rasterizer (long segments).</summary>
        public int hardwareMeshCount => geometry.hardwareEntries;
        public string computeUnsupportedReason => geometry.unsupported;
        /// <summary>Whether the last compute-rasterized camera used the 64-bit single pass.</summary>
        public bool lastRecordedSinglePass { get; private set; }

        GroomPass pass;
        FlagPass flagPass;
        readonly List<Material> materials = new List<Material>();
        readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
        // Compute shade groups: one per (material, GroomShadowVolume) pair; rendererGroups[i] is renderers[i]'s group.
        readonly List<ShadeGroup> groups = new List<ShadeGroup>();
        readonly List<int> rendererGroups = new List<int>();
        bool multiMaterialRenderer;
        int scannedVolumes;
        readonly Geometry geometry = new Geometry();
        int lastScanFrame;
        bool scanned;

        sealed class ShadeGroup
        {
            public Material material;
            public MeshRenderer representative;
            public readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
            public Vector4 rect;
        }
        const string ShadePassName = "GroomDeferredShade", ComputeShadePassName = "GroomComputeShade";
        static readonly int MaterialIdId = Shader.PropertyToID("_MHGroomDeferredId");
        static readonly int ActiveId = Shader.PropertyToID("_MHGroomDeferredActive");

        static readonly SharedCameraHook cameraHook = new SharedCameraHook(ResetActive);
        [System.NonSerialized] bool cameraHookHeld;

        public override void Create()
        {
            cameraHook.Acquire(ref cameraHookHeld);
            pass?.Dispose();
            pass = new GroomPass(this, settings);
            // Before GroomTransparencyFeature's pass: its forward-path groom passes cull deferred materials.
            flagPass = new FlagPass { renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents - 3) };
        }

        protected override void Dispose(bool disposing)
        {
            cameraHook.Release(ref cameraHookHeld);
            Shader.SetGlobalFloat(ActiveId, 0);
            pass?.Dispose();
            geometry.Release();
        }

        static void ResetActive(ScriptableRenderContext context, Camera camera) { Shader.SetGlobalFloat(ActiveId, 0); exclusiveLayersThisCamera = 0; }

        /// <summary>Layers GroomTransparencyFeature skips for the camera being rendered (settings.exclusiveLayers of the
        /// deferred features drawing it; reset as each camera begins).</summary>
        internal static int exclusiveLayersThisCamera;

        sealed class FlagData { }
        sealed class FlagPass : ScriptableRenderPass
        {
            public FlagPass() => requiresIntermediateTexture = true;
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                if (frame.Get<UniversalResourceData>().isActiveTargetBackBuffer) return;
                using var builder = graph.AddUnsafePass<FlagData>("MetaHuman: deferred groom on", out _);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (FlagData d, UnsafeGraphContext ctx) => ctx.cmd.SetGlobalFloat(ActiveId, 1));
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData;
            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection ||
                camera.camera.stereoEnabled || camera.renderType != CameraRenderType.Base) return;
            lastRecordedSkippedMultisample = camera.cameraTargetDescriptor.msaaSamples > 1;
            if (lastRecordedSkippedMultisample) return;
            Refresh();
            if (materials.Count == 0) return;
            // A deferred-capable groom material on a renderer this feature cannot draw (a SkinnedMeshRenderer, for strands
            // that deform with the skin) would be drawn by neither path: the shared-layer flag culls its forward passes and
            // exclusive layers make the transparency feature skip it. Leave every groom to the forward path for this camera.
            if (unsupportedRenderer)
            {
                if (warnedRenderer != unsupportedRenderer)
                {
                    warnedRenderer = unsupportedRenderer;
                    Debug.LogWarning($"GroomDeferredFeature: '{unsupportedRenderer.name}' ({unsupportedRenderer.GetType().Name}) uses a deferred groom material on the feature's layers, but only MeshRenderers can be drawn deferred. All grooms use the forward path until it is moved to another layer or given another material.", unsupportedRenderer);
                }
                lastRecordedRasterMode = RasterMode.Hardware;
                return;
            }
            pass.compute = settings.rasterMode == RasterMode.Compute && PrepareCompute(camera.camera, camera.cameraTargetDescriptor.height);
            if (!pass.compute) AssignMaterialIds();
            lastRecordedRasterMode = pass.compute ? RasterMode.Compute : RasterMode.Hardware;
            // Never claim Everything: the transparency feature would drop the grooms this feature does not draw (eyelashes).
            // With its own layers the forward path already skips the deferred grooms, and grooms of the same shaders on
            // other layers (strands that deform with the skin, which this feature cannot draw) stay on the forward path:
            // the shader-side cull flag would hide them, so it is set only when the layers are shared.
            if (settings.exclusiveLayers && settings.layers != ~0) exclusiveLayersThisCamera |= settings.layers;
            else renderer.EnqueuePass(flagPass);
            renderer.EnqueuePass(pass);
        }

        Renderer unsupportedRenderer, warnedRenderer;

        /// <summary>Forces a rescan of the scene's groom renderers and materials (and a segment rebuild) on the next frame.</summary>
        public void InvalidateMaterials() { scanned = false; geometry.Invalidate(); }

        static bool Drawn(Renderer r) => r && r.enabled && r.gameObject.activeInHierarchy && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly;

        void Refresh()
        {
            int frame = Time.frameCount;
            // Rescan on schedule, and at once when a tracked renderer stops drawing (e.g. the LookDev switching character).
            if (scanned && frame >= lastScanFrame && frame - lastScanFrame < settings.rescanFrames && materials.TrueForAll(m => m)
                && renderers.TrueForAll(Drawn) && scannedVolumes == MetaHuman.Groom.GroomShadowVolume.Active.Count) return;
            scanned = true; lastScanFrame = frame;
            materials.Clear(); renderers.Clear(); groups.Clear(); rendererGroups.Clear();
            multiMaterialRenderer = false; unsupportedRenderer = null;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!Drawn(r) || ((1 << r.gameObject.layer) & settings.layers.value) == 0) continue;
                bool groom = false;
                foreach (var m in r.sharedMaterials)
                {
                    if (!m || m.FindPass(ShadePassName) < 0) continue;
                    groom = true;
                    if (!materials.Contains(m)) materials.Add(m);
                }
                if (groom && !(r is MeshRenderer) && !unsupportedRenderer) unsupportedRenderer = r;
                if (!groom || !(r is MeshRenderer mr)) continue;
                renderers.Add(mr);
                if (r.sharedMaterials.Length != 1) multiMaterialRenderer = true;
            }
            // Shade groups: renderers of one material bound to the same self-shadow volume (its per-renderer property
            // block) shade together, with the first renderer's property block.
            var volumes = MetaHuman.Groom.GroomShadowVolume.Active;
            scannedVolumes = volumes.Count;
            var keys = new Dictionary<(Material, Object), int>();
            foreach (var r in renderers)
            {
                Object volume = null;
                foreach (var v in volumes)
                {
                    if (!v) continue;
                    bool member = v.renderers != null && v.renderers.Length > 0 ? System.Array.IndexOf(v.renderers, r) >= 0 : r.transform.IsChildOf(v.transform);
                    if (member) { volume = v; break; }
                }
                var key = (r.sharedMaterial, volume);
                if (!keys.TryGetValue(key, out int g))
                {
                    keys[key] = g = groups.Count;
                    groups.Add(new ShadeGroup { material = r.sharedMaterial, representative = r });
                }
                rendererGroups.Add(g);
            }
        }

        // Hardware mode: material ids select each material's pixels in its full-screen shade pass (written to the shared
        // materials; one material needs none, id 0). Compute mode selects shade groups through property blocks instead.
        void AssignMaterialIds()
        {
            if (materials.Count > 1)
                for (int i = 0; i < materials.Count && i < 256; i++)
                    if (materials[i].GetFloat(MaterialIdId) != i) materials[i].SetFloat(MaterialIdId, i);
        }

        bool PrepareCompute(Camera camera, float targetHeight)
        {
            var shader = settings.computeShader ? settings.computeShader : Resources.Load<ComputeShader>("MetaHumanGroomComputeRaster");
            if (!shader) { geometry.unsupported = "compute shader missing"; return false; }
            // The compute rasterizer compares depths as reversed Z (nearest = largest) and uses atomics: fall back to the
            // hardware mode where the platform has a conventional depth buffer (OpenGL) or no compute shaders.
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.usesReversedZBuffer) { geometry.unsupported = "compute raster needs compute shaders and a reversed depth buffer"; return false; }
            if (multiMaterialRenderer) { geometry.unsupported = "compute raster needs one groom material per renderer"; return false; }
            foreach (var material in materials)
                if (!material.HasFloat(RasterProfileId) || material.GetFloat(RasterProfileId) < .5f || material.GetFloat(CoverageModeId) < .5f
                    || material.FindPass(ComputeShadePassName) < 0 || !material.HasFloat(MinimumPixelsId) || !material.HasColor(BaseColorId))
                { geometry.unsupported = "compute raster needs the continuous compensated-rectangle profile: " + material.name; return false; }
            // Without the long-segment shader (stripped from a player build, unsupported) every mesh stays on compute:
            // hardware meshes are skipped by the compute rasterizer and would otherwise draw nothing.
            bool rasterDiagnostic = settings.computeDiagnostic == 1 || settings.computeDiagnostic == 2;
            float longPixels = !rasterDiagnostic && pass.LongMaterial() ? settings.longSegmentPixels : 0;
            if (!geometry.Prepare(shader, renderers, rendererGroups, camera, targetHeight, longPixels, settings.computeDiagnostic == 3)) return false;
            geometry.shader64 = null;
            if (settings.singlePassInt64)
            {
                var shader64 = Resources.Load<ComputeShader>("MetaHumanGroomComputeRaster64");
                if (shader64 && shader64.HasKernel("RasterMerged") && shader64.IsSupported(shader64.FindKernel("RasterMerged"))) geometry.shader64 = shader64;
            }
            lastRecordedSinglePass = geometry.shader64;
            return true;
        }

        /// <summary>Screen rectangle of each shade group (pixels, y down as the compute rasterizer's), from its renderers' bounds.</summary>
        void UpdateGroupRects(Matrix4x4 viewProj, int width, int height)
        {
            foreach (var g in groups) g.rect = new Vector4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (!r) continue;
                var g = groups[rendererGroups[i]];
                var b = r.bounds;
                Vector3 lo = b.min, hi = b.max;
                bool behind = false;
                for (int k = 0; k < 8; k++)
                {
                    var p = new Vector4((k & 1) != 0 ? hi.x : lo.x, (k & 2) != 0 ? hi.y : lo.y, (k & 4) != 0 ? hi.z : lo.z, 1);
                    var c = viewProj * p;
                    if (c.w <= 1e-5f) { behind = true; break; }
                    float x = (c.x / c.w * .5f + .5f) * width, y = (.5f - c.y / c.w * .5f) * height;
                    g.rect = new Vector4(Mathf.Min(g.rect.x, x), Mathf.Min(g.rect.y, y), Mathf.Max(g.rect.z, x), Mathf.Max(g.rect.w, y));
                }
                // Bounds crossing the camera plane: the whole screen.
                if (behind) g.rect = new Vector4(-1e9f, -1e9f, 1e9f, 1e9f);
            }
            foreach (var g in groups)
                g.rect = new Vector4(Mathf.Max(0, Mathf.Floor(g.rect.x) - 2), Mathf.Max(0, Mathf.Floor(g.rect.y) - 2),
                    Mathf.Min(width, Mathf.Ceil(g.rect.z) + 2), Mathf.Min(height, Mathf.Ceil(g.rect.w) + 2));
        }

        static readonly int RasterProfileId = Shader.PropertyToID("_RasterProfile"), CoverageModeId = Shader.PropertyToID("_CoverageMode");
        static readonly int MinimumPixelsId = Shader.PropertyToID("_RectangleMinimumPixels"), StrandFractionId = Shader.PropertyToID("_StrandFraction");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), CoverageSourceId = Shader.PropertyToID("_RectangleCoverageSource");
        static readonly int CoverageScaleId = Shader.PropertyToID("_RectangleCoverageScale"), CoverageBoundId = Shader.PropertyToID("_RectangleCoverageDataBound");

        /// <summary>Point and segment buffers built on the GPU from the ribbon meshes, plus the per-pixel targets.</summary>
        sealed class Geometry
        {
            public ComputeShader shader, shader64;
            public GraphicsBuffer points, segments, meshes, opticalDepth, nearestDepth, nearestId, nearest64, meshLengths;
            /// <summary>Segment ranges of the compute-rasterized meshes (per camera) and the per-segment "raised the nearest depth" bits.</summary>
            public GraphicsBuffer ranges, raised;
            public int activeSegments, rangeCount, raisedWords;
            Range[] rangeData = System.Array.Empty<Range>();
            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            struct Range { public uint firstSegment, firstDispatch; }
            /// <summary>Mean object-space segment length per entry (read back once after the build; negative until then).</summary>
            public float[] meanLengths = System.Array.Empty<float>();
            public int hardwareEntries;
            AsyncGPUReadbackRequest lengthRequest;
            bool lengthRequested, lengthPending;
            public int pointCount, segmentCount, pixelCount;
            public string unsupported;
            public bool needsBuild;
            public readonly List<Entry> entries = new List<Entry>();
            public MeshData[] meshData = System.Array.Empty<MeshData>();
            readonly List<GraphicsBuffer> meshBuffers = new List<GraphicsBuffer>();
            int signature;

            public struct Entry
            {
                public MeshRenderer renderer; public Material material; public Mesh mesh; public GraphicsBuffer vertices, indices;
                public int rendererIndex, pointBase, pointCount, segmentBase, segmentCount, stride, position, normal, tangent, uv0, uv1, uv3;
                public bool index16;
            }
            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            public struct MeshData { public Matrix4x4 localToWorld; public Vector4 raster; public uint group, coverageSource, hardware, pad; public Vector4 swayPivot, swayAxisAngle, swayFine, swayFront; }
            public const int MeshStride = 160;
            MaterialPropertyBlock scratch;   // created on first use: not allowed during ScriptableObject construction

            public void Invalidate() => signature = 0;

            public bool Prepare(ComputeShader computeShader, List<MeshRenderer> renderers, List<int> groups, Camera camera, float targetHeight, float longPixels, bool dispatchAll = false)
            {
                shader = computeShader;
                int sig = renderers.Count * 397;
                foreach (var r in renderers) sig = sig * 31 + (r ? r.GetEntityId().GetHashCode() : 0);
                if (sig != signature || points == null)
                {
                    signature = sig;
                    if (!Rebuild(renderers)) { Release(); return false; }
                }
                if (!needsBuild) ReleaseMeshBuffers();
                PollLengths();
                hardwareEntries = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    meshData[i].localToWorld = e.renderer ? e.renderer.localToWorldMatrix : Matrix4x4.identity;
                    meshData[i].group = (uint)groups[e.rendererIndex];
                    ReadRaster(e.renderer, e.material, ref meshData[i]);
                    // Long on-screen segments: the mean segment length projected at the mesh's distance.
                    bool hardware = false;
                    if (longPixels > 0 && meanLengths[i] > 0 && e.renderer && camera)
                    {
                        var scale = e.renderer.transform.lossyScale;
                        float length = meanLengths[i] * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                        float projection = Mathf.Abs(camera.projectionMatrix.m11) * targetHeight * .5f;
                        float depth = camera.orthographic ? 1 : Vector3.Dot(e.renderer.bounds.center - camera.transform.position, camera.transform.forward);
                        hardware = depth > 1e-4f && length * projection / depth >= longPixels;
                    }
                    meshData[i].hardware = hardware ? 1u : 0u;
                    if (hardware) hardwareEntries++;
                }
                meshes.SetData(meshData);
                // Only the compute-rasterized meshes are dispatched: a hardware mesh's segments would load their points
                // and mesh record just to return (Celeste: 48 of 49 meshes, 1.3M threads per pass).
                rangeCount = 0; activeSegments = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    if ((meshData[i].hardware != 0 && !dispatchAll) || entries[i].segmentCount == 0) continue;
                    rangeData[rangeCount++] = new Range { firstSegment = (uint)entries[i].segmentBase, firstDispatch = (uint)activeSegments };
                    activeSegments += entries[i].segmentCount;
                }
                if (rangeCount > 0) ranges.SetData(rangeData, 0, 0, rangeCount);
                return true;
            }

            // Mean segment length per mesh: read back once, after the build has been submitted (polled; no callback, so it
            // also completes where the player loop does not run). Until then every mesh is rasterized by compute.
            void PollLengths()
            {
                if (!needsBuild && !lengthRequested && meshLengths != null)
                {
                    lengthRequest = AsyncGPUReadback.Request(meshLengths);
                    lengthRequested = lengthPending = true;
                }
                if (!lengthPending) return;
                lengthRequest.Update();
                if (!lengthRequest.done) return;
                lengthPending = false;
                if (lengthRequest.hasError) return;
                var sums = lengthRequest.GetData<uint>();
                for (int i = 0; i < entries.Count && i < sums.Length; i++)
                    meanLengths[i] = sums[i] * 1e-4f / Mathf.Max(1, entries[i].segmentCount);
            }

            // The material's raster parameters with the renderer's property-block overrides (per material index 0 first,
            // as Unity applies them), as the strand shader's vertex stage reads them.
            void ReadRaster(Renderer r, Material m, ref MeshData d)
            {
                scratch ??= new MaterialPropertyBlock();
                scratch.Clear();
                if (r) { r.GetPropertyBlock(scratch, 0); if (scratch.isEmpty) r.GetPropertyBlock(scratch); }
                float F(int id) => scratch.HasFloat(id) ? scratch.GetFloat(id) : m.HasFloat(id) ? m.GetFloat(id) : 0;
                float fraction = F(StrandFractionId);
                float alpha = scratch.HasColor(BaseColorId) ? scratch.GetColor(BaseColorId).a : m.GetColor(BaseColorId).a;
                float source = F(CoverageSourceId);
                d.raster = new Vector4(F(MinimumPixelsId), fraction > 0 && fraction < 1 ? fraction : 1, alpha, F(CoverageScaleId));
                d.coverageSource = source > 1.5f ? 2u : source > .5f && F(CoverageBoundId) > .5f ? 1u : 0u;
                // GroomSway: the renderer's sway rotation (property block only; absent = no sway).
                d.swayPivot = scratch.HasVector(MetaHuman.Groom.GroomSway.PivotId) ? scratch.GetVector(MetaHuman.Groom.GroomSway.PivotId) : Vector4.zero;
                d.swayAxisAngle = scratch.HasVector(MetaHuman.Groom.GroomSway.AxisAngleId) ? scratch.GetVector(MetaHuman.Groom.GroomSway.AxisAngleId) : Vector4.zero;
                d.swayFine = scratch.HasVector(MetaHuman.Groom.GroomSway.FineId) ? scratch.GetVector(MetaHuman.Groom.GroomSway.FineId) : Vector4.zero;
                d.swayFront = scratch.HasVector(MetaHuman.Groom.GroomSway.FrontId) ? scratch.GetVector(MetaHuman.Groom.GroomSway.FrontId) : Vector4.zero;
            }

            static int Offset(Mesh mesh, VertexAttribute attribute, int dimension, bool required)
            {
                if (!mesh.HasVertexAttribute(attribute)) return required ? -2 : -1;
                if (mesh.GetVertexAttributeStream(attribute) != 0 || mesh.GetVertexAttributeFormat(attribute) != VertexAttributeFormat.Float32
                    || mesh.GetVertexAttributeDimension(attribute) < dimension) return -2;
                return mesh.GetVertexAttributeOffset(attribute);
            }

            bool Rebuild(List<MeshRenderer> renderers)
            {
                ReleaseBuffers();
                entries.Clear();
                int pointTotal = 0, segmentTotal = 0;
                for (int ri = 0; ri < renderers.Count; ri++)
                {
                    var r = renderers[ri];
                    var filter = r ? r.GetComponent<MeshFilter>() : null;
                    var mesh = filter ? filter.sharedMesh : null;
                    if (!mesh) continue;
                    if (mesh.subMeshCount != 1) { unsupported = "ribbon mesh with one submesh required: " + mesh.name; return false; }
                    var e = new Entry
                    {
                        renderer = r, rendererIndex = ri, material = r.sharedMaterial, mesh = mesh, stride = mesh.GetVertexBufferStride(0),
                        position = Offset(mesh, VertexAttribute.Position, 3, true), normal = Offset(mesh, VertexAttribute.Normal, 3, true),
                        tangent = Offset(mesh, VertexAttribute.Tangent, 3, false), uv0 = Offset(mesh, VertexAttribute.TexCoord0, 2, true),
                        uv1 = Offset(mesh, VertexAttribute.TexCoord1, 2, true), uv3 = Offset(mesh, VertexAttribute.TexCoord3, 1, false),
                        index16 = mesh.indexFormat == IndexFormat.UInt16,
                        pointBase = pointTotal, pointCount = mesh.vertexCount / 2,
                        segmentBase = segmentTotal, segmentCount = (int)(mesh.GetIndexCount(0) / 6)
                    };
                    if (e.position < 0 || e.normal < 0 || e.uv0 < 0 || e.uv1 < 0 || e.tangent == -2 || e.uv3 == -2)
                    { unsupported = "ribbon mesh layout (stream 0, Float32) required: " + mesh.name; return false; }
                    mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                    mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
                    e.vertices = mesh.GetVertexBuffer(0); e.indices = mesh.GetIndexBuffer();
                    meshBuffers.Add(e.vertices); meshBuffers.Add(e.indices);
                    pointTotal += e.pointCount; segmentTotal += e.segmentCount;
                    entries.Add(e);
                }
                if (segmentTotal == 0) { unsupported = "no ribbon segments"; return false; }
                pointCount = pointTotal; segmentCount = segmentTotal;
                points = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pointTotal, 32) { name = "MetaHuman groom points" };
                segments = new GraphicsBuffer(GraphicsBuffer.Target.Structured, segmentTotal, 4) { name = "MetaHuman groom segments" };
                meshes = new GraphicsBuffer(GraphicsBuffer.Target.Structured, entries.Count, MeshStride) { name = "MetaHuman groom meshes" };
                meshLengths = new GraphicsBuffer(GraphicsBuffer.Target.Structured, entries.Count, 4) { name = "MetaHuman groom mesh segment lengths" };
                meshLengths.SetData(new uint[entries.Count]);
                meanLengths = new float[entries.Count];
                for (int i = 0; i < meanLengths.Length; i++) meanLengths[i] = -1;
                lengthRequested = lengthPending = false;
                meshData = new MeshData[entries.Count];
                ranges = new GraphicsBuffer(GraphicsBuffer.Target.Structured, entries.Count, 8) { name = "MetaHuman groom compute segment ranges" };
                rangeData = new Range[entries.Count];
                raisedWords = (segmentTotal + 31) / 32;
                raised = new GraphicsBuffer(GraphicsBuffer.Target.Structured, raisedWords, 4) { name = "MetaHuman groom segments raising the nearest depth" };
                needsBuild = true; unsupported = null;
                return true;
            }

            /// <summary>Per-pixel buffers, grown to the largest camera seen (cameras of other sizes reuse them).</summary>
            public void EnsurePixels(int width, int height)
            {
                int count = Mathf.Max(1, width * height);
                if (opticalDepth != null && pixelCount >= count) return;
                opticalDepth?.Release(); nearestDepth?.Release(); nearestId?.Release(); nearest64?.Release();
                pixelCount = count;
                nearest64 = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 8) { name = "MetaHuman groom nearest (64-bit)" };
                opticalDepth = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4) { name = "MetaHuman groom optical depth" };
                nearestDepth = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4) { name = "MetaHuman groom nearest depth" };
                nearestId = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4) { name = "MetaHuman groom nearest segment" };
            }

            /// <summary>Mesh buffer wrappers are released once the build has been submitted.</summary>
            public void ReleaseMeshBuffers()
            {
                foreach (var b in meshBuffers) b?.Dispose();
                meshBuffers.Clear();
                for (int i = 0; i < entries.Count; i++) { var e = entries[i]; e.vertices = e.indices = null; entries[i] = e; }
            }

            void ReleaseBuffers()
            {
                ReleaseMeshBuffers();
                points?.Release(); segments?.Release(); meshes?.Release(); meshLengths?.Release(); ranges?.Release(); raised?.Release();
                points = segments = meshes = meshLengths = ranges = raised = null;
            }

            public void Release()
            {
                ReleaseBuffers();
                opticalDepth?.Release(); nearestDepth?.Release(); nearestId?.Release(); nearest64?.Release();
                opticalDepth = nearestDepth = nearestId = nearest64 = null;
                pixelCount = 0; signature = 0;
            }
        }

        sealed class GroomPass : ScriptableRenderPass
        {
            readonly GroomDeferredFeature owner;
            readonly Settings settings;
            public bool compute;
            static readonly ShaderTagId AttributesTag = new ShaderTagId("MetaHumanGroomDeferredAttributes");
            static readonly ShaderTagId OpticalDepthTag = new ShaderTagId("MetaHumanGroomOpticalDepth");
            static readonly int AttributesId = Shader.PropertyToID("_MHGroomDeferredAttributes");
            static readonly int DepthId = Shader.PropertyToID("_MHGroomDeferredDepth");
            static readonly int OpticalDepthId = Shader.PropertyToID("_MHGroomDeferredOpticalDepth");

            public GroomPass(GroomDeferredFeature owner, Settings settings)
            {
                this.owner = owner; this.settings = settings;
                // Same slot as GroomTransparencyFeature: after skin diffusion, before transparents.
                renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents - 2);
                requiresIntermediateTexture = true;
            }

            sealed class DrawData { public RendererListHandle list; }
            sealed class CopyData { public TextureHandle source, destination; }
            sealed class ShadeData
            {
                public TextureHandle attributes, depth, opticalDepth;
                public Material[] materials;
                public int[] passes;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var resources = frame.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var camera = frame.Get<UniversalCameraData>();
                var color = resources.activeColorTexture;
                var colorDesc = color.GetDescriptor(graph);
                if (colorDesc.msaaSamples != MSAASamples.None) return;
                if (compute) RecordCompute(graph, resources, camera, color, colorDesc);
                else RecordHardware(graph, frame, resources, camera, color, colorDesc);
            }

            // ---------------------------------------------------------------- compute raster
            static class Ids
            {
                public static readonly int VertexData = Shader.PropertyToID("_VertexData"), IndexData = Shader.PropertyToID("_IndexData");
                public static readonly int PointsOut = Shader.PropertyToID("_PointsOut"), SegmentsOut = Shader.PropertyToID("_SegmentsOut");
                public static readonly int VertexStride = Shader.PropertyToID("_VertexStride"), PositionOffset = Shader.PropertyToID("_PositionOffset");
                public static readonly int NormalOffset = Shader.PropertyToID("_NormalOffset"), TangentOffset = Shader.PropertyToID("_TangentOffset");
                public static readonly int UV0Offset = Shader.PropertyToID("_UV0Offset"), UV1Offset = Shader.PropertyToID("_UV1Offset"), UV3Offset = Shader.PropertyToID("_UV3Offset");
                public static readonly int HasTangent = Shader.PropertyToID("_HasTangent"), HasUV3 = Shader.PropertyToID("_HasUV3");
                public static readonly int PointCount = Shader.PropertyToID("_PointCount"), PointBase = Shader.PropertyToID("_PointBase"), MeshIndex = Shader.PropertyToID("_MeshIndex");
                public static readonly int SegmentCount = Shader.PropertyToID("_SegmentCount"), SegmentBase = Shader.PropertyToID("_SegmentBase"), Index16 = Shader.PropertyToID("_Index16");
                public static readonly int Points = Shader.PropertyToID("_Points"), Segments = Shader.PropertyToID("_Segments"), Meshes = Shader.PropertyToID("_Meshes");
                public static readonly int OpticalDepth = Shader.PropertyToID("_OpticalDepth"), NearestDepth = Shader.PropertyToID("_NearestDepth"), NearestId = Shader.PropertyToID("_NearestId");
                public static readonly int SceneDepth = Shader.PropertyToID("_SceneDepth");
                public static readonly int ViewMatrix = Shader.PropertyToID("_ViewMatrix"), ViewProjMatrix = Shader.PropertyToID("_ViewProjMatrix");
                public static readonly int CameraPosition = Shader.PropertyToID("_CameraPosition"), ScreenSize = Shader.PropertyToID("_ScreenSize");
                public static readonly int ProjectionScale = Shader.PropertyToID("_ProjectionScale"), Orthographic = Shader.PropertyToID("_Orthographic");
                public static readonly int Diagnostic = Shader.PropertyToID("_Diagnostic");
                public static readonly int SegmentTotal = Shader.PropertyToID("_SegmentTotal"), GroupsX = Shader.PropertyToID("_GroupsX");
                public static readonly int SegmentRanges = Shader.PropertyToID("_SegmentRanges"), RangeCount = Shader.PropertyToID("_RangeCount");
                public static readonly int SegmentRaised = Shader.PropertyToID("_SegmentRaised"), RaisedWords = Shader.PropertyToID("_RaisedWords");
                public static readonly int ShadeOpticalDepth = Shader.PropertyToID("_MHGroomComputeOpticalDepth"), ShadeNearestDepth = Shader.PropertyToID("_MHGroomComputeNearestDepth");
                public static readonly int ShadeNearestId = Shader.PropertyToID("_MHGroomComputeNearestId"), ShadePoints = Shader.PropertyToID("_MHGroomPoints");
                public static readonly int ShadeSegments = Shader.PropertyToID("_MHGroomSegments"), ShadeMeshes = Shader.PropertyToID("_MHGroomMeshes");
                public static readonly int ShadeScreen = Shader.PropertyToID("_MHGroomComputeScreen");
                public static readonly int Nearest64 = Shader.PropertyToID("_Nearest64"), ShadeNearest64 = Shader.PropertyToID("_MHGroomComputeNearest64");
                public static readonly int ShadeMerged = Shader.PropertyToID("_MHGroomComputeMerged");
                public static readonly int ShadeGroup = Shader.PropertyToID("_MHGroomComputeGroup"), ShadeRect = Shader.PropertyToID("_MHGroomComputeRect");
                public static readonly int MeshLengths = Shader.PropertyToID("_MeshLengths"), PointBaseDraw = Shader.PropertyToID("_PointBase");
                public static readonly int LongActive = Shader.PropertyToID("_MHGroomLongActive");
                public static readonly int LongOpticalDepth = Shader.PropertyToID("_MHGroomLongOpticalDepth"), LongId = Shader.PropertyToID("_MHGroomLongId");
                public static readonly int LongDepth = Shader.PropertyToID("_MHGroomLongDepth");
            }

            sealed class ComputeData
            {
                public Geometry geometry;
                public TextureHandle sceneDepth;
                public Matrix4x4 view, viewProj;
                public Vector3 cameraPosition;
                public Vector4 screen;
                public float projectionScale, orthographic;
                public bool build, skipOptical, skipNearest;
                public int diagnostic, pixels;
            }

            sealed class LongData
            {
                public Material material;
                public int pass;
                public MaterialPropertyBlock block;
                public readonly List<Mesh> meshes = new List<Mesh>();
                public readonly List<int> pointBases = new List<int>();
            }

            sealed class ComputeShadeData
            {
                public readonly List<Material> materials = new List<Material>();
                public readonly List<int> passes = new List<int>();
                public readonly List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();
                public bool longActive;
                public TextureHandle longOptical, longId, longDepth;
            }

            Material longMaterial;
            MaterialPropertyBlock longBlock;
            static readonly Plane[] FrustumPlanes = new Plane[6];

            internal Material LongMaterial()
            {
                if (longMaterial) return longMaterial;
                var shader = settings.longSegmentShader ? settings.longSegmentShader : Shader.Find("Hidden/MetaHuman/GroomLongSegments");
                if (shader && shader.isSupported) longMaterial = CoreUtils.CreateEngineMaterial(shader);
                return longMaterial;
            }

            public void Dispose() => CoreUtils.Destroy(longMaterial);

            static void Dispatch(CommandBuffer cmd, ComputeShader cs, int kernel, int items)
            {
                int groups = Mathf.Max(1, (items + 63) / 64);
                int x = Mathf.Min(groups, 65535), y = (groups + x - 1) / x;
                cmd.SetComputeIntParam(cs, Ids.GroupsX, x);
                cmd.DispatchCompute(cs, kernel, x, y, 1);
            }

            void RecordCompute(RenderGraph graph, UniversalResourceData resources, UniversalCameraData camera, TextureHandle color, TextureDesc colorDesc)
            {
                var geometry = owner.geometry;
                geometry.EnsurePixels(colorDesc.width, colorDesc.height);
                Matrix4x4 viewProj;
                float projectionScale;
                using (var builder = graph.AddUnsafePass<ComputeData>("MetaHuman: deferred groom compute raster", out var data))
                {
                    data.geometry = geometry;
                    data.sceneDepth = resources.activeDepthTexture;
                    data.view = camera.GetViewMatrix();
                    // The pass only draws into intermediate textures (never the back buffer): flipped where UVs start at the top.
                    var projection = GL.GetGPUProjectionMatrix(camera.GetProjectionMatrix(), SystemInfo.graphicsUVStartsAtTop);
                    data.viewProj = viewProj = projection * data.view;
                    projectionScale = Mathf.Abs(projection.m11);
                    data.cameraPosition = camera.worldSpaceCameraPos;
                    data.screen = new Vector4(colorDesc.width, colorDesc.height, 1f / colorDesc.width, 1f / colorDesc.height);
                    data.pixels = colorDesc.width * colorDesc.height;
                    data.projectionScale = Mathf.Abs(projection.m11);
                    data.orthographic = camera.camera.orthographic ? 1 : 0;
                    data.build = geometry.needsBuild;
                    geometry.needsBuild = false;
                    data.skipOptical = settings.skipOpticalDepth; data.skipNearest = settings.skipAttributes; data.diagnostic = settings.computeDiagnostic;
                    builder.UseTexture(data.sceneDepth, AccessFlags.Read);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (ComputeData d, UnsafeGraphContext context) =>
                    {
                        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        var g = d.geometry; var cs = g.shader;
                        if (d.build)
                        {
                            int buildPoints = cs.FindKernel("BuildPoints"), buildSegments = cs.FindKernel("BuildSegments");
                            for (int i = 0; i < g.entries.Count; i++)
                            {
                                var e = g.entries[i];
                                cmd.SetComputeBufferParam(cs, buildPoints, Ids.VertexData, e.vertices);
                                cmd.SetComputeBufferParam(cs, buildPoints, Ids.PointsOut, g.points);
                                cmd.SetComputeIntParam(cs, Ids.VertexStride, e.stride);
                                cmd.SetComputeIntParam(cs, Ids.PositionOffset, e.position);
                                cmd.SetComputeIntParam(cs, Ids.NormalOffset, e.normal);
                                cmd.SetComputeIntParam(cs, Ids.TangentOffset, Mathf.Max(0, e.tangent));
                                cmd.SetComputeIntParam(cs, Ids.UV0Offset, e.uv0);
                                cmd.SetComputeIntParam(cs, Ids.UV1Offset, e.uv1);
                                cmd.SetComputeIntParam(cs, Ids.UV3Offset, Mathf.Max(0, e.uv3));
                                cmd.SetComputeIntParam(cs, Ids.HasTangent, e.tangent >= 0 ? 1 : 0);
                                cmd.SetComputeIntParam(cs, Ids.HasUV3, e.uv3 >= 0 ? 1 : 0);
                                cmd.SetComputeIntParam(cs, Ids.PointCount, e.pointCount);
                                cmd.SetComputeIntParam(cs, Ids.PointBase, e.pointBase);
                                cmd.SetComputeIntParam(cs, Ids.MeshIndex, i);
                                cmd.DispatchCompute(cs, buildPoints, Mathf.Max(1, (e.pointCount + 63) / 64), 1, 1);
                                cmd.SetComputeBufferParam(cs, buildSegments, Ids.IndexData, e.indices);
                                cmd.SetComputeBufferParam(cs, buildSegments, Ids.SegmentsOut, g.segments);
                                cmd.SetComputeBufferParam(cs, buildSegments, Ids.VertexData, e.vertices);
                                cmd.SetComputeBufferParam(cs, buildSegments, Ids.MeshLengths, g.meshLengths);
                                cmd.SetComputeIntParam(cs, Ids.SegmentCount, e.segmentCount);
                                cmd.SetComputeIntParam(cs, Ids.SegmentBase, e.segmentBase);
                                cmd.SetComputeIntParam(cs, Ids.Index16, e.index16 ? 1 : 0);
                                cmd.DispatchCompute(cs, buildSegments, Mathf.Max(1, (e.segmentCount + 63) / 64), 1, 1);
                            }
                        }
                        cmd.SetComputeMatrixParam(cs, Ids.ViewMatrix, d.view);
                        cmd.SetComputeMatrixParam(cs, Ids.ViewProjMatrix, d.viewProj);
                        cmd.SetComputeVectorParam(cs, Ids.CameraPosition, d.cameraPosition);
                        cmd.SetComputeVectorParam(cs, Ids.ScreenSize, d.screen);
                        cmd.SetComputeFloatParam(cs, Ids.ProjectionScale, d.projectionScale);
                        cmd.SetComputeFloatParam(cs, Ids.Orthographic, d.orthographic);
                        cmd.SetComputeIntParam(cs, Ids.SegmentTotal, g.activeSegments);
                        cmd.SetComputeIntParam(cs, Ids.RangeCount, g.rangeCount);
                        cmd.SetComputeIntParam(cs, Ids.RaisedWords, g.raisedWords);
                        cmd.SetComputeIntParam(cs, Ids.Diagnostic, d.diagnostic);
                        if (g.shader64 && !d.skipOptical && !d.skipNearest)
                        {
                            var cs64 = g.shader64;
                            cmd.SetComputeMatrixParam(cs64, Ids.ViewMatrix, d.view);
                            cmd.SetComputeMatrixParam(cs64, Ids.ViewProjMatrix, d.viewProj);
                            cmd.SetComputeVectorParam(cs64, Ids.CameraPosition, d.cameraPosition);
                            cmd.SetComputeVectorParam(cs64, Ids.ScreenSize, d.screen);
                            cmd.SetComputeFloatParam(cs64, Ids.ProjectionScale, d.projectionScale);
                            cmd.SetComputeFloatParam(cs64, Ids.Orthographic, d.orthographic);
                            cmd.SetComputeIntParam(cs64, Ids.SegmentTotal, g.activeSegments);
                            cmd.SetComputeIntParam(cs64, Ids.RangeCount, g.rangeCount);
                            int clear64 = cs64.FindKernel("ClearMerged"), merged = cs64.FindKernel("RasterMerged");
                            cmd.SetComputeBufferParam(cs64, clear64, Ids.OpticalDepth, g.opticalDepth);
                            cmd.SetComputeBufferParam(cs64, clear64, Ids.Nearest64, g.nearest64);
                            Dispatch(cmd, cs64, clear64, d.pixels);
                            cmd.SetComputeBufferParam(cs64, merged, Ids.Points, g.points);
                            cmd.SetComputeBufferParam(cs64, merged, Ids.Segments, g.segments);
                            cmd.SetComputeBufferParam(cs64, merged, Ids.Meshes, g.meshes);
                            cmd.SetComputeBufferParam(cs64, merged, Ids.OpticalDepth, g.opticalDepth);
                            cmd.SetComputeBufferParam(cs64, merged, Ids.Nearest64, g.nearest64);
                            cmd.SetComputeTextureParam(cs64, merged, Ids.SceneDepth, (RTHandle)d.sceneDepth);
                            cmd.SetComputeBufferParam(cs64, merged, Ids.SegmentRanges, g.ranges);
                            if (g.activeSegments > 0) Dispatch(cmd, cs64, merged, g.activeSegments);
                            return;
                        }
                        int clear = cs.FindKernel("ClearTargets");
                        cmd.SetComputeBufferParam(cs, clear, Ids.OpticalDepth, g.opticalDepth);
                        cmd.SetComputeBufferParam(cs, clear, Ids.NearestDepth, g.nearestDepth);
                        cmd.SetComputeBufferParam(cs, clear, Ids.NearestId, g.nearestId);
                        cmd.SetComputeBufferParam(cs, clear, Ids.SegmentRaised, g.raised);
                        Dispatch(cmd, cs, clear, Mathf.Max(d.pixels, g.raisedWords));
                        if (g.activeSegments == 0 || d.diagnostic == 7) return;
                        void Raster(string name, int run = 1)
                        {
                            int k = cs.FindKernel(name);
                            cmd.SetComputeBufferParam(cs, k, Ids.Points, g.points);
                            cmd.SetComputeBufferParam(cs, k, Ids.Segments, g.segments);
                            cmd.SetComputeBufferParam(cs, k, Ids.Meshes, g.meshes);
                            cmd.SetComputeBufferParam(cs, k, Ids.OpticalDepth, g.opticalDepth);
                            cmd.SetComputeBufferParam(cs, k, Ids.NearestDepth, g.nearestDepth);
                            cmd.SetComputeBufferParam(cs, k, Ids.NearestId, g.nearestId);
                            cmd.SetComputeTextureParam(cs, k, Ids.SceneDepth, (RTHandle)d.sceneDepth);
                            cmd.SetComputeBufferParam(cs, k, Ids.SegmentRanges, g.ranges);
                            cmd.SetComputeBufferParam(cs, k, Ids.SegmentRaised, g.raised);
                            Dispatch(cmd, cs, k, (g.activeSegments + run - 1) / run);
                        }
                        if (!d.skipOptical && !d.skipNearest) { Raster("RasterOpticalAndDepth"); Raster("RasterNearestId"); }
                        else
                        {
                            if (!d.skipOptical) Raster("RasterOpticalDepth");
                            if (!d.skipNearest) { Raster("RasterNearestDepth"); Raster("RasterNearestId"); }
                        }
                    });
                }

                // Long-segment meshes: hardware draws into their own optical depth and nearest point/depth targets.
                bool longActive = geometry.hardwareEntries > 0 && LongMaterial() && !settings.skipOpticalDepth;
                TextureHandle longOptical = TextureHandle.nullHandle, longId = TextureHandle.nullHandle, longDepth = TextureHandle.nullHandle;
                if (longActive)
                {
                    var depthDesc = resources.activeDepthTexture.GetDescriptor(graph);
                    depthDesc.name = "MetaHuman deferred groom long-segment depth";
                    depthDesc.clearBuffer = false;
                    longDepth = graph.CreateTexture(depthDesc);
                    using (var builder = graph.AddUnsafePass<CopyData>("MetaHuman: copy opaque depth for long groom segments", out var data))
                    {
                        data.source = resources.activeDepthTexture; data.destination = longDepth;
                        builder.UseTexture(data.source, AccessFlags.Read);
                        builder.UseTexture(data.destination, AccessFlags.Write);
                        builder.SetRenderFunc(static (CopyData d, UnsafeGraphContext context) => context.cmd.CopyTexture(d.source, d.destination));
                    }
                    var opticalDesc = colorDesc;
                    opticalDesc.name = "MetaHuman deferred groom long-segment optical depth";
                    opticalDesc.colorFormat = GraphicsFormat.R16_SFloat;
                    opticalDesc.clearBuffer = true; opticalDesc.clearColor = Color.clear;
                    opticalDesc.depthBufferBits = DepthBits.None; opticalDesc.filterMode = FilterMode.Point;
                    longOptical = graph.CreateTexture(opticalDesc);
                    var idDesc = opticalDesc;
                    idDesc.name = "MetaHuman deferred groom long-segment nearest";
                    idDesc.colorFormat = GraphicsFormat.R32_UInt;
                    longId = graph.CreateTexture(idDesc);

                    longBlock ??= new MaterialPropertyBlock();
                    longBlock.Clear();
                    longBlock.SetBuffer(Ids.Points, geometry.points);
                    longBlock.SetBuffer(Ids.Meshes, geometry.meshes);
                    longBlock.SetMatrix(Ids.ViewMatrix, camera.GetViewMatrix());
                    longBlock.SetMatrix(Ids.ViewProjMatrix, viewProj);
                    longBlock.SetVector(Ids.CameraPosition, camera.worldSpaceCameraPos);
                    longBlock.SetVector(Ids.ScreenSize, new Vector4(colorDesc.width, colorDesc.height, 1f / colorDesc.width, 1f / colorDesc.height));
                    longBlock.SetFloat(Ids.ProjectionScale, projectionScale);
                    longBlock.SetFloat(Ids.Orthographic, camera.camera.orthographic ? 1 : 0);
                    GeometryUtility.CalculateFrustumPlanes(viewProj, FrustumPlanes);
                    for (int p = 0; p < 2; p++)
                    {
                        if ((p == 0 && settings.computeDiagnostic == 5) || (p == 1 && settings.computeDiagnostic == 6)) continue;
                        using (var builder = graph.AddRasterRenderPass<LongData>(p == 0 ? "MetaHuman: long groom segments optical depth" : "MetaHuman: long groom segments nearest", out var data))
                        {
                            data.material = longMaterial; data.pass = p; data.block = longBlock;
                            data.meshes.Clear(); data.pointBases.Clear();
                            for (int i = 0; i < geometry.entries.Count; i++)
                            {
                                var e = geometry.entries[i];
                                if (geometry.meshData[i].hardware == 0 || !e.mesh || !e.renderer || !GeometryUtility.TestPlanesAABB(FrustumPlanes, e.renderer.bounds)) continue;
                                data.meshes.Add(e.mesh); data.pointBases.Add(e.pointBase);
                            }
                            builder.SetRenderAttachment(p == 0 ? longOptical : longId, 0, AccessFlags.Write);
                            builder.SetRenderAttachmentDepth(longDepth, p == 0 ? AccessFlags.Read : AccessFlags.ReadWrite);
                            builder.AllowPassCulling(false);
                            builder.SetRenderFunc(static (LongData d, RasterGraphContext context) =>
                            {
                                for (int i = 0; i < d.meshes.Count; i++)
                                {
                                    d.block.SetInteger(Ids.PointBaseDraw, d.pointBases[i]);
                                    context.cmd.DrawMesh(d.meshes[i], Matrix4x4.identity, d.material, 0, d.pass, d.block);
                                }
                            });
                        }
                    }
                }

                if (settings.skipShade) return;
                using (var builder = graph.AddRasterRenderPass<ComputeShadeData>("MetaHuman: deferred groom shade and composite (compute)", out var data))
                {
                    bool merged = geometry.shader64 && !settings.skipOpticalDepth && !settings.skipAttributes;
                    var screen = new Vector4(colorDesc.width, colorDesc.height, 0, 0);
                    data.materials.Clear(); data.passes.Clear(); data.blocks.Clear();
                    owner.UpdateGroupRects(viewProj, colorDesc.width, colorDesc.height);
                    for (int i = 0; i < owner.groups.Count; i++)
                    {
                        var group = owner.groups[i];
                        if (!group.material || group.rect.z <= group.rect.x || group.rect.w <= group.rect.y) continue;
                        // The group's renderer properties (e.g. GroomShadowVolume's), then the deferred inputs.
                        var block = group.block;
                        block.Clear();
                        if (group.representative) { group.representative.GetPropertyBlock(block, 0); if (block.isEmpty) group.representative.GetPropertyBlock(block); }
                        block.SetBuffer(Ids.ShadeOpticalDepth, geometry.opticalDepth);
                        block.SetBuffer(Ids.ShadeNearestDepth, geometry.nearestDepth);
                        block.SetBuffer(Ids.ShadeNearestId, geometry.nearestId);
                        block.SetBuffer(Ids.ShadePoints, geometry.points);
                        block.SetBuffer(Ids.ShadeSegments, geometry.segments);
                        block.SetBuffer(Ids.ShadeMeshes, geometry.meshes);
                        block.SetBuffer(Ids.ShadeNearest64, geometry.nearest64);
                        block.SetVector(Ids.ShadeScreen, screen);
                        block.SetFloat(Ids.ShadeMerged, merged ? 1 : 0);
                        block.SetFloat(Ids.ShadeGroup, i);
                        block.SetVector(Ids.ShadeRect, group.rect);
                        data.materials.Add(group.material); data.passes.Add(group.material.FindPass(ComputeShadePassName)); data.blocks.Add(block);
                    }
                    owner.lastRecordedMaterials = data.materials.Count;
                    data.longActive = longActive; data.longOptical = longOptical; data.longId = longId; data.longDepth = longDepth;
                    if (longActive)
                    {
                        builder.UseTexture(longOptical, AccessFlags.Read);
                        builder.UseTexture(longId, AccessFlags.Read);
                        builder.UseTexture(longDepth, AccessFlags.Read);
                    }
                    builder.UseAllGlobalTextures(true);
                    if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);
                    if (resources.additionalShadowsTexture.IsValid()) builder.UseTexture(resources.additionalShadowsTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(color, 0, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (ComputeShadeData d, RasterGraphContext context) =>
                    {
                        for (int i = 0; i < d.materials.Count; i++)
                        {
                            var block = d.blocks[i];
                            block.SetFloat(Ids.LongActive, d.longActive ? 1 : 0);
                            if (d.longActive)
                            {
                                block.SetTexture(Ids.LongOpticalDepth, (RTHandle)d.longOptical);
                                block.SetTexture(Ids.LongId, (RTHandle)d.longId);
                                block.SetTexture(Ids.LongDepth, (RTHandle)d.longDepth);
                            }
                            context.cmd.DrawProcedural(Matrix4x4.identity, d.materials[i], d.passes[i], MeshTopology.Triangles, 6, 1, block);
                        }
                    });
                }
            }

            // ---------------------------------------------------------------- hardware raster
            void RecordHardware(RenderGraph graph, ContextContainer frame, UniversalResourceData resources, UniversalCameraData camera,
                TextureHandle color, TextureDesc colorDesc)
            {
                var rendering = frame.Get<UniversalRenderingData>();
                var lights = frame.Get<UniversalLightData>();
                var filtering = new FilteringSettings(RenderQueueRange.opaque, settings.layers);

                var depthDesc = resources.activeDepthTexture.GetDescriptor(graph);
                depthDesc.name = "MetaHuman deferred groom depth";
                depthDesc.clearBuffer = false;
                var groomDepth = graph.CreateTexture(depthDesc);
                using (var builder = graph.AddUnsafePass<CopyData>("MetaHuman: copy opaque depth for deferred groom", out var data))
                {
                    data.source = resources.activeDepthTexture; data.destination = groomDepth;
                    builder.UseTexture(data.source, AccessFlags.Read);
                    builder.UseTexture(data.destination, AccessFlags.Write);
                    builder.SetRenderFunc(static (CopyData d, UnsafeGraphContext context) => context.cmd.CopyTexture(d.source, d.destination));
                }

                var attributesDesc = colorDesc;
                attributesDesc.name = "MetaHuman deferred groom attributes";
                attributesDesc.colorFormat = GraphicsFormat.R32G32B32A32_UInt;
                attributesDesc.clearBuffer = true; attributesDesc.clearColor = Color.clear;
                attributesDesc.depthBufferBits = DepthBits.None; attributesDesc.filterMode = FilterMode.Point;
                var attributes = graph.CreateTexture(attributesDesc);
                var attributeDrawing = RenderingUtils.CreateDrawingSettings(AttributesTag, rendering, camera, lights, SortingCriteria.CommonOpaque);
                var attributeList = graph.CreateRendererList(new RendererListParams(rendering.cullResults, attributeDrawing, filtering));
                if (!settings.skipAttributes)
                using (var builder = graph.AddRasterRenderPass<DrawData>("MetaHuman: deferred groom attributes", out var data))
                {
                    data.list = attributeList;
                    builder.UseRendererList(attributeList);
                    builder.SetRenderAttachment(attributes, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(groomDepth, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (DrawData d, RasterGraphContext context) => context.cmd.DrawRendererList(d.list));
                }

                var opticalDesc = colorDesc;
                opticalDesc.name = "MetaHuman deferred groom optical depth";
                opticalDesc.colorFormat = GraphicsFormat.R16_SFloat;
                opticalDesc.clearBuffer = true; opticalDesc.clearColor = Color.clear;
                opticalDesc.depthBufferBits = DepthBits.None;
                var opticalDepth = graph.CreateTexture(opticalDesc);
                var opticalDrawing = RenderingUtils.CreateDrawingSettings(OpticalDepthTag, rendering, camera, lights, SortingCriteria.None);
                var opticalList = graph.CreateRendererList(new RendererListParams(rendering.cullResults, opticalDrawing, filtering));
                if (!settings.skipOpticalDepth)
                using (var builder = graph.AddRasterRenderPass<DrawData>("MetaHuman: deferred groom optical depth", out var data))
                {
                    data.list = opticalList;
                    builder.UseRendererList(opticalList);
                    builder.SetRenderAttachment(opticalDepth, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.SetRenderFunc(static (DrawData d, RasterGraphContext context) => context.cmd.DrawRendererList(d.list));
                }

                if (settings.skipShade) return;
                using (var builder = graph.AddRasterRenderPass<ShadeData>("MetaHuman: deferred groom shade and composite", out var data))
                {
                    data.attributes = attributes; data.depth = groomDepth; data.opticalDepth = opticalDepth;
                    data.materials = owner.materials.ToArray();
                    data.passes = System.Array.ConvertAll(data.materials, mat => mat.FindPass(ShadePassName));
                    owner.lastRecordedMaterials = data.materials.Length;
                    builder.UseTexture(attributes, AccessFlags.Read);
                    builder.UseTexture(groomDepth, AccessFlags.Read);
                    builder.UseTexture(opticalDepth, AccessFlags.Read);
                    builder.UseAllGlobalTextures(true);
                    if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);
                    if (resources.additionalShadowsTexture.IsValid()) builder.UseTexture(resources.additionalShadowsTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(color, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (ShadeData d, RasterGraphContext context) =>
                    {
                        var block = context.renderGraphPool.GetTempMaterialPropertyBlock();
                        block.SetTexture(AttributesId, (RTHandle)d.attributes);
                        block.SetTexture(DepthId, (RTHandle)d.depth);
                        block.SetTexture(OpticalDepthId, (RTHandle)d.opticalDepth);
                        for (int i = 0; i < d.materials.Length; i++)
                            if (d.materials[i] && d.passes[i] >= 0)
                                context.cmd.DrawProcedural(Matrix4x4.identity, d.materials[i], d.passes[i], MeshTopology.Triangles, 3, 1, block);
                    });
                }
            }
        }
    }
}

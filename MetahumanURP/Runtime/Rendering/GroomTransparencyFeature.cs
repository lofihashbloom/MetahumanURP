using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace MetaHuman.Rendering
{
    /// <summary>Continuous, order-independent optical coverage for fine groom ribbons.</summary>
    public sealed class GroomTransparencyFeature : ScriptableRendererFeature
    {
        public enum VisibilityMode { WeightedAll = 0, NearestLayer = 1 }
        /// <summary>How NearestLayer picks the nearest strand. Auto uses the depth test only on multisampled targets,
        /// where it is measured faster; single-sample targets keep the shader path, which draws one geometry pass fewer.</summary>
        public enum NearestSelection { Auto = 0, Shader = 1, DepthTest = 2 }
        [Serializable] public sealed class Settings
        {
            public LayerMask layers = -1;
            public Shader shader;
            [Tooltip("Nearest expanded ribbon depth anchors radiance, with optional material depth weighting; optical coverage includes all strands. This is not native hair visibility.")]
            public VisibilityMode visibilityMode = VisibilityMode.WeightedAll;
            [Tooltip("Diagnostic white groom contribution over the existing background. This displays composite coverage, not stored camera alpha.")]
            public bool debugCoverage;
            [Tooltip("Feed selected static/rigid ribbon depth and velocity into URP TAA. Requires single-sample TAA, the nearest-layer visibility mode, and continuous real frames. Deforming strand positions are not supported.")]
            public bool temporalReconstruction;
            [Tooltip("NearestLayer: write all-strand optical depth and nearest-layer depth in one geometry pass (per-target add/min blending) instead of two. Requires groom shaders with the MetaHumanGroomOpticalDepthNearest pass.")]
            public bool singlePassNearestDepth = true;
            [Tooltip("NearestLayer strand selection. DepthTest: a depth prepass into a copy of the camera depth lets the hardware depth test pick the nearest strand, so only that layer is shaded and MSAA coverage comes from per-sample depth (hard selection only: material radiance depth bands are ignored; needs the MetaHumanGroomNearestDepthPrepass, MetaHumanGroomOpticalDepth and MetaHumanGroomNearestRadianceDepthTested passes). Shader: compare against a nearest-depth texture. Auto: DepthTest on multisampled targets, Shader otherwise. Temporal reconstruction always uses Shader.")]
            public NearestSelection nearestSelection = NearestSelection.Auto;
        }
        public Settings settings = new Settings();
        public int lastRecordedSamples { get; private set; }
        public string lastRecordedCameraId { get; private set; }
        public int lastRecordedCompositePass { get; private set; }
        public int lastRecordedVisibilityMode { get; private set; }
        public bool lastRecordedDebugCoverage { get; private set; }
        public bool lastRecordedHardwareSelection { get; private set; }
        public int recordSequence { get; private set; }
        public int temporalRecordSequence { get; private set; }
        public int lastTemporalFrame { get; private set; } = -1;
        public string lastTemporalCameraId { get; private set; }
        Material material;
        GroomPass pass;
        TemporalPass temporalPass;
        public override void Create()
        {
            CoreUtils.Destroy(material);
            var shader = settings.shader ? settings.shader : Shader.Find("Hidden/MetaHuman/GroomTransparency");
            material = shader ? CoreUtils.CreateEngineMaterial(shader) : null;
            pass = new GroomPass(this, settings, material);
            temporalPass = new TemporalPass(this, settings);
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData;
            if (!material || camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection ||
                camera.camera.stereoEnabled || camera.renderType != CameraRenderType.Base) return;
            renderer.EnqueuePass(pass);
            if (WantsTemporal(settings, camera.camera, camera.antialiasing, camera.postProcessEnabled,
                camera.cameraTargetDescriptor.msaaSamples))
                renderer.EnqueuePass(temporalPass);
        }
        protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);

        static bool WantsTemporal(Settings settings, Camera camera, AntialiasingMode aa, bool post, int samples)
        {
            return settings.temporalReconstruction && settings.visibilityMode == VisibilityMode.NearestLayer &&
                !settings.debugCoverage && camera.cameraType == CameraType.Game && !camera.stereoEnabled &&
                aa == AntialiasingMode.TemporalAntiAliasing && post && samples == 1 && !camera.allowDynamicResolution &&
                camera.TryGetComponent<UniversalAdditionalCameraData>(out var additional) &&
                additional.renderType == CameraRenderType.Base && additional.cameraStack.Count == 0;
        }

        // These graph handles belong exclusively to the current frame/camera.
        sealed class TemporalResources : ContextItem
        {
            public TextureHandle depth, opticalDepth;
            public override void Reset() { depth = opticalDepth = TextureHandle.nullHandle; }
        }

        sealed class TemporalPass : ScriptableRenderPass
        {
            readonly GroomTransparencyFeature owner;
            readonly Settings settings;
            static readonly ShaderTagId Tag = new ShaderTagId("MetaHumanGroomTemporal");
            sealed class Data { public RendererListHandle list; }
            public TemporalPass(GroomTransparencyFeature owner, Settings settings)
            {
                this.owner = owner; this.settings = settings;
                // Run after transparent surfaces and the pipeline's own motion pass.
                // TAA in URP 17.6 reads cameraDepth, not cameraDepthTexture.
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Motion);
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var inputs = frame.GetOrCreate<TemporalResources>();
                var resources = frame.Get<UniversalResourceData>();
                var camera = frame.Get<UniversalCameraData>();
                if (!inputs.depth.IsValid() || !inputs.opticalDepth.IsValid() ||
                    !resources.motionVectorColor.IsValid() || !resources.cameraDepth.IsValid() ||
                    resources.cameraDepth.GetDescriptor(graph).msaaSamples != MSAASamples.None) return;
                var rendering = frame.Get<UniversalRenderingData>();
                var lights = frame.Get<UniversalLightData>();
                var drawing = RenderingUtils.CreateDrawingSettings(Tag, rendering, camera, lights, SortingCriteria.None);
                drawing.perObjectData |= PerObjectData.MotionVectors;
                var filtering = new FilteringSettings(RenderQueueRange.opaque, settings.layers & ~GroomDeferredFeature.exclusiveLayersThisCamera);
                var list = graph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));
                using (var builder = graph.AddRasterRenderPass<Data>("MetaHuman: groom depth and rigid velocity for TAA", out var data))
                {
                    data.list = list;
                    builder.UseRendererList(list);
                    builder.UseTexture(inputs.depth, AccessFlags.Read);
                    builder.UseTexture(inputs.opticalDepth, AccessFlags.Read);
                    // Declare only groom inputs. UseAllGlobalTextures would also
                    // read URP's globally published motion target while writing it.
                    builder.SetRenderAttachment(resources.motionVectorColor, 0, AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(resources.cameraDepth, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (Data d, RasterGraphContext context) => context.cmd.DrawRendererList(d.list));
                }
                owner.temporalRecordSequence++;
                owner.lastTemporalFrame = Time.frameCount;
                owner.lastTemporalCameraId = camera.camera.GetEntityId().ToString();
            }
        }

        sealed class GroomPass : ScriptableRenderPass
        {
            readonly Settings settings;
            readonly GroomTransparencyFeature owner;
            readonly Material material;
            readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
            static readonly ShaderTagId Tag = new ShaderTagId("MetaHumanGroomAccumulation");
            static readonly ShaderTagId NearestDepthTag = new ShaderTagId("MetaHumanGroomNearestDepth");
            static readonly ShaderTagId NearestRadianceTag = new ShaderTagId("MetaHumanGroomNearestRadiance");
            static readonly ShaderTagId NearestRadianceMsTag = new ShaderTagId("MetaHumanGroomNearestRadianceMS");
            static readonly ShaderTagId OpticalDepthNearestTag = new ShaderTagId("MetaHumanGroomOpticalDepthNearest");
            static readonly ShaderTagId DepthPrepassTag = new ShaderTagId("MetaHumanGroomNearestDepthPrepass");
            static readonly ShaderTagId OpticalDepthTag = new ShaderTagId("MetaHumanGroomOpticalDepth");
            static readonly ShaderTagId RadianceDepthTestedTag = new ShaderTagId("MetaHumanGroomNearestRadianceDepthTested");
            static readonly int AccumulationId = Shader.PropertyToID("_MHGroomAccumulation");
            static readonly int OpticalDepthId = Shader.PropertyToID("_MHGroomOpticalDepth");
            // This global is single-sample only. Never reuse the composite's
            // property name: that shader also declares a Texture2DMS variant.
            static readonly int TemporalOpticalDepthId = Shader.PropertyToID("_MHGroomTemporalOpticalDepth");
            static readonly int NearestDepthId = Shader.PropertyToID("_MHGroomNearestDepth");
            static readonly int NearestDepthMsId = Shader.PropertyToID("_MHGroomNearestDepthMS");
            static readonly int DebugCoverageId = Shader.PropertyToID("_MHGroomDebugCoverage");
            static readonly int OpticalDepthOnlyId = Shader.PropertyToID("_MHGroomOpticalDepthOnly");
            public GroomPass(GroomTransparencyFeature owner, Settings settings, Material material)
            {
                this.owner = owner; this.settings = settings; this.material = material;
                // Install after skin diffusion in the renderer-feature list. Both run
                // before ordinary transparent surfaces and camera post-processing.
                renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents - 2);
                requiresIntermediateTexture = true;
            }
            sealed class DrawData { public RendererListHandle list; public float opticalDepthOnly; }
            sealed class CompositeData
            {
                public TextureHandle accumulation, opticalDepth;
                public int pass;
                public Material material;
                public MaterialPropertyBlock properties;
                public bool debugCoverage;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var resources = frame.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var camera = frame.Get<UniversalCameraData>();
                var rendering = frame.Get<UniversalRenderingData>();
                var lights = frame.Get<UniversalLightData>();
                var color = resources.activeColorTexture;
                var desc = color.GetDescriptor(graph);
                bool multisampled = desc.msaaSamples != MSAASamples.None;
                owner.lastRecordedSamples = (int)desc.msaaSamples;
                owner.lastRecordedCameraId = camera.camera.GetEntityId().ToString();
                owner.lastRecordedCompositePass = multisampled ? 1 : 0;
                owner.lastRecordedVisibilityMode = (int)settings.visibilityMode;
                owner.lastRecordedDebugCoverage = settings.debugCoverage;
                owner.recordSequence++;
                desc.name = "MetaHuman groom weighted radiance";
                desc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                desc.clearBuffer = true; desc.clearColor = Color.clear;
                desc.depthBufferBits = DepthBits.None; desc.bindTextureMS = multisampled;
                var accumulation = graph.CreateTexture(desc);
                desc.name = "MetaHuman groom optical depth"; desc.colorFormat = GraphicsFormat.R16_SFloat;
                var opticalDepth = graph.CreateTexture(desc);
                bool temporal = WantsTemporal(settings, camera.camera, camera.antialiasing,
                    camera.postProcessEnabled, (int)desc.msaaSamples);
                bool nearestMode = settings.visibilityMode == VisibilityMode.NearestLayer;
                bool singlePass = nearestMode && settings.singlePassNearestDepth;
                var filtering = new FilteringSettings(RenderQueueRange.opaque, settings.layers & ~GroomDeferredFeature.exclusiveLayersThisCamera);
                // TAA reconstruction consumes the nearest-depth texture, so it keeps the shader-selected path.
                bool hardware = nearestMode && !temporal &&
                    (settings.nearestSelection == NearestSelection.DepthTest || (settings.nearestSelection == NearestSelection.Auto && multisampled)) &&
                    SystemInfo.copyTextureSupport != CopyTextureSupport.None;
                owner.lastRecordedHardwareSelection = hardware;
                if (hardware)
                {
                    var selected = RecordHardwareNearest(graph, resources, rendering, camera, lights, filtering, accumulation, opticalDepth);
                    RecordComposite(graph, color, selected, opticalDepth, multisampled);
                    return;
                }
                TextureHandle nearestDepth = TextureHandle.nullHandle;
                if (nearestMode)
                {
                    // This diagnostic selects the nearest expanded raster ribbon,
                    // while retaining the all-strand transmittance. It is not
                    // a reconstruction of native control-point/sample visibility.
                    var nearestDesc = color.GetDescriptor(graph);
                    nearestDesc.name = "MetaHuman groom nearest linear eye depth";
                    nearestDesc.colorFormat = GraphicsFormat.R32_SFloat;
                    nearestDesc.clearBuffer = true;
                    nearestDesc.clearColor = new Color(float.MaxValue, 0, 0, 0);
                    nearestDesc.depthBufferBits = DepthBits.None;
                    nearestDesc.bindTextureMS = multisampled;
                    nearestDepth = graph.CreateTexture(nearestDesc);
                }
                if (singlePass)
                {
                    // One geometry pass: optical depth (added) and nearest depth (min) for every strand.
                    var mergedDrawing = RenderingUtils.CreateDrawingSettings(OpticalDepthNearestTag, rendering, camera, lights, SortingCriteria.None);
                    var mergedList = graph.CreateRendererList(new RendererListParams(rendering.cullResults, mergedDrawing, filtering));
                    using (var builder = graph.AddRasterRenderPass<DrawData>("MetaHuman: groom optical depth and nearest layer depth", out var data))
                    {
                        data.list = mergedList;
                        builder.UseRendererList(mergedList);
                        builder.SetRenderAttachment(opticalDepth, 0, AccessFlags.Write);
                        builder.SetRenderAttachment(nearestDepth, 1, AccessFlags.Write);
                        builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                        if (temporal) builder.SetGlobalTextureAfterPass(opticalDepth, TemporalOpticalDepthId);
                        builder.SetGlobalTextureAfterPass(nearestDepth, multisampled ? NearestDepthMsId : NearestDepthId);
                        builder.SetRenderFunc(static (DrawData d, RasterGraphContext context) => context.cmd.DrawRendererList(d.list));
                    }
                }
                else
                {
                var drawing = RenderingUtils.CreateDrawingSettings(Tag, rendering, camera, lights, SortingCriteria.None);
                var list = graph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));
                using (var builder = graph.AddRasterRenderPass<DrawData>("MetaHuman: continuous groom accumulation", out var data))
                {
                    data.list = list;
                    builder.UseRendererList(list);
                    builder.UseAllGlobalTextures(true);
                    if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);
                    if (resources.additionalShadowsTexture.IsValid()) builder.UseTexture(resources.additionalShadowsTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(accumulation, 0, AccessFlags.Write);
                    builder.SetRenderAttachment(opticalDepth, 1, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    if (temporal) builder.SetGlobalTextureAfterPass(opticalDepth, TemporalOpticalDepthId);
                    // NearestLayer replaces this pass's radiance with the selected layer, so only optical depth is needed.
                    data.opticalDepthOnly = settings.visibilityMode == VisibilityMode.NearestLayer ? 1 : 0;
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (DrawData d, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalFloat(OpticalDepthOnlyId, d.opticalDepthOnly);
                        context.cmd.DrawRendererList(d.list);
                        context.cmd.SetGlobalFloat(OpticalDepthOnlyId, 0);
                    });
                }
                }
                var compositeAccumulation = accumulation;
                if (nearestMode)
                {
                    if (temporal)
                    {
                        var temporalInputs = frame.GetOrCreate<TemporalResources>();
                        temporalInputs.depth = nearestDepth;
                        temporalInputs.opticalDepth = opticalDepth;
                    }
                    if (!singlePass)
                    {
                    var depthDrawing = RenderingUtils.CreateDrawingSettings(multisampled ? new ShaderTagId("MetaHumanGroomNearestDepthMS") : NearestDepthTag, rendering, camera, lights, SortingCriteria.None);
                    var depthList = graph.CreateRendererList(new RendererListParams(rendering.cullResults, depthDrawing, filtering));
                    using (var builder = graph.AddRasterRenderPass<DrawData>("MetaHuman: nearest groom layer depth diagnostic", out var data))
                    {
                        data.list = depthList;
                        builder.UseRendererList(depthList);
                        builder.SetRenderAttachment(nearestDepth, 0, AccessFlags.Write);
                        builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                        // Renderer-list fragment shaders need graph-published global
                        // binding. The selected pass also declares the direct read.
                        builder.SetGlobalTextureAfterPass(nearestDepth, multisampled ? NearestDepthMsId : NearestDepthId);
                        builder.SetRenderFunc(static (DrawData d, RasterGraphContext context) => context.cmd.DrawRendererList(d.list));
                    }
                    }
                    var selectedDesc = accumulation.GetDescriptor(graph);
                    selectedDesc.name = "MetaHuman groom nearest-layer weighted radiance diagnostic";
                    var selectedRadiance = graph.CreateTexture(selectedDesc);
                    var selectedDrawing = RenderingUtils.CreateDrawingSettings(multisampled ? NearestRadianceMsTag : NearestRadianceTag, rendering, camera, lights, SortingCriteria.None);
                    var selectedList = graph.CreateRendererList(new RendererListParams(rendering.cullResults, selectedDrawing, filtering));
                    using (var builder = graph.AddRasterRenderPass<DrawData>("MetaHuman: nearest groom layer radiance diagnostic", out var data))
                    {
                        data.list = selectedList;
                        builder.UseRendererList(selectedList);
                        builder.UseTexture(nearestDepth, AccessFlags.Read);
                        builder.UseAllGlobalTextures(true);
                        if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);
                        if (resources.additionalShadowsTexture.IsValid()) builder.UseTexture(resources.additionalShadowsTexture, AccessFlags.Read);
                        builder.SetRenderAttachment(selectedRadiance, 0, AccessFlags.Write);
                        builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                        builder.SetRenderFunc(static (DrawData d, RasterGraphContext context) => context.cmd.DrawRendererList(d.list));
                    }
                    compositeAccumulation = selectedRadiance;
                }
                RecordComposite(graph, color, compositeAccumulation, opticalDepth, multisampled);
            }

            sealed class CopyData { public TextureHandle source, destination; }

            // Nearest layer selected by the depth test: copy opaque depth, write the nearest strand per sample
            // (depth only), accumulate all-strand optical depth, then shade only fragments that pass LEqual against
            // the stored nearest depth. Hard selection only (materials' radiance depth band is not applied).
            TextureHandle RecordHardwareNearest(RenderGraph graph, UniversalResourceData resources, UniversalRenderingData rendering,
                UniversalCameraData camera, UniversalLightData lights, FilteringSettings filtering, TextureHandle accumulation, TextureHandle opticalDepth)
            {
                var depthDesc = resources.activeDepthTexture.GetDescriptor(graph);
                depthDesc.name = "MetaHuman groom nearest-layer depth";
                depthDesc.clearBuffer = false;
                var groomDepth = graph.CreateTexture(depthDesc);
                using (var builder = graph.AddUnsafePass<CopyData>("MetaHuman: copy opaque depth for groom selection", out var data))
                {
                    data.source = resources.activeDepthTexture; data.destination = groomDepth;
                    builder.UseTexture(data.source, AccessFlags.Read);
                    builder.UseTexture(data.destination, AccessFlags.Write);
                    builder.SetRenderFunc(static (CopyData d, UnsafeGraphContext context) => context.cmd.CopyTexture(d.source, d.destination));
                }
                var prepassDrawing = RenderingUtils.CreateDrawingSettings(DepthPrepassTag, rendering, camera, lights, SortingCriteria.CommonOpaque);
                var prepassList = graph.CreateRendererList(new RendererListParams(rendering.cullResults, prepassDrawing, filtering));
                using (var builder = graph.AddRasterRenderPass<DrawData>("MetaHuman: groom nearest-layer depth prepass", out var data))
                {
                    data.list = prepassList;
                    builder.UseRendererList(prepassList);
                    builder.SetRenderAttachmentDepth(groomDepth, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (DrawData d, RasterGraphContext context) => context.cmd.DrawRendererList(d.list));
                }
                var opticalDrawing = RenderingUtils.CreateDrawingSettings(OpticalDepthTag, rendering, camera, lights, SortingCriteria.None);
                var opticalList = graph.CreateRendererList(new RendererListParams(rendering.cullResults, opticalDrawing, filtering));
                using (var builder = graph.AddRasterRenderPass<DrawData>("MetaHuman: groom optical depth", out var data))
                {
                    data.list = opticalList;
                    builder.UseRendererList(opticalList);
                    builder.SetRenderAttachment(opticalDepth, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.SetRenderFunc(static (DrawData d, RasterGraphContext context) => context.cmd.DrawRendererList(d.list));
                }
                var selectedDesc = accumulation.GetDescriptor(graph);
                selectedDesc.name = "MetaHuman groom nearest-layer radiance";
                var selectedRadiance = graph.CreateTexture(selectedDesc);
                var radianceDrawing = RenderingUtils.CreateDrawingSettings(RadianceDepthTestedTag, rendering, camera, lights, SortingCriteria.None);
                var radianceList = graph.CreateRendererList(new RendererListParams(rendering.cullResults, radianceDrawing, filtering));
                using (var builder = graph.AddRasterRenderPass<DrawData>("MetaHuman: groom nearest-layer radiance (depth tested)", out var data))
                {
                    data.list = radianceList;
                    builder.UseRendererList(radianceList);
                    builder.UseAllGlobalTextures(true);
                    if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);
                    if (resources.additionalShadowsTexture.IsValid()) builder.UseTexture(resources.additionalShadowsTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(selectedRadiance, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(groomDepth, AccessFlags.Read);
                    builder.SetRenderFunc(static (DrawData d, RasterGraphContext context) => context.cmd.DrawRendererList(d.list));
                }
                return selectedRadiance;
            }

            void RecordComposite(RenderGraph graph, TextureHandle color, TextureHandle compositeAccumulation, TextureHandle opticalDepth, bool multisampled)
            {
                using (var builder = graph.AddRasterRenderPass<CompositeData>("MetaHuman: groom transmittance composite", out var data))
                {
                    data.accumulation = compositeAccumulation; data.opticalDepth = opticalDepth;
                    data.pass = multisampled ? 1 : 0;
                    data.material = material; data.properties = properties;
                    data.debugCoverage = settings.debugCoverage;
                    builder.UseTexture(compositeAccumulation, AccessFlags.Read);
                    builder.UseTexture(opticalDepth, AccessFlags.Read);
                    // Hardware blending preserves each camera sample and its alpha.
                    // No sampled camera-color feedback or premature tau resolve.
                    builder.SetRenderAttachment(color, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (CompositeData d, RasterGraphContext context) =>
                    {
                        d.properties.Clear();
                        d.properties.SetFloat(DebugCoverageId, d.debugCoverage ? 1 : 0);
                        d.properties.SetTexture(AccumulationId, (RTHandle)d.accumulation);
                        d.properties.SetTexture(OpticalDepthId, (RTHandle)d.opticalDepth);
                        context.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.pass, MeshTopology.Triangles, 3, 1, d.properties);
                    });
                }
            }
        }
    }
}

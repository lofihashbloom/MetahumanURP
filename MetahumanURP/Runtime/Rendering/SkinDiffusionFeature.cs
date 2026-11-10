using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace MetaHuman.Rendering
{
    /// <summary>Depth/normal/mask-aware separable diffusion of skin diffuse lighting only.</summary>
    public sealed class SkinDiffusionFeature : ScriptableRendererFeature
    {
        [Serializable] public sealed class Settings
        {
            [Range(0, 1)] public float strength = 0.65f;
            [Range(0, 1)] public float albedoPreservation = 1;
            public bool useProfileLengths;
            public bool normalizedDiffusion;
            public Vector3 scatterDistanceMillimetres = new Vector3(.1f, .03f, .015f);
            public Vector3 meanFreePathMillimetres = Vector3.one;
            [Range(0.1f, 10)] public float radiusMillimetres = 3.0f;
            [Range(0.1f, 10)] public float depthRejectionMillimetres = 1.5f;
            [Range(0, 1)] public float normalRejection = 0.6f;
            [Tooltip("Single-draw path: skin is drawn once, after the opaques, writing its lit colour and the diffusion inputs together; its forward pass culls itself. Off: the forward pass plus a second, diffuse-only draw. Only with layers = Everything (otherwise the second draw is used).")]
            public bool singleDraw = true;
            [Tooltip("Add the diffusion change onto the camera colour (keeps MSAA samples, no full-screen colour copy). Off: composite into a new copy of the camera colour.")]
            public bool additiveComposite = true;
            public LayerMask layers = -1;
            // Serialize a shader reference so player shader stripping preserves the fullscreen passes.
            public Shader shader;
        }

        public Settings settings = new Settings();
        /// <summary>Whether the last recorded camera used the single skin draw (diagnostics and tests).</summary>
        public bool lastRecordedSingleDraw { get; private set; }
        Material material;
        DiffusionPass pass;
        SingleDrawFlagPass enableSingleDraw;
        static readonly int SingleDrawId = Shader.PropertyToID("_MHSkinSingleDraw");

        static readonly SharedCameraHook cameraHook = new SharedCameraHook(ResetSingleDraw);
        [System.NonSerialized] bool cameraHookHeld;

        public override void Create()
        {
            cameraHook.Acquire(ref cameraHookHeld);
            CoreUtils.Destroy(material);
            var shader = settings.shader ? settings.shader : Shader.Find("Hidden/MetaHuman/SkinDiffusion");
            material = shader ? CoreUtils.CreateEngineMaterial(shader) : null;
            pass = new DiffusionPass(this, settings, material);
            // Before the opaque forward pass (and after the prepasses, which keep drawing skin depth).
            enableSingleDraw = new SingleDrawFlagPass { renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingOpaques - 1) };
        }

        // Cameras without this feature (previews, reflections, overlays, other renderers) draw skin in its forward pass.
        static void ResetSingleDraw(ScriptableRenderContext context, Camera camera) => Shader.SetGlobalFloat(SingleDrawId, 0);

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData;
            if (!material || settings.strength <= 0 || camera.cameraType == CameraType.Preview
                || camera.cameraType == CameraType.Reflection || camera.camera.stereoEnabled
                || camera.renderType != CameraRenderType.Base) return;
            bool single = settings.singleDraw && settings.layers.value == -1;
            pass.singleDraw = single;
            // Single draw: right after the opaques, before the skybox and the depth/colour copies, so they see the skin.
            pass.renderPassEvent = single ? RenderPassEvent.AfterRenderingOpaques
                : (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents - 3);
            if (single) renderer.EnqueuePass(enableSingleDraw);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            cameraHook.Release(ref cameraHookHeld);
            Shader.SetGlobalFloat(SingleDrawId, 0);
            CoreUtils.Destroy(material);
        }

        sealed class FlagData { public float value; }

        static void RecordSingleDrawFlag(RenderGraph graph, float value, string name)
        {
            using var builder = graph.AddUnsafePass<FlagData>(name, out var data);
            data.value = value;
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc(static (FlagData d, UnsafeGraphContext ctx) => ctx.cmd.SetGlobalFloat(SingleDrawId, d.value));
        }

        /// <summary>Makes the skin forward pass cull itself for this camera: the single draw replaces it.</summary>
        sealed class SingleDrawFlagPass : ScriptableRenderPass
        {
            public SingleDrawFlagPass() => requiresIntermediateTexture = true;

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                // Same condition as the single draw itself: a camera that cannot draw it keeps the forward pass.
                if (frame.Get<UniversalResourceData>().isActiveTargetBackBuffer) return;
                RecordSingleDrawFlag(graph, 1, "MetaHuman: skin single draw on");
            }
        }

        sealed class DiffusionPass : ScriptableRenderPass
        {
            readonly SkinDiffusionFeature owner;
            readonly Settings settings;
            readonly Material material;
            readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
            public bool singleDraw;
            static readonly ShaderTagId DiffuseTag = new ShaderTagId("MetaHumanSkinDiffuse");
            static readonly ShaderTagId SplitTag = new ShaderTagId("MetaHumanSkinSplit");
            static readonly int SourceId = Shader.PropertyToID("_MHSource");
            static readonly int OriginalId = Shader.PropertyToID("_MHOriginalDiffuse");
            static readonly int GuideId = Shader.PropertyToID("_MHGuide");
            static readonly int AlbedoId = Shader.PropertyToID("_MHAlbedo");
            static readonly int WidthsId = Shader.PropertyToID("_MHWidths");
            static readonly int PreservationId = Shader.PropertyToID("_MHAlbedoPreservation");
            static readonly int CameraId = Shader.PropertyToID("_MHCameraColor");
            static readonly int ParametersId = Shader.PropertyToID("_MHDiffusion");
            static readonly int DirectionId = Shader.PropertyToID("_MHDirection");
            const int CompositePass = 1, AdditiveCompositePass = 3;

            public DiffusionPass(SkinDiffusionFeature owner, Settings settings, Material material)
            {
                this.owner = owner; this.settings = settings; this.material = material;
                renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents - 3);
                requiresIntermediateTexture = true;
            }

            sealed class DrawData { public RendererListHandle list; }
            sealed class FilterData
            {
                public TextureHandle source, original, guide, camera, albedo;
                public bool readsCamera;
                public Material material;
                public MaterialPropertyBlock properties;
                public Vector4 parameters, direction, widths;
                public int shaderPass;
                public float albedoPreservation;
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
                // Geometry targets retain the active color/depth sample count. The renderer's
                // resolved sampling views are used by the following single-sample filters.
                desc.name = "MetaHuman skin diffuse + amount";
                desc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                desc.clearBuffer = true; desc.clearColor = Color.clear;
                desc.depthBufferBits = DepthBits.None; desc.bindTextureMS = false;
                var diffuse = graph.CreateTexture(desc);
                desc.name = "MetaHuman surface normal + eye depth";
                var guide = graph.CreateTexture(desc);
                desc.name = "MetaHuman skin diffuse reflectance";
                var albedo = graph.CreateTexture(desc);
                owner.lastRecordedSingleDraw = singleDraw;
                var drawing = RenderingUtils.CreateDrawingSettings(singleDraw ? SplitTag : DiffuseTag, rendering, camera, lights, camera.defaultOpaqueSortFlags);
                var filtering = new FilteringSettings(RenderQueueRange.opaque, settings.layers);
                var list = graph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));
                string drawName = singleDraw ? "MetaHuman: skin lit colour + diffuse (single draw)" : "MetaHuman: skin diffuse extraction";
                using (var builder = graph.AddRasterRenderPass<DrawData>(drawName, out var data))
                {
                    data.list = list;
                    builder.UseRendererList(list);
                    // Renderer lists evaluate the complete lit material. Other URP
                    // features can publish inputs such as screen-space shadows,
                    // beyond the regular shadow/AO handles exposed below.
                    builder.UseAllGlobalTextures(true);
                    // The lit extraction shader consumes URP's shadow/AO globals.
                    // Declare those graph resources too, without publishing extra globals.
                    if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);
                    if (resources.additionalShadowsTexture.IsValid()) builder.UseTexture(resources.additionalShadowsTexture, AccessFlags.Read);
                    if (resources.ssaoTexture.IsValid()) builder.UseTexture(resources.ssaoTexture, AccessFlags.Read);
                    int slot = 0;
                    // Single draw: the skin's lit colour goes to the camera and its depth is written here (its
                    // forward pass is culled); the diffuse-only draw instead reads the opaque depth (ZTest Equal).
                    if (singleDraw) builder.SetRenderAttachment(color, slot++, AccessFlags.ReadWrite);
                    builder.SetRenderAttachment(diffuse, slot++, AccessFlags.Write);
                    builder.SetRenderAttachment(guide, slot++, AccessFlags.Write);
                    builder.SetRenderAttachment(albedo, slot, AccessFlags.Write);
                    // Read-only opaque depth rejects eyes, hair, props and backfaces in front of skin.
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, singleDraw ? AccessFlags.ReadWrite : AccessFlags.Read);
                    builder.SetRenderFunc(static (DrawData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.list));
                }
                // Later skin draws of this camera (none expected) use the forward pass again.
                if (singleDraw) RecordSingleDrawFlag(graph, 0, "MetaHuman: skin single draw off");
                desc.msaaSamples = MSAASamples.None;
                float support = settings.useProfileLengths ? Mathf.Max(.01f, Mathf.Max(settings.meanFreePathMillimetres.x, Mathf.Max(settings.meanFreePathMillimetres.y,settings.meanFreePathMillimetres.z))) * 3 : settings.radiusMillimetres;
                var parameters = new Vector4(support * 0.001f,
                    settings.depthRejectionMillimetres * 0.001f, settings.normalRejection, settings.strength);
                float width = camera.cameraTargetDescriptor.width, height = camera.cameraTargetDescriptor.height;
                TextureHandle filtered;
                if (settings.normalizedDiffusion)
                {
                    desc.name = "MetaHuman radial normalized diffusion";
                    filtered = graph.CreateTexture(desc);
                    AddFilter(graph, "MetaHuman: radial normalized diffusion", diffuse, diffuse, guide, color, albedo, filtered,
                        parameters, new Vector4(1f / width, 1f / height, width, height), 2);
                }
                else
                {
                    desc.name = "MetaHuman horizontal skin diffusion";
                    var horizontal = graph.CreateTexture(desc);
                    desc.name = "MetaHuman vertical skin diffusion";
                    filtered = graph.CreateTexture(desc);
                    AddFilter(graph, "MetaHuman: horizontal diffusion", diffuse, diffuse, guide, color, albedo, horizontal,
                        parameters, new Vector4(1f / width, 0, 1, height), 0);
                    AddFilter(graph, "MetaHuman: vertical diffusion", horizontal, diffuse, guide, color, albedo, filtered,
                        parameters, new Vector4(0, 1f / height, 0, height), 0);
                }
                if (settings.additiveComposite)
                {
                    // Blend One One onto the camera colour: its samples are kept and nothing is copied.
                    AddFilter(graph, "MetaHuman: diffuse-only composite (additive)", filtered, diffuse, guide, color, albedo, color,
                        parameters, new Vector4(0, 0, width, height), AdditiveCompositePass);
                    return;
                }
                var outputDesc = color.GetDescriptor(graph);
                outputDesc.name = "Camera color with MetaHuman skin diffusion";
                outputDesc.clearBuffer = false;
                outputDesc.depthBufferBits = DepthBits.None;
                var output = graph.CreateTexture(outputDesc);
                AddFilter(graph, "MetaHuman: diffuse-only composite", filtered, diffuse, guide, color, albedo, output,
                    parameters, new Vector4(0, 0, width, height), CompositePass);
                // Preserve the sample count for the following transparent and post-processing passes.
                resources.cameraColor = output;
            }

            void AddFilter(RenderGraph graph, string name, TextureHandle source, TextureHandle original,
                TextureHandle guide, TextureHandle camera, TextureHandle albedo, TextureHandle destination, Vector4 parameters,
                Vector4 direction, int shaderPass)
            {
                using var builder = graph.AddRasterRenderPass<FilterData>(name, out var data);
                data.source = source; data.original = original; data.guide = guide; data.camera = camera; data.albedo = albedo;
                // Only the copy composite samples the camera colour; the additive one writes into it.
                data.readsCamera = shaderPass == CompositePass;
                data.albedoPreservation = settings.albedoPreservation;
                float support = Mathf.Max(parameters.x * 1000, .001f);
                Vector3 widths = settings.useProfileLengths ? settings.meanFreePathMillimetres / support : new Vector3(.45f,.22f,.12f);
                if (shaderPass == 2) widths = settings.scatterDistanceMillimetres * .001f;
                float minimum = shaderPass == 2 ? 1e-7f : .001f;
                data.widths = new Vector4(Mathf.Max(minimum,widths.x), Mathf.Max(minimum,widths.y), Mathf.Max(minimum,widths.z), 0);
                data.parameters = parameters; data.direction = direction; data.material = material; data.shaderPass = shaderPass;
                data.properties = properties;
                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(original, AccessFlags.Read);
                builder.UseTexture(guide, AccessFlags.Read);
                if (data.readsCamera) builder.UseTexture(camera, AccessFlags.Read);
                builder.UseTexture(albedo, AccessFlags.Read);
                // The additive composite blends onto the existing camera colour, so it must be loaded.
                builder.SetRenderAttachment(destination, 0, shaderPass == AdditiveCompositePass ? AccessFlags.ReadWrite : AccessFlags.Write);
                // Bind resolved textures only during execution. DrawProcedural copies the
                // property block into the command buffer, so sequential draws/cameras cannot
                // overwrite an earlier draw. No resources are published as global state.
                builder.SetRenderFunc(static (FilterData d, RasterGraphContext ctx) =>
                {
                    var p = d.properties;
                    p.Clear();
                    p.SetTexture(SourceId, (RTHandle)d.source);
                    p.SetTexture(OriginalId, (RTHandle)d.original);
                    p.SetTexture(GuideId, (RTHandle)d.guide);
                    if (d.readsCamera) p.SetTexture(CameraId, (RTHandle)d.camera);
                    p.SetTexture(AlbedoId, (RTHandle)d.albedo);
                    p.SetFloat(PreservationId, d.albedoPreservation);
                    p.SetVector(WidthsId, d.widths);
                    p.SetVector(ParametersId, d.parameters);
                    p.SetVector(DirectionId, d.direction);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.shaderPass, MeshTopology.Triangles, 3, 1, d.properties);
                });
            }
        }
    }
}

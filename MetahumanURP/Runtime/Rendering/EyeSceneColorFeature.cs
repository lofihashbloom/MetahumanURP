using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace MetaHuman.Rendering
{
    /// <summary>Scene-linear snapshot for the eye's transparent optical layer.</summary>
    public sealed class EyeSceneColorFeature : ScriptableRendererFeature
    {
        [Tooltip("Legacy source scene-color copy range; zero keeps HDR radiance.")]
        [Min(0)] public float radianceCeiling = 10;
        [Range(1, 8)] public float encodingExponent = 4;
        public Shader shader;
        Material material;
        SnapshotPass pass;
        static readonly int Ready = Shader.PropertyToID("_MHEyeSceneReady");
        void ResetCameraSnapshot(ScriptableRenderContext context, Camera camera) => Shader.SetGlobalFloat(Ready, 0);
        public override void Create()
        {
            RenderPipelineManager.beginCameraRendering -= ResetCameraSnapshot;
            RenderPipelineManager.beginCameraRendering += ResetCameraSnapshot;
            CoreUtils.Destroy(material);
            var selected = shader ? shader : Shader.Find("Hidden/MetaHuman/EyeSceneColor");
            material = selected ? CoreUtils.CreateEngineMaterial(selected) : null;
            pass = new SnapshotPass(this, material);
        }

        protected override void Dispose(bool disposing)
        {
            RenderPipelineManager.beginCameraRendering -= ResetCameraSnapshot;
            Shader.SetGlobalFloat(Ready, 0);
            CoreUtils.Destroy(material);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            if (!material || data.cameraData.cameraType == CameraType.Preview) return;
            renderer.EnqueuePass(pass);
        }

        sealed class SnapshotPass : ScriptableRenderPass
        {
            static readonly int SceneColor = Shader.PropertyToID("_MHEyeSceneColor");
            static readonly int Encoding = Shader.PropertyToID("_MHEyeSceneEncoding");
            readonly EyeSceneColorFeature settings;
            readonly Material material;
            sealed class Data { public TextureHandle source; public Material material; public Vector4 encoding; }

            public SnapshotPass(EyeSceneColorFeature settings, Material material)
            {
                this.settings = settings;
                this.material = material;
                renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents - 1);
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var resources = frame.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var source = resources.activeColorTexture;
                var desc = source.GetDescriptor(graph);
                desc.name = "MetaHuman eye scene color";
                desc.depthBufferBits = DepthBits.None;
                desc.msaaSamples = MSAASamples.None;
                desc.bindTextureMS = false;
                desc.colorFormat = settings.radianceCeiling > 0 ? GraphicsFormat.R8G8B8A8_UNorm : GraphicsFormat.R16G16B16A16_SFloat;
                desc.clearBuffer = false;
                var snapshot = graph.CreateTexture(desc);
                using var builder = graph.AddRasterRenderPass<Data>("MetaHuman: eye optical snapshot", out var data);
                data.source = source;
                data.material = material;
                data.encoding = new Vector4(settings.radianceCeiling, settings.encodingExponent, 0, 0);
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(snapshot, 0, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(snapshot, SceneColor);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.SetGlobalVector(Encoding, d.encoding);
                    ctx.cmd.SetGlobalFloat(Ready, 1);
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0);
                });
            }
        }
    }
}

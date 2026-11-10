using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Split from FilmGrade.cs: a renderer feature is saved as an asset only when its class has a file of its own name.
/// <summary>
/// The teaser film grade as a URP renderer feature: after post-processing, keeps three scene-linear frames, grades their
/// [1/4,1/2,1/4] blend (one frame of latency; off: the current frame only) and presents the square in the camera target.
/// </summary>
public sealed class FilmGradeFeature : ScriptableRendererFeature
{
    public FilmGrade.Settings settings = new FilmGrade.Settings();
    [Tooltip("[1/4,1/2,1/4] filter over three frames, as the offline video grade (one frame of latency)")] public bool temporal = true;
    [Tooltip("Write display-encoded values (for a capture that saves the target as is) instead of linear ones")] public bool encodedOutput;
    GradePass pass;
    Material present;

    // Create runs again on every inspector edit: release the previous pass's three history targets before replacing it.
    public override void Create() { pass?.Release(); pass = new GradePass(); }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType != CameraType.Game) return;
        if (!present)
        {
            var shader = Shader.Find("Hidden/MetaHuman/Sample/FilmGradePresent");
            if (!shader) return;
            present = CoreUtils.CreateEngineMaterial(shader);
        }
        pass.feature = this; pass.present = present;
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing) { pass?.Release(); pass = null; FilmGrade.Release(); CoreUtils.Destroy(present); present = null; }

    sealed class GradePass : ScriptableRenderPass
    {
        public FilmGradeFeature feature;
        public Material present;
        readonly RenderTexture[] history = new RenderTexture[3];
        readonly RTHandle[] handles = new RTHandle[3];
        RTHandle outputHandle;
        int pushed;

        sealed class Data { public GradePass pass; public TextureHandle color; public int width, height; public GraphicsFormat format; }

        public GradePass() { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing; requiresIntermediateTexture = true; }

        public void Release()
        {
            for (int i = 0; i < 3; i++)
            {
                handles[i]?.Release(); handles[i] = null;
                if (history[i]) { history[i].Release(); UnityEngine.Object.DestroyImmediate(history[i]); history[i] = null; }
            }
            outputHandle?.Release(); outputHandle = null; pushed = 0;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            var resources = frame.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;
            var desc = resources.activeColorTexture.GetDescriptor(graph);
            using var builder = graph.AddUnsafePass<Data>("Film grade", out var data);
            data.pass = this; data.color = resources.activeColorTexture; data.width = desc.width; data.height = desc.height; data.format = desc.colorFormat;
            builder.UseTexture(data.color, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (Data d, UnsafeGraphContext context) => d.pass.Execute(CommandBufferHelpers.GetNativeCommandBuffer(context.cmd), d));
        }

        void Execute(CommandBuffer cmd, Data d)
        {
            if (!history[0] || history[0].width != d.width || history[0].height != d.height)
            {
                Release();
                for (int i = 0; i < 3; i++)
                {
                    history[i] = new RenderTexture(d.width, d.height, 0, GraphicsFormat.R16G16B16A16_SFloat) { name = "Film grade history " + i, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
                    history[i].Create();
                    handles[i] = RTHandles.Alloc(history[i]);
                }
            }
            RTHandle color = d.color;
            // Newest frame into the ring; the first frame fills it, so the clip starts as the offline filter does.
            int slot = pushed % 3;
            Blitter.BlitCameraTexture(cmd, color, handles[slot]);
            if (pushed == 0) for (int i = 1; i < 3; i++) Blitter.BlitCameraTexture(cmd, color, handles[i]);
            pushed++;
            Texture newest = history[slot], middle = history[(slot + 2) % 3], oldest = history[(slot + 1) % 3];
            bool ok = feature.temporal
                ? FilmGrade.Process(cmd, oldest, middle, newest, new Vector3(.25f, .5f, .25f), feature.settings, pushed)
                : FilmGrade.Process(cmd, newest, newest, newest, new Vector3(0, 1, 0), feature.settings, pushed);
            if (!ok) return;
            if (outputHandle == null || outputHandle.rt != FilmGrade.Output) { outputHandle?.Release(); outputHandle = RTHandles.Alloc(FilmGrade.Output); }
            int side = Mathf.Min(d.width, d.height);
            cmd.SetGlobalFloat("_TeaserGradeEncoded", feature.encodedOutput ? 1 : 0);
            CoreUtils.SetRenderTarget(cmd, color, ClearFlag.Color, Color.black);
            cmd.SetViewport(new Rect((d.width - side) / 2, (d.height - side) / 2, side, side));
            Blitter.BlitTexture(cmd, outputHandle, new Vector4(1, 1, 0, 0), present, 0);
        }
    }
}

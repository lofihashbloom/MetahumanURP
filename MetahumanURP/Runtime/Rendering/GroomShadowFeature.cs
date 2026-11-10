using System;
using System.Collections.Generic;
using MetaHuman.Groom;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace MetaHuman.Rendering
{
    /// <summary>
    /// Hair self-shadowing driver. Before opaque drawing it updates every active <see cref="GroomShadowVolume"/>
    /// (a transmittance dispatch only for lights that moved relative to their volume) and publishes, for this camera,
    /// the identity of the main light and of each additional-light buffer entry. GroomStrands matches those identities
    /// against its volume's slots. Requires per-pixel additional lights for additional-light shadows.
    /// </summary>
    public sealed class GroomShadowFeature : ScriptableRendererFeature
    {
        [Serializable] public sealed class Settings
        {
            [Tooltip("Transmittance compute shader. Empty: the package's MetaHumanGroomShadowTransmittance.")]
            public ComputeShader compute;
        }

        public Settings settings = new Settings();
        UpdatePass pass;

        public override void Create()
        {
            var compute = settings.compute ? settings.compute : Resources.Load<ComputeShader>("MetaHumanGroomShadowTransmittance");
            pass = new UpdatePass(compute);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            if (GroomShadowVolume.Active.Count == 0 || !pass.Ready) return;
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supports3DRenderTextures) return;
            renderer.EnqueuePass(pass);
        }

        /// <summary>
        /// Light identity for each entry of the additional-light buffer: visible order, main light skipped, capped at
        /// capacity (URP's additional-light constants). Lights without a volume slot still get an identity; the shader
        /// leaves them unshadowed when no slot matches.
        /// </summary>
        public static float[] MapAdditionalLightIds(IReadOnlyList<Light> visible, int mainIndex, int capacity, out float mainId)
        {
            mainId = mainIndex >= 0 && mainIndex < visible.Count ? GroomShadowVolume.LightId(visible[mainIndex]) : 0;
            var ids = new List<float>();
            for (int i = 0; i < visible.Count && ids.Count < capacity; i++)
            {
                if (i == mainIndex) continue;
                ids.Add(GroomShadowVolume.LightId(visible[i]));
            }
            return ids.ToArray();
        }

        static readonly int DensityId = Shader.PropertyToID("_MHGroomDensity");
        static readonly int TauId = Shader.PropertyToID("_MHGroomTau");
        static readonly int ResolutionId = Shader.PropertyToID("_MHGroomResolution");
        static readonly int SizeId = Shader.PropertyToID("_MHGroomSize");
        static readonly int LightId = Shader.PropertyToID("_MHGroomLight");
        static readonly int MarchId = Shader.PropertyToID("_MHGroomMarch");
        static readonly int CoarseId = Shader.PropertyToID("_MHGroomCoarse");
        static readonly int FullMarchId = Shader.PropertyToID("_MHGroomFullMarch");
        static readonly int CoarseScaleId = Shader.PropertyToID("_MHGroomCoarseScale");

        /// <summary>Records one transmittance march (4x4x4 threads per group).</summary>
        public static void Encode(CommandBuffer cmd, ComputeShader compute, int kernel, GroomShadowVolume.Dispatch job)
        {
            if (!job.density || !job.target) return;
            cmd.SetComputeTextureParam(compute, kernel, DensityId, job.density);
            cmd.SetComputeTextureParam(compute, kernel, TauId, job.target);
            cmd.SetComputeVectorParam(compute, ResolutionId, job.resolution);
            cmd.SetComputeVectorParam(compute, SizeId, job.size);
            cmd.SetComputeVectorParam(compute, LightId, job.light);
            cmd.SetComputeVectorParam(compute, MarchId, job.march);
            // Without the coarse inputs every voxel takes the full march (the density stands in for the unused textures).
            bool coarse = job.coarse && job.fullMarch && job.coarseScale.w > 0;
            cmd.SetComputeTextureParam(compute, kernel, CoarseId, coarse ? job.coarse : job.density);
            cmd.SetComputeTextureParam(compute, kernel, FullMarchId, coarse ? job.fullMarch : job.density);
            cmd.SetComputeVectorParam(compute, CoarseScaleId, coarse ? job.coarseScale : Vector4.zero);
            cmd.DispatchCompute(compute, kernel, Mathf.CeilToInt(job.resolution.x / 4), Mathf.CeilToInt(job.resolution.y / 4), Mathf.CeilToInt(job.resolution.z / 4));
        }

        sealed class UpdatePass : ScriptableRenderPass
        {
            static readonly int MainLightId = Shader.PropertyToID("_MHGroomMainLightId");
            static readonly int CountId = Shader.PropertyToID("_MHGroomAdditionalLightCount");
            static readonly int IdsId = Shader.PropertyToID("_MHGroomAdditionalLightIds");

            readonly ComputeShader compute;
            readonly int kernel = -1;
            public bool Ready => compute && kernel >= 0;

            sealed class Data
            {
                public ComputeShader compute;
                public int kernel;
                public readonly List<GroomShadowVolume.Dispatch> dispatches = new List<GroomShadowVolume.Dispatch>();
                public float mainId, count;
                public readonly Vector4[] ids = new Vector4[64];
            }

            // Before opaques (as the receiver shadows): the stochastic strands draw in the opaque queue.
            public UpdatePass(ComputeShader compute)
            {
                this.compute = compute;
                if (compute && compute.HasKernel("March")) kernel = compute.FindKernel("March");
                renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingOpaques - 2);
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var lightData = frame.Get<UniversalLightData>();
                using var builder = graph.AddUnsafePass<Data>("MetaHuman groom self-shadow", out var data);
                data.compute = compute; data.kernel = kernel;
                data.dispatches.Clear();
                // Main thread: volumes pick lights, bind their renderers and queue only stale transmittance.
                foreach (var volume in GroomShadowVolume.Active) if (volume) volume.Prepare(data.dispatches);

                var visible = new List<Light>(lightData.visibleLights.Length);
                foreach (var v in lightData.visibleLights) visible.Add(v.light);
                int capacity = Mathf.Min(256, UniversalRenderPipeline.maxVisibleAdditionalLights);
                var ids = MapAdditionalLightIds(visible, lightData.mainLightIndex, capacity, out data.mainId);
                Array.Fill(data.ids, Vector4.zero);
                for (int i = 0; i < ids.Length; i++) data.ids[i >> 2][i & 3] = ids[i];
                // Per-vertex additional lights have no per-pixel position to attenuate.
                data.count = lightData.supportsAdditionalLights && !lightData.shadeAdditionalLightsPerVertex
                    ? Mathf.Min(ids.Length, lightData.additionalLightsCount) : 0;

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (Data d, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    foreach (var job in d.dispatches) Encode(cmd, d.compute, d.kernel, job);
                    cmd.SetGlobalFloat(MainLightId, d.mainId);
                    cmd.SetGlobalFloat(CountId, d.count);
                    cmd.SetGlobalVectorArray(IdsId, d.ids);
                });
            }
        }
    }
}

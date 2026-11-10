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
    /// Binds the active <see cref="HairReceiverShadowVolume"/> before opaque drawing: four optical-depth
    /// textures, their world frames and the mapping from the camera's actual additional-light buffer
    /// order to volume slots. Skin materials with MH_HAIR_RECEIVER_SHADOW consume it in both the forward
    /// and skin-diffuse passes. Requires per-pixel additional lights.
    /// </summary>
    public sealed class HairReceiverShadowFeature : ScriptableRendererFeature
    {
        BindPass pass;

        public override void Create() => pass = new BindPass();

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            if (!HairReceiverShadowVolume.Active) return;
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) => pass?.Release();

        /// <summary>
        /// Volume slot for each entry of the additional-light buffer. Mirrors URP's additional-light
        /// constants: visible order, main light skipped, capped at capacity. Unknown lights map to -1.
        /// </summary>
        public static int[] MapAdditionalLights(IReadOnlyList<Light> visible, int mainIndex, IReadOnlyList<Light> slots, int capacity, out int mainSlot)
        {
            int Slot(Light light)
            {
                if (!light) return -1;
                for (int s = 0; s < slots.Count; s++) if (slots[s] == light) return s;
                return -1;
            }
            mainSlot = mainIndex >= 0 && mainIndex < visible.Count ? Slot(visible[mainIndex]) : -1;
            var mapped = new List<int>();
            for (int i = 0; i < visible.Count && mapped.Count < capacity; i++)
            {
                if (i == mainIndex) continue;
                mapped.Add(Slot(visible[i]));
            }
            return mapped.ToArray();
        }

        sealed class BindPass : ScriptableRenderPass
        {
            static readonly int EnabledId = Shader.PropertyToID("_MHReceiverEnabled");
            static readonly int DensityId = Shader.PropertyToID("_MHReceiverDensity");
            static readonly int ResolutionId = Shader.PropertyToID("_MHReceiverResolution");
            static readonly int OriginId = Shader.PropertyToID("_MHReceiverOriginZMin");
            static readonly int RightId = Shader.PropertyToID("_MHReceiverRightUMin");
            static readonly int UpId = Shader.PropertyToID("_MHReceiverUpVMin");
            static readonly int ForwardId = Shader.PropertyToID("_MHReceiverForwardZMax");
            static readonly int ScalesId = Shader.PropertyToID("_MHReceiverScales");
            static readonly int MainSlotId = Shader.PropertyToID("_MHReceiverMainSlot");
            static readonly int CountId = Shader.PropertyToID("_MHReceiverAdditionalCount");
            static readonly int SlotsId = Shader.PropertyToID("_MHReceiverAdditionalSlots");
            static readonly int[] TauIds =
            {
                Shader.PropertyToID("_MHReceiverTau0"), Shader.PropertyToID("_MHReceiverTau1"),
                Shader.PropertyToID("_MHReceiverTau2"), Shader.PropertyToID("_MHReceiverTau3")
            };

            readonly Dictionary<Texture3D, RTHandle> handles = new Dictionary<Texture3D, RTHandle>();
            readonly List<Texture3D> stale = new List<Texture3D>();

            sealed class Data
            {
                public bool enabled;
                public Vector4 density, resolution;
                public readonly Vector4[] origin = new Vector4[4], right = new Vector4[4], up = new Vector4[4], forward = new Vector4[4], scales = new Vector4[4];
                public int mainSlot, count;
                public readonly Vector4[] slots = new Vector4[64];
            }

            // Strictly before the opaque draw: RenderGraph global textures set after the opaque pass are
            // dropped at the end of the frame, leaving the skin sampling the default black texture.
            public BindPass() => renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingOpaques - 2);

            public void Release()
            {
                foreach (var h in handles.Values) h?.Release();
                handles.Clear();
            }

            RTHandle Handle(Texture3D texture)
            {
                if (!handles.TryGetValue(texture, out var handle)) handles.Add(texture, handle = RTHandles.Alloc(texture));
                return handle;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var volume = HairReceiverShadowVolume.Active;
                if (!volume) return;
                var lightData = frame.Get<UniversalLightData>();
                var resolved = volume.ResolveSlots();

                // Drop handles for textures no longer referenced (field reassigned or destroyed).
                stale.Clear();
                foreach (var key in handles.Keys)
                    if (!key || Array.FindIndex(resolved, s => s.Valid && s.field.opticalDepth == key) < 0) stale.Add(key);
                foreach (var key in stale) { handles[key]?.Release(); handles.Remove(key); }

                using var builder = graph.AddUnsafePass<Data>("MetaHuman hair receiver shadows", out var data);
                bool any = false;
                for (int i = 0; i < HairReceiverShadowVolume.SlotCount; i++)
                {
                    var s = resolved[i];
                    data.origin[i] = s.originZMin; data.right[i] = s.rightUMin; data.up[i] = s.upVMin;
                    data.forward[i] = s.forwardZMax; data.scales[i] = s.scales;
                    data.resolution[i] = s.Valid ? s.field.Resolution : 0;
                    data.density[i] = s.Valid ? volume.density : 0;
                    if (!s.Valid) continue;
                    var texture = graph.ImportTexture(Handle(s.field.opticalDepth));
                    builder.UseTexture(texture, AccessFlags.Read);
                    builder.SetGlobalTextureAfterPass(texture, TauIds[i]);
                    any = true;
                }

                var visible = new List<Light>(lightData.visibleLights.Length);
                foreach (var v in lightData.visibleLights) visible.Add(v.light);
                var slotLights = new Light[HairReceiverShadowVolume.SlotCount];
                for (int i = 0; i < slotLights.Length; i++) slotLights[i] = resolved[i].Valid ? resolved[i].light : null;
                int capacity = Mathf.Min(256, UniversalRenderPipeline.maxVisibleAdditionalLights);
                var mapped = MapAdditionalLights(visible, lightData.mainLightIndex, slotLights, capacity, out data.mainSlot);
                Array.Fill(data.slots, new Vector4(-1, -1, -1, -1));
                for (int i = 0; i < mapped.Length; i++) data.slots[i >> 2][i & 3] = mapped[i];
                data.count = lightData.supportsAdditionalLights ? Mathf.Min(mapped.Length, lightData.additionalLightsCount) : 0;
                // Per-vertex additional lights have no per-pixel position to attenuate; leave them unshadowed.
                data.enabled = any && !lightData.shadeAdditionalLightsPerVertex;

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (Data d, UnsafeGraphContext context) =>
                {
                    var cmd = context.cmd;
                    cmd.SetGlobalFloat(EnabledId, d.enabled ? 1 : 0);
                    cmd.SetGlobalVector(DensityId, d.density);
                    cmd.SetGlobalVector(ResolutionId, d.resolution);
                    cmd.SetGlobalVectorArray(OriginId, d.origin);
                    cmd.SetGlobalVectorArray(RightId, d.right);
                    cmd.SetGlobalVectorArray(UpId, d.up);
                    cmd.SetGlobalVectorArray(ForwardId, d.forward);
                    cmd.SetGlobalVectorArray(ScalesId, d.scales);
                    cmd.SetGlobalInt(MainSlotId, d.mainSlot);
                    cmd.SetGlobalInt(CountId, d.count);
                    cmd.SetGlobalVectorArray(SlotsId, d.slots);
                });
            }
        }
    }
}

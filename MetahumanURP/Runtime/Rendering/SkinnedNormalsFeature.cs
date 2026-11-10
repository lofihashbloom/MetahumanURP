using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace MetaHuman.Rendering
{
    /// <summary>
    /// Runs every active <see cref="SkinnedNormalRecompute"/> before the shadow and opaque passes: three compute dispatches
    /// per renderer rewrite the normals and tangents of its deformed vertex buffer for this frame. The kernels only read
    /// positions and setup data, so several cameras in one frame rewrite the same values. Inert without components.
    /// </summary>
    public sealed class SkinnedNormalsFeature : ScriptableRendererFeature
    {
        public ComputeShader computeShader;
        RecomputePass pass;

        public override void Create() => pass = new RecomputePass();

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            if (SkinnedNormalRecompute.Active.Count == 0) return;
            pass.shader = computeShader ? computeShader : Resources.Load<ComputeShader>("MetaHumanSkinnedNormals");
            if (!pass.shader) return;
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) => pass?.ReleaseHandles();

        sealed class RecomputePass : ScriptableRenderPass
        {
            static readonly int VerticesId = Shader.PropertyToID("_Vertices"), TrianglesId = Shader.PropertyToID("_Triangles"), SkinnedId = Shader.PropertyToID("_Skinned");
            static readonly int CountId = Shader.PropertyToID("_VertexCount"), StrideId = Shader.PropertyToID("_Stride");
            static readonly int NormalOffsetId = Shader.PropertyToID("_NormalOffset"), TangentOffsetId = Shader.PropertyToID("_TangentOffset"), HasTangentId = Shader.PropertyToID("_HasTangent");
            static readonly int TempId = Shader.PropertyToID("_Temp"), CreaseStartId = Shader.PropertyToID("_CreaseStart"), CreaseEndId = Shader.PropertyToID("_CreaseEnd"), StretchId = Shader.PropertyToID("_Stretch");
            static readonly int CreaseSourceId = Shader.PropertyToID("_CreaseSource"), CreaseFinalId = Shader.PropertyToID("_CreaseFinal");
            public ComputeShader shader;
            // Handles from SkinnedMeshRenderer.GetVertexBuffer are kept until the next recording: the commands that use
            // them run after this method returns (RenderGraph records, then executes, each camera).
            readonly List<GraphicsBuffer> handles = new List<GraphicsBuffer>();

            sealed class Job { public GraphicsBuffer skinned, vertices, triangles, temp; public int count, stride, normalOffset, tangentOffset, hasTangent; public Vector2 crease, stretch; }
            sealed class Data { public ComputeShader shader; public readonly List<Job> jobs = new List<Job>(); }

            public RecomputePass() => renderPassEvent = RenderPassEvent.BeforeRenderingShadows;

            public void ReleaseHandles() { foreach (var h in handles) h?.Dispose(); handles.Clear(); }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                ReleaseHandles();
                using var builder = graph.AddUnsafePass<Data>("MetaHuman: recompute skinned normals", out var data);
                data.shader = shader; data.jobs.Clear();
                foreach (var c in SkinnedNormalRecompute.Active)
                {
                    if (!c || !c.Prepare()) continue;
                    var skinned = c.AcquireSkinnedBuffer();
                    if (skinned == null) continue;
                    handles.Add(skinned);
                    int expected = 12 + 12 + (c.data.hasTangents ? 16 : 0);
                    if (skinned.stride < expected || c.normalOffset + 12 > skinned.stride) continue;
                    data.jobs.Add(new Job
                    {
                        skinned = skinned, vertices = c.vertexBuffer, triangles = c.triangleBuffer, temp = c.tempBuffer, crease = c.Crease, stretch = c.Stretch, count = c.data.vertices.Length, stride = skinned.stride,
                        normalOffset = c.normalOffset, tangentOffset = c.tangentOffset, hasTangent = c.data.hasTangents && c.tangentOffset + 12 <= skinned.stride ? 1 : 0,
                    });
                }
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (Data d, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    foreach (var j in d.jobs)
                    {
                        cmd.SetComputeIntParam(d.shader, CountId, j.count);
                        cmd.SetComputeIntParam(d.shader, StrideId, j.stride);
                        cmd.SetComputeIntParam(d.shader, NormalOffsetId, j.normalOffset);
                        cmd.SetComputeIntParam(d.shader, TangentOffsetId, j.tangentOffset);
                        cmd.SetComputeIntParam(d.shader, HasTangentId, j.hasTangent);
                        cmd.SetComputeFloatParam(d.shader, CreaseStartId, j.crease.x);
                        cmd.SetComputeFloatParam(d.shader, CreaseEndId, j.crease.y);
                        cmd.SetComputeVectorParam(d.shader, StretchId, j.stretch);
                        for (int kernel = 0; kernel < 2; kernel++)
                        {
                            cmd.SetComputeBufferParam(d.shader, kernel, VerticesId, j.vertices);
                            cmd.SetComputeBufferParam(d.shader, kernel, TrianglesId, j.triangles);
                            cmd.SetComputeBufferParam(d.shader, kernel, SkinnedId, j.skinned);
                            cmd.SetComputeBufferParam(d.shader, kernel, TempId, j.temp);
                        }
                        int groups = (j.count + 63) / 64;
                        cmd.DispatchCompute(d.shader, 0, groups, 1, 1);   // Recompute into the first half of _Temp
                        for (int it = 0; it < SkinnedNormalRecompute.CreaseIterations; it++)   // Crease: ping-pong, the last into the vertex buffer
                        {
                            cmd.SetComputeIntParam(d.shader, CreaseSourceId, it % 2 == 0 ? 0 : 2 * j.count);
                            cmd.SetComputeIntParam(d.shader, CreaseFinalId, it == SkinnedNormalRecompute.CreaseIterations - 1 ? 1 : 0);
                            cmd.DispatchCompute(d.shader, 1, groups, 1, 1);
                        }
                    }
                });
            }
        }
    }
}

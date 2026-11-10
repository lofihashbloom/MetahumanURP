using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MetaHuman.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MetaHuman.Tests
{
    public sealed class GroomDeferredTests
    {
        const string Package = "Packages/com.unity.metahuman/Runtime/";

        [Test] public void ComputeRasterKernelsCompileWithoutErrors()
        {
            var cs = Resources.Load<ComputeShader>("MetaHumanGroomComputeRaster");
            Assert.IsNotNull(cs);
            var errors = ShaderUtil.GetComputeShaderMessages(cs).Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => e.message + " " + e.file + ":" + e.line)));
            foreach (var k in new[] { "BuildPoints", "BuildSegments", "ClearTargets", "RasterOpticalAndDepth", "RasterNearestId" })
                Assert.IsTrue(cs.HasKernel(k), k);
        }

        // The per-mesh record is filled from C# (GraphicsBuffer.SetData) and read as MHGroomMesh in HLSL.
        [Test] public void MeshRecordLayoutMatchesShader()
        {
            var geometry = typeof(GroomDeferredFeature).GetNestedType("Geometry", System.Reflection.BindingFlags.NonPublic);
            var meshData = geometry.GetNestedType("MeshData");
            int stride = (int)geometry.GetField("MeshStride").GetValue(null);
            Assert.AreEqual(stride, Marshal.SizeOf(meshData));
            var hlsl = File.ReadAllText(Path.GetFullPath(Package + "Shaders/GroomSegments.hlsl"));
            int start = hlsl.IndexOf("struct MHGroomMesh"), end = hlsl.IndexOf("};", start);
            var body = hlsl.Substring(start, end - start);
            int bytes = 0;
            foreach (var line in body.Split('\n').Select(l => l.Trim()))
            {
                if (line.StartsWith("float4x4 ")) bytes += 64;
                else if (line.StartsWith("float4 ") || line.StartsWith("uint4 ")) bytes += 16;
                else if (line.StartsWith("float ") || line.StartsWith("uint ")) bytes += 4;
            }
            Assert.AreEqual(stride, bytes);
        }

        [Test] public void LongSegmentShaderCompiles()
        {
            var shader = Shader.Find("Hidden/MetaHuman/GroomLongSegments");
            Assert.IsNotNull(shader);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), string.Join("\n", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)));
            Assert.AreEqual(2, shader.passCount);
        }

        [Test] public void ShadeGroupRectangleVertexIsDeclared()
        {
            var passes = File.ReadAllText(Path.GetFullPath(Package + "Shaders/GroomDeferredPasses.hlsl"));
            StringAssert.Contains("DeferredVaryings StrandComputeShadeVertex(uint vertexID : SV_VertexID)", passes);
            StringAssert.Contains("mesh.info.x != (uint)_MHGroomComputeGroup", passes);
        }
    }
}

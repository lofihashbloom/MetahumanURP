using System.Collections.Generic;
using MetaHuman.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace MetaHuman.Tests
{
    public sealed class SkinnedNormalRecomputeTests
    {
        readonly List<Object> created = new List<Object>();
        [TearDown] public void TearDown() { foreach (var o in created) if (o) Object.DestroyImmediate(o); created.Clear(); }

        // A 5 x 5 grid in the XY plane facing -Z, the column x = 2 split in two (a UV seam: duplicated vertices at the same
        // positions), with authored normals tilted off the geometric normal (as artists' smoothing does).
        Mesh Grid(out int[] seamLeft, out int[] seamRight)
        {
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var normals = new List<Vector3>(); var sl = new List<int>(); var sr = new List<int>();
            var id = new int[5, 6];
            for (int y = 0; y < 5; y++)
                for (int c = 0; c < 6; c++)
                {
                    int x = c == 5 ? 2 : c;
                    id[y, c] = verts.Count; verts.Add(new Vector3(x * .01f, y * .01f, 0)); uvs.Add(new Vector2(x * .1f + (c == 5 || x > 2 ? .5f : 0), y * .1f));
                    normals.Add(new Vector3(.2f * Mathf.Sin(x + 2 * y), .15f * Mathf.Cos(3 * x - y), -1).normalized);
                    if (c == 2) sl.Add(id[y, c]); else if (c == 5) sr.Add(id[y, c]);
                }
            int Col(int y, int x, bool rightSide) => x == 2 && rightSide ? id[y, 5] : id[y, x];
            var tris = new List<int>();
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    bool rs = x >= 2;
                    int a = Col(y, x, rs), b = Col(y, x + 1, rs), c = Col(y + 1, x, rs), d = Col(y + 1, x + 1, rs);
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            var mesh = new Mesh { name = "grid" }; created.Add(mesh);
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0); mesh.SetNormals(normals); mesh.RecalculateTangents();
            seamLeft = sl.ToArray(); seamRight = sr.ToArray();
            return mesh;
        }

        static Vector3[] Copy(Vector3[] a) => (Vector3[])a.Clone();

        [Test] public void RestPoseReproducesAuthoredNormalsAndTangents()
        {
            var mesh = Grid(out _, out _);
            var data = SkinnedNormalRecompute.Build(mesh);
            Assert.AreEqual(25, data.welded, "seam copies weld");
            Assert.AreEqual(mesh.vertexCount, data.recomputed);
            var n = new Vector3[mesh.vertexCount]; var t = new Vector3[mesh.vertexCount];
            SkinnedNormalRecompute.Evaluate(data, mesh.vertices, n, t);
            var an = mesh.normals; var at = mesh.tangents;
            for (int v = 0; v < n.Length; v++)
            {
                Assert.That(Vector3.Angle(n[v], an[v]), Is.LessThan(.01f), "normal " + v);
                Assert.That(Vector3.Angle(t[v], (Vector3)at[v]), Is.LessThan(.01f), "tangent " + v);
            }
        }

        [Test] public void RigidMotionTurnsNormalsWithTheMesh()
        {
            var mesh = Grid(out _, out _);
            var data = SkinnedNormalRecompute.Build(mesh);
            var q = Quaternion.Euler(30, -50, 20); var offset = new Vector3(.3f, -1.2f, 2);
            var moved = Copy(mesh.vertices); for (int i = 0; i < moved.Length; i++) moved[i] = q * moved[i] + offset;
            var n = new Vector3[moved.Length]; var t = new Vector3[moved.Length];
            SkinnedNormalRecompute.Evaluate(data, moved, n, t);
            var an = mesh.normals;
            for (int v = 0; v < n.Length; v++) Assert.That(Vector3.Angle(n[v], q * an[v]), Is.LessThan(.01f));
        }

        // The blink case: vertices moved by translation alone (as joint translations and blend shapes move them), folding
        // the right half 90 degrees about the seam. Skinning would leave their normals facing -Z; the recompute turns them
        // to face -X with the surface, keeping the authored tilt, and both seam copies agree.
        [Test] public void TranslatedFoldCarriesNormalsWithTheSurface()
        {
            var mesh = Grid(out var seamLeft, out var seamRight);
            var data = SkinnedNormalRecompute.Build(mesh);
            var p = mesh.vertices; var folded = Copy(p);
            for (int i = 0; i < p.Length; i++) if (p[i].x > .02f + 1e-6f) folded[i] = new Vector3(.02f, p[i].y, -(p[i].x - .02f));
            var n = new Vector3[p.Length]; var t = new Vector3[p.Length];
            SkinnedNormalRecompute.Evaluate(data, folded, n, t, 90, 90);   // transport alone (crease pass off)
            var turn = Quaternion.AngleAxis(90, Vector3.up);   // the fold: +X -> -Z, so -Z -> -X
            var an = mesh.normals;
            for (int v = 0; v < p.Length; v++)
            {
                if (p[v].x < .025f) continue;   // the seam column averages both halves
                Assert.That(Vector3.Angle(n[v], turn * an[v]), Is.LessThan(.5f), "folded vertex " + v + " " + n[v]);
                Assert.That(Vector3.Dot(n[v], t[v]), Is.EqualTo(Vector3.Dot(an[v], (Vector3)mesh.tangents[v])).Within(1e-3f), "tangent keeps its angle");
            }
            for (int v = 0; v < p.Length; v++) if (p[v].x < .005f) Assert.That(Vector3.Angle(n[v], an[v]), Is.LessThan(.01f), "far side untouched");
            for (int i = 0; i < seamLeft.Length; i++) Assert.That(Vector3.Angle(n[seamLeft[i]], n[seamRight[i]]), Is.LessThan(.01f));
        }

        // A crease made by the deformation (the 90 degree fold line) is softened: neighbouring normals across it differ
        // less than with the crease pass off, while the flat parts away from it keep their exact normals.
        [Test] public void CreasePassSoftensFoldLines()
        {
            var mesh = Grid(out var seamLeft, out _);
            var data = SkinnedNormalRecompute.Build(mesh);
            var p = mesh.vertices; var folded = Copy(p);
            for (int i = 0; i < p.Length; i++) if (p[i].x > .02f + 1e-6f) folded[i] = new Vector3(.02f, p[i].y, -(p[i].x - .02f));
            var on = new Vector3[p.Length]; var off = new Vector3[p.Length];
            SkinnedNormalRecompute.Evaluate(data, folded, on, null);
            SkinnedNormalRecompute.Evaluate(data, folded, off, null, 90, 90);
            float Spread(Vector3[] n)   // largest angle between a seam vertex and the vertices one column either side
            {
                float worst = 0;
                for (int v = 0; v < p.Length; v++)
                    foreach (int s in seamLeft)
                        if (Mathf.Abs(p[v].y - p[s].y) < 1e-6f && Mathf.Abs(Mathf.Abs(p[v].x - .02f) - .01f) < 1e-6f) worst = Mathf.Max(worst, Vector3.Angle(n[v], n[s]));
                return worst;
            }
            Assert.That(Spread(on), Is.LessThan(Spread(off) - 3), "softened");
            var an = mesh.normals;
            for (int v = 0; v < p.Length; v++) if (p[v].x < .005f) Assert.That(Vector3.Angle(on[v], an[v]), Is.LessThan(1f), "flat side away from the fold barely touched (two passes spread a little)");
        }

        // The compute kernels against the CPU reference, on a raw buffer laid out as Unity's skinned stream 0
        // (position, normal, tangent; stride 40).
        [Test] public void ComputeKernelMatchesTheCpuReference()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("no compute shaders");
            var shader = Resources.Load<ComputeShader>("MetaHumanSkinnedNormals");
            Assert.IsNotNull(shader);
            var mesh = Grid(out _, out _);
            var data = SkinnedNormalRecompute.Build(mesh);
            var p = mesh.vertices; var folded = Copy(p);
            for (int i = 0; i < p.Length; i++) if (p[i].x > .02f + 1e-6f) folded[i] = new Vector3(.02f, p[i].y + .003f * p[i].x, -(p[i].x - .02f));
            int n = p.Length; var raw = new float[n * 10];
            for (int v = 0; v < n; v++) { raw[10 * v] = folded[v].x; raw[10 * v + 1] = folded[v].y; raw[10 * v + 2] = folded[v].z; raw[10 * v + 9] = 1; }
            using var skinned = new GraphicsBuffer(GraphicsBuffer.Target.Raw, n * 10, 4);
            using var vertices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, SkinnedNormalRecompute.VertexDataStride);
            using var triangles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.triangles.Length, 4);
            using var temp = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4 * n, 16);
            skinned.SetData(raw); vertices.SetData(data.vertices); triangles.SetData(data.triangles);
            var crease = SkinnedNormalRecompute.CreaseRange(SkinnedNormalRecompute.DefaultCreaseStart, SkinnedNormalRecompute.DefaultCreaseFull);
            shader.SetInt("_VertexCount", n); shader.SetInt("_Stride", 40); shader.SetInt("_NormalOffset", 12); shader.SetInt("_TangentOffset", 24); shader.SetInt("_HasTangent", 1);
            shader.SetFloat("_CreaseStart", crease.x); shader.SetFloat("_CreaseEnd", crease.y);
            for (int k = 0; k < 2; k++) { shader.SetBuffer(k, "_Skinned", skinned); shader.SetBuffer(k, "_Vertices", vertices); shader.SetBuffer(k, "_Triangles", triangles); shader.SetBuffer(k, "_Temp", temp); }
            shader.Dispatch(0, (n + 63) / 64, 1, 1);
            for (int it = 0; it < SkinnedNormalRecompute.CreaseIterations; it++)
            {
                shader.SetInt("_CreaseSource", it % 2 == 0 ? 0 : 2 * n); shader.SetInt("_CreaseFinal", it == SkinnedNormalRecompute.CreaseIterations - 1 ? 1 : 0);
                shader.Dispatch(1, (n + 63) / 64, 1, 1);
            }
            var result = new float[n * 10]; skinned.GetData(result);
            var cn = new Vector3[n]; var ct = new Vector3[n];
            SkinnedNormalRecompute.Evaluate(data, folded, cn, ct);
            for (int v = 0; v < n; v++)
            {
                var gn = new Vector3(result[10 * v + 3], result[10 * v + 4], result[10 * v + 5]);
                var gt = new Vector3(result[10 * v + 6], result[10 * v + 7], result[10 * v + 8]);
                Assert.That(Vector3.Angle(gn, cn[v]), Is.LessThan(.05f), "normal " + v);
                Assert.That(Vector3.Angle(gt, ct[v]), Is.LessThan(.05f), "tangent " + v);
                Assert.AreEqual(1, result[10 * v + 9], "tangent sign untouched");
                Assert.AreEqual(folded[v].x, result[10 * v], "positions untouched");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MetaHuman.Rendering
{
    /// <summary>
    /// Recomputes a skinned mesh's normals and tangents on the GPU from its final deformed positions, every frame, before
    /// shadows and opaques draw (<see cref="SkinnedNormalsFeature"/>). Unity skinning turns the authored normals only by
    /// the bone rotations: blend shapes are imported position-only, and the MetaHuman rig also moves the eyelids by joint
    /// translation. A blink therefore rolls skin that was tucked in the lid fold out over the eye while it keeps its tucked
    /// normals, which point down and away from the camera, so the closed lid shades almost black. Unreal recomputes the
    /// deforming face's tangents in its skin cache; this is the URP equivalent.
    ///
    /// At setup, each vertex's authored normal and tangent are stored in a local surface frame: the smooth area-weighted
    /// normal of the welded mesh (UV-seam copies merged by position within a submesh) and the direction to one neighbour.
    /// Each frame the same frame is rebuilt from the deformed positions and the stored vectors are rebuilt in it. The rest
    /// pose reproduces the authored normals; any deformation (joints, translations, blend shapes) carries them along.
    /// A second pass softens creases the deformation creates: where a vertex's normal departs from the average of its
    /// neighbours' by more than it does at rest (the closed lid's fold line, which otherwise read as a hard dark line with
    /// a rim-light glint), it is blended toward that average, fully at <see cref="creaseFullDegrees"/> beyond rest; the
    /// pass runs twice.
    /// Cost: two compute dispatches over the vertices (about 35k on a MetaHuman face), a few reads per incident triangle.
    /// Requirements: GPU skinning with float32 position/normal/tangent in vertex stream 0; mesh data readable at setup.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(SkinnedMeshRenderer))]
    public sealed class SkinnedNormalRecompute : MonoBehaviour
    {
        public static readonly List<SkinnedNormalRecompute> Active = new List<SkinnedNormalRecompute>();
        [Tooltip("Positions closer than this (object units) weld into one vertex for the smooth normal.")]
        public float weldTolerance = 1e-6f;
        [Tooltip("Crease softening starts when a normal departs from its neighbours' average by this much more than at rest.")]
        [Range(0, 90)] public float creaseStartDegrees = 3;
        [Tooltip("...and reaches the full average at this much more than at rest. Equal to the start (or 90) turns it off.")]
        [Range(0, 90)] public float creaseFullDegrees = 20;
        [Tooltip("Where the skin around a vertex has stretched to this many times its rest area, its authored normal starts turning into the plain surface normal (folds that open out, like an eyelid crease when the eye closes). 0 turns it off.")]
        [Min(0)] public float stretchStart = 0;
        [Tooltip("...and is the plain surface normal at this area ratio.")]
        [Min(0)] public float stretchFull = 0;
        public const float DefaultCreaseStart = 3, DefaultCreaseFull = 20; public const int CreaseIterations = 2;

        public struct VertexData
        {
            public uint triStart, triCount, self, neighbour;
            public Vector3 normal; public float restCrease;   // authored normal in the local frame (T, B, N); 1 - cos to the neighbour average at rest
            public Vector3 tangent; public float restArea;    // authored tangent xyz in the local frame; length of the summed face normals at rest
        }
        public const int VertexDataStride = 48;

        public sealed class Data
        {
            public VertexData[] vertices;
            public uint[] triangles;   // corner vertex indices, three per incident triangle, grouped per welded vertex
            public int recomputed, welded;
            public bool hasTangents;
        }

        SkinnedMeshRenderer skinned;
        Mesh built;
        internal GraphicsBuffer vertexBuffer, triangleBuffer, tempBuffer;
        internal Data data;
        internal int normalOffset, tangentOffset, stride;
        public string Problem { get; private set; }
        public int RecomputedVertices => data?.recomputed ?? 0;

        void OnEnable()
        {
            skinned = GetComponent<SkinnedMeshRenderer>();
            skinned.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            if (!Active.Contains(this)) Active.Add(this);
        }

        void OnDisable() { Active.Remove(this); Release(); }

        void Release()
        {
            vertexBuffer?.Release(); vertexBuffer = null;
            triangleBuffer?.Release(); triangleBuffer = null;
            tempBuffer?.Release(); tempBuffer = null;
            built = null; data = null;
        }

        /// <summary>Builds the GPU data for the current mesh if needed. False (with <see cref="Problem"/>) when unusable.</summary>
        internal bool Prepare()
        {
            if (!skinned || !skinned.sharedMesh || !skinned.enabled || !skinned.gameObject.activeInHierarchy) return false;
            var mesh = skinned.sharedMesh;
            if (built == mesh) return data != null;
            Release(); built = mesh; Problem = null;
            if (mesh.GetVertexAttributeStream(UnityEngine.Rendering.VertexAttribute.Normal) != 0 ||
                mesh.GetVertexAttributeFormat(UnityEngine.Rendering.VertexAttribute.Position) != UnityEngine.Rendering.VertexAttributeFormat.Float32 ||
                mesh.GetVertexAttributeFormat(UnityEngine.Rendering.VertexAttribute.Normal) != UnityEngine.Rendering.VertexAttributeFormat.Float32)
            { Problem = "needs float32 position and normal in vertex stream 0"; return false; }
            data = Build(mesh, weldTolerance);
            normalOffset = mesh.GetVertexAttributeOffset(UnityEngine.Rendering.VertexAttribute.Normal);
            tangentOffset = data.hasTangents ? mesh.GetVertexAttributeOffset(UnityEngine.Rendering.VertexAttribute.Tangent) : 0;
            stride = 0;   // taken from the skinned buffer at dispatch
            vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.vertices.Length, VertexDataStride);
            tempBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, 4 * data.vertices.Length), 16);
            vertexBuffer.SetData(data.vertices);
            triangleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, data.triangles.Length), 4);
            if (data.triangles.Length > 0) triangleBuffer.SetData(data.triangles);
            return true;
        }

        /// <summary>The renderer's deformed vertex buffer for this frame (dispose the returned handle), or null.</summary>
        internal GraphicsBuffer AcquireSkinnedBuffer() => skinned ? skinned.GetVertexBuffer() : null;

        /// <summary>Crease thresholds as 1 - cos (start, end) for the kernel.</summary>
        internal static Vector2 CreaseRange(float startDegrees, float fullDegrees)
            => new Vector2(1 - Mathf.Cos(startDegrees * Mathf.Deg2Rad), 1 - Mathf.Cos(Mathf.Max(fullDegrees, startDegrees) * Mathf.Deg2Rad));
        internal Vector2 Crease => CreaseRange(creaseStartDegrees, creaseFullDegrees);
        /// <summary>Area ratios (start, full) of the stretch flattening; (0, 0) when off.</summary>
        internal Vector2 Stretch => stretchStart > 0 && stretchFull > stretchStart ? new Vector2(stretchStart, stretchFull) : Vector2.zero;

        /// <summary>Setup data: welded adjacency and each vertex's authored normal and tangent in its local rest frame.</summary>
        public static Data Build(Mesh mesh, float weldTolerance = 1e-6f)
        {
            int n = mesh.vertexCount;
            var positions = mesh.vertices; var normals = mesh.normals; var tangents = mesh.tangents;
            bool hasTangents = tangents != null && tangents.Length == n;
            var weld = new int[n]; Array.Fill(weld, -1);
            var keys = new Dictionary<(int, long, long, long), int>();
            var reps = new List<int>(); var welded = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                var t = mesh.GetTriangles(s);
                foreach (int v in t)
                {
                    if (weld[v] >= 0) continue;
                    var p = positions[v];
                    var key = (s, (long)Math.Round(p.x / weldTolerance), (long)Math.Round(p.y / weldTolerance), (long)Math.Round(p.z / weldTolerance));
                    if (!keys.TryGetValue(key, out int w)) { w = reps.Count; keys.Add(key, w); reps.Add(v); }
                    weld[v] = w;
                }
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int a = weld[t[i]], b = weld[t[i + 1]], c = weld[t[i + 2]];
                    if (a != b && b != c && a != c) { welded.Add(a); welded.Add(b); welded.Add(c); }
                }
            }
            int count = reps.Count, triCount = welded.Count / 3;
            var start = new int[count + 1];
            foreach (int w in welded) start[w + 1]++;
            for (int i = 0; i < count; i++) start[i + 1] += start[i];
            var list = new int[welded.Count]; var fill = (int[])start.Clone();
            for (int t = 0; t < triCount; t++) for (int k = 0; k < 3; k++) list[fill[welded[3 * t + k]]++] = t;

            var triangles = new uint[welded.Count * 3];   // per welded vertex: its incident triangles as representative corners
            var frames = new (Vector3 t, Vector3 b, Vector3 n, bool ok)[count]; var areas = new float[count];
            var neighbour = new int[count];
            for (int w = 0; w < count; w++)
            {
                var sum = Vector3.zero; neighbour[w] = -1;
                for (int j = start[w]; j < start[w + 1]; j++)
                {
                    int t = list[j]; int a = welded[3 * t], b = welded[3 * t + 1], c = welded[3 * t + 2];
                    triangles[3 * j] = (uint)reps[a]; triangles[3 * j + 1] = (uint)reps[b]; triangles[3 * j + 2] = (uint)reps[c];
                    Vector3 pa = positions[reps[a]], pb = positions[reps[b]], pc = positions[reps[c]];
                    sum += Vector3.Cross(pb - pa, pc - pa);
                    if (neighbour[w] < 0) neighbour[w] = a != w ? a : b;
                }
                frames[w] = Frame(sum, neighbour[w] >= 0 ? positions[reps[neighbour[w]]] - positions[reps[w]] : Vector3.zero); areas[w] = sum.magnitude;
            }

            var result = new Data { vertices = new VertexData[n], triangles = triangles, welded = count, hasTangents = hasTangents };
            for (int v = 0; v < n; v++)
            {
                int w = weld[v];
                if (w < 0 || !frames[w].ok) continue;
                var f = frames[w]; var nv = normals[v];
                var d = new VertexData
                {
                    triStart = (uint)start[w], triCount = (uint)(start[w + 1] - start[w]), self = (uint)reps[w], neighbour = (uint)reps[neighbour[w]],
                    normal = new Vector3(Vector3.Dot(nv, f.t), Vector3.Dot(nv, f.b), Vector3.Dot(nv, f.n)), restArea = areas[w],
                };
                if (hasTangents) { var tv = (Vector3)tangents[v]; d.tangent = new Vector3(Vector3.Dot(tv, f.t), Vector3.Dot(tv, f.b), Vector3.Dot(tv, f.n)); }
                result.vertices[v] = d; result.recomputed++;
            }
            // Rest crease: the authored normal against the average of its neighbourhood's authored normals.
            for (int v = 0; v < n; v++)
            {
                var d = result.vertices[v]; if (d.triCount == 0) continue;
                var avg = Vector3.zero;
                for (uint i = 0; i < 3 * d.triCount; i++) avg += normals[triangles[3 * d.triStart + i]];
                if (avg.sqrMagnitude > 1e-12f) d.restCrease = 1 - Vector3.Dot(normals[v].normalized, avg.normalized);
                result.vertices[v] = d;
            }
            return result;
        }

        static (Vector3 t, Vector3 b, Vector3 n, bool ok) Frame(Vector3 normalSum, Vector3 edge)
        {
            float len = normalSum.magnitude;
            if (len < 1e-20f) return default;
            var n = normalSum / len;
            var e = edge - n * Vector3.Dot(edge, n);
            float el = e.magnitude;
            if (el < 1e-12f) return default;
            var t = e / el;
            return (t, Vector3.Cross(n, t), n, true);
        }

        /// <summary>CPU reference of the compute kernels (MetaHumanSkinnedNormals.compute): normals and tangent xyz for the
        /// given deformed positions, crease pass included. Vertices without data keep the values passed in.</summary>
        public static void Evaluate(Data data, Vector3[] deformed, Vector3[] normals, Vector3[] tangents,
                                    float creaseStartDegrees = DefaultCreaseStart, float creaseFullDegrees = DefaultCreaseFull, float stretchStart = 0, float stretchFull = 0)
        {
            int n = data.vertices.Length;
            var tn = new Vector3[n]; var tt = new Vector3[n]; var ok = new bool[n];
            for (int v = 0; v < n; v++)
            {
                var d = data.vertices[v]; if (d.triCount == 0) continue;
                var sum = Vector3.zero;
                for (uint i = 0; i < d.triCount; i++)
                {
                    uint j = 3 * (d.triStart + i);
                    var a = deformed[data.triangles[j]];
                    sum += Vector3.Cross(deformed[data.triangles[j + 1]] - a, deformed[data.triangles[j + 2]] - a);
                }
                var f = Frame(sum, deformed[d.neighbour] - deformed[d.self]);
                if (!f.ok) continue;
                var ln = d.normal;
                if (stretchStart > 0 && stretchFull > stretchStart && d.restArea > 0)
                    ln = Vector3.Lerp(ln, Vector3.forward, Mathf.Clamp01((sum.magnitude / d.restArea - stretchStart) / (stretchFull - stretchStart)));
                tn[v] = (ln.x * f.t + ln.y * f.b + ln.z * f.n).normalized;
                tt[v] = (d.tangent.x * f.t + d.tangent.y * f.b + d.tangent.z * f.n).normalized;
                ok[v] = true;
            }
            var range = CreaseRange(creaseStartDegrees, creaseFullDegrees);
            for (int iteration = 0; iteration < CreaseIterations; iteration++)
            {
                var nn = new Vector3[n]; var nt = new Vector3[n];
                for (int v = 0; v < n; v++)
                {
                    if (!ok[v]) continue;
                    var d = data.vertices[v]; var nv = tn[v];
                    var avg = Vector3.zero;
                    for (uint i = 0; i < 3 * d.triCount; i++) { uint c = data.triangles[3 * d.triStart + i]; if (ok[c]) avg += tn[c]; }
                    float blend = 0;
                    if (avg.sqrMagnitude > 1e-12f)
                    {
                        avg.Normalize();
                        float crease = 1 - Vector3.Dot(nv, avg);
                        blend = Mathf.Clamp01((crease - d.restCrease - range.x) / Mathf.Max(range.y - range.x, 1e-5f));
                        if (blend > 0) nv = Vector3.Lerp(nv, avg, blend).normalized;
                    }
                    var t = tt[v];
                    if (blend > 0) { var o = t - nv * Vector3.Dot(t, nv); if (o.sqrMagnitude > 1e-12f) t = o.normalized; }
                    nn[v] = nv; nt[v] = t;
                }
                tn = nn; tt = nt;
            }
            for (int v = 0; v < n; v++)
            {
                if (!ok[v]) continue;
                normals[v] = tn[v];
                if (tangents != null && data.hasTangents) tangents[v] = tt[v];
            }
        }
    }
}

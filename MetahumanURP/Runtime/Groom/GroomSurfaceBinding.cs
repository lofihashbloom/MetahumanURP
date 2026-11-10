using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("MetaHuman.Tests")]

namespace MetaHuman.Groom
{
    /// <summary>
    /// Makes a short groom strand mesh (e.g. eyebrows) follow the deforming face skin. Each strand vertex is
    /// attached at rest to its closest face-skin triangle, keeping its full offset, strand direction (NORMAL)
    /// and material frame (TANGENT xyz) in that triangle's frame; every update rebuilds them from the deformed
    /// triangles. UVs, width and coverage attributes are unchanged. Unlike CardSurfaceDeformer, strands are not
    /// projected onto the skin. CPU: one face BakeMesh per face and frame, shared by every binding of that face that
    /// updates itself in Play mode (UpdateNow always bakes), and a Burst job over the strand vertices.
    /// Bind() captures the current face pose as rest, so call it before any expression is applied.
    /// </summary>
    [DefaultExecutionOrder(1000), DisallowMultipleComponent]
    public sealed class GroomSurfaceBinding : MonoBehaviour
    {
        public SkinnedMeshRenderer face;
        public MeshFilter strands;
        [Tooltip("Strand vertices farther than this from the face skin (metres) fail the bind.")]
        [Min(0)] public float maximumDistance = .02f;
        [Tooltip("Attach every point of a strand to its root's triangle, so the strand turns rigidly with the skin at its root " +
                 "(eyelashes: with per-point binding, lash points nearest the eyelid fold ride the fold skin as a blink unrolls it " +
                 "and the lashes stand up through the closed lid). A strand is a run of vertices whose UV0.y (root 0 to tip 1) never " +
                 "decreases (ribbon meshes list each point twice, once per side).")]
        public bool rigidStrands;

        struct Attachment { public int a, b, c; public Vector3 barycentric, offset, direction, frame; public float frameW; }

        NativeArray<Attachment> attachments;
        Mesh source, runtime, faceBake;
        readonly List<Vector3> faceVertices = new List<Vector3>();
        NativeArray<Vector3> positions, directions;
        NativeArray<Vector4> frames;
        bool hasDirections, hasFrames;
        public bool Bound => attachments.IsCreated;
        public int BoundVertices => attachments.IsCreated ? attachments.Length : 0;
        public float MaximumBindDistance { get; private set; }
        public int UpdatedFrames { get; private set; }

        /// <summary>Attaches the strand mesh to the face in its current (rest) pose.</summary>
        public void Bind()
        {
            Release();
            if (!face || !face.sharedMesh || !strands || !strands.sharedMesh) throw new InvalidOperationException("Assign the face renderer and the strand mesh.");
            source = strands.sharedMesh;
            if (!source.isReadable) throw new InvalidOperationException("The strand mesh must be readable.");
            faceBake = new Mesh { name = "MetaHuman face bake (groom surface binding)", hideFlags = HideFlags.DontSave };
            face.BakeMesh(faceBake);
            faceBake.GetVertices(faceVertices);
            var faceMesh = face.sharedMesh;
            int skin = 0;
            for (int i = 1; i < faceMesh.subMeshCount; i++) if (faceMesh.GetIndexCount(i) > faceMesh.GetIndexCount(skin)) skin = i;
            var triangles = faceMesh.GetTriangles(skin);

            var toFace = face.transform.worldToLocalMatrix * strands.transform.localToWorldMatrix;
            var points = source.vertices;
            var strandDirections = source.normals;
            var materialFrames = source.tangents;
            var bounds = new Bounds(toFace.MultiplyPoint3x4(points[0]), Vector3.zero);
            foreach (var p in points) bounds.Encapsulate(toFace.MultiplyPoint3x4(p));
            bounds.Expand(maximumDistance * 2);
            // Only triangles near the strands are candidates; brute force over that set.
            var candidates = new List<int>();
            for (int t = 0; t < triangles.Length; t += 3)
            {
                var tb = new Bounds(faceVertices[triangles[t]], Vector3.zero);
                tb.Encapsulate(faceVertices[triangles[t + 1]]); tb.Encapsulate(faceVertices[triangles[t + 2]]);
                if (tb.Intersects(bounds)) candidates.Add(t);
            }
            if (candidates.Count == 0) throw new InvalidOperationException("No face triangles near the strands.");
            var grid = new TriangleGrid(faceVertices, triangles, candidates, bounds);

            var bound = new Attachment[points.Length];
            MaximumBindDistance = 0;
            // Rigid strands: each vertex takes its strand root's triangle, barycentrics and surface point.
            var along = new List<Vector2>(); source.GetUVs(0, along);
            bool rigid = rigidStrands && along.Count == points.Length;
            int rootT = 0; Vector3 rootBary = default, rootPoint = default;
            for (int i = 0; i < points.Length; i++)
            {
                var p = toFace.MultiplyPoint3x4(points[i]);
                int bestT; Vector3 bestBary, bestPoint; float best;
                if (rigid && i > 0 && along[i].y >= along[i - 1].y) { bestT = rootT; bestBary = rootBary; bestPoint = rootPoint; best = 0; }
                else
                {
                    grid.Closest(p, out bestT, out bestBary, out bestPoint, out best);
                    rootT = bestT; rootBary = bestBary; rootPoint = bestPoint;
                }
                float distance = Mathf.Sqrt(best);
                if (distance > maximumDistance) throw new InvalidOperationException($"Strand vertex {i} is {distance:F4} m from the face skin.");
                MaximumBindDistance = Mathf.Max(MaximumBindDistance, distance);
                int a = triangles[bestT], b = triangles[bestT + 1], c = triangles[bestT + 2];
                var inverse = Quaternion.Inverse(Frame(faceVertices[a], faceVertices[b], faceVertices[c]));
                var dir = strandDirections.Length > 0 ? toFace.MultiplyVector(strandDirections[i]) : Vector3.zero;
                var mf = materialFrames.Length > 0 ? materialFrames[i] : Vector4.zero;
                bound[i] = new Attachment
                {
                    a = a, b = b, c = c, barycentric = bestBary,
                    offset = inverse * (p - bestPoint),
                    direction = inverse * dir,
                    frame = inverse * toFace.MultiplyVector(mf),
                    frameW = mf.w
                };
            }
            runtime = Instantiate(source);
            runtime.name = source.name + " (surface bound)";
            runtime.hideFlags = HideFlags.DontSave;
            runtime.MarkDynamic();
            strands.sharedMesh = runtime;
            attachments = new NativeArray<Attachment>(bound, Allocator.Persistent);
            positions = new NativeArray<Vector3>(points.Length, Allocator.Persistent);
            directions = new NativeArray<Vector3>(points.Length, Allocator.Persistent);
            frames = new NativeArray<Vector4>(points.Length, Allocator.Persistent);
            hasDirections = strandDirections.Length > 0; hasFrames = materialFrames.Length > 0;
        }

        // One baked face per renderer and frame for the bindings that update themselves (LateUpdate).
        sealed class FaceBake { public int frame = -1; public Mesh mesh; public NativeArray<Vector3> vertices; public int users; }
        static readonly Dictionary<SkinnedMeshRenderer, FaceBake> shared = new Dictionary<SkinnedMeshRenderer, FaceBake>();
        FaceBake sharedBake;

        static void Read(Mesh baked, ref NativeArray<Vector3> vertices)
        {
            if (!vertices.IsCreated || vertices.Length != baked.vertexCount)
            {
                if (vertices.IsCreated) vertices.Dispose();
                vertices = new NativeArray<Vector3>(baked.vertexCount, Allocator.Persistent);
            }
            using var data = Mesh.AcquireReadOnlyMeshData(baked);
            data[0].GetVertices(vertices);
        }

        NativeArray<Vector3> ownVertices;

        [BurstCompile]
        struct FollowJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Vector3> face;
            [ReadOnly] public NativeArray<Attachment> attachments;
            public float4x4 toStrands;
            [WriteOnly] public NativeArray<Vector3> positions, directions;
            [WriteOnly] public NativeArray<Vector4> frames;

            public void Execute(int i)
            {
                var at = attachments[i];
                float3 a = face[at.a], b = face[at.b], c = face[at.c];
                // The triangle frame as a basis (the matrix of Frame's rotation): z the normal, x and y in the plane.
                float3 tangent = b - a, normal = math.cross(tangent, c - a);
                float tangentSq = math.lengthsq(tangent), normalSq = math.lengthsq(normal);
                float3 x, y, z;
                if (tangentSq < 1e-16f || normalSq < 1e-20f) { x = new float3(1, 0, 0); y = new float3(0, 1, 0); z = new float3(0, 0, 1); }   // collapsed triangle: unrotated
                else
                {
                    z = normal * math.rsqrt(normalSq);
                    y = math.normalize(math.cross(z, tangent * math.rsqrt(tangentSq)));
                    x = math.cross(y, z);
                }
                float3 bary = at.barycentric, offset = at.offset, direction = at.direction, frame = at.frame;
                float3 surface = a * bary.x + b * bary.y + c * bary.z;
                positions[i] = math.transform(toStrands, surface + x * offset.x + y * offset.y + z * offset.z);
                directions[i] = math.normalizesafe(math.rotate(toStrands, x * direction.x + y * direction.y + z * direction.z));
                float3 f = math.rotate(toStrands, x * frame.x + y * frame.y + z * frame.z);
                frames[i] = new Vector4(f.x, f.y, f.z, at.frameW);
            }
        }

        /// <summary>Rebuilds the strand mesh from the current face pose (bakes the face).</summary>
        public void UpdateNow()
        {
            if (!attachments.IsCreated) throw new InvalidOperationException("Call Bind() first.");
            face.BakeMesh(faceBake);
            Read(faceBake, ref ownVertices);
            Follow(ownVertices);
        }

        void Follow(NativeArray<Vector3> faceNow)
        {
            new FollowJob
            {
                face = faceNow, attachments = attachments, positions = positions, directions = directions, frames = frames,
                toStrands = strands.transform.worldToLocalMatrix * face.transform.localToWorldMatrix,
            }.Schedule(attachments.Length, 2048).Complete();
            runtime.SetVertices(positions);
            if (hasDirections) runtime.SetNormals(directions);
            if (hasFrames) runtime.SetTangents(frames);
            runtime.RecalculateBounds();
            UpdatedFrames++;
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || !attachments.IsCreated) return;
            if (sharedBake == null)
            {
                if (!shared.TryGetValue(face, out sharedBake))
                    shared[face] = sharedBake = new FaceBake { mesh = new Mesh { name = "MetaHuman face bake (shared groom surface bindings)", hideFlags = HideFlags.DontSave } };
                sharedBake.users++;
            }
            if (sharedBake.frame != Time.frameCount)
            {
                face.BakeMesh(sharedBake.mesh);
                Read(sharedBake.mesh, ref sharedBake.vertices);
                sharedBake.frame = Time.frameCount;
            }
            Follow(sharedBake.vertices);
        }

        public void Release()
        {
            if (strands && source && strands.sharedMesh == runtime) strands.sharedMesh = source;
            Destroy(runtime); Destroy(faceBake);
            runtime = faceBake = null;
            if (attachments.IsCreated) attachments.Dispose();
            if (positions.IsCreated) positions.Dispose();
            if (directions.IsCreated) directions.Dispose();
            if (frames.IsCreated) frames.Dispose();
            if (ownVertices.IsCreated) ownVertices.Dispose();
            attachments = default; positions = directions = default; frames = default; ownVertices = default;
            if (sharedBake != null && --sharedBake.users == 0)
            {
                Destroy(sharedBake.mesh);
                if (sharedBake.vertices.IsCreated) sharedBake.vertices.Dispose();
                foreach (var pair in shared) if (pair.Value == sharedBake) { shared.Remove(pair.Key); break; }
            }
            sharedBake = null;
        }

        void OnDisable() => Release();

        static void Destroy(UnityEngine.Object o)
        {
            if (!o) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else DestroyImmediate(o);
        }

        static Quaternion Frame(Vector3 a, Vector3 b, Vector3 c)
        {
            var tangent = b - a;
            var normal = Vector3.Cross(tangent, c - a);
            if (tangent.sqrMagnitude < 1e-16f || normal.sqrMagnitude < 1e-20f) throw new InvalidOperationException("A face attachment triangle collapsed.");
            normal *= 1f / Mathf.Sqrt(normal.sqrMagnitude);
            tangent *= 1f / Mathf.Sqrt(tangent.sqrMagnitude);
            return Quaternion.LookRotation(normal, Vector3.Cross(normal, tangent));
        }

        /// <summary>Closest point on triangle abc to p (Ericson, Real-Time Collision Detection 5.1.5), with barycentrics.</summary>
        /// <summary>
        /// Exact closest candidate triangle through a uniform grid: each triangle is listed in every cell its bounds
        /// overlap, and rings of cells around the query are searched outwards until the best distance is no larger
        /// than the distance to the unsearched space. Ties go to the lowest triangle index, which is what a scan of
        /// the candidates in order picks, so the result matches a brute-force search exactly.
        /// </summary>
        internal sealed class TriangleGrid
        {
            readonly List<Vector3> v; readonly int[] tri; readonly List<int> candidates;
            readonly Vector3 min; readonly float cell; readonly int nx, ny, nz;
            readonly int[] start, items, stamp;
            int query;

            public TriangleGrid(List<Vector3> vertices, int[] triangles, List<int> candidates, Bounds points)
            {
                v = vertices; tri = triangles; this.candidates = candidates;
                var all = points; float extent = 0;
                foreach (int t in candidates) { var tb = TriangleBounds(t); all.Encapsulate(tb); extent += Mathf.Max(tb.size.x, Mathf.Max(tb.size.y, tb.size.z)); }
                float largest = Mathf.Max(all.size.x, Mathf.Max(all.size.y, all.size.z));
                // About a triangle per cell, at most 128 cells along the longest axis.
                cell = Mathf.Max(extent / candidates.Count, largest / 128, 1e-6f);
                min = all.min;
                nx = Mathf.FloorToInt(all.size.x / cell) + 1; ny = Mathf.FloorToInt(all.size.y / cell) + 1; nz = Mathf.FloorToInt(all.size.z / cell) + 1;
                start = new int[nx * ny * nz + 1];
                for (int pass = 0; pass < 2; pass++)
                {
                    var cursor = pass == 1 ? (int[])start.Clone() : null;
                    for (int k = 0; k < candidates.Count; k++)
                    {
                        var tb = TriangleBounds(candidates[k]);
                        Index(tb.min, out int x0, out int y0, out int z0); Index(tb.max, out int x1, out int y1, out int z1);
                        for (int z = z0; z <= z1; z++) for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                        {
                            int c = (z * ny + y) * nx + x;
                            if (pass == 0) start[c + 1]++; else items[cursor[c]++] = k;
                        }
                    }
                    if (pass == 0) { for (int c = 0; c < nx * ny * nz; c++) start[c + 1] += start[c]; items = new int[start[nx * ny * nz]]; }
                }
                stamp = new int[candidates.Count];
            }

            Bounds TriangleBounds(int t)
            {
                var b = new Bounds(v[tri[t]], Vector3.zero); b.Encapsulate(v[tri[t + 1]]); b.Encapsulate(v[tri[t + 2]]); return b;
            }

            void Index(Vector3 p, out int x, out int y, out int z)
            {
                x = Mathf.Clamp((int)((p.x - min.x) / cell), 0, nx - 1);
                y = Mathf.Clamp((int)((p.y - min.y) / cell), 0, ny - 1);
                z = Mathf.Clamp((int)((p.z - min.z) / cell), 0, nz - 1);
            }

            public void Closest(Vector3 p, out int bestT, out Vector3 bestBary, out Vector3 bestPoint, out float bestSq)
            {
                query++; bestT = -1; bestBary = bestPoint = default; bestSq = float.MaxValue;
                Index(p, out int cx, out int cy, out int cz);
                int rings = Mathf.Max(nx, Mathf.Max(ny, nz));
                for (int r = 0; r <= rings; r++)
                {
                    for (int z = cz - r; z <= cz + r; z++)
                    {
                        if (z < 0 || z >= nz) continue;
                        for (int y = cy - r; y <= cy + r; y++)
                        {
                            if (y < 0 || y >= ny) continue;
                            // Inside the shell's y/z faces every x is new; elsewhere only the two x faces are.
                            int step = Math.Abs(z - cz) == r || Math.Abs(y - cy) == r ? 1 : Math.Max(1, 2 * r);
                            for (int x = cx - r; x <= cx + r; x += step)
                            {
                                if (x < 0 || x >= nx) continue;
                                int c = (z * ny + y) * nx + x;
                                for (int s = start[c]; s < start[c + 1]; s++)
                                {
                                    int k = items[s];
                                    if (stamp[k] == query) continue;
                                    stamp[k] = query;
                                    int t = candidates[k];
                                    var q = GroomSurfaceBinding.Closest(p, v[tri[t]], v[tri[t + 1]], v[tri[t + 2]], out var bary);
                                    float d = (q - p).sqrMagnitude;
                                    if (d < bestSq || (d == bestSq && t < bestT)) { bestSq = d; bestT = t; bestBary = bary; bestPoint = q; }
                                }
                            }
                        }
                    }
                    if (bestT < 0) continue;
                    // A triangle not met yet lies wholly outside the searched cube of cells.
                    float reach = float.MaxValue;
                    for (int a = 0; a < 3; a++)
                    {
                        int ca = a == 0 ? cx : a == 1 ? cy : cz;
                        float lo = min[a] + (ca - r) * cell, hi = min[a] + (ca + r + 1) * cell;
                        reach = Mathf.Min(reach, Mathf.Min(p[a] - lo, hi - p[a]));
                    }
                    if (bestSq < reach * reach) return;
                }
            }
        }

        public static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 barycentric)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) { barycentric = new Vector3(1, 0, 0); return a; }
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) { barycentric = new Vector3(0, 1, 0); return b; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) { float v = d1 / (d1 - d3); barycentric = new Vector3(1 - v, v, 0); return a + v * ab; }
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) { barycentric = new Vector3(0, 0, 1); return c; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) { float w = d2 / (d2 - d6); barycentric = new Vector3(1 - w, 0, w); return a + w * ac; }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) { float w = (d4 - d3) / (d4 - d3 + (d5 - d6)); barycentric = new Vector3(0, 1 - w, w); return b + w * (c - b); }
            float denom = 1f / (va + vb + vc);
            float vv = vb * denom, ww = vc * denom;
            barycentric = new Vector3(1 - vv - ww, vv, ww);
            return a + ab * vv + ac * ww;
        }
    }
}

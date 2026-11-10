using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetaHuman.Editor.Groom
{
    /// <summary>Original static card approximation: root/flow clustering, mean-curve strips and actual projected-strand coverage.</summary>
    public static class GroomClusterCardBaker
    {
        [Serializable] public sealed class Settings
        {
            [Range(1, 256)] public int cards = 128;
            [Range(4, 64)] public int stations = 24;
            public int tileWidth = 128, tileHeight = 256;
            public float minimumHalfWidth = .0001f;
        }
        public sealed class Result
        {
            public Mesh mesh;
            public Texture2D coverage;
            public int sourceStrands, cardCount, projectedSegments;
            public int[] sourceCurveToCard;
            public float maximumOffPlaneDistance, rootMeanSquareOffPlaneDistance;
            public float[] cardMaximumHalfWidths;
        }

        public static Result Build(GroomCurveData source, Matrix4x4 sourceToLocal, Settings settings = null)
        {
            settings ??= new Settings();
            if (source == null || source.strands == null || source.positions == null || source.widths == null) throw new ArgumentNullException(nameof(source));
            if (source.strands.Length < 1 || source.strands.Length > 1000000 || source.positions.Length < 2 || source.positions.Length > 10000000 || source.widths.Length != source.positions.Length)
                throw new ArgumentException("Invalid source array lengths.");
            int nextPoint = 0;
            foreach (var strand in source.strands)
            {
                if (strand.first != nextPoint || strand.count < 2 || strand.count > source.positions.Length - nextPoint || strand.group < 0 || strand.group > 1023 ||
                    !Finite(strand.width) || strand.width < 0 || !Finite(strand.rootUV.x) || !Finite(strand.rootUV.y))
                    throw new ArgumentException("Invalid source strand range, group or width.");
                nextPoint += strand.count;
            }
            if (nextPoint != source.positions.Length) throw new ArgumentException("Source curves must cover the full point buffer.");
            for (int i = 0; i < source.positions.Length; i++)
            {
                Vector3 p = source.positions[i];
                if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z) || !Finite(source.widths[i]) || source.widths[i] < 0)
                    throw new ArgumentException("Source points and nonnegative diameters must be finite.");
            }
            int k = settings.cards, n = settings.stations, tw = settings.tileWidth, th = settings.tileHeight;
            if (k < 1 || k > 256 || n < 4 || n > 64 || tw < 16 || tw > 512 || th < 16 || th > 512 ||
                !Finite(settings.minimumHalfWidth) || settings.minimumHalfWidth <= 0) throw new ArgumentException("Invalid card bake settings.");
            for (int entry = 0; entry < 16; entry++) if (!Finite(sourceToLocal[entry])) throw new ArgumentException("Nonfinite source transform.");
            if (Mathf.Abs(sourceToLocal.m30) > 1e-7f || Mathf.Abs(sourceToLocal.m31) > 1e-7f || Mathf.Abs(sourceToLocal.m32) > 1e-7f || Mathf.Abs(sourceToLocal.m33 - 1) > 1e-7f)
                throw new ArgumentException("A source-to-local affine transform is required.");
            Vector3 sx = sourceToLocal.MultiplyVector(Vector3.right), sy = sourceToLocal.MultiplyVector(Vector3.up), sz = sourceToLocal.MultiplyVector(Vector3.forward);
            float scale = sx.magnitude;
            if (scale <= 0 || !Finite(scale) || Mathf.Abs(sy.magnitude / scale - 1) > 1e-4f || Mathf.Abs(sz.magnitude / scale - 1) > 1e-4f ||
                Mathf.Abs(Vector3.Dot(sx.normalized, sy.normalized)) > 1e-4f || Mathf.Abs(Vector3.Dot(sx.normalized, sz.normalized)) > 1e-4f || Mathf.Abs(Vector3.Dot(sy.normalized, sz.normalized)) > 1e-4f)
                throw new ArgumentException("Card width transport requires a finite rigid/uniform-scale source transform.");
            var ids = new List<int>();
            for (int i = 0; i < source.strands.Length; i++) if (!source.strands[i].guide) ids.Add(i);
            if (ids.Count == 0) throw new ArgumentException("No render strands.");
            k = Mathf.Min(k, ids.Count);
            var samples = new Vector3[ids.Count * n]; var diameters = new float[samples.Length];
            for (int i = 0; i < ids.Count; i++) Resample(source, ids[i], sourceToLocal, scale, n, samples, diameters, i * n);
            // Deterministic farthest-first seeds in root/mid/tip feature space; no random state or engine clustering code.
            var centers = new Vector3[k * 3]; var nearest = new float[ids.Count];
            for (int i = 0; i < nearest.Length; i++) nearest[i] = float.PositiveInfinity;
            int seed = 0;
            for (int c = 0; c < k; c++)
            {
                CopyFeature(samples, seed * n, n, centers, c * 3);
                float farthest = -1;
                for (int i = 0; i < ids.Count; i++) { nearest[i] = Mathf.Min(nearest[i], Distance(samples, i * n, n, centers, c * 3)); if (nearest[i] > farthest) { farthest = nearest[i]; seed = i; } }
            }
            var assignment = new int[ids.Count]; var counts = new int[k];
            for (int iteration = 0; iteration < 3; iteration++)
            {
                Array.Clear(counts, 0, k); var sums = new Vector3[centers.Length];
                for (int i = 0; i < ids.Count; i++)
                {
                    float best = float.PositiveInfinity; int chosen = 0;
                    for (int c = 0; c < k; c++) { float d = Distance(samples, i * n, n, centers, c * 3); if (d < best) { best = d; chosen = c; } }
                    assignment[i] = chosen; counts[chosen]++;
                    sums[chosen * 3] += samples[i * n]; sums[chosen * 3 + 1] += samples[i * n + n / 2]; sums[chosen * 3 + 2] += samples[i * n + n - 1];
                }
                for (int c = 0; c < k; c++) if (counts[c] > 0) for (int j = 0; j < 3; j++) centers[c * 3 + j] = sums[c * 3 + j] / counts[c];
            }
            // Compact empty clusters rather than emitting zero-area cards.
            var remap = new int[k]; int used = 0;
            for (int c = 0; c < k; c++) remap[c] = counts[c] > 0 ? used++ : -1;
            for (int i = 0; i < assignment.Length; i++) assignment[i] = remap[assignment[i]];
            k = used; counts = new int[k]; var mean = new Vector3[k * n];
            for (int i = 0; i < ids.Count; i++) { int c = assignment[i]; counts[c]++; for (int s = 0; s < n; s++) mean[c * n + s] += samples[i * n + s]; }
            for (int c = 0; c < k; c++) for (int s = 0; s < n; s++) mean[c * n + s] /= counts[c];
            var sides = new Vector3[mean.Length]; var tangents = new Vector3[mean.Length];
            for (int c = 0; c < k; c++)
            {
                Vector3 initialT = Direction(mean, c * n, n, 0), side = Vector3.Cross(initialT, Mathf.Abs(initialT.y) < .9f ? Vector3.up : Vector3.right).normalized;
                // Principal root-spread direction constrained to strand flow. Seed from a nonzero
                // covariance column: an arbitrary perpendicular axis may be exactly orthogonal to all spread.
                Vector3 covX = Vector3.zero, covY = Vector3.zero, covZ = Vector3.zero;
                for (int i = 0; i < ids.Count; i++) if (assignment[i] == c)
                {
                    Vector3 delta = samples[i * n] - mean[c * n]; delta -= initialT * Vector3.Dot(delta, initialT);
                    covX += delta * delta.x; covY += delta * delta.y; covZ += delta * delta.z;
                }
                Vector3 seedAxis = covX.sqrMagnitude >= covY.sqrMagnitude ? covX : covY;
                if (covZ.sqrMagnitude > seedAxis.sqrMagnitude) seedAxis = covZ;
                if (seedAxis.sqrMagnitude > 1e-20f) side = seedAxis.normalized;
                for (int pass = 0; pass < 10; pass++)
                {
                    Vector3 sum = covX * side.x + covY * side.y + covZ * side.z;
                    if (sum.sqrMagnitude > 1e-20f) side = sum.normalized;
                }
                for (int s = 0; s < n; s++)
                {
                    var t = Direction(mean, c * n, n, s); var candidate = side - t * Vector3.Dot(side, t);
                    if (candidate.sqrMagnitude < 1e-12f) candidate = Vector3.Cross(t, Mathf.Abs(t.y) < .9f ? Vector3.up : Vector3.right);
                    side = candidate.normalized; sides[c * n + s] = side; tangents[c * n + s] = t;
                }
            }
            var half = new float[mean.Length]; for (int i = 0; i < half.Length; i++) half[i] = settings.minimumHalfWidth;
            double offPlaneSquare = 0; float offPlaneMax = 0;
            for (int i = 0; i < ids.Count; i++) for (int s = 0; s < n; s++)
            {
                int target = assignment[i] * n + s, point = i * n + s;
                Vector3 delta = samples[point] - mean[target];
                half[target] = Mathf.Max(half[target], Mathf.Abs(Vector3.Dot(delta, sides[target])) + diameters[point] * .5f);
                float error = Mathf.Abs(Vector3.Dot(delta, Vector3.Cross(sides[target], tangents[target]).normalized));
                offPlaneMax = Mathf.Max(offPlaneMax, error); offPlaneSquare += error * error;
            }
            int columns = Mathf.CeilToInt(Mathf.Sqrt(k * (float)th / tw)), rows = Mathf.CeilToInt(k / (float)columns);
            int aw = columns * tw, ah = rows * th; const int gutter = 3;
            if (aw > SystemInfo.maxTextureSize || ah > SystemInfo.maxTextureSize) throw new ArgumentException("Coverage atlas exceeds the platform texture limit.");
            var alpha = new float[aw * ah]; int segments = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                int c = assignment[i], tileX = (c % columns) * tw, tileY = (c / columns) * th;
                for (int s = 0; s < n - 1; s++)
                {
                    int a = c * n + s, b = a + 1, pa = i * n + s;
                    float ua = .5f + .5f * Vector3.Dot(samples[pa] - mean[a], sides[a]) / half[a];
                    float ub = .5f + .5f * Vector3.Dot(samples[pa + 1] - mean[b], sides[b]) / half[b];
                    var p = new Vector2(tileX + gutter + ua * (tw - 2 * gutter), tileY + gutter + s / (float)(n - 1) * (th - 2 * gutter));
                    var q = new Vector2(tileX + gutter + ub * (tw - 2 * gutter), tileY + gutter + (s + 1f) / (n - 1) * (th - 2 * gutter));
                    float radius = .25f * (diameters[pa] / half[a] + diameters[pa + 1] / half[b]) * .5f * (tw - 2 * gutter);
                    Raster(alpha, aw, p, q, radius, tileX, tileY, tw, th); segments++;
                }
            }
            var vertices = new Vector3[k * n * 2]; var normals = new Vector3[vertices.Length]; var uv = new Vector2[vertices.Length]; var flow = new Vector4[vertices.Length];
            var triangles = new int[k * (n - 1) * 6]; var maxWidths = new float[k];
            for (int c = 0; c < k; c++) for (int s = 0; s < n; s++)
            {
                int m = c * n + s, v = m * 2; var normal = Vector3.Cross(sides[m], tangents[m]).normalized;
                maxWidths[c] = Mathf.Max(maxWidths[c], half[m]);
                for (int edge = 0; edge < 2; edge++)
                {
                    vertices[v + edge] = mean[m] + sides[m] * half[m] * (edge == 0 ? -1 : 1); normals[v + edge] = normal;
                    flow[v + edge] = new Vector4(tangents[m].x, tangents[m].y, tangents[m].z, 1);
                    uv[v + edge] = new Vector2(((c % columns) * tw + gutter + edge * (tw - 2 * gutter)) / (float)aw,
                        ((c / columns) * th + gutter + s / (float)(n - 1) * (th - 2 * gutter)) / ah);
                }
                if (s == n - 1) continue;
                int ti = (c * (n - 1) + s) * 6;
                triangles[ti] = v; triangles[ti + 1] = v + 2; triangles[ti + 2] = v + 1;
                triangles[ti + 3] = v + 1; triangles[ti + 4] = v + 2; triangles[ti + 5] = v + 3;
            }
            var pixels = new Color32[alpha.Length]; for (int i = 0; i < pixels.Length; i++) { byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha[i]) * 255); pixels[i] = new Color32(a, a, a, a); }
            var texture = new Texture2D(aw, ah, TextureFormat.RGBA32, false, true) { name = "GroomClusterCoverage", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels); texture.Apply(false, false);
            var mesh = new Mesh { name = "GroomClusterCards", indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = vertices; mesh.normals = normals; mesh.tangents = flow; mesh.uv = uv; mesh.triangles = triangles; mesh.RecalculateBounds();
            var map = new int[source.strands.Length]; for (int i = 0; i < map.Length; i++) map[i] = -1; for (int i = 0; i < ids.Count; i++) map[ids[i]] = assignment[i];
            return new Result { mesh = mesh, coverage = texture, sourceStrands = ids.Count, cardCount = k, projectedSegments = segments, sourceCurveToCard = map,
                maximumOffPlaneDistance = offPlaneMax, rootMeanSquareOffPlaneDistance = (float)Math.Sqrt(offPlaneSquare / samples.Length), cardMaximumHalfWidths = maxWidths };
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static void CopyFeature(Vector3[] p, int start, int n, Vector3[] target, int offset) { target[offset] = p[start]; target[offset + 1] = p[start + n / 2]; target[offset + 2] = p[start + n - 1]; }
        static float Distance(Vector3[] p, int start, int n, Vector3[] centers, int c) => (p[start] - centers[c]).sqrMagnitude + .15f * (p[start + n / 2] - centers[c + 1]).sqrMagnitude + .15f * (p[start + n - 1] - centers[c + 2]).sqrMagnitude;
        static Vector3 Direction(Vector3[] p, int first, int n, int s)
        { Vector3 direction = p[first + Mathf.Min(n - 1, s + 1)] - p[first + Mathf.Max(0, s - 1)]; return direction.sqrMagnitude > 1e-14f ? direction.normalized : Vector3.down; }
        static void Resample(GroomCurveData source, int index, Matrix4x4 transform, float scale, int n, Vector3[] output, float[] widths, int offset)
        {
            var strand = source.strands[index]; var arc = new float[strand.count];
            for (int s = 1; s < arc.Length; s++) arc[s] = arc[s - 1] + Vector3.Distance(source.positions[strand.first + s - 1], source.positions[strand.first + s]);
            if (arc[arc.Length - 1] <= 1e-8f) throw new ArgumentException("Zero-length source curve.");
            int edge = 0;
            for (int s = 0; s < n; s++)
            {
                float u = s / (float)(n - 1) * arc[arc.Length - 1]; while (edge < strand.count - 2 && arc[edge + 1] < u) edge++;
                float t = Mathf.InverseLerp(arc[edge], arc[edge + 1], u); int a = strand.first + edge;
                output[offset + s] = transform.MultiplyPoint3x4(Vector3.Lerp(source.positions[a], source.positions[a + 1], t));
                float width = Mathf.Lerp(source.widths[a], source.widths[a + 1], t); if (width <= 0) width = strand.width;
                if (!Finite(width) || width < 0) throw new ArgumentException("Invalid physical source diameter.");
                widths[offset + s] = width * scale;
            }
        }
        static void Raster(float[] alpha, int width, Vector2 a, Vector2 b, float radius, int tx, int ty, int tw, int th)
        {
            int minX = Mathf.Max(tx, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - radius - 1)), maxX = Mathf.Min(tx + tw - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + radius + 1));
            int minY = Mathf.Max(ty, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - radius - 1)), maxY = Mathf.Min(ty + th - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + radius + 1));
            Vector2 delta = b - a; float square = delta.sqrMagnitude;
            for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(x + .5f, y + .5f); float t = square > 1e-10f ? Mathf.Clamp01(Vector2.Dot(p - a, delta) / square) : 0;
                float coverage = Mathf.Clamp01(radius + .5f - Vector2.Distance(p, a + delta * t));
                int target = y * width + x; alpha[target] = 1 - (1 - alpha[target]) * (1 - coverage);
            }
        }
    }
}

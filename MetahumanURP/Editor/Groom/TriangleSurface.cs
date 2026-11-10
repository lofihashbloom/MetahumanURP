using System;
using System.Collections.Generic;
using UnityEngine;

namespace MetaHuman.Editor.Groom
{
    /// <summary>Neutral-face BVH used only during binding construction.</summary>
    public sealed class TriangleSurface
    {
        public struct Hit { public int a, b, c; public Vector3 point, barycentric; public float distance; }
        struct Triangle { public int a, b, c; public Bounds bounds; public Vector3 center; }
        sealed class Node { public Bounds bounds; public int start, count; public Node left, right; }
        readonly Vector3[] vertices;
        readonly Triangle[] triangles;
        readonly Node root;

        public TriangleSurface(Vector3[] vertices, int[] indices)
        {
            this.vertices = vertices;
            var valid = new List<Triangle>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                if (Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).sqrMagnitude < 1e-20f) continue;
                var bounds = new Bounds(vertices[a], Vector3.zero); bounds.Encapsulate(vertices[b]); bounds.Encapsulate(vertices[c]);
                valid.Add(new Triangle { a = a, b = b, c = c, bounds = bounds, center = bounds.center });
            }
            triangles = valid.ToArray();
            if (triangles.Length == 0) throw new ArgumentException("Surface has no nondegenerate triangles.");
            root = Build(0, triangles.Length);
        }

        Node Build(int start, int count)
        {
            var node = new Node { start = start, count = count, bounds = triangles[start].bounds };
            for (int i = start + 1; i < start + count; i++) node.bounds.Encapsulate(triangles[i].bounds);
            if (count <= 8) return node;
            var size = node.bounds.size; int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            Array.Sort(triangles, start, count, Comparer<Triangle>.Create((a, b) => a.center[axis].CompareTo(b.center[axis])));
            int half = count / 2;
            node.left = Build(start, half); node.right = Build(start + half, count - half);
            return node;
        }

        public Hit Closest(Vector3 point)
        {
            var hit = new Hit { distance = float.PositiveInfinity };
            Search(root, point, ref hit);
            return hit;
        }
        void Search(Node node, Vector3 point, ref Hit hit)
        {
            if (node.bounds.SqrDistance(point) >= hit.distance * hit.distance) return;
            if (node.left != null)
            {
                var first = node.left.bounds.SqrDistance(point) < node.right.bounds.SqrDistance(point) ? node.left : node.right;
                Search(first, point, ref hit); Search(first == node.left ? node.right : node.left, point, ref hit);
                return;
            }
            for (int i = node.start; i < node.start + node.count; i++)
            {
                var tri = triangles[i];
                var bary = ClosestBarycentric(point, vertices[tri.a], vertices[tri.b], vertices[tri.c]);
                var surface = vertices[tri.a] * bary.x + vertices[tri.b] * bary.y + vertices[tri.c] * bary.z;
                float distance = Vector3.Distance(point, surface);
                if (distance < hit.distance) hit = new Hit { a = tri.a, b = tri.b, c = tri.c, point = surface, barycentric = bary, distance = distance };
            }
        }

        public static Vector3 ClosestBarycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a; var ac = c - a; var ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return new Vector3(1, 0, 0);
            var bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return new Vector3(0, 1, 0);
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) { float v = d1 / (d1 - d3); return new Vector3(1 - v, v, 0); }
            var cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return new Vector3(0, 0, 1);
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) { float w = d2 / (d2 - d6); return new Vector3(1 - w, 0, w); }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return new Vector3(0, 1 - w, w);
            }
            float sum = va + vb + vc;
            if (Mathf.Abs(sum) < 1e-20f) throw new ArgumentException("Cannot project onto a degenerate triangle.");
            return new Vector3(va / sum, vb / sum, vc / sum);
        }
    }
}

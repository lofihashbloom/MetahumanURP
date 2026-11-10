using System;
using System.Collections.Generic;
using UnityEngine;

namespace MetaHuman.Editor.Groom
{
    /// <summary>Static, rigid-head preview. Source curves stay intact; no simulation or source binding reconstruction.</summary>
    public static class GroomRibbonBaker
    {
        public sealed class Result
        {
            public readonly List<Mesh> meshes = new List<Mesh>();
            public int renderStrands, guidesSkipped, vertices, triangles;
            public bool preservedSourceWidths, arclengthCoordinates, sourceMaterialFrame, sourceCurveSelection;
            public float[] groupMaximumSourceWidths;
        }
        public struct GroupSettings
        {
            public float widthCentimetres, rootScale, tipScale, sourceMaximumWidthCentimetres;
            // Retain the final control point for adjacent-direction evaluation,
            // while the visible polyline ends at the preceding point.
            public bool terminalTangentPoint;
        }

        // Position is the curve centre; normal stores its tangent; UV0 is (side, length),
        // UV1 is (diameter metres, stable strand random). The shader expands both sides.
        public static Result Build(GroomCurveData source, Matrix4x4 sourceToLocal, GroupSettings[] groups,
            float density = 1, int maximumPoints = 12, bool preserveSourceWidths = false, bool useArcLength = false,
            bool sourceMaterialFrame = false, IReadOnlyDictionary<int, float> sourceCurveSeeds = null,
            float[] sourcePointCoordinates = null)
        {
            if (source == null || groups == null || groups.Length == 0 || density <= 0 || density > 1 || float.IsNaN(density) || maximumPoints < 2 || maximumPoints > 64)
                throw new ArgumentException("Invalid groom build settings.");
            foreach (var group in groups)
                if (group.widthCentimetres <= 0 || float.IsNaN(group.widthCentimetres) || float.IsInfinity(group.widthCentimetres) ||
                    group.rootScale < 0 || group.tipScale < 0 || !float.IsFinite(group.rootScale) || !float.IsFinite(group.tipScale) ||
                    !float.IsFinite(group.sourceMaximumWidthCentimetres) || group.sourceMaximumWidthCentimetres < 0)
                    throw new ArgumentException("Invalid groom group width or taper.");
            if (sourceCurveSeeds != null)
                foreach (var entry in sourceCurveSeeds)
                    if (entry.Key < 0 || entry.Key >= source.strands.Length || source.strands[entry.Key].guide ||
                        !float.IsFinite(entry.Value) || entry.Value < 0 || entry.Value > 1)
                        throw new ArgumentException("Invalid source curve selection or seed.");
            if (sourcePointCoordinates != null && (sourcePointCoordinates.Length != source.positions.Length ||
                Array.Exists(sourcePointCoordinates, u => !float.IsFinite(u) || u < 0 || u > 1)))
                throw new ArgumentException("Invalid source point coordinates.");
            bool vertexWidths = preserveSourceWidths && source.widths != null && source.widths.Length == source.positions.Length
                && Array.Exists(source.widths, value => value > 0);
            bool curveWidths = preserveSourceWidths && !vertexWidths && Array.Exists(source.strands, strand => strand.width > 0);
            var maximumWidths = new float[groups.Length];
            if (vertexWidths || curveWidths)
                foreach (var strand in source.strands)
                {
                    if (strand.guide) continue;
                    if (strand.group >= groups.Length) throw new ArgumentException("Missing source groom group settings.");
                    if (vertexWidths)
                        for (int p = 0; p < strand.count; p++) maximumWidths[strand.group] = Mathf.Max(maximumWidths[strand.group], source.widths[strand.first + p]);
                    else maximumWidths[strand.group] = Mathf.Max(maximumWidths[strand.group], strand.width);
                }
            for (int i = 0; i < groups.Length; i++)
                if (groups[i].sourceMaximumWidthCentimetres > 0) maximumWidths[i] = groups[i].sourceMaximumWidthCentimetres;
            var result = new Result { preservedSourceWidths = vertexWidths || curveWidths,
                arclengthCoordinates = useArcLength, sourceMaterialFrame = sourceMaterialFrame,
                sourceCurveSelection = sourceCurveSeeds != null, groupMaximumSourceWidths = maximumWidths };
            var positions = new List<Vector3>(); var directions = new List<Vector3>();
            var uvs = new List<Vector2>(); var widths = new List<Vector2>(); var indices = new List<int>();
            var rootUVs = new List<Vector2>();
            var frames = new List<Vector4>();
            void Flush()
            {
                if (positions.Count == 0) return;
                var mesh = new Mesh { name = "GroomStrands_" + result.meshes.Count };
                mesh.SetVertices(positions); mesh.SetNormals(directions); mesh.SetUVs(0, uvs); mesh.SetUVs(1, widths); mesh.SetUVs(2, rootUVs);
                if (sourceMaterialFrame) mesh.SetTangents(frames);
                mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
                var bounds = mesh.bounds; bounds.Expand(0.02f); mesh.bounds = bounds;
                result.vertices += positions.Count; result.triangles += indices.Count / 3; result.meshes.Add(mesh);
                positions.Clear(); directions.Clear(); uvs.Clear(); widths.Clear(); rootUVs.Clear(); frames.Clear(); indices.Clear();
            }
            for (int s = 0; s < source.strands.Length; s++)
            {
                var strand = source.strands[s];
                if (strand.guide) { result.guidesSkipped++; continue; }
                float sourceSeed = 0;
                if (sourceCurveSeeds != null && !sourceCurveSeeds.TryGetValue(s, out sourceSeed)) continue;
                uint hash = unchecked((uint)s * 747796405u + 2891336453u); hash = ((hash >> (int)((hash >> 28) + 4)) ^ hash) * 277803737u; hash = (hash >> 22) ^ hash;
                float random = (hash & 0xffffff) / 16777216f;
                if (random >= density) continue;
                if (strand.group >= groups.Length) throw new ArgumentException("Missing source groom group settings.");
                var settings = groups[strand.group];
                float[] distance = null;
                if (useArcLength)
                {
                    distance = new float[strand.count];
                    for (int p = 1; p < strand.count; p++) distance[p] = distance[p - 1]
                        + Vector3.Distance(source.positions[strand.first + p - 1], source.positions[strand.first + p]);
                }
                int count = Mathf.Min(strand.count, maximumPoints);
                if (positions.Count + count * 2 > 60000) Flush();
                int start = positions.Count;
                for (int p = 0; p < count; p++)
                {
                    float ordinal = (float)p / (count - 1);
                    int index = strand.first + Mathf.RoundToInt(ordinal * (strand.count - 1));
                    float t = sourcePointCoordinates != null ? sourcePointCoordinates[index] : distance != null && distance[strand.count - 1] > 0
                        ? distance[index - strand.first] / distance[strand.count - 1] : ordinal;
                    int previous = Mathf.Max(strand.first, index - 1), next = Mathf.Min(strand.first + strand.count - 1, index + 1);
                    var sourceTangent = source.positions[next] - source.positions[previous];
                    if (sourceMaterialFrame)
                    {
                        // Equal contribution from adjacent segment directions, independent
                        // of sample spacing. Endpoints have one contributing segment.
                        sourceTangent = (source.positions[index] - source.positions[previous]).normalized
                            + (source.positions[next] - source.positions[index]).normalized;
                    }
                    sourceTangent.Normalize();
                    if (sourceTangent.sqrMagnitude < .5f) sourceTangent = Vector3.forward;
                    var tangent = sourceToLocal.MultiplyVector(sourceTangent).normalized;
                    if (tangent.sqrMagnitude < 0.5f) tangent = Vector3.up;
                    // Independently construct the source-local material frame by
                    // rotating the nearest signed Z pole onto the strand direction.
                    // This quaternion construction avoids translating native basis code.
                    Vector3 frameX = Vector3.zero;
                    if (sourceMaterialFrame)
                    {
                        var pole = sourceTangent.z >= 0 ? Vector3.forward : Vector3.back;
                        var rotation = Quaternion.FromToRotation(pole, sourceTangent);
                        frameX = sourceToLocal.MultiplyVector(rotation * Vector3.right).normalized;
                    }
                    var centre = sourceToLocal.MultiplyPoint3x4(source.positions[index]);
                    float diameter = settings.widthCentimetres * 0.01f * Mathf.Lerp(settings.rootScale, settings.tipScale, t);
                    if (vertexWidths || curveWidths)
                    {
                        float raw = vertexWidths ? source.widths[index] : strand.width;
                        diameter *= maximumWidths[strand.group] > 0 ? raw / maximumWidths[strand.group] : 0;
                    }
                    for (int side = 0; side < 2; side++)
                    {
                        positions.Add(centre); directions.Add(tangent);
                        uvs.Add(new Vector2(side == 0 ? -1 : 1, t)); widths.Add(new Vector2(diameter, sourceCurveSeeds != null ? sourceSeed : random));
                        rootUVs.Add(strand.rootUV);
                        if (sourceMaterialFrame) frames.Add(new Vector4(frameX.x, frameX.y, frameX.z, 1));
                    }
                    if (p >= count - 1 - (settings.terminalTangentPoint ? 1 : 0)) continue;
                    int a = start + p * 2;
                    indices.Add(a); indices.Add(a + 2); indices.Add(a + 1);
                    indices.Add(a + 1); indices.Add(a + 2); indices.Add(a + 3);
                }
                result.renderStrands++;
            }
            Flush();
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MetaHuman.Groom;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MetaHuman.Editor.Groom
{
    public static class CardSurfaceBaker
    {
        [Serializable] public sealed class Report
        {
            public string source, binding;
            public int vertices;
            public float meanProjectionDistance, maximumProjectionDistance, clearance;
            public string method = "Spatial neutral fit and CPU triangle-frame attachment; independent approximation of groom binding";
        }

        public static List<Report> Bind(GameObject root, SkinnedMeshRenderer face, string output)
        {
            var reports = new List<Report>();
            var cards = root.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.sharedMaterials.Any(m => m && m.shader && m.shader.name == "MetaHuman/URP/HairCards")).ToArray();
            if (cards.Length == 0) return reports;
            if (!face || !face.sharedMesh) throw new InvalidOperationException("Hair cards require a face mesh for binding.");
            var faceMesh = face.sharedMesh;
            int skinSection = Enumerable.Range(0, faceMesh.subMeshCount).OrderByDescending(i => faceMesh.GetIndexCount(i)).First();
            var vertices = faceMesh.vertices;
            var normals = faceMesh.normals;
            var surface = new TriangleSurface(vertices, faceMesh.GetTriangles(skinSection));
            Directory.CreateDirectory(output + "/Bindings"); AssetDatabase.Refresh();
            var targets = new List<CardSurfaceDeformer.Target>();
            foreach (var card in cards)
            {
                var filter = card.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) throw new InvalidOperationException("Hair card mesh is missing: " + card.name);
                // Facial surface projection is appropriate for short eyebrow
                // cards. Preserve head hair/beard silhouettes and attach those
                // original meshes rigidly until a root-based groom bind exists.
                if (card.name.IndexOf("eyebrow", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    var head = face.bones.FirstOrDefault(b => b && b.name == "head");
                    if (!head) head = face.rootBone;
                    if (!head) throw new InvalidOperationException("A head bone is required for rigid hair-card attachment.");
                    bool visible = card.gameObject.activeInHierarchy;
                    card.transform.SetParent(head, true);
                    card.gameObject.SetActive(visible);
                    reports.Add(new Report { source = card.name, vertices = filter.sharedMesh.vertexCount,
                        method = "Original card geometry attached to the head bone; no face projection, deformation or simulation" });
                    continue;
                }
                string safeName = string.Concat(card.name.Where(c => char.IsLetterOrDigit(c) || c == '_'));
                string assetPath = output + "/Bindings/" + safeName + ".asset";
                string meshPath = output + "/Bindings/" + safeName + ".mesh";
                var binding = AssetDatabase.LoadAssetAtPath<CardSurfaceAsset>(assetPath);
                if (!binding) { binding = ScriptableObject.CreateInstance<CardSurfaceAsset>(); AssetDatabase.CreateAsset(binding, assetPath); }
                // A clone of an imported mesh also clones its non-readable flag.
                // Build a readable generated mesh for the CPU runtime reference path.
                var originalMesh = filter.sharedMesh;
                var fitted = new Mesh { name = safeName + "_SurfaceFitted", indexFormat = originalMesh.indexFormat };
                fitted.vertices = originalMesh.vertices;
                fitted.colors32 = originalMesh.colors32;
                for (int channel = 0; channel < 8; channel++)
                {
                    var uv = new List<Vector4>(); originalMesh.GetUVs(channel, uv);
                    if (uv.Count > 0) fitted.SetUVs(channel, uv);
                }
                fitted.subMeshCount = originalMesh.subMeshCount;
                for (int section = 0; section < originalMesh.subMeshCount; section++)
                    fitted.SetIndices(originalMesh.GetIndices(section), originalMesh.GetTopology(section), section, false);
                var points = fitted.vertices;
                var attachments = new CardSurfaceAsset.Attachment[points.Length];
                var frames = new Quaternion[points.Length];
                var toFace = face.transform.worldToLocalMatrix * card.transform.localToWorldMatrix;
                float maxDistance = 0, sumDistance = 0;
                const float clearance = 0.0007f;
                for (int i = 0; i < points.Length; i++)
                {
                    var original = toFace.MultiplyPoint3x4(points[i]);
                    var hit = surface.Closest(original);
                    if (hit.distance > 0.05f) throw new InvalidOperationException($"Hair card vertex is {hit.distance:F3} m from the face. Supply a fitted card mesh: {card.name}");
                    var n = Vector3.Cross(vertices[hit.b] - vertices[hit.a], vertices[hit.c] - vertices[hit.a]);
                    var vertexNormal = normals[hit.a] * hit.barycentric.x + normals[hit.b] * hit.barycentric.y + normals[hit.c] * hit.barycentric.z;
                    float sign = Vector3.Dot(n, vertexNormal) >= 0 ? 1 : -1;
                    var frame = CardSurfaceMath.Frame(vertices[hit.a], vertices[hit.b], vertices[hit.c], sign);
                    float height = Mathf.Clamp(Vector3.Dot(original - hit.point, frame * Vector3.forward), clearance, 0.002f);
                    points[i] = hit.point + frame * new Vector3(0, 0, height);
                    frames[i] = frame;
                    attachments[i] = new CardSurfaceAsset.Attachment {
                        a = hit.a, b = hit.b, c = hit.c, barycentric = hit.barycentric, normalSign = sign,
                        offset = new Vector3(0, 0, height)
                    };
                    maxDistance = Mathf.Max(maxDistance, hit.distance); sumDistance += hit.distance;
                }
                fitted.vertices = points; fitted.RecalculateNormals(); fitted.RecalculateTangents(); fitted.RecalculateBounds();
                var fittedNormals = fitted.normals; var fittedTangents = fitted.tangents;
                for (int i = 0; i < attachments.Length; i++)
                {
                    var bindingData = attachments[i]; var inverse = Quaternion.Inverse(frames[i]);
                    bindingData.normal = inverse * fittedNormals[i];
                    bindingData.tangent = inverse * new Vector3(fittedTangents[i].x, fittedTangents[i].y, fittedTangents[i].z);
                    bindingData.tangentSign = fittedTangents[i].w;
                    attachments[i] = bindingData;
                }
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (existing) { EditorUtility.CopySerialized(fitted, existing); Object.DestroyImmediate(fitted); fitted = existing; }
                else AssetDatabase.CreateAsset(fitted, meshPath);
                binding.faceMesh = faceMesh; binding.fittedMesh = fitted; binding.attachments = attachments;
                binding.sourceCardName = card.name; binding.maximumProjectionDistance = maxDistance;
                binding.meanProjectionDistance = sumDistance / points.Length; binding.clearance = clearance;
                EditorUtility.SetDirty(binding); EditorUtility.SetDirty(fitted);

                var bound = new GameObject(card.name + "_SurfaceBound");
                bound.transform.SetParent(face.transform, false);
                bound.SetActive(card.gameObject.activeInHierarchy);
                var boundFilter = bound.AddComponent<MeshFilter>(); boundFilter.sharedMesh = fitted;
                var renderer = bound.AddComponent<MeshRenderer>(); renderer.sharedMaterials = card.sharedMaterials;
                renderer.shadowCastingMode = card.shadowCastingMode; renderer.receiveShadows = card.receiveShadows;
                card.enabled = false;
                targets.Add(new CardSurfaceDeformer.Target { binding = binding, filter = boundFilter });
                reports.Add(new Report { source = card.name, binding = assetPath, vertices = points.Length,
                    meanProjectionDistance = binding.meanProjectionDistance, maximumProjectionDistance = maxDistance, clearance = clearance });
            }
            if (targets.Count > 0)
            {
                var deformer = root.GetComponent<CardSurfaceDeformer>();
                if (!deformer) deformer = root.AddComponent<CardSurfaceDeformer>();
                deformer.face = face; deformer.targets = targets.ToArray();
            }
            return reports;
        }
    }
}

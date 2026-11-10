using System;
using System.Collections.Generic;
using UnityEngine;

namespace MetaHuman.Groom
{
    /// <summary>Exact triangle attachments after a spatial rest-pose fit; CPU reference implementation.</summary>
    [ExecuteAlways, DefaultExecutionOrder(1000), DisallowMultipleComponent]
    public sealed class CardSurfaceDeformer : MonoBehaviour
    {
        [Serializable] public struct Target { public CardSurfaceAsset binding; public MeshFilter filter; }
        public SkinnedMeshRenderer face;
        public Target[] targets = Array.Empty<Target>();
        public bool previewInEditor;
        [NonSerialized] Mesh faceBake;
        [NonSerialized] Mesh[] meshes;
        [NonSerialized] Target[] initializedTargets;
        [NonSerialized] Vector3[][] positions, normals;
        [NonSerialized] Vector4[][] tangents;
        readonly List<Vector3> faceVertices = new List<Vector3>();
        public int UpdatedFrames { get; private set; }

        void Initialize()
        {
            if (!face || !face.sharedMesh) throw new InvalidOperationException("Assign the card binding's face renderer.");
            faceBake = new Mesh { name = "MetaHuman face CPU reference" };
            initializedTargets = (Target[])targets.Clone();
            meshes = new Mesh[targets.Length];
            positions = new Vector3[targets.Length][];
            normals = new Vector3[targets.Length][];
            tangents = new Vector4[targets.Length][];
            try
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    var target = targets[i];
                    if (!target.binding || !target.filter || target.binding.faceMesh != face.sharedMesh)
                        throw new InvalidOperationException("Card binding/face mesh mismatch. Reimport to rebuild this LOD's bindings.");
                    if (target.binding.attachments.Length != target.binding.fittedMesh.vertexCount)
                        throw new InvalidOperationException("Card attachment count does not match the fitted mesh.");
                    meshes[i] = Instantiate(target.binding.fittedMesh);
                    meshes[i].name = target.binding.fittedMesh.name + " (runtime)";
                    meshes[i].hideFlags = HideFlags.DontSave;
                    meshes[i].MarkDynamic();
                    target.filter.sharedMesh = meshes[i];
                    int count = target.binding.attachments.Length;
                    positions[i] = new Vector3[count]; normals[i] = new Vector3[count]; tangents[i] = new Vector4[count];
                }
            }
            catch { Release(); throw; }
        }

        public void UpdateNow()
        {
            if (!face || !face.sharedMesh) throw new InvalidOperationException("The bound face renderer is missing.");
            bool changed = meshes == null || initializedTargets == null || meshes.Length != targets.Length;
            if (!changed)
                for (int i = 0; i < targets.Length; i++)
                    if (!targets[i].binding || !targets[i].filter || initializedTargets[i].binding != targets[i].binding || initializedTargets[i].filter != targets[i].filter
                        || positions[i].Length != targets[i].binding.attachments.Length || targets[i].filter.sharedMesh != meshes[i]
                        || targets[i].binding.faceMesh != face.sharedMesh) changed = true;
            if (changed) { Release(); Initialize(); }
            bool anyActive = false;
            foreach (var target in targets) if (target.filter && target.filter.gameObject.activeInHierarchy) anyActive = true;
            if (!anyActive) return;
            // One bake serves all active card groups. This is a CPU quality baseline,
            // not a GPU groom renderer or a zero-cost skin-weight approximation.
            face.BakeMesh(faceBake);
            faceBake.GetVertices(faceVertices);
            for (int i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                if (!target.filter || !target.filter.gameObject.activeInHierarchy) continue;
                var bindings = target.binding.attachments;
                var toTarget = target.filter.transform.worldToLocalMatrix * face.transform.localToWorldMatrix;
                var normalMatrix = toTarget.inverse.transpose;
                for (int v = 0; v < bindings.Length; v++)
                {
                    var binding = bindings[v];
                    var a = faceVertices[binding.a]; var b = faceVertices[binding.b]; var c = faceVertices[binding.c];
                    var edge = b - a;
                    var faceNormal = Vector3.Cross(edge, c - a) * binding.normalSign;
                    if (edge.sqrMagnitude < 1e-16f || faceNormal.sqrMagnitude < 1e-20f)
                        throw new InvalidOperationException("A card attachment triangle collapsed.");
                    var faceTangent = edge * (1f / Mathf.Sqrt(edge.sqrMagnitude));
                    faceNormal *= 1f / Mathf.Sqrt(faceNormal.sqrMagnitude);
                    var faceBitangent = Vector3.Cross(faceNormal, faceTangent);
                    var surface = a * binding.barycentric.x + b * binding.barycentric.y + c * binding.barycentric.z;
                    var offset = faceTangent * binding.offset.x + faceBitangent * binding.offset.y + faceNormal * binding.offset.z;
                    var normal = faceTangent * binding.normal.x + faceBitangent * binding.normal.y + faceNormal * binding.normal.z;
                    var tangentLocal = faceTangent * binding.tangent.x + faceBitangent * binding.tangent.y + faceNormal * binding.tangent.z;
                    positions[i][v] = toTarget.MultiplyPoint3x4(surface + offset);
                    normals[i][v] = normalMatrix.MultiplyVector(normal).normalized;
                    var tangent = toTarget.MultiplyVector(tangentLocal).normalized;
                    tangents[i][v] = new Vector4(tangent.x, tangent.y, tangent.z, binding.tangentSign);
                }
                meshes[i].vertices = positions[i];
                meshes[i].normals = normals[i];
                meshes[i].tangents = tangents[i];
                meshes[i].RecalculateBounds();
            }
            UpdatedFrames++;
        }

        void LateUpdate()
        {
            if ((!Application.IsPlaying(gameObject) && !previewInEditor) || !face || targets.Length == 0) return;
            UpdateNow();
        }

        public void Release()
        {
            if (meshes != null)
                for (int i = 0; i < meshes.Length; i++)
                {
                    if (initializedTargets != null && i < initializedTargets.Length && initializedTargets[i].filter && initializedTargets[i].binding)
                        initializedTargets[i].filter.sharedMesh = initializedTargets[i].binding.fittedMesh;
                    if (meshes[i])
                    {
                        if (Application.isPlaying) Destroy(meshes[i]); else DestroyImmediate(meshes[i]);
                    }
                }
            if (faceBake) { if (Application.isPlaying) Destroy(faceBake); else DestroyImmediate(faceBake); }
            meshes = null; faceBake = null; initializedTargets = null;
        }
        void OnDisable() => Release();
        void OnDestroy() => Release();
    }
}

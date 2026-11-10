using System;
using System.Collections.Generic;
using MetaHuman.Groom;
using UnityEngine;

namespace MetaHuman.Editor.Groom
{
    /// <summary>Explicit setup API. Does not save scenes/assets or manufacture a card representation.</summary>
    public static class GroomLodSetup
    {
        public static GroomLodController Configure(GameObject root, Renderer[] strands, Renderer[] cards, Camera referenceCamera = null)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            strands ??= Array.Empty<Renderer>(); cards ??= Array.Empty<Renderer>();
            var seen = new HashSet<Renderer>(); bool hasBounds = false; Bounds bounds = default;
            foreach (var group in new[] { strands, cards }) foreach (var renderer in group)
            {
                if (!renderer) throw new ArgumentException("LOD groups must not contain null renderers.");
                if (!seen.Add(renderer)) throw new ArgumentException("A renderer occurs more than once in LOD groups.");
                if (!renderer.transform.IsChildOf(root.transform) && renderer.transform != root.transform) throw new ArgumentException("LOD renderers must be descendants of the controller root.");
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    if (skin) mesh = skin.sharedMesh;
                }
                else
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter) mesh = filter.sharedMesh;
                }
                if (!mesh || mesh.vertexCount < 3) throw new ArgumentException("Each representation requires real mesh geometry.");
                var local = GroomLodMath.TransformBounds(renderer is SkinnedMeshRenderer skinned ? skinned.localBounds : mesh.bounds,
                    root.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix);
                if (!hasBounds) { bounds = local; hasBounds = true; } else bounds.Encapsulate(local);
            }
            if (!hasBounds) throw new ArgumentException("At least one real representation is required.");
            var controller = root.GetComponent<GroomLodController>();
            if (!controller) controller = root.AddComponent<GroomLodController>();
            controller.Restore(); controller.automaticSelection = false;
            controller.strandRenderers = (Renderer[])strands.Clone(); controller.cardRenderers = (Renderer[])cards.Clone();
            controller.referenceCamera = referenceCamera; controller.localReferenceBounds = bounds;
            return controller;
        }
    }
}

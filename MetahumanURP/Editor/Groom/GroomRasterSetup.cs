using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetaHuman.Editor.Groom
{
    /// <summary>Explicit setup for a material shared by a known set of static groom meshes.</summary>
    public static class GroomRasterSetup
    {
        /// <summary>
        /// Null coverageMeshes selects unit compensation. Otherwise every supplied mesh must
        /// contain positive finite coverage scales in the fourth UV set, channel3.x/TEXCOORD3 (Mesh.uv4). Supply all meshes sharing
        /// the material; call again when that set or its geometry changes. Camera MSAA and the
        /// renderer feature are configured separately.
        /// </summary>
        public static void ConfigureRectangle(Material material, float minimumPixels = 1,
            IReadOnlyList<Mesh> coverageMeshes = null)
        {
            if (!material || !material.HasProperty("_RectangleCoverageDataBound"))
                throw new ArgumentException("Use a groom material with the compensated rectangle profile.", nameof(material));
            // Fail closed before inspecting data. Missing GPU attributes do not have a
            // portable numeric default and must never be used as a presence test.
            material.SetFloat("_RectangleCoverageDataBound", 0);
            if (!float.IsFinite(minimumPixels) || minimumPixels < .25f || minimumPixels > 2)
                throw new ArgumentOutOfRangeException(nameof(minimumPixels));
            if (coverageMeshes != null)
            {
                if (coverageMeshes.Count == 0) throw new ArgumentException("Supply every coverage mesh.", nameof(coverageMeshes));
                var data = new List<Vector2>();
                foreach (var mesh in coverageMeshes)
                {
                    if (!mesh || !mesh.isReadable || mesh.vertexCount == 0 ||
                        !mesh.HasVertexAttribute(VertexAttribute.TexCoord3) ||
                        mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord3) < 1)
                        throw new ArgumentException("All meshes must expose readable coverage data in channel3/TEXCOORD3 (Mesh.uv4).", nameof(coverageMeshes));
                    data.Clear(); mesh.GetUVs(3, data);
                    if (data.Count != mesh.vertexCount || data.Exists(v => !float.IsFinite(v.x) || v.x <= 0))
                        throw new ArgumentException("Every vertex requires a positive finite channel3.x (Mesh.uv4) coverage scale.", nameof(coverageMeshes));
                }
            }
            material.SetFloat("_CoverageMode", 1);
            material.SetFloat("_RasterProfile", 1);
            material.SetFloat("_RectangleMinimumPixels", minimumPixels);
            material.SetFloat("_RectangleCoverageSource", coverageMeshes == null ? 0 : 1);
            material.SetFloat("_RectangleCoverageScale", 1);
            material.SetFloat("_GroomRadianceDepthBandPixels", 0);
            material.SetFloat("_RectangleCoverageDataBound", coverageMeshes == null ? 0 : 1);
        }
    }
}

// Hardware-rasterized ribbon meshes of the deferred groom (GroomDeferredFeature, compute raster mode). Meshes whose
// segments project long on screen (settings.longSegmentPixels) skip the compute rasterizer, which pays a long thin
// ribbon's length serially per thread, and are drawn here with their own index buffer (vertices shared between
// neighbouring segments). The vertex stage ignores the mesh attributes: vertex 2p + side is placed from the point
// buffer by the same MakeCorner as the compute rasterizer (_PointBase: the mesh's first point).
//   LongOpticalDepth: optical depth -log(1 - alpha), additive (R16F), depth-tested against the opaque depth.
//   LongNearest: nearest strand point and position along its segment (point << 8 | along), opaque with depth write
//   into a copy of the opaque depth (hidden-surface removal keeps the nearest).
// The shade pass takes the nearer of the compute and hardware results and sums both optical depths.
Shader "Hidden/MetaHuman/GroomLongSegments"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off
        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/metahuman-urp/Runtime/Shaders/GroomRibbon.hlsl"
        uint _PointBase;
        struct LongVaryings
        {
            float4 positionCS : SV_POSITION;
            float alpha : TEXCOORD0;
            float parity : TEXCOORD1;                   // point index parity, interpolated along the segment
            nointerpolation uint pointIndex : TEXCOORD2;  // provoking vertex: the segment's first point
            nointerpolation float baseAlpha : TEXCOORD3;
        };
        LongVaryings LongVertex(uint vertexID : SV_VertexID)
        {
            LongVaryings o;
            uint p = _PointBase + vertexID / 2;
            MHGroomPoint pt = _Points[p];
            MHGroomMesh mesh = _Meshes[pt.meshFlags & 0xFFFFFFu];
            float keep = mesh.raster.y;
            o.alpha = 0; o.parity = p & 1u; o.pointIndex = p; o.baseAlpha = mesh.raster.z;
            // Strand LOD: dropped strands leave the clip volume, as StrandVertex does.
            if (keep < 1 && frac(MHGroomUnpackHalf2(pt.randomCov).x * 7.31 + .17) >= keep) { o.positionCS = float4(0, 0, -2, 1); return o; }
            // Vertex pair (2p, 2p + 1): left and right ribbon edges.
            Corner c = MakeCorner(pt, mesh, (vertexID & 1u) ? 1 : -1, keep);
            o.positionCS = c.clip;
            o.alpha = c.alpha;
            return o;
        }
        float LongOpticalDepth(LongVaryings i) : SV_Target
        {
            float alpha = saturate(i.alpha * i.baseAlpha);
            return alpha > 1e-6 ? -log(max(1 - min(alpha, .9999), .0001)) : 0;
        }
        uint LongNearest(LongVaryings i) : SV_Target
        {
            // Triangles (a, a+2, a+1) and (a+1, a+2, a+3) both start at a vertex of the segment's first point p.
            float along = abs(i.parity - float(i.pointIndex & 1u));
            return (i.pointIndex << 8) | uint(saturate(along) * 255 + .5);
        }
        ENDHLSL
        Pass
        {
            Name "LongOpticalDepth"
            ZWrite Off ZTest LEqual Blend One One
            HLSLPROGRAM
            #pragma vertex LongVertex
            #pragma fragment LongOpticalDepth
            ENDHLSL
        }
        Pass
        {
            Name "LongNearest"
            ZWrite On ZTest LEqual Blend Off
            HLSLPROGRAM
            #pragma vertex LongVertex
            #pragma fragment LongNearest
            ENDHLSL
        }
    }
}

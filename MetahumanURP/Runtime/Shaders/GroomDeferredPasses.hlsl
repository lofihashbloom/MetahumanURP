// Deferred groom passes (GroomDeferredFeature) for strand shaders. Include it inside the shader's HLSLINCLUDE after
// StrandRadiance. The host shader provides:
//   Attributes / Varyings with positionCS, positionWS, tangentWS, uv (y: position along the strand),
//   coverageRandom (y: strand random), materialFrameX and fog; Varyings StrandVertex(Attributes);
//   float3 StrandRadiance(Varyings); a float _MHGroomDeferredId in its UnityPerMaterial cbuffer.
// It adds the vertex entry StrandForwardPathVertex (forward-path groom passes cull themselves while the feature draws
// the camera), the fragment entries StrandDeferredAttributes (hardware raster attributes), StrandDeferredShade and
// StrandComputeShade (full-screen shade + composite), and the full-screen vertex StrandDeferredVertex.
#ifndef METAHUMAN_GROOM_DEFERRED_PASSES_INCLUDED
#define METAHUMAN_GROOM_DEFERRED_PASSES_INCLUDED
#include "Packages/metahuman-urp/Runtime/Shaders/GroomSegments.hlsl"

// Set by GroomDeferredFeature for the cameras it draws: the forward-path groom passes (drawn by
// GroomTransparencyFeature) cull themselves, so the deferred materials are not drawn twice.
float _MHGroomDeferredActive;
Varyings StrandForwardPathVertex(Attributes input)
{
    [branch] if (_MHGroomDeferredActive > .5)
    {
        Varyings culled = (Varyings)0;
        culled.positionCS = float4(2, 2, 2, 1);
        return culled;
    }
    return StrandVertex(input);
}
// Deferred groom (GroomDeferredFeature, prototype). The attribute pass rasterizes the same ribbons as the
// nearest-layer passes, opaque and without discard: the depth test (and the GPU's hidden-surface removal)
// keeps the nearest expanded ribbon per pixel, and only its shading inputs are written. The shade pass then
// evaluates StrandRadiance once per hair pixel from those inputs and composites it with the all-strand
// optical depth, as the nearest-layer composite does: colour * (1 - o) + radiance * o, o = 1 - exp(-tau).
uint StrandPackUnorm2(float2 v) { v = saturate(v); return (uint(v.x * 65535 + .5)) | (uint(v.y * 65535 + .5) << 16); }
float2 StrandUnpackUnorm2(uint v) { return float2(v & 65535u, v >> 16) / 65535.0; }
uint4 StrandDeferredAttributes(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    float3 tangent = normalize(float3(input.tangentWS));
    float frameLength2 = dot(input.materialFrameX, input.materialFrameX);
    uint frameValid = frameLength2 > 1e-20 ? 2u : 0u;
    float3 frame = frameValid ? input.materialFrameX * rsqrt(frameLength2) : tangent;
    return uint4(StrandPackUnorm2(PackNormalOctQuadEncode(tangent) * .5 + .5),
        StrandPackUnorm2(float2(input.uv.y, input.coverageRandom.y)),
        StrandPackUnorm2(PackNormalOctQuadEncode(frame) * .5 + .5),
        1u | frameValid | ((uint(_MHGroomDeferredId) & 255u) << 8));
}
Texture2D<uint4> _MHGroomDeferredAttributes;
TEXTURE2D_FLOAT(_MHGroomDeferredDepth);
TEXTURE2D(_MHGroomDeferredOpticalDepth);
struct DeferredVaryings { float4 positionCS : SV_POSITION; };
DeferredVaryings StrandDeferredVertex(uint vertexID : SV_VertexID)
{
    DeferredVaryings output;
    output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
    return output;
}
float4 StrandDeferredShade(DeferredVaryings input) : SV_Target
{
    uint2 pixel = uint2(input.positionCS.xy);
    uint4 packed = _MHGroomDeferredAttributes.Load(int3(pixel, 0));
    if ((packed.w & 1u) == 0 || ((packed.w >> 8) & 255u) != (uint(_MHGroomDeferredId) & 255u)) discard;
    float tau = LOAD_TEXTURE2D(_MHGroomDeferredOpticalDepth, pixel).r;
    if (tau <= 0) discard;
    float deviceDepth = LOAD_TEXTURE2D(_MHGroomDeferredDepth, pixel).r;
    float2 screenUV = (float2(pixel) + .5) / _ScaledScreenParams.xy;
    Varyings v = (Varyings)0;
    v.positionCS = input.positionCS;
    v.positionWS = ComputeWorldSpacePosition(screenUV, deviceDepth, UNITY_MATRIX_I_VP);
    v.tangentWS = UnpackNormalOctQuadEncode(StrandUnpackUnorm2(packed.x) * 2 - 1);
    float2 uvRandom = StrandUnpackUnorm2(packed.y);
    v.uv = float2(0, uvRandom.x);
    v.coverageRandom = half2(1, uvRandom.y);
    v.materialFrameX = (packed.w & 2u) ? UnpackNormalOctQuadEncode(StrandUnpackUnorm2(packed.z) * 2 - 1) : 0;
    v.fog = ComputeFogFactor(TransformWorldToHClip(v.positionWS).z);
    float opacity = 1 - exp(-min(tau, 80));
    return float4(StrandRadiance(v) * opacity, opacity);
}
// Compute-rasterized variant (GroomDeferredFeature RasterMode.Compute): the nearest segment and the position
// along it come from the compute rasterizer; its two points are interpolated as the ribbon's vertices are.
StructuredBuffer<uint> _MHGroomComputeOpticalDepth;
StructuredBuffer<uint> _MHGroomComputeNearestDepth;
StructuredBuffer<uint> _MHGroomComputeNearestId;
StructuredBuffer<uint2> _MHGroomComputeNearest64;   // 64-bit path: x = segment << 8 | along, y = asuint(depth)
float _MHGroomComputeMerged;
StructuredBuffer<MHGroomPoint> _MHGroomPoints;
StructuredBuffer<uint> _MHGroomSegments;
StructuredBuffer<MHGroomMesh> _MHGroomMeshes;
float4 _MHGroomComputeScreen;
// Meshes drawn by hardware (GroomLongSegments, long on-screen segments): their optical depth, nearest point (point << 8 |
// position along its segment) and its depth (a copy of the opaque depth they were tested and written into). Merged with
// the compute result when _MHGroomLongActive is set.
float _MHGroomLongActive;
TEXTURE2D(_MHGroomLongOpticalDepth);
Texture2D<uint> _MHGroomLongId;
TEXTURE2D_FLOAT(_MHGroomLongDepth);
float _MHGroomComputeGroup;     // shade group drawn (meshes of one material and one set of renderer properties)
float4 _MHGroomComputeRect;     // its screen rectangle in pixels (min xy, max xy; y down, as the rasterizer's pixels)
// Two triangles covering _MHGroomComputeRect (DrawProcedural, 6 vertices). Clip y is up and pixel rows go down
// (Metal and D3D conventions).
DeferredVaryings StrandComputeShadeVertex(uint vertexID : SV_VertexID)
{
    const uint2 corners[6] = { uint2(0, 0), uint2(1, 0), uint2(0, 1), uint2(0, 1), uint2(1, 0), uint2(1, 1) };
    uint2 corner = corners[vertexID % 6];
    float2 pixel = float2(corner.x ? _MHGroomComputeRect.z : _MHGroomComputeRect.x, corner.y ? _MHGroomComputeRect.w : _MHGroomComputeRect.y);
    float2 ndc = pixel / _MHGroomComputeScreen.xy * 2 - 1;
    DeferredVaryings output;
    output.positionCS = float4(ndc.x, -ndc.y, UNITY_NEAR_CLIP_VALUE, 1);
    return output;
}
float3 StrandWorldDirection(float4x4 m, uint packed) { return normalize(mul((float3x3)m, MHGroomOctDecode(MHGroomUnpackUnorm2(packed)))); }
float4 StrandComputeShade(DeferredVaryings input) : SV_Target
{
    uint2 pixel = uint2(input.positionCS.xy);
    uint index = pixel.y * (uint)_MHGroomComputeScreen.x + pixel.x;
    uint nearest;
    float deviceDepth;
    bool found, fromPoint = false;
    if (_MHGroomComputeMerged > .5)
    {
        uint2 merged = _MHGroomComputeNearest64[index];
        found = merged.y != 0;
        nearest = merged.x; deviceDepth = asfloat(merged.y);
    }
    else
    {
        nearest = _MHGroomComputeNearestId[index];
        found = nearest != MH_GROOM_EMPTY;
        deviceDepth = asfloat(_MHGroomComputeNearestDepth[index]);
    }
    float tau = _MHGroomComputeOpticalDepth[index] / MH_GROOM_TAU_SCALE;
    [branch] if (_MHGroomLongActive > .5)
    {
        float longTau = LOAD_TEXTURE2D(_MHGroomLongOpticalDepth, pixel).r;
        if (longTau > 0)
        {
            tau += longTau;
            // Reversed Z: the larger device depth is nearer.
            float longDepth = LOAD_TEXTURE2D(_MHGroomLongDepth, pixel).r;
            if (!found || longDepth > deviceDepth) { nearest = _MHGroomLongId.Load(int3(pixel, 0)); deviceDepth = longDepth; found = true; fromPoint = true; }
        }
    }
    if (!found || tau <= 0) discard;
    uint first = fromPoint ? nearest >> 8 : _MHGroomSegments[nearest >> 8];
    MHGroomPoint a = _MHGroomPoints[first], b = _MHGroomPoints[first + 1];
    MHGroomMesh mesh = _MHGroomMeshes[a.meshFlags & 0xFFFFFFu];
    if (mesh.info.x != (uint)_MHGroomComputeGroup) discard;
    float along = (nearest & 255u) / 255.0;
    float2 screenUV = (float2(pixel) + .5) / _ScaledScreenParams.xy;
    Varyings v = (Varyings)0;
    v.positionCS = input.positionCS;
    v.positionWS = ComputeWorldSpacePosition(screenUV, deviceDepth, UNITY_MATRIX_I_VP);
    v.uv = float2(0, lerp(MHGroomUnpackHalf2(a.diameterU).y, MHGroomUnpackHalf2(b.diameterU).y, along));
    float3 sway = MHGroomSwayVector(mul(mesh.localToWorld, float4(a.centre, 1)).xyz, v.uv.y, MHGroomUnpackHalf2(a.randomCov).x,
        mesh.swayPivot, mesh.swayAxisAngle, mesh.swayFine, mesh.swayFront);
    v.tangentWS = MHGroomSwayRotate(lerp(StrandWorldDirection(mesh.localToWorld, a.tangentOct), StrandWorldDirection(mesh.localToWorld, b.tangentOct), along), sway);
    v.coverageRandom = half2(1, MHGroomUnpackHalf2(a.randomCov).x);
    v.materialFrameX = (a.meshFlags & (1u << 24)) ? MHGroomSwayRotate(lerp(StrandWorldDirection(mesh.localToWorld, a.frameOct), StrandWorldDirection(mesh.localToWorld, b.frameOct), along), sway) : 0;
    v.fog = ComputeFogFactor(TransformWorldToHClip(v.positionWS).z);
    float opacity = 1 - exp(-min(tau, 80));
    return float4(StrandRadiance(v) * opacity, opacity);
}
#endif

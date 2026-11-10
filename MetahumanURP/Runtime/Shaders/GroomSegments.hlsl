// Shared layout of the deferred groom's compute rasterizer (GroomDeferredFeature, RasterMode.Compute).
// Points: one record per strand point (a ribbon mesh's vertex pair 2p, 2p+1). Segments: the global index of a
// segment's first point (the second is the next point). Per-pixel buffers: optical depth (fixed point), nearest
// device depth (asuint, reversed Z: larger is nearer) and the nearest segment (24 bits) with its position along it (8 bits).
#ifndef METAHUMAN_GROOM_SEGMENTS_INCLUDED
#define METAHUMAN_GROOM_SEGMENTS_INCLUDED
#include "Packages/com.unity.metahuman/Runtime/Shaders/GroomSway.hlsl"

struct MHGroomPoint
{
    float3 centre;      // object space
    uint diameterU;     // f16 diameter (m) | f16 position along the strand (0..1)
    uint randomCov;     // f16 strand random | f16 source coverage scale (mesh channel 3.x)
    uint tangentOct;    // object-space strand direction, octahedral unorm16 x2
    uint frameOct;      // object-space material frame X, octahedral unorm16 x2
    uint meshFlags;     // mesh index (24 bits) | bit 24: material frame valid
};

// One record per ribbon mesh (renderer), refreshed every frame: its transform and the raster parameters of its
// material with the renderer's property-block overrides (compensated-rectangle profile).
struct MHGroomMesh
{
    float4x4 localToWorld;
    float4 raster;      // rectangle minimum diameter (pixels), strand fraction kept (0..1], base alpha, uniform coverage scale
    uint4 info;         // x: shade group, y: coverage source (0 unit, 1 mesh channel 3, 2 uniform scale), zw: unused
    float4 swayPivot;   // GroomSway.hlsl: world pivot, tip exponent (the renderer's _MHGroomSwayPivot)
    float4 swayAxisAngle; // world rotation vector (rad), w = 1 when active (_MHGroomSwayAxisAngle)
    float4 swayFine;    // loose spring rotation vector, w = per-strand spread (_MHGroomSwayFine)
    float4 swayFront;   // face direction, w = front boost (_MHGroomSwayFront)
};

#define MH_GROOM_TAU_SCALE 65536.0
#define MH_GROOM_EMPTY 0xFFFFFFFFu

float2 MHGroomOctEncode(float3 n)
{
    n /= abs(n.x) + abs(n.y) + abs(n.z);
    float2 p = n.z >= 0 ? n.xy : (1 - abs(n.yx)) * (n.xy >= 0 ? 1 : -1);
    return p * .5 + .5;
}

float3 MHGroomOctDecode(float2 e)
{
    e = e * 2 - 1;
    float3 n = float3(e, 1 - abs(e.x) - abs(e.y));
    float t = saturate(-n.z);
    n.xy += n.xy >= 0 ? -t : t;
    return normalize(n);
}

uint MHGroomPackUnorm2(float2 v) { v = saturate(v); return uint(v.x * 65535 + .5) | (uint(v.y * 65535 + .5) << 16); }
float2 MHGroomUnpackUnorm2(uint v) { return float2(v & 65535u, v >> 16) / 65535.0; }
uint MHGroomPackHalf2(float2 v) { return f32tof16(v.x) | (f32tof16(v.y) << 16); }
float2 MHGroomUnpackHalf2(uint v) { return float2(f16tof32(v & 65535u), f16tof32(v >> 16)); }

#endif

// Shared per-frame state and ribbon rasterization of the deferred groom's compute kernels (32-bit and 64-bit variants).
#ifndef METAHUMAN_GROOM_RASTER_COMMON_INCLUDED
#define METAHUMAN_GROOM_RASTER_COMMON_INCLUDED
#include "Packages/com.unity.metahuman/Runtime/Shaders/GroomRibbon.hlsl"
// ---------------------------------------------------------------- per frame
RWStructuredBuffer<uint> _OpticalDepth;   // per pixel, fixed point
RWStructuredBuffer<uint> _NearestDepth;   // per pixel, asuint(device depth), 0 = none
RWStructuredBuffer<uint> _NearestId;      // per pixel, segment << 8 | position along it, MH_GROOM_EMPTY = none
Texture2D<float> _SceneDepth;
uint _SegmentTotal, _GroupsX;   // _SegmentTotal: segments dispatched (those of the compute-rasterized meshes)
uint _Diagnostic;   // 0 = normal, 1 = stop after the corners, 2 = pixel tests without atomics
// Compute-rasterized meshes as segment ranges (first segment, first dispatch index; ascending dispatch index): meshes
// on the hardware path are not dispatched at all.
StructuredBuffer<uint2> _SegmentRanges;
uint _RangeCount;
// One bit per segment, set when it raised the nearest depth somewhere in the first pass. Only those segments can be the
// nearest at any pixel (the maximum never decreases), so the id pass skips the others before any corner work.
RWStructuredBuffer<uint> _SegmentRaised;

uint SegmentIndex(uint3 id) { return id.y * _GroupsX * 64 + id.x; }
uint SegmentFromDispatch(uint t)
{
    uint lo = 0, hi = _RangeCount - 1;
    [loop] while (lo < hi) { uint mid = (lo + hi + 1) >> 1; if (_SegmentRanges[mid].y <= t) lo = mid; else hi = mid - 1; }
    uint2 range = _SegmentRanges[lo];
    return range.x + (t - range.y);
}
bool SegmentRaised(uint segment) { return (_SegmentRaised[segment >> 5] & (1u << (segment & 31u))) != 0; }
void MarkSegmentRaised(uint segment) { InterlockedOr(_SegmentRaised[segment >> 5], 1u << (segment & 31u)); }

// Rasterizes the segment's two triangles (a, a+2, a+1), (a+1, a+2, a+3) = (L0, L1, R0), (R0, L1, R1) at pixel centres.
// Meshes flagged for the hardware draw (MHGroomMesh.info.z: long on-screen segments) are skipped. PRE runs once the
// segment is known, BODY per covered pixel (may set raised), POST after the pixel loop.
#define MH_RASTER_SEGMENT(BODY) MH_RASTER_SEGMENT_EX(;, BODY, ;)
#define MH_RASTER_SEGMENT_EX(PRE, BODY, POST)                                                                       \
    uint dispatchIndex = SegmentIndex(id);                                                                          \
    if (dispatchIndex >= _SegmentTotal) return;                                                                     \
    uint segment = SegmentFromDispatch(dispatchIndex);                                                              \
    PRE                                                                                                             \
    uint p0 = _Segments[segment];                                                                                   \
    MHGroomPoint a = _Points[p0], b = _Points[p0 + 1];                                                              \
    MHGroomMesh mesh = _Meshes[a.meshFlags & 0xFFFFFFu];                                                            \
    if (mesh.info.z != 0) return;                                                                                   \
    float random = MHGroomUnpackHalf2(a.randomCov).x;                                                               \
    float keep = mesh.raster.y;                                                                                     \
    if (keep < 1 && frac(random * 7.31 + .17) >= keep) return;                                                      \
    float baseAlpha = mesh.raster.z;                                                                                \
    Corner c[4];                                                                                                    \
    c[0] = MakeCorner(a, mesh, -1, keep); c[1] = MakeCorner(b, mesh, -1, keep);                                      \
    c[2] = MakeCorner(a, mesh, 1, keep);  c[3] = MakeCorner(b, mesh, 1, keep);                                       \
    if (min(min(c[0].clip.w, c[1].clip.w), min(c[2].clip.w, c[3].clip.w)) <= 0) return;                              \
    if (_Diagnostic == 1) { if (c[0].clip.w == -12345) _OpticalDepth[0] = 1; return; }                              \
    float2 lo = min(min(c[0].screen, c[1].screen), min(c[2].screen, c[3].screen));                                   \
    float2 hi = max(max(c[0].screen, c[1].screen), max(c[2].screen, c[3].screen));                                   \
    int2 pmin = max(int2(floor(lo - .5)), 0), pmax = min(int2(ceil(hi - .5)), int2(_ScreenSize.xy) - 1);            \
    if (any(pmax < pmin) || max(pmax.x - pmin.x, pmax.y - pmin.y) > 2048) return;                                   \
    /* Triangle setup once per segment: edge equations scaled by 1 / area, z/w and 1/w per corner. */               \
    float iw[4], zw[4];                                                                                             \
    [unroll] for (int k = 0; k < 4; k++) { iw[k] = 1 / c[k].clip.w; zw[k] = c[k].clip.z * iw[k]; }                   \
    float3 e0[2], e1[2]; bool live[2];                                                                              \
    [unroll] for (int t = 0; t < 2; t++)                                                                            \
    {                                                                                                               \
        uint i0 = t == 0 ? 0 : 2, i1 = 1, i2 = t == 0 ? 2 : 3;                                                       \
        float area = Edge(c[i0].screen, c[i1].screen, c[i2].screen);                                                \
        live[t] = abs(area) >= 1e-12;                                                                                \
        float inv = live[t] ? 1 / area : 0;                                                                          \
        float2 a1 = c[i1].screen, a2 = c[i2].screen, a0 = c[i0].screen;                                             \
        e0[t] = float3(-(a2.y - a1.y), a2.x - a1.x, (a2.y - a1.y) * a1.x - (a2.x - a1.x) * a1.y) * inv;             \
        e1[t] = float3(-(a0.y - a2.y), a0.x - a2.x, (a0.y - a2.y) * a2.x - (a0.x - a2.x) * a2.y) * inv;             \
    }                                                                                                               \
    /* Scan along the major axis (u); per pixel line, the minor-axis (v) span is where the line crosses the five  \
       edges of the two triangles, so a long thin ribbon costs its length, not its bounding box. The exact          \
       pixel-centre edge tests below decide coverage; the span only bounds them (padded). */                       \
    bool xMajor = pmax.x - pmin.x >= pmax.y - pmin.y;                                                               \
    float2 q[4];                                                                                                    \
    [unroll] for (int k2 = 0; k2 < 4; k2++) q[k2] = xMajor ? c[k2].screen : c[k2].screen.yx;                        \
    int uMin = xMajor ? pmin.x : pmin.y, uMax = xMajor ? pmax.x : pmax.y;                                           \
    int vMin = xMajor ? pmin.y : pmin.x, vMax = xMajor ? pmax.y : pmax.x;                                           \
    bool raised = false;                                                                                            \
    [loop] for (int u = uMin; u <= uMax; u++)                                                                       \
    {                                                                                                               \
        float uc = u + .5, vlo = 1e30, vhi = -1e30;                                                                 \
        [unroll] for (int ed = 0; ed < 5; ed++)                                                                     \
        {                                                                                                           \
            /* Edges (0,1) (1,2) (2,0) (1,3) (3,2). */                                                               \
            uint ea = ed == 0 ? 0 : ed == 1 ? 1 : ed == 2 ? 2 : ed == 3 ? 1 : 3;                                     \
            uint eb = ed == 0 ? 1 : ed == 1 ? 2 : ed == 2 ? 0 : ed == 3 ? 3 : 2;                                     \
            float2 A = q[ea], B = q[eb];                                                                             \
            if ((A.x - uc) * (B.x - uc) > 0) continue;                                                               \
            float du = B.x - A.x;                                                                                    \
            if (abs(du) < 1e-3) { vlo = min(vlo, min(A.y, B.y)); vhi = max(vhi, max(A.y, B.y)); continue; }          \
            float v = A.y + (B.y - A.y) * saturate((uc - A.x) / du);                                                 \
            vlo = min(vlo, v); vhi = max(vhi, v);                                                                    \
        }                                                                                                           \
        int v0 = max((int)ceil(vlo - .5 - 1e-3), vMin), v1 = min((int)floor(vhi - .5 + 1e-3), vMax);                \
        [loop] for (int vv = v0; vv <= v1; vv++)                                                                    \
        {                                                                                                           \
            int x = xMajor ? u : vv, y = xMajor ? vv : u;                                                            \
            float3 pc = float3(x + .5, y + .5, 1);                                                                  \
            [unroll] for (int t = 0; t < 2; t++)                                                                    \
            {                                                                                                       \
                if (!live[t]) continue;                                                                              \
                uint i0 = t == 0 ? 0 : 2, i1 = 1, i2 = t == 0 ? 2 : 3;                                               \
                float w0 = dot(e0[t], pc), w1 = dot(e1[t], pc), w2 = 1 - w0 - w1;                                     \
                /* Shared edge L1-R0 belongs to the first triangle only (no double coverage). */                     \
                if (w0 < 0 || w1 < 0 || w2 < 0 || (t == 1 && w2 == 0)) continue;                                     \
                precise float z = w0 * zw[i0] + w1 * zw[i1] + w2 * zw[i2];                                           \
                if (z < _SceneDepth[uint2(x, y)]) continue;   /* reversed Z: behind the opaque depth */           \
                float3 pw = float3(w0 * iw[i0], w1 * iw[i1], w2 * iw[i2]);                                           \
                pw /= pw.x + pw.y + pw.z;                                                                            \
                uint pixel = y * (uint)_ScreenSize.x + x;                                                            \
                if (_Diagnostic == 2) { if (z == -12345) _OpticalDepth[pixel] = 1; break; }                          \
                BODY                                                                                                 \
                break;                                                                                               \
            }                                                                                                       \
        }                                                                                                           \
    }                                                                                                               \
    POST


#endif

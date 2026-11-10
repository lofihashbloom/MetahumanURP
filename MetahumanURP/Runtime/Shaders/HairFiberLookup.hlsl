#ifndef METAHUMAN_HAIR_FIBER_LOOKUP_INCLUDED
#define METAHUMAN_HAIR_FIBER_LOOKUP_INCLUDED
#include "HairFiberScattering.hlsl"

// .mhfiber tables are material-fixed in eta, betaN and sigmaA. The caller must
// match those profile values; varying pigment or betaN without rebaking is invalid.
// U repeats relative azimuth. V clamps inclination-difference endpoint rows.
float2 MHHairFiberLookupUv(MHHairFiberAngles a, float2 dimensions)
{
    float u = frac((a.azimuth + MH_FIBER_PI) / (2.0 * MH_FIBER_PI));
    float inclination = acos(saturate(a.cosDifference)) * (2.0 / MH_FIBER_PI);
    float v = (0.5 + inclination * (dimensions.y - 1.0)) / dimensions.y;
    return float2(u, v);
}

// RGB contains intrinsic path-weighted azimuth density; alpha0 is NOT coverage.
void MHHairFiberLookupPaths(TEXTURE2D_ARRAY_PARAM(table, samplerTable),
                          MHHairFiberAngles a, float2 dimensions,
                          out float3 reflection, out float3 transmission,
                          out float3 internalReflection, out float3 higherOrders)
{
    float2 uv = MHHairFiberLookupUv(a, dimensions);
    reflection = SAMPLE_TEXTURE2D_ARRAY_LOD(table, samplerTable, uv, 0, 0).rgb;
    transmission = SAMPLE_TEXTURE2D_ARRAY_LOD(table, samplerTable, uv, 1, 0).rgb;
    internalReflection = SAMPLE_TEXTURE2D_ARRAY_LOD(table, samplerTable, uv, 2, 0).rgb;
    higherOrders = SAMPLE_TEXTURE2D_ARRAY_LOD(table, samplerTable, uv, 3, 0).rgb;
}
// Validated first configuration: one longitudinal variance shared by all paths,
// zero cuticle shifts, fixed eta/betaN/absorption profile. ReflectionScale is an
// explicit surface-loss/artist adapter;1 is the nonabsorbing furnace convention.
float3 MHHairFiberEvaluateCommonLookup(TEXTURE2D_ARRAY_PARAM(table, samplerTable),
                                      MHHairFiberAngles a, float2 dimensions,
                                      float variance, float reflectionScale)
{
    float3 r, tt, trt, tail;
    MHHairFiberLookupPaths(TEXTURE2D_ARRAY_ARGS(table, samplerTable), a, dimensions, r, tt, trt, tail);
    float longitudinal = MHHairFiberLongitudinal(a.sinLight, a.cosLight, a.sinView, a.cosView, variance);
    return longitudinal * (r * saturate(reflectionScale) + tt + trt + tail);
}
#endif

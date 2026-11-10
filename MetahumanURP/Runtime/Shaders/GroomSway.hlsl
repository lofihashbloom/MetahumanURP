// Light groom sway (GroomSway component): the groom's lag behind the head as a small rotation about a pivot near the
// crown, weighted root (0) to tip (1) along each strand by along^exponent, so roots stay on the scalp and tips move most.
// Each strand point turns by one rotation vector: the main spring's, spread per strand by its random value (fine.w),
// plus a looser spring's by that random value, times 1 + front.w x how far the point lies in front of the pivot (the
// fringe). Set every frame from the CPU springs: a few ALU per strand point in the existing passes, no extra pass or
// buffer. Not a strand simulation: no collision, and the precomputed self-shadow and receiver-shadow volumes do not
// follow the sway.
//   pivot.xyz      world pivot                       pivot.w      tip exponent
//   axisAngle.xyz  main rotation vector (rad, world) axisAngle.w  1 when active (0: no sway, the default everywhere)
//   fine.xyz       loose rotation vector (rad)       fine.w       per-strand spread of the main sway (0..1)
//   front.xyz      face direction (world)            front.w      front boost
#ifndef METAHUMAN_GROOM_SWAY_INCLUDED
#define METAHUMAN_GROOM_SWAY_INCLUDED

float3 MHGroomSwayVector(float3 positionWS, float along, float strandRandom, float4 pivot, float4 axisAngle, float4 fine, float4 front)
{
    if (axisAngle.w <= 0) return 0;
    float tip = pow(saturate(along), max(pivot.w, .01));
    float3 d = positionWS - pivot.xyz;
    float len = length(d);
    float facing = len > 1e-5 ? saturate(dot(d / len, front.xyz)) : 0;
    return tip * (1 + front.w * facing) * (axisAngle.xyz * (1 + fine.w * (2 * strandRandom - 1)) + fine.xyz * strandRandom);
}

// Rodrigues rotation of v by the rotation vector r.
float3 MHGroomSwayRotate(float3 v, float3 r)
{
    float angle = length(r);
    if (angle < 1e-7) return v;
    float3 k = r / angle;
    float s, c; sincos(angle, s, c);
    return v * c + cross(k, v) * s + k * (dot(k, v) * (1 - c));
}

float3 MHGroomSwayAt(float3 positionWS, float3 pivotWS, float3 r)
{
    return pivotWS + MHGroomSwayRotate(positionWS - pivotWS, r);
}

#endif

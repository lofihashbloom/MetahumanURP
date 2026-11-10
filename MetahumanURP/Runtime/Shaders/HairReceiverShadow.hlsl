// Hair-to-skin receiver shadows: Beer/Poisson transmission through a static, light-space optical-depth
// field of projected fiber area. Independent implementation; applied once to the direct light's
// shadow attenuation. Inactive (transmission 1) unless MH_HAIR_RECEIVER_SHADOW is enabled on the
// material and HairReceiverShadowFeature has bound a volume for the current camera.
#ifndef METAHUMAN_HAIR_RECEIVER_SHADOW_INCLUDED
#define METAHUMAN_HAIR_RECEIVER_SHADOW_INCLUDED

float _MHReceiverEnabled;
float4 _MHReceiverDensity;
float4 _MHReceiverResolution;
float4 _MHReceiverOriginZMin[4], _MHReceiverRightUMin[4], _MHReceiverUpVMin[4], _MHReceiverForwardZMax[4], _MHReceiverScales[4];
int _MHReceiverMainSlot;
int _MHReceiverAdditionalCount;
float4 _MHReceiverAdditionalSlots[64];

#if defined(MH_HAIR_RECEIVER_SHADOW)
TEXTURE3D(_MHReceiverTau0); TEXTURE3D(_MHReceiverTau1); TEXTURE3D(_MHReceiverTau2); TEXTURE3D(_MHReceiverTau3);
#endif

float MHReceiverTransmission(float3 positionWS, int slot)
{
#if !defined(MH_HAIR_RECEIVER_SHADOW)
    return 1;
#else
    if (_MHReceiverEnabled < .5 || slot < 0 || slot > 3) return 1;
    float3 rel = positionWS - _MHReceiverOriginZMin[slot].xyz;
    float depth = dot(rel, _MHReceiverForwardZMax[slot].xyz);
    if (depth <= 0) return 1;
    float3 q = float3((dot(rel, _MHReceiverRightUMin[slot].xyz) / depth - _MHReceiverRightUMin[slot].w) * _MHReceiverScales[slot].x,
                      (dot(rel, _MHReceiverUpVMin[slot].xyz) / depth - _MHReceiverUpVMin[slot].w) * _MHReceiverScales[slot].y,
                      (depth - _MHReceiverOriginZMin[slot].w) * _MHReceiverScales[slot].z);
    if (any(q.xy < 0) || any(q.xy > 1) || q.z < 0) return 1;
    // N depth cells are stored as N+1 boundary-prefix planes. The last plane is the full terminal
    // optical depth, kept for every receiver behind the field. Receivers use no depth bias.
    float n = _MHReceiverResolution[slot];
    q.z = (saturate(q.z) * n + .5) / (n + 1);
    float tau;
    if (slot == 0) tau = SAMPLE_TEXTURE3D_LOD(_MHReceiverTau0, sampler_LinearClamp, q, 0).r;
    else if (slot == 1) tau = SAMPLE_TEXTURE3D_LOD(_MHReceiverTau1, sampler_LinearClamp, q, 0).r;
    else if (slot == 2) tau = SAMPLE_TEXTURE3D_LOD(_MHReceiverTau2, sampler_LinearClamp, q, 0).r;
    else tau = SAMPLE_TEXTURE3D_LOD(_MHReceiverTau3, sampler_LinearClamp, q, 0).r;
    return exp(-max(tau, 0) * max(_MHReceiverDensity[slot], 0));
#endif
}

// Maps the light loop index to a volume slot. Forward uses the per-object light index;
// Forward+ uses the resolved light-buffer index. Slots come from actual Light identities, not directions.
int MHReceiverAdditionalSlot(uint loopIndex)
{
#if defined(MH_HAIR_RECEIVER_SHADOW)
    #if USE_CLUSTER_LIGHT_LOOP
    int bufferIndex = (int)loopIndex;
    #else
    int bufferIndex = GetPerObjectLightIndex(loopIndex);
    #endif
    if (bufferIndex < 0 || bufferIndex >= min(_MHReceiverAdditionalCount, 256)) return -1;
    return (int)_MHReceiverAdditionalSlots[bufferIndex >> 2][bufferIndex & 3];
#else
    return -1;
#endif
}

Light MHReceiverMain(Light light, float3 positionWS)
{
    light.shadowAttenuation *= MHReceiverTransmission(positionWS, _MHReceiverMainSlot);
    return light;
}

Light MHReceiverAdditional(Light light, float3 positionWS, uint loopIndex)
{
    light.shadowAttenuation *= MHReceiverTransmission(positionWS, MHReceiverAdditionalSlot(loopIndex));
    return light;
}

#endif

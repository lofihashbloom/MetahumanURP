// Hair self-shadow: transmittance of a light through the groom's own fibres.
// GroomShadowVolume precomputes, per light, the optical depth from every voxel of a head-local extinction volume
// toward that light (GroomShadowTransmittance compute). A strand fragment samples it once per light and multiplies
// that light's shadow attenuation by exp(-tau). Fibres are gray occluders: no fibre colour or multiple scattering.
// Inactive (transmission 1) unless GroomShadowFeature is on the renderer and a GroomShadowVolume drives the renderer.
#ifndef METAHUMAN_GROOM_SELF_SHADOW_INCLUDED
#define METAHUMAN_GROOM_SELF_SHADOW_INCLUDED

// Per renderer (MaterialPropertyBlock from GroomShadowVolume).
float _MHGroomShadowEnabled;
float4x4 _MHGroomShadowWorldToVolume;
float4 _MHGroomShadowSlotIds;            // light identity per slot (0 = empty)
TEXTURE3D(_MHGroomShadowTau0); TEXTURE3D(_MHGroomShadowTau1); TEXTURE3D(_MHGroomShadowTau2); TEXTURE3D(_MHGroomShadowTau3);

// Per camera (GroomShadowFeature): light identity of the main light and of each additional-light buffer entry.
float _MHGroomMainLightId;
float _MHGroomAdditionalLightCount;
float4 _MHGroomAdditionalLightIds[64];

// True when this renderer's volume holds a transmittance for the light; transmission is then exp(-tau).
bool MHGroomTrySelfTransmission(float3 positionWS, float lightId, out float transmission)
{
    transmission = 1;
    if (_MHGroomShadowEnabled < .5 || lightId < .5) return false;
    float3 q = mul(_MHGroomShadowWorldToVolume, float4(positionWS, 1)).xyz;
    float tau;
    if (_MHGroomShadowSlotIds.x == lightId) tau = SAMPLE_TEXTURE3D_LOD(_MHGroomShadowTau0, sampler_LinearClamp, q, 0).r;
    else if (_MHGroomShadowSlotIds.y == lightId) tau = SAMPLE_TEXTURE3D_LOD(_MHGroomShadowTau1, sampler_LinearClamp, q, 0).r;
    else if (_MHGroomShadowSlotIds.z == lightId) tau = SAMPLE_TEXTURE3D_LOD(_MHGroomShadowTau2, sampler_LinearClamp, q, 0).r;
    else if (_MHGroomShadowSlotIds.w == lightId) tau = SAMPLE_TEXTURE3D_LOD(_MHGroomShadowTau3, sampler_LinearClamp, q, 0).r;
    else return false;
    // Outside the volume there are no fibres of this groom, so nothing to attenuate.
    if (any(q < 0) || any(q > 1)) return true;
    transmission = exp(-max(tau, 0));
    return true;
}

float MHGroomSelfTransmission(float3 positionWS, float lightId)
{
    float transmission;
    MHGroomTrySelfTransmission(positionWS, lightId, transmission);
    return transmission;
}

// Light identity of an additional-light loop index: the per-object light index under Forward, the light-buffer
// index under Forward+ (same mapping as the hair receiver shadows).
float MHGroomAdditionalLightId(uint loopIndex)
{
    #if USE_CLUSTER_LIGHT_LOOP
    int bufferIndex = (int)loopIndex;
    #else
    int bufferIndex = GetPerObjectLightIndex(loopIndex);
    #endif
    if (bufferIndex < 0 || bufferIndex >= (int)min(_MHGroomAdditionalLightCount, 256)) return 0;
    return _MHGroomAdditionalLightIds[bufferIndex >> 2][bufferIndex & 3];
}

Light MHGroomSelfShadowMain(Light light, float3 positionWS)
{
    light.shadowAttenuation *= MHGroomSelfTransmission(positionWS, _MHGroomMainLightId);
    return light;
}

Light MHGroomSelfShadowAdditional(Light light, float3 positionWS, uint loopIndex)
{
    light.shadowAttenuation *= MHGroomSelfTransmission(positionWS, MHGroomAdditionalLightId(loopIndex));
    return light;
}
#endif

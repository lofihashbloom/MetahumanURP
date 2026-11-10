#ifndef MH_HAIR_LOCAL_REFERENCE_INCLUDED
#define MH_HAIR_LOCAL_REFERENCE_INCLUDED
// Original isolated-unit fitted transport model; no engine renderer/LUT implementation.
// Host evidence: Validation/GroomMatched/v14/LocalScattering/LocalHarmonicProfile.json
// Explicit authored roughness .37 diagnostic; the material caller must guard its domain.
// Source profile validated at roughness .37 only. Local component only; not global scattering.
// Returns K; existing URP adapter applies PI once. Do not add ribbonNdotL or2*Spec.
float3 MHHairLocalReference037(float sinLight, float sinView, float3 sourceColor)
{
    float sl = clamp(sinLight, -1.0, 1.0);
    float sv = clamp(sinView, -1.0, 1.0);
    float x = sl * sl + sv * sv;
    float floorRawPi = 0.000563946935892 + x * (2.63423367763e-05 + x * 0.000314610886343);
    float phase = 1.0 + -0.240461961646 * sl * sv
                + -0.083134356868 * (0.5 * (3.0 * sl * sl - 1.0)) * (0.5 * (3.0 * sv * sv - 1.0));
    float3 t = saturate((saturate(sourceColor) - 0.0009765625) / (1.0 - 0.0009765625));
    float3 albedo = 1.0 - pow(1.0 - t, 2.19086152435);
    return floorRawPi * 0.3183098861837907
         + 0.995254625176 * albedo * phase * 0.07957747154594767;
}
#endif

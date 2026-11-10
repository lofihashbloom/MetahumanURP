#ifndef METAHUMAN_SKIN_LIGHTING_INCLUDED
#define METAHUMAN_SKIN_LIGHTING_INCLUDED

// Skin lighting in one light loop (MHSkinLighting). Included by MetaHumanSurface.hlsl after MHAddExtraLight.
// Every light is fetched once, so its shadow map, the hair receiver field and the reflection probes are sampled once
// per lobe that needs them, whatever the outputs: the lit colour, the diffuse part alone (the skin-diffusion input) and
// the scatter, fuzz and teeth-translucency terms all come from the same loop.
// Two specular responses, chosen per material by _DesktopSkinLighting:
//   0: URP's own (DirectBRDFSpecular), mixed over the two lobes; the result equals the mix of two UniversalFragmentPBR
//      evaluations, which share their diffuse term and reflectance.
//   1: an independent desktop dual-lobe microfacet evaluation with a rough diffuse response.
// Radiance follows URP's convention with pi absorbed into the light.
float MHSkinDistribution(float perceptualRoughness, float normalHalf)
{
    float slope = max(perceptualRoughness * perceptualRoughness, .000625);
    float variance = slope * slope;
    float denominator = (1 - normalHalf * normalHalf) + variance * normalHalf * normalHalf;
    return variance / max(denominator * denominator, 1e-12);
}

// Desktop response of one light; diffuse receives its diffuse part alone.
float3 MHSkinDirectParts(Light light, InputData data, BRDFData brdf, BRDFData second, float roughness,
    float primary, float secondary, float mixture, out float3 diffuse)
{
    float3 n = data.normalWS, v = data.viewDirectionWS, l = light.direction;
    float nl = saturate(dot(n, l)), nv = max(abs(dot(n, v)), 1e-5);
    float3 h = SafeNormalize(v + l);
    float nh = saturate(dot(n, h)), vh = saturate(dot(v, h));
    float grazingDiffuse = .5 + 2 * roughness * vh * vh;
    float diffuseFactor = (1 + (grazingDiffuse - 1) * pow(1 - nl, 5))
        * (1 + (grazingDiffuse - 1) * pow(1 - nv, 5));
    float3 radiance = nl * light.color * light.distanceAttenuation * light.shadowAttenuation;
    float3 response = brdf.diffuse * lerp(1, diffuseFactor, _RoughDiffuseWeight);
    diffuse = response * radiance;
    {
        float average = lerp(primary, secondary, mixture);
        float slope = average * average;
        float visibilityDenominator = 2 * (2 * nl * nv * (1 - slope) + slope * (nl + nv));
        float distribution = lerp(MHSkinDistribution(primary, nh), MHSkinDistribution(secondary, nh), mixture);
        // Cavity-masked skin can approach zero reflectance. Fade the grazing
        // endpoint over the lowest two percent instead of adding a white rim
        // to a completely suppressed specular response.
        float grazing = saturate(brdf.specular.g / .02);
        float3 fresnel = lerp(brdf.specular, grazing.xxx, pow(1 - vh, 5));
        float3 desktopSpecular = distribution * fresnel / max(visibilityDenominator, 1e-6);
        float3 urpSpecular = lerp(DirectBRDFSpecular(brdf, n, l, v), DirectBRDFSpecular(second, n, l, v), mixture) * brdf.specular;
        response += lerp(urpSpecular, desktopSpecular, _DesktopSpecularWeight);
    }
    return response * radiance;
}

// URP's response of one light (LightingPhysicallyBased) with its specular term mixed over the two lobes; diffuse
// receives its diffuse part alone.
float3 MHSkinUrpDirectParts(Light light, InputData data, BRDFData brdf, BRDFData second, float mixture, out float3 diffuse)
{
    half3 n = data.normalWS, v = data.viewDirectionWS;
    half nl = saturate(dot(n, light.direction));
    float attenuation = light.distanceAttenuation * light.shadowAttenuation;
    half3 radiance = light.color * (attenuation * nl);
    half specular = lerp(DirectBRDFSpecular(brdf, n, light.direction, v), DirectBRDFSpecular(second, n, light.direction, v), mixture);
    diffuse = brdf.diffuse * radiance;
    return (brdf.diffuse + brdf.specular * specular) * radiance;
}

// Running sums of the loop. The scatter, fuzz and translucency terms keep their own sums and join at the end.
struct MHSkinSums { float3 color, diffuse; half3 extraColor, extraDiffuse; };

void MHSkinAddLight(Light light, AmbientOcclusionFactor ao, InputData inputData, half3 albedo, BRDFData brdf, BRDFData second,
    float roughness, float primary, float secondary, float mixture, half3 tangentWS, half scatter, half fuzz, inout MHSkinSums sums)
{
    // Scatter, fuzz and teeth translucency take the light as fetched; the surface response takes it with the
    // screen-space occlusion of direct light, as URP's GetMainLight / GetAdditionalLight (InputData) overloads apply it.
    MHAddExtraLight(light, inputData.normalWS, inputData.viewDirectionWS, tangentWS, albedo, scatter, fuzz, sums.extraColor, sums.extraDiffuse);
#if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT)
    light.color *= ao.directAmbientOcclusion;
#endif
    float3 lightDiffuse, lit;
    [branch] if (_DesktopSkinLighting > .5)
        lit = MHSkinDirectParts(light, inputData, brdf, second, roughness, primary, secondary, mixture, lightDiffuse);
    else
        lit = MHSkinUrpDirectParts(light, inputData, brdf, second, mixture, lightDiffuse);
    sums.color += lit;
    sums.diffuse += lightDiffuse;
}

// Full skin lighting; diffuse receives the part the skin diffusion filters: ambient and direct diffuse, vertex lights,
// emission, scatter and teeth translucency (fuzz and specular stay out). surface.smoothness is the primary lobe's;
// primary and secondary are the two lobes' roughness, mixture the secondary lobe's weight.
float3 MHSkinLighting(InputData inputData, SurfaceData surface, float roughness, float primary, float secondary, float mixture,
    half3 tangentWS, half scatter, half fuzz, out float3 diffuse)
{
    BRDFData brdf;
    InitializeBRDFData(surface, brdf);
    SurfaceData secondSurface = surface;
    secondSurface.smoothness = 1 - secondary;
    BRDFData second;
    InitializeBRDFData(secondSurface, second);
    BRDFData coat = (BRDFData)0;
    AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(inputData, surface);
    half4 shadowMask = CalculateShadowMask(inputData);
    Light key = MHReceiverMain(GetMainLight(inputData.shadowCoord, inputData.positionWS, shadowMask), inputData.positionWS);
    MixRealtimeAndBakedGI(key, inputData.normalWS, inputData.bakedGI);
    MHSkinSums sums;
    sums.extraColor = 0; sums.extraDiffuse = 0;
    sums.diffuse = inputData.bakedGI * brdf.diffuse * ao.indirectAmbientOcclusion;
    sums.color = lerp(
        GlobalIllumination(brdf, coat, 0, inputData.bakedGI, ao.indirectAmbientOcclusion,
            inputData.positionWS, inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV),
        GlobalIllumination(second, coat, 0, inputData.bakedGI, ao.indirectAmbientOcclusion,
            inputData.positionWS, inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV), mixture);
    MHSkinAddLight(key, ao, inputData, surface.albedo, brdf, second, roughness, primary, secondary, mixture, tangentWS, scatter, fuzz, sums);
#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = MHReceiverAdditional(GetAdditionalLight(lightIndex, inputData.positionWS, shadowMask), inputData.positionWS, lightIndex);
        MHSkinAddLight(light, ao, inputData, surface.albedo, brdf, second, roughness, primary, secondary, mixture, tangentWS, scatter, fuzz, sums);
    }
    #endif
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = MHReceiverAdditional(GetAdditionalLight(lightIndex, inputData.positionWS, shadowMask), inputData.positionWS, lightIndex);
        MHSkinAddLight(light, ao, inputData, surface.albedo, brdf, second, roughness, primary, secondary, mixture, tangentWS, scatter, fuzz, sums);
    LIGHT_LOOP_END
#endif
#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    sums.color += inputData.vertexLighting * brdf.diffuse;
    sums.diffuse += inputData.vertexLighting * brdf.diffuse;
#endif
    diffuse = sums.diffuse + surface.emission + sums.extraDiffuse;
    return sums.color + surface.emission + sums.extraColor;
}

#endif

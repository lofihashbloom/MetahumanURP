#ifndef METAHUMAN_SURFACE_INCLUDED
#define METAHUMAN_SURFACE_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "HairReceiverShadow.hlsl"
#if defined(MH_REFERENCE_DEBUG)
float _MHReferenceDebug;
#endif

TEXTURE2D(_DetailNormalMap); SAMPLER(sampler_DetailNormalMap);
TEXTURE2D(_SRMFMap); SAMPLER(sampler_SRMFMap);
TEXTURE2D(_SpecularMap); SAMPLER(sampler_SpecularMap);
TEXTURE2D(_RoughnessMap); SAMPLER(sampler_RoughnessMap);
TEXTURE2D(_ScatterMap); SAMPLER(sampler_ScatterMap);
TEXTURE2D(_OpacityMap); SAMPLER(sampler_OpacityMap);
TEXTURE2D(_WrinkleNormal1); SAMPLER(sampler_WrinkleNormal1);
TEXTURE2D(_WrinkleNormal2); SAMPLER(sampler_WrinkleNormal2);
TEXTURE2D(_WrinkleNormal3); SAMPLER(sampler_WrinkleNormal3);
TEXTURE2D(_WrinkleColor1); TEXTURE2D(_WrinkleColor2); TEXTURE2D(_WrinkleColor3);
#if defined(MH_ANIMATED_MAPS)
TEXTURE2D_ARRAY(_WrinkleMaskArray); SAMPLER(sampler_WrinkleMaskArray);
#endif
// Animated maps (rig-driven wrinkles): per wrinkle map N and mask slice s, the rig weights of the slice's four
// regions, at [N * MH_ANIMATED_MAP_SLICES + s]. Set per renderer by DnaAnimatedMapBinding.
#define MH_ANIMATED_MAP_SLICES 10
float4 _AnimatedMapWeights[3 * MH_ANIMATED_MAP_SLICES];
TEXTURE2D(_ScleraMap); SAMPLER(sampler_ScleraMap);
TEXTURE2D(_IrisColorMap); SAMPLER(sampler_IrisColorMap);
TEXTURE2D(_ScleraTintMap); SAMPLER(sampler_ScleraTintMap);
TEXTURE2D(_IrisMasksMap); SAMPLER(sampler_IrisMasksMap);
TEXTURE2D(_IrisHeightMap); SAMPLER(sampler_IrisHeightMap);
TEXTURE2D(_EyeAxisMap); SAMPLER(sampler_EyeAxisMap);
TEXTURE2D(_MakeupMask); SAMPLER(sampler_MakeupMask);
TEXTURE2D(_VeinsMap); SAMPLER(sampler_VeinsMap);
TEXTURE2D(_MouthOcclusionMap); SAMPLER(sampler_MouthOcclusionMap);
TEXTURE2D(_TranslucencyMap); SAMPLER(sampler_TranslucencyMap);
TEXTURE2D(_TeethLayerMap);   // sampled with sampler_TranslucencyMap (same teeth UV space; the Skin shader is at the 16-sampler limit)

CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
half4 _BaseColor;
half4 _ScatterColor;
half4 _IrisColor;
half4 _ScleraColor;
float4 _WrinkleWeights;
half _NormalScale, _Roughness, _Specular, _UseSRMF;
half _UseSpecularMap, _UseRoughnessMap, _Scatter, _Fuzz;
half _SecondaryRoughness, _SecondaryWeight, _Cutoff, _UseOpacityMap;
half _ClearCoat, _ClearCoatSmoothness, _Anisotropy, _WrinklesEnabled;
half _IrisRadius, _OpacityStrength;
half _DetailTiling, _DetailNormalStrength;
half _ProfileLobes, _PrimaryRoughnessScale, _SecondaryRoughnessScale;
half _DiffusionAmount, _UsePackedFuzz, _IrisColorBalance;
half _RoughnessMin, _RoughnessMax, _SpecularMultiplier, _SpecularCavityPower;
float _LegacySourceFresnel, _LegacyFresnelBase, _LegacyFresnelExponent;
float _LegacyFresnelFlatness, _LegacyFresnelNormalYSign, _LegacySpecularMultiplier;
float4 _IrisPicker1, _IrisPicker2, _ScleraPicker;
half _UseScleraMap, _UseIrisColorMap, _UseScleraTintMap;
half _CorneaIOR, _IrisDepth, _PupilScale, _LimbusWidth, _LimbusDarkening;
half _IrisContrast, _IrisTextureScale, _IrisBrightness, _ScleraBrightness, _IrisBrightnessBoost;
half _PupilRadius, _IrisResponse;
half _DesktopEyeLighting, _DeferredEyeInputs;
half _UseAuthoredEyeSpecular;
half _DesktopSkinLighting;
half _RoughDiffuseWeight, _DesktopSpecularWeight;
half _EyeFamily, _UseEyeAxisMap, _ScaleIrisWithinMask, _IrisColorBalanceSmoothness;
half _RadialStructuralBlend, _IrisSaturation, _IrisAO;
half _NormalUVFlipY, _NormalGreenSign, _DetailUVFlipY, _DetailGreenSign;
half _ScleraPower;
float4 _MakeupWeights, _FoundationColor, _ConcealerColor, _BlusherColor, _MakeupRoughness;
float4 _HeadRoughnessControls, _HeadCavityPowers, _HeadRegionMask, _IrisPaletteVariation;
float _ModernHeadInputs, _HeadFresnelExponent, _UseVeinsMap, _VeinsPower;
float _RoughnessUVFlipY, _SpecularUVFlipY;
float4 _CloudyIrisColor, _EyeCornerColor, _EyeColorFields;
float4 _SourceNeckHide;
half _MouthOcclusionStrength, _DimReflections;
half _Translucency, _TranslucencyShadow, _DiffusionNeutralRadius;
half _TeethLayers, _EnamelDensity, _TeethShadowFloor, _TeethShadowStart, _TeethShadowEnd, _TeethGumShade, _TeethEdgeShade;
float _TeethContactAO, _TeethContactRadius, _TeethContactDepth, _TeethContactBias;
half4 _EnamelColor, _DentinTint, _CavityColor, _TeethGumColor, _TeethContactColor;
half _PunctualSpecular;
half4 _CatchlightColor;
float4 _CatchlightDirection, _CatchlightShape;
half _CatchlightIntensity;
CBUFFER_END

struct MHAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct MHVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
    half4 tangentWS : TEXCOORD3;
    half fog : TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

MHVaryings MHVertex(MHAttributes input)
{
    MHVaryings output = (MHVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);
#if defined(MH_SKIN)
    // The source face uses an authored inward neck displacement. Keep its raw
    // mesh UV independent of the base-texture transform and apply the same
    // world offset in color, depth, normals and shadow passes.
    float neckWeight = smoothstep(_SourceNeckHide.y, 1, 1 - input.uv.y);
    position.positionWS += normal.normalWS * (_SourceNeckHide.x * neckWeight);
    position.positionCS = TransformWorldToHClip(position.positionWS);
#endif
    output.positionCS = position.positionCS;
    output.positionWS = position.positionWS;
    output.normalWS = normal.normalWS;
    output.tangentWS = half4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.fog = ComputeFogFactor(position.positionCS.z);
    return output;
}

half MHAlpha(float2 uv)
{
    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a;
    alpha *= lerp(1, SAMPLE_TEXTURE2D(_OpacityMap, sampler_OpacityMap, uv).r, _UseOpacityMap);
#if defined(MH_HAIR)
    alpha *= _OpacityStrength;
    clip(alpha - _Cutoff);
    // Stabilize subpixel strands for MSAA alpha-to-coverage rather than emitting
    // the tiny mip-filtered alpha as an almost empty sample mask.
    alpha = saturate((alpha - _Cutoff) / max(fwidth(alpha), 0.0001h) + 0.5h);
#endif
    return alpha;
}

// Region masks for the three wrinkle maps: mask_N = sum_s dot(saturate(weights[N][s]), regions[s]).
// Slices whose weights are all zero are not sampled.
half3 MHAnimatedMapMasks(float2 uv)
{
    half3 masks = 0;
#if defined(MH_ANIMATED_MAPS)
    [unroll] for (int s = 0; s < MH_ANIMATED_MAP_SLICES; s++)
    {
        float4 w1 = saturate(_AnimatedMapWeights[s]);
        float4 w2 = saturate(_AnimatedMapWeights[MH_ANIMATED_MAP_SLICES + s]);
        float4 w3 = saturate(_AnimatedMapWeights[2 * MH_ANIMATED_MAP_SLICES + s]);
        [branch] if (dot(w1 + w2 + w3, 1) > 0)
        {
            half4 regions = SAMPLE_TEXTURE2D_ARRAY(_WrinkleMaskArray, sampler_WrinkleMaskArray, uv, s);
            masks += half3(dot(w1, regions), dot(w2, regions), dot(w3, regions));
        }
    }
#endif
    return masks;
}

half3 MHNormal(MHVaryings input, half faceSign)
{
    float2 normalUV = float2(input.uv.x, lerp(input.uv.y, 1 - input.uv.y, _NormalUVFlipY));
    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, normalUV), _NormalScale);
#if defined(MH_EYE)
    if (_EyeFamily > .5) normalTS = lerp(half3(0,0,1), UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, normalUV), 1), _NormalScale);
#endif
    normalTS.y *= _NormalGreenSign < 0 ? -1 : 1;
#if defined(MH_SKIN) && defined(MH_ANIMATED_MAPS)
    // Rig-driven wrinkles: default + sum_N (wrinkle_N - default) * mask_N on unpacked tangent normals, with the
    // same UV and green-channel conventions as the main normal map.
    half3 masks = MHAnimatedMapMasks(input.uv);
    half3 wrinkle1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_WrinkleNormal1, sampler_WrinkleNormal1, normalUV), _NormalScale);
    half3 wrinkle2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_WrinkleNormal2, sampler_WrinkleNormal2, normalUV), _NormalScale);
    half3 wrinkle3 = UnpackNormalScale(SAMPLE_TEXTURE2D(_WrinkleNormal3, sampler_WrinkleNormal3, normalUV), _NormalScale);
    half greenSign = _NormalGreenSign < 0 ? -1 : 1;
    wrinkle1.y *= greenSign; wrinkle2.y *= greenSign; wrinkle3.y *= greenSign;
    normalTS += (wrinkle1 - normalTS) * masks.x + (wrinkle2 - normalTS) * masks.y + (wrinkle3 - normalTS) * masks.z;
#endif
#if defined(MH_SKIN)
    half3 weights = saturate(_WrinkleWeights.xyz) * _WrinklesEnabled;
    weights /= max(1, weights.x + weights.y + weights.z);
    normalTS = normalTS * (1 - weights.x - weights.y - weights.z)
        + UnpackNormalScale(SAMPLE_TEXTURE2D(_WrinkleNormal1, sampler_WrinkleNormal1, input.uv), _NormalScale) * weights.x
        + UnpackNormalScale(SAMPLE_TEXTURE2D(_WrinkleNormal2, sampler_WrinkleNormal2, input.uv), _NormalScale) * weights.y
        + UnpackNormalScale(SAMPLE_TEXTURE2D(_WrinkleNormal3, sampler_WrinkleNormal3, input.uv), _NormalScale) * weights.z;
    if (_DetailNormalStrength > 0)
    {
        float2 detailUV = input.uv * _DetailTiling;
        detailUV.y = lerp(detailUV.y, 1 - detailUV.y, _DetailUVFlipY);
        half3 detail = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, detailUV), 1);
        detail.y *= _DetailGreenSign < 0 ? -1 : 1;
        detail = lerp(half3(0, 0, 1), detail, _DetailNormalStrength);
        normalTS = BlendNormalRNM(normalize(normalTS), detail);
    }
#endif
    half3 n = normalize(input.normalWS);
    half3 t = normalize(input.tangentWS.xyz);
    half3 b = cross(n, t) * input.tangentWS.w;
    return NormalizeNormalPerPixel(TransformTangentToWorld(normalize(normalTS), half3x3(t, b, n))) * faceSign;
}

#if defined(MH_SKIN)
float MHLegacyFresnelInput(MHVaryings input)
{
    // Compatibility with the captured legacy asset input: a flattened coarse
    // tangent normal is used numerically against the source world camera vector.
    // It precedes the micro normal, uses its own source-data Y convention, and
    // is deliberately not transformed by the surface TBN or normalized again.
    float2 uv = float2(input.uv.x, lerp(input.uv.y, 1 - input.uv.y, _NormalUVFlipY));
    float3 coarse = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), 1);
    coarse.y *= _LegacyFresnelNormalYSign < 0 ? -1 : 1;
    coarse = lerp(coarse, float3(0, 0, 1), _LegacyFresnelFlatness);
    float3 unityView = GetWorldSpaceNormalizeViewDir(input.positionWS);
    // Package interchange basis: UE centimetres = (Unity.x, -Unity.z, Unity.y)*100.
    float3 sourceView = float3(unityView.x, -unityView.z, unityView.y);
    float edgeWeight = pow(1 - saturate(dot(coarse, sourceView)), max(_LegacyFresnelExponent, 0));
    return lerp(_LegacyFresnelBase, 1, edgeWeight);
}
#endif

// Local backlighting remains an approximation alongside the separate diffusion pass.
half3 MHExtraLight(Light light, half3 n, half3 v, half3 tangent, half3 albedo, half scatter, half fuzz)
{
    half3 h = SafeNormalize(light.direction + v);
    half3 radiance = light.color * light.distanceAttenuation * light.shadowAttenuation;
#if defined(MH_HAIR)
    half tDotH = dot(tangent, h);
    half sinTH = sqrt(saturate(1 - tDotH * tDotH));
    half highlight = pow(sinTH, lerp(16, 128, _Anisotropy));
    return radiance * highlight * (0.08h * _Specular) * saturate(dot(n, light.direction) + 0.3);
#elif defined(MH_SKIN)
    half back = saturate(dot(-n, light.direction));
    half forward = pow(saturate(dot(v, -light.direction)), 4);
    half rim = pow(1 - saturate(dot(n, v)), 4);
    return radiance * (albedo * _ScatterColor.rgb * scatter * back * (0.2 + forward)
        + albedo * fuzz * rim * saturate(dot(n, light.direction)) * 0.15);
#else
    return 0;
#endif
}

// Teeth translucency (Skin shader, opt-in): the fragment sets it to _Translucency x _TranslucencyMap.r (0 = off).
static half mhTranslucency = 0;

#if defined(MH_SKIN)
// Adds a light's scatter (transmission), fuzz and teeth-translucency terms to the colour. Scatter and translucency also
// go to the diffuse output (the skin-diffusion input); fuzz stays out of it.
void MHAddExtraLight(Light light, half3 n, half3 v, half3 tangent, half3 albedo, half scatter, half fuzz,
    inout half3 color, inout half3 diffuse)
{
    // Enamel and dentin carry light around a tooth, so its sides and the faces between neighbouring crowns do not fall
    // to the Lambert terminator (opaque shading draws a dark line between every pair of teeth). Adds the share that
    // wrapped diffuse, saturate((N.L + w) / (1 + w)) with w = mhTranslucency, has over Lambert; _TranslucencyShadow
    // lets that share pass the light's shadow (light through a neighbouring tooth). A look approximation, not
    // Unreal's subsurface profile.
    if (mhTranslucency > 0)
    {
        half nl = dot(n, light.direction);
        half3 through = light.color * light.distanceAttenuation * lerp(light.shadowAttenuation, 1, _TranslucencyShadow) * albedo
            * (saturate((nl + mhTranslucency) / (1 + mhTranslucency)) - saturate(nl));
        color += through;
        diffuse += through;
    }
    half3 scattered = MHExtraLight(light, n, v, tangent, albedo, scatter, 0);
    color += scattered + MHExtraLight(light, n, v, tangent, albedo, 0, fuzz);
    diffuse += scattered;
}
#include "MetaHumanSkinLighting.hlsl"
#endif

half3 MHIrisPalette(float2 center, float structural)
{
    float2 offset = float2(_IrisPaletteVariation.x, -_IrisPaletteVariation.y);
    float2 a = center + offset, c = center - offset;
    half3 first = SAMPLE_TEXTURE2D(_IrisColorMap, sampler_TrilinearClamp, float2(a.x, 1 - a.y)).rgb;
    half3 middle = SAMPLE_TEXTURE2D(_IrisColorMap, sampler_TrilinearClamp, float2(center.x, 1 - center.y)).rgb;
    half3 last = SAMPLE_TEXTURE2D(_IrisColorMap, sampler_TrilinearClamp, float2(c.x, 1 - c.y)).rgb;
    half3 color = lerp(lerp(first, middle, saturate(2 * structural)), last, saturate(2 * structural - 1));
    float luminance = dot(color, float3(.212639, .715169, .072192));
    return lerp(color, luminance.xxx, (2 * structural - 1) * _IrisPaletteVariation.z);
}

half3 MHEyeAlbedo(MHVaryings input, out half irisMask)
{
    float cloudy = 0;
    float2 delta = input.uv - 0.5;
    float radius = max(_IrisRadius * 0.5, 0.01);
    float r = length(delta) / radius;
    float feather = max(fwidth(r), 0.01);
    irisMask = 1 - smoothstep(1 - feather, 1 + feather, r);
    // Single-surface optical approximation: refract through the geometric cornea
    // and intersect a recessed iris plane in tangent UV space. Depth is in UV units,
    // not a reconstructed physical corneal thickness or a screen-color refraction.
    half3 n = normalize(input.normalWS);
    half3 t = normalize(input.tangentWS.xyz);
    half3 b = cross(n, t) * input.tangentWS.w;
    half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
    half3 ray = refract(-v, n, rcp(max(_CorneaIOR, 1.001)));
    float2 refracted = delta + float2(dot(ray, t), dot(ray, b))
        * (_IrisDepth / max(0.25, -dot(ray, n))) * irisMask;
    float2 disc = refracted / radius;
    float irisR = length(disc);
    float pupil = clamp(_PupilRadius * _PupilScale, 0.08, 0.85);
    // The texture already contains a black central aperture. Match that boundary
    // to the requested pupil before stretching the surrounding annulus, rather
    // than stacking an unrelated black disc over a second, smaller texture pupil.
    float texturePupil = clamp(0.22 / max(_IrisTextureScale, 0.01), 0.08, 0.8);
    float remappedR = irisR < pupil ? irisR * texturePupil / pupil
        : texturePupil + (irisR - pupil) * (1 - texturePupil) / (1 - pupil);
    float2 irisUV = 0.5 + disc * (remappedR / max(irisR, 0.0001)) * 0.5 * _IrisTextureScale;
    half detail = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, saturate(irisUV)).r;
    detail = pow(max(detail, 0.001h), _IrisContrast);
    // Uniform source lookup UVs have not passed through FBX's UV conversion:
    // Unreal's top-left V convention must be reflected for the Unity texture.
    half3 first = lerp(1, SAMPLE_TEXTURE2D(_IrisColorMap, sampler_IrisColorMap, float2(_IrisPicker1.x, 1 - _IrisPicker1.y)).rgb, _UseIrisColorMap);
    half3 second = lerp(1, SAMPLE_TEXTURE2D(_IrisColorMap, sampler_IrisColorMap, float2(_IrisPicker2.x, 1 - _IrisPicker2.y)).rgb, _UseIrisColorMap);
    half3 iris = saturate(detail * lerp(first, second, _IrisColorBalance) * _IrisColor.rgb * _IrisBrightness * _IrisBrightnessBoost * _IrisResponse);
    if (_EyeFamily > .5)
    {
        // Authored structural masks remain data. Sample an aperture transform
        // anchored at the outer rim, independently of the simplified eye fit.
        float2 coord = disc * .5;
        float rho = length(coord);
        float inward = saturate(1 - rho * 2);
        float mappedRadius = .5 * (1 - saturate(inward * _PupilScale));
        float2 maskUV = .5 + coord * (mappedRadius / max(rho, .0001)) / max(_ScaleIrisWithinMask, .01);
        half4 masks = SAMPLE_TEXTURE2D(_IrisMasksMap, sampler_IrisMasksMap, saturate(maskUV));
        float variation = 1 - lerp(masks.b, masks.r, _RadialStructuralBlend);
        float transition = max(_IrisColorBalanceSmoothness, .001);
        float paletteWeight = smoothstep(_IrisColorBalance - transition, _IrisColorBalance + transition, variation);
        first = MHIrisPalette(_IrisPicker1.xy, masks.r);
        second = MHIrisPalette(_IrisPicker2.xy, masks.r);
        iris = lerp(second, first, paletteWeight);
        iris *= masks.a * lerp(1, masks.g, _IrisAO);
        float rim = smoothstep(.275, .5, length(maskUV - .5));
        iris = lerp(iris, pow(max(iris, 0), 1 + _LimbusDarkening), rim);
        iris *= _IrisBrightness;
        float luminance = dot(iris, float3(.212639, .715169, .072192));
        iris = lerp(luminance.xxx, iris, 2 * _IrisSaturation);
        iris *= _IrisColor.rgb;
        cloudy = saturate((1 - length(maskUV - .5) / max(_EyeColorFields.z, .001)) / max(1 - _EyeColorFields.w, .001));
    }
    else iris *= smoothstep(pupil - feather, pupil + feather, irisR);
    float limbus = smoothstep(1 - _LimbusWidth, 1, r) * irisMask;
    if (_EyeFamily < .5) iris *= 1 - limbus * _LimbusDarkening;
    half3 scleraTint = lerp(1, SAMPLE_TEXTURE2D(_ScleraTintMap, sampler_ScleraTintMap, float2(_ScleraPicker.x, 1 - _ScleraPicker.y)).rgb, _UseScleraTintMap);
    half3 sclera = lerp(1, SAMPLE_TEXTURE2D(_ScleraMap, sampler_ScleraMap, input.uv).rgb, _UseScleraMap);
    if (_EyeFamily > .5)
        sclera = pow(max(sclera, 0), max(_ScleraPower, .001)) * (.8 + 1.1 * _ScleraBrightness);
    else sclera *= _ScleraBrightness;
    sclera *= scleraTint * _ScleraColor.rgb;
    if (_EyeFamily > .5 && _UseVeinsMap > .5)
        sclera *= pow(max(SAMPLE_TEXTURE2D(_VeinsMap, sampler_VeinsMap, input.uv).rgb, 0), 1.6 * _VeinsPower);
    half3 eye = lerp(sclera, iris, irisMask);
    if (_EyeFamily > .5)
    {
        eye += _CloudyIrisColor.rgb * cloudy;
        float center = saturate((1 - length(input.uv - .5) / max(_EyeColorFields.x, .001)) / max(1 - _EyeColorFields.y, .001));
        eye *= lerp(_EyeCornerColor.rgb, 1, center);
        eye = saturate(eye);
    }
    return eye * _BaseColor.rgb;
}

#if defined(MH_EYE)
// Independently evaluated isotropic microfacet BRDF. URP light intensities use
// a diffuse convention with pi absorbed into radiance, so the NDF below does too.
// Keep the narrow lobe arithmetic in float, including the final accumulation.
float3 MHEyeDirect(Light light, float3 n, float3 diffuseNormal, float3 v, float3 diffuse, float3 f0, float perceptualRoughness)
{
    float facing = saturate(dot(n, light.direction));
    float viewFacing = max(abs(dot(n, v)), 1e-5);
    float3 bisector = SafeNormalize(v + float3(light.direction));
    float projection = saturate(dot(n, bisector));
    float slope = max(perceptualRoughness * perceptualRoughness, 0.000625);
    float variance = slope * slope;
    // This form avoids cancellation near a perfectly aligned reflection.
    float denominator = (1 - projection * projection) + variance * projection * projection;
    float distribution = variance / max(denominator * denominator, 1e-12);
    float grazing = pow(1 - saturate(dot(v, bisector)), 5);
    float3 reflectance = f0 + (1 - f0) * grazing;
    float maskingDenominator = 2 * (2 * facing * viewFacing * (1 - slope) + slope * (facing + viewFacing));
    float3 specular = distribution * reflectance / max(maskingDenominator, 1e-6);
    // _PunctualSpecular (default 1) scales the punctual highlight only: see the forward-path note in MHFragment.
    return (diffuse * saturate(dot(diffuseNormal, light.direction)) + specular * facing * _PunctualSpecular)
        * light.color * light.distanceAttenuation * light.shadowAttenuation;
}

float3 MHEyeLighting(InputData inputData, SurfaceData surface, float3 diffuseNormal)
{
    BRDFData brdf;
    InitializeBRDFData(surface, brdf);
    AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(inputData, surface);
    half4 shadowMask = CalculateShadowMask(inputData);
    Light key = GetMainLight(inputData, shadowMask, ao);
    MixRealtimeAndBakedGI(key, inputData.normalWS, inputData.bakedGI);
    BRDFData coat = (BRDFData)0;
    float3 result = GlobalIllumination(brdf, coat, 0, inputData.bakedGI,
        ao.indirectAmbientOcclusion, inputData.positionWS, inputData.normalWS,
        inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
    result += MHEyeDirect(key, inputData.normalWS, diffuseNormal, inputData.viewDirectionWS,
        brdf.diffuse, surface.specular, 1 - surface.smoothness);
#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, ao);
        result += MHEyeDirect(light, inputData.normalWS, diffuseNormal, inputData.viewDirectionWS,
            brdf.diffuse, surface.specular, 1 - surface.smoothness);
    }
    #endif
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, ao);
        result += MHEyeDirect(light, inputData.normalWS, diffuseNormal, inputData.viewDirectionWS,
            brdf.diffuse, surface.specular, 1 - surface.smoothness);
    LIGHT_LOOP_END
#endif
#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    result += inputData.vertexLighting * brdf.diffuse;
#endif
    return result + surface.emission;
}
#endif

half3 MHSurfaceAlbedo(float2 uv)
{
    half3 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
#if defined(MH_SKIN) && defined(MH_ANIMATED_MAPS)
    // Rig-driven colour maps share the wrinkle region masks: default + sum_N (color_N - default) * mask_N.
    half3 masks = MHAnimatedMapMasks(uv);
    color += (SAMPLE_TEXTURE2D(_WrinkleColor1, sampler_BaseMap, uv).rgb - color) * masks.x
           + (SAMPLE_TEXTURE2D(_WrinkleColor2, sampler_BaseMap, uv).rgb - color) * masks.y
           + (SAMPLE_TEXTURE2D(_WrinkleColor3, sampler_BaseMap, uv).rgb - color) * masks.z;
#endif
    color *= _BaseColor.rgb;
#if defined(MH_SKIN)
    // Frozen source controls remain explicit data; compose the authored linear
    // color layers before either lighting or reflectance-aware diffusion.
    half3 weights = SAMPLE_TEXTURE2D(_MakeupMask, sampler_MakeupMask, uv).rgb * _MakeupWeights.xyz;
    color = lerp(color, _FoundationColor.rgb, weights.r);
    color = lerp(color, _ConcealerColor.rgb, weights.g);
    color = lerp(color, _BlusherColor.rgb, weights.b);
#endif
    return color;
}

// Layered teeth (Skin shader, opt-in _TeethLayers): a real-time stand-in for the two-layer tooth model of Velinov et al.
// 2018 ("Appearance Capture and Modeling of Human Teeth"): a translucent enamel shell (thinner at the gums, thicker toward
// the tip) over a scattering dentin core, with enamel only near the incisal edge. The view sees the enamel's own
// scattering (_EnamelColor, linear albedo) by its opacity along the view path, 1 - exp(-density x thickness / N.V), and
// through it the dentin (base map x _DentinTint) or, where there is no dentin, the oral cavity behind the thin edge
// (_CavityColor). _TeethLayerMap (linear): R crown, G dentin coverage, B enamel thickness (taro_teeth_layers_bake.py).
// Only the albedo changes, so lighting, diffusion and specular stay as they are; one texture fetch and a few ALU.
// Teeth corner shadow (opt-in, _TeethContactAO > 0; teeth material only). Toward the mouth corners the lips and cheeks
// close in front of the crowns and shade them, which the shadow maps miss at this scale (and the key sits near the
// camera axis). Screen-space, from the depth prepass: 8 reads each way along the camera's horizontal, out to
// _TeethContactRadius metres, find the horizon on each side (rise over the tooth = (depth in front - _TeethContactBias)
// / distance across, signed; occluders fade out toward _TeethContactDepth in front). The two horizon angles add up to
// how far the tooth sits in a valley: 0 on the front of the arch (it falls away on both sides) or on a row seen at
// an angle (one side rises, the other falls by as much), a right angle (cheek on one side, the arch coming forward on
// the other) = full shadow; under 15 degrees is ignored (front teeth of a wide-open mouth, the lips rising far to each
// side). Only sideways: an all-round disc also took the lips above and below and greyed the whole
// row. Crowns only (_TeethLayerMap red): the tongue lies in that valley everywhere. The shadow multiplies the crown
// albedo toward _TeethContactColor (warm: a shaded tooth is lit by the lips and tongue) and the specular reflectance
// toward 0. Needs _CameraDepthTexture before opaques (depth prepass).
static half mhTeethContact = 1;
half MHTeethContactOcclusion(float3 positionWS)
{
    if (_TeethContactAO <= 0) return 1;
    float2 uv = ComputeNormalizedDeviceCoordinates(positionWS, UNITY_MATRIX_VP);
    float z = LinearEyeDepth(positionWS, UNITY_MATRIX_V);
    float uvPerMetre = abs(UNITY_MATRIX_P._m00) * .5 / max(z, 1e-3);
    float2 rise = -8;
    [unroll] for (int i = 0; i < 8; i++)
    {
        float d = _TeethContactRadius * (i + 1) / 8;
        float2 o = float2(d * uvPerMetre, 0);
        float2 dz = z - float2(LinearEyeDepth(SampleSceneDepth(saturate(uv - o)), _ZBufferParams), LinearEyeDepth(SampleSceneDepth(saturate(uv + o)), _ZBufferParams));
        rise = max(rise, (dz - _TeethContactBias) / d * saturate(1 - dz / _TeethContactDepth));
    }
    float valley = atan(rise.x) + atan(rise.y);
    return saturate(1 - _TeethContactAO * saturate((valley - .26) / (HALF_PI - .26)));
}
half3 MHTeethContactTint() { return lerp(_TeethContactColor.rgb, 1, mhTeethContact); }

half3 MHTeethLayered(half3 albedo, float2 uv, float3 normalWS, float3 positionWS)
{
    if (_TeethLayers <= 0 && _TeethContactAO <= 0) return albedo;
    half3 layer = SAMPLE_TEXTURE2D(_TeethLayerMap, sampler_TranslucencyMap, uv).rgb;
    mhTeethContact = lerp(1, MHTeethContactOcclusion(positionWS), layer.r);   // crowns only, not the tongue and gums
    if (_TeethLayers <= 0) return albedo * MHTeethContactTint();
    half nv = max(saturate(dot(normalize(normalWS), GetWorldSpaceNormalizeViewDir(positionWS))), .2h);
    half enamel = 1 - exp(-_EnamelDensity * layer.b / nv);
    half3 under = lerp(_CavityColor.rgb, albedo * _DentinTint.rgb, layer.g);
    // Back teeth sit in the mouth's shadow: the cheeks and lips occlude direct light too, which shadow maps miss at
    // this scale. _TeethShadowFloor < 1 darkens the layered crowns by the mouth occlusion map (Epic's T_Teeth_mouthOcc:
    // front teeth open, molars occluded); 1 = off. The constant enamel colour otherwise lit the
    // molars like the incisors.
    half shade = 1;
    if (_TeethShadowFloor < 1)
    {
        half occ = SAMPLE_TEXTURE2D(_MouthOcclusionMap, sampler_MouthOcclusionMap, uv).r;
        // Back teeth: full shadow below _TeethShadowStart mouth occlusion, full light above _TeethShadowEnd (visible
        // crowns span about .7 at the molars to .94 at the incisors on Taro's map).
        shade = lerp(_TeethShadowFloor, 1, smoothstep(_TeethShadowStart, max(_TeethShadowEnd, _TeethShadowStart + .001h), occ));
    }
    // Gum-to-tip fade (_TeethGumShade, signed: > 0 darkens toward the gum line, < 0 toward the incisal edge) along the
    // layer map's gum-to-edge coordinate s (enamel thickness .35 at the gum to 1 at the edge, taro_teeth_layers_bake.py),
    // and _TeethEdgeShade darkening the enamel-only edge (no dentin). Both 0 = off.
    half s = saturate((layer.b - .35h) / .65h);
    shade *= _TeethGumShade >= 0 ? lerp(1 - _TeethGumShade, 1, s) : lerp(1, 1 + _TeethGumShade, s);
    shade *= lerp(1, 1 - _TeethEdgeShade, 1 - layer.g);
    // _TeethGumColor: colour gain at the gum end of each crown, fading to 1 at the incisal edge (default 1 = off).
    half3 gumTint = lerp(_TeethGumColor.rgb, 1, s);
    return lerp(albedo, lerp(under, _EnamelColor.rgb, enamel) * shade * gumTint, saturate(layer.r * _TeethLayers)) * MHTeethContactTint();
}


float2 MHScalarTextureUV(float2 uv, float flipY)
{
    return float2(uv.x, lerp(uv.y, 1 - uv.y, flipY));
}

// MH_SHADE_FULL: lit colour. MH_SHADE_DIFFUSE: the skin's diffuse part only (rgb) and its diffusion amount (a).
// MH_SHADE_SPLIT (skin): both from one evaluation, for the single-draw skin diffusion path.
#define MH_SHADE_FULL 0
#define MH_SHADE_DIFFUSE 1
#define MH_SHADE_SPLIT 2
struct MHShadeResult { half4 color; half4 diffuse; };
MHShadeResult MHShadeColor(half4 color)
{
    MHShadeResult result;
    result.color = color;
    result.diffuse = 0;
    return result;
}

MHShadeResult MHShadeSurface(MHVaryings input, half faceSign, int mode)
{
    bool diffuseOnly = mode == MH_SHADE_DIFFUSE;
    bool split = mode == MH_SHADE_SPLIT;
    half3 diffuse = 0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half alpha = MHAlpha(input.uv);
    half3 albedo = MHSurfaceAlbedo(input.uv);
#if defined(MH_SKIN)
    albedo = MHTeethLayered(albedo, input.uv, input.normalWS, input.positionWS);
#endif
#if defined(MH_EYE)
    half irisMask;
    albedo = MHEyeAlbedo(input, irisMask);
#endif
    half3 normal = MHNormal(input, faceSign);
#if defined(MH_EYE)
    // Smooth corneal normals keep reflections independent of iris texture detail.
    normal = normalize(lerp(normal, normalize(input.normalWS), irisMask * _ClearCoat));
    if (_DeferredEyeInputs > 0.5)
        normal = normalize(round((float3(normal) * 0.5 + 0.5) * 1023) * (2.0 / 1023) - 1);
#endif
#if defined(MH_REFERENCE_DEBUG)
    // Reference-only variant: material inputs can be compared without lighting.
    if (_MHReferenceDebug < 1.5) return MHShadeColor(half4(albedo, 1));
    if (_MHReferenceDebug < 2.5) return MHShadeColor(half4(normal * 0.5 + 0.5, 1));
    if (_MHReferenceDebug > 5.5 && _MHReferenceDebug < 6.5)
        return MHShadeColor(half4(SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, MHScalarTextureUV(input.uv, _RoughnessUVFlipY)).rgb, 1));
    if (_MHReferenceDebug > 7.5 && _MHReferenceDebug < 8.5)
    {
        // Teeth data (look-development sliders): red 10 + crown mask (only the layered teeth material; others black),
        // green mouth occlusion, blue the layer map's enamel thickness (gum .35 -> edge 1).
        half3 layer = SAMPLE_TEXTURE2D(_TeethLayerMap, sampler_TranslucencyMap, input.uv).rgb;
        half occ = SAMPLE_TEXTURE2D(_MouthOcclusionMap, sampler_MouthOcclusionMap, input.uv).r;
        // Alpha carries the translucency map too: the layer map shares its sampler, which must stay referenced.
        half crown = SAMPLE_TEXTURE2D(_TranslucencyMap, sampler_TranslucencyMap, input.uv).r;
        return MHShadeColor(_TeethLayers > 0 ? half4(10 + layer.r, occ, layer.b, 1 + crown) : half4(0, 0, 0, 1));
    }
    if (_MHReferenceDebug >= 6.5) return MHShadeColor(half4((1 - input.uv.y).xxx, 1));
#endif
    half4 srmf = SAMPLE_TEXTURE2D(_SRMFMap, sampler_SRMFMap, input.uv);
    half roughness = lerp(_Roughness, srmf.g, _UseSRMF);
    roughness = lerp(roughness, SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, MHScalarTextureUV(input.uv, _RoughnessUVFlipY)).r, _UseRoughnessMap);
    half specular = lerp(_Specular, srmf.r, _UseSRMF);
    specular = lerp(specular, SAMPLE_TEXTURE2D(_SpecularMap, sampler_SpecularMap, MHScalarTextureUV(input.uv, _SpecularUVFlipY)).r, _UseSpecularMap);
#if defined(MH_EYE)
    if (_DeferredEyeInputs > 0.5)
    {
        roughness = round(float(roughness) * 255) / 255;
        specular = round(float(specular) * 255) / 255;
    }
#endif
#if defined(MH_SKIN)
    roughness = lerp(_RoughnessMin, _RoughnessMax, roughness);
    if (_ModernHeadInputs > .5)
    {
        // Head and neck occupy distinct source-UV regions. These are authored
        // material-input semantics, evaluated before the independent BRDF.
        float sourceV = 1 - input.uv.y;
        float head = saturate((1 - abs(sourceV) / max(_HeadRegionMask.x, .001)) / max(1 - _HeadRegionMask.y, .001));
        float2 channels = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, MHScalarTextureUV(input.uv, _RoughnessUVFlipY)).rg;
        float chest = lerp(_HeadRoughnessControls.x, _HeadRoughnessControls.y, channels.g);
        float face = lerp(_RoughnessMin, _RoughnessMax, channels.g);
        roughness = 2 * channels.r * lerp(chest, face, head) * _HeadRoughnessControls.z;
        float edge = pow(1 - saturate(dot(normal, GetWorldSpaceNormalizeViewDir(input.positionWS))), _HeadFresnelExponent);
        roughness *= lerp(1, _HeadRoughnessControls.w, edge);
        float cavity = SAMPLE_TEXTURE2D(_SpecularMap, sampler_SpecularMap, MHScalarTextureUV(input.uv, _SpecularUVFlipY)).r;
        specular = saturate(pow(saturate(cavity), lerp(_HeadCavityPowers.x, _HeadCavityPowers.y, head)) * _Specular);
    }
    half3 makeup = SAMPLE_TEXTURE2D(_MakeupMask, sampler_MakeupMask, input.uv).rgb * _MakeupWeights.xyz;
    half foundationRoughness = roughness * _MakeupRoughness.x;
    roughness = lerp(roughness, foundationRoughness, makeup.r);
    roughness = lerp(roughness, foundationRoughness, makeup.g);
    roughness = lerp(roughness, roughness * _MakeupRoughness.y, makeup.b);
    if (_ModernHeadInputs > .5) roughness = saturate(roughness);
    // Some legacy masks store a cavity response, not a linear dielectric F0.
    // Keep the remap explicit; modern SRMF defaults to an identity curve.
    float legacyFresnelInput = 1;
    bool evaluateLegacyFresnel = _LegacySourceFresnel > .5;
#if defined(MH_REFERENCE_DEBUG)
    evaluateLegacyFresnel = evaluateLegacyFresnel || _MHReferenceDebug > 4.5;
#endif
    if (evaluateLegacyFresnel) legacyFresnelInput = MHLegacyFresnelInput(input);
    float specularMultiplier = _LegacySourceFresnel > .5
        ? _LegacySpecularMultiplier * legacyFresnelInput : _SpecularMultiplier;
    if (_ModernHeadInputs < .5)
        specular = pow(saturate(specular), max(_SpecularCavityPower, 0.01h)) * specularMultiplier;
#endif
#if defined(MH_REFERENCE_DEBUG)
    // Raw material scalars, before BRDF conversion to dielectric reflectance.
    if (_MHReferenceDebug < 3.5) return MHShadeColor(half4(roughness.xxx, 1));
#if defined(MH_SKIN)
    if (_MHReferenceDebug > 4.5) return MHShadeColor(half4(legacyFresnelInput.xxx, 1));
#endif
    return MHShadeColor(half4(specular.xxx, 1));
#endif
    half scatter = _Scatter * SAMPLE_TEXTURE2D(_ScatterMap, sampler_ScatterMap, input.uv).r;
#if defined(MH_SKIN)
    if (_Translucency > 0) mhTranslucency = _Translucency * SAMPLE_TEXTURE2D(_TranslucencyMap, sampler_TranslucencyMap, input.uv).r;
#endif
    half fuzz = lerp(_Fuzz, srmf.a, _UseSRMF * _UsePackedFuzz);
    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.positionCS = input.positionCS;
    inputData.normalWS = normal;
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.bakedGI = SampleSH(normal);
    inputData.shadowMask = half4(1, 1, 1, 1);
    inputData.vertexLighting = VertexLighting(input.positionWS, normal);
    SurfaceData surface = (SurfaceData)0;
    surface.albedo = albedo;
    // UE Specular 0.5 maps to dielectric F0 0.04; Unity specular workflow expects F0.
    surface.specular = (0.08 * specular).xxx;
    half primaryRoughness = roughness;
    half secondaryRoughness = _SecondaryRoughness;
#if defined(MH_SKIN)
    primaryRoughness = lerp(roughness, roughness * _PrimaryRoughnessScale, _ProfileLobes);
    secondaryRoughness = lerp(_SecondaryRoughness, roughness * _SecondaryRoughnessScale, _ProfileLobes);
#endif
    surface.smoothness = 1 - clamp(primaryRoughness, 0.025, 1);
    surface.normalTS = half3(0, 0, 1);
    surface.occlusion = 1;
    surface.alpha = 1;
#if defined(MH_SKIN)
    // Teeth: the mouth cavity occludes ambient light and reflections (Epic's teeth material dims reflections by its
    // mouth occlusion map). Indirect diffuse and specular take the occlusion; specular reflectance is also scaled by
    // its square so teeth deep in the mouth lose the sheen that otherwise reads as plastic. Both default to 0 (off).
    if (_MouthOcclusionStrength > 0 || _DimReflections > 0)
    {
        half mouth = SAMPLE_TEXTURE2D(_MouthOcclusionMap, sampler_MouthOcclusionMap, input.uv).r;
        surface.occlusion = lerp(1, mouth, _MouthOcclusionStrength);
        surface.specular *= lerp(1, mouth * mouth, _DimReflections);
    }
    surface.specular *= mhTeethContact;   // teeth corner shadow (1 unless _TeethContactAO)
#endif
#if defined(MH_EYE)
    half f0 = pow((_CorneaIOR - 1) / (_CorneaIOR + 1), 2);
    if (_UseAuthoredEyeSpecular < .5)
        surface.specular = lerp(surface.specular, f0.xxx, irisMask * _ClearCoat);
    surface.smoothness = 1 - lerp(max(roughness, 0.22h), roughness, irisMask);
    if (_DesktopEyeLighting > 0.5)
        surface.smoothness = 1 - clamp(roughness, 0.025h, 1);
#endif
#if defined(MH_SKIN)
    // One light loop for everything the skin outputs (MetaHumanSkinLighting.hlsl): the lit colour, its diffuse part for
    // the skin diffusion, and the scatter, fuzz and teeth-translucency terms.
    float3 skinDiffuse;
    half3 color = MHSkinLighting(inputData, surface, roughness, clamp(primaryRoughness, .025, 1), clamp(secondaryRoughness, .025, 1),
        _SecondaryWeight, input.tangentWS.xyz, scatter, fuzz, skinDiffuse);
    if (diffuseOnly) color = skinDiffuse;
    diffuse = skinDiffuse;
#else
    // Eyes and hair cards: URP's own light loop.
    half3 color = UniversalFragmentPBR(inputData, surface).rgb;
#endif
#if defined(MH_EYE)
    // Punctual lights reflect in the cornea as pin-point dots (a grid of spots standing in for a softbox reads as a row
    // of dots). _PunctualSpecular < 1 removes that share of the main and additional lights' specular: the same
    // evaluation with no albedo, emission or ambient (occlusion 0) is the punctual specular alone. Environment
    // reflections (the reflection-probe catchlight) and all diffuse lighting are unchanged. Default 1 (off). The
    // desktop eye path (MHEyeLighting) applies the same scale inside MHEyeDirect.
    if (_PunctualSpecular < 1 && _DesktopEyeLighting < 0.5)
    {
        SurfaceData specOnly = surface;
        specOnly.albedo = 0; specOnly.emission = 0; specOnly.occlusion = 0;
        color = max(0, color - (1 - _PunctualSpecular) * UniversalFragmentPBR(inputData, specOnly).rgb);
    }
    if (_DesktopEyeLighting > 0.5)
    {
        half3 diffuseNormal = normal;
        if (_EyeFamily > .5 && _UseEyeAxisMap > .5)
        {
            half3 axisTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_EyeAxisMap, sampler_EyeAxisMap, input.uv), 1);
            half3 n = normalize(input.normalWS), t = normalize(input.tangentWS.xyz);
            half3 b = cross(n, t) * input.tangentWS.w;
            half3 axis = normalize(TransformTangentToWorld(axisTS, half3x3(t, b, n)));
            diffuseNormal = normalize(lerp(normal, axis, irisMask));
        }
        color = MHEyeLighting(inputData, surface, diffuseNormal);
    }
    if (_CatchlightIntensity > 0)
    {
        // Analytic softbox catchlight (opt-in; 0 = off): the eye's mirror reflection of one rounded-square area light
        // of half-size _CatchlightShape.x degrees (soft edge .y degrees) in world direction _CatchlightDirection (from
        // the eye toward the light), at radiance _CatchlightIntensity x _CatchlightColor, weighted by Schlick Fresnel
        // from the surface reflectance. URP has no area-light specular: punctual lights reflect as pin-points.
        float3 v = inputData.viewDirectionWS;
        float3 r = reflect(-v, inputData.normalWS);
        float3 d = normalize(_CatchlightDirection.xyz);
        float3 side = normalize(cross(float3(0, 1, 0), d)), up = cross(d, side);
        float along = dot(r, d);
        float2 p = float2(dot(r, side), dot(r, up)) / max(along, 1e-3);
        float size = tan(radians(_CatchlightShape.x));
        float soft = max(tan(radians(_CatchlightShape.x + _CatchlightShape.y)) - size, 1e-4);
        float q = sqrt(sqrt(pow(abs(p.x), 4) + pow(abs(p.y), 4)));
        float mask = along > 0 ? 1 - smoothstep(size, size + soft, q) : 0;
        half3 f0 = surface.specular;
        half3 fresnel = f0 + (1 - f0) * pow(1 - saturate(dot(inputData.normalWS, v)), 5);
        color += _CatchlightColor.rgb * (_CatchlightIntensity * mask) * fresnel;
    }
#endif
#if defined(MH_HAIR)
    // Card highlight: a pass over the lights of its own.
    Light main = GetMainLight(inputData.shadowCoord, input.positionWS, inputData.shadowMask);
    color += MHExtraLight(main, normal, inputData.viewDirectionWS, input.tangentWS.xyz, albedo, scatter, fuzz);
#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, input.positionWS, inputData.shadowMask);
        color += MHExtraLight(light, normal, inputData.viewDirectionWS, input.tangentWS.xyz, albedo, scatter, fuzz);
    }
    #endif
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, input.positionWS, inputData.shadowMask);
        color += MHExtraLight(light, normal, inputData.viewDirectionWS, input.tangentWS.xyz, albedo, scatter, fuzz);
    LIGHT_LOOP_END
#endif
#endif
    MHShadeResult result;
    result.diffuse = 0;
    if (diffuseOnly || split)
        result.diffuse = half4(MixFogColor(diffuseOnly ? color : diffuse, 0, input.fog), _DiffusionAmount * SAMPLE_TEXTURE2D(_ScatterMap, sampler_ScatterMap, input.uv).r);
#if defined(MH_HAIR)
    result.color = half4(MixFog(color, input.fog), alpha);
#else
    result.color = half4(MixFog(color, input.fog), 1);
#endif
    return result;
}

half4 MHShade(MHVaryings input, half faceSign, bool diffuseOnly)
{
    MHShadeResult result = MHShadeSurface(input, faceSign, diffuseOnly ? MH_SHADE_DIFFUSE : MH_SHADE_FULL);
    return diffuseOnly ? result.diffuse : result.color;
}

half4 MHFragment(MHVaryings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
{
    half sign = 1;
#if defined(MH_HAIR)
    sign = IS_FRONT_VFACE(frontFace, 1, -1);
#endif
    return MHShade(input, sign, false);
}

#if defined(MH_SKIN)
// Per-material neutral scattering radius for the normalized diffusion (Skin _DiffusionNeutralRadius, mm; 0 = the
// feature's profile): enamel scatters about equally in every channel, unlike the red-dominant skin profile. Carried in
// the albedo target's alpha as 1 + radius (masked by _TranslucencyMap, e.g. crowns only); 1 = use the profile.
half MHDiffusionRadiusCode(float2 uv)
{
    return 1 + (_DiffusionNeutralRadius > 0 ? _DiffusionNeutralRadius * SAMPLE_TEXTURE2D(_TranslucencyMap, sampler_TranslucencyMap, uv).r : 0);
}

struct MHDiffuseOutput { half4 diffuse : SV_Target0; float4 guide : SV_Target1; half4 albedo : SV_Target2; };
MHDiffuseOutput MHDiffuseFragment(MHVaryings input)
{
    MHDiffuseOutput output;
    clip(_DiffusionAmount - 0.0001);
    output.diffuse = MHShade(input, 1, true);
    output.guide = float4(normalize(input.normalWS), -TransformWorldToView(input.positionWS).z);
    output.albedo = half4(MHTeethLayered(MHSurfaceAlbedo(input.uv), input.uv, input.normalWS, input.positionWS), MHDiffusionRadiusCode(input.uv));
    return output;
}

// Single-draw skin diffusion (SkinDiffusionFeature.singleDraw): the lit colour and the diffusion inputs from one
// evaluation. Surfaces without diffusion write zero inputs, which the filters and composite skip, as the cleared
// targets of the diffuse-only draw.
struct MHSplitOutput { half4 color : SV_Target0; half4 diffuse : SV_Target1; float4 guide : SV_Target2; half4 albedo : SV_Target3; };
MHSplitOutput MHSplitFragment(MHVaryings input)
{
    MHShadeResult shaded = MHShadeSurface(input, 1, MH_SHADE_SPLIT);
    MHSplitOutput output;
    output.color = shaded.color;
    bool diffuses = _DiffusionAmount >= 0.0001;
    output.diffuse = diffuses ? shaded.diffuse : 0;
    output.guide = diffuses ? float4(normalize(input.normalWS), -TransformWorldToView(input.positionWS).z) : 0;
    output.albedo = diffuses ? half4(MHTeethLayered(MHSurfaceAlbedo(input.uv), input.uv, input.normalWS, input.positionWS), MHDiffusionRadiusCode(input.uv)) : 0;
    return output;
}

// Set by SkinDiffusionFeature for the cameras where the single skin draw replaces the forward pass.
float _MHSkinSingleDraw;
MHVaryings MHSkinForwardVertex(MHAttributes input)
{
    [branch] if (_MHSkinSingleDraw > 0.5)
    {
        MHVaryings culled = (MHVaryings)0;
        culled.positionCS = float4(2, 2, 2, 1);
        return culled;
    }
    return MHVertex(input);
}
#endif

float3 _LightDirection;
float3 _LightPosition;
MHVaryings MHShadowVertex(MHAttributes input)
{
    MHVaryings output = MHVertex(input);
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 direction = normalize(_LightPosition - output.positionWS);
#else
    float3 direction = _LightDirection;
#endif
    output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS, output.normalWS, direction));
    output.positionCS = ApplyShadowClamping(output.positionCS);
    return output;
}
half4 MHDepthFragment(MHVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    half alpha = MHAlpha(input.uv);
    return half4(0, 0, 0, alpha);
}
half4 MHDepthNormalsFragment(MHVaryings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    half sign = 1;
#if defined(MH_HAIR)
    sign = IS_FRONT_VFACE(frontFace, 1, -1);
#endif
    half alpha = MHAlpha(input.uv);
    half3 normalWS = MHNormal(input, sign);
#if defined(_GBUFFER_NORMALS_OCT)
    float2 octNormal = PackNormalOctQuadEncode(normalWS);
    return half4(PackFloat2To888(saturate(octNormal * 0.5 + 0.5)), alpha);
#else
    return half4(normalWS, alpha);
#endif
}
#endif

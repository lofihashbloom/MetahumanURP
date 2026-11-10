// Independent analytic-area and compensated-rectangle strand profiles.
Shader "MetaHuman/URP/GroomStrands"
{
    Properties
    {
        [Enum(StochasticFallback,0,ContinuousTransparency,1)] _CoverageMode("Coverage Mode (Continuous Requires Renderer Feature)", Float) = 0
        [Enum(AnalyticArea,0,CompensatedRectangle,1)] _RasterProfile("Strand Raster Profile",Float)=0
        _RectangleMinimumPixels("Rectangle Minimum Diameter (Pixels)",Range(.25,2))=1
        [Enum(Unit,0,MeshChannel3Scale,1,UniformScale,2)] _RectangleCoverageSource("Rectangle Coverage Source",Float)=0
        _RectangleCoverageScale("Rectangle Uniform Coverage Scale",Range(0,2))=1
        [HideInInspector] _RectangleCoverageDataBound("Validated Mesh Coverage Data",Float)=0
        [MainColor] _BaseColor("Hair Color", Color) = (0.055,0.035,0.025,1)
        _Roughness("Longitudinal Roughness", Range(0.12,1)) = 0.4
        _Specular("Reflection Strength", Range(0,1)) = 0.35
        _SecondaryWeight("Secondary Reflection Weight", Range(0,1)) = 0.3
        _RootDarkening("Root Darkening", Range(0,1)) = 0.5
        [Enum(Legacy,0,NormalizedReflection,1,FourPathLookup,2,HalfNormalSineReflection,3,FacetSineReflection,4,UnitMatchedDirect,5)] _FiberModel("Fiber Scattering Model", Float) = 0
        [Enum(Disabled,0,Roughness037Reference,1,Roughness037Envelope,2)] _LocalScatteringProfile("Local Scattering (Experimental Reference)", Float) = 0
        [Toggle] _UseGroomInputLUT("Use Authored Groom Material Inputs", Float) = 0
        [Enum(ViewApproximation,0,SourceMaterialFrame,1)] _GroomTangentFrame("Authored Tangent Coordinate Frame", Float) = 0
        _GroomTangentLongitudinalScale("Authored Tangent Basis Longitudinal Scale",Range(0.25,1))=1
        _GroomRadianceDepthBandPixels("Nearest Radiance Depth Band (0 = Hard Selection)",Range(0,2))=0
        _GroomColorRoughness("Authored Linear Color / Roughness", 2D) = "white" {}
        _GroomTangentSpecular("Authored Tangent Delta + .25 / Specular", 2D) = "gray" {}
        _GroomColorTint("Authored Color Tint (Legacy / Reflection)", Color) = (1,1,1,1)
        [Enum(PerceptualSquared,0,DisneyFit,1)] _FiberVarianceAdapter("Fiber Longitudinal Variance Adapter", Float) = 0
        _FiberEta("Fiber Index Of Refraction", Range(1.01,2)) = 1.55
        _FiberSpecularScale("Fiber R Scale Per Source Specular", Range(0,2)) = 2
        _FiberRShiftDegrees("Reflection Direction-Sum Shift (Degrees)", Range(-10,10)) = 0
        _FiberSineOffset("Projected-Sine R Offset (Unit-Probe Profile)", Range(-0.3,0.3)) = -0.139173101
        _FiberLookup("Fixed Fiber Angular Profile", 2DArray) = "" {}
        [Toggle] _FiberLookupValid("Angular Profile Validated And Assigned", Float) = 0
        _FiberLookupSize("Angular Profile Width / Height", Vector) = (512,128,0,0)
        _FiberLookupEta("Angular Profile Index Of Refraction", Float) = 1.55
        _MinimumPixels("Minimum Ribbon Width (MSAA Coverage)", Range(0.5,2)) = 1
        _StrandFraction("Strand LOD Fraction (Kept Strands Widened)", Range(0.05,1)) = 1
        [Toggle] _CoverageRasterPadding("Rasterize Full Pixel Filter Support", Float) = 1
        [Toggle] _CoverageExactArea("Integrate Physical Strand Pixel Area", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        ZWrite On
        AlphaToMask On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "HairFiberLookup.hlsl"
        #include "HairDirectProfile.hlsl"
        #include "HairLocalReferenceProfile.hlsl"
        #include "HairLocalEnvelopeProfile.hlsl"
        #include "GroomSelfShadow.hlsl"
        #include "GroomSway.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        float _RasterProfile,_RectangleMinimumPixels,_RectangleCoverageSource,_RectangleCoverageScale,_RectangleCoverageDataBound;
        float _Roughness;
        half _Specular, _SecondaryWeight, _RootDarkening;
        float _MinimumPixels, _CoverageMode, _StrandFraction;
        float _CoverageRasterPadding, _CoverageExactArea;
        float _FiberModel, _UseGroomInputLUT, _FiberVarianceAdapter, _FiberEta;
        float _FiberSpecularScale, _FiberRShiftDegrees, _FiberLookupValid, _FiberLookupEta;
        float4 _GroomColorTint, _FiberLookupSize;
        float _GroomTangentFrame;
        float _GroomTangentLongitudinalScale;
        float _GroomRadianceDepthBandPixels;
        float _LocalScatteringProfile;
        float _FiberSineOffset;
        CBUFFER_END
        float _MHGroomOpticalDepthOnly; // Global, set per frame by GroomTransparencyFeature.
        // Light sway (GroomSway component, per-renderer property block; zero = off): this frame's and the previous
        // frame's rotation about the pivot, see GroomSway.hlsl. Outside UnityPerMaterial, as renderer-only values.
        float4 _MHGroomSwayPivot, _MHGroomSwayAxisAngle, _MHGroomSwayFine, _MHGroomSwayFront, _MHGroomSwayPrevPivot, _MHGroomSwayPrevAxisAngle, _MHGroomSwayPrevFine;
        TEXTURE2D(_GroomColorRoughness);
        TEXTURE2D(_GroomTangentSpecular);
        TEXTURE2D(_MHGroomNearestDepth);
        TEXTURE2D(_MHGroomTemporalOpticalDepth);
        Texture2DMS<float> _MHGroomNearestDepthMS;
        TEXTURE2D_ARRAY(_FiberLookup); SAMPLER(sampler_FiberLookup);
        struct Attributes { float4 positionOS:POSITION; float3 tangentOS:NORMAL; float4 materialFrame:TANGENT; float2 uv:TEXCOORD0; float2 widthRandom:TEXCOORD1; float2 sourceCov:TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings
        {
            float4 positionCS:SV_POSITION;
            float3 positionWS:TEXCOORD0;
            half3 tangentWS:TEXCOORD1;
            float2 uv:TEXCOORD2;
            half2 coverageRandom:TEXCOORD3;
            half fog:TEXCOORD4;
            float3 materialFrameX:TEXCOORD5;
            float rectangleAlpha:TEXCOORD6;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings StrandVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input, output); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            // Strand LOD: _StrandFraction (0 or 1 = every strand) keeps that fraction of the strands, chosen by the
            // per-strand random, and widens the kept ones by 1/fraction so the groom's coverage is preserved.
            // Dropped strands exit before any work at one point outside the clip volume (no rasterisation).
            float keep = _StrandFraction > 0 && _StrandFraction < 1 ? _StrandFraction : 1;
            [branch] if (keep < 1 && frac(input.widthRandom.y * 7.31 + .17) >= keep)
            {
                output.positionCS = float4(0, 0, -2, 1);
                return output;
            }
            float3 centre = TransformObjectToWorld(input.positionOS.xyz);
            float3 tangent = normalize(TransformObjectToWorldDir(input.tangentOS));
            float3 sway = MHGroomSwayVector(centre, input.uv.y, input.widthRandom.y, _MHGroomSwayPivot, _MHGroomSwayAxisAngle, _MHGroomSwayFine, _MHGroomSwayFront);
            centre = MHGroomSwayAt(centre, _MHGroomSwayPivot.xyz, sway);
            tangent = MHGroomSwayRotate(tangent, sway);
            float3 view = GetWorldSpaceNormalizeViewDir(centre);
            float3 side = cross(tangent, view);
            if (dot(side, side) < 1e-6) side = cross(tangent, abs(tangent.y) < 0.9 ? float3(0,1,0) : float3(1,0,0));
            side = normalize(side);
            float depth = max(0.001, -TransformWorldToView(centre).z);
            float pixelWorld = 2 * (unity_OrthoParams.w > 0.5 ? 1 : depth) / (abs(UNITY_MATRIX_P._m11) * _ScaledScreenParams.y);
            float diameter = input.widthRandom.x / keep;
            float expanded = max(diameter, pixelWorld * _MinimumPixels);
            // Pixel filtering extends beyond the strand's geometric edge. A
            // pixel-square's maximum projected width is sqrt(2) pixels.
            float rasterWidth = expanded + (_CoverageMode > .5 && _CoverageRasterPadding > .5 ? pixelWorld * 1.41421356237 : 0);
            output.rectangleAlpha = 0;
            if (_RasterProfile > .5 && _CoverageMode > .5)
            {
                // Independently derived rectangle dilation with opacity compensation.
                // Geometry and optical compensation are separate measurements:
                // radial camera distance sets support; eye depth sets compensation.
                float3 cameraFacing = -GetViewForwardDir();
                float3 screenSide = cross(tangent,cameraFacing);
                if (dot(screenSide,screenSide) < 1e-6) screenSide = cross(tangent,abs(tangent.y)<.9?float3(0,1,0):float3(1,0,0));
                side = normalize(screenSide);
                float radialDistance = distance(GetCameraPositionWS(),centre);
                float minimumAtEyeDepth = pixelWorld * _RectangleMinimumPixels;
                float supportAtDistance = 2 * (unity_OrthoParams.w > .5 ? 1 : radialDistance)
                    / (abs(UNITY_MATRIX_P._m11) * _ScaledScreenParams.y) * _RectangleMinimumPixels;
                rasterWidth = max(diameter,supportAtDistance);
                expanded = rasterWidth;
                float k = 1;
                if (_RectangleCoverageSource > 1.5) k = _RectangleCoverageScale;
                else if (_RectangleCoverageSource > .5 && _RectangleCoverageDataBound > .5)
                    k = input.sourceCov.x; // CPU setup validates every mesh before setting the binding flag.
                output.rectangleAlpha = diameter / max(diameter,minimumAtEyeDepth) * k;
            }
            output.positionWS = centre + side * (input.uv.x * rasterWidth * 0.5);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.tangentWS = tangent; output.uv = input.uv;
            output.materialFrameX = MHGroomSwayRotate(TransformObjectToWorldDir(input.materialFrame.xyz, false), sway);
            float frameLength2 = dot(output.materialFrameX,output.materialFrameX);
            output.materialFrameX *= rsqrt(max(frameLength2,1e-20));
            output.uv.x *= rasterWidth / expanded;
            output.coverageRandom = half2(saturate(diameter / expanded), input.widthRandom.y);
            output.fog = ComputeFogFactor(output.positionCS.z);
            return output;
        }
        float StrandPixelProjectionCDF(float z, float a, float b)
        {
            // Distribution of a linear coordinate over a unit pixel square.
            // Degenerate axis-aligned cases reduce to a one-dimensional box.
            if (min(a, b) < 0.00001)
                return saturate(.5 + z / max(2 * max(a, b), 0.00001));
            float major = max(a, b), minor = min(a, b), support = major + minor;
            if (z <= -support) return 0;
            if (z >= support) return 1;
            if (abs(z) <= major - minor) return .5 + z / (2 * major);
            float shoulder = support - abs(z);
            float mass = shoulder * shoulder / (8 * major * minor);
            return z < 0 ? mass : 1 - mass;
        }
        float StrandAlpha(Varyings input)
        {
            float alpha;
            if (_RasterProfile > .5 && _CoverageMode > .5)
            {
                alpha = saturate(input.rectangleAlpha * _BaseColor.a);
                clip(alpha-1e-6);
                return alpha;
            }
            if (_CoverageMode > .5 && _CoverageExactArea > .5)
            {
                float a = .5 * abs(ddx(input.uv.x)), b = .5 * abs(ddy(input.uv.x));
                float halfWidth = input.coverageRandom.x;
                alpha = saturate(StrandPixelProjectionCDF(halfWidth - input.uv.x, a, b)
                    - StrandPixelProjectionCDF(-halfWidth - input.uv.x, a, b)) * _BaseColor.a;
            }
            else
            {
                float edge = saturate((1 - abs(input.uv.x)) / max(fwidth(input.uv.x), 0.0001) + 0.5);
                alpha = edge * input.coverageRandom.x * _BaseColor.a;
            }
            clip(alpha - (_CoverageMode > .5 && _CoverageExactArea > .5 ? 0.000001 : 0.001));
            return alpha;
        }
        half StrandCoverage(Varyings input)
        {
            half alpha = StrandAlpha(input);
            // Decorrelate overlapping fine fibres. Reusing the same alpha-to-mask
            // pattern on every strand leaves whole MSAA samples permanently empty.
            // This spatial stochastic baseline needs temporal filtering in future.
            float threshold = frac(InterleavedGradientNoise(input.positionCS.xy, 0) + input.coverageRandom.y);
            clip(alpha - threshold);
            return 1;
        }
        // Legacy remains available for A/B. New fiber response is evaluated in
        // float and uses an explicit cosine-weighted angular-response convention.
        float3 StrandLegacyLight(Light light, float3 tangent, float3 view, float3 albedo,
                                 float roughness, float specular)
        {
            float3 halfVector = SafeNormalize(float3(light.direction) + view);
            float3 facing = SafeNormalize(view - tangent * dot(tangent, view));
            float3 first = SafeNormalize(tangent + facing * 0.10);
            float3 second = SafeNormalize(tangent - facing * 0.18);
            float exponent = max(2.0, 2.0 / max(roughness * roughness, 0.01));
            float a = saturate(1.0 - pow(dot(first, halfVector), 2));
            float b = saturate(1.0 - pow(dot(second, halfVector), 2));
            float reflection = (pow(a, exponent * 0.5) + _SecondaryWeight * pow(b, exponent * 0.25)) * specular * 0.08;
            float diffuse = sqrt(saturate(1.0 - pow(dot(tangent, light.direction), 2)));
            return (albedo * diffuse + reflection) * float3(light.color) * light.distanceAttenuation * light.shadowAttenuation;
        }
        float StrandFiberVariance(float roughness)
        {
            float r = clamp(roughness, 0.01, 1.0);
            if (_FiberVarianceAdapter > 0.5)
            {
                // Published Chiang2016 longitudinal perceptual-width fit. This
                // is an independent adapter candidate, not native equivalence.
                float width = 0.726 * r + 0.812 * r * r + 3.7 * pow(r, 20);
                return max(width * width, 1e-4);
            }
            // Source renderer's R width uses r^2; our normalized kernel takes
            // squared width (variance), hence this explicit r^4 candidate.
            return max(r * r * r * r, 1e-4);
        }
        bool StrandHasFiberLookup()
        {
            if (_FiberLookupValid < 0.5 || abs(_FiberLookupEta - _FiberEta) > 1e-4) return false;
            uint width, height, layers;
            _FiberLookup.GetDimensions(width, height, layers);
            return layers == 4 && width >= 32 && height >= 16
                && abs(float(width) - _FiberLookupSize.x) < 0.5
                && abs(float(height) - _FiberLookupSize.y) < 0.5;
        }
        float3 StrandFiberLight(Light light, float3 tangent, float3 view, float3 albedo,
                                float roughness, float specular, float model)
        {
            if (model < 0.5) return StrandLegacyLight(light, tangent, view, albedo, roughness, specular);
            MHHairFiberAngles angles = MHHairFiberGetAngles(tangent, float3(light.direction), view);
            float variance = StrandFiberVariance(roughness);
            // Verified native R artist multiplier is2*Specular. Kept explicit,
            // independent of eta's Fresnel and never treated as generic .08 F0.
            float reflectionScale = saturate(_FiberSpecularScale * specular);
            float3 radiance;
            if (model > 4.5 && model < 5.5)
            {
                float3 tt,trt;
                MHHairProfilePathKernels(angles,roughness,albedo,_FiberEta,tt,trt);
                float3 reflection=MHHairFiberEvaluateFacetSineR(angles,roughness,_FiberSineOffset,_FiberEta,reflectionScale);
                radiance=MH_FIBER_PI*(reflection+tt+trt);
            }
            else if (model < 1.5 || model > 2.5)
            {
                float3 reflection = model > 3.5
                    ? MHHairFiberEvaluateFacetSineR(angles, roughness, _FiberSineOffset, _FiberEta, reflectionScale)
                    : model > 2.5
                    ? MHHairFiberEvaluateProjectedSineR(angles, roughness, _FiberSineOffset, _FiberEta, reflectionScale)
                    : MHHairFiberEvaluateR(angles, variance,
                        _FiberRShiftDegrees * (MH_FIBER_PI / 180.0), _FiberEta, reflectionScale);
                float legacyDiffuse = sqrt(saturate(1.0 - pow(dot(tangent, light.direction), 2)));
                // Retain only the old diffuse surrogate to isolate the R change.
                radiance = albedo * legacyDiffuse + MH_FIBER_PI * reflection;
            }
            else
            {
                // Fixed-profile, zero-shift/common-variance configuration. The
                // table already contains path-weighted absorption and Fresnel.
                radiance = MH_FIBER_PI * MHHairFiberEvaluateCommonLookup(
                    TEXTURE2D_ARRAY_ARGS(_FiberLookup, sampler_FiberLookup), angles,
                    _FiberLookupSize.xy, variance, reflectionScale);
            }
            // Fixed authored-material reference, not a generic roughness model.
            // The hard scope guard is intentionally diagnostic and default-off.
            if (model > 4.5 && model < 5.5 && _LocalScatteringProfile > .5 && abs(roughness-.37) < 1e-4)
                radiance += MH_FIBER_PI * (_LocalScatteringProfile > 1.5
                    ? MHHairLocalEnvelope037(angles.sinLight, angles.sinView, albedo)
                    : MHHairLocalReference037(angles.sinLight, angles.sinView, albedo));
            return radiance * float3(light.color) * light.distanceAttenuation * light.shadowAttenuation;
        }
        float3 StrandRadiance(Varyings input)
        {
            UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float3 tangent = normalize(float3(input.tangentWS));
            float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
            float roughness = _Roughness, specular = _Specular;
            float3 albedo;
            if (_UseGroomInputLUT > 0.5)
            {
                // Native bake: Seed increases right; source arc length U increases
                // down EXR rows. Linear clamp at(Seed,1-U), fixed LOD0.
                float2 sourceUV = float2(saturate(input.coverageRandom.y), 1.0 - saturate(input.uv.y));
                float4 colorRoughness = SAMPLE_TEXTURE2D_LOD(_GroomColorRoughness, sampler_LinearClamp, sourceUV, 0);
                float4 tangentSpecular = SAMPLE_TEXTURE2D_LOD(_GroomTangentSpecular, sampler_LinearClamp, sourceUV, 0);
                albedo = colorRoughness.rgb * _GroomColorTint.rgb;
                roughness = colorRoughness.a; specular = tangentSpecular.a;
                float3 delta = tangentSpecular.rgb - 0.25;
                float3 side = cross(tangent, view);
                if (dot(side, side) < 1e-6) side = cross(tangent, abs(tangent.y) < 0.9 ? float3(0,1,0) : float3(1,0,0));
                side = normalize(side);
                float3 facing = normalize(cross(side, tangent));
                // Independent approximate fiber TS frame: baseT=(0,0,1),
                // axes(side,facing,curveT). Native TS orientation is not recovered.
                float3 projectedFrameX = input.materialFrameX - tangent * dot(input.materialFrameX,tangent);
                if (_GroomTangentFrame > .5 && dot(projectedFrameX,projectedFrameX) > 1e-10)
                {
                    float3 frameX = normalize(projectedFrameX);
                    float3 frameY = normalize(cross(tangent,frameX));
                    // Source material uses green as the fiber axis. Its authored
                    // RGB delta becomes coefficients(-X,Z,1+Y) in the geometric
                    // (frameX,frameY,curveT) basis. This is coordinate transport,
                    // independently implemented, not native scattering code.
                    tangent = normalize(frameX * -delta.x + frameY * delta.z + tangent * ((1 + delta.y)*_GroomTangentLongitudinalScale));
                }
                else if (_GroomTangentFrame > .5)
                    tangent = normalize(side * -delta.x + facing * delta.z + tangent * ((1 + delta.y)*_GroomTangentLongitudinalScale));
                else tangent = normalize(tangent + side * delta.x + facing * delta.y + tangent * delta.z);
                // Authored BaseColor is complete: no prototype random/root tint.
            }
            else
                albedo = float3(_BaseColor.rgb) * lerp(0.85, 1.15, input.coverageRandom.y)
                    * lerp(1.0 - _RootDarkening, 1.0, saturate(input.uv.y * 3.0));
            float model = _FiberModel;
            bool fourPath = model > 1.5 && model < 2.5;
            bool unitDirect = model > 4.5 && model < 5.5;
            if (fourPath && !StrandHasFiberLookup()) { model = 0.0; fourPath = false; }
            InputData inputData = (InputData)0;
            inputData.positionWS = input.positionWS; inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            // Complete direct models omit the legacy diffuse/SH surrogate.
            float3 color = (fourPath || unitDirect) ? 0.0 : float3(SampleSH(view)) * albedo;
            color += StrandFiberLight(MHGroomSelfShadowMain(GetMainLight(TransformWorldToShadowCoord(input.positionWS)), input.positionWS), tangent, view, albedo, roughness, specular, model);
            #if defined(_ADDITIONAL_LIGHTS)
            uint pixelLightCount = GetAdditionalLightsCount();
            #if USE_CLUSTER_LIGHT_LOOP
            [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
            {
                CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                color += StrandFiberLight(MHGroomSelfShadowAdditional(GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1)), input.positionWS, lightIndex), tangent, view, albedo, roughness, specular, model);
            }
            #endif
            LIGHT_LOOP_BEGIN(pixelLightCount)
                color += StrandFiberLight(MHGroomSelfShadowAdditional(GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1)), input.positionWS, lightIndex), tangent, view, albedo, roughness, specular, model);
            LIGHT_LOOP_END
            #endif
            return MixFog(color, input.fog);
        }
        // Continuous coverage is drawn only by GroomTransparencyFeature. The URP forward and depth-only passes
        // would otherwise rasterize every strand just to clip it, so their vertex stage exits with an off-screen
        // position and the triangles are culled before rasterization.
        Varyings StrandCulledVertex(Attributes input)
        {
            [branch] if (_CoverageMode > .5)
            {
                Varyings culled = (Varyings)0;
                culled.positionCS = float4(2, 2, 2, 1);
                return culled;
            }
            return StrandVertex(input);
        }
        half4 StrandFragment(Varyings input):SV_Target
        {
            clip(0.5 - _CoverageMode);
            half alpha = StrandCoverage(input);
            return half4(StrandRadiance(input), alpha);
        }
        struct Accumulation { float4 radiance:SV_Target0; float opticalDepth:SV_Target1; };
        Accumulation StrandAccumulation(Varyings input)
        {
            UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            clip(_CoverageMode - 0.5);
            float alpha = min(StrandAlpha(input), 0.9999);
            Accumulation output;
            // NearestLayer composites only the selected layer's radiance; this pass then supplies
            // all-strand optical depth only, so skip the full fiber shading for every overlap.
            output.radiance = 0;
            [branch] if (_MHGroomOpticalDepthOnly < .5)
                output.radiance = float4(StrandRadiance(input) * alpha, alpha);
            output.opticalDepth = -log(max(1 - alpha, 0.0001));
            return output;
        }
        half4 StrandDepth(Varyings input):SV_Target { UNITY_SETUP_INSTANCE_ID(input); clip(0.5 - _CoverageMode); return half4(0,0,0,StrandCoverage(input)); }
        float StrandLinearEyeDepth(Varyings input)
        {
            return max(1e-6, -TransformWorldToView(input.positionWS).z);
        }
        float StrandNearestDepth(Varyings input):SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);clip(_CoverageMode-.5);StrandAlpha(input);
            return StrandLinearEyeDepth(input);
        }
        struct SampleVaryings
        {
            float4 positionCS:SV_POSITION;
            sample float3 positionWS:TEXCOORD0;
            sample half3 tangentWS:TEXCOORD1;
            sample float2 uv:TEXCOORD2;
            sample half2 coverageRandom:TEXCOORD3;
            sample half fog:TEXCOORD4;
            sample float3 materialFrameX:TEXCOORD5;
            sample float rectangleAlpha:TEXCOORD6;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings StrandSampleInputs(SampleVaryings input)
        {
            Varyings v=(Varyings)0;
            UNITY_TRANSFER_INSTANCE_ID(input,v);
            v.positionCS=input.positionCS;v.positionWS=input.positionWS;
            v.uv=input.uv;v.coverageRandom=input.coverageRandom;
            v.tangentWS=input.tangentWS;v.fog=input.fog;v.materialFrameX=input.materialFrameX;v.rectangleAlpha=input.rectangleAlpha;
            return v;
        }
        float StrandNearestDepthMS(SampleVaryings input):SV_Target
        {
            return StrandNearestDepth(StrandSampleInputs(input));
        }
        float4 StrandSelectedRadiance(Varyings input,float nearest)
        {
            UNITY_SETUP_INSTANCE_ID(input);clip(_CoverageMode-.5);
            float alpha=min(StrandAlpha(input),.9999);
            float depth=StrandLinearEyeDepth(input);
            // Diagnostic nearest expanded-ribbon layer. This tolerance handles
            // independent pass interpolation; it is not a density/brightness fit.
            float weight = 1;
            if (_GroomRadianceDepthBandPixels <= 0)
                clip(max(1e-5,depth*1e-5)-abs(depth-nearest));
            else
            {
                // Independent weighted OIT approximation. Nearby layers share
                // radiance in proportion to area and distance from the depth
                // anchor. The all-strand optical depth is unchanged. This is
                // not exact ordered visibility: a distant faint front tail
                // can still determine the anchor.
                float worldPixel = 2 * (unity_OrthoParams.w > .5 ? 1 : depth)
                    / (abs(UNITY_MATRIX_P._m11) * _ScaledScreenParams.y);
                weight = exp(-max(depth-nearest,0) / max(worldPixel*_GroomRadianceDepthBandPixels,1e-7));
                clip(weight-1e-6);
            }
            return float4(StrandRadiance(input)*alpha*weight,alpha*weight);
        }
        float4 StrandNearestRadiance(Varyings input):SV_Target
        {
            return StrandSelectedRadiance(input,LOAD_TEXTURE2D(_MHGroomNearestDepth,uint2(input.positionCS.xy)).r);
        }
        float4 StrandNearestRadianceMS(SampleVaryings input,uint sampleIndex:SV_SampleIndex):SV_Target
        {
            return StrandSelectedRadiance(StrandSampleInputs(input),_MHGroomNearestDepthMS.Load(uint2(input.positionCS.xy),sampleIndex));
        }
        // Pixel-rate nearest-layer selection for MSAA targets. Both MS passes run once per pixel and use the
        // pixel-center eye depth: the depth pass writes it to every sample the strand covers (min-blended per
        // sample), and the radiance pass selects the covered samples whose stored minimum equals its own center
        // depth, shading once and writing through SV_Coverage. The per-sample variants above shade every
        // sample (up to 8x the work at MSAA8) and remain available for comparison.
        // NearestLayer single geometry pass: all-strand optical depth (added) and nearest pixel-center eye
        // depth (min-blended per sample) in one draw. Only these two are used in nearest-layer mode, so no
        // lighting is evaluated here; the selected layer is shaded once in the nearest-radiance pass.
        // Hardware nearest-layer selection (GroomTransparencyFeature.NearestSelection.DepthTest). A depth-only
        // prepass stores the nearest strand per sample in a copy of the camera depth; the radiance pass then
        // depth-tests against it, so only the nearest layer reaches the fragment shader and MSAA coverage comes
        // from the per-sample depth test (no nearest-depth texture loads or SV_Coverage output).
        void StrandNearestDepthPrepass(Varyings input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            clip(_CoverageMode - .5);
            StrandAlpha(input);
        }
        float StrandOpticalDepthOnly(Varyings input):SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            clip(_CoverageMode - .5);
            float alpha = min(StrandAlpha(input), .9999);
            return -log(max(1 - alpha, .0001));
        }
        float4 StrandNearestRadianceDepthTested(Varyings input):SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            clip(_CoverageMode - .5);
            float alpha = min(StrandAlpha(input), .9999);
            return float4(StrandRadiance(input) * alpha, alpha);
        }
        struct OpticalNearest { float opticalDepth:SV_Target0; float nearest:SV_Target1; };
        OpticalNearest StrandOpticalDepthNearest(Varyings input)
        {
            UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            clip(_CoverageMode - 0.5);
            float alpha = min(StrandAlpha(input), 0.9999);
            OpticalNearest output;
            output.opticalDepth = -log(max(1 - alpha, 0.0001));
            output.nearest = StrandLinearEyeDepth(input);
            return output;
        }
        float StrandNearestDepthMSPixel(Varyings input):SV_Target
        {
            return StrandNearestDepth(input);
        }
        float4 StrandNearestRadianceMSPixel(Varyings input, uint coverage:SV_Coverage, out uint selected:SV_Coverage):SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            clip(_CoverageMode-.5);
            float alpha = min(StrandAlpha(input), .9999);
            float depth = StrandLinearEyeDepth(input);
            uint2 pixel = uint2(input.positionCS.xy);
            float worldPixel = 2 * (unity_OrthoParams.w > .5 ? 1 : depth) / (abs(UNITY_MATRIX_P._m11) * _ScaledScreenParams.y);
            uint mask = 0, count = 0;
            float weightSum = 0;
            [unroll] for (uint s = 0; s < 8; s++)
            {
                if ((coverage & (1u << s)) == 0) continue;
                float nearest = _MHGroomNearestDepthMS.Load(pixel, s);
                float w;
                if (_GroomRadianceDepthBandPixels <= 0) w = abs(depth - nearest) <= max(1e-5, depth * 1e-5) ? 1 : 0;
                else w = exp(-max(depth - nearest, 0) / max(worldPixel * _GroomRadianceDepthBandPixels, 1e-7));
                if (w > 1e-6) { mask |= 1u << s; weightSum += w; count++; }
            }
            if (count == 0) discard;
            selected = mask;
            // With a depth band, the selected samples share one value weighted by their mean band weight.
            float weight = weightSum / count;
            return float4(StrandRadiance(input) * alpha * weight, alpha * weight);
        }
        struct TemporalVaryings
        {
            Varyings strand;
            float4 currentCS:TEXCOORD7;
            float4 previousCS:TEXCOORD8;
        };
        TemporalVaryings StrandTemporalVertex(Attributes input)
        {
            TemporalVaryings output;
            output.strand = StrandVertex(input);
            // Source curve centers carry rigid motion. Camera-facing width is
            // raster support, so it must not invent motion when the camera turns.
            // No previous deformed control points are invented.
            // Sway is motion too: this frame's and the previous frame's rotation (GroomSway keeps both).
            float3 cur = mul(UNITY_MATRIX_M,input.positionOS).xyz, prev = mul(UNITY_PREV_MATRIX_M,input.positionOS).xyz;
            cur = MHGroomSwayAt(cur,_MHGroomSwayPivot.xyz,MHGroomSwayVector(cur,input.uv.y,input.widthRandom.y,_MHGroomSwayPivot,_MHGroomSwayAxisAngle,_MHGroomSwayFine,_MHGroomSwayFront));
            prev = MHGroomSwayAt(prev,_MHGroomSwayPrevPivot.xyz,MHGroomSwayVector(prev,input.uv.y,input.widthRandom.y,_MHGroomSwayPrevPivot,_MHGroomSwayPrevAxisAngle,_MHGroomSwayPrevFine,_MHGroomSwayFront));
            output.currentCS = mul(_NonJitteredViewProjMatrix,float4(cur,1));
            output.previousCS = mul(_PrevViewProjMatrix,float4(prev,1));
            return output;
        }
        float4 StrandTemporal(TemporalVaryings input, out float depth:SV_Depth):SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input.strand);
            clip(_CoverageMode-.5);
            StrandAlpha(input.strand);
            float nearest = LOAD_TEXTURE2D(_MHGroomNearestDepth,uint2(input.strand.positionCS.xy)).r;
            float eyeDepth = StrandLinearEyeDepth(input.strand);
            clip(max(1e-5,eyeDepth*1e-5)-abs(eyeDepth-nearest));
            float tau = LOAD_TEXTURE2D(_MHGroomTemporalOpticalDepth,uint2(input.strand.positionCS.xy)).r;
            clip(tau-1e-6);
            // Use the actual covered layer. An opaque enclosing proxy would
            // assign hair motion to the background and produce silhouette trails.
            depth = input.strand.positionCS.z;
            return float4(CalcNdcMotionVectorFromCsPositions(input.currentCS,input.previousCS),0,0);
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StrandCulledVertex
            #pragma fragment StrandFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "GroomAccumulation"
            Tags { "LightMode"="MetaHumanGroomAccumulation" }
            ZWrite Off
            ZTest LEqual
            AlphaToMask Off
            Blend 0 One One
            Blend 1 One One
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StrandVertex
            #pragma fragment StrandAccumulation
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "GroomOpticalDepthNearest"
            Tags { "LightMode"="MetaHumanGroomOpticalDepthNearest" }
            ZWrite Off
            ZTest LEqual
            AlphaToMask Off
            Blend 0 One One
            Blend 1 One One
            BlendOp 1 Min
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StrandVertex
            #pragma fragment StrandOpticalDepthNearest
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "GroomNearestDepth"
            Tags { "LightMode"="MetaHumanGroomNearestDepth" }
            ZWrite Off ZTest LEqual AlphaToMask Off
            Blend One One BlendOp Min
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StrandVertex
            #pragma fragment StrandNearestDepth
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "GroomNearestDepthMS"
            Tags { "LightMode"="MetaHumanGroomNearestDepthMS" }
            ZWrite Off ZTest LEqual AlphaToMask Off
            Blend One One BlendOp Min
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StrandVertex
            #pragma fragment StrandNearestDepthMSPixel
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "GroomNearestRadiance"
            Tags { "LightMode"="MetaHumanGroomNearestRadiance" }
            ZWrite Off ZTest LEqual AlphaToMask Off Blend One One
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StrandVertex
            #pragma fragment StrandNearestRadiance
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "GroomNearestRadianceMS"
            Tags { "LightMode"="MetaHumanGroomNearestRadianceMS" }
            ZWrite Off ZTest LEqual AlphaToMask Off Blend One One
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StrandVertex
            #pragma fragment StrandNearestRadianceMSPixel
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "GroomNearestDepthPrepass"
            Tags { "LightMode"="MetaHumanGroomNearestDepthPrepass" }
            ZWrite On ZTest LEqual AlphaToMask Off ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StrandVertex
            #pragma fragment StrandNearestDepthPrepass
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "GroomOpticalDepth"
            Tags { "LightMode"="MetaHumanGroomOpticalDepth" }
            ZWrite Off ZTest LEqual AlphaToMask Off Blend One One
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StrandVertex
            #pragma fragment StrandOpticalDepthOnly
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "GroomNearestRadianceDepthTested"
            Tags { "LightMode"="MetaHumanGroomNearestRadianceDepthTested" }
            // Same vertex program as the prepass; the one-unit pull keeps the stored nearest layer passing.
            ZWrite Off ZTest LEqual Offset 0, -1 AlphaToMask Off Blend One One
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StrandVertex
            #pragma fragment StrandNearestRadianceDepthTested
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "GroomTemporal"
            Tags { "LightMode"="MetaHumanGroomTemporal" }
            ZWrite On ZTest LEqual AlphaToMask Off Blend Off ColorMask RG
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StrandTemporalVertex
            #pragma fragment StrandTemporal
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StrandCulledVertex
            #pragma fragment StrandDepth
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    FallBack Off
}

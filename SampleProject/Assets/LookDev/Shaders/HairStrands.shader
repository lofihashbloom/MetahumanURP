// Independent analytic-area and compensated-rectangle strand profiles.
Shader "MetaHuman/Sample/HairStrands"
{
    Properties
    {
        [Toggle] _HairShadowEnabled("Independent Geometric Self Shadow",Float)=0
        [Toggle] _HairDensityEnabled("Head-Local Hair Density Ray March",Float)=0
        _HairDensity("Hair Extinction Volume (1/m, head-local)",3D)="black"{}
        _HairDensityScale("Hair Density Scale",Float)=0.7
        _HairDensitySize("Volume Size (m) / Voxel (m)",Vector)=(1,1,1,0.01)
        _GlobalScatterGain("Multiple-Scattering Gain (fitted to Unreal stills, 0 = off)",Float)=0
        _GlobalScatterExponent("Multiple-Scattering Colour Exponent",Float)=0.65
        _GlobalScatterExtinction("Multiple-Scattering Extinction Scale",Float)=0.35
        [Enum(FittedGain,0,OpticalDepth,1)] _HairMSModel("Multiple-Scattering Model",Float)=0
        _HairMSGain("Optical-Depth MS Gain (one global calibration)",Float)=0
        _HairMSColorExponent("Optical-Depth MS Per-Fibre Throughput Exponent (albedo^e)",Float)=0.65
        _HairMSLightDepth("Optical-Depth MS Light-Path Attenuation (per fibre)",Float)=1
        _HairMSChordScale("Optical-Depth MS Chord Saturation",Float)=0.5
        _HairMSMeshShadow("Optical-Depth MS Mesh Shadow Weight",Range(0,1))=1
        _HairMSLumExponent("Optical-Depth MS Brightness Exponent (lum(albedo)^m; = colour exponent keeps albedo^e)",Float)=0.65
        [Toggle] _HairMSSkullStop("Optical-Depth MS: Away-From-Light March Stops At The Skull Ellipsoid",Float)=0
        _HairSkullCentre("Skull Ellipsoid Centre (volume coords)",Vector)=(0.5,0.5,0.5,0)
        _HairSkullRadii("Skull Ellipsoid Radii (volume coords), w = valid",Vector)=(0,0,0,0)
        [Toggle] _HairMSOnly("Debug: Output Multiple Scattering Only",Float)=0
        _HairShadowDensity("Geometric Extinction Density (no fit)",Float)=1
        _HairShadowBiasCells("Receiver Bias In Froxels",Float)=1
        _HairShadowResolution("Static Froxel Resolution",Float)=128
        _HairShadow0("Optical Depth 0",3D)="black"{}
        _HairShadowOriginZMin0("Shadow OriginZMin 0",Vector)=(0,0,0,0)
        _HairShadowRightUMin0("Shadow RightUMin 0",Vector)=(0,0,0,0)
        _HairShadowUpVMin0("Shadow UpVMin 0",Vector)=(0,0,0,0)
        _HairShadowForwardZMax0("Shadow ForwardZMax 0",Vector)=(0,0,0,0)
        _HairShadowScales0("Shadow Scales 0",Vector)=(0,0,0,0)
        _HairShadow1("Optical Depth 1",3D)="black"{}
        _HairShadowOriginZMin1("Shadow OriginZMin 1",Vector)=(0,0,0,0)
        _HairShadowRightUMin1("Shadow RightUMin 1",Vector)=(0,0,0,0)
        _HairShadowUpVMin1("Shadow UpVMin 1",Vector)=(0,0,0,0)
        _HairShadowForwardZMax1("Shadow ForwardZMax 1",Vector)=(0,0,0,0)
        _HairShadowScales1("Shadow Scales 1",Vector)=(0,0,0,0)
        _HairShadow2("Optical Depth 2",3D)="black"{}
        _HairShadowOriginZMin2("Shadow OriginZMin 2",Vector)=(0,0,0,0)
        _HairShadowRightUMin2("Shadow RightUMin 2",Vector)=(0,0,0,0)
        _HairShadowUpVMin2("Shadow UpVMin 2",Vector)=(0,0,0,0)
        _HairShadowForwardZMax2("Shadow ForwardZMax 2",Vector)=(0,0,0,0)
        _HairShadowScales2("Shadow Scales 2",Vector)=(0,0,0,0)
        _HairShadow3("Optical Depth 3",3D)="black"{}
        _HairShadowOriginZMin3("Shadow OriginZMin 3",Vector)=(0,0,0,0)
        _HairShadowRightUMin3("Shadow RightUMin 3",Vector)=(0,0,0,0)
        _HairShadowUpVMin3("Shadow UpVMin 3",Vector)=(0,0,0,0)
        _HairShadowForwardZMax3("Shadow ForwardZMax 3",Vector)=(0,0,0,0)
        _HairShadowScales3("Shadow Scales 3",Vector)=(0,0,0,0)

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
        [HideInInspector] _MHGroomDeferredId("Deferred Groom Material Id", Float) = 0
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
        #include "Packages/com.unity.metahuman/Runtime/Shaders/HairFiberLookup.hlsl"
        #include "Packages/com.unity.metahuman/Runtime/Shaders/HairDirectProfile.hlsl"
        #include "Packages/com.unity.metahuman/Runtime/Shaders/HairLocalReferenceProfile.hlsl"
        #include "Packages/com.unity.metahuman/Runtime/Shaders/HairLocalEnvelopeProfile.hlsl"
        #include "Packages/com.unity.metahuman/Runtime/Shaders/GroomSelfShadow.hlsl"
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
        float _HairShadowEnabled,_HairShadowDensity,_HairShadowBiasCells,_HairShadowResolution;
        float _GlobalScatterGain,_GlobalScatterExponent,_GlobalScatterExtinction;
        float _HairMSModel,_HairMSGain,_HairMSColorExponent,_HairMSLightDepth,_HairMSChordScale,_HairMSOnly,_HairMSMeshShadow,_HairMSLumExponent,_HairMSSkullStop; float4 _HairSkullCentre,_HairSkullRadii;
        float _HairDensityEnabled,_HairDensityScale; float4 _HairDensitySize; float4x4 _HairDensityWorldToVolume;
        float4 _HairShadowOriginZMin0;
        float4 _HairShadowRightUMin0;
        float4 _HairShadowUpVMin0;
        float4 _HairShadowForwardZMax0;
        float4 _HairShadowScales0;
        float4 _HairShadowOriginZMin1;
        float4 _HairShadowRightUMin1;
        float4 _HairShadowUpVMin1;
        float4 _HairShadowForwardZMax1;
        float4 _HairShadowScales1;
        float4 _HairShadowOriginZMin2;
        float4 _HairShadowRightUMin2;
        float4 _HairShadowUpVMin2;
        float4 _HairShadowForwardZMax2;
        float4 _HairShadowScales2;
        float4 _HairShadowOriginZMin3;
        float4 _HairShadowRightUMin3;
        float4 _HairShadowUpVMin3;
        float4 _HairShadowForwardZMax3;
        float4 _HairShadowScales3;
        float _MHGroomDeferredId;
        CBUFFER_END
        float _MHGroomOpticalDepthOnly;
        TEXTURE2D(_GroomColorRoughness);
        TEXTURE2D(_GroomTangentSpecular);
        TEXTURE2D(_MHGroomNearestDepth);
        TEXTURE2D(_MHGroomTemporalOpticalDepth);
        Texture2DMS<float> _MHGroomNearestDepthMS;
        TEXTURE2D_ARRAY(_FiberLookup); SAMPLER(sampler_FiberLookup);
        TEXTURE3D(_HairShadow0);
        TEXTURE3D(_HairShadow1);
        TEXTURE3D(_HairShadow2);
        TEXTURE3D(_HairShadow3);

        float3 MHShadowCoordinates(float3 p,float4 origin,float4 right,float4 up,float4 forward,float4 scales)
        {
            float3 r=p-origin.xyz;float z=dot(r,forward.xyz);
            return float3((dot(r,right.xyz)/max(z,1e-6)-right.w)*scales.x,
                (dot(r,up.xyz)/max(z,1e-6)-up.w)*scales.y,
                (z-origin.w)*scales.z-_HairShadowBiasCells/_HairShadowResolution);
        }
        TEXTURE3D(_HairDensity);
        // Head-local extinction volume, marched along a direction (24 steps, starting one voxel out to skip the
        // fibre's own cell); returns the scaled optical depth. Follows the head every frame; valid for any light.
        float MHHairDensityOpticalDepth(float3 p,float3 lightDirection,bool stopAtSkull=false)
        {
            float3 o=mul(_HairDensityWorldToVolume,float4(p,1)).xyz;
            float3 d=mul((float3x3)_HairDensityWorldToVolume,normalize(lightDirection));
            float3 inv=1/((abs(d)>1e-7)?d:float3(1e-7,1e-7,1e-7));
            float3 ta=(0-o)*inv,tb=(1-o)*inv,tmin3=min(ta,tb),tmax3=max(ta,tb);
            float tEnter=max(max(tmin3.x,tmin3.y),max(tmin3.z,0)),tExit=min(min(tmax3.x,tmax3.y),tmax3.z);
            tEnter=max(tEnter,_HairDensitySize.w);
            if(stopAtSkull&&_HairSkullRadii.w>.5)
            {
                // First hit of the ray with the skull ellipsoid (unit-sphere space); the march ends there.
                float3 so=(o-_HairSkullCentre.xyz)/_HairSkullRadii.xyz,sd=d/_HairSkullRadii.xyz;
                float a=dot(sd,sd),b=dot(so,sd),c=dot(so,so)-1,disc=b*b-a*c;
                if(c<0)return 0;
                if(disc>0){float tHit=(-b-sqrt(disc))/a;if(tHit>0)tExit=min(tExit,tHit);}
            }
            if(tExit<=tEnter)return 0;
            const int N=24;float dt=(tExit-tEnter)/N,tau=0;
            [unroll]for(int i=0;i<N;i++)
                tau+=SAMPLE_TEXTURE3D_LOD(_HairDensity,sampler_LinearClamp,o+d*(tEnter+(i+.5)*dt),0).r*dt;
            return tau*max(_HairDensityScale,0);
        }
        float MHHairDensityTransmission(float3 p,float3 lightDirection){return exp(-MHHairDensityOpticalDepth(p,lightDirection));}
        // Package GroomShadowVolume (one fetch) when it holds this light; else this shader's own paths.
        float MHOpticalHairTransmission(float3 p,float3 lightDirection,float lightId)
        {
            float packaged;
            if(MHGroomTrySelfTransmission(p,lightId,packaged))return packaged;
            if(_HairDensityEnabled>.5)return MHHairDensityTransmission(p,lightDirection);
            if(_HairShadowEnabled<.5)return 1;
            if(dot(lightDirection,lightDirection)<1e-8)return 1;
            lightDirection=normalize(lightDirection);
            float4 dots=float4(dot(normalize(_HairShadowOriginZMin0.xyz-p),lightDirection),
                dot(normalize(_HairShadowOriginZMin1.xyz-p),lightDirection),
                dot(normalize(_HairShadowOriginZMin2.xyz-p),lightDirection),
                dot(normalize(_HairShadowOriginZMin3.xyz-p),lightDirection));
            float best=max(max(dots.x,dots.y),max(dots.z,dots.w));if(best<.9999)return 1;
            float3 q;float tau;
            if(dots.x==best){q=MHShadowCoordinates(p,_HairShadowOriginZMin0,_HairShadowRightUMin0,_HairShadowUpVMin0,_HairShadowForwardZMax0,_HairShadowScales0);if(any(q<0)||any(q>1))return 1;tau=SAMPLE_TEXTURE3D_LOD(_HairShadow0,sampler_LinearClamp,q,0).r;}
            else if(dots.y==best){q=MHShadowCoordinates(p,_HairShadowOriginZMin1,_HairShadowRightUMin1,_HairShadowUpVMin1,_HairShadowForwardZMax1,_HairShadowScales1);if(any(q<0)||any(q>1))return 1;tau=SAMPLE_TEXTURE3D_LOD(_HairShadow1,sampler_LinearClamp,q,0).r;}
            else if(dots.z==best){q=MHShadowCoordinates(p,_HairShadowOriginZMin2,_HairShadowRightUMin2,_HairShadowUpVMin2,_HairShadowForwardZMax2,_HairShadowScales2);if(any(q<0)||any(q>1))return 1;tau=SAMPLE_TEXTURE3D_LOD(_HairShadow2,sampler_LinearClamp,q,0).r;}
            else if(dots.w==best){q=MHShadowCoordinates(p,_HairShadowOriginZMin3,_HairShadowRightUMin3,_HairShadowUpVMin3,_HairShadowForwardZMax3,_HairShadowScales3);if(any(q<0)||any(q>1))return 1;tau=SAMPLE_TEXTURE3D_LOD(_HairShadow3,sampler_LinearClamp,q,0).r;}
            else return 1;
            return exp(-max(tau,0)*max(_HairShadowDensity,0));
        }
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
        // _TeaserStrandKeep (global; 0 or 1 = every strand, the default): the web player keeps this fraction of the
        // strands (by the per-strand random) and widens the kept ones by 1/keep, so the hair's coverage is preserved
        // with fewer fragments to rasterise. A web performance budget, not a fidelity setting.
        float _TeaserStrandKeep;
        // Drawn fraction: the smaller of the global web budget and the material's _StrandFraction (set per material by
        // the package's GroomStrandLod from the on-screen size). Either at 0 or 1 means every strand.
        float TeaserStrandKeep()
        {
            float global = _TeaserStrandKeep > 0 && _TeaserStrandKeep < 1 ? _TeaserStrandKeep : 1;
            float material = _StrandFraction > 0 && _StrandFraction < 1 ? _StrandFraction : 1;
            return min(global, material);
        }
        Varyings StrandVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input, output); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float3 centre = TransformObjectToWorld(input.positionOS.xyz);
            float3 tangent = normalize(TransformObjectToWorldDir(input.tangentOS));
            float3 view = GetWorldSpaceNormalizeViewDir(centre);
            float3 side = cross(tangent, view);
            if (dot(side, side) < 1e-6) side = cross(tangent, abs(tangent.y) < 0.9 ? float3(0,1,0) : float3(1,0,0));
            side = normalize(side);
            float depth = max(0.001, -TransformWorldToView(centre).z);
            float pixelWorld = 2 * (unity_OrthoParams.w > 0.5 ? 1 : depth) / (abs(UNITY_MATRIX_P._m11) * _ScaledScreenParams.y);
            float keep = TeaserStrandKeep();
            // Dropped strands exit here, before the expansion and frame work, at one point outside the clip volume.
            [branch] if (keep < 1 && frac(input.widthRandom.y * 7.31 + .17) >= keep)
            {
                output.positionCS = float4(0, 0, -2, 1);
                return output;
            }
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
            output.materialFrameX = TransformObjectToWorldDir(input.materialFrame.xyz, false);
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
        float _TeaserCoverageFrame, _TeaserCoverageLevels;
        // Beauty stage only (globals, 0 elsewhere): environment light on strands, which the four-path direct model omits.
        float _TeaserHairEnvDiffuse, _TeaserHairEnvSpecular;
        half StrandCoverage(Varyings input)
        {
            half alpha = StrandAlpha(input);
            // Decorrelate overlapping fine fibres. Reusing the same alpha-to-mask
            // pattern on every strand leaves whole MSAA samples permanently empty.
            // This spatial stochastic baseline needs temporal filtering in future.
            // _TeaserCoverageFrame (global, 0 unless the web player sets it) varies the pattern per frame for TAA.
            float threshold = frac(InterleavedGradientNoise(input.positionCS.xy, _TeaserCoverageFrame) + input.coverageRandom.y);
            // _TeaserCoverageLevels = MSAA sample count (global, 0 = off): dither at sample granularity, so
            // alpha-to-coverage keeps 0..N samples instead of all-or-none per pixel (N times less grain).
            if (_TeaserCoverageLevels > .5)
            {
                float covered = floor(alpha * _TeaserCoverageLevels + threshold);
                clip(covered - .5);
                return covered / _TeaserCoverageLevels;
            }
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
        float3 StrandFiberLight(Light light, float3 positionWS, float3 tangent, float3 view, float3 albedo,
                                float roughness, float specular, float model, float lightId)
        {
            // v34 teaser approximation of multiple scattering through the hair volume (Unreal's global/local hair
            // scattering, not modelled by the package): authored colour ^ exponent, fiber-diffuse angular term,
            // mesh shadow maps, softened hair transmission. One gain fitted on Unreal reference stills; not Unreal's model.
            float hairT=MHOpticalHairTransmission(positionWS,float3(light.direction),lightId);
            float3 multiple=0;
            // Optical-depth model (animation-safe: only the head-local volume): per-fibre throughput af = albedo^e;
            // light reaching the fibre through nL fibres keeps af^(k*nL) (coloured, dual-scattering-like);
            // the amount scattered back saturates with the hair chord through the point (toward + away from the
            // light). One global gain for every character and light. Teaser model, not Unreal's.
            if(_HairMSModel>.5)
            {
                [branch]if(_HairDensityEnabled>.5&&_HairMSGain>0)
                {
                    float nL=-log(max(hairT,1e-6));
                    float nB=MHHairDensityOpticalDepth(positionWS,-float3(light.direction),_HairMSSkullStop>.5);
                    float3 af=pow(saturate(albedo)+1e-5,_HairMSColorExponent);
                    float lum=dot(saturate(albedo),float3(.2126,.7152,.0722))+1e-5;
                    multiple=_HairMSGain*af*pow(lum,_HairMSLumExponent-_HairMSColorExponent)*exp(_HairMSLightDepth*nL*log(af))
                        *(1-exp(-_HairMSChordScale*(nL+nB)))
                        *sqrt(saturate(1.0-pow(dot(tangent,float3(light.direction)),2)))
                        *float3(light.color)*light.distanceAttenuation*lerp(1,light.shadowAttenuation,_HairMSMeshShadow);
                }
            }
            else if(_GlobalScatterGain>0)
                multiple=_GlobalScatterGain*pow(max(albedo,0),_GlobalScatterExponent)
                    *sqrt(saturate(1.0-pow(dot(tangent,float3(light.direction)),2)))
                    *pow(max(hairT,1e-6),_GlobalScatterExtinction)
                    *float3(light.color)*light.distanceAttenuation*light.shadowAttenuation;
            light.shadowAttenuation*=hairT;
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
            if(_HairMSOnly>.5)return multiple;
            return radiance * float3(light.color) * light.distanceAttenuation * light.shadowAttenuation + multiple;
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
            [branch] if (_TeaserHairEnvDiffuse + _TeaserHairEnvSpecular > 0)
            {
                // Look-development approximation, not a fibre integral: SH irradiance on the view-facing fibre normal
                // times albedo, plus the probe's glossy reflection about that normal at the strand roughness (F0 0.046).
                float3 fibreN = view - tangent * dot(view, tangent);
                fibreN = dot(fibreN, fibreN) > 1e-6 ? normalize(fibreN) : view;
                color += float3(SampleSH(fibreN)) * albedo * _TeaserHairEnvDiffuse;
                color += float3(GlossyEnvironmentReflection(reflect(-view, fibreN), input.positionWS, saturate(roughness), 1, inputData.normalizedScreenSpaceUV))
                    * (0.046 * _TeaserHairEnvSpecular);
            }
            color += StrandFiberLight(GetMainLight(TransformWorldToShadowCoord(input.positionWS)), input.positionWS, tangent, view, albedo, roughness, specular, model, _MHGroomMainLightId);
            #if defined(_ADDITIONAL_LIGHTS)
            uint pixelLightCount = GetAdditionalLightsCount();
            #if USE_CLUSTER_LIGHT_LOOP
            [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
            {
                CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                color += StrandFiberLight(GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1)), input.positionWS, tangent, view, albedo, roughness, specular, model, MHGroomAdditionalLightId(lightIndex));
            }
            #endif
            LIGHT_LOOP_BEGIN(pixelLightCount)
                color += StrandFiberLight(GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1)), input.positionWS, tangent, view, albedo, roughness, specular, model, MHGroomAdditionalLightId(lightIndex));
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
            output.currentCS = mul(_NonJitteredViewProjMatrix,mul(UNITY_MATRIX_M,input.positionOS));
            output.previousCS = mul(_PrevViewProjMatrix,mul(UNITY_PREV_MATRIX_M,input.positionOS));
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
        #include "Packages/com.unity.metahuman/Runtime/Shaders/GroomDeferredPasses.hlsl"
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
            #pragma vertex StrandForwardPathVertex
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
            #pragma vertex StrandForwardPathVertex
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
            #pragma vertex StrandForwardPathVertex
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
            #pragma vertex StrandForwardPathVertex
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
            #pragma vertex StrandForwardPathVertex
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
            #pragma vertex StrandForwardPathVertex
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
            #pragma vertex StrandForwardPathVertex
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
            #pragma vertex StrandForwardPathVertex
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
            Name "GroomDeferredAttributes"
            Tags { "LightMode"="MetaHumanGroomDeferredAttributes" }
            ZWrite On ZTest LEqual AlphaToMask Off Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StrandVertex
            #pragma fragment StrandDeferredAttributes
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "GroomDeferredShade"
            Tags { "LightMode"="MetaHumanGroomDeferredShade" }
            ZWrite Off ZTest Always AlphaToMask Off Blend One OneMinusSrcAlpha ColorMask RGB
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StrandDeferredVertex
            #pragma fragment StrandDeferredShade
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
            Name "GroomComputeShade"
            Tags { "LightMode"="MetaHumanGroomComputeShade" }
            ZWrite Off ZTest Always AlphaToMask Off Blend One OneMinusSrcAlpha ColorMask RGB
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StrandComputeShadeVertex
            #pragma fragment StrandComputeShade
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
        // URP shadow-map casting for lights the baked optical-depth volumes were not built for (LookDev rigs).
        // Opt-in: off unless the global _TeaserStrandShadows > 0, so every existing teaser render is unchanged.
        // Ribbons face the light and are widened to _TeaserStrandShadowWidth (metres, ~a shadow texel; default
        // 2 mm) with opacity kept by stochastic clipping (diameter / width), so PCF filtering of the binary depth
        // map approximates the hair's fractional occlusion. Nearest-occluder shadow, not a deep opacity map.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 AlphaToMask Off Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StrandShadowVertex
            #pragma fragment StrandShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection, _LightPosition;
            // _TeaserStrandShadowKeep (global, 0 or 1 = all): fraction of the drawn strands that cast, widened by
            // 1/fraction so the shadow's opacity holds. Strand shadows are soft (PCF over ~mm texels), so a fraction
            // reads the same at a fraction of the shadow-pass cost.
            float _TeaserStrandShadows, _TeaserStrandShadowWidth, _TeaserStrandShadowOpacity, _TeaserStrandShadowKeep;
            struct ShadowVaryings { float4 positionCS:SV_POSITION; float2 alphaRandom:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            ShadowVaryings StrandShadowVertex(Attributes input)
            {
                ShadowVaryings o = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float keep = min(TeaserStrandKeep(), _TeaserStrandShadowKeep > 0 && _TeaserStrandShadowKeep < 1 ? _TeaserStrandShadowKeep : 1);
                if (_TeaserStrandShadows <= 0 || (keep < 1 && frac(input.widthRandom.y * 7.31 + .17) >= keep))
                {
                    o.positionCS = float4(2, 2, 2, 1);   // culled before rasterisation
                    return o;
                }
                float3 centre = TransformObjectToWorld(input.positionOS.xyz);
                float3 tangent = normalize(TransformObjectToWorldDir(input.tangentOS));
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 toLight = normalize(_LightPosition - centre);
                #else
                float3 toLight = _LightDirection;
                #endif
                float3 side = cross(tangent, toLight);
                if (dot(side, side) < 1e-6) side = cross(tangent, abs(tangent.y) < 0.9 ? float3(0,1,0) : float3(1,0,0));
                side = normalize(side);
                float diameter = input.widthRandom.x / keep;
                float width = max(diameter, _TeaserStrandShadowWidth > 0 ? _TeaserStrandShadowWidth : .002);
                float3 p = ApplyShadowBias(centre + side * (input.uv.x * width * .5), toLight, toLight);
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(p));   // pancaking: directional lights only
                float opacity = _TeaserStrandShadowOpacity > 0 ? _TeaserStrandShadowOpacity : 1;
                o.alphaRandom = float2(saturate(diameter / width * opacity * _BaseColor.a), input.widthRandom.y);
                return o;
            }
            half4 StrandShadowFragment(ShadowVaryings input):SV_Target
            {
                clip(input.alphaRandom.x - frac(InterleavedGradientNoise(input.positionCS.xy, 0) + input.alphaRandom.y));
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}

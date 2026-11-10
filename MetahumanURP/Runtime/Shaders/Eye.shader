Shader "MetaHuman/URP/Eye"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Color", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        [Normal] _BumpMap("Normal", 2D) = "bump" {}
        _NormalScale("Normal Strength", Range(0,2)) = 1
        [Toggle] _UseSRMF("Use SRMF (Specular, Roughness, Metallic, Fuzz)", Float) = 0
        _SRMFMap("SRMF (Linear)", 2D) = "white" {}
        [Toggle] _UseSpecularMap("Use Legacy Specular Map", Float) = 0
        _SpecularMap("Specular (Linear)", 2D) = "gray" {}
        [Toggle] _UseRoughnessMap("Use Separate Roughness Map", Float) = 0
        _RoughnessMap("Roughness (Linear)", 2D) = "gray" {}
        _Roughness("Roughness", Range(0.025,1)) = 0.45
        _Specular("UE Specular (F0 = 0.08 x value)", Range(0,1)) = 0.5
        _ScatterMap("Scatter Amount (Linear)", 2D) = "white" {}
        _ScatterColor("Transmission Color", Color) = (1,0.32,0.2,1)
        _Scatter("Local Transmission Approximation", Range(0,1)) = 0.25
        _Fuzz("Fuzz", Range(0,1)) = 0.25
        _SecondaryRoughness("Secondary Lobe Roughness", Range(0.025,1)) = 0.65
        _SecondaryWeight("Secondary Lobe Weight", Range(0,1)) = 0.3
        [Toggle] _WrinklesEnabled("Use Full Normal Wrinkle Maps (Not Delta)", Float) = 0
        [Normal] _WrinkleNormal1("Full Wrinkle Normal 1", 2D) = "bump" {}
        [Normal] _WrinkleNormal2("Full Wrinkle Normal 2", 2D) = "bump" {}
        [Normal] _WrinkleNormal3("Full Wrinkle Normal 3", 2D) = "bump" {}
        _WrinkleWeights("Wrinkle Weights", Vector) = (0,0,0,0)
        [Toggle] _UseOpacityMap("Use Separate Coverage", Float) = 0
        _OpacityMap("Coverage (Linear)", 2D) = "white" {}
        _Cutoff("Coverage Cutoff", Range(0,1)) = 0.33
        _ClearCoat("Corneal Surface Weight", Range(0,1)) = 1
        _PunctualSpecular("Punctual Light Specular (Pin-point Catchlights)", Range(0,1)) = 1
        _CatchlightIntensity("Analytic Catchlight Radiance (0 = Off)", Float) = 0
        _CatchlightColor("Analytic Catchlight Color", Color) = (1,1,1,1)
        _CatchlightDirection("Analytic Catchlight World Direction (Toward Light)", Vector) = (0,0,-1,0)
        _CatchlightShape("Analytic Catchlight Half-size / Softness (Degrees)", Vector) = (8,4,0,0)
        [Toggle] _DesktopEyeLighting("Desktop Microfacet Lighting", Float) = 0
        [Toggle] _UseAuthoredEyeSpecular("Use Authored Eye Specularity (Iris Normal Off)", Float) = 0
        [Toggle] _DeferredEyeInputs("Legacy Deferred Input Precision", Float) = 0
        [Enum(Simplified,0,Refractive,1)] _EyeFamily("Source Eye Family", Float) = 0
        _IrisMasksMap("Authored Iris Structural / AO / Radial / Aperture", 2D) = "white" {}
        _IrisHeightMap("Authored Iris Height", 2D) = "black" {}
        [Normal] _EyeAxisMap("Eye Axis Normal Field", 2D) = "bump" {}
        [Toggle] _UseEyeAxisMap("Use Eye Axis Normal Field", Float) = 0
        _ScaleIrisWithinMask("Iris Texture Scale Within Rim", Range(0.1,2)) = .85
        _IrisColorBalanceSmoothness("Iris Color Transition", Range(0.001,1)) = .5
        _RadialStructuralBlend("Radial / Structural Color", Range(0,1)) = 1
        _IrisSaturation("Iris Saturation", Range(0,2)) = 1
        _IrisAO("Iris Fiber Occlusion", Range(0,1)) = .7
        [Toggle] _NormalUVFlipY("Native Normal UV V Flip", Float) = 0
        _NormalGreenSign("Native Normal Y Convention", Float) = 1
        _IrisColor("Iris Tint", Color) = (1,1,1,1)
        _ScleraColor("Sclera Tint", Color) = (1,1,1,1)
        _IrisRadius("Iris UV Diameter", Range(0.1,1)) = 0.32
        _ScleraMap("Sclera Base Color / Veins", 2D) = "white" {}
        [Toggle] _UseScleraMap("Use Sclera Texture", Float) = 0
        _IrisColorMap("Iris Color Lookup (sRGB)", 2D) = "white" {}
        [Toggle] _UseIrisColorMap("Use Iris Color Lookup", Float) = 0
        _ScleraTintMap("Sclera Tint Lookup (sRGB)", 2D) = "white" {}
        [Toggle] _UseScleraTintMap("Use Sclera Tint Lookup", Float) = 0
        _IrisPicker1("Primary Iris Lookup UV", Vector) = (0.81,0.16,0,0)
        _IrisPicker2("Secondary Iris Lookup UV", Vector) = (0.67,0.14,0,0)
        _IrisColorBalance("Iris Color Balance", Range(0,1)) = 0.5
        _ScleraPicker("Sclera Lookup UV", Vector) = (0.69,0.63,0,0)
        _CorneaIOR("Corneal Refraction Index", Range(1.01,1.6)) = 1.336
        _IrisDepth("Recessed Iris Depth (UV Approximation)", Range(0,0.06)) = 0.015
        _PupilScale("Pupil Scale", Range(0.35,2.3)) = 1.12
        _PupilRadius("Neutral Pupil / Iris Radius", Range(0.08,0.7)) = 0.23
        _IrisResponse("Iris Response Calibration", Range(0,2)) = 1
        _LimbusWidth("Limbus Width", Range(0.01,0.4)) = 0.12
        _LimbusDarkening("Limbus Darkening", Range(0,1)) = 0.5
        _IrisContrast("Iris Contrast", Range(0.1,3)) = 1.5
        _IrisTextureScale("Iris Texture Scale", Range(0.5,2)) = 1.44
        _IrisBrightness("Iris Brightness", Range(0,4)) = 1
        _IrisBrightnessBoost("Source Iris Brightness Boost (Approximation)", Range(0,12)) = 8
        _ScleraBrightness("Sclera Brightness", Range(0,1)) = 0.47
        _ScleraPower("Modern Sclera Color Power", Float) = 1
        _VeinsMap("Modern Veins Color", 2D) = "white" {}
        [Toggle] _UseVeinsMap("Use Modern Veins Color", Float) = 0
        _VeinsPower("Veins Color Power", Float) = 0.5
        _IrisPaletteVariation("Hue / Value / Saturation Variation", Vector) = (0.1,0.25,0.25,0)
        _CloudyIrisColor("Pupil Color Addition (Linear RGB)", Vector) = (0,0,0,0)
        _EyeCornerColor("Peripheral Eye Tint (Linear RGB)", Vector) = (1,1,1,1)
        _EyeColorFields("Corner Radius / Hardness / Cloud Radius / Hardness", Vector) = (0.6,0.4,0.13,0)
        _Anisotropy("Hair Highlight Sharpness", Range(0,1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 400
        Cull Back
        HLSLINCLUDE
        #define MH_EYE 1
        #define _SPECULAR_SETUP 1
        #include "MetaHumanSurface.hlsl"
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            AlphaToMask Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MHVertex
            #pragma fragment MHFragment
            #pragma multi_compile_fragment _ MH_REFERENCE_DEBUG
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MHShadowVertex
            #pragma fragment MHDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MHVertex
            #pragma fragment MHDepthFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MHVertex
            #pragma fragment MHDepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    FallBack Off
}

Shader "MetaHuman/URP/Skin"
{
    Properties
    {
        [Toggle] _DesktopSkinLighting("Desktop Skin Lighting", Float) = 0
        _RoughDiffuseWeight("Rough Diffuse Response", Range(0,1)) = 1
        _DesktopSpecularWeight("Desktop Specular Response", Range(0,1)) = 1
        [Toggle] _NormalUVFlipY("Native Normal UV V Flip", Float) = 0
        _NormalGreenSign("Native Normal Y Convention", Float) = 1
        [Toggle] _DetailUVFlipY("Native Detail UV V Flip", Float) = 0
        _DetailGreenSign("Native Detail Y Convention", Float) = 1
        [MainTexture] _BaseMap("Base Color", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _MakeupMask("Foundation / Concealer / Blusher Mask (Linear)", 2D) = "black" {}
        _MakeupWeights("Authored Makeup Weights", Vector) = (0,0,0,0)
        _FoundationColor("Foundation (Linear RGB)", Vector) = (0,0,0,0)
        _ConcealerColor("Concealer (Linear RGB)", Vector) = (0,0,0,0)
        _BlusherColor("Blusher (Linear RGB)", Vector) = (0,0,0,0)
        _MakeupRoughness("Foundation / Blusher Roughness Factors", Vector) = (1,1,0,0)
        _MouthOcclusionMap("Mouth Occlusion (Linear, Teeth UV)", 2D) = "white" {}
        _MouthOcclusionStrength("Mouth Occlusion of Ambient and Reflections", Range(0,1)) = 0
        _DimReflections("Dim Reflectance by Mouth Occlusion", Range(0,1)) = 0
        _Translucency("Teeth Translucency (Wrapped Diffuse, 0 = Off)", Range(0,1)) = 0
        _TranslucencyShadow("Translucency Passes Shadow", Range(0,1)) = 0
        _TranslucencyMap("Translucency Mask (Linear, R)", 2D) = "white" {}
        [Toggle] _ModernHeadInputs("Verified Modern Head Input Layout", Float) = 0
        _HeadRoughnessControls("Chest Roughness Range / Global Factor / Fresnel", Vector) = (0.34,0.35,1,0.97)
        _HeadFresnelExponent("Head Roughness Fresnel Exponent", Float) = 4.2
        _HeadCavityPowers("Chest / Head Cavity Powers", Vector) = (1,1.93,0,0)
        _HeadRegionMask("Source UV Radius / Hardness", Vector) = (0.985,0.9,0,0)
        _SourceNeckHide("Neck Offset Metres / Source UV Ramp", Vector) = (0,0.8,0,0)
        [Normal] _BumpMap("Normal", 2D) = "bump" {}
        [Normal] _DetailNormalMap("Skin Micro Normal", 2D) = "bump" {}
        _DetailTiling("Micro Normal Tiling", Float) = 30
        _DetailNormalStrength("Micro Normal Strength", Range(0,1)) = 0
        _NormalScale("Normal Strength", Range(0,2)) = 1
        [Toggle] _UseSRMF("Use SRMF (Specular, Roughness, Metallic, Fuzz)", Float) = 0
        _SRMFMap("SRMF (Linear)", 2D) = "white" {}
        [Toggle] _UseSpecularMap("Use Legacy Specular Map", Float) = 0
        _SpecularMap("Specular (Linear)", 2D) = "gray" {}
        [Toggle] _UseRoughnessMap("Use Separate Roughness Map", Float) = 0
        _RoughnessMap("Roughness (Linear)", 2D) = "gray" {}
        [Toggle] _RoughnessUVFlipY("Native Roughness UV V Flip", Float) = 0
        [Toggle] _SpecularUVFlipY("Native Specular UV V Flip", Float) = 0
        _Roughness("Roughness", Range(0.025,1)) = 0.45
        _Specular("UE Specular (F0 = 0.08 x value)", Range(0,1)) = 0.5
        _SpecularMultiplier("Source Specular Multiplier", Range(0,2)) = 1
        _SpecularCavityPower("Specular Mask Cavity Response", Range(0.1,8)) = 1
        [Toggle] _LegacySourceFresnel("Legacy Source Fresnel Input (Experimental)", Float) = 0
        _LegacySpecularMultiplier("Legacy Authored SpecMult", Float) = 1
        _LegacyFresnelBase("Legacy Authored Fresnel Base", Float) = 1.5
        _LegacyFresnelExponent("Legacy Authored Fresnel Exponent", Float) = 5
        _LegacyFresnelFlatness("Legacy Coarse Normal Flatness", Float) = 0
        _LegacyFresnelNormalYSign("Legacy Source Tangent Normal Y Convention", Float) = 1
        _RoughnessMin("Source Minimum Roughness", Range(0,1)) = 0
        _RoughnessMax("Source Maximum Roughness", Range(0,1)) = 1
        _ScatterMap("Scatter Amount (Linear)", 2D) = "white" {}
        _ScatterColor("Transmission Color", Color) = (1,0.32,0.2,1)
        _Scatter("Local Transmission Approximation", Range(0,1)) = 0.25
        _DiffusionAmount("Screen-space Diffusion Amount", Range(0,1)) = 0
        _DiffusionNeutralRadius("Diffusion Neutral Radius (mm, e.g. Teeth Enamel; 0 = Profile)", Float) = 0
        _TeethLayers("Teeth Enamel/Dentin Layers (0 = Off)", Range(0,1)) = 0
        _TeethLayerMap("Teeth Layer Map (Linear: R Crown, G Dentin, B Enamel Thickness)", 2D) = "black" {}
        _EnamelColor("Enamel Scatter Albedo (Linear RGB)", Vector) = (0.8,0.82,0.84,0)
        _DentinTint("Dentin Tint on Base Map (Linear RGB)", Vector) = (1,0.95,0.82,0)
        _CavityColor("Behind Thin Enamel (Linear RGB)", Vector) = (0.06,0.035,0.03,0)
        _TeethGumColor("Teeth Colour Gain at the Gum End (Linear RGB; 1 = Off)", Vector) = (1,1,1,0)
        _EnamelDensity("Enamel Opacity per Unit Thickness", Float) = 0.9
        _TeethShadowFloor("Teeth Shadow Floor (Mouth Occlusion on Layered Crowns; 1 = Off)", Range(0,1)) = 1
        _TeethShadowStart("Teeth Shadow Start (Mouth Occlusion, Full Shadow Below)", Range(0,1)) = 0
        _TeethContactAO("Teeth Corner Shadow (Screen-Space, 0 = Off)", Range(0,2)) = 0
        _TeethContactRadius("Teeth Corner Shadow Reach Sideways (m)", Float) = 0.012
        _TeethContactDepth("Teeth Corner Shadow Max Occluder Distance in Front (m)", Float) = 0.03
        _TeethContactBias("Teeth Corner Shadow Depth Bias (m)", Float) = 0.001
        _TeethContactColor("Teeth Corner Shadow Albedo Gain at Full Shadow (Linear RGB)", Vector) = (0.4,0.27,0.2,0)
        _TeethShadowEnd("Teeth Shadow End (Mouth Occlusion, Full Light Above)", Range(0,1)) = 1
        _TeethGumShade("Teeth Gum-to-Tip Fade (+ Darker at Gums, - Darker at Edge)", Range(-1,1)) = 0
        _TeethEdgeShade("Teeth Enamel Edge Darkening", Range(0,1)) = 0
        _Fuzz("Fuzz", Range(0,1)) = 0.25
        [Toggle] _UsePackedFuzz("SRMF Alpha Contains Fuzz (Disable For Legacy SRM)", Float) = 1
        [Toggle] _ProfileLobes("Use Relative Profile Roughness Lobes", Float) = 0
        _PrimaryRoughnessScale("Profile Primary Roughness Scale", Range(0.1,2)) = 1
        _SecondaryRoughnessScale("Profile Secondary Roughness Scale", Range(0.1,2)) = 1
        _SecondaryRoughness("Secondary Lobe Roughness", Range(0.025,1)) = 0.65
        _SecondaryWeight("Secondary Lobe Weight", Range(0,1)) = 0.3
        [Toggle] _WrinklesEnabled("Use Full Normal Wrinkle Maps (Not Delta)", Float) = 0
        [Normal] _WrinkleNormal1("Full Wrinkle Normal 1", 2D) = "bump" {}
        [Normal] _WrinkleNormal2("Full Wrinkle Normal 2", 2D) = "bump" {}
        [Normal] _WrinkleNormal3("Full Wrinkle Normal 3", 2D) = "bump" {}
        _WrinkleWeights("Wrinkle Weights", Vector) = (0,0,0,0)
        _WrinkleColor1("Wrinkle Color 1 (Animated Maps)", 2D) = "white" {}
        _WrinkleColor2("Wrinkle Color 2 (Animated Maps)", 2D) = "white" {}
        _WrinkleColor3("Wrinkle Color 3 (Animated Maps)", 2D) = "white" {}
        _WrinkleMaskArray("Wrinkle Region Masks (Linear, 4 Regions per Slice)", 2DArray) = "" {}
        [Toggle] _UseOpacityMap("Use Separate Coverage", Float) = 0
        _OpacityMap("Coverage (Linear)", 2D) = "white" {}
        _Cutoff("Coverage Cutoff", Range(0,1)) = 0.33
        _ClearCoat("Corneal Coat", Range(0,1)) = 1
        _ClearCoatSmoothness("Corneal Smoothness", Range(0,1)) = 0.98
        _IrisColor("Iris Tint", Color) = (0.28,0.14,0.06,1)
        _ScleraColor("Sclera Color", Color) = (0.72,0.68,0.63,1)
        _IrisRadius("Iris UV Radius", Range(0.1,1)) = 0.2
        _Anisotropy("Hair Highlight Sharpness", Range(0,1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 400
        Cull Back
        HLSLINCLUDE
        #define MH_SKIN 1
        #define _SPECULAR_SETUP 1
        #include "MetaHumanSurface.hlsl"
        ENDHLSL
        Pass
        {
            Name "SkinDiffuse"
            Tags { "LightMode"="MetaHumanSkinDiffuse" }
            ZWrite Off
            ZTest Equal
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MHVertex
            #pragma fragment MHDiffuseFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_local_fragment _ MH_HAIR_RECEIVER_SHADOW
            #pragma multi_compile_local_fragment _ MH_ANIMATED_MAPS
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            // Single-draw skin diffusion: lit colour plus the diffusion inputs (MRT), drawn by SkinDiffusionFeature
            // in place of ForwardLit, which then culls itself (_MHSkinSingleDraw).
            Name "SkinSplit"
            Tags { "LightMode"="MetaHumanSkinSplit" }
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MHVertex
            #pragma fragment MHSplitFragment
            #pragma multi_compile_fragment _ MH_REFERENCE_DEBUG
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_local_fragment _ MH_HAIR_RECEIVER_SHADOW
            #pragma multi_compile_local_fragment _ MH_ANIMATED_MAPS
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            AlphaToMask Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MHSkinForwardVertex
            #pragma fragment MHFragment
            #pragma multi_compile_fragment _ MH_REFERENCE_DEBUG
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_local_fragment _ MH_HAIR_RECEIVER_SHADOW
            #pragma multi_compile_local_fragment _ MH_ANIMATED_MAPS
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

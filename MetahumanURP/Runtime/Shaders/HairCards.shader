Shader "MetaHuman/URP/HairCards"
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
        _OpacityStrength("Source Coverage Strength", Range(0,1)) = 1
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
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        LOD 400
        Cull Off
        AlphaToMask On
        HLSLINCLUDE
        #define MH_HAIR 1
        #define _SPECULAR_SETUP 1
        #include "MetaHumanSurface.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"
        struct MHCardMotionVaryings
        {
            MHVaryings surface;
            float4 currentCS:TEXCOORD5;
            float4 previousCS:TEXCOORD6;
        };
        MHCardMotionVaryings MHCardMotionVertex(MHAttributes input)
        {
            MHCardMotionVaryings output;
            output.surface = MHVertex(input);
            output.currentCS = mul(_NonJitteredViewProjMatrix,mul(UNITY_MATRIX_M,input.positionOS));
            output.previousCS = mul(_PrevViewProjMatrix,mul(UNITY_PREV_MATRIX_M,input.positionOS));
            return output;
        }
        float4 MHCardMotionFragment(MHCardMotionVaryings input):SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input.surface);
            // Identical texture/cutoff coverage to the color/depth card surface.
            MHAlpha(input.surface.uv);
            return float4(CalcNdcMotionVectorFromCsPositions(input.currentCS,input.previousCS),0,0);
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            AlphaToMask On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MHVertex
            #pragma fragment MHFragment
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
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ZWrite On ZTest LEqual AlphaToMask Off ColorMask RG
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MHCardMotionVertex
            #pragma fragment MHCardMotionFragment
            #pragma multi_compile_instancing
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

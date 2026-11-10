Shader "Hidden/MetaHuman/GroomTransparency"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        float _MHGroomDebugCoverage;
        float4 GroomPremultiplied(float4 accumulated, float tau)
        {
            if (accumulated.a <= 0 || tau <= 0) return 0;
            float opacity = 1 - exp(-min(tau, 80));
            if (_MHGroomDebugCoverage > .5) return float4(opacity, opacity, opacity, opacity);
            return float4(accumulated.rgb * (opacity / max(accumulated.a, 1e-6)), opacity);
        }
        ENDHLSL
        Pass
        {
            Name "SingleSampleComposite"
            ZTest Always ZWrite Off Cull Off
            Blend One OneMinusSrcAlpha
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Composite
            TEXTURE2D_X(_MHGroomAccumulation);
            TEXTURE2D_X(_MHGroomOpticalDepth);
            float4 Composite(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                int2 pixel = int2(input.positionCS.xy);
                return GroomPremultiplied(LOAD_TEXTURE2D_X(_MHGroomAccumulation, pixel), LOAD_TEXTURE2D_X(_MHGroomOpticalDepth, pixel).r);
            }
            ENDHLSL
        }
        Pass
        {
            Name "PerSampleComposite"
            ZTest Always ZWrite Off Cull Off
            Blend One OneMinusSrcAlpha
            ColorMask RGB
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment CompositeSamples
            Texture2DMS<float4> _MHGroomAccumulation;
            Texture2DMS<float> _MHGroomOpticalDepth;
            float4 CompositeSamples(Varyings input, uint sampleIndex : SV_SampleIndex):SV_Target
            {
                int2 pixel = int2(input.positionCS.xy);
                return GroomPremultiplied(_MHGroomAccumulation.Load(pixel, sampleIndex), _MHGroomOpticalDepth.Load(pixel, sampleIndex));
            }
            ENDHLSL
        }
    }
    FallBack Off
}

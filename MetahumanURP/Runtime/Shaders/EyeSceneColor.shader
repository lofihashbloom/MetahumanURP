Shader "Hidden/MetaHuman/EyeSceneColor"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Encode
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float4 _MHEyeSceneEncoding;
            float4 Encode(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).rgb;
                if (_MHEyeSceneEncoding.x > 0)
                    color = pow(saturate(color / _MHEyeSceneEncoding.x), rcp(_MHEyeSceneEncoding.y));
                return float4(color, 1);
            }
            ENDHLSL
        }
    }
}

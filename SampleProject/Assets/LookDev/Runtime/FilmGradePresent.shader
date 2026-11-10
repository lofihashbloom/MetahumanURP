// Presents the 8-bit display-encoded output of FilmGrade in a linear camera target (decoded, so the display's
// sRGB encoding returns the graded values).
Shader "Hidden/MetaHuman/Sample/FilmGradePresent"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float _TeaserGradeEncoded;
            float4 Frag(Varyings input) : SV_Target
            {
                float3 v = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).rgb;
                if (_TeaserGradeEncoded < .5) v = v <= .04045 ? v / 12.92 : pow((v + .055) / 1.055, 2.4);
                return float4(v, 1);
            }
            ENDHLSL
        }
    }
}

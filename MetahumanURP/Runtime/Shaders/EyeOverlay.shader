Shader "MetaHuman/URP/EyeOverlay"
{
    Properties
    {
        [Enum(Occlusion,0,Wetline,1)] _Mode("Overlay Type", Float) = 0
        _Strength("Strength", Range(0,1)) = 0.35
        _OcclusionBaseline("Interior Occlusion", Range(0,1)) = 0.05
        _OcclusionPower("Edge Falloff", Range(1,12)) = 4
        [Toggle] _UseSceneFilter("Use Scene Color Optical Filter", Float) = 0
        _BlurWeightMap("Baked Optical Blur Weight (Linear)", 2D) = "black" {}
        _ShadowTransmissionMap("Baked Optical Transmission (Linear)", 2D) = "white" {}
        _BlurRadiusMillimetres("Optical Filter Radius (mm)", Range(0,3)) = 0.272
        _BlurTint("Optical Filter Tint", Color) = (1,1,1,1)
        _IOR("Wet Film Refraction Index", Range(1.01,1.6)) = 1.336
        _FilmMetallic("Source Film Metallic", Range(0,1)) = 0
        _FilmSpecular("Source Film Specular", Range(0,1)) = 0.5
        _Roughness("Wet Film Roughness", Range(0.025,1)) = 0.12
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+5" }
        Cull Back ZWrite Off
        // Premultiplied reflection over the eye. Occlusion outputs zero RGB and
        // can only attenuate destination light, never create a plastic highlight.
        Blend One OneMinusSrcAlpha
        Pass
        {
            Name "EyeOverlay"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #define _SPECULAR_SETUP 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D_X(_MHEyeSceneColor);
            float4 _MHEyeSceneEncoding;
            float _MHEyeSceneReady;
            TEXTURE2D(_BlurWeightMap); SAMPLER(sampler_BlurWeightMap);
            TEXTURE2D(_ShadowTransmissionMap); SAMPLER(sampler_ShadowTransmissionMap);
            CBUFFER_START(UnityPerMaterial)
            half _Mode, _Strength, _OcclusionBaseline, _OcclusionPower, _IOR, _Roughness, _FilmMetallic, _FilmSpecular;
            half _UseSceneFilter, _BlurRadiusMillimetres;
            half4 _BlurTint;
            CBUFFER_END
            float3 EyeScene(float2 uv)
            {
                float3 value = SAMPLE_TEXTURE2D_X(_MHEyeSceneColor, sampler_LinearClamp, uv).rgb;
                return _MHEyeSceneEncoding.x > 0
                    ? pow(value, _MHEyeSceneEncoding.y) * _MHEyeSceneEncoding.x : value;
            }
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half fog : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv; o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // ComputeFogIntensity returns zero when no fog keyword is active;
                // MixFogColor correctly returns full transmittance in that case.
                half transmittance = MixFogColor(half3(1, 1, 1), half3(0, 0, 0), input.fog).r;
                if (_Mode < 0.5)
                {
                    if (_UseSceneFilter > 0.5 && _MHEyeSceneReady > 0.5)
                    {
                        float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                        float depth = max(-TransformWorldToView(input.positionWS).z, 0.001);
                        float projectionDepth = lerp(depth, 1, unity_OrthoParams.w);
                        float2 footprint = abs(float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11))
                            * (_BlurRadiusMillimetres * 0.0005 / projectionDepth);
                        float3 center = EyeScene(uv);
                        float3 filtered = 0;
                        // Independent equal-area spiral quadrature; no temporal noise.
                        [unroll] for (int tap = 0; tap < 32; tap++)
                        {
                            float angle = tap * 2.39996323;
                            float2 disc = sqrt((tap + 0.5) / 32.0) * float2(cos(angle), sin(angle));
                            filtered += EyeScene(uv + disc * footprint);
                        }
                        half blur = SAMPLE_TEXTURE2D(_BlurWeightMap, sampler_BlurWeightMap, input.uv).r;
                        half3 transmission = SAMPLE_TEXTURE2D(_ShadowTransmissionMap, sampler_ShadowTransmissionMap, input.uv).rgb;
                        half3 optical = lerp(center, filtered * (_BlurTint.rgb / 32.0), blur) * transmission;
                        return half4(optical * transmittance, transmittance);
                    }
                    float2 q = saturate(abs(input.uv - 0.5) * 2);
                    half edge = pow(max(q.x, q.y), _OcclusionPower);
                    half alpha = _Strength * lerp(_OcclusionBaseline, 1, edge);
                    // Fade attenuation into atmospheric fog without adding fog color twice.
                    alpha *= transmittance;
                    return half4(0, 0, 0, alpha);
                }
                InputData data = (InputData)0;
                data.positionWS = input.positionWS; data.positionCS = input.positionCS;
                data.normalWS = NormalizeNormalPerPixel(input.normalWS);
                data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                data.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                data.shadowMask = 1;
                SurfaceData surface = (SurfaceData)0;
                half f0 = pow((_IOR - 1) / (_IOR + 1), 2);
                surface.specular = lerp((0.08h * _FilmSpecular).xxx, half3(1,1,1), _FilmMetallic); surface.smoothness = 1 - _Roughness;
                surface.normalTS = half3(0, 0, 1); surface.occlusion = 1; surface.alpha = 1;
                half3 reflection = UniversalFragmentPBR(data, surface).rgb * _Strength;
                half fresnel = f0 + (1 - f0) * pow(1 - saturate(dot(data.normalWS, data.viewDirectionWS)), 5);
                // Imported metallic tear films use source opacity directly.
                // Their Fresnel is already evaluated by the lighting BRDF.
                half alpha = saturate(lerp(fresnel, 1, _FilmMetallic) * _Strength);
                return half4(reflection * transmittance, alpha * transmittance);
            }
            ENDHLSL
        }
    }
}

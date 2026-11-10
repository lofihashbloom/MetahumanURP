Shader "Hidden/MetaHuman/SkinDiffusion"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D_X(_MHSource);
        TEXTURE2D_X(_MHOriginalDiffuse);
        TEXTURE2D_X(_MHGuide);
        TEXTURE2D_X(_MHCameraColor);
        TEXTURE2D_X(_MHAlbedo);
        float _MHAlbedoPreservation;
        float4 _MHWidths;
        float4 _MHDiffusion; // world radius, depth tolerance, normal threshold, strength
        float4 _MHDirection; // pixel direction, source is raw extraction, target height
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
        Varyings Vert(uint id : SV_VertexID)
        {
            Varyings o; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS = GetFullScreenTriangleVertexPosition(id);
            o.uv = GetFullScreenTriangleTexCoord(id);
            return o;
        }
        half4 Filter(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.uv;
            half4 center = SAMPLE_TEXTURE2D_X(_MHSource, sampler_LinearClamp, uv);
            half4 original = SAMPLE_TEXTURE2D_X(_MHOriginalDiffuse, sampler_PointClamp, uv);
            if (original.a <= 0) return 0;
            float4 guide = SAMPLE_TEXTURE2D_X(_MHGuide, sampler_PointClamp, uv);
            // MRT resolve averages both lighting and guides with uncovered (zero)
            // samples. Unit-normal length recovers coverage independently of the
            // material's diffusion amount stored in the other target's alpha.
            float coverage = max(length(guide.xyz), 0.0001);
            guide /= coverage;
            if (_MHDirection.z > 0.5)
            {
                float3 albedo = SAMPLE_TEXTURE2D_X(_MHAlbedo, sampler_PointClamp, uv).rgb / coverage;
                center.rgb /= coverage * pow(max(albedo, 0.02), _MHAlbedoPreservation);
            }
            // A radius in metres projects consistently under perspective, orthographic,
            // resolution, and distance changes. Clamp the cost/extent near the camera.
            float projection = abs(UNITY_MATRIX_P._m11) * _MHDirection.w * 0.5;
            float radius = min(40, _MHDiffusion.x * projection / lerp(max(guide.a, 0.001), 1, unity_OrthoParams.w));
            if (radius < 0.35) return half4(center.rgb, original.a);
            // Accumulate differences so a constant field stays bit-stable instead
            // of drifting when several rounded HDR samples are summed/divided.
            float3 deltaSum = 0;
            float3 weights = 1;
            [unroll] for (int i = -6; i <= 6; i++)
            {
                if (i == 0) continue;
                float offset = i / 6.0;
                float2 at = uv + _MHDirection.xy * (offset * radius);
                float4 g = SAMPLE_TEXTURE2D_X(_MHGuide, sampler_PointClamp, at);
                half amount = SAMPLE_TEXTURE2D_X(_MHOriginalDiffuse, sampler_PointClamp, at).a;
                float sampleCoverage = max(length(g.xyz), 0.0001);
                g /= sampleCoverage;
                float valid = step(0.0001, amount) * step(_MHDiffusion.z, dot(guide.xyz, g.xyz));
                valid *= exp(-abs(g.a - guide.a) / max(_MHDiffusion.y, 0.0001));
                valid *= all(at >= 0) && all(at <= 1);
                // Independent RGB Gaussian widths: red travels farther than green/blue.
                float3 weight = exp(-0.5 * offset * offset / (_MHWidths.xyz * _MHWidths.xyz)) * valid;
                // Sample lighting and its guide at the same texel. Linear color
                // filtering could mix a non-skin texel before mask rejection.
                half3 sampleColor = SAMPLE_TEXTURE2D_X(_MHSource, sampler_PointClamp, at).rgb;
                if (_MHDirection.z > 0.5)
                {
                    float3 albedo = SAMPLE_TEXTURE2D_X(_MHAlbedo, sampler_PointClamp, at).rgb / sampleCoverage;
                    sampleColor /= sampleCoverage * pow(max(albedo, 0.02), _MHAlbedoPreservation);
                }
                deltaSum += (sampleColor - center.rgb) * weight;
                weights += weight;
            }
            return half4(center.rgb + deltaSum / weights, original.a);
        }
        half4 RadialFilter(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.uv;
            float4 guide = SAMPLE_TEXTURE2D_X(_MHGuide, sampler_PointClamp, uv);
            half4 original = SAMPLE_TEXTURE2D_X(_MHOriginalDiffuse, sampler_PointClamp, uv);
            if (original.a <= 0) return 0;
            float coverage = max(length(guide.xyz), 0.0001);
            guide /= coverage;
            float3 albedo = SAMPLE_TEXTURE2D_X(_MHAlbedo, sampler_PointClamp, uv).rgb / coverage;
            float3 center = original.rgb / (coverage * pow(max(albedo, .02), _MHAlbedoPreservation));
            float3 distances = _MHWidths.xyz;
            // Per-material neutral radius (Skin _DiffusionNeutralRadius, mm): the albedo target's alpha is 1 + radius,
            // 1 for the profile (MetaHumanSurface.hlsl MHDiffusionRadiusCode; MSAA resolve divided by coverage).
            float neutral = SAMPLE_TEXTURE2D_X(_MHAlbedo, sampler_PointClamp, uv).a / coverage - 1;
            if (neutral > .01) distances = (neutral * .001).xxx;
            float proposalDistance = max(distances.x, max(distances.y, distances.z));
            float depth = lerp(max(guide.a, .001), 1, unity_OrthoParams.w);
            float2 projection = abs(float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11)) * .5 / depth;
            float3 difference = 0;
            float3 totalWeight = 0;
            // Integrate the published normalized-diffusion radial density as a
            // 1:3 mixture of exponential distributions. Independent fixed spiral
            // quadrature avoids frame noise and Unreal's temporal reconstruction.
            [loop] for (int tap = 0; tap < 48; tap++)
            {
                bool shortLobe = tap < 12;
                float quantile = shortLobe ? (tap + .5) / 12 : (tap - 12 + .5) / 36;
                float radius = -log(1 - quantile) * proposalDistance * (shortLobe ? 1 : 3);
                float angle = tap * 2.39996323;
                float2 at = uv + float2(cos(angle), sin(angle)) * radius * projection;
                float4 g = SAMPLE_TEXTURE2D_X(_MHGuide, sampler_PointClamp, at);
                float amount = SAMPLE_TEXTURE2D_X(_MHOriginalDiffuse, sampler_PointClamp, at).a;
                float sampleCoverage = max(length(g.xyz), .0001);
                g /= sampleCoverage;
                float valid = step(.0001, amount) * step(_MHDiffusion.z, dot(guide.xyz, g.xyz));
                valid *= exp(-abs(g.a - guide.a) / max(_MHDiffusion.y, .0001));
                valid *= all(at >= 0) && all(at <= 1);
                float3 density = (exp(-radius / distances) + exp(-radius / (3 * distances))) / distances;
                float proposal = (exp(-radius / proposalDistance) + exp(-radius / (3 * proposalDistance))) / proposalDistance;
                float3 weight = density / max(proposal, 1e-8) * valid;
                float3 sampleAlbedo = SAMPLE_TEXTURE2D_X(_MHAlbedo, sampler_PointClamp, at).rgb / sampleCoverage;
                float3 lighting = SAMPLE_TEXTURE2D_X(_MHSource, sampler_PointClamp, at).rgb
                    / (sampleCoverage * pow(max(sampleAlbedo, .02), _MHAlbedoPreservation));
                difference += (lighting - center) * weight;
                totalWeight += weight;
            }
            return half4(center + difference / max(totalWeight, 1e-5), original.a);
        }

        // Change of the camera colour: the filtered diffuse in place of the original diffuse, scaled by the amount.
        half3 CompositeDelta(float2 uv)
        {
            half4 original = SAMPLE_TEXTURE2D_X(_MHOriginalDiffuse, sampler_PointClamp, uv);
            half3 filtered = SAMPLE_TEXTURE2D_X(_MHSource, sampler_PointClamp, uv).rgb;
            float coverage = saturate(length(SAMPLE_TEXTURE2D_X(_MHGuide, sampler_PointClamp, uv).xyz));
            half amount = saturate(original.a / max(coverage, 0.0001));
            float3 albedo = SAMPLE_TEXTURE2D_X(_MHAlbedo, sampler_PointClamp, uv).rgb / max(coverage, 0.0001);
            filtered *= pow(max(albedo, 0.02), _MHAlbedoPreservation);
            // Specular, fuzz, eye, hair and background are never present in the diffuse
            // buffer. Add only its change; preserve camera alpha and all other lighting.
            return (filtered * coverage - original.rgb) * amount * _MHDiffusion.w;
        }

        half4 Composite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 camera = SAMPLE_TEXTURE2D_X(_MHCameraColor, sampler_PointClamp, input.uv);
            return half4(max(0, camera.rgb + CompositeDelta(input.uv)), camera.a);
        }

        // Additive form (Blend One One onto the camera colour): no copy of the camera colour, and multisampled
        // targets keep their samples. The camera colour already holds the original diffuse, so the sum stays >= 0
        // up to rounding (the copy form clamps it).
        half4 CompositeAdditive(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return half4(CompositeDelta(input.uv), 0);
        }
        ENDHLSL
        Pass
        {
            Name "Bilateral diffusion"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Filter
            ENDHLSL
        }
        Pass
        {
            Name "Diffuse-only composite"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Composite
            ENDHLSL
        }
        Pass
        {
            Name "Radial normalized diffusion"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment RadialFilter
            ENDHLSL
        }
        Pass
        {
            Name "Diffuse-only composite (additive)"
            Blend One One
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment CompositeAdditive
            ENDHLSL
        }
    }
}

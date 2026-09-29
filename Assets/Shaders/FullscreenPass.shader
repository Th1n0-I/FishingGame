Shader "Custom/VolumetricFog"
{
    Properties
    {
        
    }

    SubShader
    {
        Tags {"RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "ProceduralSky.hlsl"

            TEXTURE2D(_VolumetricsTex);
            SAMPLER(sampler_VolumetricsTex);
            
            TEXTURE2D(shadowRT);
            SAMPLER(sampler_shadowRT);
            
            float4 _VolumetricsTex_TexelSize;

            float _ShadowWorldSize;
            // Center of the cloud shadow map, snapped to whole texels by NoiseController.
            float4 _CloudShadowCenter;
            // xyz: direction to the light the clouds use, w: height of the cloud base where the shadow map rays start.
            float4 _CloudShadowLight;
            // Distance in meters over which the air turns the scene into the sky colour, the same as for the clouds.
            float _CloudHazeDistance;
            static const float _ShadowStrength = 0.4;
            
            half4 frag(Varyings IN) : SV_Target
            {
                float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, IN.texcoord);
                float4 fogData = SAMPLE_TEXTURE2D(_VolumetricsTex, sampler_VolumetricsTex, IN.texcoord);
                
                float2 texel = _VolumetricsTex_TexelSize.xy;
                
                float depth = SampleSceneDepth(IN.texcoord);
                
                #if UNITY_REVERSED_Z
                    bool isSky = depth <= 0.0;
                #else
                    bool isSky = depth >= 1.0;
                #endif
                
                // With the light near or below the horizon there is no direct light to shadow.
                if (!isSky && _CloudShadowLight.y > 0.05)
                {
                    float3 worldPos = ComputeWorldSpacePosition(IN.texcoord, depth, UNITY_MATRIX_I_VP);
                    // Follow the light up to the cloud base, that is where this point's shadow ray starts in the map.
                    float2 base_xz = worldPos.xz + _CloudShadowLight.xz * ((_CloudShadowLight.w - worldPos.y) / _CloudShadowLight.y);
                    float2 suv = (base_xz - _CloudShadowCenter.xy) / _ShadowWorldSize + 0.5;

                    if (all(suv >= 0) && all(suv <= 1))
                    {
                        float shadow = SAMPLE_TEXTURE2D(shadowRT, sampler_LinearClamp, suv).r;
                        // Fade the shadows out near the edge of the shadow map instead of a hard square cutoff,
                        // and as the light gets close to the horizon.
                        float edge = saturate(min(min(suv.x, suv.y), min(1.0 - suv.x, 1.0 - suv.y)) * 10.0);
                        edge *= saturate((_CloudShadowLight.y - 0.05) * 10.0);
                        color.rgb *= lerp(1.0, lerp(_ShadowStrength, 1.0, shadow), edge);
                    }
                }

                // Far ground fades into the horizon haze like the clouds do, so seen from above the clouds the
                // gaps and the distance don't show a dark band.
                if (!isSky && _CloudHazeDistance > 0)
                {
                    float3 worldPos = ComputeWorldSpacePosition(IN.texcoord, depth, UNITY_MATRIX_I_VP);
                    float3 toPixel = worldPos - _WorldSpaceCameraPos;
                    float dist = length(toPixel);
                    color.rgb = lerp(color.rgb, procedural_sky(toPixel / max(dist, 1e-3)), 1.0 - exp(-dist / _CloudHazeDistance));
                }

                // The clouds are premultiplied, so only the scene behind them gets dimmed.
                return float4(color.rgb * (1.0 - saturate(fogData.a)) + fogData.rgb, lerp(color.a, 1.0, saturate(fogData.a)));
                
            }
            ENDHLSL
        }
    }
}

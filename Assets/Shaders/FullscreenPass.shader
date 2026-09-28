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
            
            TEXTURE2D(_VolumetricsTex);
            SAMPLER(sampler_VolumetricsTex);
            
            TEXTURE2D(shadowRT);
            SAMPLER(sampler_shadowRT);
            
            float4 _VolumetricsTex_TexelSize;

            float _ShadowWorldSize;
            // Center of the cloud shadow map, snapped to whole texels by NoiseController.
            float4 _CloudShadowCenter;
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
                
                if (!isSky)
                {
                    float3 worldPos = ComputeWorldSpacePosition(IN.texcoord, depth, UNITY_MATRIX_I_VP);
                    float2 suv = (worldPos.xz - _CloudShadowCenter.xy) / _ShadowWorldSize + 0.5;
                    
                    if (all(suv >= 0) && all(suv <= 1))
                    {
                        float shadow = SAMPLE_TEXTURE2D(shadowRT, sampler_LinearClamp, suv).r;
                        // Fade the shadows out near the edge of the shadow map instead of a hard square cutoff.
                        float edge = saturate(min(min(suv.x, suv.y), min(1.0 - suv.x, 1.0 - suv.y)) * 10.0);
                        color.rgb *= lerp(1.0, lerp(_ShadowStrength, 1.0, shadow), edge);
                    }
                }

                // The clouds are premultiplied, so only the scene behind them gets dimmed.
                return float4(color.rgb * (1.0 - saturate(fogData.a)) + fogData.rgb, lerp(color.a, 1.0, saturate(fogData.a)));
                
            }
            ENDHLSL
        }
    }
}

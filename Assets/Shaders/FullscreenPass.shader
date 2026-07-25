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
                    float2 suv = (worldPos.xz - _WorldSpaceCameraPos.xz) / _ShadowWorldSize + 0.5;
                    
                    if (all(suv >= 0) && all(suv <= 1))
                    {
                        float shadow = SAMPLE_TEXTURE2D(shadowRT, sampler_LinearClamp, suv).r;
                        color.rgb *= lerp(_ShadowStrength, 1.0, shadow);
                    }
                }
                
                return lerp(color, float4(fogData.rgb ,1.0), saturate(fogData.a));
                
            }
            ENDHLSL
        }
    }
}

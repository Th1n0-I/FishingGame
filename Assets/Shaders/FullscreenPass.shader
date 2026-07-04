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
            
            TEXTURE2D(_VolumetricsTex);
            SAMPLER(sampler_VolumetricsTex);
            
            float4 _VolumetricsTex_TexelSize;
            
            
            half4 frag(Varyings IN) : SV_Target
            {
                float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, IN.texcoord);
                float4 fogData = SAMPLE_TEXTURE2D(_VolumetricsTex, sampler_VolumetricsTex, IN.texcoord);
                
                float2 texel = _VolumetricsTex_TexelSize.xy;
                
                float w[9] = {
                    0.5, 2.0, 0.5,
                    2.0, 8.0, 0.5,
                    0.5, 2.0, 0.5
                };
                
                float4 sum = 0.0;
                int idx = 0;
                
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 offset = float2(x, y) * texel;
                        sum += SAMPLE_TEXTURE2D(_VolumetricsTex, sampler_VolumetricsTex, IN.texcoord + offset) * w[idx];
                        idx++;
                    }
                }
                sum /= 16;
                
                return lerp(color, float4(sum.rgb ,1.0), saturate(sum.a));
                
            }
            ENDHLSL
        }
    }
}

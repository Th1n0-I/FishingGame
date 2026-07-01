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
                float scaleFactor = _BlitTexture_TexelSize.w / _VolumetricsTex_TexelSize.w;
                
                
                float2 pixelCoords = IN.texcoord * _BlitTexture_TexelSize.wz;
                
                float2 fractionalPixelParts = frac(pixelCoords / scaleFactor);
                
                float2 uv2 = float2(pixelCoords.x + 2, pixelCoords.y) / _BlitTexture_TexelSize.wz;
                float2 uv3 = float2(pixelCoords.x, pixelCoords.y + 2) / _BlitTexture_TexelSize.wz;
                float2 uv4 = (pixelCoords + 2) / _BlitTexture_TexelSize.wz;
                
                float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, IN.texcoord);
                float4 fogData1 = SAMPLE_TEXTURE2D(_VolumetricsTex, sampler_VolumetricsTex, IN.texcoord);
                float4 fogData2 = SAMPLE_TEXTURE2D(_VolumetricsTex, sampler_VolumetricsTex, uv2);
                float4 fogData3 = SAMPLE_TEXTURE2D(_VolumetricsTex, sampler_VolumetricsTex, uv3);
                float4 fogData4 = SAMPLE_TEXTURE2D(_VolumetricsTex, sampler_VolumetricsTex, uv4);
                
                float4 fogData = lerp(lerp(fogData1, fogData2, fractionalPixelParts.x),lerp(fogData3, fogData4, fractionalPixelParts.x), fractionalPixelParts.y);
                
                
                
                return lerp(color, float4(fogData1.rgb ,1.0), saturate(fogData.a));
            }
            ENDHLSL
        }
    }
}

Shader "Custom/Sample3DWorldspace"
{
    Properties
    {
        _NoiseSize("Noise Size", float) = 100
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE3D(_PerlinTex);
            SAMPLER(sampler_PerlinTex);
            
            TEXTURE3D(_WorleyTex);
            SAMPLER(sampler_WorleyTex);
            float _NoiseSize;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 uvw = frac(IN.positionWS / _NoiseSize);
                float3 worley = SAMPLE_TEXTURE3D(_PerlinTex, sampler_PerlinTex, uvw).rgb;
                float fbm = worley.r * 0.625 + worley.g * 0.25 + worley.b * 0.125;
                return float4(fbm.xxx, 1.0);
            }
            ENDHLSL
        }
    }
}
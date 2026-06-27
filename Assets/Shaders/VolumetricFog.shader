Shader "Custom/VolumetricFog"
{
    Properties
    {
        _StartDistance("Start Distance", Range(0,1000)) = 0
        _MaxDistance("Max Distance", Range(20,500)) = 100
        _StepSize("Step Size", Range(0.1, 20)) = 1
        _DensityMultiplier("Density Multiplier", Range(0, 3)) = 1
        _Color("Color", Color) = (1,1,1,1)
        _NoiseOffset("Noise Offset", Range(0,10)) = 0
        _FogHeightCutoff("Fog Height Cutoff", Range(0, 100)) = 20
        _HeightCutoffSoftness("Height Cutoff Softness", Range(0.1, 10)) = 1
        _LightScattering("Light Scattering", Range(0,1)) = 0
        
        _NoiseTiling("Noise Tiling", float) = 1
        _DensityThreshold("Density Threshold", Range(0,1)) = 0.1
        _NoiseSize1("Noise Size 1", Range(1,1000)) = 1.0
        _NoiseSize2("Noise Size 2", Range(1,1000)) = 1.0
        _NoiseSize3("Noise Size 3", Range(1,1000)) = 1.0
        _NoiseWeight1("Noise Weight 1", Range(0.1,1)) = 1.0
        _NoiseWeight2("Noise Weight 2", Range(0.1,1)) = 1.0
        _NoiseWeight3("Noise Weight 3", Range(0.1,1)) = 1.0
        _NoiseSpeed1x("Noise Speed 1 X", Range(1, 100)) = 1.0
        _NoiseSpeed1z("Noise Speed 1 Z", Range(1, 100)) = 1.0
        _NoiseSpeed2x("Noise Speed 2 X", Range(1, 100)) = 1.0
        _NoiseSpeed2z("Noise Speed 2 Z", Range(1, 100)) = 1.0
        _NoiseSpeed3x("Noise Speed 3 X", Range(1, 100)) = 1.0
        _NoiseSpeed3z("Noise Speed 3 Z", Range(1, 100)) = 1.
        
        [HDR]_LightContribution("Light Contribution", Color) = (1,1,1,1)
        
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            float _StartDistance;
            float _MaxDistance;
            float _StepSize;
            float _DensityMultiplier;
            float _NoiseOffset;
            float _NoiseTiling;
            float _DensityThreshold;
            float _FogHeightCutoff;
            float _HeightCutoffSoftness;
            float _LightScattering;
            
            float _NoiseSize1;
            float _NoiseSize2;
            float _NoiseSize3;
            float _NoiseWeight1;
            float _NoiseWeight2;
            float _NoiseWeight3;
            float _NoiseSpeed1x;
            float _NoiseSpeed1z;
            float _NoiseSpeed2x;
            float _NoiseSpeed2z;
            float _NoiseSpeed3x;
            float _NoiseSpeed3z;
            
            float4 _Color;
            float4 _LightContribution;
            
            TEXTURE3D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            
            float HenyeyGreenstein(float g, float costh)
            {
                return (1.0 - g * g) / (4.0 * PI * pow(1.0 + g * g - 2.0 * g * costh, 3.0/2.0));
            }
            
            float get_density(float3 worldPos)
            {
                float t = _Time.x;
                float d = SAMPLE_TEXTURE3D(_NoiseTex, sampler_NoiseTex, float3(
                    (worldPos.x + _NoiseSpeed1x * t) / _NoiseSize1,
                    (worldPos.z + _NoiseSpeed1z * t) / _NoiseSize1,
                    worldPos.y / _NoiseSize1)).b * _DensityMultiplier * _NoiseWeight1;
                d += SAMPLE_TEXTURE3D(_NoiseTex, sampler_NoiseTex, float3(
                    (worldPos.x + _NoiseSpeed2x * t) / _NoiseSize2,
                    (worldPos.z + _NoiseSpeed2z * t) / _NoiseSize2,
                    worldPos.y / _NoiseSize2)).b * _DensityMultiplier * _NoiseWeight2;
                d += SAMPLE_TEXTURE3D(_NoiseTex, sampler_NoiseTex, float3(
                    (worldPos.x + _NoiseSpeed3x * t) / _NoiseSize3,
                    (worldPos.z + _NoiseSpeed3z * t) / _NoiseSize3,
                    worldPos.y / _NoiseSize3)).b * _DensityMultiplier * _NoiseWeight3;
                if (d < _DensityThreshold) return 0;
                return d;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 fogCol = _Color;
                float4 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, IN.texcoord);
                float depth = SampleSceneDepth(IN.texcoord);
                float3 worldPos = ComputeWorldSpacePosition(IN.texcoord, depth, UNITY_MATRIX_I_VP);
                
                float3 entryPoint = _WorldSpaceCameraPos;
                float3 viewDir = worldPos - _WorldSpaceCameraPos;
                float viewLength = length(viewDir);
                float3 rayDir = normalize(viewDir);
                
                float2 pixelCoords = IN.texcoord * _BlitTexture_TexelSize.zw;
                float distLimit = min(viewLength, _MaxDistance + _StartDistance);
                float distTravelled = InterleavedGradientNoise(pixelCoords, (int)(_Time.y / max(HALF_EPS,unity_DeltaTime.x))) * _NoiseOffset + _StartDistance;
                float transmittance = 1;
                
                [loop]
                while (distTravelled < distLimit && transmittance > 0.1)
                {
                    float3 rayPos = entryPoint + rayDir * distTravelled;
                    
                    float density = get_density(rayPos);
                    if (density > 0)
                    {
                        Light mainLight = GetMainLight(TransformWorldToShadowCoord(rayPos));
                        fogCol.rgb += mainLight.color.rgb * _LightContribution.rgb * HenyeyGreenstein(dot(rayDir, mainLight.direction), _LightScattering) *mainLight.shadowAttenuation * density * _StepSize;
                        transmittance *= exp(-density * _StepSize);
                        
                    }
                    distTravelled += _StepSize;
                }
                
                return lerp(col, fogCol, 1.0 - saturate(transmittance));
            }
            ENDHLSL
        }
    }
}

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
            #include "CloudRain.hlsl"

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

            // Rain around the camera, set by NoiseController. See CloudRain.hlsl for _CloudRain.
            TEXTURE2D(weatherTexture);
            SAMPLER(sampler_weatherTexture);
            TEXTURE2D(_CloudStormMap);
            SAMPLER(sampler_CloudStormMap);
            float4 _CloudRain;
            // x: cloud base height, y: angle of one screen pixel in radians.
            float4 _CloudRainInfo;
            // xy: how far the wind has carried the clouds (xz), zw: wind near the ground in m/s (xz).
            float4 _CloudWind;

            float rain_hash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            // Streaks of falling rain on 4 cylinders around the camera, 3 to 37 m out. Being fixed in the world they
            // stay put when the camera turns, and anything closer than a cylinder hides its streaks.
            // Returns how much of the pixel the streaks cover.
            float rain_streaks(float3 ray, float scene_distance, float amount)
            {
                float horizontal = length(ray.xz);
                if (horizontal < 0.05) return 0.0;
                float angle = atan2(ray.z, ray.x);
                // Wind across the view slants the streaks and carries them sideways.
                float wind_across = dot(_CloudWind.zw, float2(-ray.z, ray.x) / horizontal);
                float cover = 0.0;
                [unroll] for (int layer = 0; layer < 4; layer++)
                {
                    float radius = 3.0 * pow(2.3, layer);
                    float t = radius / horizontal;
                    if (t > scene_distance) continue;
                    // A whole number of columns around the cylinder, so there is no seam behind the camera.
                    float columns = max(round(6.2831853 * radius / (0.3 * (1.0 + layer))), 1.0);
                    float cell_width = 6.2831853 * radius / columns;
                    float cell_height = 2.0 * (1.0 + layer);
                    float height = _WorldSpaceCameraPos.y + ray.y * t;
                    float u = (angle / 6.2831853 + 0.5) * columns - wind_across * _Time.y / cell_width;
                    float v = (height + 9.0 * _Time.y) / cell_height + layer * 0.37;
                    float2 cell = floor(float2(u, v));
                    float2 f = float2(u, v) - cell;
                    float h = rain_hash(cell + layer * 17.0);
                    // Light rain has drops in fewer cells.
                    if (h > amount) continue;
                    float along = frac(f.y - frac(h * 7.7)) / 0.4;
                    if (along > 1.0) continue;
                    float slant = clamp(wind_across * cell_height / (9.0 * cell_width), -0.5, 0.5);
                    float x = 0.15 + 0.7 * frac(h * 13.1) - along * 0.4 * slant;
                    // About 1.2 pixels wide on screen, whatever the distance.
                    float width = max(t * _CloudRainInfo.y * 1.2 / cell_width, 0.005);
                    cover += saturate(1.0 - abs(f.x - x) / width) * sin(along * 3.14159265) * (1.0 - 0.18 * layer);
                }
                return saturate(cover);
            }
            
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
                float3 result = color.rgb * (1.0 - saturate(fogData.a)) + fogData.rgb;

                // Rain in front of everything, where it rains above the camera and only below the cloud base.
                if (_CloudRain.x > 0)
                {
                    float2 uv = (_WorldSpaceCameraPos.xz - _CloudWind.xy) / 128000.0;
                    float rain = cloud_precipitation(SAMPLE_TEXTURE2D_LOD(weatherTexture, sampler_weatherTexture, uv, 0),
                                                     SAMPLE_TEXTURE2D_LOD(_CloudStormMap, sampler_CloudStormMap, uv, 0).rg, _CloudRain);
                    rain *= saturate((_CloudRainInfo.x - _WorldSpaceCameraPos.y) / 300.0);
                    if (rain > 0.01)
                    {
                        float3 toPixel = ComputeWorldSpacePosition(IN.texcoord, depth, UNITY_MATRIX_I_VP) - _WorldSpaceCameraPos;
                        float sceneDistance = isSky ? 1e9 : length(toPixel);
                        float3 ray = normalize(toPixel);
                        // Drops catch the light of the sky around them.
                        float3 rainColor = procedural_sky(float3(ray.x, 0.2, ray.z)) * 0.8;
                        result = lerp(result, rainColor, rain_streaks(ray, sceneDistance, rain) * 0.35);
                    }
                }

                return float4(result, lerp(color.a, 1.0, saturate(fogData.a)));
                
            }
            ENDHLSL
        }
    }
}

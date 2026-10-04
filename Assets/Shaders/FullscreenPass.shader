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

            // Rain around the camera, set by NoiseController. See rain_amount in CloudRain.hlsl for _CloudRain.
            TEXTURE2D(weatherTexture);
            SAMPLER(sampler_weatherTexture);
            TEXTURE2D(_CloudStormMap);
            SAMPLER(sampler_CloudStormMap);
            float4 _CloudRain;
            // x: cloud base height, y: angle of one screen pixel in radians.
            float4 _CloudRainInfo;
            // xy: how far the wind has carried the clouds (xz), zw: wind near the ground in m/s (xz).
            float4 _CloudWind;

            // Lightning, set by NoiseController. xyz: where the flash is, w: its brightness right now.
            float4 _LightningFlash;
            // The bolt from the cloud base to the ground in world space, x of the info: number of points (0 = no bolt).
            float4 _LightningBolt[33];
            float4 _LightningBoltInfo;

            // Clip space position of a bolt point, flipped the same way ComputeWorldSpacePosition reads IN.texcoord.
            float4 bolt_clip(float3 position)
            {
                float4 clip = mul(UNITY_MATRIX_VP, float4(position, 1.0));
                #if UNITY_UV_STARTS_AT_TOP
                    clip.y = -clip.y;
                #endif
                return clip;
            }

            float2 bolt_pixel(float4 clip)
            {
                return (clip.xy / max(abs(clip.w), 1e-5) * 0.5 + 0.5) * _ScreenParams.xy;
            }

            // How bright the bolt is in this pixel: a thin core and a soft glow around the line. Hidden where the
            // scene is closer than the bolt.
            float lightning_bolt(float2 pixel, float scene_distance)
            {
                int count = (int)_LightningBoltInfo.x;
                float best = 1e9;
                float best_distance = 0.0;
                float4 clip_a = bolt_clip(_LightningBolt[0].xyz);
                float2 a = bolt_pixel(clip_a);
                for (int i = 1; i < 33; i++)
                {
                    if (i >= count) break;
                    float4 clip_b = bolt_clip(_LightningBolt[i].xyz);
                    float2 b = bolt_pixel(clip_b);
                    // Skip segments that reach behind the camera.
                    if (clip_a.w > 0 && clip_b.w > 0)
                    {
                        float2 ab = b - a;
                        float along = saturate(dot(pixel - a, ab) / max(dot(ab, ab), 1e-4));
                        float d = length(pixel - (a + ab * along));
                        if (d < best)
                        {
                            best = d;
                            best_distance = lerp(length(_LightningBolt[i - 1].xyz - _WorldSpaceCameraPos),
                                                 length(_LightningBolt[i].xyz - _WorldSpaceCameraPos), along);
                        }
                    }
                    clip_a = clip_b;
                    a = b;
                }
                if (best_distance > scene_distance) return 0.0;
                return exp(-best * best / 2.0) + 0.25 * exp(-best * best / 150.0);
            }

            float rain_hash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            // Streaks of falling rain on 6 cylinders around the camera, 2.5 to 62 m out. Being fixed in the world they
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
                [unroll] for (int layer = 0; layer < 6; layer++)
                {
                    float radius = 2.5 * pow(1.9, layer);
                    float t = radius / horizontal;
                    if (t > scene_distance) continue;
                    // A whole number of columns around the cylinder, so there is no seam behind the camera.
                    float columns = max(round(6.2831853 * radius / (0.18 * (1.0 + 0.6 * layer))), 1.0);
                    float cell_width = 6.2831853 * radius / columns;
                    float cell_height = 1.6 * (1.0 + 0.7 * layer);
                    float height = _WorldSpaceCameraPos.y + ray.y * t;
                    float u = (angle / 6.2831853 + 0.5) * columns - wind_across * _Time.y / cell_width;
                    float v = (height + 9.0 * _Time.y) / cell_height + layer * 0.37;
                    float2 cell = floor(float2(u, v));
                    float2 f = float2(u, v) - cell;
                    float h = rain_hash(cell + layer * 17.0);
                    // Light rain has drops in fewer cells.
                    if (h > amount) continue;
                    float along = frac(f.y - frac(h * 7.7)) / 0.5;
                    if (along > 1.0) continue;
                    float slant = clamp(wind_across * cell_height / (9.0 * cell_width), -0.5, 0.5);
                    float x = 0.15 + 0.7 * frac(h * 13.1) - along * 0.5 * slant;
                    // About 2.2 pixels wide on screen, whatever the distance.
                    float width = max(t * _CloudRainInfo.y * 2.2 / cell_width, 0.005);
                    cover += saturate(1.0 - abs(f.x - x) / width) * sin(along * 3.14159265) * (1.0 - 0.12 * layer);
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

                // Out past the terrain the sky pixels below the horizon stand in for ground at sea level, hazed by its
                // distance like the real ground above. The skybox's own dark ground showed as a band under the far
                // clouds when seen from above them.
                if (isSky && _CloudHazeDistance > 0)
                {
                    float3 skyRay = normalize(ComputeWorldSpacePosition(IN.texcoord, depth, UNITY_MATRIX_I_VP) - _WorldSpaceCameraPos);
                    if (skyRay.y < 0)
                    {
                        float groundDistance = max(_WorldSpaceCameraPos.y, 1.0) / -skyRay.y;
                        color.rgb = lerp(color.rgb, procedural_sky(skyRay), 1.0 - exp(-groundDistance / _CloudHazeDistance));
                    }
                }

                // The clouds are premultiplied, so only the scene behind them gets dimmed.
                float cloudAlpha = saturate(fogData.a);
                float3 result = color.rgb * (1.0 - cloudAlpha) + fogData.rgb;

                // Lightning: the flash lights up the clouds around it from inside, a bit of the whole scene, and the bolt.
                if (_LightningFlash.w > 0)
                {
                    float3 flashColor = float3(0.8, 0.86, 1.0);
                    float3 toPixel = ComputeWorldSpacePosition(IN.texcoord, depth, UNITY_MATRIX_I_VP) - _WorldSpaceCameraPos;
                    float sceneDistance = isSky ? 1e9 : length(toPixel);
                    float3 ray = normalize(toPixel);
                    float3 toFlash = _LightningFlash.xyz - _WorldSpaceCameraPos;
                    float flashDistance = max(length(toFlash), 1.0);
                    // About a 4 km glow inside the cloud, plus a wide faint one.
                    float spread = 4000.0 / flashDistance;
                    float offAxis = (1.0 - dot(ray, toFlash / flashDistance)) * 2.0;
                    float glow = exp(-offAxis / (spread * spread)) * 3.0 + exp(-offAxis / (16.0 * spread * spread)) * 0.4;
                    result += flashColor * (_LightningFlash.w * cloudAlpha * glow);
                    result += flashColor * (_LightningFlash.w * 0.08 * saturate(20000.0 / flashDistance));
                    if (_LightningBoltInfo.x > 1)
                    {
                        // From below the clouds the bolt is in front of them, from above they hide it.
                        float bolt = lightning_bolt(IN.texcoord * _ScreenParams.xy, sceneDistance);
                        bolt *= _WorldSpaceCameraPos.y < _CloudRainInfo.x ? 1.0 : 1.0 - cloudAlpha;
                        result += flashColor * (bolt * _LightningBoltInfo.y * 6.0);
                    }
                }

                // Rain in front of everything, where it rains above the camera and only below the cloud base.
                if (_CloudRain.x > 0)
                {
                    // The rain falling on the camera, leaning with the wind like the curtains (CloudRain.hlsl).
                    float2 windDir = dot(_CloudWind.zw, _CloudWind.zw) > 1e-6 ? normalize(_CloudWind.zw) : float2(0.0, 0.0);
                    float2 p = _WorldSpaceCameraPos.xz + windDir * ((_CloudRainInfo.x - _WorldSpaceCameraPos.y) * 0.35) - _CloudWind.xy;
                    float2 warp_noise = SAMPLE_TEXTURE2D_LOD(weatherTexture, sampler_weatherTexture, p / STORM_WARP_SCALE, STORM_WARP_MIP).ba;
                    float patches = SAMPLE_TEXTURE2D_LOD(weatherTexture, sampler_weatherTexture, p / 24000.0 + 0.5, 0).a;
                    bool underCell;
                    float rain = rain_amount(SAMPLE_TEXTURE2D_LOD(weatherTexture, sampler_weatherTexture, p / 128000.0, 0),
                                             SAMPLE_TEXTURE2D_LOD(_CloudStormMap, sampler_CloudStormMap,
                                                                  storm_warp(p, warp_noise) / 128000.0, 0).rg,
                                             patches, _CloudRain, 1.0, underCell);
                    rain *= saturate((_CloudRainInfo.x - _WorldSpaceCameraPos.y) / 300.0);
                    if (rain > 0.01)
                    {
                        float3 toPixel = ComputeWorldSpacePosition(IN.texcoord, depth, UNITY_MATRIX_I_VP) - _WorldSpaceCameraPos;
                        float sceneDistance = isSky ? 1e9 : length(toPixel);
                        float3 ray = normalize(toPixel);
                        // Drops catch the light of the sky around them and show up lighter than what is behind them.
                        float3 rainColor = max(procedural_sky(float3(ray.x, 0.2, ray.z)) * 1.1, result * 1.35);
                        result = lerp(result, rainColor, rain_streaks(ray, sceneDistance, rain) * 0.6);
                    }
                }

                return float4(result, lerp(color.a, 1.0, saturate(fogData.a)));
                
            }
            ENDHLSL
        }
    }
}

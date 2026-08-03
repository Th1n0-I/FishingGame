Shader "UI/Texture3DSlice"
{
    Properties {
        _Volume ("Volume", 3D) = "" {}
        _Slice  ("Slice", Range(0,1)) = 0.5
        _Channel ("Channel", Int) = 0
        [HideInInspector] _MainTex ("Sprite Texture", 2D) = "white" {}
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent"
               "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Cull Off  Lighting Off  ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE3D(_Volume); SAMPLER(sampler_Volume);
            float _Slice;
            int _Channel;
            struct A { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct V { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            V vert(A v){ V o; o.vertex=TransformObjectToHClip(v.vertex.xyz); o.uv=v.uv; o.color=v.color; return o; }
            half4 frag(V i):SV_Target {
                float val;

                if (_Channel == 0)
                {
                    val = SAMPLE_TEXTURE3D(_Volume, sampler_Volume, float3(i.uv / 2.0, _Slice)).r;
                } else if (_Channel == 1)
                {
                    val = SAMPLE_TEXTURE3D(_Volume, sampler_Volume, float3(i.uv / 2.0, _Slice)).g;
                } else if (_Channel == 2)
                {
                    val = SAMPLE_TEXTURE3D(_Volume, sampler_Volume, float3(i.uv / 2.0, _Slice)).b;
                } else if (_Channel == 3)
                {
                    val = SAMPLE_TEXTURE3D(_Volume, sampler_Volume, float3(i.uv / 2.0, _Slice)).a;
                }
                
                return half4(val,val,val,1) * i.color;
            }
            ENDHLSL
        }
    }
}
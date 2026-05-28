Shader "Custom/PortalZone"
{
    Properties
    {
        _Color ("Color", Color) = (0, 1, 0.5, 1)
        _Speed ("Speed", Float) = 1.0
        _RingWidth ("Ring Width", Float) = 0.2
        _Glow ("Glow", Float) = 2.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Speed;
                float _RingWidth;
                float _Glow;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 center = float2(0.5, 0.5);
                float dist = distance(i.uv, center) * 2.0; 
                
                if (dist > 1.0) return half4(0,0,0,0);

                float time = _Time.y * _Speed;
                
                // Expanding rings
                float ring = frac(dist * 3.0 - time);
                float ringGlow = smoothstep(0.0, 0.2, ring) * smoothstep(1.0, 0.8, ring);

                // Outer edge glow
                float edge = smoothstep(1.0 - _RingWidth, 1.0, dist);
                
                // Swirling energy
                float angle = atan2(i.uv.y - 0.5, i.uv.x - 0.5);
                float swirl = sin(angle * 6.0 + time * 3.0) * 0.5 + 0.5;
                float swirl2 = sin(angle * 3.0 - time * 2.0) * 0.5 + 0.5;
                
                float energy = (ringGlow * 0.5 + edge * 0.8) * (swirl * 0.5 + swirl2 * 0.5 + 0.5);
                
                // Center core
                float core = smoothstep(0.3, 0.0, dist);
                
                float alpha = (energy + core) * _Glow;
                alpha *= smoothstep(1.0, 0.95, dist); // Soft edge

                return half4(_Color.rgb * alpha, alpha * _Color.a);
            }
            ENDHLSL
        }
    }
}

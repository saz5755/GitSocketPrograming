// 점광원 빌보드 — 항법등·충돌방지 스트로브·노즐 글로우·미사일 모터 불빛.
//   · 버텍스 셰이더에서 카메라를 향하는 사각형을 만든다 (MeshRenderer + 단위 쿼드)
//   · 멀어져도 최소 시야각(_MinAngle)을 유지 — 실제처럼 수 km 밖에서도 불빛이 점으로 보인다
//   · 확대된 만큼 밝기를 줄여 멀리서 과하게 번지지 않게
//   · _Star : 강한 광원의 십자 회절 광선
Shader "Custom/FXFlare"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Size      ("World Size (m)", Float) = 0.5
        _MinAngle  ("Min Angular Size (rad)", Float) = 0.0025
        _Star      ("Star Streaks", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "FXFlare"
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Intensity, _Size, _MinAngle, _Star;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float gain : TEXCOORD1; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                float3 toCam  = _WorldSpaceCameraPos - center;
                float  d      = length(toCam);
                float  want   = d * _MinAngle;
                float  size   = max(_Size, want);
                float3 right  = UNITY_MATRIX_V[0].xyz;
                float3 up     = UNITY_MATRIX_V[1].xyz;
                // 기체 표면에 파묻히지 않게 카메라 쪽으로 살짝 당긴다
                float3 p = center + (right * IN.positionOS.x + up * IN.positionOS.y) * size * 2
                         + toCam / max(d, 1e-3) * min(size, 0.6);
                o.positionCS = TransformWorldToHClip(p);
                o.uv = IN.uv;
                o.gain = saturate(pow(abs(_Size / max(want, 1e-4)), 0.6));
                return o;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 c = IN.uv * 2 - 1;
                float r2 = dot(c, c);
                float a = exp(-r2 * 9) + exp(-r2 * 2.2) * 0.18;
                float st = (exp(-abs(c.y) * 45) * exp(-abs(c.x) * 2.5) + exp(-abs(c.x) * 45) * exp(-abs(c.y) * 2.5)) * _Star;
                float edge = saturate(1 - r2);
                return float4(_Color.rgb * _Intensity * (a + st) * edge * IN.gain, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

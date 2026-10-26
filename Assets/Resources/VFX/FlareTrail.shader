// 플레어 연소 궤적 — TrailRenderer 전용 리본 셰이더 (Additive, HDR).
// uv.x: 0 = 연소체(머리), 1 = 꼬리   /  uv.y: 리본 폭 방향
// 정점 색(그라데이션)에 강도를 곱해 머리 쪽은 주황 백열, 꼬리는 어두운 적색으로 식는다.
Shader "Custom/FlareTrail"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 3.0
        _Seed      ("Seed", Float) = 0
        _Fade      ("Fade (0-1)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent+20"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FlareTrail"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _Seed;
                float _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                float3 positionWS : TEXCOORD1;
            };

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float noise2(float2 p)
            {
                float2 i = floor(p), f = frac(p), u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), u.x),
                            lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), u.x), u.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs v = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = v.positionCS;
                OUT.positionWS = v.positionWS;
                OUT.uv    = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float across = abs(IN.uv.y * 2.0 - 1.0);

                // 월드 고정 노이즈 — 리본이 길어져도 무늬가 미끄러지지 않는다
                float n = noise2(IN.positionWS.xz * 2.3 + IN.positionWS.y * 1.7 + _Seed)
                        * 0.6 + noise2(IN.positionWS.xy * 5.1 - _Time.y * 3.0) * 0.4;

                // 가장자리를 노이즈로 깎아 균일한 띠가 아니라 타오르는 연소 가스처럼 보이게
                float edge = 1.0 - smoothstep(0.15 + n * 0.35, 0.95, across);
                float core = exp(-across * across * 18.0);
                float tail = 1.0 - IN.uv.x;
                // 머리 구간은 빌보드 핵이 담당 — 리본이 핵과 겹쳐 각진 백색 덩어리로 포화되지 않게 페이드인
                float head = smoothstep(0.0, 0.07, IN.uv.x);

                float flick = 0.8 + 0.3 * noise2(float2(_Time.y * 24.0, _Seed));
                float3 col  = IN.color.rgb * (edge * 0.65 + core * 1.4) * tail * tail * head;

                col *= IN.color.a * _Intensity * flick * _Fade;
                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

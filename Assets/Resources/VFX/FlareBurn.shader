// 적외선 기만 플레어 연소체 — 카메라 정렬 빌보드 (파티클 미사용).
// 쿼드 메쉬(±0.5)를 버텍스 셰이더에서 뷰 공간으로 펼쳐 항상 카메라를 향하게 한다.
//   · 백열 핵      — 아주 작고 강한 HDR 백황색 (Bloom이 광원처럼 번지게 함)
//   · 연소 화염    — 불규칙하게 일렁이는 주황-적색 층
//   · 적색 광륜    — 넓고 옅은 심홍색 글로우 (멀리서도 보이는 실루엣)
//   · 렌즈 스트릭  — 가로로 얇게 퍼지는 아나모픽 광선
// _Age(0~1)로 점화 플래시 → 안정 연소 → 냉각(적열 잔화) 단계를 표현한다.
Shader "Custom/FlareBurn"
{
    Properties
    {
        _Age         ("Age (0-1)", Range(0, 1)) = 0
        _Seed        ("Seed", Float) = 0
        _Size        ("World Size (m)", Float) = 4.2
        _MinAngle    ("Min Angular Size (rad)", Float) = 0.006
        _Intensity   ("Intensity", Float) = 1
        _CoreColor   ("Core Color", Color)  = (1.0, 0.86, 0.62, 1)
        _FlameColor  ("Flame Color", Color) = (1.0, 0.30, 0.06, 1)
        _GlowColor   ("Glow Color", Color)  = (0.85, 0.08, 0.02, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent+30"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FlareBurn"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _Age;
                float  _Seed;
                float  _Size;
                float  _MinAngle;
                float  _Intensity;
                float4 _CoreColor;
                float4 _FlameColor;
                float4 _GlowColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;   // -1..1
                float  boost      : TEXCOORD1;   // 원거리 확대 비율 — 멀수록 광륜 위주로
            };

            float hash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }
            float noise1(float x)
            {
                float i = floor(x), f = frac(x);
                return lerp(hash11(i), hash11(i + 1.0), f * f * (3.0 - 2.0 * f));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 centerWS = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float3 centerVS = TransformWorldToView(centerWS);
                float  dist     = max(-centerVS.z, 0.01);

                // 멀어져도 일정 각크기 이하로 작아지지 않게 — 실제 플레어는 수 km에서도 점광원으로 보인다
                float size  = max(_Size, dist * _MinAngle);
                OUT.boost   = size / _Size;

                float3 posVS = centerVS + float3(IN.positionOS.xy * size, 0);
                OUT.positionCS = TransformWViewToHClip(posVS);
                OUT.uv = IN.positionOS.xy * 2.0;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 uv  = IN.uv;
                float  r   = length(uv);
                if (r > 1.0) discard;

                float t    = _Time.y;
                float seed = _Seed * 17.31;

                // ── 수명 단계 ────────────────────────────────────────────
                float ignite  = 1.0 - smoothstep(0.0, 0.06, _Age);           // 점화 순간 플래시
                float burnout = smoothstep(0.72, 1.0, _Age);                  // 냉각
                float alive   = 1.0 - smoothstep(0.93, 1.0, _Age);

                // ── 연소 불안정성 — 마그네슘/테플론 연소 특유의 고주파 깜빡임 ──
                float flick = 0.78 + 0.32 * noise1(t * 31.0 + seed) + 0.12 * noise1(t * 73.0 + seed * 2.1);

                // 화염 가장자리 일렁임: 각도별 반경 왜곡
                float ang   = atan2(uv.y, uv.x);
                float wob   = noise1(ang * 2.2 + t * 9.0 + seed) * 0.5 + noise1(ang * 5.1 - t * 14.0 + seed) * 0.25;
                float rf    = r * (1.0 + (wob - 0.37) * 0.55);

                // ── 레이어 ───────────────────────────────────────────────
                float coreR  = lerp(0.055, 0.02, burnout) * (1.0 + ignite * 1.8);
                float core   = exp(-(r * r) / (coreR * coreR));
                float flame  = exp(-(rf * rf) / 0.022) * (1.0 - burnout * 0.65);
                float glow   = exp(-r * 5.2) * smoothstep(1.0, 0.55, r);
                float streak = exp(-abs(uv.y) * 85.0) * exp(-abs(uv.x) * 4.2) * (1.0 - burnout);

                // 원거리: 핵·화염은 화면에서 너무 작아지므로 광륜 비중을 키워 붉은 점광으로 보이게
                float far = saturate((IN.boost - 1.0) * 0.5);

                float3 coreCol  = _CoreColor.rgb  * core  * (9.0 + ignite * 22.0);
                float3 flameCol = _FlameColor.rgb * flame * 3.6;
                float3 glowCol  = _GlowColor.rgb  * glow  * (1.35 + far * 1.6) * (1.0 + ignite * 2.0);
                float3 strkCol  = lerp(_FlameColor.rgb, _CoreColor.rgb, 0.35) * streak * 1.4;

                // 냉각 단계: 백열 → 심홍색 잔화
                float3 col = coreCol + flameCol + glowCol + strkCol;
                col = lerp(col, _GlowColor.rgb * (core * 2.5 + glow * 0.6), burnout * 0.7);

                col *= flick * alive * _Intensity;
                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

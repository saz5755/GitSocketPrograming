// 플레어 연기 궤적 — TrailRenderer 전용 리본 셰이더 (알파 블렌드).
// 입자 대신 하나의 연속된 리본으로 실제 플레어가 남기는 매끈한 백색 연기 곡선을 그린다.
//   · 월드 고정 노이즈로 가장자리를 침식 — 리본이 길어져도 무늬가 흘러내리지 않음
//   · 주광/환경광으로 음영 — 하늘 밝기와 어울리게
//   · 머리 쪽은 연소체의 붉은 빛을 받아 적색으로 물든다
Shader "Custom/FlareSmoke"
{
    Properties
    {
        _Color     ("Smoke Color", Color) = (0.86, 0.85, 0.84, 1)
        _GlowTint  ("Glow Tint (HDR)", Color) = (2.2, 0.35, 0.08, 1)
        _GlowReach ("Glow Reach (0-1 of trail)", Range(0.01, 0.5)) = 0.07
        _Opacity   ("Opacity", Range(0, 1)) = 0.55
        _Seed      ("Seed", Float) = 0
        _Fade      ("Fade (0-1)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent+10"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FlareSmoke"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _GlowTint;
                float  _GlowReach;
                float  _Opacity;
                float  _Seed;
                float  _Fade;
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

            float hash31(float3 p) { p = frac(p * 0.1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }
            float noise3(float3 p)
            {
                float3 i = floor(p), f = frac(p), u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash31(i),                  hash31(i + float3(1, 0, 0)), u.x),
                                 lerp(hash31(i + float3(0, 1, 0)), hash31(i + float3(1, 1, 0)), u.x), u.y),
                            lerp(lerp(hash31(i + float3(0, 0, 1)), hash31(i + float3(1, 0, 1)), u.x),
                                 lerp(hash31(i + float3(0, 1, 1)), hash31(i + float3(1, 1, 1)), u.x), u.y), u.z);
            }
            float fbm(float3 p)
            {
                return noise3(p) * 0.55 + noise3(p * 2.07 + 7.1) * 0.3 + noise3(p * 4.13 + 3.7) * 0.15;
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
                float age    = IN.uv.x;                     // 0 머리 → 1 꼬리
                float across = abs(IN.uv.y * 2.0 - 1.0);

                float3 p = IN.positionWS * 0.55 + float3(0, -_Time.y * 0.25, 0) + _Seed;
                float  n = fbm(p);

                // 가장자리 침식 — 오래된 연기일수록 더 흩어짐
                float soft  = lerp(0.35, 0.8, age);
                float body  = 1.0 - smoothstep(0.55 - soft * 0.5 + n * 0.45 - 0.2, 1.0, across);
                float dens  = body * lerp(0.75, 1.15, n);

                // 머리에서 연기가 막 생기는 구간은 얇게 (연소체에 가려지는 부분)
                float birth = smoothstep(0.0, 0.025, age);
                float alpha = dens * birth * (1.0 - age) * IN.color.a * _Opacity * _Fade;

                // 음영: 위쪽 환경광 + 주광 — 노이즈로 두께감
                Light  mainLight = GetMainLight();
                float3 ambient   = SampleSH(float3(0, 1, 0));
                float3 lit       = ambient * 0.85 + mainLight.color * (0.35 + 0.25 * n);
                float3 col       = _Color.rgb * IN.color.rgb * lit;

                // 연소체 근처는 붉은 빛을 받는다
                float glow = exp(-age / _GlowReach) * (1.0 - across * 0.6);
                col += _GlowTint.rgb * glow * _Fade;

                return float4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }
    FallBack Off
}

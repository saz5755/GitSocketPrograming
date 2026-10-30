// 가산 발광 파티클 — 화염·섬광·스파크·예광탄 공용 (텍스처 없이 절차적 형태).
//   · 방사형 부드러운 감쇠 + 하얗게 타는 중심부 (HDR → 블룸)
//   · 노이즈 깜빡임 — 화염이 살아 움직이게
//   · 소프트 파티클 — 지오메트리와 맞닿는 경계가 칼처럼 잘리지 않음
//   · _LineMode=1 : LineRenderer/TrailRenderer 용 (가로 방향으로만 감쇠)
Shader "Custom/FXGlow"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 1
        _Falloff   ("Falloff Power", Float) = 1.6
        _Core      ("Core Whiteness", Range(0, 2)) = 0.6
        _Flicker   ("Noise Flicker", Range(0, 1)) = 0.25
        _SoftDist  ("Soft Particle Distance", Float) = 1.5
        _LineMode  ("Line Mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }

        Pass
        {
            Name "FXGlow"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity, _Falloff, _Core, _Flicker, _SoftDist, _LineMode;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; float4 screen : TEXCOORD1; };

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p), u = f * f * (3 - 2 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), u.x), lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), u.x), u.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings o;
                VertexPositionInputs v = GetVertexPositionInputs(IN.positionOS.xyz);
                o.positionCS = v.positionCS;
                o.screen = ComputeScreenPos(v.positionCS);
                o.color = IN.color;
                o.uv = IN.uv;
                return o;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 c = IN.uv.xy * 2 - 1;
                float r = _LineMode > 0.5 ? abs(c.y) : length(c);
                float body = pow(saturate(1 - r), _Falloff);
                float core = pow(saturate(1 - r * 1.6), 3);

                float n = vnoise(c * 3.1 + IN.uv.zw * 37 + _Time.y * float2(6.3, -4.7));
                float flick = lerp(1, 0.55 + 0.9 * n, _Flicker);

                // 소프트 파티클 + 카메라 근접 페이드
                float eye   = IN.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(IN.screen.xy / IN.screen.w), _ZBufferParams);
                float soft  = saturate((scene - eye) / max(0.01, _SoftDist));
                float nearF = saturate((eye - 0.25) / 1.2);

                float3 col = IN.color.rgb * body + lerp(IN.color.rgb, 1, 0.75) * core * _Core;
                return float4(col * _Intensity * flick * IN.color.a * soft * nearF, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

// 조명 받는 연기 파티클 — 폭발 연기·미사일 항적·물보라·증기 공용 (텍스처 없이 절차적 형태).
//   · 3D 노이즈로 가장자리를 침식, 나이가 들수록 더 흩어진다
//   · 구 형태 법선 근사 + 하프 램버트 + 환경광 → 볼륨감 있는 음영
//   · 태양을 등진 연기의 은빛 테두리 (전방 산란)
//   · _Ember : 막 생긴 불덩이 연기 안쪽에서 비치는 불빛
// 파티클 커스텀 버텍스 스트림: UV(TEXCOORD0.xy) · StableRandomXY(TEXCOORD0.zw) · AgePercent(TEXCOORD1.x)
Shader "Custom/FXSmoke"
{
    Properties
    {
        _Color     ("Smoke Color", Color) = (0.82, 0.82, 0.82, 1)
        _Opacity   ("Opacity", Range(0, 2)) = 0.8
        _Ember     ("Ember Glow (HDR)", Color) = (0, 0, 0, 1)
        _EmberAge  ("Ember Age (0-1)", Range(0.01, 1)) = 0.25
        _NoiseScale("Noise Scale", Float) = 2.2
        _Erode     ("Edge Erosion", Range(0, 1.5)) = 0.7
        _SoftDist  ("Soft Particle Distance", Float) = 3
        _Shade     ("Self Shadow", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }

        Pass
        {
            Name "FXSmoke"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _Ember;
                float  _Opacity, _EmberAge, _NoiseScale, _Erode, _SoftDist, _Shade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; float4 uv1 : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float4 color : COLOR; float4 uv : TEXCOORD0;
                float  age : TEXCOORD1; float4 screen : TEXCOORD2; float3 positionWS : TEXCOORD3;
            };

            float hash31(float3 p) { p = frac(p * 0.1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }
            float noise3(float3 p)
            {
                float3 i = floor(p), f = frac(p), u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash31(i), hash31(i + float3(1, 0, 0)), u.x), lerp(hash31(i + float3(0, 1, 0)), hash31(i + float3(1, 1, 0)), u.x), u.y),
                            lerp(lerp(hash31(i + float3(0, 0, 1)), hash31(i + float3(1, 0, 1)), u.x), lerp(hash31(i + float3(0, 1, 1)), hash31(i + float3(1, 1, 1)), u.x), u.y), u.z);
            }
            float fbm(float3 p) { return noise3(p) * 0.5 + noise3(p * 2.03 + 5.1) * 0.3 + noise3(p * 4.11 + 1.7) * 0.2; }

            Varyings vert(Attributes IN)
            {
                Varyings o;
                VertexPositionInputs v = GetVertexPositionInputs(IN.positionOS.xyz);
                o.positionCS = v.positionCS;
                o.positionWS = v.positionWS;
                o.screen = ComputeScreenPos(v.positionCS);
                o.color = IN.color;
                o.uv = IN.uv;
                o.age = IN.uv1.x;
                return o;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 c   = IN.uv.xy * 2 - 1;
                float  r2  = dot(c, c);
                float  age = IN.age;
                float2 rnd = IN.uv.zw;

                float n = fbm(float3(c * _NoiseScale * (0.8 + 0.4 * rnd.x), rnd.y * 61.0 + age * 1.3 + _Time.y * 0.05));

                // 구형 마스크를 노이즈로 침식 — 오래될수록 성기게
                float mask = saturate(1 - r2);
                float erode = _Erode * (0.55 + age * 0.6);
                float dens = saturate((mask - (1 - n) * erode) * 2.4);

                // 구 법선 근사 (뷰 공간 → 월드)
                float3 nV = normalize(float3(c * 0.9 + (n - 0.5) * 0.6, sqrt(saturate(1 - r2)) + 0.25));
                float3 camR = UNITY_MATRIX_V[0].xyz, camU = UNITY_MATRIX_V[1].xyz, camF = UNITY_MATRIX_V[2].xyz;
                float3 nW = normalize(camR * nV.x + camU * nV.y + camF * nV.z);

                Light  L   = GetMainLight();
                float  ndl = saturate(dot(nW, L.direction) * 0.5 + 0.5);
                float  shade = lerp(1, ndl * (0.6 + 0.4 * n), _Shade);
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float  fwd = pow(saturate(dot(-viewDir, L.direction)), 6) * (1 - dens) * 1.6;   // 은빛 테두리
                // 환경광 하한 — 하늘빛이 늘 연기 속까지 스며든다 (검은 실루엣 방지)
                float3 amb = max(SampleSH(nW), float3(0.48, 0.52, 0.58));
                float3 lit = amb + L.color * (shade * 0.7 + fwd);

                float3 col = _Color.rgb * IN.color.rgb * lit;

                // 불씨 — 젊은 연기의 두꺼운 안쪽에서만
                float ember = pow(saturate(1 - age / _EmberAge), 2) * saturate(dens * 1.5) * saturate(1 - r2 * 0.8) * (0.4 + n);
                col += _Ember.rgb * ember;

                float eye   = IN.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(IN.screen.xy / IN.screen.w), _ZBufferParams);
                float soft  = saturate((scene - eye) / max(0.01, _SoftDist));
                float nearF = saturate((eye - 0.5) / 3.0);

                return float4(col, saturate(dens * IN.color.a * _Opacity * soft * nearF));
            }
            ENDHLSL
        }
    }
    FallBack Off
}

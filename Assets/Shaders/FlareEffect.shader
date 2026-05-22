// 군용 플레어 (마그네슘 연소) 셰이더
// 구체 메시에 적용 – 법선·시선 내적으로 원형 원반 분포 계산
// Additive 블렌드로 HDR 발광 효과 연출
Shader "Custom/FlareEffect"
{
    Properties
    {
        _Age  ("Age (0-1)",    Range(0,1))    = 0
        _Seed ("Flicker Seed", Range(0,6.28)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent+20"
            "RenderPipeline" = "UniversalPipeline"
        }

        // ── Pass 1: 내부 핵 (Core + Halo) ──────────────────────────────────
        Pass
        {
            Name "FlareGlow"
            Blend One One      // Additive – 발광 느낌
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Age;
                float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 localPos    : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ── Noise ─────────────────────────────────────────────────────
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise2(float2 p)
            {
                float2 i = floor(p), f = frac(p), u = f*f*(3.0-2.0*f);
                return lerp(lerp(hash(i),              hash(i+float2(1,0)), u.x),
                            lerp(hash(i+float2(0,1)), hash(i+float2(1,1)), u.x), u.y);
            }
            float fbm2(float2 p)
            {
                return noise2(p)*0.5 + noise2(p*2.03+float2(1.7,9.2))*0.25
                     + noise2(p*4.01+float2(5.1,2.9))*0.125;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = vpi.positionCS;
                OUT.positionWS  = vpi.positionWS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.localPos    = IN.positionOS.xyz;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                // ── 방향 내적: 중심=1, 엣지=0 (원형 디스크 분포) ──────────
                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 n       = normalize(IN.normalWS);
                float  vdotn   = saturate(dot(viewDir, n));

                // ── 깜빡임 (시간 기반 FBM) ───────────────────────────────
                float  anim  = _Time.y * 7.0 + _Seed;
                float2 flkA  = IN.localPos.xy * 3.5 + float2(anim * 0.75, anim * 0.52);
                float2 spiUV = float2(atan2(IN.localPos.y, IN.localPos.x) * 0.7,
                                      IN.localPos.z * 2.1 + anim * 0.28);
                float  flk   = fbm2(flkA);
                float  spike = noise2(spiUV * 2.8) * 0.45 + 0.55;
                float  flicker = lerp(0.50, 1.05, flk) * spike;

                // ── 수명 페이드 ───────────────────────────────────────────
                float fadein  = smoothstep(0.0, 0.05, _Age);
                float fadeout = 1.0 - smoothstep(0.68, 1.0, _Age);
                float life    = fadein * fadeout;

                // ── 방사 레이어 ───────────────────────────────────────────
                float core  = pow(vdotn, 1.10);   // 백색 핵
                float inner = pow(vdotn, 0.40);   // 황색 내부 광
                float outer = pow(vdotn, 0.12);   // 주황 외부 광

                // ── 색상: 마그네슘 연소 (~5500 K 근사) ───────────────────
                float3 coreCol  = float3(1.00, 0.97, 0.90) * core  * 5.0;  // 백색
                float3 innerCol = float3(1.00, 0.85, 0.22) * inner * 2.0;  // 황백색
                float3 outerCol = float3(1.00, 0.42, 0.04) * outer * 0.7;  // 주황

                float3 col = (coreCol + innerCol + outerCol) * flicker * life;

                // ── 알파 (Additive이지만 거리 페이드 필요) ───────────────
                float a = inner * flicker * life;

                return float4(col, a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}

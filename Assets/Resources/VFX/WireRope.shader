// 어레스팅 와이어(크로스덱 펜던트) 강철 와이어로프 셰이더 — URP Forward 라이팅.
// 메쉬 규약: uv.x = 케이블 길이 방향 거리(m), uv.y = 둘레 0..1, tangent = 케이블 진행 방향.
//   · 나선 스트랜드: 둘레/길이 UV로 꼬인 가닥을 계산하고 법선을 기울여 굴곡을 표현
//   · Kajiya-Kay 이방성 하이라이트: 가닥 방향으로 길게 늘어지는 강선 특유의 반사
//   · 가닥 사이 홈 차폐(AO) + 그리스 얼룩으로 사용감
//   · 주광 그림자 수신 + 환경광(SH) + 반사 프로브
Shader "Custom/WireRope"
{
    Properties
    {
        _BaseColor   ("Steel Color", Color) = (0.50, 0.49, 0.47, 1)
        _GreaseColor ("Grease Color", Color) = (0.09, 0.08, 0.07, 1)
        _Grease      ("Grease Amount", Range(0, 1)) = 0.35
        _Smoothness  ("Smoothness", Range(0, 1)) = 0.62
        _Strands     ("Strand Count", Float) = 6
        _Lay         ("Lay (twists per m)", Float) = 7
        _Wires       ("Wire Detail Freq", Float) = 9
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "WireRopeForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _GreaseColor;
                float  _Grease;
                float  _Smoothness;
                float  _Strands;
                float  _Lay;
                float  _Wires;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 tangentWS  : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                float  fog        : TEXCOORD4;
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
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   n = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = n.normalWS;
                OUT.tangentWS  = n.tangentWS;
                OUT.uv         = IN.uv;
                OUT.fog        = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 T = normalize(IN.tangentWS - N * dot(IN.tangentWS, N));
                float3 B = cross(N, T);

                // ── 나선 스트랜드 ─────────────────────────────────────────────
                // 가닥 위상: 둘레(uv.y)와 길이(uv.x)를 함께 감아 꼬임을 만든다
                float phase  = IN.uv.y * _Strands + IN.uv.x * _Lay;
                float s      = frac(phase);
                float crown  = sin(s * PI);                         // 가닥 볼록면
                float slope  = cos(s * PI);                         // 가닥 경사 → 법선 기울기

                // 가닥 진행 방향(표면 위 나선) — 이방성 하이라이트 축
                float3 strandDir = normalize(T + B * (_Lay / max(_Strands, 1.0)) * 0.9);
                float3 across    = cross(N, strandDir);

                // 가닥 안의 개별 소선 무늬 (더 고주파, 반대 방향 꼬임)
                float wphase = IN.uv.y * _Strands * _Wires - IN.uv.x * _Lay * 2.3;
                float wire   = cos(frac(wphase) * PI);

                float3 Np = normalize(N + across * (slope * 0.75 + wire * 0.12));

                // 가닥 사이 홈 = 차폐 + 그리스 고임
                float groove = 1.0 - crown;
                float ao     = lerp(1.0, 0.35, pow(groove, 2.5));

                float grime  = saturate(noise1(IN.uv.x * 1.3 + floor(phase) * 3.7) * 1.4 - 0.4) * _Grease;
                float greasy = saturate(grime + pow(groove, 3.0) * _Grease);

                float3 albedo     = lerp(_BaseColor.rgb, _GreaseColor.rgb, greasy);
                float  smoothness = lerp(_Smoothness, 0.25, greasy * 0.8);

                // ── 라이팅 ───────────────────────────────────────────────────
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light  mainLight   = GetMainLight(shadowCoord);
                float3 V           = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float3 L           = mainLight.direction;
                float3 H           = normalize(L + V);
                float  atten       = mainLight.shadowAttenuation * mainLight.distanceAttenuation;

                float  NdotL   = saturate(dot(Np, L));
                float3 diffuse = albedo * 0.25 * NdotL;            // 금속이라 확산은 약하게

                // Kajiya-Kay: 가닥 방향 기준 이방성 반사 (강철 = 착색 반사)
                float  TdotH   = dot(strandDir, H);
                float  sinTH   = sqrt(saturate(1.0 - TdotH * TdotH));
                float  expo    = exp2(smoothness * 10.0 + 1.0);
                float  aniso   = pow(sinTH, expo) * smoothstep(-0.1, 0.4, dot(N, L));
                float3 spec    = lerp(albedo, 1.0.xxx, 0.25) * aniso * (smoothness * 1.6) * crown;

                float3 direct  = (diffuse + spec) * mainLight.color * atten;

                // 환경광 + 반사
                float3 ambient = SampleSH(Np) * albedo * 0.45;
                float  perceptualRough = 1.0 - smoothness;
                float3 R       = reflect(-V, Np);
                float3 envRefl = GlossyEnvironmentReflection(R, IN.positionWS, perceptualRough, 1.0);
                float  fres    = lerp(0.55, 1.0, pow(1.0 - saturate(dot(Np, V)), 4.0));
                float3 indirect = (ambient + envRefl * albedo * fres) * ao;

                float3 col = direct * lerp(0.6, 1.0, ao) + indirect;
                col = MixFog(col, IN.fog);
                return float4(col, 1.0);
            }
            ENDHLSL
        }

        // 깊이 프리패스 / SSAO용 깊이 — 케이블이 SSAO·DoF에서 빠지지 않도록
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex   depthVert
            #pragma fragment depthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _GreaseColor;
                float  _Grease;
                float  _Smoothness;
                float  _Strands;
                float  _Lay;
                float  _Wires;
            CBUFFER_END

            float4 depthVert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half   depthFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   dnVert
            #pragma fragment dnFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _GreaseColor;
                float  _Grease;
                float  _Smoothness;
                float  _Strands;
                float  _Lay;
                float  _Wires;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            V dnVert(A IN)
            {
                V OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }
            half4 dnFrag(V IN) : SV_Target { return half4(NormalizeNormalPerPixel(IN.normalWS), 0.0); }
            ENDHLSL
        }
    }
    FallBack Off
}

Shader "Custom/ExplosionEffect"
{
    Properties
    {
        _Age ("Age (0-1)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Age;
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
                float3 localPos    : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ── Noise ─────────────────────────────────────────────────────
            float hash(float3 p)
            {
                p  = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float valueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);

                float a  = hash(i);
                float b  = hash(i + float3(1,0,0));
                float c  = hash(i + float3(0,1,0));
                float d  = hash(i + float3(1,1,0));
                float e  = hash(i + float3(0,0,1));
                float f_ = hash(i + float3(1,0,1));
                float g  = hash(i + float3(0,1,1));
                float h  = hash(i + float3(1,1,1));

                return lerp(lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y),
                            lerp(lerp(e, f_, u.x), lerp(g, h, u.x), u.y), u.z);
            }

            float fbm(float3 p)
            {
                float v = 0.0;
                v += 0.5000 * valueNoise(p); p = p * 2.02 + float3(1.7, 9.2, 8.3);
                v += 0.2500 * valueNoise(p); p = p * 2.03 + float3(5.2, 2.7, 3.1);
                v += 0.1250 * valueNoise(p); p = p * 1.99 + float3(8.1, 4.3, 5.8);
                v += 0.0625 * valueNoise(p);
                return v;
            }

            // ── Vertex ────────────────────────────────────────────────────
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // 버텍스 변위: 초기에 강하게, 스모크 단계로 갈수록 감쇠
                float3 localN = normalize(IN.positionOS.xyz);
                float  dispStrength = max(0.0, 1.0 - _Age * 1.4);   // ~0→0.7 age 이후 0
                float  disp = fbm(localN * 2.5 + float3(0.3, -_Age * 1.8, _Age * 0.9));
                float3 displaced = IN.positionOS.xyz + localN * (disp * 0.40 * dispStrength);

                VertexPositionInputs vpi = GetVertexPositionInputs(displaced);
                OUT.positionHCS = vpi.positionCS;
                OUT.positionWS  = vpi.positionWS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.localPos    = IN.positionOS.xyz;
                return OUT;
            }

            // ── Fragment ──────────────────────────────────────────────────
            float4 frag(Varyings IN) : SV_Target
            {
                float age = _Age;

                // 뷰-법선 각도
                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 n       = normalize(IN.normalWS);
                float  vDotN   = saturate(dot(viewDir, n));

                // 애니메이션 FBM (위로 상승, 옆으로 팽창)
                float3 np = IN.localPos * 3.5 + float3(0.0, -age * 3.2, age * 1.6);
                float  f  = fbm(np);
                float  f2 = fbm(np * 0.5 + float3(3.7, 1.2, 8.1));
                float  mask = saturate(f * 1.55 - 0.12) * saturate(f2 * 1.6 - 0.08);

                // 페이즈 가중치
                float fireAmt  = 1.0 - smoothstep(0.0, 0.70, age);
                float smokeAmt = smoothstep(0.28, 0.58, age) * (1.0 - smoothstep(0.78, 1.0, age));

                // 화염 색상 램프: 백색 → 황색 → 주황 → 짙은 적색
                float  ct = saturate(age / 0.55);
                float3 c0 = float3(1.00, 1.00, 0.95);
                float3 c1 = float3(1.00, 0.82, 0.12);
                float3 c2 = float3(1.00, 0.30, 0.03);
                float3 c3 = float3(0.52, 0.07, 0.02);
                float3 fireCol;
                if      (ct < 0.33) fireCol = lerp(c0, c1, ct * 3.0);
                else if (ct < 0.66) fireCol = lerp(c1, c2, (ct - 0.33) * 3.0);
                else                fireCol = lerp(c2, c3, (ct - 0.66) * 3.0);

                float3 smokeCol = lerp(float3(0.30, 0.26, 0.22), float3(0.09, 0.08, 0.07), age);

                // 화염↔연기 혼합
                float total  = fireAmt + smokeAmt + 0.001;
                float3 col   = lerp(fireCol, smokeCol, smokeAmt / total);
                col *= 1.0 + fireAmt * mask * 1.5;   // 화염 발광 부스트

                // 알파
                float alpha = mask * (fireAmt * 1.8 + smokeAmt * 0.70);
                alpha *= lerp(0.60, 1.0, vDotN);     // 엣지 약간 투명
                alpha  = saturate(alpha);

                return float4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/InternalErrorShader"
}

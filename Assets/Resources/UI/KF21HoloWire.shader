Shader "KF21/HoloWire"
{
    // 홀로그램 와이어 셰이더 (URP, 가산 합성).
    // 기체 로컬 좌표의 격자 등고선 + 면 꺾임 외곽선 + 프레넬 림 + 스캔 밴드. 기수→후미 순으로 형태가 드러난다.
    Properties
    {
        _Color     ("Hologram Color", Color) = (0.20, 0.70, 1.00, 1)
        _Hot       ("Hot Color",      Color) = (0.80, 1.00, 1.00, 1)
        _Reveal    ("Reveal", Range(0, 1))   = 1
        _Intensity ("Intensity", Float)      = 1
        _Cell      ("Grid Cell (m)", Float)  = 0.30
        _Fill      ("Fill", Range(0, 1))     = 0.03
        _Range     ("Range along Z (min,max)", Vector) = (-9, 8.2, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Holo"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4  _Color;
                half4  _Hot;
                float  _Reveal;
                float  _Intensity;
                float  _Cell;
                float  _Fill;
                float4 _Range;
            CBUFFER_END
            float4x4 _KF_W2J;   // 월드 → 기체 로컬

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 frag(Varyings i, bool isFront : SV_IsFrontFace) : SV_Target
            {
                float3 p = mul(_KF_W2J, float4(i.positionWS, 1.0)).xyz;   // 기체 로컬(m)
                float t = saturate((p.z - _Range.x) / max(0.001, _Range.y - _Range.x));   // 0 후미 ~ 1 기수

                float th   = lerp(1.06, -0.06, _Reveal);
                float n    = hash21(floor(p.xy * 9.0) + floor(p.z * 9.0));
                float edge = t - th + (n - 0.5) * 0.02;
                clip(edge);
                float edgeGlow = saturate(1.0 - edge / 0.05) * step(_Reveal, 0.995);

                float3 nrm = normalize(i.normalWS);
                float3 vdir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float ndv = abs(dot(nrm, vdir));
                float rim = pow(1.0 - ndv, 2.4);

                // 격자 등고선 (3축)
                float3 q = p / _Cell;
                float3 g = abs(frac(q - 0.5) - 0.5) / max(fwidth(q), 1e-4);
                float line_ = saturate(1.0 - min(min(g.x, g.y), g.z));

                // 면 꺾임 외곽선
                float crease = saturate(length(fwidth(nrm)) * 14.0);

                float scan = pow(0.5 + 0.5 * sin(p.y * 6.0 + p.z * 2.0 - _Time.y * 2.2), 8.0);

                float side = isFront ? 1.0 : 0.45;
                float3 col = _Color.rgb * (_Fill + rim * 0.9 + line_ * 0.75 + scan * 0.22)
                           + _Hot.rgb * (crease * 0.55 + line_ * rim * 0.7 + edgeGlow * 1.6);
                col *= side * _Intensity;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}

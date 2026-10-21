Shader "KF21/Neon"
{
    // 3D 네온 (URP, 가산 합성). 정점 색 × _Color × 강도.
    // _Mode 0: 리본(폭 방향 u 소프트 프로파일) / 1: 방사형 글로우 / 2: 화염(길이 방향 v)
    Properties
    {
        _Color     ("Color", Color) = (0.3, 0.8, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Mode      ("Mode", Float) = 0
        _Sharp     ("Ribbon Sharpness", Float) = 5
        _Flame     ("Flame Amount", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Neon"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _Mode;
                float _Sharp;
                float _Flame;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv; o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float u = i.uv.x, w = i.uv.y;
                float3 c = _Color.rgb * i.color.rgb;
                if (_Mode > 2.5)
                {
                    // 평면 홀로그램 면: 정점 색 그대로 + 가로 스캔라인 일렁임
                    float sh = 0.82 + 0.18 * sin(w * 70.0 - _Time.y * 3.0) * sin(w * 13.0 + _Time.y * 0.7);
                    return half4(c * _Intensity * i.color.a * sh, 1);
                }
                float p;
                if (_Mode < 0.5)
                {
                    float k = 1.0 - abs(2.0 * u - 1.0);
                    p = pow(saturate(k), _Sharp) + 0.25 * pow(saturate(k), 1.2);
                    p *= smoothstep(0.0, 0.10, w) * smoothstep(1.0, 0.90, w);
                }
                else if (_Mode < 1.5)
                {
                    float r = length(float2(u, w) - 0.5) * 2.0;
                    p = pow(saturate(1.0 - r), 2.2);
                }
                else
                {
                    // 화염: w = 노즐(0) → 끝(1). 좁고 밝은 코어 + 주황 외염 + 충격 다이아몬드
                    float hw = lerp(0.85, 0.03, pow(w, 0.6));
                    float wob = sin(w * 28.0 - _Time.y * 45.0 + u * 6.0) * 0.06 + sin(w * 11.0 - _Time.y * 30.0) * 0.05;
                    float d = abs(2.0 * u - 1.0) / max(hw, 0.001) + wob * (0.4 + w);
                    float fade = pow(saturate(1.0 - w), 1.1);
                    float outer = pow(saturate(1.0 - d), 1.6) * fade;
                    float core = pow(saturate(1.0 - d / 0.38), 2.0) * saturate(1.0 - w * 1.4);
                    float diamond = 0.8 + 0.2 * sin(w * 55.0 - _Time.y * 50.0);
                    float3 col = float3(1.0, 0.38, 0.08) * outer * 0.9 * diamond
                               + float3(0.75, 0.88, 1.0) * core * 1.6 + float3(1.0, 0.85, 0.6) * core * 0.6;
                    col *= _Flame * _Intensity * i.color.a * 0.55;
                    return half4(col, 1);
                }
                return half4(c * p * _Intensity * i.color.a, 1);
            }
            ENDHLSL
        }
    }
}

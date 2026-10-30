// 열 아지랑이 굴절 — 엔진 배기·폭발 충격파 뒤로 보이는 배경을 흔든다.
// URP 불투명 텍스처(_CameraOpaqueTexture, PC_RPAsset 에서 켜져 있음)를 노이즈로 어긋나게 샘플링.
// 다른 투명 이펙트(화염·연기)를 덮지 않도록 투명 큐 앞쪽에서 그린다.
Shader "Custom/FXHeatHaze"
{
    Properties
    {
        _Strength ("Distortion Strength", Range(0, 0.1)) = 0.018
        _Scale    ("Noise Scale", Float) = 3
        _Speed    ("Scroll Speed", Float) = 2.5
        _SoftDist ("Soft Particle Distance", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "FXHeatHaze"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Strength, _Scale, _Speed, _SoftDist;
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
                float mask = pow(saturate(1 - length(c)), 1.5) * IN.color.a;

                float eye   = IN.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(IN.screen.xy / IN.screen.w), _ZBufferParams);
                mask *= saturate((scene - eye) / max(0.01, _SoftDist)) * saturate((eye - 0.3) / 1.5);

                float2 p = c * _Scale + IN.uv.zw * 17;
                float t = _Time.y * _Speed;
                float2 off = float2(vnoise(p + float2(0, t)), vnoise(p * 1.3 + float2(t * 0.7, 3.1))) - 0.5;

                // 화면 해상도와 거리 무관하게 일정한 체감 굴절 — 멀면 덜 흔들린다
                float2 uv = IN.screen.xy / IN.screen.w + off * _Strength * mask * saturate(30 / eye);
                float3 col = SampleSceneColor(uv);
                return float4(col, saturate(mask * 3));
            }
            ENDHLSL
        }
    }
    FallBack Off
}

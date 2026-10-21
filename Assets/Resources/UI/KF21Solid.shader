Shader "KF21/Solid"
{
    // 불투명 단색(정점 색) — 디지털 입구의 도어 블레이드용. 뒤쪽 가산 글로우를 가린다.
    Properties { _Color ("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Solid"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.color = v.color; return o; }
            half4 frag(Varyings i) : SV_Target { return half4(i.color.rgb * _Color.rgb, 1); }
            ENDHLSL
        }
    }
}

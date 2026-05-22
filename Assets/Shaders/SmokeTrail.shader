Shader "Custom/SmokeTrail"
{
    Properties
    {
        _Color      ("Smoke Color", Color)  = (0.75, 0.72, 0.70, 0.55)
        _NoiseScale ("Noise Scale",  Float) = 3.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _NoiseScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 texcoord   : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 color       : TEXCOORD1;
            };

            float hash2(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float noise2(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash2(i),              hash2(i + float2(1,0)), u.x),
                            lerp(hash2(i + float2(0,1)), hash2(i + float2(1,1)), u.x), u.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.texcoord;
                OUT.color       = IN.color;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                // Two octaves of noise for smoke turbulence
                float n1 = noise2(uv * _NoiseScale + float2(uv.x * 2.3, 0.0));
                float n2 = noise2(uv * _NoiseScale * 2.1 + float2(0.0, uv.x * 3.5));
                float n  = n1 * 0.60 + n2 * 0.40;

                // Cross-trail width fade (V: 0=left, 1=right, centre=0.5)
                float edge = 1.0 - abs(uv.y * 2.0 - 1.0);
                edge = pow(saturate(edge), 1.3);

                // Use vertex-colour alpha (driven by TrailRenderer.colorGradient)
                float trailAlpha = IN.color.a;

                float alpha = edge * trailAlpha * (0.50 + n * 0.50) * _Color.a;
                alpha = saturate(alpha);

                float3 col = _Color.rgb * lerp(0.75, 1.05, n) * IN.color.rgb;

                return float4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/InternalErrorShader"
}

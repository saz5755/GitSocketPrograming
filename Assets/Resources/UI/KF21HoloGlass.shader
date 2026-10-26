Shader "KF21/HoloGlass"
{
    // 홀로그램 글래스 셰이더 (URP) — 홀로그램디자인_03 레퍼런스.
    //  Pass 1 (SRPDefaultUnlit) : 어두운 틴트 유리 몸체. 깊이를 써서 뒤쪽 면/배경을 정리하고 부피감을 만든다.
    //  Pass 2 (UniversalForward): 가산 발광 —
    //     · 정밀 격자: 화면에서 촘촘해지는 곳은 스스로 사라져(밀도 페이드) 표면이 우윳빛으로 뜨지 않는다
    //     · 유리 하이라이트: 차가운 키 광 + 따뜻한 반사 — 매끈한 곡면을 따라 흐르는 광택
    //     · 실루엣 림/면 꺾임 외곽선: 가장자리만 또렷하게
    //     · 펄스: _PulsePeriod 마다 한 번, 기수 → 후미로 빛의 파동이 지나간다
    // _Reveal/_RevealDir: 기체 로컬 z 기준 표시 범위. Dir=+1 → 기수부터 드러남, Dir=-1 → 반대(솔리드화 스윕용 덮개).
    Properties
    {
        _Color       ("Line Color", Color)    = (0.16, 0.62, 1.00, 1)
        _Hot         ("Hot Color", Color)     = (0.75, 0.97, 1.00, 1)
        _Tint        ("Glass Tint", Color)    = (0.012, 0.05, 0.12, 1)
        _Opacity     ("Glass Opacity", Range(0, 1)) = 0.42
        _Reveal      ("Reveal", Range(0, 1))  = 1
        _RevealDir   ("Reveal Dir", Float)    = 1
        _Intensity   ("Intensity", Float)     = 1
        _Cell        ("Grid Cell (m)", Float) = 0.2
        _Fill        ("Fill", Range(0, 1))    = 0.01
        _PulsePeriod ("Pulse Period (s)", Float) = 5
        _Range       ("Range along Z (min,max)", Vector) = (-9, 8.2, 0, 0)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4  _Color;
        half4  _Hot;
        half4  _Tint;
        float  _Opacity;
        float  _Reveal;
        float  _RevealDir;
        float  _Intensity;
        float  _Cell;
        float  _Fill;
        float  _PulsePeriod;
        float4 _Range;
    CBUFFER_END
    float4x4 _KF_W2J;   // 월드 → 기체 로컬
    float4   _KF_JetCenter;   // 기체 중심 (월드) — 깊이 감쇠 기준

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

    // 표시 경계까지의 거리(양수 = 보임). 경계 근처는 밝은 띠로 빛난다.
    float RevealEdge(float3 p)
    {
        float t  = saturate((p.z - _Range.x) / max(0.001, _Range.y - _Range.x));   // 0 후미 ~ 1 기수
        float th = lerp(1.06, -0.06, _Reveal);
        float n  = hash21(floor(p.xy * 9.0) + floor(p.z * 9.0));
        return (t - th) * _RevealDir + (n - 0.5) * 0.012;
    }

    // 1px 내외의 안티에일리어스 격자선. 셀이 화면에서 너무 작아지면(fw 큼) 선 자체를 지운다.
    float GridLine(float3 q, float width)
    {
        float3 fw = max(fwidth(q), 1e-4);
        float3 g  = abs(frac(q - 0.5) - 0.5) / fw;
        float l   = saturate(1.0 - min(min(g.x, g.y), g.z) / width);
        float dens = max(max(fw.x, fw.y), fw.z);
        return l * saturate(1.0 - (dens - 0.32) / 0.4);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        // ── 1. 틴트 유리 몸체 ─────────────────────────────────────────────
        Pass
        {
            Name "GlassBody"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragBody

            half4 fragBody(Varyings i) : SV_Target
            {
                float3 p = mul(_KF_W2J, float4(i.positionWS, 1.0)).xyz;
                clip(RevealEdge(p));
                float3 nrm  = normalize(i.normalWS);
                float3 vdir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float ndv   = saturate(dot(nrm, vdir));
                // 유리 두께감: 정면(빛이 짧게 통과)은 투명, 비스듬한 가장자리(긴 경로)는 진하게 → 볼륨
                float fres = 1.0 - ndv;
                float a = _Opacity * lerp(0.28, 1.0, fres * fres * (3.0 - 2.0 * fres));
                // 입체 음영: 위를 향한 면은 밝은 하늘빛, 아래를 향한 면은 깊은 남색 (하늘광 + 바닥 그림자)
                float up = nrm.y * 0.5 + 0.5;
                float3 c = _Tint.rgb * lerp(0.9, 2.4, up * up);
                return half4(c, a);
            }
            ENDHLSL
        }

        // ── 2. 발광 라인 ─────────────────────────────────────────────────
        Pass
        {
            Name "HoloLines"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragLines

            half4 fragLines(Varyings i, bool isFront : SV_IsFrontFace) : SV_Target
            {
                float3 p = mul(_KF_W2J, float4(i.positionWS, 1.0)).xyz;
                float edge = RevealEdge(p);
                clip(edge);
                float edgeGlow = saturate(1.0 - edge / 0.035) * step(_Reveal, 0.995);

                float3 nrm  = normalize(i.normalWS);
                float3 vdir = normalize(GetWorldSpaceViewDir(i.positionWS));
                // 모델 일부 파츠의 정점 법선이 깨져 있어(면과 수직이 아님) 림이 면 전체를 하얗게 칠하는 문제 →
                // 화면 미분으로 얻은 실제 면 법선과 크게 어긋나면 면 법선을 쓴다.
                float3 geo  = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                if (abs(dot(nrm, geo)) < 0.6) nrm = geo * sign(dot(geo, vdir) + 1e-4);
                float ndv   = abs(dot(nrm, vdir));
                float rim   = pow(1.0 - ndv, 5.0);
                float rimHi = pow(1.0 - ndv, 16.0);   // 실루엣 바로 가장자리만

                // 정밀 격자 (보조/주선) — 밀도 페이드로 깔끔하게
                float3 q     = p / _Cell;
                float minor  = GridLine(q, 0.9);
                float major  = GridLine(q * 0.25, 1.1);

                // 면 꺾임 외곽선
                // 면 꺾임: 고밀도 메쉬의 미세한 법선 변화는 무시하고 실제 패널 경계/실루엣만 (임계값)
                float crease = saturate((length(fwidth(nrm)) - 0.12) * 6.0);

                // 유리 하이라이트: 위쪽 차가운 키 광 + 측후방 따뜻한 반사
                float3 R    = reflect(-vdir, nrm);
                float keyS  = pow(saturate(dot(R, normalize(float3(-0.35, 0.8, 0.45)))), 55.0);
                float warmS = pow(saturate(dot(R, normalize(float3(0.7, 0.25, -0.65)))), 30.0);
                float sheen = pow(saturate(dot(R, float3(0, 1, 0)) * 0.5 + 0.5), 8.0) * 0.04;

                // 펄스: 주기마다 한 번, 기수 → 후미로 지나가는 빛의 파동 (주기의 45% 동안 진행, 나머지는 휴지)
                float ph   = frac(_Time.y / max(_PulsePeriod, 0.1));
                float span = _Range.y - _Range.x;
                float wz   = _Range.y + 0.8 - saturate(ph / 0.45) * (span + 1.6);
                float dz   = (p.z - wz) / 0.6;
                float wave = exp(-dz * dz) * step(ph, 0.45);

                float face = lerp(0.8, 0.95, 1.0 - ndv);   // 정면 곡면(기수)도 몸통만큼 격자/광택이 보이게
                // 키 라이트(좌상단 앞) 방향 확산광 — 면마다 밝기 차이로 입체감
                float lambert = saturate(dot(nrm, normalize(float3(-0.4, 0.75, 0.5)))) ;
                float shade = lerp(0.45, 1.25, lambert);
                float side = isFront ? 1.0 : 0.07;   // 뒷면 격자가 앞면과 겹쳐 형태를 납작하게 만들지 않게
                // 깊이 감쇠: 기체 중심보다 카메라에서 먼 쪽은 흐리게, 가까운 쪽은 밝게 → 앞뒤가 읽힌다
                float3 toC  = normalize(_KF_JetCenter.xyz - _WorldSpaceCameraPos);
                float depth = dot(i.positionWS - _KF_JetCenter.xyz, toC);       // + 먼 쪽, - 가까운 쪽 (m)
                side *= lerp(1.25, 0.45, saturate(depth / 9.0 * 0.5 + 0.5));

                float3 col = _Color.rgb * ((_Fill + 0.085 + (minor * 0.17 + major * 0.52) * face) * shade + lambert * 0.07 + rim * 0.3 + crease * 0.22 + sheen)
                           + _Hot.rgb   * (rimHi * 0.8 + crease * 0.1 + edgeGlow * 2.2 + keyS * 0.35)
                           + float3(0.7, 0.85, 1.0) * warmS * 0.25

                           + _Hot.rgb   * wave * (0.12 + major * 1.3 + minor * 0.6 + rim * 0.6);
                col *= side * _Intensity;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}

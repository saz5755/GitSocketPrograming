// 볼류메트릭 구름 레이어 — 전체 화면 레이마칭 (URP, 불투명 이후). 언리얼 Volumetric Cloud 와 같은 방식의 조명 모델.
//  · 형태: 3D 노이즈(Perlin-Worley + Worley 3옥타브) 로 적운 덩어리, 고주파 침식으로 가장자리 솜털
//  · 커버리지: 저주파 슬라이스로 맑은 하늘 사이사이 구름 군집
//  · 조명: Beer–Lambert 투과 + 다중 산란 근사(Wrenninge/Hillaire 옥타브: 감쇠·산란·이방성을 옥타브마다 줄여 합산)
//          → 속까지 빛이 번진 부드러운 솜 질감, 이중 HG 위상(태양 쪽 은빛 테두리), 고도별 하늘 환경광(아랫면 회청색)
//  · 스텝: 거리에 비례해 길어지고 빈 하늘은 성큼 건너뛰어, 같은 비용으로 구름 안을 촘촘히 샘플 (입자감 감소)
//  · 장면 깊이로 레이를 잘라 비행기/항모/바다가 구름 앞뒤로 올바르게 겹친다
//  · 먼 구름은 투과율 가중 거리로 수평선 연무색에 녹아든다 (공기 원근)
Shader "Custom/CloudVolume"
{
    Properties
    {
        _Noise3D    ("Noise 3D", 3D) = "" {}
        _CloudBase  ("Base Altitude (m)", Float) = 650
        _CloudTop   ("Top Altitude (m)", Float) = 1150
        _Coverage   ("Coverage", Range(0,1)) = 0.45
        _Extinction ("Extinction (1/m)", Float) = 0.045
        _ShapeScale ("Shape Scale (m per tile)", Float) = 1400
        _ClusterScale ("Cluster Scale (m)", Float) = 2900
        _DetailScale("Detail Scale (m per tile)", Float) = 260
        _Erosion    ("Erosion", Range(0,1)) = 0.45
        _Wind       ("Wind (m/s xz)", Vector) = (9, 0, 4, 0)
        _SunIntensity ("Sun Intensity", Float) = 1
        _AmbientIntensity ("Ambient Intensity", Float) = 1
        _MultiScatter ("Multi Scattering", Range(0,1)) = 0.6
        _SilverLining ("Silver Lining (forward g)", Range(0,0.95)) = 0.75
        _BaseDarkening ("Base Darkening", Range(0,1)) = 0.45
        _CloudTint  ("Cloud Tint", Color) = (1, 1, 1, 1)
        _HazeColor  ("Horizon Haze", Color) = (0.66, 0.75, 0.82, 1)
        _HazeDistance ("Haze Distance (m)", Float) = 9000
        _MaxDist    ("Max Distance (m)", Float) = 16000
        _MaxSteps   ("Max Steps", Int) = 112
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-200" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "CloudVolume"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE3D(_Noise3D); SAMPLER(sampler_Noise3D);

            CBUFFER_START(UnityPerMaterial)
                float _CloudBase, _CloudTop, _Coverage, _Extinction, _ShapeScale, _DetailScale, _MaxDist, _ClusterScale, _Erosion;
                float4 _Wind;
                float _SunIntensity, _AmbientIntensity, _MultiScatter, _SilverLining, _BaseDarkening, _HazeDistance;
                half4 _CloudTint, _HazeColor;
                int _MaxSteps;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 screen : TEXCOORD0; };

            // 메쉬는 클립 공간 전체 화면 삼각형 (CloudLayer)
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = float4(v.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1.0);
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            float CRemap(float v, float lo, float hi, float nlo, float nhi) { return nlo + (v - lo) / max(hi - lo, 1e-4) * (nhi - nlo); }

            float Density(float3 p, float lod, bool detailed)
            {
                float h = saturate((p.y - _CloudBase) / (_CloudTop - _CloudBase));
                if (h <= 0.0 || h >= 1.0) return 0.0;
                float3 wp = p + float3(_Wind.x, 0, _Wind.z) * _Time.y;
                // 커버리지: 군집 × 대규모 변화 → 크기·간격이 제각각인 구름 무리 (격자처럼 반복되지 않게)
                float4 wc = SAMPLE_TEXTURE3D_LOD(_Noise3D, sampler_Noise3D, float3(wp.x / _ClusterScale, 0.37, wp.z / _ClusterScale), 0);
                float4 wl = SAMPLE_TEXTURE3D_LOD(_Noise3D, sampler_Noise3D, float3(wp.x / (_ClusterScale * 3.8), 0.81, wp.z / (_ClusterScale * 3.8)), 0);
                float field = wc.r * 0.65 + wc.g * 0.35 + (wl.r - 0.5) * 0.6;
                float cov = smoothstep(1.0 - _Coverage, 1.0 - _Coverage + 0.4, field);
                if (cov <= 0.001) return 0.0;
                // 높이 프로파일: 평평한 바닥 + 층 전체에 걸친 완만한 둥근 상단
                float grad = smoothstep(0.0, 0.07, h) * smoothstep(1.0, 0.55, h) * lerp(0.75, 1.0, smoothstep(0.0, 0.4, h));
                // 세로로 압축된 노이즈 → 옆으로 넓게 퍼진 뭉게구름 (기둥 모양 방지)
                // 크기가 다른 두 스케일(회전)을 섞어 같은 덩어리가 타일처럼 반복되지 않게
                float4 n = SAMPLE_TEXTURE3D_LOD(_Noise3D, sampler_Noise3D, float3(wp.x, wp.y * 2.2, wp.z) / _ShapeScale, lod);
                float3 wr = float3(wp.x * 0.8 - wp.z * 0.6, wp.y * 1.6, wp.x * 0.6 + wp.z * 0.8);
                float4 n2 = SAMPLE_TEXTURE3D_LOD(_Noise3D, sampler_Noise3D, wr / (_ShapeScale * 2.43) + 0.41, lod);
                float fbmW = n.g * 0.625 + n.b * 0.25 + n.a * 0.125;
                float shape = saturate(CRemap(n.r, fbmW - 1.0, 1.0, 0.0, 1.0));
                float shape2 = saturate(CRemap(n2.r, n2.g * 0.7 + n2.b * 0.3 - 1.0, 1.0, 0.0, 1.0));
                shape = saturate(shape * 0.62 + shape2 * 0.5 - 0.06);
                float d = saturate(CRemap(shape * grad, 1.0 - cov * 0.85, 1.0, 0.0, 1.0));
                if (d <= 0.0 || !detailed) return d;
                // 고주파 침식 — 아래쪽은 솜털(wispy), 위쪽은 뭉게뭉게(billowy)
                float4 dn = SAMPLE_TEXTURE3D_LOD(_Noise3D, sampler_Noise3D, wp / _DetailScale + float3(0.31, 0.17, 0.53), lod);
                float detail = dn.g * 0.6 + dn.b * 0.3 + dn.a * 0.1;
                detail = lerp(detail, 1.0 - detail, saturate(h * 4.0));
                return saturate(CRemap(d, detail * _Erosion, 1.0, 0.0, 1.0));
            }

            float HG(float c, float g) { float g2 = g * g; return (1.0 - g2) / pow(max(1.0 + g2 - 2.0 * g * c, 1e-4), 1.5); }   // ×4π 정규화 (등방 = 1)

            // 전방(은빛 테두리) + 약한 후방 산란
            float Phase(float c, float k) { return lerp(HG(c, -0.2 * k), HG(c, _SilverLining * k), 0.62); }

            float StepAt(float t) { return clamp(t * 0.008, 16.0, 120.0); }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                float3 ro = _WorldSpaceCameraPos;
                float3 farWS = ComputeWorldSpacePosition(uv, 0.5, UNITY_MATRIX_I_VP);
                float3 rd = normalize(farWS - ro);

                // 장면 깊이: 스카이(최원)면 제한 없음. 원거리 클립 밖이라 깊이가 눌린 바다도 스카이 취급
                float raw = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool sky = raw <= 1e-5;
                #else
                    bool sky = raw >= 1.0 - 1e-5;
                #endif
                float tScene = 1e9;
                if (!sky)
                {
                    float3 sp = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                    tScene = distance(sp, ro);
                }

                // 구름 층과의 교차 구간
                float t0, t1;
                if (abs(rd.y) < 1e-4)
                {
                    if (ro.y < _CloudBase || ro.y > _CloudTop) return 0;
                    t0 = 0; t1 = _MaxDist;
                }
                else
                {
                    float a = (_CloudBase - ro.y) / rd.y, b = (_CloudTop - ro.y) / rd.y;
                    t0 = max(min(a, b), 0.0); t1 = max(a, b);
                }
                t1 = min(min(t1, tScene), _MaxDist);
                if (t1 <= t0) return 0;

                Light sun = GetMainLight();
                float3 L = sun.direction;
                float cosT = dot(rd, L);
                float3 sunCol = sun.color * _SunIntensity;
                // 하늘 환경광: 씬 SH 는 회갈색으로 치우칠 수 있어 수평선 하늘색과 섞는다 (구름 그늘이 푸른 회색이 되도록)
                float3 skyUp = (max(SampleSH(float3(0, 1, 0)), 0.02) * 0.5 + _HazeColor.rgb * 0.55) * _AmbientIntensity;

                // 화면 고정 지터 (해시) — 스텝 경계의 계단 무늬를 잡음으로 바꾼다. 구름 안 스텝이 짧아 잡음도 작다
                float jit = frac(sin(dot(i.positionCS.xy, float2(12.9898, 78.233))) * 43758.5453);
                float t = t0 + StepAt(t0) * jit;
                float T = 1.0;
                float3 acc = 0;
                float wSum = 0, dSum = 0;
                int empty = 0;

                [loop]
                for (int s = 0; s < _MaxSteps; s++)
                {
                    if (t >= t1) break;
                    float st = StepAt(t);
                    float3 p = ro + rd * t;
                    float lod = saturate(t / 7000.0) * 2.0;
                    float d = Density(p, lod, true);
                    if (d > 0.003)
                    {
                        empty = 0;
                        float ext = d * _Extinction;

                        // 태양 방향 광학 깊이 (6 스텝, 점점 길게 ≈ 650 m)
                        float od = 0, ls = 25.0;
                        float3 lp = p;
                        [unroll] for (int k = 0; k < 6; k++) { lp += L * ls; od += Density(lp, lod + 1.0, k < 3) * ls; ls *= 1.6; }
                        od *= _Extinction;

                        // 다중 산란 옥타브: 깊이 들어갈수록 감쇠↓ 산란↓ 이방성↓
                        float3 sunS = 0;
                        float aK = 1.0, bK = 1.0, cK = 1.0;
                        [unroll] for (int o = 0; o < 3; o++)
                        {
                            sunS += bK * Phase(cosT, cK) * exp(-od * aK);
                            aK *= 0.35; bK *= _MultiScatter * 0.75; cK *= 0.5;
                        }

                        float h = saturate((p.y - _CloudBase) / (_CloudTop - _CloudBase));
                        // 환경광: 위는 하늘빛, 아래는 어둡게 (바닥 반사광 약간) — 구름 위쪽 밀도가 클수록 더 가려진다
                        float3 amb = skyUp * lerp(1.0 - _BaseDarkening, 1.0, pow(h, 0.6));
                        float3 S = (sunCol * sunS + amb) * _CloudTint.rgb;

                        float dT = exp(-ext * st);
                        float w = T * (1.0 - dT);
                        acc += S * w;
                        wSum += w; dSum += w * t;
                        T *= dT;
                        if (T < 0.005) break;
                        t += st;
                    }
                    else
                    {
                        // 빈 공간은 성큼 (연속으로 비면 더 길게)
                        empty++;
                        t += st * (empty > 2 ? 2.5 : 1.0);
                    }
                }

                float alpha = 1.0 - T;
                if (alpha <= 0.002) return 0;
                // 공기 원근: 투과율 가중 평균 거리로 연무색에 녹아들고, 최대 거리 근처에서 사라진다
                float dist = dSum / max(wSum, 1e-5);
                float haze = 1.0 - exp(-max(dist - 1500.0, 0.0) / _HazeDistance);
                float3 col = lerp(acc, _HazeColor.rgb * alpha, haze);
                float fadeFar = 1.0 - smoothstep(_MaxDist * 0.7, _MaxDist, dist);
                return half4(col * fadeFar, alpha * fadeFar);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

// 바다 표면 — URP Forward.
//  · 격자는 카메라 기준(셰이더에서 위치 계산)이라 게임/씬 뷰 어느 카메라에서든 같은 메쉬 하나로 그린다
//  · 정점: Gerstner 너울 6개. 각 파는 그 위치의 격자 간격으로 표현 가능한 만큼만 변위 (격자보다 짧은 파를 정점에 넣으면
//          삼각형 면이 드러나 그물망처럼 보인다)
//  · 픽셀: 너울·잔물결·항적 법선을 전부 픽셀 단위로 계산. 화면 픽셀보다 촘촘한 파는 걸러내고 그 기울기 분산을
//          거칠기로 옮겨 원거리에서도 반짝이는 격자 무늬 없이 자연스럽게 흐려진다
//  · 프레넬 하늘 반사(해석적 하늘), GGX 태양 글린트, 마루의 청록 투과광, 바람 얼룩(거친 해면 패치), 흰 포말
//  · 항모 항적: Kelvin 쐐기(19.47°) 발산파 + 횡파, 선미 프로펠러 백파(난류 포말), 선체 주변 선수파 포말
//  · 원거리: 수평선 연무로 녹아들고, 카메라 원거리 클립 밖 정점은 깊이를 클립 안쪽으로 눌러 에디터 카메라에서도 수평선까지 보인다
Shader "Custom/Ocean"
{
    Properties
    {
        _DeepColor    ("Deep Color", Color)    = (0.012, 0.045, 0.085, 1)
        _ShallowColor ("Crest Scatter", Color) = (0.030, 0.165, 0.190, 1)
        _FoamColor    ("Foam Color", Color)    = (0.92, 0.95, 0.97, 1)
        _HazeColor    ("Horizon Haze", Color)  = (0.62, 0.72, 0.80, 1)
        _ZenithColor  ("Sky Zenith", Color)    = (0.24, 0.42, 0.68, 1)
        _HazeStart    ("Haze Start (m)", Float) = 600
        _HazeEnd      ("Haze End (m)", Float)   = 14000
        _WaveScale    ("Wave Height Scale", Float) = 1
        _Choppiness   ("Choppiness", Float) = 1
        _RippleStrength ("Ripple Strength", Float) = 1
        _WindPatches  ("Wind Patches", Float) = 0.6
        _FoamAmount   ("Crest Foam", Float) = 1

        _SeaLevel     ("Sea Level", Float) = -11
        _GridSnap     ("Grid Snap (m)", Float) = 4
        _GridK        ("Grid Spacing / Radius", Float) = 0.026

        _ShipPos      ("Ship Stern (world)", Vector) = (0, 0, 94, 0)
        _ShipFwd      ("Ship Forward (xz)", Vector)  = (0, 0, -1, 0)
        _ShipSpeed    ("Ship Speed (m/s)", Float)    = 12
        _ShipHalf     ("Ship Half Size (len, beam)", Vector) = (94, 13, 0, 0)
        _WakeLength   ("Wake Length (m)", Float)     = 650
        _WakeStrength ("Wake Strength", Float)       = 1
        _OuterRadius  ("Grid Outer Radius", Float)   = 18000
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "OceanForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4  _DeepColor, _ShallowColor, _FoamColor, _HazeColor, _ZenithColor;
                float  _HazeStart, _HazeEnd, _WaveScale, _Choppiness, _RippleStrength, _WindPatches, _FoamAmount;
                float  _SeaLevel, _GridSnap, _GridK;
                float4 _ShipPos, _ShipFwd, _ShipHalf;
                float  _ShipSpeed, _WakeLength, _WakeStrength, _OuterRadius;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 baseXZ     : TEXCOORD1;   // 변위 전 수면 좌표 — 픽셀 법선을 여기서 계산 (삼각형 안에서 선형이라 정확)
            };

            // ── Gerstner ─────────────────────────────────────────────────────
            // dir / wavelength / steepness / amplitude / 필터(0~1: 표현 불가능한 파는 0)
            void Gerstner(float2 p, float2 dir, float L, float Q, float A, float t, float f,
                          inout float3 disp, inout float3 T, inout float3 B)
            {
                A *= f; Q *= f * _Choppiness;
                float k = 2.0 * PI / L;
                float c = sqrt(9.81 / k);
                float2 d = normalize(dir);
                float ph = k * (dot(d, p) - c * t);
                float s, co; sincos(ph, s, co);
                float qa = Q / k;
                disp += float3(d.x * qa * co, A * s, d.y * qa * co);
                T += float3(-d.x * d.x * Q * s, d.x * A * k * co, -d.x * d.y * Q * s);
                B += float3(-d.x * d.y * Q * s, d.y * A * k * co, -d.y * d.y * Q * s);
            }

            // 파장 L 을 간격 s 로 표현할 수 있는 정도: L ≤ n·s 이면 0, L ≥ 2n·s 이면 1
            float WF(float L, float s, float n) { return saturate(L / (s * n) - 1.0); }

            // 걸러낸 파의 기울기 분산 (거칠기로 옮겨 밝기/반사 에너지를 보존)
            float LostVar(float L, float A, float f) { float sl = A * 2.0 * PI / L; return (1.0 - f * f) * 0.5 * sl * sl; }

            // 물결 패턴이 함정 진행 반대 방향으로 흘러가도록 샘플 좌표를 이동 (항모 정지 + 해수 이동 = 항해)
            float2 FlowXZ(float2 xz) { return xz + _ShipFwd.xz * _ShipSpeed * _Time.y; }

            #define SWELL(X) \
                X(float2( 1.0,  0.31), 48.0, 0.22, 0.42) \
                X(float2( 0.83, -0.57), 31.0, 0.20, 0.27) \
                X(float2( 0.27,  1.0 ), 19.5, 0.20, 0.16) \
                X(float2(-0.61,  0.79), 12.7, 0.18, 0.09) \
                X(float2( 0.97, -0.13),  8.3, 0.16, 0.05) \
                X(float2(-0.23, -0.97),  5.9, 0.14, 0.03)

            // 바람 물결: 방향을 넓게 흩어 같은 방향 줄무늬가 겹쳐 보이지 않게. 더 짧은 잔물결은 비주기 노이즈(NoiseSlope)가 담당
            #define RIPPLE(X) \
                X(float2( 0.99,  0.11), 7.37, 0.22, 0.026) \
                X(float2( 0.62,  0.78), 5.13, 0.25, 0.020) \
                X(float2(-0.82,  0.57), 4.43, 0.20, 0.016) \
                X(float2( 0.35, -0.94), 3.71, 0.25, 0.014) \
                X(float2( 0.88, -0.47), 2.87, 0.22, 0.010)

            // ── 노이즈 ───────────────────────────────────────────────────────
            float hash12(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p), u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash12(i), hash12(i + float2(1,0)), u.x), lerp(hash12(i + float2(0,1)), hash12(i + float2(1,1)), u.x), u.y);
            }
            // 비주기 잔물결: 회전한 값 노이즈 두 겹의 기울기 (주기 파의 반복 무늬를 깨뜨린다)
            float NoiseH(float2 q) { return vnoise(q) * 0.62 + vnoise(float2(q.x * 0.8 - q.y * 0.6, q.x * 0.6 + q.y * 0.8) * 2.31 + 5.2) * 0.38; }
            float2 NoiseSlope(float2 p, float scale, float2 flow, float rot, float t)
            {
                float s, c; sincos(rot, s, c);
                float2 q = float2(c * p.x - s * p.y, s * p.x + c * p.y) / scale + flow * t;
                const float e = 0.07;
                float h = NoiseH(q);
                float2 g = float2(NoiseH(q + float2(e, 0)) - h, NoiseH(q + float2(0, e)) - h) / e;
                return float2(c * g.x + s * g.y, -s * g.x + c * g.y) / scale;   // 월드 기울기 (높이 1 기준)
            }
            float fbm(float2 p) { return vnoise(p) * 0.5 + vnoise(p * 2.07 + 13.1) * 0.27 + vnoise(p * 4.13 + 7.7) * 0.14 + vnoise(p * 8.3 + 3.1) * 0.09; }

            // 파군(wave group) 포락선: 파마다 파장의 수 배 크기로 진폭이 들쭉날쭉 — 몇 개의 사인파 합이 만드는 규칙적인 격자 간섭무늬를 깨뜨린다
            float Env(float2 p, float L) { return 0.4 + 1.2 * vnoise(p / (L * 5.3) + L * 1.37); }

            // ── 항적 ─────────────────────────────────────────────────────────
            // 함정 기준 좌표: along = 선미 뒤쪽 거리(+), lat = 좌우 거리
            void ShipLocal(float2 xz, out float along, out float lat)
            {
                float2 f = normalize(_ShipFwd.xz);
                float2 r = float2(f.y, -f.x);
                float2 rel = xz - _ShipPos.xz;
                along = -dot(rel, f);
                lat = dot(rel, r);
            }

            // 항적 높이(물결) + 포말량
            float WakeHeight(float2 xz, out float foam)
            {
                float along, lat; ShipLocal(xz, along, lat);
                float ay = abs(lat);
                float t = _Time.y;
                foam = 0;
                float h = 0;
                if (along > -2.0 * _ShipHalf.x && along < _WakeLength)
                {
                    float aw = max(along, 0.0);
                    float fade = saturate(1.0 - aw / _WakeLength);
                    fade *= fade;

                    // Kelvin 쐐기: 각도 19.47° 근처의 발산파 팔 + 쐐기 안쪽 횡파
                    float ang = degrees(atan2(ay, aw + 1.0));
                    float arm = exp(-pow((ang - 19.47) / 3.2, 2.0)) * smoothstep(0.0, 25.0, aw);
                    float inside = smoothstep(21.0, 17.0, ang) * smoothstep(0.0, 30.0, aw);
                    float divergent = sin(0.32 * (aw * 0.82 + ay * 1.9) - t * 1.6);
                    float lambdaT = 2.0 * PI * _ShipSpeed * _ShipSpeed / 9.81;   // 횡파 파장 = 2πv²/g
                    float transverse = sin(2.0 * PI * aw / lambdaT - t * 0.2);
                    h += (arm * divergent * 0.32 + inside * transverse * 0.12) * fade;

                    // 프로펠러 백파: 선미 중앙에서 퍼지는 난류 포말 띠 (폭이 √거리로 벌어짐)
                    float width = 4.5 + sqrt(aw) * 0.75;
                    float center = exp(-pow(ay / width, 2.0)) * smoothstep(-6.0, 4.0, along);
                    // 길게 늘어진 난류 줄무늬 (진행 방향으로 늘인 노이즈)
                    float churn = fbm(float2(aw * 0.035 - t * 0.25, lat * 0.35));
                    float washFade = saturate(1.0 - aw / (_WakeLength * 0.6));
                    washFade *= washFade;
                    foam += center * washFade * smoothstep(0.42, 0.78, churn + washFade * 0.18) * 0.85;

                    // Kelvin 팔의 포말 줄무늬 (바깥쪽으로 갈수록 옅게)
                    foam += arm * saturate(divergent) * 0.3 * fade * smoothstep(0.45, 0.8, fbm(float2(aw, lat) * 0.08 + t * 0.05));
                }

                // 선체 주변 선수파 포말 (타원 경계 바깥 수 m)
                float2 f = normalize(_ShipFwd.xz); float2 r = float2(f.y, -f.x);
                float2 rel = xz - (_ShipPos.xz + f * _ShipHalf.x);     // 선체 중심
                float2 ell = float2(dot(rel, f) / _ShipHalf.x, dot(rel, r) / _ShipHalf.y);
                float e = length(ell);
                float bow = saturate(dot(rel, f) / _ShipHalf.x * 0.5 + 0.5);          // 선수 쪽일수록 강함
                float hull = exp(-pow((e - 1.0) * _ShipHalf.y / 4.5, 2.0)) * step(0.85, e);
                foam += hull * lerp(0.35, 1.0, bow) * smoothstep(0.3, 0.65, fbm(xz * 0.25 + float2(0, t * 1.5)));
                h += hull * 0.25 * bow;
                foam *= _WakeStrength;
                return h * _WakeStrength;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                // 카메라를 따라가는 격자 (스냅으로 미세한 흔들림 방지). 오브젝트 트랜스폼은 쓰지 않는다
                float2 center = round(_WorldSpaceCameraPos.xz / _GridSnap) * _GridSnap;
                float2 xz = center + v.positionOS.xz;
                float gridR = length(v.positionOS.xz);
                float spacing = max(gridR * _GridK, 0.05);

                float2 p = FlowXZ(xz);
                float t = _Time.y;
                float3 d = 0, T = 0, B = 0;
                #define VTX_WAVE(dir, L, Q, A) Gerstner(p, dir, L, Q, A * _WaveScale * Env(p, L), t, WF(L, spacing, 3.0), d, T, B);
                SWELL(VTX_WAVE)

                // 원거리는 변위를 줄여 수평선이 출렁이지 않게
                float dist = distance(xz, _WorldSpaceCameraPos.xz);
                float lod = saturate(1.0 - (dist - 600.0) / 1400.0);
                float wf; float wh = WakeHeight(xz, wf) * WF(14.0, spacing, 3.0);
                float3 ws = float3(xz.x, _SeaLevel, xz.y) + d * lod + float3(0, wh * lod, 0);
                // 바깥 스커트: 맨 끝 고리를 눈높이까지 올려 수평선(0°)에 정확히 닿게 — 스카이박스 지면이 보이지 않는다
                if (gridR > _OuterRadius * 0.995) ws.y = max(ws.y, _WorldSpaceCameraPos.y);

                o.positionWS = ws;
                o.baseXZ = xz;
                o.positionCS = TransformWorldToHClip(ws);
                // 원거리 클립 밖 정점은 깊이를 클립 안쪽으로 눌러 잘리지 않게 (바다는 가장 먼 물체라 앞뒤 관계가 깨지지 않는다)
                #if UNITY_REVERSED_Z
                    o.positionCS.z = max(o.positionCS.z, o.positionCS.w * 2e-6);
                #else
                    o.positionCS.z = min(o.positionCS.z, o.positionCS.w * (1.0 - 2e-6));
                #endif
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float dist = distance(i.positionWS, _WorldSpaceCameraPos);
                float2 xz = i.baseXZ;
                float2 p = FlowXZ(xz);
                float t = _Time.y;

                // 픽셀 하나가 덮는 수면 크기 — 이보다 짧은 파는 걸러낸다 (모아레·반짝이 격자 방지)
                float2 fw = fwidth(xz);
                float fp = max(max(fw.x, fw.y), 1e-3);

                // 바람 얼룩: 수백 m 단위로 잔물결 세기가 달라지는 거친/잔잔한 패치 (실제 해면의 고양이 발자국)
                float patch = fbm(p * 0.0045 + float2(t * 0.004, 0));
                float rough = lerp(1.0, smoothstep(0.25, 0.75, patch) * 1.5 + 0.25, _WindPatches);

                float3 d = 0, T = float3(1, 0, 0), B = float3(0, 0, 1);
                float var = 0;
                #define PIX_WAVE(dir, L, Q, A) { float f_ = WF(L, fp, 2.0); float e_ = Env(p, L); Gerstner(p, dir, L, Q, A * _WaveScale * e_, t, f_, d, T, B); var += LostVar(L, A * _WaveScale * e_, f_); }
                SWELL(PIX_WAVE)
                float crest = saturate(d.y / max(0.7 * _WaveScale, 0.01) * 0.5 + 0.5);
                float rs = _RippleStrength * rough;
                #define RIP_WAVE(dir, L, Q, A) { float f_ = WF(L, fp, 2.0); float e_ = Env(p, L); Gerstner(p, dir, L, Q * 0.35, A * rs * e_, t, f_, d, T, B); var += LostVar(L, A * rs * e_, f_); }
                RIPPLE(RIP_WAVE)
                float3 N = normalize(cross(B, T));

                // 비주기 잔물결 3겹 (각 층은 화면에서 표현 가능할 때만, 걸러진 만큼 거칠기로)
                float2 slope = 0;
                #define NOISE_LAYER(S, A, FL, R) { float f_ = WF(S * 1.5, fp, 2.0); float a_ = A * S * rs; slope += NoiseSlope(p, S, FL, R, t) * a_ * f_; var += (1.0 - f_ * f_) * 0.35 * a_ * a_ / (S * S); }
                NOISE_LAYER(23.0, 0.07, float2(0.05, 0.03), 1.3)
                NOISE_LAYER(9.0, 0.11, float2(0.09, -0.06), 5.1)
                NOISE_LAYER(3.1, 0.16, float2(0.21, 0.13), 0.7)
                NOISE_LAYER(1.3, 0.14, float2(-0.17, 0.29), 2.1)
                NOISE_LAYER(0.55, 0.12, float2(0.33, -0.22), 4.0)
                N = normalize(N - float3(slope.x, 0, slope.y));

                // 항적 법선 (유한 차분, 간격은 픽셀 크기 이상)
                float foamW;
                float e = max(0.6, fp);
                float h0 = WakeHeight(xz, foamW);
                float fx, fz;
                float hx = WakeHeight(xz + float2(e, 0), fx);
                float hz = WakeHeight(xz + float2(0, e), fz);
                float wakeF = WF(14.0, fp, 2.0);
                N = normalize(N + float3(-(hx - h0) / e, 0, -(hz - h0) / e) * 1.2 * wakeF);
                if (!front) N = -N;

                // 걸러낸 기울기 분산 → GGX 거칠기 (원거리일수록 글린트가 넓고 부드럽게 번진다)
                float a2 = clamp(0.0004 + var * 2.0, 0.0004, 0.25);

                float4 sc = TransformWorldToShadowCoord(i.positionWS);
                Light L = GetMainLight(sc);
                float NdotL = saturate(dot(N, L.direction));

                // 프레넬 반사 (Schlick, 물 F0 = 0.02) — 거칠수록 스침각 반사가 약해진다
                float ndv = saturate(dot(N, V));
                float F = 0.02 + 0.98 * pow(1.0 - ndv, 5.0) / (1.0 + a2 * 12.0);
                F = max(F, 0.06);
                float3 R = reflect(-V, N);
                R.y = abs(R.y);   // 수면 아래를 향한 반사는 하늘로 접는다
                // 하늘 반사: 씬 반사 프로브(베이크 안 된 어두운 큐브맵일 수 있음) 대신 해석적 하늘 그라데이션
                float3 sky = lerp(_HazeColor.rgb, _ZenithColor.rgb, pow(saturate(R.y), 0.45));
                sky += pow(saturate(dot(R, L.direction)), 8.0) * L.color * 0.25;   // 태양 주변 밝은 하늘

                // 물 본체: 깊은 색 + 마루에서 태양 투과(청록)
                float sss = pow(saturate(dot(V, -L.direction) * 0.5 + 0.5), 4.0) * crest;
                float3 body = _DeepColor.rgb * (0.6 + 0.4 * NdotL) * L.color
                            + _ShallowColor.rgb * (crest * 0.25 + sss * 0.8) * L.color
                            + SampleSH(float3(0, 1, 0)) * _DeepColor.rgb * 0.8;

                // 태양 글린트 (GGX)
                float3 H = normalize(L.direction + V);
                float nh = saturate(dot(N, H));
                float dd = nh * nh * (a2 - 1.0) + 1.0;
                float D = a2 / (PI * dd * dd);
                float spec = min(D * 0.25 * F / max(ndv, 0.15), 40.0) * NdotL;
                float3 glint = spec * L.color * L.shadowAttenuation;

                float3 col = lerp(body, sky, F) + glint;

                // 포말: 너울 마루 + 항적
                float crestFoam = smoothstep(0.86, 0.99, crest) * smoothstep(0.55, 0.8, fbm(p * 0.35)) * _FoamAmount * rough * saturate(1.0 - dist / 450.0);
                float foam = saturate(crestFoam * 0.5 + foamW);
                foam *= saturate(1.0 - dist / 900.0);
                float3 foamCol = _FoamColor.rgb * (0.55 + 0.45 * NdotL) * L.color + SampleSH(N) * 0.25;
                col = lerp(col, foamCol, foam * 0.92);

                // 수평선 연무 — 지수 대기 감쇠: 거리 무한대에서 1 로 수렴 → 스커트(수평선)에서 하늘색과 매끈하게 이어진다
                float haze = 1.0 - exp(-max(dist - _HazeStart, 0.0) / max(_HazeEnd * 0.28, 1.0));
                haze = saturate(haze + smoothstep(_OuterRadius * 0.8, _OuterRadius, length(i.positionWS.xz - _WorldSpaceCameraPos.xz)));
                col = lerp(col, _HazeColor.rgb, haze);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

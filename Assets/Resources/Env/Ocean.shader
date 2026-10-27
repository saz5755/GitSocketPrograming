// 바다 표면 — URP Forward.
//  · 격자는 카메라 기준(셰이더에서 위치 계산)이라 게임/씬 뷰 어느 카메라에서든 같은 메쉬 하나로 그린다
//  · 정점: Gerstner 너울 6개. 각 파는 그 위치의 격자 간격으로 표현 가능한 만큼만 변위 (격자보다 짧은 파를 정점에 넣으면
//          삼각형 면이 드러나 그물망처럼 보인다)
//  · 픽셀: 너울·잔물결·항적 법선을 전부 픽셀 단위로 계산. 화면 픽셀보다 촘촘한 파는 걸러내고 그 기울기 분산을
//          거칠기로 옮겨 원거리에서도 반짝이는 격자 무늬 없이 자연스럽게 흐려진다
//  · 프레넬 하늘 반사(해석적 하늘), GGX 태양 글린트, 마루의 청록 투과광, 바람 얼룩(거친 해면 패치), 흰 포말
//  · 항모 항적 (물리 근거):
//      - Kelvin 파: 선수·선미 압력원 두 개의 정상위상 해 (횡파·발산파, 쿠스프 19.47°, 함정 기준 정지 패턴)
//      - 선수파: 선수에서 벌어지는 쇄파 마루 + 선체 측면 파형 (λ = 2πU²/g)
//      - 선체 접촉선의 난류 세류(경계층), 선미 난류 항적: 포말(수십 초) → 기포(청록 산란) → 슬릭(잔물결 감쇠) 순으로 수명이 길다
//      - 포말 무늬는 물에 실려 흘러가는 셀형 레이스 — 덮임률이 줄수록 끊어진 레이스·패치로 흩어진다
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

        _HullCenter   ("Waterline Center (world)", Vector) = (0, 0, 0, 0)
        _ShipFwd      ("Ship Forward (xz)", Vector)  = (0, 0, -1, 0)
        _ShipSpeed    ("Ship Speed (m/s)", Float)    = 12
        _HullShape    ("Hull (halfLen, halfBeam, bowEntry, sternRun)", Vector) = (87, 11.77, 60, 55)
        _Draft        ("Draft (m)", Float)           = 8.6
        _WakeLength   ("Wake Extent (m)", Float)     = 2500
        _WakeStrength ("Wake Strength", Float)       = 1
        _KelvinHeight ("Kelvin Wave Height (m)", Float) = 1.2
        _BowWave      ("Bow Wave (x U²/2g)", Float)  = 0.3
        _WakeLife     ("Foam / Bubble / Slick life (s)", Vector) = (35, 45, 400, 0)
        _AeratedColor ("Aerated Water", Color)       = (0.05, 0.24, 0.24, 1)
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
                half4  _DeepColor, _ShallowColor, _FoamColor, _HazeColor, _ZenithColor, _AeratedColor;
                float  _HazeStart, _HazeEnd, _WaveScale, _Choppiness, _RippleStrength, _WindPatches, _FoamAmount;
                float  _SeaLevel, _GridSnap, _GridK;
                float4 _HullCenter, _ShipFwd, _HullShape, _WakeLife;
                float  _ShipSpeed, _WakeLength, _WakeStrength, _OuterRadius, _Draft, _KelvinHeight, _BowWave;
                float4 _BoatParams;     // (기록 점 수, 선폭, 선장, 활성)
                float  _OceanTime;      // 파도 시계 — CPU 수면 높이(OceanSurface.SampleHeight)와 같은 시간을 쓰도록 스크립트가 넣는다
                float4 _BoatBounds;     // 항적 영역 (minX, minZ, maxX, maxZ)
            CBUFFER_END
            // 소형정 경로 기록: (x, z, 경과 시간 s, 그때 속도 m/s). 0 = 현재 선수
            float4 _BoatTrail[24];

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
            float2 FlowXZ(float2 xz) { return xz + _ShipFwd.xz * _ShipSpeed * _OceanTime; }

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
            // 함정 좌표: a = 흘수선 중심 기준 선수 방향(+) 거리, y = 우현 방향 거리
            void ShipLocal(float2 xz, out float a, out float y)
            {
                float2 f = normalize(_ShipFwd.xz);
                float2 r = float2(f.y, -f.x);
                float2 rel = xz - _HullCenter.xz;
                a = dot(rel, f);
                y = dot(rel, r);
            }

            // 흘수선 반폭: 선수는 가는 쐐기(입사 반각 ≈ 반폭/선수부 길이), 선미는 트랜섬을 남기고 좁아진다
            float HullHalfBeam(float a)
            {
                float sb = saturate((_HullShape.x - a) / _HullShape.z);
                float ss = saturate((a + _HullShape.x) / _HullShape.w);
                return _HullShape.y * min(pow(sb, 0.95), lerp(0.3, 1.0, sqrt(ss)));
            }

            // 흘수선까지 거리 (선체 밖 +, 안 −)
            float HullDist(float a, float y)
            {
                float ay = abs(y);
                if (a > _HullShape.x)  return length(float2(a - _HullShape.x, ay));
                if (a < -_HullShape.x) return length(float2(-_HullShape.x - a, max(ay - _HullShape.y * 0.3, 0.0)));
                return ay - HullHalfBeam(a);
            }

            // ── Kelvin 파 (심해, 정상위상 근사) ──────────────────────────────
            //  파 방향 θ(진행 방향 기준)의 파수 k = g/(U²cos²θ). t = tanθ, 관측점 방위 α 에서 정상점 조건
            //  tanα = t/(1+2t²) 의 두 근 → 횡파(작은 t)·발산파(큰 t). 8tan²α = 1 (α = 19.47°) 에서 합쳐지는 쿠스프.
            //  위상 φ = K0(x + y·t)√(1+t²), K0 = g/U². 정상점이라 기울기는 파수 벡터 그대로: ∂φ/∂x = K0√(1+t²), ∂φ/∂y = K0·t√(1+t²).
            //  진폭: 원거리 (K0r)^-½, 쿠스프는 Airy 상한 (K0r)^-⅓, 쐐기 밖은 지수 감쇠.
            //  짧은 발산파는 흘수 깊이 인자 e^(−kT/2) 로 약해진다 (Michell 적분). 패턴은 함정 기준으로 정지해 있다.
            void KelvinTerm(float t, float x, float ay, float K0, float amp, float spacing,
                            inout float h, inout float2 g, inout float steep)
            {
                float s1 = sqrt(1.0 + t * t);
                float k = K0 * (1.0 + t * t);
                float A = amp * exp(-0.35 * k * _Draft) * WF(2.0 * PI / k, spacing, 2.0);
                float sn, cs; sincos(K0 * (x + ay * t) * s1, sn, cs);
                h += A * cs;
                g += -A * sn * float2(K0 * s1, K0 * t * s1);     // (∂/∂x, ∂/∂|y|)
                steep = max(steep, A * k);
            }

            // x: 압력원 뒤쪽 거리, y: 횡거리(부호 포함). g 에 (∂η/∂x, ∂η/∂y) 누적
            void KelvinSource(float x, float y, float H, float spacing, inout float h, inout float2 g, inout float steep)
            {
                if (x <= 0.5) return;
                float K0 = 9.81 / (_ShipSpeed * _ShipSpeed);
                float ay = abs(y);
                float r = sqrt(x * x + ay * ay);
                float kr = K0 * r + 1.0;
                float tau = ay / x;
                float disc = 1.0 - 8.0 * tau * tau;                       // 0 = 쿠스프
                float q0 = pow(kr, -1.0 / 3.0);
                float q = sqrt(max(disc, 0.0));
                float caustic = rsqrt(q + q0) * sqrt(1.0 + q0);           // 중심선 1, 쿠스프 ≈ (K0r)^⅙
                float beyond = exp(-max(-disc, 0.0) * pow(kr, 2.0 / 3.0) * 2.0);
                float near = smoothstep(0.0, 25.0, x);                    // 원천 근처는 원거리 근사 대신 선수파가 담당
                float fade = exp(-x / _WakeLength * 1.5);
                float amp = H * rsqrt(1.0 + r / (2.0 * _HullShape.x)) * caustic * beyond * near * fade;
                float tT = disc >= 0.0 ? 2.0 * tau / (1.0 + q) : 0.70710678;          // 횡파
                float tD = disc >= 0.0 ? (1.0 + q) / max(4.0 * tau, 1e-4) : 0.70710678; // 발산파
                float h0 = 0; float2 g0 = 0;
                KelvinTerm(tT, x, ay, K0, amp, spacing, h0, g0, steep);
                KelvinTerm(tD, x, ay, K0, amp * 0.85, spacing, h0, g0, steep);
                h += h0;
                g += float2(g0.x, g0.y * (y < 0.0 ? -1.0 : 1.0));
            }

            // ── 선체에 붙은 물결: 선수파 쇄파 마루 + 측면 파형 ───────────────
            //  선수 정체점 수두 U²/2g 의 일부만큼 물이 솟고, 마루는 선체 입사각보다 넓게(≈24°) 벌어지며 떨어져 나간다.
            //  측면 파형은 선수 마루 → λ/2 뒤 골 → λ 뒤 마루 (λ = 2πU²/g). bowFoam: 선수 쇄파 포말
            float HullWave(float a, float y, out float bowFoam)
            {
                float d = HullDist(a, y);
                float xb = _HullShape.x - a;
                float U2g = _ShipSpeed * _ShipSpeed / (2.0 * 9.81);
                float decay = exp(-max(xb, 0.0) / 38.0) * smoothstep(-2.0, 4.0, xb);
                float Hb = _BowWave * U2g * decay;
                float tanEntry = _HullShape.y / _HullShape.z;
                float dc = max(xb, 0.0) * max(0.445 - tanEntry, 0.05);   // 마루가 선체에서 떨어진 거리
                float wc = 2.0 + 0.07 * max(xb, 0.0);
                float crest = exp(-pow((d - dc) / wc, 2.0));
                float plateau = smoothstep(dc + wc, dc - wc, d) * 0.55;  // 선체와 마루 사이는 들려 있다
                bowFoam = crest * decay;

                float K0 = 9.81 / (_ShipSpeed * _ShipSpeed);
                float side = cos(K0 * max(xb, 0.0)) * exp(-max(d, 0.0) / 9.0)
                           * step(abs(a), _HullShape.x + 2.0) * smoothstep(0.0, 12.0, xb);
                float h = Hb * max(crest, plateau) + side * _KelvinHeight * 0.9;
                return h * smoothstep(-2.5, 0.0, d);                     // 선체 안쪽은 0 (접촉선에서 부드럽게)
            }

            // ── 포말 무늬 ────────────────────────────────────────────────────
            float2 hash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }
            // 셀 경계까지 거리 (F2 − F1) — 0 근처가 셀 경계 = 포말 줄기
            float WorleyEdge(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float d1 = 8.0, d2 = 8.0;
                [unroll] for (int yy = -1; yy <= 1; yy++)
                [unroll] for (int xx = -1; xx <= 1; xx++)
                {
                    float2 o = float2(xx, yy);
                    float2 rr = o + hash22(i + o) - f;
                    float dd = dot(rr, rr);
                    if (dd < d1) { d2 = d1; d1 = dd; } else if (dd < d2) d2 = dd;
                }
                return sqrt(d2) - sqrt(d1);
            }
            // 물에 실린 포말 레이스 (0~1). q: 해수 고정 좌표(함정 정렬 — x 진행 방향).
            //  항적 포말은 전단 흐름에 늘어난 줄무늬·덩어리다: 큰 소용돌이로 좌표를 비틀고(도메인 워프),
            //  진행 방향으로 크게 늘인 노이즈가 주 형태(줄무늬), 셀 경계는 포말 안의 거품 결로만 얹는다
            float FoamLace(float2 q, float t)
            {
                float2 wq = q * float2(0.008, 0.025);
                float2 s = q + (float2(fbm(wq + 1.7), fbm(wq + 9.2)) - 0.5) * float2(60.0, 16.0);
                float streak = fbm(s * float2(0.030, 0.22) + t * 0.003);                  // 진행 방향 줄무늬 (≈7:1, 폭 수 m)
                float blob   = fbm(s * float2(0.050, 0.10) + 6.1);                        // 덩어리
                // 값 분포를 0~1 로 넓혀 덮임률 문턱이 실제 포말 비율과 비슷하게 동작하도록
                float shape  = saturate((streak * 0.65 + blob * 0.55 - 0.6) * 2.4 + 0.5);
                float cells  = exp(-pow(WorleyEdge(s * float2(0.14, 0.34) + 5.3) / 0.12, 2.0));   // 거품 결
                float fine   = exp(-pow(WorleyEdge(s * float2(0.45, 0.90) + 11.1) / 0.10, 2.0));
                return saturate(shape + (cells * 0.35 + fine * 0.2) * smoothstep(0.2, 0.75, shape) - (1.0 - cells) * 0.08);
            }
            // 덮임률 cov 만큼 레이스를 살린 포말 — 덮임률이 낮아지면 레이스·패치로 끊어진다. 원거리(fp: 픽셀 크기)는 평균으로 수렴
            float FoamFromCoverage(float cov, float lace, float fp)
            {
                // 오래된 포말은 얇은 막이라 반투명 — 덮임률이 낮을수록 불투명도도 낮다
                float opacity = 0.4 + 0.6 * cov;
                float f = smoothstep(1.0 - cov, 1.0 - cov + 0.3, lace) * saturate(cov * 3.0);
                return lerp(f, cov * 0.5, saturate((fp - 1.0) / 3.0)) * opacity;
            }

            struct WakeOut { float h; float2 grad; float foam; float bubbles; float slick; };

            // 항적 전체. spacing: 이 위치에서 표현 가능한 최소 간격 (정점 격자 또는 픽셀). shade = false 면 높이만 (정점용)
            WakeOut Wake(float2 xz, float spacing, bool shade, float fp)
            {
                WakeOut o = (WakeOut)0;
                if (_WakeStrength <= 0.0) return o;
                float a, y; ShipLocal(xz, a, y);
                float Hl = _HullShape.x;
                // 영역 밖 조기 종료 (쐐기 19.47° + 여유)
                if (a > Hl + 40.0 || a < -Hl - _WakeLength || abs(y) > 80.0 + max(Hl - a, 0.0) * 0.42) return o;

                float t = _OceanTime;
                float2 f = normalize(_ShipFwd.xz);
                float2 r = float2(f.y, -f.x);

                // Kelvin: 선수(+) · 선미 어깨(−, 위상 반대) 두 압력원
                float steep = 0, hk = 0; float2 gk = 0;
                KelvinSource(Hl - 4.0 - a, y, _KelvinHeight, spacing, hk, gk, steep);
                KelvinSource(-Hl * 0.6 - a, y, -_KelvinHeight * 0.6, spacing, hk, gk, steep);

                float bf;
                float hh = HullWave(a, y, bf);

                // 선미 난류 항적: 폭 ∝ (1 + x/4B)^⅓ 로 완만히 넓어지고, 큰 와류로 가장자리가 구불거린다
                float xs = -Hl - a;
                float2 p = FlowXZ(xz);
                float2 q = float2(dot(p, f), dot(p, r));                  // 해수 고정 좌표 (물과 함께 흘러감)
                float hw = _HullShape.y * 0.8 * pow(1.0 + max(xs, 0.0) / (4.0 * _HullShape.y), 1.0 / 3.0);
                // 가장자리 굴곡 노이즈는 항적 띠 근처에서만 (Kelvin 쐐기 대부분은 건너뛴다)
                bool nearWake = xs > -8.0 && abs(y) < hw * 3.0;
                float wob = nearWake ? (fbm(q * float2(0.006, 0.01)) - 0.5) * hw * 0.7 + (fbm(q * float2(0.03, 0.05) + 4.1) - 0.5) * hw * 0.35 : 0.0;
                float span = smoothstep(-6.0, 6.0, xs) * smoothstep(_WakeLength, _WakeLength * 0.6, xs);
                float yy = abs(y + wob) / hw;
                float tw = exp(-yy * yy * yy * yy) * span;
                float yw = yy / 1.4;
                float twWide = exp(-yw * yw * yw * yw) * span;
                // 수명: 표면 포말(수십 초) < 수중 기포(청록 산란) < 슬릭(잔물결 감쇠, 수 km)
                float age = max(xs, 0.0) / max(_ShipSpeed, 0.5);
                // 흰 포말은 항적 중앙에 몰리고 가장자리는 기포(청록)만 남는다
                float covW = tw * tw * 0.85 * exp(-age / _WakeLife.x);
                // 기포층은 가장자리가 부드럽게 퍼진다 (난류 확산) — 띠처럼 칠해지지 않게 가우스 단면
                float yb = abs(y + wob) / (hw * 1.5);
                o.bubbles = exp(-yb * yb * 1.6) * span * exp(-age / _WakeLife.y);
                o.slick = twWide * exp(-age / _WakeLife.z);

                float damp = 1.0 - 0.5 * o.slick;      // 난류가 항적 중앙의 Kelvin 파를 누그러뜨린다
                o.h = (hk * damp + hh) * _WakeStrength;
                if (!shade) return o;

                // 선체 물결 기울기 (완만한 형상 — 유한 차분)
                float e = max(0.8, spacing);
                float dummy;
                float2 gh = float2(HullWave(a + e, y, dummy) - hh, HullWave(a, y + e, dummy) - hh) / e;
                float2 gAY = float2(-gk.x * damp, gk.y * damp) + gh;    // (∂/∂a, ∂/∂y)
                o.grad = (gAY.x * f + gAY.y * r) * _WakeStrength;

                // 포말 덮임률
                float d = HullDist(a, y);
                float xb = Hl - a;
                // 선체 접촉선 세류: 경계층이 선미로 갈수록 두꺼워진다 (δ ∝ x^0.8)
                float aft = saturate(xb / (2.0 * Hl));
                float bl = 2.5 + 4.0 * pow(aft, 0.8);
                // 세기: 선수파가 선체를 따라 흐르는 앞쪽 1/3 과 선미 쪽이 강하고 중앙부도 흰 띠가 이어진다
                float washAmt = 0.75 + 0.25 * exp(-xb / 50.0) + 0.2 * smoothstep(0.6, 1.0, aft);
                float wash = exp(-max(d, 0.0) / bl) * smoothstep(-1.8, -0.3, d) * step(-Hl - 4.0, a) * washAmt;
                // 프로펠러 직후 난류 용승(boil): 선미 수십 m 안이 가장 희고 끓어오르되, 사이사이 청록 물이 비친다
                float boil = tw * exp(-max(xs, 0.0) / 55.0) * 0.9;
                // 발산파 마루 쇄파: 선수 근처 가파른 마루 (Kelvin 팔의 흰 깃털)
                float crestBreak = saturate((steep - 0.06) * 8.0) * exp(-max(xb, 0.0) / 220.0) * saturate(hk / max(_KelvinHeight, 0.01));

                float cov = saturate(max(max(covW, boil), max(wash, bf * 0.95)) + crestBreak * 0.55);
                // 포말 무늬(셀 노이즈 3겹 + fbm)는 비싸므로 포말이 있는 곳에서만
                if (cov > 0.004)
                {
                    float lace = FoamLace(q, t);
                    lace = lerp(lace, saturate(lace + fbm(q * 0.08 + float2(0.0, t * 0.4)) * 0.4), boil);
                    o.foam = FoamFromCoverage(cov, lace, fp) * _WakeStrength;
                }
                o.bubbles = saturate(o.bubbles + boil * 0.5 + wash * 0.3) * _WakeStrength;
                o.slick *= _WakeStrength;
                return o;
            }

            // ── 소형정 항적 (구조정) ─────────────────────────────────────────
            //  고속 활주정은 길이 Froude 수 Fn = U/√(gL) 가 커서 항적 각이 Kelvin 19.47° 보다 좁아진다
            //  (Rabaud & Moisy 2013: α ≈ 0.4/Fn). 배가 실제로 지나간 경로(물에 실려 흘러간 기록)를 따라
            //  V자 발산파 묶음, 마루의 쇄파 포말, 선미 프로펠러 백파 포말·기포·슬릭을 더한다.
            void BoatWake(float2 xz, float spacing, bool shade, float fp, inout WakeOut o)
            {
                if (_BoatParams.w < 0.5) return;
                if (xz.x < _BoatBounds.x || xz.y < _BoatBounds.y || xz.x > _BoatBounds.z || xz.y > _BoatBounds.w) return;
                int n = (int)_BoatParams.x;
                float beam = _BoatParams.y, blen = _BoatParams.z;

                // 경로 위 가장 가까운 점 → 호 길이 s(선수에서 뒤로), 측면 거리 d, 그 지점의 경과 시간·속도
                float best = 1e12, s = 0, d = 0, age = 0, spd = 0;
                float2 tdir = float2(0, 1);
                float acc = 0;
                bool ahead = false;
                [loop] for (int i = 0; i < 23; i++)
                {
                    if (i >= n - 1) break;
                    float4 A = _BoatTrail[i], B = _BoatTrail[i + 1];
                    float2 ab = B.xy - A.xy;
                    float len = length(ab);
                    if (len < 1e-3) continue;
                    float2 dir = ab / len;                              // 선수 → 뒤쪽
                    float u = dot(xz - A.xy, dir);
                    float tt = saturate(u / len);
                    float2 rel = xz - (A.xy + ab * tt);
                    float d2 = dot(rel, rel);
                    if (d2 < best)
                    {
                        best = d2; s = acc + tt * len; tdir = dir;
                        d = rel.x * dir.y - rel.y * dir.x;
                        age = lerp(A.z, B.z, tt); spd = lerp(A.w, B.w, tt);
                        ahead = (i == 0 && u < -1.0);
                    }
                    acc += len;
                }
                if (ahead) return;                                       // 선수 앞쪽은 물보라 파티클이 담당

                float ad = abs(d);
                float U = max(spd, 0.1);
                float Fn = U / sqrt(9.81 * blen);
                float alpha = min(0.3398, 0.4 / max(Fn, 0.05));          // 항적 반각 (rad)
                float tanA = tan(alpha);
                float2 nrm = float2(tdir.y, -tdir.x) * (d < 0.0 ? -1.0 : 1.0);

                // V자 발산파: 선수 양옆에서 출발해 α 로 벌어지는 마루 묶음 (2~3 마루)
                float dc = beam * 0.5 + s * tanA;
                float w = 1.6 + 0.04 * s;
                float delta = ad - dc;
                float lam = clamp(0.06 * U * U, 3.0, 12.0);
                float H = 0.03 * U * rsqrt(1.0 + s / (2.0 * blen)) * exp(-age / 30.0) * smoothstep(0.0, 3.0, s)
                        * WF(lam, spacing, 2.0);
                float env = exp(-pow(delta / (w * 1.5), 2.0));
                float sn, cs; sincos(6.2832 * delta / lam, sn, cs);
                // 선체 안쪽·바로 옆은 물이 갑판으로 솟지 않게
                float hullMask = s < blen + 1.0 ? smoothstep(beam * 0.5, beam * 0.5 + 1.5, ad) : 1.0;
                float hArm = H * env * cs * hullMask;
                o.h += hArm;
                if (!shade) return;

                // 기울기: η(δ), δ = |d| − dc(s) → ∂δ/∂xz = sign(d)·n − tanα·t
                float dEta = -H * env * sn * 6.2832 / lam * hullMask;
                o.grad += dEta * (nrm - tanA * tdir);

                // 포말: 선미 백파(선폭 정도에서 (1+x/4B)^⅓ 로 퍼짐) + 용승 + V 마루 쇄파 + 활주 중 선체 옆 물보라 띠
                float sx = s - blen;
                float tw0 = beam * 0.45 * pow(1.0 + max(sx, 0.0) / (4.0 * beam), 1.0 / 3.0);
                float core = exp(-pow(ad / tw0, 2.0)) * smoothstep(-1.0, 2.0, sx);
                float power = saturate(U / 8.0);
                // 백파 포말: 선미 뒤 수십 m 만 하얗고(수 초) 이후엔 기포·슬릭으로 남는다
                float covProp = core * core * exp(-age / 5.0) * (0.3 + 0.55 * power);
                float boil = core * exp(-max(sx, 0.0) / 8.0) * power * 0.9;
                // V 마루 쇄파: 배 바로 뒤에서만 흰 거품이 일고 1~2초 안에 흩어진다 (마루 폭만큼 넓고 부드럽게)
                float armFoam = exp(-pow(delta / (1.4 + 0.04 * s), 2.0)) * exp(-age / 1.5) * saturate((U - 6.0) / 8.0) * 0.45;
                float hullFoam = exp(-pow((ad - beam * 0.5) / 1.0, 2.0)) * step(s, blen) * saturate((U - 3.0) / 6.0);
                float cov = saturate(max(max(covProp, boil), max(armFoam * 0.8, hullFoam * 0.9)));
                if (cov > 0.004)
                {
                    float2 p = FlowXZ(xz);
                    float2 q = float2(dot(p, tdir), dot(p, float2(tdir.y, -tdir.x))) * 1.8;   // 작은 배 — 포말 결도 잘게
                    float lace = FoamLace(q, _OceanTime);
                    lace = lerp(lace, saturate(lace + fbm(q * 0.1 + float2(0.0, _OceanTime * 0.6)) * 0.4), boil);
                    o.foam = max(o.foam, FoamFromCoverage(cov, lace, fp));
                }
                o.bubbles = max(o.bubbles, core * exp(-age / 10.0) * power + boil * 0.4);
                o.slick = max(o.slick, core * exp(-age / 60.0));
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
                float t = _OceanTime;
                float3 d = 0, T = 0, B = 0;
                #define VTX_WAVE(dir, L, Q, A) Gerstner(p, dir, L, Q, A * _WaveScale * Env(p, L), t, WF(L, spacing, 3.0), d, T, B);
                SWELL(VTX_WAVE)

                // 원거리는 변위를 줄여 수평선이 출렁이지 않게
                float dist = distance(xz, _WorldSpaceCameraPos.xz);
                float lod = saturate(1.0 - (dist - 600.0) / 1400.0);
                WakeOut wv = Wake(xz, spacing * 1.5, false, spacing);
                BoatWake(xz, spacing * 1.5, false, spacing, wv);
                float wh = wv.h;
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
                float t = _OceanTime;

                // 픽셀 하나가 덮는 수면 크기 — 이보다 짧은 파는 걸러낸다 (모아레·반짝이 격자 방지)
                float2 fw = fwidth(xz);
                float fp = max(max(fw.x, fw.y), 1e-3);

                // 바람 얼룩: 수백 m 단위로 잔물결 세기가 달라지는 거친/잔잔한 패치 (실제 해면의 고양이 발자국)
                float patch = fbm(p * 0.0045 + float2(t * 0.004, 0));
                float rough = lerp(1.0, smoothstep(0.25, 0.75, patch) * 1.5 + 0.25, _WindPatches);

                // 항적 (높이 기울기·포말·기포·슬릭). 슬릭: 항적 난류·표면막이 짧은 잔물결을 눌러 매끈하고 어두운 띠가 된다
                WakeOut wk = Wake(xz, fp, true, fp);
                BoatWake(xz, fp, true, fp, wk);
                rough *= 1.0 - 0.78 * wk.slick;

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

                // 항적 법선 (Kelvin 은 해석적 기울기, 각 파는 픽셀로 표현 가능한 만큼만)
                N = normalize(N - float3(wk.grad.x, 0, wk.grad.y));
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
                // 항적 기포층: 수중 기포가 빛을 산란해 밝은 청록 (포말이 사라진 뒤에도 오래 남는다)
                float3 aerated = _AeratedColor.rgb * ((0.35 + 0.65 * NdotL) * L.color + SampleSH(float3(0, 1, 0)) * 0.6);
                body = lerp(body, aerated, saturate(wk.bubbles * 0.7));

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
                // 항적 포말은 수 km 밖에서도 보인다 — 무늬만 원거리에서 평균으로 흐려지고(FoamFromCoverage) 연무가 덮는다
                float foam = saturate(crestFoam * 0.5 * saturate(1.0 - dist / 900.0) + wk.foam);
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

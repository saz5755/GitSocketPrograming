using System;
using UnityEngine;
using static ProcAudio;

/// <summary>
/// 비행 사운드 레시피 모음 — 모두 ProcAudio 로 합성해 캐시한다.
/// 상용 비행시뮬(DCS·MSFS·Ace Combat)의 사운드 설계를 따라 엔진을 여러 레이어로 나눈다:
///   코어 럼블 · 터빈 휘슬 · 팬 버즈쏘 · 배기 로어 · 애프터버너 — 각 레이어를 RPM·방향·거리로 믹스
/// 루프는 두 방식으로 이음매를 없앤다: 노이즈는 등전력 크로스페이드, 음정 성분은 루프 길이의 정수배 주기.
/// </summary>
public static class FlightSounds
{
    // ── 루프 헬퍼 ───────────────────────────────────────────────────────────
    /// <summary>정확히 seconds 길이의 이음매 없는 노이즈 루프</summary>
    static float[] NoiseLoop(float seconds, Func<int, float[]> gen)
    {
        int fade = SR / 4;
        var b = gen(N(seconds) + fade);
        MakeSeamless(ref b, fade);
        return b;
    }

    /// <summary>루프 길이에 딱 맞는 주파수 — 끝에서 위상이 정확히 이어진다</summary>
    static float Snap(float hz, float loopSec) => Mathf.Max(1f, Mathf.Round(hz * loopSec)) / loopSec;

    static void AddTone(float[] b, float hz, float amp, float loopSec, float phase = 0f)
    {
        float f = Snap(hz, loopSec);
        double w = 2.0 * Math.PI * f / SR;
        for (int i = 0; i < b.Length; i++) b[i] += (float)Math.Sin(w * i + phase) * amp;
    }

    /// <summary>주기적 진폭 변조 (루프 길이에 맞춘 주파수)</summary>
    static void AddAM(float[] b, float hz, float depth, float loopSec)
    {
        float f = Snap(hz, loopSec);
        double w = 2.0 * Math.PI * f / SR;
        for (int i = 0; i < b.Length; i++) b[i] *= 1f - depth * 0.5f * (1f + (float)Math.Sin(w * i));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 엔진 레이어 (루프) — AudioSource.pitch 로 RPM 을 표현하므로 중간 RPM 기준으로 합성
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>연소실 코어 럼블 — 몸으로 느끼는 저역</summary>
    public static AudioClip EngineCore => Get("eng_core", () =>
    {
        var b = NoiseLoop(3f, n => LP(Brown(n, 11), 220f, 2));
        Mix(b, NoiseLoop(3f, n => BP(Pink(n, 12), 420f, 0.8f)), 0.45f);
        AddTone(b, 38f, 0.10f, 3f);      // 고정자 블레이드 통과 저역 맥동
        AddAM(b, 6f, 0.15f, 3f);
        return b;
    });

    /// <summary>터빈 휘슬 — 압축기 블레이드 통과 주파수와 배음, 미세한 맥놀이</summary>
    public static AudioClip EngineWhine => Get("eng_whine", () =>
    {
        const float L = 2f;
        var b = NoiseLoop(L, n => BP(White(n, 21), 2400f, 6f));
        for (int i = 0; i < b.Length; i++) b[i] *= 0.35f;
        AddTone(b, 1180f, 0.32f, L);
        AddTone(b, 1181.5f, 0.18f, L, 1.3f);  // 다단 압축기의 미세 디튠 → 살아있는 맥놀이
        AddTone(b, 2360f, 0.12f, L, 0.4f);
        AddTone(b, 3540f, 0.05f, L, 2.1f);
        AddTone(b, 590f, 0.08f, L, 0.9f);
        return b;
    }, 0.8f);

    /// <summary>팬 버즈쏘 — 고추력에서 팬 끝이 초음속이 되며 나는 톱니형 다중음 (전방에서 크게 들림)</summary>
    public static AudioClip EngineBuzz => Get("eng_buzz", () =>
    {
        const float L = 2f;
        var b = new float[N(L)];
        var r = new System.Random(31);
        float f0 = 92f;
        for (int h = 2; h <= 40; h++)   // 기본음(92Hz)은 빼서 저역 웅웅거림 없이 — 필터를 쓰면 루프 주기가 깨진다
        {
            float amp = 0.6f / Mathf.Pow(h, 0.85f) * (0.4f + (float)r.NextDouble() * 0.9f);  // 블레이드 편차 → 불균일 배음
            AddTone(b, f0 * h, amp, L, (float)(r.NextDouble() * 6.28));
        }
        return b;
    }, 0.7f);

    /// <summary>배기 로어 — 후방에서 가장 크게 들리는 거친 광대역 제트 소음</summary>
    public static AudioClip EngineRoar => Get("eng_roar", () =>
    {
        var b = NoiseLoop(3f, n =>
        {
            var p = Pink(n, 41);
            var x = BP(p, 520f, 0.55f);
            Mix(x, LP(Brown(n, 42), 260f), 0.7f);
            Mix(x, BP(White(n, 43), 1900f, 0.9f), 0.12f);
            return x;
        });
        AddAM(b, 3f, 0.10f, 3f);
        return b;
    });

    /// <summary>애프터버너 — 저역이 두껍고 불규칙하게 터지는(crackle) 연소음</summary>
    public static AudioClip Afterburner => Get("eng_ab", () =>
    {
        var b = NoiseLoop(4f, n =>
        {
            var x = LP(Brown(n, 51), 300f);
            Mix(x, BP(Pink(n, 52), 700f, 0.6f), 0.55f);
            // 크래클: 무작위 짧은 압력 펄스 (충격파가 흩어지는 '타닥' 질감)
            var r = new System.Random(53);
            var crack = HP(White(n, 54), 900f);
            var env = new float[n];
            for (int i = 0; i < n; i += SR / 60 + r.Next(SR / 25))
            {
                float a = 0.4f + (float)r.NextDouble() * 0.6f;
                int len = SR / 70;
                for (int k = 0; k < len && i + k < n; k++) env[i + k] = Mathf.Max(env[i + k], a * Mathf.Exp(-k / (float)(len / 4)));
            }
            for (int i = 0; i < n; i++) x[i] += crack[i] * env[i] * 0.35f;
            return x;
        });
        return b;
    });

    // ════════════════════════════════════════════════════════════════════════
    // 공력 · 콕핏 · 환경 (루프)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>저역 풍압 — 동체를 때리는 공기 덩어리</summary>
    public static AudioClip WindLow => Get("wind_low", () =>
    {
        var b = NoiseLoop(5f, n => LP(Pink(n, 61), 380f, 2));
        AddAM(b, 0.4f, 0.35f, 5f);
        AddAM(b, 1.2f, 0.15f, 5f);
        return b;
    });

    /// <summary>캐노피 위를 흐르는 고역 풍절음</summary>
    public static AudioClip WindHiss => Get("wind_hiss", () =>
    {
        var b = NoiseLoop(4f, n => BP(Pink(n, 71), 2600f, 0.7f));
        Mix(b, NoiseLoop(4f, n => BP(White(n, 72), 5200f, 1.5f)), 0.15f);
        AddAM(b, 0.75f, 0.25f, 4f);
        return b;
    });

    /// <summary>버핏 — 고받음각·실속 직전 기체를 두드리는 불규칙 저역 진동</summary>
    public static AudioClip Buffet => Get("buffet", () =>
    {
        int n = N(3f);
        var b = new float[n];
        var r = new System.Random(81);
        var body = LP(Brown(n, 82), 140f);
        for (int i = 0; i < n; i += SR / 14 + r.Next(SR / 9))
        {
            float a = 0.5f + (float)r.NextDouble() * 0.5f;
            int len = SR / 8;
            for (int k = 0; k < len && i + k < n; k++)
                b[i + k] += body[i + k] * 4f * a * Env(k / (float)SR, 0.008f, 0.04f)
                          + Mathf.Sin(2f * Mathf.PI * (28f + 6f * a) * k / SR) * a * 0.5f * Env(k / (float)SR, 0.004f, 0.05f);
        }
        int fade = SR / 5;
        for (int i = 0; i < fade; i++) { float g = (float)i / fade; b[i] *= g; b[n - 1 - i] *= g; }
        return b;
    });

    /// <summary>항모 갑판 주변 바다 — 파도가 부딪는 저역 스웰 + 거품 고역</summary>
    public static AudioClip SeaAmbience => Get("sea_amb", () =>
    {
        const float L = 12f;
        var b = NoiseLoop(L, n => LP(Pink(n, 91), 650f, 2));
        AddAM(b, 0.13f, 0.6f, L);
        var foam = NoiseLoop(L, n => HP(Pink(n, 92), 2500f));
        AddAM(foam, 0.13f, 0.9f, L);
        Mix(b, foam, 0.25f);
        Mix(b, NoiseLoop(L, n => LP(Brown(n, 93), 90f)), 0.6f);   // 선체 기관 저음
        return b;
    }, 0.75f);

    /// <summary>캐노피 안 환기·항전장비 팬 — 조용한 콕핏에도 들리는 바닥 소음</summary>
    public static AudioClip CockpitHum => Get("ckpt_hum", () =>
    {
        const float L = 2f;
        var b = NoiseLoop(L, n => BP(Pink(n, 95), 900f, 0.5f));
        for (int i = 0; i < b.Length; i++) b[i] *= 0.4f;
        AddTone(b, 400f, 0.05f, L);      // 400Hz 항공기 교류 전원 험
        AddTone(b, 800f, 0.02f, L);
        AddTone(b, 120f, 0.04f, L);
        return b;
    }, 0.5f);

    /// <summary>기총 루프 — M61A2 분당 6000발(100Hz) 의 'BRRRT' 버즈</summary>
    public static AudioClip GunLoop => Get("gun_loop", () =>
    {
        const float L = 0.5f;
        int n = N(L), period = SR / 100;
        var b = new float[n];
        var r = new System.Random(101);
        var nz = HP(White(n, 102), 300f);
        for (int s = 0; s * period < n; s++)
        {
            float a = 0.8f + (float)r.NextDouble() * 0.2f;
            for (int k = 0; k < period && s * period + k < n; k++)
            {
                float t = k / (float)SR;
                int i = s * period + k;
                b[i] += nz[i] * a * Env(t, 0.0004f, 0.0018f) * 0.9f                            // 장약 폭음
                      + Mathf.Sin(2f * Mathf.PI * 180f * t) * a * Env(t, 0.0005f, 0.004f) * 0.7f;  // 포신 몸통
            }
        }
        AddTone(b, 100f, 0.18f, L);      // 회전 포신 기계음
        AddTone(b, 300f, 0.06f, L);
        var low = LP(b, 900f);
        Mix(low, b, 0.35f);
        for (int i = 0; i < n; i++) low[i] = Sat(low[i], 1.6f);
        return low;
    });

    /// <summary>기총 정지 — 포신이 감속하며 몇 발 더 나가고 잔향이 꼬리를 남긴다</summary>
    public static AudioClip GunTail => Get("gun_tail", () =>
    {
        int n = N(1.4f);
        var b = new float[n];
        var nz = HP(White(n, 111), 300f);
        double ph = 0;
        float rate = 100f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            rate = Mathf.Max(0f, 100f * (1f - t / 0.25f));
            double prev = ph;
            ph += rate / SR;
            if (rate > 0f && Math.Floor(ph) != Math.Floor(prev))
            {
                float a = Mathf.Clamp01(1f - t / 0.25f);
                for (int k = 0; k < SR / 120 && i + k < n; k++)
                    b[i + k] += nz[i + k] * a * Env(k / (float)SR, 0.0004f, 0.002f);
            }
            // 회전 포신 감속음
            b[i] += Mathf.Sin(2f * Mathf.PI * 100f * (1f - t / 0.6f) * t) * 0.12f * Mathf.Clamp01(1f - t / 0.45f);
        }
        var tail = LP(Brown(n, 112), 400f);
        for (int i = 0; i < n; i++) b[i] += tail[i] * 0.5f * Env(i / (float)SR, 0.03f, 0.35f);
        return LP(b, 2400f);
    });

    /// <summary>미사일 모터 루프 — 고체 로켓의 거친 쇳소리 섞인 굉음</summary>
    public static AudioClip MissileMotor => Get("msl_motor", () =>
    {
        var b = NoiseLoop(2f, n =>
        {
            var x = BP(Pink(n, 121), 900f, 0.5f);
            Mix(x, LP(Brown(n, 122), 250f), 0.8f);
            Mix(x, HP(White(n, 123), 3500f), 0.08f);
            return x;
        });
        AddAM(b, 22f, 0.25f, 2f);
        return b;
    });

    // ════════════════════════════════════════════════════════════════════════
    // 원샷
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>미사일 발사 — 레일 이탈 '쿵' + 모터 점화 + 멀어지며 낮아지는 굉음</summary>
    public static AudioClip MissileLaunch => Get("msl_launch", () =>
    {
        int n = N(2.4f);
        var b = new float[n];
        var roar = BP(Pink(n, 131), 800f, 0.5f);
        Mix(roar, LP(Brown(n, 132), 260f), 1.0f);
        var lpRoar = ProcAudio.Biquad.LowPass(6000f);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            // 런처 이탈 쿵 (금속성)
            b[i] += Mathf.Sin(2f * Mathf.PI * 62f * t) * Env(t, 0.002f, 0.07f) * 0.8f
                  + Mathf.Sin(2f * Mathf.PI * 1430f * t) * Env(t, 0.0005f, 0.012f) * 0.25f;
            // 점화 직후 최대, 이후 멀어진다
            float ign = Env(t - 0.06f, 0.025f, 0.9f) * (t > 0.06f ? 1f : 0f);
            b[i] += roar[i] * ign * 1.1f;
        }
        // 멀어지면서 고역이 줄어드는 공기 흡수 — 구간별 저역 통과
        var o = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            if (i % 256 == 0) lpRoar = ProcAudio.Biquad.LowPass(Mathf.Lerp(7000f, 900f, Mathf.Clamp01(t / 1.8f)));
            o[i] = lpRoar.Process(b[i]);
        }
        for (int i = 0; i < n; i++) o[i] = Sat(o[i], 1.4f);
        return o;
    });

    /// <summary>근거리 폭발 — 충격파 크랙 + 저역 붐 + 파편 잔향</summary>
    public static AudioClip ExplosionNear => Get("expl_near", () =>
    {
        int n = N(3.2f);
        var b = new float[n];
        var crack = White(n, 141);
        var body  = LP(Brown(n, 142), 900f);
        var rumble= LP(Brown(n, 143), 160f, 2);
        var deb   = HP(White(n, 144), 2500f);
        var r = new System.Random(145);
        var debEnv = new float[n];
        for (int i = N(0.15f); i < N(1.6f); i += SR / 40 + r.Next(SR / 12))
        {
            float a = (float)r.NextDouble() * Mathf.Exp(-(i / (float)SR) * 1.8f);
            for (int k = 0; k < SR / 200 && i + k < n; k++) debEnv[i + k] = a * Mathf.Exp(-k / 40f);
        }
        double ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            ph += 2.0 * Math.PI * Mathf.Lerp(95f, 30f, Mathf.Clamp01(t / 0.8f)) / SR;
            b[i] = crack[i] * Env(t, 0.0003f, 0.012f) * 1.2f
                 + (float)Math.Sin(ph) * Env(t, 0.004f, 0.45f) * 1.3f
                 + body[i] * 3f * Env(t, 0.006f, 0.35f)
                 + rumble[i] * 4f * Env(t, 0.05f, 1.1f)
                 + deb[i] * debEnv[i] * 0.4f;
            b[i] = Sat(b[i], 1.8f);
        }
        return b;
    }, 0.98f);

    /// <summary>원거리 폭발 — 고역이 대기에 흡수되어 남는 둔탁한 굉음</summary>
    public static AudioClip ExplosionFar => Get("expl_far", () =>
    {
        int n = N(4f);
        var b = new float[n];
        var rumble = LP(Brown(n, 151), 220f, 2);
        var thud   = LP(Pink(n, 152), 500f);
        double ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            ph += 2.0 * Math.PI * Mathf.Lerp(60f, 28f, Mathf.Clamp01(t / 1.2f)) / SR;
            b[i] = rumble[i] * 4f * Env(t, 0.06f, 1.4f)
                 + thud[i] * 1.5f * Env(t, 0.01f, 0.25f)
                 + (float)Math.Sin(ph) * 0.7f * Env(t, 0.02f, 0.6f);
        }
        return b;
    });

    /// <summary>플레어 사출 — 카트리지 폭음 + 마그네슘 연소 쉿 소리</summary>
    public static AudioClip FlarePop => Get("flare_pop", () =>
    {
        int n = N(0.9f);
        var b = new float[n];
        var hiss = BP(White(n, 161), 3200f, 0.7f);
        var pop  = LP(White(n, 162), 3000f);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            b[i] = pop[i] * Env(t, 0.0005f, 0.015f) * 1.4f
                 + Mathf.Sin(2f * Mathf.PI * 140f * t) * Env(t, 0.001f, 0.03f) * 0.7f
                 + hiss[i] * Env(t - 0.02f, 0.03f, 0.35f) * (t > 0.02f ? 0.55f : 0f);
        }
        return b;
    });

    /// <summary>채프 사출 — 폭음 + 금속 박편이 흩어지는 바스락</summary>
    public static AudioClip ChaffPop => Get("chaff_pop", () =>
    {
        int n = N(0.8f);
        var b = new float[n];
        var pop = LP(White(n, 171), 2500f);
        var gr  = HP(White(n, 172), 4000f);
        var r = new System.Random(173);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            b[i] = pop[i] * Env(t, 0.0005f, 0.018f) * 1.3f
                 + Mathf.Sin(2f * Mathf.PI * 120f * t) * Env(t, 0.001f, 0.03f) * 0.6f;
            if (t > 0.02f && r.NextDouble() < 0.05 * Mathf.Exp(-t * 4f)) b[i] += gr[i] * 2f;
        }
        return Merge(b, BP(b, 5000f, 1f), 0.3f);
    });

    /// <summary>무장 선택 스위치 — 실제 토글 스위치의 짧은 이중 기계음</summary>
    public static AudioClip SwitchClick => Get("sw_click", () =>
    {
        int n = N(0.12f);
        var b = new float[n];
        var nz = White(n, 181);
        foreach (float at in new[] { 0f, 0.038f })
            for (int i = N(at); i < n; i++)
            {
                float t = (i - N(at)) / (float)SR;
                b[i] += (nz[i] * 0.6f + Mathf.Sin(2f * Mathf.PI * 2700f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 4300f * t) * 0.3f)
                        * Env(t, 0.0002f, 0.004f) * (at > 0 ? 0.6f : 1f);
            }
        return HP(b, 600f);
    }, 0.6f);

    /// <summary>내 기체 피탄 — 외피를 때리는 금속성 충격 (모드 합성)</summary>
    public static AudioClip HitMetal => Get("hit_metal", () =>
    {
        int n = N(0.5f);
        var b = new float[n];
        var nz = White(n, 191);
        float[] modes = { 610f, 1130f, 1720f, 2460f, 3310f };
        float[] decay = { 0.12f, 0.08f, 0.06f, 0.045f, 0.03f };
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float s = nz[i] * Env(t, 0.0002f, 0.006f) * 1.2f
                    + Mathf.Sin(2f * Mathf.PI * 90f * t) * Env(t, 0.001f, 0.05f) * 0.8f;
            for (int m = 0; m < modes.Length; m++) s += Mathf.Sin(2f * Mathf.PI * modes[m] * t) * Env(t, 0.0003f, decay[m]) * 0.25f;
            b[i] = Sat(s, 1.5f);
        }
        return b;
    });

    /// <summary>원거리 피탄 — 짧은 파열음</summary>
    public static AudioClip HitFar => Get("hit_far", () =>
    {
        int n = N(0.25f);
        var b = LP(White(n, 201), 2200f);
        for (int i = 0; i < n; i++) b[i] *= Env(i / (float)SR, 0.0005f, 0.03f);
        return b;
    }, 0.7f);

    /// <summary>바다 착수 — 물기둥이 솟고 떨어지는 소리</summary>
    public static AudioClip WaterSplash => Get("splash", () =>
    {
        int n = N(2.5f);
        var b = new float[n];
        var wash = LP(Pink(n, 211), 1800f);
        var hi   = HP(White(n, 212), 3000f);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            b[i] = wash[i] * 2.5f * Env(t, 0.02f, 0.6f)
                 + hi[i] * 0.5f * Env(t - 0.3f, 0.2f, 0.6f) * (t > 0.3f ? 1f : 0f)   // 떨어지는 물보라
                 + Mathf.Sin(2f * Mathf.PI * 55f * t) * Env(t, 0.003f, 0.12f) * 0.8f;
        }
        return b;
    });

    /// <summary>G 호흡 — 고G 에서 조종사가 하는 항G 호흡(L-1 기법)의 짧고 거친 날숨</summary>
    public static AudioClip GStrainExhale => Get("g_exhale", () =>
    {
        int n = N(0.55f);
        var nz = Pink(n, 221);
        var f1 = BP(nz, 650f, 3f);
        var f2 = BP(nz, 1150f, 4f);
        var b = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float env = Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((0.55f - t) / 0.2f);
            float voice = Mathf.Sin(2f * Mathf.PI * (112f - t * 20f) * t);   // 성대가 조이는 낮은 '흡' 소리
            b[i] = (f1[i] * 1.8f + f2[i] * 1.2f + voice * 0.15f * Env(t, 0.01f, 0.12f)) * env;
        }
        return b;
    }, 0.6f);

    public static AudioClip GStrainInhale => Get("g_inhale", () =>
    {
        int n = N(0.35f);
        var nz = HP(Pink(n, 231), 1200f);
        var b = BP(nz, 2600f, 1.2f);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            b[i] *= Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.35f));
        }
        return b;
    }, 0.45f);

    /// <summary>애프터버너 점화 — 연료가 한꺼번에 붙으며 나는 낮은 '훅'</summary>
    public static AudioClip AfterburnerLight => Get("ab_light", () =>
    {
        int n = N(1.1f);
        var b = new float[n];
        var body = LP(Brown(n, 241), 260f);
        var air  = BP(Pink(n, 242), 900f, 0.6f);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            b[i] = body[i] * 4f * Env(t, 0.03f, 0.35f)
                 + air[i] * 1.2f * Env(t, 0.02f, 0.2f)
                 + Mathf.Sin(2f * Mathf.PI * 46f * t) * Env(t, 0.02f, 0.25f) * 0.9f;
            b[i] = Sat(b[i], 1.3f);
        }
        return b;
    });

    /// <summary>근접 통과 — 공기를 가르는 '휘익' (도플러와 함께 들린다)</summary>
    public static AudioClip Flyby => Get("flyby", () =>
    {
        int n = N(2.2f);
        var src = Pink(n, 251);
        var lo  = LP(Brown(n, 252), 200f);
        var o = new float[n];
        var f = ProcAudio.Biquad.BandPass(500f, 0.7f);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float k = t / 2.2f;
            if (i % 128 == 0) f = ProcAudio.Biquad.BandPass(Mathf.Lerp(380f, 2400f, Mathf.Sin(Mathf.PI * Mathf.Pow(k, 0.7f))), 0.7f);
            // 다가올 땐 천천히 커지고 지나간 직후 정점, 이후 빠르게 낮아진다
            float env = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(k, 0.55f)), 2f);
            o[i] = (f.Process(src[i]) * 2.2f + lo[i] * 2.5f) * env;
        }
        return o;
    });

    static float[] Merge(float[] a, float[] b, float g)
    {
        var o = (float[])a.Clone();
        Mix(o, b, g);
        return o;
    }
}

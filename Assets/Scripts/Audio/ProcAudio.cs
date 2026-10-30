using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 절차 오디오 합성 공용 라이브러리 — 오디오 파일 없이 상용 비행시뮬 수준의 레이어 사운드를 만든다.
///  · 바이쿼드 필터(LP/HP/BP), 핑크·브라운 노이즈, 크로스페이드로 이음매 없는 루프
///  · 생성한 클립은 이름으로 캐시 — 기체가 여러 대여도 합성은 한 번
/// 샘플레이트 44.1kHz (예전 22kHz 는 고역 휘슬·풍절음이 뭉개졌다).
/// </summary>
public static class ProcAudio
{
    public const int SR = 44100;

    static readonly Dictionary<string, AudioClip> s_cache = new();

    /// <summary>캐시된 클립을 돌려주거나 make 로 합성해 캐시한다 (루프 이음매 처리는 레시피가 직접 한다)</summary>
    public static AudioClip Get(string name, Func<float[]> make, float peak = 0.9f)
    {
        if (s_cache.TryGetValue(name, out var c) && c != null) return c;
        float[] buf = make();
        Normalize(buf, peak);
        c = AudioClip.Create(name, buf.Length, 1, SR, false);
        c.SetData(buf, 0);
        s_cache[name] = c;
        return c;
    }

    public static int N(float seconds) => Mathf.CeilToInt(seconds * SR);

    // ── 노이즈 ───────────────────────────────────────────────────────────────
    public static float[] White(int n, int seed)
    {
        var r = new System.Random(seed);
        var b = new float[n];
        for (int i = 0; i < n; i++) b[i] = (float)(r.NextDouble() * 2.0 - 1.0);
        return b;
    }

    /// <summary>핑크 노이즈 (Paul Kellet 근사) — 자연스러운 바람·연소음의 바탕</summary>
    public static float[] Pink(int n, int seed)
    {
        var r = new System.Random(seed);
        var b = new float[n];
        float b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
        for (int i = 0; i < n; i++)
        {
            float w = (float)(r.NextDouble() * 2.0 - 1.0);
            b0 = 0.99886f * b0 + w * 0.0555179f;
            b1 = 0.99332f * b1 + w * 0.0750759f;
            b2 = 0.96900f * b2 + w * 0.1538520f;
            b3 = 0.86650f * b3 + w * 0.3104856f;
            b4 = 0.55000f * b4 + w * 0.5329522f;
            b5 = -0.7616f * b5 - w * 0.0168980f;
            b[i] = (b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362f) * 0.11f;
            b6 = w * 0.115926f;
        }
        return b;
    }

    /// <summary>브라운 노이즈 — 저역 럼블(연소·폭발 잔향)</summary>
    public static float[] Brown(int n, int seed)
    {
        var r = new System.Random(seed);
        var b = new float[n];
        float last = 0f;
        for (int i = 0; i < n; i++)
        {
            float w = (float)(r.NextDouble() * 2.0 - 1.0);
            last = (last + 0.02f * w) / 1.02f;
            b[i] = last * 3.5f;
        }
        return b;
    }

    // ── 바이쿼드 필터 (RBJ cookbook) ─────────────────────────────────────────
    public struct Biquad
    {
        float b0, b1, b2, a1, a2, x1, x2, y1, y2;

        public static Biquad LowPass(float fc, float q = 0.707f)  => Make(0, fc, q);
        public static Biquad HighPass(float fc, float q = 0.707f) => Make(1, fc, q);
        public static Biquad BandPass(float fc, float q = 1f)     => Make(2, fc, q);

        static Biquad Make(int type, float fc, float q)
        {
            float w0 = 2f * Mathf.PI * Mathf.Clamp(fc, 10f, SR * 0.45f) / SR;
            float cs = Mathf.Cos(w0), sn = Mathf.Sin(w0), al = sn / (2f * q);
            float a0 = 1f + al;
            var f = new Biquad();
            switch (type)
            {
                case 0: f.b0 = (1 - cs) / 2; f.b1 = 1 - cs;    f.b2 = (1 - cs) / 2; break;
                case 1: f.b0 = (1 + cs) / 2; f.b1 = -(1 + cs); f.b2 = (1 + cs) / 2; break;
                default: f.b0 = al;          f.b1 = 0;         f.b2 = -al;          break;
            }
            f.a1 = -2 * cs; f.a2 = 1 - al;
            f.b0 /= a0; f.b1 /= a0; f.b2 /= a0; f.a1 /= a0; f.a2 /= a0;
            return f;
        }

        public float Process(float x)
        {
            float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return y;
        }
    }

    public static float[] Filter(float[] src, Biquad f, int passes = 1)
    {
        var o = (float[])src.Clone();
        for (int p = 0; p < passes; p++)
        {
            var ff = f;
            for (int i = 0; i < o.Length; i++) o[i] = ff.Process(o[i]);
        }
        return o;
    }

    public static float[] LP(float[] s, float fc, int passes = 1) => Filter(s, Biquad.LowPass(fc), passes);
    public static float[] HP(float[] s, float fc, int passes = 1) => Filter(s, Biquad.HighPass(fc), passes);
    public static float[] BP(float[] s, float fc, float q)        => Filter(s, Biquad.BandPass(fc, q));

    // ── 유틸 ─────────────────────────────────────────────────────────────────
    public static void Mix(float[] dst, float[] src, float gain)
    {
        int n = Mathf.Min(dst.Length, src.Length);
        for (int i = 0; i < n; i++) dst[i] += src[i] * gain;
    }

    public static void Normalize(float[] b, float peak)
    {
        float m = 1e-6f;
        for (int i = 0; i < b.Length; i++) m = Mathf.Max(m, Mathf.Abs(b[i]));
        float g = peak / m;
        for (int i = 0; i < b.Length; i++) b[i] *= g;
    }

    /// <summary>끝부분을 시작부분과 등전력 크로스페이드해 이음매 없는 루프로 만든다 (길이가 fade 만큼 줄어듦)</summary>
    public static void MakeSeamless(ref float[] b, int fade)
    {
        int n = b.Length - fade;
        if (n <= fade) return;
        var o = new float[n];
        Array.Copy(b, o, n);
        for (int i = 0; i < fade; i++)
        {
            float t = (float)i / fade;
            float gIn = Mathf.Sin(t * Mathf.PI * 0.5f), gOut = Mathf.Cos(t * Mathf.PI * 0.5f);
            o[i] = b[i] * gIn + b[n + i] * gOut;
        }
        b = o;
    }

    /// <summary>attack(초) 선형 상승 후 decay 시정수 지수 감쇠</summary>
    public static float Env(float t, float attack, float decay)
        => (attack > 0f ? Mathf.Clamp01(t / attack) : 1f) * Mathf.Exp(-Mathf.Max(0f, t - attack) / Mathf.Max(1e-4f, decay));

    /// <summary>소프트 클리핑 — 큰 폭발음의 묵직한 포화감</summary>
    public static float Sat(float x, float drive) => (float)Math.Tanh(x * drive);

    /// <summary>주파수가 시간에 따라 변하는 사인 (위상 누적)</summary>
    public static float[] Sweep(int n, Func<float, float> freqAt, Func<float, float> ampAt)
    {
        var b = new float[n];
        double ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SR;
            ph += 2.0 * Math.PI * freqAt(t) / SR;
            b[i] = (float)Math.Sin(ph) * ampAt(t);
        }
        return b;
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 월드 3D 원샷 사운드 풀.
///  · 음속 지연 — 멀리서 터진 폭발은 섬광이 먼저 보이고 소리가 늦게 도착한다 (343 m/s)
///  · 거리별 공기 흡수 — 멀수록 고역이 깎여 둔탁해진다 (AudioLowPassFilter)
///  · 풀링 — 전투 중 AudioSource 를 새로 만들지 않는다
/// </summary>
public class SfxPlayer : MonoBehaviour
{
    const float SpeedOfSound = 343f;
    const int   PoolSize     = 24;

    static SfxPlayer s_inst;

    class Voice
    {
        public AudioSource src;
        public AudioLowPassFilter lpf;
        public float busyUntil;
    }

    struct Pending
    {
        public AudioClip clip; public Vector3 pos; public float vol, minDist, maxDist, at, pitch;
    }

    readonly List<Voice>   _voices  = new();
    readonly List<Pending> _pending = new();

    static SfxPlayer Inst
    {
        get
        {
            if (s_inst == null)
            {
                var go = new GameObject("[SfxPlayer]");
                s_inst = go.AddComponent<SfxPlayer>();
            }
            return s_inst;
        }
    }

    void Awake()
    {
        for (int i = 0; i < PoolSize; i++)
        {
            var go = new GameObject("Sfx" + i);
            go.transform.SetParent(transform, false);
            var v = new Voice { src = go.AddComponent<AudioSource>(), lpf = go.AddComponent<AudioLowPassFilter>() };
            v.src.playOnAwake  = false;
            v.src.spatialBlend = 1f;
            v.src.rolloffMode  = AudioRolloffMode.Custom;
            v.src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff);
            v.src.dopplerLevel = 0f;
            v.src.spread       = 40f;
            v.src.priority     = 90;
            _voices.Add(v);
        }
    }

    void OnDestroy() { if (s_inst == this) s_inst = null; }

    // 역제곱보다 완만한 감쇠 — 원거리 폭음이 '들리되 작게' 남는다 (상용 시뮬 공통 튜닝)
    static readonly AnimationCurve Rolloff = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(0.05f, 0.75f), new Keyframe(0.2f, 0.35f),
        new Keyframe(0.5f, 0.12f), new Keyframe(1f, 0f));

    /// <summary>pos 에서 clip 을 재생. delayBySound=true 면 청자 거리만큼 음속 지연</summary>
    public static void Play(AudioClip clip, Vector3 pos, float volume = 1f, float minDist = 30f, float maxDist = 6000f,
                            bool delayBySound = true, float pitch = 1f)
    {
        if (clip == null) return;
        var inst = Inst;
        float delay = 0f;
        var lis = Listener;
        if (delayBySound && lis != null) delay = Vector3.Distance(lis.position, pos) / SpeedOfSound;
        inst._pending.Add(new Pending { clip = clip, pos = pos, vol = volume, minDist = minDist, maxDist = maxDist,
                                        at = Time.time + delay, pitch = pitch });
    }

    /// <summary>2D (청자 기준) 원샷 — 콕핏 안 스위치·피탄음</summary>
    public static void Play2D(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;
        var v = Inst.Grab();
        if (v == null) return;
        v.src.spatialBlend = 0f;
        v.lpf.cutoffFrequency = 22000f;
        v.src.pitch = pitch;
        v.src.clip = clip;
        v.src.volume = volume;
        v.src.Play();
        v.busyUntil = Time.time + clip.length / Mathf.Max(0.1f, pitch) + 0.05f;
    }

    static Transform s_listener;
    public static Transform Listener
    {
        get
        {
            if (s_listener == null)
            {
                var l = FindAnyObjectByType<AudioListener>();
                s_listener = l != null ? l.transform : null;
            }
            return s_listener;
        }
    }

    /// <summary>거리(m) → 공기 흡수 저역통과 차단 주파수</summary>
    public static float AirAbsorptionCutoff(float dist)
        => Mathf.Lerp(22000f, 700f, Mathf.Clamp01(Mathf.Log10(1f + dist / 60f) / 2f));

    void Update()
    {
        float now = Time.time;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            if (p.at > now) continue;
            _pending.RemoveAt(i);
            var v = Grab();
            if (v == null) continue;
            v.src.transform.position = p.pos;
            v.src.spatialBlend = 1f;
            v.src.minDistance  = p.minDist;
            v.src.maxDistance  = p.maxDist;
            v.src.pitch        = p.pitch;
            var lis = Listener;
            float d = lis != null ? Vector3.Distance(lis.position, p.pos) : 0f;
            v.lpf.cutoffFrequency = AirAbsorptionCutoff(d);
            v.src.clip   = p.clip;
            v.src.volume = p.vol;
            v.src.Play();
            v.busyUntil = now + p.clip.length / Mathf.Max(0.1f, p.pitch) + 0.05f;
        }
    }

    Voice Grab()
    {
        float now = Time.time;
        Voice oldest = null;
        foreach (var v in _voices)
        {
            if (v.busyUntil <= now) return v;
            if (oldest == null || v.busyUntil < oldest.busyUntil) oldest = v;
        }
        return oldest;   // 전부 사용 중이면 가장 먼저 끝날 소리를 끊는다
    }
}

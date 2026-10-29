using UnityEngine;

// 비행 효과음 시스템: 엔진/바람 루프 + 무장 SFX (모두 PCM 코드 생성, 오디오 파일 불필요)
public class FlightAudioSystem : MonoBehaviour
{
    // ── 외부 이벤트 (MissileLauncher, HitEffectSystem에서 호출) ──────────────
    public static event System.Action               OnMissileFired;
    public static event System.Action               OnWeaponSelect;
    public static event System.Action<Vector3,bool> OnExplosionEvent;

    public static void NotifyMissileFired()                        => OnMissileFired?.Invoke();
    public static void NotifyWeaponSelect()                        => OnWeaponSelect?.Invoke();
    public static void NotifyExplosion(Vector3 pos, bool localHit) => OnExplosionEvent?.Invoke(pos, localHit);

    // ── AudioSource ──────────────────────────────────────────────────────────
    AudioSource _engineSrc;  // 루프: 스로틀 연동
    AudioSource _windSrc;    // 루프: 속도 연동
    AudioSource _sfxSrc;     // 단발 SFX

    // ── Clip ─────────────────────────────────────────────────────────────────
    AudioClip _engineClip;
    AudioClip _windClip;
    AudioClip _fireClip;
    AudioClip _explodeClip;
    AudioClip _flareClip;
    AudioClip _chaffClip;
    AudioClip _selectClip;

    PlayerController _local;
    float            _engineRpm;

    void Awake()
    {
        const int SR = 22050;
        _engineClip  = MakeEngineLoop(SR);
        _windClip    = MakeWindLoop(SR);
        _fireClip    = MakeMissileFire(SR);
        _explodeClip = MakeExplosion(SR);
        _flareClip   = MakeFlareDeploy(SR);
        _chaffClip   = MakeChaffDeploy(SR);
        _selectClip  = MakeSelectClick(SR);

        _engineSrc = MakeSrc(loop: true,  volume: 0f);
        _windSrc   = MakeSrc(loop: true,  volume: 0f);
        _sfxSrc    = MakeSrc(loop: false, volume: 1f);

        _engineSrc.clip = _engineClip;
        _windSrc.clip   = _windClip;
    }

    void Start()
    {
        CountermeasureSystem.OnDeploy += OnCMSDeploy;
        OnMissileFired   += PlayFire;
        OnWeaponSelect   += PlaySelect;
        OnExplosionEvent += PlayExplosion;

        _engineSrc.Play();
        _windSrc.Play();
    }

    void OnDestroy()
    {
        CountermeasureSystem.OnDeploy -= OnCMSDeploy;
        OnMissileFired   -= PlayFire;
        OnWeaponSelect   -= PlaySelect;
        OnExplosionEvent -= PlayExplosion;
    }

    void Update()
    {
        if (_local == null)
        {
            foreach (var pc in PlayerController.All)
                if (pc != null && pc.isLocalPlayer) { _local = pc; break; }
            if (_local == null) return;
        }

        float throttle = Mathf.Clamp01(_local.CurrentSpeed / 80f);

        // 엔진: 스로틀 레버(RPM)에 따라 피치·볼륨 — 강하로 속도만 붙을 때는 엔진음이 오르지 않는다
        _engineRpm = Mathf.MoveTowards(_engineRpm,
            Mathf.Max(_local.Throttle01, throttle * 0.35f) + (_local.AfterburnerActive ? 0.15f : 0f),
            Time.deltaTime * 0.8f);   // 스풀업/다운 지연
        _engineSrc.pitch  = Mathf.Lerp(0.55f, 1.85f, Mathf.Clamp01(_engineRpm));
        _engineSrc.volume = Mathf.Lerp(0.10f, 0.48f, Mathf.Clamp01(_engineRpm));

        // 바람: 속도 제곱 비례 (고속 시 급격히 커짐)
        float tSq = throttle * throttle;
        _windSrc.pitch  = Mathf.Lerp(0.50f, 1.40f, throttle);
        _windSrc.volume = Mathf.Lerp(0.00f, 0.18f, tSq);
    }

    // ── SFX 핸들러 ────────────────────────────────────────────────────────────
    void OnCMSDeploy(CountermeasureType type)
        => _sfxSrc.PlayOneShot(
            type == CountermeasureType.Flare ? _flareClip : _chaffClip, 0.70f);

    void PlayFire()
        => _sfxSrc.PlayOneShot(_fireClip, 0.90f);

    void PlayExplosion(Vector3 _, bool localHit)
        => _sfxSrc.PlayOneShot(_explodeClip, localHit ? 1.0f : 0.55f);

    // ── AudioSource 생성 ──────────────────────────────────────────────────────
    AudioSource MakeSrc(bool loop, float volume)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.spatialBlend = 0f;   // 2D 사운드
        src.loop         = loop;
        src.volume       = volume;
        src.playOnAwake  = false;
        src.priority     = 64;
        return src;
    }

    // ════════════════════════════════════════════════════════════════════════
    // PCM 생성 메서드 (오디오 파일 없이 코드로 직접 합성)
    // ════════════════════════════════════════════════════════════════════════

    // 제트 엔진 루프 (2초): 기본음 + 배음 + 터빈 노이즈
    static AudioClip MakeEngineLoop(int sr)
    {
        int     n   = sr * 2;
        float[] buf = new float[n];
        var     rng = new System.Random(42);
        for (int i = 0; i < n; i++)
        {
            float t    = (float)i / sr;
            float fund = Mathf.Sin(2f * Mathf.PI * 90f * t)  * 0.40f
                       + Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.18f
                       + Mathf.Sin(2f * Mathf.PI * 270f * t) * 0.09f
                       + Mathf.Sin(2f * Mathf.PI * 360f * t) * 0.05f;
            float noise = (float)(rng.NextDouble() * 2 - 1) * 0.11f;
            buf[i] = fund + noise;
        }
        CrossFade(buf, sr / 50); // 20ms 크로스페이드 (팝 방지)
        var clip = AudioClip.Create("Engine", n, 1, sr, false);
        clip.SetData(buf, 0);
        return clip;
    }

    // 바람/풍절음 루프 (3초): 로우패스 화이트 노이즈
    static AudioClip MakeWindLoop(int sr)
    {
        int     n   = sr * 3;
        float[] buf = new float[n];
        var     rng = new System.Random(7);
        float   lp  = 0f;
        for (int i = 0; i < n; i++)
        {
            float raw = (float)(rng.NextDouble() * 2 - 1);
            lp = Mathf.Lerp(lp, raw, 0.04f); // 로우패스 필터
            buf[i] = lp * 0.55f;
        }
        CrossFade(buf, sr / 50);
        var clip = AudioClip.Create("Wind", n, 1, sr, false);
        clip.SetData(buf, 0);
        return clip;
    }

    // 미사일 발사음 (0.45초): 쉭 + 로켓 착화 저주파
    static AudioClip MakeMissileFire(int sr)
    {
        int     n   = (int)(sr * 0.45f);
        float[] buf = new float[n];
        var     rng = new System.Random(13);
        float   dur = (float)n / sr;
        for (int i = 0; i < n; i++)
        {
            float t   = (float)i / sr;
            float env = Mathf.Clamp01(t / 0.008f) * Mathf.Clamp01((dur - t) / 0.12f);
            float noise = (float)(rng.NextDouble() * 2 - 1);
            float low   = Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.5f;
            buf[i] = (noise * 0.50f + low * 0.50f) * env;
        }
        var clip = AudioClip.Create("Fire", n, 1, sr, false);
        clip.SetData(buf, 0);
        return clip;
    }

    // 폭발음 (0.9초): 저주파 충격파 + 잔향
    static AudioClip MakeExplosion(int sr)
    {
        int     n   = (int)(sr * 0.9f);
        float[] buf = new float[n];
        var     rng = new System.Random(99);
        for (int i = 0; i < n; i++)
        {
            float t     = (float)i / sr;
            float env   = Mathf.Clamp01(t / 0.004f) * Mathf.Exp(-t * 4.0f);
            float noise = (float)(rng.NextDouble() * 2 - 1);
            float bass  = Mathf.Sin(2f * Mathf.PI * 50f * t) * 0.5f
                        + Mathf.Sin(2f * Mathf.PI * 85f * t) * 0.3f;
            buf[i] = (noise * 0.35f + bass * 0.65f) * env;
        }
        var clip = AudioClip.Create("Explode", n, 1, sr, false);
        clip.SetData(buf, 0);
        return clip;
    }

    // 플레어 투하음 (0.22초): 탁 + 치직 (마그네슘 착화)
    static AudioClip MakeFlareDeploy(int sr)
    {
        int     n   = (int)(sr * 0.22f);
        float[] buf = new float[n];
        var     rng = new System.Random(21);
        for (int i = 0; i < n; i++)
        {
            float t     = (float)i / sr;
            float env   = Mathf.Clamp01(t / 0.003f) * Mathf.Exp(-t * 20f);
            float click = Mathf.Sin(2f * Mathf.PI * 900f * t);
            float hiss  = (float)(rng.NextDouble() * 2 - 1);
            buf[i] = (click * 0.55f + hiss * 0.45f) * env;
        }
        var clip = AudioClip.Create("Flare", n, 1, sr, false);
        clip.SetData(buf, 0);
        return clip;
    }

    // 채프 투하음 (0.28초): 흩뿌리는 금속 노이즈
    static AudioClip MakeChaffDeploy(int sr)
    {
        int     n   = (int)(sr * 0.28f);
        float[] buf = new float[n];
        var     rng = new System.Random(55);
        for (int i = 0; i < n; i++)
        {
            float t   = (float)i / sr;
            float env = Mathf.Clamp01(t / 0.004f) * Mathf.Exp(-t * 9f);
            buf[i] = (float)(rng.NextDouble() * 2 - 1) * env * 0.80f;
        }
        var clip = AudioClip.Create("Chaff", n, 1, sr, false);
        clip.SetData(buf, 0);
        return clip;
    }

    // 루프 팝 방지: 시작/끝 페이드인/아웃
    static void CrossFade(float[] buf, int fadeSamples)
    {
        int n = buf.Length;
        for (int i = 0; i < fadeSamples && i < n; i++)
        {
            float f = (float)i / fadeSamples;
            buf[i]         *= f;
            buf[n - 1 - i] *= f;
        }
    }

    void PlaySelect() { if (_sfxSrc != null && _selectClip != null) _sfxSrc.PlayOneShot(_selectClip, 0.6f); }

    // 무장 선택 — 짧은 2연 클릭
    static AudioClip MakeSelectClick(int sr)
    {
        int n = Mathf.CeilToInt(sr * 0.09f);
        var buf = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / sr;
            float env = (t < 0.025f ? 1f - t / 0.025f : 0f) + (t > 0.05f && t < 0.075f ? 1f - (t - 0.05f) / 0.025f : 0f);
            buf[i] = Mathf.Sin(2f * Mathf.PI * 1800f * t) * env * 0.5f;
        }
        var clip = AudioClip.Create("WpnSelect", n, 1, sr, false);
        clip.SetData(buf, 0);
        return clip;
    }
}

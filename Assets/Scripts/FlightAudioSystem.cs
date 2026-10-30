using UnityEngine;

// 비행 사운드 디렉터 (로컬 청자 기준) — 모두 PCM 코드 합성, 오디오 파일 불필요.
//  · 엔진음은 기체별 AircraftEngineAudio 가 담당 (레이어·지향성·도플러)
//  · 여기서는 공력음(풍압·풍절·버핏), 콕핏 바닥 소음, G 호흡, 바다 환경음,
//    무장·기만체 SFX, 거리별 폭발음(근거리 크랙/원거리 굉음 + 음속 지연)을 믹스한다
//  · G-LOC 로 의식이 흐려지면 전체 음이 멀어진다
public class FlightAudioSystem : MonoBehaviour
{
    // ── 외부 이벤트 (MissileLauncher, HitEffectSystem에서 호출) ──────────────
    public static event System.Action               OnMissileFired;
    public static event System.Action               OnWeaponSelect;
    public static event System.Action<Vector3,bool> OnExplosionEvent;

    public static void NotifyMissileFired()                        => OnMissileFired?.Invoke();
    public static void NotifyWeaponSelect()                        => OnWeaponSelect?.Invoke();
    public static void NotifyExplosion(Vector3 pos, bool localHit) => OnExplosionEvent?.Invoke(pos, localHit);

    // ── 루프 ─────────────────────────────────────────────────────────────────
    AudioSource _windLow, _windHiss, _buffet, _hum, _sea;
    AudioLowPassFilter _windLpf;

    PlayerController _local;
    FlightCamera     _cam;
    float _buffetAmt, _breathTimer, _gVol;
    bool  _exhaleNext = true;

    void Awake()
    {
        // 첫 교전에서 합성하느라 끊기지 않게 씬 로드 시 미리 만든다
        _ = FlightSounds.EngineCore; _ = FlightSounds.EngineWhine; _ = FlightSounds.EngineBuzz;
        _ = FlightSounds.EngineRoar; _ = FlightSounds.Afterburner; _ = FlightSounds.GunLoop;
        _ = FlightSounds.GunTail;    _ = FlightSounds.MissileLaunch; _ = FlightSounds.MissileMotor;
        _ = FlightSounds.ExplosionNear; _ = FlightSounds.ExplosionFar; _ = FlightSounds.HitMetal;
        _ = FlightSounds.HitFar;     _ = FlightSounds.FlarePop; _ = FlightSounds.ChaffPop;
        _ = FlightSounds.AfterburnerLight; _ = FlightSounds.Flyby; _ = FlightSounds.WaterSplash;

        _windLow  = MakeLoop(FlightSounds.WindLow);
        _windHiss = MakeLoop(FlightSounds.WindHiss);
        _windLpf  = _windHiss.gameObject.AddComponent<AudioLowPassFilter>();
        _buffet   = MakeLoop(FlightSounds.Buffet);
        _hum      = MakeLoop(FlightSounds.CockpitHum);
        _sea      = MakeLoop(FlightSounds.SeaAmbience);
    }

    void Start()
    {
        CountermeasureSystem.OnDeploy += OnCMSDeploy;
        OnMissileFired   += PlayFire;
        OnWeaponSelect   += PlaySelect;
        OnExplosionEvent += PlayExplosion;
    }

    void OnDestroy()
    {
        CountermeasureSystem.OnDeploy -= OnCMSDeploy;
        OnMissileFired   -= PlayFire;
        OnWeaponSelect   -= PlaySelect;
        OnExplosionEvent -= PlayExplosion;
        AudioListener.volume = 1f;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (_local == null || !_local.isLocalPlayer)
        {
            _local = null;
            foreach (var pc in PlayerController.All)
                if (pc != null && pc.isLocalPlayer) { _local = pc; break; }
        }
        if (_cam == null) _cam = FindAnyObjectByType<FlightCamera>();

        var  gm      = GameModeManager.Instance;
        bool flying  = _local != null && gm != null && gm.IsFlying && !gm.IsDead;
        bool inside  = _cam != null && _cam.IsInsideCockpit;
        float v      = flying ? _local.CurrentSpeed : 0f;
        float vn     = Mathf.Clamp01(v / 120f);
        float q      = vn * vn;                       // 동압 ∝ v²

        // ── 공력음 ──────────────────────────────────────────────────────
        Smooth(_windLow,  flying ? (0.04f + 0.5f * q) * (inside ? 0.7f : 1f) : 0f, Mathf.Lerp(0.7f, 1.2f, vn), dt);
        Smooth(_windHiss, flying ? 0.38f * q * (inside ? 1f : 0.75f) : 0f,           Mathf.Lerp(0.8f, 1.25f, vn), dt);
        _windLpf.cutoffFrequency = inside ? 5500f : 12000f;

        // 버핏 — 가용 G 한계 근접 · 실속 경고 · 실속
        float buffetT = 0f;
        if (flying)
        {
            float gRatio = _local.AvailableG > 0.1f ? _local.PilotG / _local.AvailableG : 0f;
            buffetT = Mathf.Clamp01((gRatio - 0.82f) / 0.18f) * 0.7f;
            if (_local.StallWarning) buffetT = Mathf.Max(buffetT, 0.5f);
            if (_local.IsStalling)   buffetT = 1f;
        }
        _buffetAmt = Mathf.MoveTowards(_buffetAmt, buffetT, dt * 2.5f);
        Smooth(_buffet, _buffetAmt * 0.65f, 1f, dt, 10f);
        if (_buffetAmt > 0.05f) _cam?.AddBuffetShake(_buffetAmt);

        // 콕핏 바닥 소음 (환기·항전 팬)
        bool inCockpitAny = inside && (flying || (gm != null && gm.IsBoardedInCockpit));
        Smooth(_hum, inCockpitAny ? 0.12f : 0f, 1f, dt);

        // 바다 — 갑판·저공에서. 콕핏 안 비행 중엔 거의 안 들린다
        var lis = SfxPlayer.Listener;
        float h = lis != null ? lis.position.y - FXLib.SeaY : 1000f;
        float seaT = 0.42f * Mathf.Clamp01(1f - (h - 5f) / 140f) * (flying ? (inside ? 0.08f : 0.4f) : (inside ? 0.3f : 1f));
        Smooth(_sea, seaT, 1f, dt, 1.5f);

        // ── G 호흡 (L-1 항G 호흡: 짧고 거친 날숨 → 빠른 들숨) ─────────────
        float g = flying ? _local.PilotG : 1f;
        _gVol = Mathf.MoveTowards(_gVol, inside && flying ? Mathf.Clamp01((g - 5f) / 3.5f) : 0f, dt * 2f);
        if (_gVol > 0.05f)
        {
            _breathTimer -= dt;
            if (_breathTimer <= 0f)
            {
                SfxPlayer.Play2D(_exhaleNext ? FlightSounds.GStrainExhale : FlightSounds.GStrainInhale,
                                 (_exhaleNext ? 0.42f : 0.28f) * _gVol, Random.Range(0.95f, 1.05f));
                _breathTimer = _exhaleNext ? 0.55f : Mathf.Lerp(2.6f, 1.4f, _gVol);
                _exhaleNext = !_exhaleNext;
            }
        }
        else { _breathTimer = 0.2f; _exhaleNext = true; }

        // ── G-LOC: 의식이 흐려지면 소리가 멀어진다 ────────────────────────
        var gloc = GLocSystem.Instance;
        float stress = gloc != null && flying ? Mathf.Clamp01(gloc.Stress) : 0f;
        float target = gloc != null && gloc.Unconscious ? 0.15f : Mathf.Lerp(1f, 0.55f, Mathf.Clamp01((stress - 0.6f) / 0.4f));
        AudioListener.volume = Mathf.MoveTowards(AudioListener.volume, target, dt * 1.5f);
    }

    static void Smooth(AudioSource s, float vol, float pitch, float dt, float rate = 4f)
    {
        s.volume = Mathf.MoveTowards(s.volume, vol, dt * rate * 0.5f);
        s.pitch  = Mathf.Lerp(s.pitch, pitch, 1f - Mathf.Exp(-dt * 3f));
        if (s.volume > 0.001f) { if (!s.isPlaying) s.Play(); }
        else if (s.isPlaying) s.Pause();
    }

    // ── SFX 핸들러 ────────────────────────────────────────────────────────────
    void OnCMSDeploy(CountermeasureType type)
        => SfxPlayer.Play2D(type == CountermeasureType.Flare ? FlightSounds.FlarePop : FlightSounds.ChaffPop,
                            0.6f, Random.Range(0.94f, 1.06f));

    void PlayFire()
        => SfxPlayer.Play2D(FlightSounds.MissileLaunch, _cam != null && _cam.IsInsideCockpit ? 0.75f : 0.95f, Random.Range(0.96f, 1.04f));

    void PlaySelect() => SfxPlayer.Play2D(FlightSounds.SwitchClick, 0.5f);

    void PlayExplosion(Vector3 pos, bool localHit)
    {
        if (localHit)
        {
            SfxPlayer.Play2D(FlightSounds.ExplosionNear, 1f);
            SfxPlayer.Play2D(FlightSounds.HitMetal, 0.7f, 0.8f);
            return;
        }
        var lis = SfxPlayer.Listener;
        float d = lis != null ? Vector3.Distance(lis.position, pos) : 0f;
        // 근거리는 날카로운 충격파 크랙, 멀면 고역이 흡수된 둔탁한 굉음 — 둘 다 음속만큼 늦게 도착
        if (d < 700f) SfxPlayer.Play(FlightSounds.ExplosionNear, pos, 1f, 60f, 9000f, true, Random.Range(0.92f, 1.06f));
        else          SfxPlayer.Play(FlightSounds.ExplosionFar,  pos, 1f, 250f, 14000f, true, Random.Range(0.9f, 1.05f));
    }

    // ── AudioSource 생성 ──────────────────────────────────────────────────────
    AudioSource MakeLoop(AudioClip clip)
    {
        var go = new GameObject(clip != null ? clip.name : "Loop");
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.clip         = clip;
        src.spatialBlend = 0f;   // 2D — 청자 주변의 공기·콕핏
        src.loop         = true;
        src.volume       = 0f;
        src.playOnAwake  = false;
        src.priority     = 40;
        return src;
    }
}

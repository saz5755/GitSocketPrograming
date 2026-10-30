using UnityEngine;

/// <summary>
/// 기체마다 붙는 엔진 사운드 — 상용 비행시뮬처럼 레이어를 RPM·청자 방향·거리·시점으로 믹스한다.
///   코어 럼블 / 터빈 휘슬 / 팬 버즈쏘 / 배기 로어 / 애프터버너 (+ 원격 기체 기총)
///  · 지향성: 기체 앞에서는 휘슬·버즈, 뒤에서는 로어·AB 가 지배적 (실제 제트기 소음 패턴)
///  · 스풀 지연: 스로틀을 밀어도 RPM 은 천천히 따라온다
///  · 콕핏: 캐노피가 고역을 막아 낮고 둔탁하게, 외부 시점: 3D 로 공간감·도플러
///  · 공기 흡수: 멀수록 고역이 깎이고, 근접 통과 시 '휘익' 플라이바이
/// PlayerController.Awake 에서 모든 기체(로컬·원격·AI)에 붙는다.
/// </summary>
public class AircraftEngineAudio : MonoBehaviour
{
    enum L { Core, Whine, Buzz, Roar, AB, Gun, Count }

    // 레이어별 외부 기준 볼륨
    static readonly float[] BaseVol = { 0.55f, 0.32f, 0.26f, 0.75f, 0.9f, 0.7f };
    // 콕핏 안에서의 볼륨 배율과 차단 주파수 (캐노피 차음)
    static readonly float[] CkptVol = { 0.85f, 0.45f, 0.18f, 0.32f, 0.42f, 0f };
    static readonly float[] CkptLpf = { 22000f, 3200f, 1800f, 900f, 700f, 22000f };

    const float MaxHearDist = 7000f;

    PlayerController  _pc;
    AIBotController   _bot;
    F35VFXController  _vfx;
    FlightCamera      _cam;

    AudioSource[]        _src = new AudioSource[(int)L.Count];
    AudioLowPassFilter[] _lpf = new AudioLowPassFilter[(int)L.Count];

    float _rpm, _ab, _interior, _lastSpeed, _speed, _gunTimer;
    bool  _wasAB, _built, _paused = true, _started;
    Vector3 _lastPos;
    float _prevDist = float.MaxValue, _flybyCooldown;
    bool  _closing;

    const float HoldAfterStop = 20f;   // 착함·정지 후 엔진을 끄기까지 (지상 아이들)
    float _lastMovingTime = -999f;

    /// <summary>이 기체의 엔진이 돌고 있다고 보는가 — 항법등 등 다른 연출이 같은 판정을 쓴다</summary>
    public bool EngineRunning { get; private set; }

    public static AircraftEngineAudio Ensure(GameObject aircraft)
    {
        var a = aircraft.GetComponent<AircraftEngineAudio>();
        return a != null ? a : aircraft.AddComponent<AircraftEngineAudio>();
    }

    /// <summary>원격 기체의 기총 발사를 알림 — 짧은 시간 동안 3D 기총음이 이어진다</summary>
    public void NotifyGunFire() => _gunTimer = 0.14f;

    void Start()
    {
        _pc  = GetComponent<PlayerController>();
        _bot = GetComponent<AIBotController>();
        _vfx = GetComponent<F35VFXController>();
        _lastPos = transform.position;
    }

    void Build()
    {
        AudioClip[] clips =
        {
            FlightSounds.EngineCore, FlightSounds.EngineWhine, FlightSounds.EngineBuzz,
            FlightSounds.EngineRoar, FlightSounds.Afterburner, FlightSounds.GunLoop
        };
        var rnd = new System.Random(Random.Range(int.MinValue, int.MaxValue));
        for (int i = 0; i < (int)L.Count; i++)
        {
            var go = new GameObject("EngAudio_" + (L)i);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = i == (int)L.Gun ? new Vector3(0f, 1.2f, 4f) : new Vector3(0f, 1.2f, -3.5f);
            var s = go.AddComponent<AudioSource>();
            s.clip         = clips[i];
            s.loop         = true;
            s.playOnAwake  = false;
            s.volume       = 0f;
            s.spatialBlend = 1f;
            s.rolloffMode  = AudioRolloffMode.Custom;
            s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff);
            s.minDistance  = 25f;
            s.maxDistance  = MaxHearDist;
            s.spread       = 60f;
            s.dopplerLevel = 0.7f;
            s.priority     = 110;
            _src[i] = s;
            _lpf[i] = go.AddComponent<AudioLowPassFilter>();
            _lpf[i].cutoffFrequency = 22000f;
            // 여러 기체가 같은 루프를 같은 위치에서 재생하면 위상이 겹쳐 '코러스'처럼 들린다 → 시작점 분산
            if (clips[i] != null) s.timeSamples = rnd.Next(clips[i].samples);
        }
        _built = true;
    }

    static readonly AnimationCurve Rolloff = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(0.02f, 0.8f), new Keyframe(0.08f, 0.38f),
        new Keyframe(0.3f, 0.1f), new Keyframe(1f, 0f));

    void OnDisable()
    {
        if (!_built) return;
        foreach (var s in _src) if (s != null) s.Stop();
        _paused = true;
        _started = false;
        _rpm = _ab = 0f;
    }

    void Update()
    {
        if (_pc == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        bool local = _pc.isLocalPlayer;
        var  gm    = GameModeManager.Instance;

        // ── 속도 추정 (원격·AI 는 위치 변화로) ─────────────────────────────
        float v = (transform.position - _lastPos).magnitude / dt;
        if (v > 600f) v = _speed;                         // 순간이동
        _lastPos = transform.position;
        _speed   = Mathf.Lerp(_speed, local ? _pc.CurrentSpeed : v, 1f - Mathf.Exp(-dt * 3f));
        float accel = (_speed - _lastSpeed) / dt;
        _lastSpeed = _speed;

        // ── 엔진 가동 여부와 목표 RPM ──────────────────────────────────────
        bool running;
        float rpmT, abT;
        if (local)
        {
            bool idle = _vfx != null && _vfx.EngineIdleOn;
            running = (gm != null && gm.IsFlying && !gm.IsDead && _pc.enabled) || idle;
            rpmT = running ? Mathf.Lerp(0.3f, 1f, _pc.Throttle01) : 0f;
            abT  = running && (_pc.AfterburnerActive || (_vfx != null && _vfx.AfterburnerOn)) ? 1f : 0f;
            if (_pc.IsEngineDamaged) rpmT *= 0.85f;
        }
        else
        {
            // 원격·봇: 실제로 움직이는 중이거나 최근 HoldAfterStop 초 안에 움직였을 때만 가동 — 갑판에 주기된 기체는 조용히
            // (AI 봇은 주기 중에도 IsFlying=true, 호스트 봇은 PlayerController 가 꺼진 채 난다)
            bool moving = _bot != null ? _bot.Speed > 1f : _speed > 3f;
            if (moving) _lastMovingTime = Time.time;
            running = !_pc.IsWreck && Time.time - _lastMovingTime < HoldAfterStop;
            float bs = _bot != null ? Mathf.Max(_bot.Speed, _speed) : _speed;
            rpmT = running ? Mathf.Clamp(0.32f + bs / 120f * 0.55f + Mathf.Max(0f, accel) * 0.04f, 0.3f, 1f) : 0f;
            abT  = running && (bs > 100f || accel > 5f || (_vfx != null && _vfx.AfterburnerOn)) ? 1f : 0f;
        }
        EngineRunning = running;
        if (_gunTimer > 0f) _gunTimer -= dt;

        // 스풀 — 올라갈 땐 느리게, 내려갈 땐 조금 더 느리게 (관성)
        _rpm = Mathf.MoveTowards(_rpm, rpmT, dt * (rpmT > _rpm ? 0.32f : 0.22f));
        _ab  = Mathf.MoveTowards(_ab, abT, dt * (abT > _ab ? 3.5f : 1.6f));

        // AB 점화음
        bool abOn = abT > 0.5f;
        if (abOn && !_wasAB && running && _rpm > 0.6f)
        {
            if (local && IsInterior()) SfxPlayer.Play2D(FlightSounds.AfterburnerLight, 0.35f);
            else SfxPlayer.Play(FlightSounds.AfterburnerLight, transform.position, local ? 0.8f : 1f, 25f, 3000f, !local);
        }
        _wasAB = abOn;

        // ── 청자 기준 믹스 ───────────────────────────────────────────────
        var lis = SfxPlayer.Listener;
        if (lis == null) return;
        Vector3 toLis = lis.position - transform.position;
        float dist = toLis.magnitude;
        bool audible = (_rpm > 0.01f || _gunTimer > 0f) && dist < MaxHearDist;

        if (!audible)
        {
            if (!_paused && _built) { foreach (var s in _src) s.Pause(); _paused = true; }
            return;
        }
        if (!_built) Build();
        if (_paused)
        {
            // 처음이거나 Stop 된 뒤면 Play, 일시정지였으면 이어서
            foreach (var s in _src) { if (_started) s.UnPause(); else s.Play(); }
            _started = true;
            _paused = false;
        }

        float interiorT = local && IsInterior() ? 1f : 0f;
        _interior = Mathf.MoveTowards(_interior, interiorT, dt * 4f);

        float cosA  = dist > 0.1f ? Vector3.Dot(transform.forward, toLis / dist) : 0f;
        float front = Mathf.Clamp01(cosA);
        float rear  = Mathf.Clamp01(-cosA);
        float air   = SfxPlayer.AirAbsorptionCutoff(dist);
        float r     = _rpm;

        // 레이어별 (볼륨, 피치)
        var vol = _vol; var pit = _pit;
        vol[(int)L.Core]  = Mathf.Lerp(0.25f, 1f, r);
        pit[(int)L.Core]  = Mathf.Lerp(0.7f, 1.2f, r);
        vol[(int)L.Whine] = Mathf.Lerp(0.55f, 0.9f, r) * Mathf.Lerp(0.55f, 1.15f, front) * (1f - 0.3f * _ab);
        pit[(int)L.Whine] = Mathf.Lerp(0.5f, 1.35f, r);
        vol[(int)L.Buzz]  = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.68f, 1f, r)) * Mathf.Lerp(0.25f, 1.2f, front);
        pit[(int)L.Buzz]  = Mathf.Lerp(0.88f, 1.12f, r);
        vol[(int)L.Roar]  = Mathf.Lerp(0.12f, 1f, Mathf.Pow(r, 1.6f)) * Mathf.Lerp(0.45f, 1.1f, Mathf.Pow(rear, 0.7f));
        pit[(int)L.Roar]  = Mathf.Lerp(0.72f, 1.12f, r);
        vol[(int)L.AB]    = _ab * Mathf.Lerp(0.5f, 1.15f, rear);
        pit[(int)L.AB]    = Mathf.Lerp(0.92f, 1.04f, _ab) * Mathf.Lerp(0.95f, 1.05f, r);
        vol[(int)L.Gun]   = !local && _gunTimer > 0f ? 1f : 0f;
        pit[(int)L.Gun]   = 1f;

        bool localExt = local && _interior < 0.5f;
        for (int i = 0; i < (int)L.Count; i++)
        {
            var s = _src[i];
            float ext = BaseVol[i] * vol[i];
            float ins = BaseVol[i] * CkptVol[i] * vol[i];
            s.volume = Mathf.Lerp(ext, ins, _interior);
            s.pitch  = pit[i];
            s.spatialBlend = 1f - _interior;
            s.dopplerLevel = local ? 0f : 0.7f;
            s.priority     = local ? 20 : 110 + Mathf.Clamp((int)(dist / 100f), 0, 120);
            float cut = Mathf.Lerp(local && localExt ? 22000f : air, CkptLpf[i], _interior);
            _lpf[i].cutoffFrequency = cut;
        }

        // 기총 정지 꼬리
        if (!local && _gunTimer <= 0f && _src[(int)L.Gun].volume <= 0f && _gunWasOn)
            SfxPlayer.Play(FlightSounds.GunTail, transform.position + transform.forward * 4f, 0.7f, 30f, 3000f, false);
        _gunWasOn = vol[(int)L.Gun] > 0f;

        // 근접 통과 '휘익' — 가까이 다가오다 멀어지기 시작한 순간
        if (!local)
        {
            _flybyCooldown -= dt;
            bool closingNow = dist < _prevDist;
            if (_closing && !closingNow && _prevDist < 160f && _speed > 45f && _flybyCooldown <= 0f)
            {
                SfxPlayer.Play(FlightSounds.Flyby, transform.position, Mathf.Lerp(1f, 0.45f, _prevDist / 160f), 20f, 1500f, false);
                _flybyCooldown = 3f;
            }
            _closing  = closingNow;
            _prevDist = dist;
        }
    }

    bool _gunWasOn;
    readonly float[] _vol = new float[(int)L.Count];
    readonly float[] _pit = new float[(int)L.Count];

    bool IsInterior()
    {
        if (_cam == null) _cam = FindAnyObjectByType<FlightCamera>();
        return _cam != null && _cam.IsInsideCockpit;
    }
}

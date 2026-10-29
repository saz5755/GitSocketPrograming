using UnityEngine;

/// <summary>
/// 플레이어 미사일 무장 — AIM-9X(적외선, 단거리)와 AIM-120(능동 레이더, 중거리)을 선택해 운용한다.
///
///   · R : 무장 선택 (AIM-9X ↔ AIM-120)     · Tab : 레이더 락 (TargetingSystem)     · Space : 발사
///   · AIM-9X : 시커가 기수 전방(또는 레이더 락 표적)의 열원을 포착 → 그로울 톤, 포착 완료 → 고음 락 톤.
///              패시브 시커라 상대 RWR 에 락 경보가 뜨지 않는다.
///   · AIM-120: 레이더 락 필요. 발사 후 중간유도 동안 표적을 레이더 시야(60°) 안에 두어야 데이터링크 유지.
///   · DLZ   : 현재 표적에 대해 Rmax(표적 현 기동 유지) / Rne(표적이 즉시 도주해도 명중) / Rmin 계산 — HUD 큐
/// </summary>
public class MissileLauncher : MonoBehaviour
{
    public enum Weapon { AIM9X, AIM120 }
    public enum SeekerState { Off, Searching, Locked }

    [SerializeField] float cooldown = 1.0f;
    [Tooltip("AIM-9X 시커 무유도(uncaged) 포착 원뿔 반각 (°)")]
    [SerializeField] float irAcquireCone = 20f;
    [Tooltip("레이더 락 표적에 시커를 종속(slave)할 수 있는 최대 기수 이탈각 (°)")]
    [SerializeField] float irSlaveCone = 45f;
    [Tooltip("IR 시커 포착에 걸리는 시간 (s)")]
    [SerializeField] float irLockTime = 0.7f;
    [Tooltip("Rne 계산 시 가정하는 표적 최고 도주 속도 (m/s)")]
    [SerializeField] float targetEscapeSpeed = 120f;

    [Tooltip("미사일 프리팹. 미할당 시 Resources/MissilePrefab 폴백 → 그것도 없으면 빈 GameObject + MissileController")]
    [SerializeField] GameObject _missilePrefab;

    TargetingSystem _targeting;
    float           _cooldownTimer;

    // ── 공개 상태 (HUD) ───────────────────────────────────────────────────
    public Weapon           Selected     { get; private set; } = Weapon.AIM9X;
    public int              Aim9Count    { get; private set; }
    public int              Aim120Count  { get; private set; }
    public int              Count(Weapon w) => w == Weapon.AIM9X ? Aim9Count : Aim120Count;
    /// <summary>선택 무장의 잔탄 (구형 HUD 호환)</summary>
    public int              MissileCount => Count(Selected);
    public MissileSpec      SelectedSpec => SpecOf(Selected);

    public SeekerState      Seeker       { get; private set; }
    public PlayerController SeekerTarget { get; private set; }

    /// <summary>현재 무장의 발사 대상 (IR: 시커 표적, 레이더: 락 표적)</summary>
    public PlayerController ShotTarget   { get; private set; }
    public bool   CanFire    { get; private set; }
    public float  Range      { get; private set; }
    public float  RMax       { get; private set; }
    public float  RNe        { get; private set; }
    public float  RMin       => SelectedSpec.minRange;
    public bool   InRange    => ShotTarget != null && Range >= RMin && Range <= RMax;
    public bool   InNoEscape => ShotTarget != null && Range >= RMin && Range <= RNe;

    /// <summary>HUD 발사 큐 — SHOOT(무탈출 구역) / IN RNG / OUT RNG / MIN RNG / STEER / TONE / NO LOCK</summary>
    public string Cue(out bool urgent)
    {
        urgent = false;
        if (Count(Selected) == 0) return "EMPTY";
        if (ShotTarget == null)
        {
            if (Selected == Weapon.AIM9X) return SeekerTarget != null ? "TONE" : "SRCH";
            return _targeting != null && _targeting.Target != null ? "LOCKING" : "NO LOCK";
        }
        if (Range < RMin)   return "MIN RNG";
        if (!CanFire)       return "STEER";
        if (InNoEscape)     { urgent = true; return "SHOOT"; }
        return InRange ? "IN RNG" : "OUT RNG";
    }

    static MissileSpec SpecOf(Weapon w) => w == Weapon.AIM9X ? GameplayConfigSO.Instance.aim9x : GameplayConfigSO.Instance.aim120;

    void Awake()
    {
        _targeting = GetComponent<TargetingSystem>();
        Rearm();
    }

    void Start() => BuildTone();

    /// <summary>새 출격 — 미사일 만재</summary>
    public void Rearm()
    {
        var cfg = GameplayConfigSO.Instance;
        Aim9Count      = cfg.aim9xLoadout;
        Aim120Count    = cfg.aim120Loadout;
        _cooldownTimer = 0f;
        if (Count(Selected) == 0) Selected = Aim9Count > 0 ? Weapon.AIM9X : Weapon.AIM120;
    }

    void Update()
    {
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;
        if (_targeting == null) _targeting = GetComponent<TargetingSystem>();

        bool active = GameModeManager.Instance?.IsFlying == true && RoomModeSO.WeaponsEnabled
                   && _targeting != null && _targeting.LocalPlayer != null;
        if (!active)
        {
            Seeker = SeekerState.Off; SeekerTarget = null; ShotTarget = null; CanFire = false;
            UpdateTone();
            return;
        }

        if (SimInput.Pressed(SimAction.WeaponSelect)) CycleWeapon();

        UpdateSeeker();
        UpdateShotSolution();
        UpdateTone();

        if (SimInput.Pressed(SimAction.FireMissile)) TryFire();

        BroadcastLockState();
    }

    void CycleWeapon()
    {
        var next = Selected == Weapon.AIM9X ? Weapon.AIM120 : Weapon.AIM9X;
        if (Count(next) == 0 && Count(Selected) > 0) return;   // 다른 무장이 바닥이면 유지
        Selected = next;
        _irLockTimer = 0f;
        FlightAudioSystem.NotifyWeaponSelect();
    }

    // ── AIM-9X 시커 ───────────────────────────────────────────────────────
    float _irLockTimer;

    void UpdateSeeker()
    {
        if (Selected != Weapon.AIM9X || Aim9Count == 0) { Seeker = SeekerState.Off; SeekerTarget = null; return; }

        var  me    = _targeting.LocalPlayer.transform;
        var  spec  = SelectedSpec;
        PlayerController cand = null;

        // 레이더 락 표적이 있으면 시커를 그쪽으로 종속 (HOBS)
        var radarTgt = _targeting.Target;
        if (radarTgt != null && Angle(me, radarTgt.transform.position) <= irSlaveCone &&
            Vector3.Distance(me.position, radarTgt.transform.position) <= spec.seekerRange)
            cand = radarTgt;
        else
        {
            // 무유도: 기수 전방 원뿔 안에서 가장 중심에 가까운 열원
            float best = irAcquireCone;
            foreach (var pc in _targeting.Enemies)
            {
                if (pc == null) continue;
                float d = Vector3.Distance(me.position, pc.transform.position);
                if (d > spec.seekerRange) continue;
                float a = Angle(me, pc.transform.position);
                if (a < best) { best = a; cand = pc; }
            }
        }

        if (cand == null) { Seeker = SeekerState.Searching; SeekerTarget = null; _irLockTimer = 0f; return; }
        if (cand != SeekerTarget) { SeekerTarget = cand; _irLockTimer = 0f; }

        // 꼬리 쪽(엔진 배기)이 정면보다 빨리 잡힌다
        float aspect = Vector3.Angle(cand.transform.forward, me.position - cand.transform.position);
        float rate   = aspect < 70f ? 1.3f : aspect > 120f ? 0.75f : 1f;
        _irLockTimer += Time.deltaTime * rate;
        Seeker = _irLockTimer >= irLockTime ? SeekerState.Locked : SeekerState.Searching;
    }

    static float Angle(Transform me, Vector3 worldPos) => Vector3.Angle(me.forward, worldPos - me.position);

    // ── 발사 해 (DLZ) ─────────────────────────────────────────────────────
    float   _dlzTimer;
    Vector3 _tgtPrevPos, _tgtVel;
    PlayerController _velTarget;

    void UpdateShotSolution()
    {
        var me   = _targeting.LocalPlayer;
        var spec = SelectedSpec;

        ShotTarget = Selected == Weapon.AIM9X
            ? (Seeker == SeekerState.Locked ? SeekerTarget : null)
            : (_targeting.State == TargetingSystem.LockState.Locked ? _targeting.Target : null);

        // DLZ 는 락이 완료되기 전에도 표시할 수 있게 후보 표적으로 계산
        var dlzTarget = ShotTarget ?? (Selected == Weapon.AIM9X ? SeekerTarget : _targeting.Target);
        if (dlzTarget != _velTarget)
        {
            _velTarget = dlzTarget;
            _tgtVel    = Vector3.zero;
            if (dlzTarget != null) _tgtPrevPos = dlzTarget.transform.position;
        }

        if (dlzTarget == null) { Range = 0f; CanFire = false; return; }

        Vector3 tp = dlzTarget.transform.position;
        float dt = Time.deltaTime;
        if (dt > 0f) _tgtVel = Vector3.Lerp(_tgtVel, (tp - _tgtPrevPos) / dt, 0.15f);
        _tgtPrevPos = tp;

        Range = Vector3.Distance(me.transform.position, tp);

        _dlzTimer -= dt;
        if (_dlzTimer <= 0f)
        {
            _dlzTimer = 0.25f;
            Vector3 los      = (tp - me.transform.position).normalized;
            float   opening  = Vector3.Dot(_tgtVel, los);    // 표적이 멀어지는 속도
            float   launchV  = me.CurrentSpeed;
            RMax = spec.MaxRangeAgainst(launchV, opening);
            RNe  = spec.MaxRangeAgainst(launchV, targetEscapeSpeed);
        }

        CanFire = ShotTarget != null && Count(Selected) > 0 && _cooldownTimer <= 0f
               && Angle(me.transform, tp) <= spec.launchOffBoresight;
    }

    void TryFire()
    {
        if (!CanFire)
        {
            // 조용히 무시하면 조작이 틀렸는지 고장인지 알 수 없다 — 발사 불가 사유를 띄운다
            CombatFeedUI.Instance?.ShowNotice(DenyReason(), warn: true);
            return;
        }
        var me = _targeting.LocalPlayer;

        _cooldownTimer = cooldown;
        if (Selected == Weapon.AIM9X) Aim9Count--; else Aim120Count--;

        FlightAudioSystem.NotifyMissileFired();
        CreateMissile(me, ShotTarget, SelectedSpec);

        // 잔탄이 바닥나면 남은 무장으로 자동 전환
        if (Count(Selected) == 0)
        {
            var other = Selected == Weapon.AIM9X ? Weapon.AIM120 : Weapon.AIM9X;
            if (Count(other) > 0) Selected = other;
        }
    }

    /// <summary>지금 발사할 수 없는 이유 (발사 가능하면 null)</summary>
    public string DenyReason()
    {
        if (CanFire) return null;
        var    spec = SelectedSpec;
        string name = spec.displayName;
        string r    = PilotGuideUI.K(SimAction.WeaponSelect), lk = PilotGuideUI.K(SimAction.LockTarget);
        if (Count(Selected) == 0) return $"{name} 소진 — [{r}] 로 무장 전환";
        if (ShotTarget == null)
        {
            if (Selected == Weapon.AIM9X)
            {
                var radar = _targeting != null ? _targeting.Target : null;
                if (radar != null && _targeting.LocalPlayer != null)
                {
                    float d = Vector3.Distance(_targeting.LocalPlayer.transform.position, radar.transform.position);
                    if (d > spec.seekerRange)
                        return $"AIM-9X 는 {spec.seekerRange / 1000f:F1}km 안에서만 포착 (현재 {d / 1000f:F1}km) — 접근하거나 [{r}] AIM-120";
                }
                return SeekerTarget != null ? "AIM-9X 시커 포착 중 — 락 톤(고음)이 들릴 때까지 기수 유지"
                                            : $"AIM-9X 시커에 표적 없음 — 적기를 기수 앞 {irAcquireCone:F0}° 안에";
            }
            if (_targeting == null || _targeting.Target == null) return $"레이더 락 없음 — [{lk}] 로 락온";
            return $"레이더 락온 중 {_targeting.LockProgress * 100f:F0}% — LOCK 후 발사";
        }
        if (_cooldownTimer > 0f) return "발사 대기 중";
        if (_targeting != null && _targeting.LocalPlayer != null)
            return $"표적이 기수 {Angle(_targeting.LocalPlayer.transform, ShotTarget.transform.position):F0}° — {spec.launchOffBoresight:F0}° 안으로 기수를 돌리세요";
        return "발사 불가";
    }

    void CreateMissile(PlayerController shooter, PlayerController target, MissileSpec spec)
    {
        Transform origin = shooter.transform;
        // 레일 발사(AIM-9)는 날개 끝, 사출 발사(AIM-120)는 동체 하부 무장창
        Vector3 offset = spec.IsIR
            ? origin.right * ((Count(Weapon.AIM9X) % 2 == 0) ? 4.5f : -4.5f) + origin.forward * 1f
            : origin.up * -1.2f + origin.forward * 3f;
        Vector3 pos = origin.position + offset;

        GameObject prefab = _missilePrefab != null ? _missilePrefab : Resources.Load<GameObject>("MissilePrefab");
        GameObject go = prefab != null ? Instantiate(prefab, pos, origin.rotation) : new GameObject(spec.displayName);
        if (prefab == null) go.transform.SetPositionAndRotation(pos, origin.rotation);
        var mc = go.GetComponent<MissileController>() ?? go.AddComponent<MissileController>();

        FlightCamera.NotifyLocalMissile(go.transform);
        string missileId = System.Guid.NewGuid().ToString("N")[..8];
        mc.Initialize(target.transform, shooter.CurrentSpeed, spec, missileId, shooter.nickname, origin);

        NetworkManager.Instance?.socketClient?.SendMissileSpawn(missileId, shooter.nickname, target.nickname,
            go.transform.position, go.transform.eulerAngles, (int)spec.guidance, spec.displayName);
    }

    // ── 레이더 락 경보 송신 (IR 시커는 패시브라 경보 없음) ──────────────────
    float _warnTimer;
    void BroadcastLockState()
    {
        _warnTimer += Time.deltaTime;
        if (_warnTimer < 0.3f) return;
        _warnTimer = 0f;

        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null || _targeting.Target == null) return;

        int lockLevel = _targeting.State switch
        {
            TargetingSystem.LockState.Searching => (int)ThreatWarningSystem.ThreatLevel.Tracked,
            TargetingSystem.LockState.Locked    => (int)ThreatWarningSystem.ThreatLevel.Locked,
            _                                   => (int)ThreatWarningSystem.ThreatLevel.None
        };
        if (lockLevel > 0)
            sc.SendMissileWarn(_targeting.LocalPlayer.nickname, _targeting.Target.nickname, lockLevel);
    }

    // ── AIM-9 시커 톤 (절차적 PCM) ────────────────────────────────────────
    AudioSource _toneSrc;
    AudioClip   _growl, _lockTone;

    void BuildTone()
    {
        _toneSrc = gameObject.AddComponent<AudioSource>();
        _toneSrc.loop = true; _toneSrc.spatialBlend = 0f; _toneSrc.playOnAwake = false; _toneSrc.volume = 0.22f;
        const int sr = 22050;
        _growl    = MakeTone("SeekerGrowl", sr, 1.0f, t => Mathf.Sin(2f * Mathf.PI * 420f * t) * (0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 11f * t)));
        _lockTone = MakeTone("SeekerLock",  sr, 0.5f, t => Mathf.Sin(2f * Mathf.PI * 1250f * t) * 0.8f);
    }

    static AudioClip MakeTone(string name, int sr, float seconds, System.Func<float, float> f)
    {
        int n = Mathf.CeilToInt(sr * seconds);
        var buf = new float[n];
        for (int i = 0; i < n; i++) buf[i] = f((float)i / sr) * 0.6f;
        var clip = AudioClip.Create(name, n, 1, sr, false);
        clip.SetData(buf, 0);
        return clip;
    }

    void UpdateTone()
    {
        if (_toneSrc == null) return;
        AudioClip want = Seeker == SeekerState.Locked ? _lockTone
                       : Seeker == SeekerState.Searching && SeekerTarget != null ? _growl
                       : null;
        if (want == null) { if (_toneSrc.isPlaying) _toneSrc.Stop(); return; }
        if (_toneSrc.clip != want || !_toneSrc.isPlaying) { _toneSrc.clip = want; _toneSrc.Play(); }
    }
}

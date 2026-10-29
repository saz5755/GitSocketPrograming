using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 적 전투기 AI (호스트에서만 실행) — 실제 공중전 절차를 따른다.
///
///   Patrol  : 초계. 레이더(전방 60°, 9 km) 또는 육안(360°, 3.5 km)으로 적기 탐지
///   Commit  : 표적으로 기수 → 레이더 락 (표적 RWR 에 TRK → LCK 경보) → DLZ 안에서 레이더 미사일 발사
///   Crank   : 발사 후 표적을 레이더 시야 가장자리(≈50°)에 두고 선회 — 미사일 데이터링크 유지 + 접근 속도 감소
///   Merge   : 근접 기동전(BFM). 리드/퓨어/래그 추격, IR 미사일(시커 포착 후), 기총(리드 조준 원뿔 안)
///   Defend  : 미사일 접근 경보(MAWS) → 원거리 레이더 미사일은 드래그(등 돌려 에너지 소진), 근거리는
///             빔/노치 + 채프, IR 미사일은 빔 + 플레어, 최종 순간 브레이크 턴
///   Extend  : 손상이 크거나 무장 소진 → 이탈 후 귀환·재무장
///
/// 숙련도(Skill)에 따라 선회율·반응 시간·대응책 운용·사격 정확도가 달라진다.
/// 기동은 AIBotController(운동학 비행)에 목표 자세·속도를 넘기는 방식.
/// </summary>
public class EnemyAI : MonoBehaviour
{
    public enum CombatState { Patrol, Commit, Crank, Merge, Defend, Extend, Return }
    public enum Skill { Rookie, Veteran, Ace }

    [System.Serializable]
    public struct SkillProfile
    {
        public float bfmTurnRate;      // 근접전 최대 선회율 (deg/s) — 플레이어 9G·100 m/s ≈ 50
        public float cruiseTurnRate;
        public float reactionTime;     // 위협 인지 → 대응 기동까지 (s)
        public float gunCone;          // 기총 사격 허용 리드 오차 (°)
        public float gunSpread;        // 탄 산포 (°)
        public float launchFraction;   // Rmax 의 몇 % 안에서 쏘는가 (작을수록 확실한 거리)
        public int   cmPerSalvo;       // 1회 투하 수
        public float cmInterval;       // 투하 간격 (s)
        public bool  notches;          // 레이더 미사일에 노치 기동을 쓰는가 (아니면 단순 회피)
        public bool  preemptiveFlares; // 후방에 적기가 붙으면 경보 없이도 플레어 선제 투하
    }

    public static SkillProfile Profile(Skill s) => s switch
    {
        Skill.Rookie => new SkillProfile { bfmTurnRate = 40f, cruiseTurnRate = 28f, reactionTime = 1.3f, gunCone = 6f, gunSpread = 1.6f,
                                           launchFraction = 0.6f, cmPerSalvo = 1, cmInterval = 0.8f, notches = false, preemptiveFlares = false },
        Skill.Ace    => new SkillProfile { bfmTurnRate = 55f, cruiseTurnRate = 36f, reactionTime = 0.3f, gunCone = 3.5f, gunSpread = 0.6f,
                                           launchFraction = 0.85f, cmPerSalvo = 2, cmInterval = 0.35f, notches = true, preemptiveFlares = true },
        _            => new SkillProfile { bfmTurnRate = 48f, cruiseTurnRate = 32f, reactionTime = 0.7f, gunCone = 4.5f, gunSpread = 1.0f,
                                           launchFraction = 0.75f, cmPerSalvo = 2, cmInterval = 0.5f, notches = true, preemptiveFlares = false },
    };

    [Header("숙련도")]
    [SerializeField] Skill _skill = Skill.Veteran;

    [Header("센서")]
    [SerializeField] float radarRange   = 9000f;
    [SerializeField] float radarGimbal  = 60f;
    [SerializeField] float visualRange  = 3500f;
    [SerializeField] float lockTime     = 1.6f;

    [Header("교전")]
    [SerializeField] float mergeRange   = 2600f;   // 이 안이면 근접 기동전
    [SerializeField] float gunRange     = 750f;
    [SerializeField] float missileCooldown = 6f;

    [Header("탑재 (출격·재무장 시)")]
    [SerializeField] int radarMissiles = 2;
    [SerializeField] int irMissiles    = 2;
    [SerializeField] int gunRounds     = 360;
    [SerializeField] int flares        = 30;
    [SerializeField] int chaff         = 30;

    [Header("순찰 / 속도 (m/s)")]
    [SerializeField] float patrolRadius = 1500f;
    [SerializeField] float patrolAlt    = 900f;
    [SerializeField] float patrolSpeed  = 55f;
    [SerializeField] float combatSpeed  = 88f;
    [SerializeField] float maxSpeed     = 118f;    // 애프터버너 (플레이어 120 m/s 와 비슷)

    [Header("프리팹")]
    [Tooltip("미사일 프리팹. 미할당 시 Resources/MissilePrefab 폴백 → 그것도 없으면 빈 오브젝트")]
    [SerializeField] GameObject _missilePrefab;

    const float HardDeck   = 120f;   // 해수면 기준 최저 고도
    const float GunFireRate = 16f;
    const float BulletSpeed = 400f;  // GunSystem 과 동일

    // ── 상태 ──────────────────────────────────────────────────────────────
    AIBotController  _bot;
    SkillProfile     _p;
    bool             _combatEnabled;
    CombatState      _state = CombatState.Patrol;
    CombatState      _resumeState = CombatState.Commit;
    float            _stateTimer;

    PlayerController _target;
    float            _targetScanTimer;
    Vector3          _tgtVel, _tgtPrevPos;
    float            _lockTimer, _warnTimer;
    bool             _locked;

    int   _radarMsl, _irMsl, _gunAmmo, _flares, _chaff;
    float _missileCd, _gunBurstTimer, _gunPauseTimer, _gunAccum, _cmTimer, _irSeekTimer;
    float _threatSeenAt = -1f, _lastThreatAt = -100f;
    int   _crankSide = 1;
    MissileController _supportedMissile;

    Vector3 _patrolCenter, _patrolWP;
    float   _extendUntil;
    static float s_seaY = float.NaN;

    public CombatState State => _state;
    public Skill       SkillLevel => _skill;
    public bool        IsUnderMissileAttack { get; private set; }

    public void EnableCombat() => _combatEnabled = true;
    public void SetSkill(Skill s) { _skill = s; _p = Profile(s); }

    /// <summary>초기 표적 힌트(스폰 기준 기체) — 이후 표적은 스스로 고른다</summary>
    public void Initialize(Transform target, string targetNickname)
    {
        _bot = GetComponent<AIBotController>();
        _p   = Profile(_skill);
        _bot.SetMaxSpeed(maxSpeed);
        _bot.SetTurnRate(_p.cruiseTurnRate);
        if (_bot.Speed < patrolSpeed) _bot.SetInitialSpeed(patrolSpeed);   // 공중 스폰 — 0 에서 가속하면 멈춘 듯 보인다
        _patrolCenter = new Vector3(transform.position.x, SeaY, transform.position.z);   // 순찰 고도는 해수면 + patrolAlt
        _patrolWP     = NextPatrolWP();
        _target       = target != null ? target.GetComponent<PlayerController>() : null;
        Rearm();
        // 첫 발사 타이밍 분산 (여러 적기가 동시 발사 방지)
        _missileCd = Random.Range(2f, missileCooldown);
        _crankSide = Random.value < 0.5f ? 1 : -1;
    }

    void Rearm()
    {
        _radarMsl = radarMissiles; _irMsl = irMissiles; _gunAmmo = gunRounds;
        _flares = flares; _chaff = chaff;
    }

    // ── Update ────────────────────────────────────────────────────────────
    void Update()
    {
        if (_bot == null) return;
        float dt = Time.deltaTime;
        _stateTimer += dt;
        _missileCd  -= dt;
        _cmTimer    -= dt;

        if (_combatEnabled)
        {
            _targetScanTimer -= dt;
            if (_targetScanTimer <= 0f) { _targetScanTimer = 1f; SelectTarget(); }
            TrackTarget(dt);
            UpdateThreats();
            UpdateRadar(dt);
        }

        switch (_state)
        {
            case CombatState.Patrol: DoPatrol();      break;
            case CombatState.Commit: DoCommit();      break;
            case CombatState.Crank:  DoCrank();       break;
            case CombatState.Merge:  DoMerge(dt);     break;
            case CombatState.Defend: DoDefend();      break;
            case CombatState.Extend: DoExtend();      break;
            case CombatState.Return: DoReturn();      break;
        }
    }

    void Transition(CombatState next)
    {
        if (_state == next) return;
        _state = next; _stateTimer = 0f;
        _gunBurstTimer = 0f; _irSeekTimer = 0f;
    }

    // ── 표적 선정 / 센서 ──────────────────────────────────────────────────
    bool Detects(PlayerController pc, out float dist)
    {
        Vector3 to = pc.transform.position - transform.position;
        dist = to.magnitude;
        if (dist <= visualRange) return true;
        return dist <= radarRange && Vector3.Angle(transform.forward, to) <= radarGimbal;
    }

    void SelectTarget()
    {
        PlayerController best = null;
        float bestScore = float.MaxValue;
        foreach (var pc in PlayerController.All)
        {
            if (!IsValidTarget(pc)) continue;
            if (!Detects(pc, out float d) && pc != _target) continue;
            float score = d;
            if (pc == _target) score -= 1200f;            // 표적 유지 경향
            if (IsShootingAtMe(pc.nickname)) score -= 3000f;   // 나를 노리는 기체 우선
            if (score < bestScore) { bestScore = score; best = pc; }
        }
        if (best != _target) { _target = best; _lockTimer = 0f; _locked = false; if (best != null) _tgtPrevPos = best.transform.position; }
    }

    static bool IsValidTarget(PlayerController pc)
    {
        if (pc == null || string.IsNullOrEmpty(pc.nickname) || AIManager.IsAIBot(pc.nickname)) return false;
        if (!TargetingSystem.IsAirborne(pc)) return false;
        return pc.transform.position.y > SeaY + 25f;   // 갑판 위 기체는 표적 아님
    }

    bool IsShootingAtMe(string nick)
    {
        foreach (var mc in MissileController.All)
            if (mc != null && mc.ShooterNick == nick && mc.TargetNick == _bot.Nickname) return true;
        foreach (var rv in RemoteMissileView.All)
            if (rv != null && rv.ShooterNick == nick && rv.TargetNick == _bot.Nickname) return true;
        return false;
    }

    void TrackTarget(float dt)
    {
        if (_target != null && !IsValidTarget(_target)) { _target = null; _locked = false; _lockTimer = 0f; }
        if (_target == null)
        {
            if (_state is CombatState.Commit or CombatState.Crank or CombatState.Merge) Transition(CombatState.Patrol);
            return;
        }
        Vector3 tp = _target.transform.position;
        if (dt > 0f) _tgtVel = Vector3.Lerp(_tgtVel, (tp - _tgtPrevPos) / dt, 0.12f);
        _tgtPrevPos = tp;

        if (_state == CombatState.Patrol) Transition(CombatState.Commit);
    }

    // 레이더 STT 락 + 표적 RWR 경보
    void UpdateRadar(float dt)
    {
        bool offensive = _state is CombatState.Commit or CombatState.Crank or CombatState.Merge;
        bool painting  = offensive && _target != null && InRadar(_target.transform.position);
        if (painting) { _lockTimer += dt; _locked = _lockTimer >= lockTime; }
        else          { _lockTimer = Mathf.Max(0f, _lockTimer - dt * 2f); _locked = false; }

        _warnTimer -= dt;
        if (painting && _warnTimer <= 0f)
        {
            _warnTimer = 0.4f;
            NetworkManager.Instance?.socketClient?.SendMissileWarn(_bot.Nickname, _target.nickname,
                _locked ? (int)ThreatWarningSystem.ThreatLevel.Locked : (int)ThreatWarningSystem.ThreatLevel.Tracked);
        }
    }

    bool InRadar(Vector3 p) =>
        Vector3.Distance(transform.position, p) <= radarRange &&
        Vector3.Angle(transform.forward, p - transform.position) <= radarGimbal;

    // ── 위협 (미사일 접근 경보) ───────────────────────────────────────────
    Vector3 _threatPos, _threatFwd;
    float   _threatDist;
    bool    _threatIR;

    void UpdateThreats()
    {
        bool found = false;
        _threatDist = float.MaxValue;
        string me = _bot.Nickname;

        foreach (var mc in MissileController.All)
        {
            if (mc == null || mc.TargetNick != me || !mc.IsTrackingTarget) continue;
            ConsiderThreat(mc.transform, mc.Spec.IsIR, ref found);
        }
        foreach (var rv in RemoteMissileView.All)
        {
            if (rv == null || rv.TargetNick != me) continue;
            // 기만된 미사일은 빗나가며 진행 방향이 틀어진다
            if (Vector3.Angle(rv.transform.forward, transform.position - rv.transform.position) > 60f) continue;
            ConsiderThreat(rv.transform, rv.Spec == null || rv.Spec.IsIR, ref found);
        }

        IsUnderMissileAttack = found;
        if (found)
        {
            _lastThreatAt = Time.time;
            if (_threatSeenAt < 0f) _threatSeenAt = Time.time;
            // 반응 시간이 지나면 방어 기동 (근거리면 즉시)
            bool react = Time.time - _threatSeenAt >= _p.reactionTime || _threatDist < 900f;
            if (react && _state != CombatState.Defend)
            {
                if (_state != CombatState.Extend) _resumeState = _state is CombatState.Patrol or CombatState.Return ? CombatState.Commit : _state;
                Transition(CombatState.Defend);
            }
        }
        else _threatSeenAt = -1f;
    }

    void ConsiderThreat(Transform m, bool ir, ref bool found)
    {
        float d = Vector3.Distance(m.position, transform.position);
        if (d > 9000f) return;
        found = true;
        if (d < _threatDist) { _threatDist = d; _threatPos = m.position; _threatFwd = m.forward; _threatIR = ir; }
    }

    // ── 상태별 동작 ──────────────────────────────────────────────────────
    void DoPatrol()
    {
        _bot.SetTurnRate(_p.cruiseTurnRate);
        if (Vector3.Distance(transform.position, _patrolWP) < 120f) _patrolWP = NextPatrolWP();
        FlyTo(_patrolWP, patrolSpeed);
    }

    void DoCommit()
    {
        if (_target == null) { Transition(CombatState.Patrol); return; }
        if (NeedsExtend()) { BeginExtend(); return; }
        _bot.SetTurnRate(_p.cruiseTurnRate);

        float dist = Dist(_target);
        if (dist < mergeRange) { Transition(CombatState.Merge); return; }

        // 고도 우위를 노리며 리드 방향으로 접근
        Vector3 aim = _target.transform.position + _tgtVel * Mathf.Clamp(dist / 600f, 0f, 4f);
        aim.y = Mathf.Max(aim.y + 150f, transform.position.y - 50f);
        // 표적이 등을 보이고 이탈 중이면 애프터버너로 추격 (아니면 뒤에서 따라오기만 한다)
        FlyTo(aim, Opening() > 15f ? maxSpeed : combatSpeed);

        // BVR 사격
        var spec = GameplayConfigSO.Instance.enemyRadar;
        if (_locked && _radarMsl > 0 && _missileCd <= 0f && InLaunchCone(spec))
        {
            float rmax = spec.MaxRangeAgainst(_bot.Speed, Opening());
            if (dist >= spec.minRange && dist <= rmax * _p.launchFraction)
            {
                _supportedMissile = FireMissile(spec);
                _radarMsl--;
                _crankSide = Vector3.SignedAngle(Flat(transform.forward), Flat(ToTarget()), Vector3.up) > 0f ? -1 : 1;
                Transition(CombatState.Crank);
            }
        }
    }

    void DoCrank()
    {
        if (_target == null) { Transition(CombatState.Patrol); return; }
        _bot.SetTurnRate(_p.cruiseTurnRate);

        float dist = Dist(_target);
        bool missileDone = _supportedMissile == null || _supportedMissile.IsARHActive || _supportedMissile.IsLost;
        if (dist < mergeRange * 0.8f || (missileDone && _stateTimer > 2f) || _stateTimer > 25f)
        {
            _supportedMissile = null;
            Transition(dist < mergeRange ? CombatState.Merge : CombatState.Commit);
            return;
        }

        // 표적을 레이더 짐벌 가장자리(gimbal − 10°)에 두고 비스듬히 비행, 약간 강하
        Vector3 los = Flat(ToTarget()).normalized;
        Vector3 dir = Quaternion.AngleAxis(_crankSide * (radarGimbal - 10f), Vector3.up) * los;
        FlyDir(dir, combatSpeed, transform.position.y - 40f);
    }

    void DoMerge(float dt)
    {
        if (_target == null) { Transition(CombatState.Patrol); return; }
        if (NeedsExtend()) { BeginExtend(); return; }
        _bot.SetTurnRate(_p.bfmTurnRate);

        float   dist = Dist(_target);
        if (dist > mergeRange * 1.4f) { Transition(CombatState.Commit); return; }

        Vector3 me      = transform.position;
        Vector3 tp      = _target.transform.position;
        float   closure = -Vector3.Dot(_tgtVel - transform.forward * _bot.Speed, (tp - me).normalized);

        // 적이 내 6시에서 기수를 겨누고 있으면 방어 BFM (브레이크 턴) + 선제 플레어
        float myAspect  = Vector3.Angle(-transform.forward, tp - me);            // 0 = 정후방
        float theirNose = Vector3.Angle(_target.transform.forward, me - tp);     // 0 = 나를 정조준
        if (myAspect < 50f && theirNose < 25f && dist < 2200f)
        {
            Vector3 breakDir = BeamDir(tp);
            FlyDir(breakDir, maxSpeed, me.y - 30f);
            if (_p.preemptiveFlares && _cmTimer <= 0f && dist < 2000f) DropCountermeasures(CountermeasureType.Flare);
            return;
        }

        // 추격 기하: 원거리 리드, 근거리 고속 접근 시 래그로 오버슈트 방지
        float   tLead = Mathf.Clamp(dist / Mathf.Max(BulletSpeed, 1f), 0f, 2f);
        Vector3 aim;
        if (dist > 900f)                  aim = tp + _tgtVel * Mathf.Clamp(dist / 250f, 0f, 3f);
        else if (closure > 45f && dist < 450f) aim = tp - _target.transform.forward * 120f;   // 래그
        else                              aim = tp + _tgtVel * tLead;                         // 기총 리드
        // 근접 고속 접근은 감속(오버슈트 방지), 표적이 벌어지면 애프터버너로 따라붙는다
        float speed = dist < 500f && closure > 20f ? combatSpeed * 0.85f
                    : closure < -5f || dist > 1200f ? maxSpeed
                    : combatSpeed * 1.1f;
        FlyTo(aim, speed);

        TryIRShot(dist, dt);
        TryGun(dist, tp, tLead, dt);
    }

    void TryIRShot(float dist, float dt)
    {
        var spec = GameplayConfigSO.Instance.enemyIR;
        bool inCone = _irMsl > 0 && dist >= spec.minRange && dist <= spec.seekerRange
                   && Vector3.Angle(transform.forward, ToTarget()) <= spec.launchOffBoresight * 0.7f;
        _irSeekTimer = inCone ? _irSeekTimer + dt : 0f;
        if (!inCone || _irSeekTimer < 0.8f || _missileCd > 0f) return;

        float rmax = spec.MaxRangeAgainst(_bot.Speed, Opening());
        if (dist > rmax * _p.launchFraction) return;
        FireMissile(spec);
        _irMsl--;
        _irSeekTimer = 0f;
    }

    void TryGun(float dist, Vector3 tp, float tLead, float dt)
    {
        if (_gunPauseTimer > 0f) { _gunPauseTimer -= dt; return; }
        if (_gunAmmo <= 0 || dist > gunRange) { _gunBurstTimer = 0f; return; }

        Vector3 leadPoint = tp + _tgtVel * tLead;
        bool solution = Vector3.Angle(transform.forward, leadPoint - transform.position) <= _p.gunCone;
        if (!solution) { _gunBurstTimer = 0f; return; }

        _gunBurstTimer += dt;
        _gunAccum += dt * GunFireRate;
        while (_gunAccum >= 1f && _gunAmmo > 0)
        {
            _gunAccum -= 1f;
            FireBullet();
        }
        // 1.2초 연사 후 잠깐 쉰다 (총열 과열·재조준)
        if (_gunBurstTimer > 1.2f) { _gunBurstTimer = 0f; _gunPauseTimer = Random.Range(0.8f, 1.6f); }
    }

    void DoDefend()
    {
        float sinceThreat = Time.time - _lastThreatAt;
        if (!IsUnderMissileAttack && sinceThreat > 1.5f)
        {
            Transition(_target != null ? (_resumeState == CombatState.Defend ? CombatState.Commit : _resumeState) : CombatState.Patrol);
            return;
        }
        _bot.SetTurnRate(_p.bfmTurnRate * 1.05f);
        Vector3 me = transform.position;
        float   floor = SeaY + HardDeck + 40f;

        if (!IsUnderMissileAttack)
        {
            // 위협이 막 사라짐 — 방금 기동 유지
            FlyDir(transform.forward, maxSpeed, me.y);
            return;
        }

        // 최종 순간: 미사일 진행 방향에 수직으로 브레이크 턴 (오버슈트 유도)
        if (_threatDist < 550f)
        {
            Vector3 breakDir = Vector3.Cross(_threatFwd, Vector3.up).normalized;
            if (Vector3.Dot(breakDir, transform.forward) < 0f) breakDir = -breakDir;
            FlyDir(breakDir, maxSpeed, me.y - 60f);
            DropCountermeasures(_threatIR ? CountermeasureType.Flare : CountermeasureType.Chaff);
            return;
        }

        if (_threatIR)
        {
            // IR: 빔 기동으로 시선각 변화율을 키우고, 시커 사거리 안에서 플레어
            FlyDir(BeamDir(_threatPos), maxSpeed, me.y - 30f);
            if (_threatDist < 2200f) DropCountermeasures(CountermeasureType.Flare);
            return;
        }

        if (!_p.notches || _threatDist > 3800f)
        {
            // 드래그: 등을 돌려 미사일이 쫓아오며 에너지를 소진하게 만든다 (강하로 속도 확보)
            Vector3 away = Flat(me - _threatPos).normalized;
            FlyDir(away, maxSpeed, Mathf.Max(floor, me.y - 120f));
            if (!_p.notches && _threatDist < 2500f) DropCountermeasures(CountermeasureType.Chaff);
            return;
        }

        // 노치: 미사일 시선에 수직으로 저고도 비행 + 채프 — 도플러 필터에 묻힌다
        FlyDir(BeamDir(_threatPos), combatSpeed, Mathf.Max(floor, me.y - 150f));
        if (_threatDist < 3000f) DropCountermeasures(CountermeasureType.Chaff);
    }

    bool NeedsExtend()
    {
        float hp = AIManager.Instance != null ? AIManager.Instance.GetBotHP01(_bot.Nickname) : 1f;
        bool winchester = _radarMsl + _irMsl == 0 && _gunAmmo <= 0;
        return hp < 0.3f || winchester;
    }

    void BeginExtend() { _extendUntil = Time.time + 18f; Transition(CombatState.Extend); }

    void DoExtend()
    {
        _bot.SetTurnRate(_p.cruiseTurnRate);
        Vector3 away = _target != null ? Flat(transform.position - _target.transform.position).normalized : Flat(transform.forward);
        FlyDir(away, maxSpeed, transform.position.y);
        if (Time.time > _extendUntil) Transition(CombatState.Return);
    }

    void DoReturn()
    {
        _bot.SetTurnRate(_p.cruiseTurnRate);
        FlyTo(_patrolCenter + Vector3.up * patrolAlt, combatSpeed);
        if (Vector3.Distance(Flat(transform.position), Flat(_patrolCenter)) < 400f)
        {
            Rearm();   // 기지 재무장 시뮬레이션
            Transition(CombatState.Patrol);
        }
    }

    // ── 무장 ──────────────────────────────────────────────────────────────
    MissileController FireMissile(MissileSpec spec)
    {
        if (_target == null) return null;
        _missileCd = missileCooldown;

        Vector3    pos = transform.position + transform.forward * 4f - transform.up * 1.2f;
        Quaternion rot = transform.rotation;
        GameObject prefab = _missilePrefab != null ? _missilePrefab : Resources.Load<GameObject>("MissilePrefab");
        GameObject go = prefab != null ? Instantiate(prefab, pos, rot) : new GameObject($"{spec.displayName}_{_bot.Nickname}");
        if (prefab == null) go.transform.SetPositionAndRotation(pos, rot);

        string id = $"AI{_bot.Nickname}{System.Guid.NewGuid().ToString("N")[..6]}";
        var mc = go.GetComponent<MissileController>() ?? go.AddComponent<MissileController>();
        mc.Initialize(_target.transform, _bot.Speed, spec, id, _bot.Nickname, transform);

        NetworkManager.Instance?.socketClient?.SendMissileSpawn(id, _bot.Nickname, _target.nickname,
            pos, rot.eulerAngles, (int)spec.guidance, spec.displayName);
        DevLog.Log($"[AI] {_bot.Nickname} FOX {(spec.IsIR ? 2 : 3)} ({spec.displayName}) → {_target.nickname}  dist={Dist(_target):F0}");
        return mc;
    }

    void FireBullet()
    {
        _gunAmmo--;
        Vector3 muzzle = transform.position + transform.forward * 5f;
        Vector3 dir    = transform.rotation * Quaternion.Euler(Random.Range(-_p.gunSpread, _p.gunSpread),
                                                               Random.Range(-_p.gunSpread, _p.gunSpread), 0f) * Vector3.forward;
        BulletController.Spawn(muzzle, dir, BulletSpeed, isLocal: true, aiShooter: _bot.Nickname);
        NetworkManager.Instance?.socketClient?.SendGunFire(_bot.Nickname, muzzle, dir);
    }

    void DropCountermeasures(CountermeasureType type)
    {
        if (_cmTimer > 0f) return;
        _cmTimer = _p.cmInterval;
        for (int i = 0; i < _p.cmPerSalvo; i++)
        {
            if (type == CountermeasureType.Flare) { if (_flares <= 0) return; _flares--; }
            else                                  { if (_chaff  <= 0) return; _chaff--;  }
            CountermeasureSystem.DeployFrom(_bot.Nickname, transform, _bot.Speed, type);
        }
    }

    // ── 기하 헬퍼 ─────────────────────────────────────────────────────────
    float   Dist(PlayerController pc) => Vector3.Distance(transform.position, pc.transform.position);
    Vector3 ToTarget() => _target.transform.position - transform.position;
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    bool InLaunchCone(MissileSpec spec) => Vector3.Angle(transform.forward, ToTarget()) <= spec.launchOffBoresight;

    float Opening() => _target == null ? 0f : Vector3.Dot(_tgtVel, ToTarget().normalized);

    // 위협 방향에 수직인 수평 방향 중 현재 기수와 가까운 쪽
    Vector3 BeamDir(Vector3 threatPos)
    {
        Vector3 los  = Flat(threatPos - transform.position).normalized;
        Vector3 perp = Vector3.Cross(Vector3.up, los).normalized;
        return Vector3.Dot(perp, transform.forward) >= 0f ? perp : -perp;
    }

    static float SeaY
    {
        get
        {
            if (float.IsNaN(s_seaY))
            {
                var ocean = FindAnyObjectByType<OceanSurface>();
                s_seaY = ocean != null ? ocean.seaLevel : 0f;
            }
            return s_seaY;
        }
    }

    void FlyDir(Vector3 dir, float speed, float desiredAlt)
    {
        dir = Flat(dir);
        if (dir.sqrMagnitude < 0.001f) dir = Flat(transform.forward);
        Vector3 dest = transform.position + dir.normalized * 600f;
        dest.y = desiredAlt;
        FlyTo(dest, speed);
    }

    void FlyTo(Vector3 dest, float speed)
    {
        float floor = SeaY + HardDeck;
        dest.y = Mathf.Max(dest.y, floor);
        // 하드 덱 아래면 기수를 들어 회복 — 수평 성분은 유지
        if (transform.position.y < floor) dest.y = Mathf.Max(dest.y, transform.position.y + 200f);

        Vector3 to = dest - transform.position;
        if (to.sqrMagnitude < 0.01f) { _bot.SetTarget(dest, transform.rotation, speed); return; }
        Vector3 fwd = to.normalized;
        // 피치 제한 (±60°) — 수직 기동으로 표적을 잃지 않게
        float pitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
        if (Mathf.Abs(pitch) > 60f)
        {
            Vector3 h = Flat(fwd).sqrMagnitude > 0.0001f ? Flat(fwd).normalized : Flat(transform.forward).normalized;
            fwd = (h * Mathf.Cos(60f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sign(pitch) * Mathf.Sin(60f * Mathf.Deg2Rad)).normalized;
        }

        // 선회 방향으로 뱅크 — 요 오차에 비례 (최대 80°)
        float yawErr = Vector3.SignedAngle(Flat(transform.forward), Flat(fwd), Vector3.up);
        float bank   = Mathf.Clamp(yawErr * 1.6f, -80f, 80f);
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 0f, -bank);

        // 급선회는 에너지를 깎는다
        float turnPenalty = Mathf.InverseLerp(30f, 120f, Mathf.Abs(yawErr)) * 0.18f;
        _bot.SetTarget(dest, rot, speed * (1f - turnPenalty));
    }

    Vector3 NextPatrolWP()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float r     = Random.Range(patrolRadius * 0.3f, patrolRadius);
        return _patrolCenter + new Vector3(Mathf.Cos(angle) * r, patrolAlt + Random.Range(-100f, 100f), Mathf.Sin(angle) * r);
    }
}

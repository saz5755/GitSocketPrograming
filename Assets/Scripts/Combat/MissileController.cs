using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum MissileGuidanceType { HeatSeeking, RadarGuided }

/// <summary>
/// 유도미사일 시뮬레이션 (발사자 클라이언트, AI 무기는 호스트에서 실행).
///
///   · 추진/항력: MissileSpec — 부스트 후 코스팅하며 감속, 상승하면 더 감속. 에너지가 바닥나면 유도 불능
///   · 유도: G 한계가 있는 비례항법(PN). 느릴수록 가용 G 가 줄어 마지막 순간 급선회로 회피 가능
///   · 시커: 짐벌 한계를 벗어나면 락 상실. 레이더 미사일은 중간유도(데이터링크) → 피트불(능동) 전환
///   · 대응책: 플레어/채프 한 발마다 시커 시야 안에 들어올 때 1회 판정. 기만체가 꺼지면 원 표적 재포착 시도
///             레이더 미사일은 표적이 빔(노치) 기동 중이면 락을 잃기 쉽다
///   · 신관: 원 표적 기준 (기만체를 쫓다가도 표적 옆을 지나면 근접신관 작동)
/// </summary>
public class MissileController : MonoBehaviour
{
    public static readonly List<MissileController> All = new List<MissileController>(32);

    const float Gravity          = 9.81f;
    const float ArmDelay         = 0.6f;
    const float DatalinkInterval = 0.5f;    // 중간유도 교점 갱신 주기
    const float ShooterGimbal    = 60f;     // 발사 기체 레이더가 표적을 계속 비춰야 데이터링크 유지
    const float LagCompensation  = 0.1f;    // 원격 표적 스냅샷 보간 지연 보상 (s)
    const float VelSmooth        = 0.2f;
    const float CmCheckInterval  = 0.2f;
    const float WarnInterval     = 0.35f;

    enum Phase { Midcourse, Terminal }

    // ── 공개 상태 ──────────────────────────────────────────────────────────
    /// <summary>시커가 현재 쫓는 대상 (기만체일 수 있음, 락 상실 시 null)</summary>
    public Transform           Target         { get; private set; }
    /// <summary>발사 시 지정한 표적 기체</summary>
    public Transform           IntendedTarget { get; private set; }
    public string              TargetNick     { get; private set; } = "";
    public bool                IsDeflected    { get; private set; }
    public bool                IsLost         { get; private set; }
    public MissileGuidanceType GuidanceType   => Spec.guidance;
    public MissileSpec         Spec           { get; private set; }
    public string              MissileId      { get; private set; }
    public string              ShooterNick    { get; private set; }
    public bool                IsARHActive    { get; private set; }
    public float               Speed          => _speed;
    public float               Age            => _age;
    /// <summary>표적을 아직 쫓고 있는가 (경보 대상)</summary>
    public bool                IsTrackingTarget => Target != null && Target == IntendedTarget && !IsLost;

    Phase     _phase = Phase.Midcourse;
    Transform _shooter;
    float     _speed, _age;
    bool      _armed;
    Vector3   _prevLOS;
    Vector3   _targetVelEst, _prevTargetPos;
    bool      _prevTargetPosValid;
    Vector3   _interceptPoint;
    float     _datalinkTimer = DatalinkInterval;
    float     _cmTimer, _warnTimer, _netTimer;
    float     _notchTime;
    float     _deadTimer = -1f;               // 에너지 고갈 후 자폭까지
    bool      _reacquireTried;
    TrailRenderer _trail;
    readonly HashSet<CountermeasureDecoy> _evaluatedDecoys = new HashSet<CountermeasureDecoy>();

    void Awake()     => All.Add(this);
    void OnDestroy() => All.Remove(this);

    /// <summary>구형 호출 호환 — 유도 방식의 기본 제원 사용</summary>
    public void Initialize(Transform target, float launchSpeed,
                           MissileGuidanceType guidanceType = MissileGuidanceType.RadarGuided,
                           string missileId = "", string shooterNickname = "")
    {
        var cfg = GameplayConfigSO.Instance;
        Initialize(target, launchSpeed, guidanceType == MissileGuidanceType.HeatSeeking ? cfg.aim9x : cfg.aim120,
                   missileId, shooterNickname, null);
    }

    public void Initialize(Transform target, float launchSpeed, MissileSpec spec,
                           string missileId, string shooterNickname, Transform shooter)
    {
        Spec           = spec ?? GameplayConfigSO.Instance.aim120;
        Target         = target;
        IntendedTarget = target;
        TargetNick     = target != null ? target.GetComponent<PlayerController>()?.nickname ?? "" : "";
        _speed         = Mathf.Max(launchSpeed, 40f);
        _shooter       = shooter;
        MissileId      = string.IsNullOrEmpty(missileId) ? System.Guid.NewGuid().ToString("N")[..8] : missileId;
        ShooterNick    = shooterNickname;

        if (target != null)
        {
            _prevTargetPos      = target.position;
            _interceptPoint     = target.position;
            _prevTargetPosValid = true;
        }

        // IR 시커는 발사 전부터 표적을 보고 있다 — 처음부터 종말유도
        if (Spec.IsIR) _phase = Phase.Terminal;

        _trail = AddSmokeTrail(gameObject);
    }

    // ── 연기 트레일 (원격 뷰와 공용) ──────────────────────────────────────
    public static TrailRenderer AddSmokeTrail(GameObject go)
    {
        var tr = go.AddComponent<TrailRenderer>();
        // 가까운 구간을 촘촘히 잇는 리본 — 오래 남는 항적은 MissileExhaustFX 의 파티클 연기가 맡는다
        tr.time              = 0.9f;
        tr.startWidth        = 0.5f;
        tr.endWidth          = 1.6f;
        tr.minVertexDistance = 0.3f;
        tr.textureMode       = LineTextureMode.Stretch;
        tr.shadowCastingMode = ShadowCastingMode.Off;
        tr.receiveShadows    = false;

        var sh = Shader.Find("Custom/SmokeTrail");
        Material mat;
        if (sh != null)
        {
            mat = new Material(sh);
            mat.SetColor("_Color",      new Color(0.75f, 0.72f, 0.70f, 0.55f));
            mat.SetFloat("_NoiseScale", 3.5f);
        }
        else
        {
            var fallback = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                        ?? Shader.Find("Sprites/Default");
            mat = new Material(fallback);
            mat.color = new Color(0.7f, 0.7f, 0.7f, 0.45f);
        }
        tr.material = mat;

        var gc = new Gradient();
        gc.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.0f, 0.90f, 0.70f), 0.00f),
                new GradientColorKey(new Color(0.65f, 0.62f, 0.60f), 0.30f),
                new GradientColorKey(new Color(0.30f, 0.28f, 0.27f), 1.00f),
            },
            new[]
            {
                new GradientAlphaKey(0.80f, 0.00f),
                new GradientAlphaKey(0.45f, 0.40f),
                new GradientAlphaKey(0.00f, 1.00f),
            });
        tr.colorGradient = gc;
        MissileExhaustFX.Attach(go, tr);
        return tr;
    }

    static float s_seaY = float.NaN;

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || Spec == null) return;   // Initialize 전 (씬에 놓인 프리팹 등)
        _age += dt;

        if (float.IsNaN(s_seaY))
        {
            var ocean = FindAnyObjectByType<OceanSurface>();
            s_seaY = ocean != null ? ocean.seaLevel : float.NegativeInfinity;
        }
        // 연료 소진 후 자폭 / 해수면 충돌 — 원격 뷰도 사라지도록 파괴를 알린다
        if (_age > Spec.lifetime || transform.position.y < s_seaY) { SelfDestruct(); return; }
        if (_age > ArmDelay) _armed = true;

        UpdateEnergy(dt);
        if (_deadTimer >= 0f && (_deadTimer -= dt) <= 0f) { SelfDestruct(); return; }

        UpdateTargetEstimate(dt);

        // 기만체가 타 버렸으면 원 표적 재포착 시도
        if (IsDeflected && Target == null) TryReacquire();

        if (_armed && Target != null && !IsLost)
        {
            _cmTimer += dt;
            if (_cmTimer >= CmCheckInterval) { _cmTimer = 0f; CheckCountermeasures(); CheckNotch(); }
        }

        Vector3 posBefore = transform.position;
        Steer(dt);
        transform.position += transform.forward * _speed * dt;

        if (_armed) CheckHit(posBefore);

        _netTimer += dt;
        if (_netTimer >= 0.1f) { _netTimer = 0f; BroadcastPosition(); }
        _warnTimer += dt;
        if (_warnTimer >= WarnInterval) { _warnTimer = 0f; BroadcastWarn(); }
    }

    // ── 추진 / 항력 ───────────────────────────────────────────────────────
    void UpdateEnergy(float dt)
    {
        bool boosting = _age < Spec.boostTime;
        if (boosting) _speed += Spec.boostAccel * dt;
        _speed -= Spec.dragK * _speed * _speed * dt;
        _speed -= Gravity * transform.forward.y * dt;   // 상승 = 감속, 강하 = 가속
        _speed  = Mathf.Max(_speed, 30f);

        // 모터 연소가 끝나면 연기 트레일이 끊긴다 — 코스팅 중인 미사일은 눈으로 덜 보인다
        if (_trail != null && _trail.emitting != boosting) _trail.emitting = boosting;

        if (!boosting && _speed < Spec.minGuideSpeed && _deadTimer < 0f)
        {
            // 운동에너지 고갈 — 유도 불능, 잠시 탄도 비행 후 자폭
            LoseLock();
            _deadTimer = 2.5f;
        }
    }

    void UpdateTargetEstimate(float dt)
    {
        if (IntendedTarget == null) return;
        if (_prevTargetPosValid)
        {
            Vector3 raw = (IntendedTarget.position - _prevTargetPos) / dt;
            if (raw.sqrMagnitude < 1e6f) _targetVelEst = Vector3.Lerp(_targetVelEst, raw, VelSmooth);
        }
        _prevTargetPos      = IntendedTarget.position;
        _prevTargetPosValid = true;
    }

    // ── 유도 ──────────────────────────────────────────────────────────────
    void Steer(float dt)
    {
        if (Target == null || IsLost)
        {
            // 락 상실: 직진하며 서서히 기수가 처진다
            _prevLOS = Vector3.zero;
            Quaternion sag = Quaternion.LookRotation(Vector3.Slerp(transform.forward, Vector3.down, 0.04f * dt));
            transform.rotation = sag;
            return;
        }

        Vector3 aim;
        if (_phase == Phase.Midcourse)
        {
            UpdateDatalink(dt);
            aim = _interceptPoint;

            // 피트불 — 미사일 자체 레이더 능동 전환
            if (Vector3.Distance(transform.position, IntendedTarget.position) <= Spec.seekerRange)
            {
                _phase      = Phase.Terminal;
                IsARHActive = true;
                _prevLOS    = Vector3.zero;
                if (!InGimbal(IntendedTarget.position)) { LoseLock(); return; }
                BroadcastWarnNow();
            }
        }
        else
        {
            if (!InGimbal(Target.position)) { LoseLock(); return; }
            float dist = Vector3.Distance(transform.position, Target.position);
            float lag  = Target == IntendedTarget ? Mathf.Clamp01((dist - 150f) / 600f) * LagCompensation : 0f;
            aim = Target.position + (Target == IntendedTarget ? _targetVelEst * lag : Vector3.zero);
        }

        NavigatePN(aim, dt);
    }

    // 중간유도: 발사 기체가 표적을 계속 비추는 동안 예측 교점 갱신, 끊기면 마지막 교점으로 관성 비행
    void UpdateDatalink(float dt)
    {
        _datalinkTimer += dt;
        if (_datalinkTimer < DatalinkInterval) return;
        _datalinkTimer = 0f;

        bool supported = _shooter == null   // 발사 기체 정보 없음 → 지원 유지로 간주
            || (_shooter.gameObject.activeInHierarchy &&
                Vector3.Angle(_shooter.forward, IntendedTarget.position - _shooter.position) <= ShooterGimbal);
        if (supported) _interceptPoint = PredictIntercept();
    }

    Vector3 PredictIntercept()
    {
        float t = Vector3.Distance(transform.position, IntendedTarget.position) / Mathf.Max(_speed, 1f);
        for (int i = 0; i < 2; i++)
        {
            Vector3 p = IntendedTarget.position + _targetVelEst * t;
            t = Vector3.Distance(transform.position, p) / Mathf.Max(_speed, 1f);
        }
        return IntendedTarget.position + _targetVelEst * t;
    }

    // G 한계가 있는 비례항법 — a = N·Vc·(dλ/dt), |a| ≤ 가용 G
    void NavigatePN(Vector3 aim, float dt)
    {
        Vector3 toAim = aim - transform.position;
        if (toAim.sqrMagnitude < 0.01f) return;
        Vector3 LOS = toAim.normalized;
        Vector3 fwd = transform.forward;

        Vector3 accel = Vector3.zero;
        if (_prevLOS.sqrMagnitude > 0.5f)
        {
            Vector3 losRate = (LOS - _prevLOS) / dt;
            Vector3 tgtVel  = Target == IntendedTarget ? _targetVelEst : Vector3.zero;
            float   vc      = Mathf.Max(Vector3.Dot(fwd * _speed - tgtVel, LOS), 50f);
            accel = Spec.navConst * vc * losRate;
        }
        _prevLOS = LOS;

        // 시선각 오차가 크면(고각 발사·중간유도 직후) 추격 성분으로 기수를 먼저 돌린다
        float err = Vector3.Angle(fwd, LOS);
        if (err > 8f) accel += Vector3.ProjectOnPlane(LOS, fwd).normalized * (_speed * 1.2f * Mathf.Min(err, 60f) * Mathf.Deg2Rad);

        accel = Vector3.ProjectOnPlane(accel, fwd);
        float aMax = AvailableG * Gravity;
        if (accel.sqrMagnitude > aMax * aMax) accel = accel.normalized * aMax;

        Vector3 newFwd = (fwd * _speed + accel * dt).normalized;
        transform.rotation = Quaternion.LookRotation(newFwd, transform.up);

        // 고G 선회는 유도 항력을 만든다
        _speed -= accel.magnitude / Gravity * 0.18f * dt;
    }

    public float AvailableG => Spec.maxG * Mathf.Clamp01(Mathf.Pow(_speed / Mathf.Max(1f, Spec.fullGSpeed), 2f));

    bool InGimbal(Vector3 worldPos) => Vector3.Angle(transform.forward, worldPos - transform.position) <= Spec.gimbalLimit;

    void LoseLock()
    {
        if (IsLost) return;
        IsLost = true;
        Target = null;
    }

    // ── 대응책 ────────────────────────────────────────────────────────────
    void CheckCountermeasures()
    {
        if (IntendedTarget == null) return;
        // 중간유도 중인 레이더 미사일은 자체 시커가 꺼져 있다 — 채프에 속을 것이 없다
        if (!Spec.IsIR && _phase == Phase.Midcourse) return;
        var cfg = GameplayConfigSO.Instance;
        Vector3 toTarget = IntendedTarget.position - transform.position;
        float   tgtDist  = toTarget.magnitude;
        float   fov      = Spec.IsIR ? 9f : 6f;   // 시커 순간 시야 (IR 레티클 / 레이더 빔폭)

        var decoys = CountermeasureSystem.Active;
        for (int i = 0; i < decoys.Count; i++)
        {
            var d = decoys[i];
            if (d == null) continue;
            bool compatible = Spec.IsIR ? d.Type == CountermeasureType.Flare : d.Type == CountermeasureType.Chaff;
            if (!compatible) continue;
            if (_evaluatedDecoys.Contains(d)) continue;

            // 표적이 뿌린 것 (소유자 정보가 없으면 표적 근처)
            bool ours = !string.IsNullOrEmpty(d.Owner) && !string.IsNullOrEmpty(TargetNick)
                      ? d.Owner == TargetNick
                      : Vector3.Distance(d.Position, IntendedTarget.position) < 200f;
            if (!ours) continue;

            Vector3 toDecoy = d.Position - transform.position;
            if (Vector3.Angle(toDecoy, toTarget) > fov) continue;   // 아직 시커 시야 밖 — 나중에 들어오면 판정
            _evaluatedDecoys.Add(d);

            float p = Spec.IsIR ? FlareChance(cfg, tgtDist) : ChaffChance(cfg, d);
            if (Random.value < p)
            {
                Target          = d.transform;
                IsDeflected     = true;
                _reacquireTried = false;
                _prevLOS        = Vector3.zero;
                return;
            }
        }
    }

    float FlareChance(GameplayConfigSO cfg, float tgtDist)
    {
        float p = cfg.flareSeduceChance * (1f - Spec.ccmResistance);

        // 열원 — 애프터버너는 플레어보다 뜨겁고, 아이들은 플레어가 압도한다
        var pc = IntendedTarget.GetComponent<PlayerController>();
        if (pc != null && pc.isLocalPlayer)
        {
            if (pc.AfterburnerActive)    p *= 0.5f;
            else if (pc.Throttle01 < 0.35f) p *= 1.4f;
        }

        // 각도 — 꼬리 쪽에서는 엔진 배기열이 강해 플레어 효과가 떨어진다
        float aspect = Vector3.Angle(IntendedTarget.forward, transform.position - IntendedTarget.position);
        p *= aspect < 60f ? 0.8f : aspect > 120f ? 1.25f : 1f;

        // 너무 이른 플레어는 (기만체가 꺼진 뒤) 재포착될 가능성이 크다 — 판정은 그대로, 재포착에서 반영
        if (tgtDist > Spec.seekerRange) p *= 0.6f;
        return Mathf.Clamp01(p);
    }

    float ChaffChance(GameplayConfigSO cfg, CountermeasureDecoy d)
    {
        // 펄스-도플러: 채프(거의 정지)와 표적의 시선 방향 속도가 비슷할수록 구별 불가 → 노치 + 채프가 정석
        Vector3 los        = (IntendedTarget.position - transform.position).normalized;
        float   tgtRadial  = Mathf.Abs(Vector3.Dot(_targetVelEst, los));
        float   chaffRad   = Mathf.Abs(Vector3.Dot(d.Velocity, los));
        float   match      = Mathf.Clamp01(1f - Mathf.Abs(tgtRadial - chaffRad) / (cfg.notchSpeed * 2.5f));
        float   p          = cfg.chaffSeduceChance * (1f - Spec.ccmResistance) * Mathf.Lerp(0.35f, 2.2f, match);
        return Mathf.Clamp01(p);
    }

    // 도플러 노치 — 표적이 미사일 시선과 수직(빔)으로 날면 클러터 속에 묻혀 레이더 락을 잃는다
    void CheckNotch()
    {
        if (Spec.IsIR || _phase != Phase.Terminal || Target != IntendedTarget || IntendedTarget == null) return;
        var cfg = GameplayConfigSO.Instance;
        Vector3 los    = (IntendedTarget.position - transform.position).normalized;
        float   radial = Mathf.Abs(Vector3.Dot(_targetVelEst, los));
        _notchTime = radial < cfg.notchSpeed ? _notchTime + CmCheckInterval : Mathf.Max(0f, _notchTime - CmCheckInterval * 2f);

        // 저고도 표적일수록 해면 클러터가 강하다
        float lowAlt = IntendedTarget.position.y < 600f ? 1.5f : 1f;
        if (_notchTime > 0.8f && Random.value < 0.12f * (1f - Spec.ccmResistance) * lowAlt)
            LoseLock();
    }

    // 기만체가 꺼진 뒤 시커 시야 안에 원 표적이 있으면 1회 재포착 시도
    void TryReacquire()
    {
        if (_reacquireTried || IntendedTarget == null) { if (!IsLost) LoseLock(); return; }
        _reacquireTried = true;

        Vector3 to = IntendedTarget.position - transform.position;
        bool inView = Vector3.Angle(transform.forward, to) < 20f && to.magnitude < Spec.seekerRange * 1.2f;
        if (inView && Random.value < 0.35f + 0.45f * Spec.ccmResistance)
        {
            Target      = IntendedTarget;
            IsDeflected = false;
            _prevLOS    = Vector3.zero;
        }
        else LoseLock();
    }

    // ── 명중 판정 ─────────────────────────────────────────────────────────
    void CheckHit(Vector3 posBefore)
    {
        // 원 표적 기준 — 기만체를 쫓던 중이라도 표적 옆을 지나면 근접신관이 작동한다
        if (IntendedTarget != null && IntendedTarget.gameObject.activeInHierarchy)
        {
            float closest = SegmentDistance(posBefore, transform.position, IntendedTarget.position);
            if (closest <= Spec.proximityFuse) { Detonate(IntendedTarget, closest); return; }
        }
        // 기만체에 충돌
        if (Target != null && Target != IntendedTarget &&
            SegmentDistance(posBefore, transform.position, Target.position) <= 4f)
            Detonate(null, 0f);
    }

    static float SegmentDistance(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float   l2 = ab.sqrMagnitude;
        float   t  = l2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2) : 0f;
        return Vector3.Distance(a + ab * t, p);
    }

    void Detonate(Transform victimT, float missDist)
    {
        var victimPc  = victimT != null ? victimT.GetComponent<PlayerController>() : null;
        bool hitLocal = victimPc != null && victimPc.isLocalPlayer;
        string victim = victimPc != null ? victimPc.nickname : "";
        float damage  = victimPc != null ? CalcDamage(missDist) : 0f;

        HitEffectSystem.Instance?.TriggerHit(transform.position, hitLocal);

        // 명중 판정은 이 클라이언트(발사자/호스트)가 내리고, 피해는 피격체 소유자가 적용한다
        CombatDamage.Apply(victim, damage, ShooterNick, CombatDamage.WeaponMissile);
        CombatDamage.ReportHit(ShooterNick, victim, CombatDamage.WeaponMissile);
        BroadcastDestroy(victim, damage);
        Destroy(gameObject);
    }

    void SelfDestruct()
    {
        HitEffectSystem.Instance?.TriggerHit(transform.position, false);
        BroadcastDestroy("", 0f);
        Destroy(gameObject);
    }

    // 직격은 치명상, 근접신관은 파편 살상반경 안에서 거리에 따라 감쇠
    float CalcDamage(float dist)
    {
        float baseDmg = GameplayConfigSO.Instance.missileDamage * Spec.damageScale;
        if (dist <= Spec.directHitDist) return baseDmg * 2.5f;
        float t = Mathf.InverseLerp(Spec.directHitDist, Spec.proximityFuse, dist);
        return baseDmg * Mathf.Lerp(1.5f, 0.75f, t);
    }

    // ── 네트워크 ──────────────────────────────────────────────────────────
    // 표적에게 주기적으로 경보 — IR 은 미사일 접근 경보(MAWS), 레이더는 피트불 후 ARH 경보.
    // 기만되었거나 락을 잃은 미사일은 경보를 멈춘다 (표적 RWR 에서 위협이 사라짐)
    void BroadcastWarn()
    {
        if (!IsTrackingTarget || string.IsNullOrEmpty(TargetNick)) return;
        BroadcastWarnNow();
    }

    void BroadcastWarnNow()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null || string.IsNullOrEmpty(TargetNick) || !CombatDamage.SimulatesWeaponOf(ShooterNick)) return;
        sc.SendMissileWarn(ShooterNick, TargetNick, IsARHActive
            ? (int)ThreatWarningSystem.ThreatLevel.MissileActive
            : (int)ThreatWarningSystem.ThreatLevel.MissileFired);
    }

    void BroadcastPosition()
    {
        var sc = NetworkManager.Instance?.socketClient;
        // AI 가 쏜 미사일은 호스트가 시뮬레이션한다
        if (sc == null || !CombatDamage.SimulatesWeaponOf(ShooterNick)) return;
        sc.SendMissileMove(MissileId, transform.position, transform.eulerAngles, _speed);
    }

    void BroadcastDestroy(string victim, float damage)
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null || !CombatDamage.SimulatesWeaponOf(ShooterNick)) return;
        sc.SendMissileDestroy(MissileId, ShooterNick, victim, transform.position, damage);
    }
}

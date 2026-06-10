using UnityEngine;

// 적 전투기 AI.
// 플레이어를 탐지하면 접근 → 인터셉트 → 미사일 발사 → 이탈 사이클을 반복한다.
// 직접 충돌 공격은 없으며, 위협 기동으로 플레이어를 압박한다.
public class EnemyAI : MonoBehaviour
{
    public enum CombatState { Patrol, Approach, Intercept, LaunchReady, Launch, Evade, Return }

    [Header("교전 거리 (m)")]
    [SerializeField] float engageRange    = 2500f;  // 탐지·추적 시작 (좁혀서 먼저 발견 우위)
    [SerializeField] float launchRange    = 1800f;  // 미사일 발사 가능
    [SerializeField] float minFlightDist  = 400f;   // 이 이하면 Evade
    [SerializeField] float launchCooldown = 20f;    // 발사 쿨다운 (길게 → 공격 빈도 낮춤)
    [SerializeField] int   maxSalvos      = 2;      // 최대 발사 횟수 감소

    [Header("순찰")]
    [SerializeField] float patrolRadius = 900f;
    [SerializeField] float patrolAlt    = 380f;

    [Header("속도 (m/s)")]
    [SerializeField] float patrolSpeed  = 36f;   // 순찰 고정 속도
    const float            CombatSpeed  = 56f;   // 플레이어 최대(80) × 0.7 고정

    [Header("프리팹")]
    [Tooltip("미사일 프리팹. 미할당 시 Resources/MissilePrefab 폴백 → 그것도 없으면 fallback 캡슐")]
    [SerializeField] GameObject _missilePrefab;

    float DynSpeed => CombatSpeed;

    // 항모 탑승·프리플라이트 중에는 전투 비활성
    bool _combatEnabled;

    public void EnableCombat() => _combatEnabled = true;

    AIBotController _bot;
    Transform       _target;
    string          _targetNick;

    CombatState _state      = CombatState.Patrol;
    float       _stateTimer;
    float       _launchTimer;
    int         _salvosFired;

    Vector3 _patrolCenter;
    Vector3 _patrolWP;

    public CombatState State => _state;

    public void Initialize(Transform target, string targetNickname)
    {
        _target       = target;
        _targetNick   = targetNickname;
        _bot          = GetComponent<AIBotController>();
        _patrolCenter = transform.position;
        _patrolWP     = NextPatrolWP();
        // 선회율을 낮춰 플레이어보다 기동성 열세 (플레이어 기본 ~120 deg/s 대비)
        _bot.SetTurnRate(38f);
        // 첫 발사 타이밍 분산 (여러 적기가 동시 발사 방지)
        _launchTimer = Random.Range(0f, launchCooldown * 0.5f);
    }

    // ── Update ────────────────────────────────────────────────────────────

    void Update()
    {
        if (_bot == null) return;

        _stateTimer  += Time.deltaTime;
        _launchTimer -= Time.deltaTime;

        if (_target == null) { DoPatrol(); return; }

        float dist = Vector3.Distance(transform.position, _target.position);

        switch (_state)
        {
            case CombatState.Patrol:
                DoPatrol();
                if (_combatEnabled && dist < engageRange && _salvosFired < maxSalvos)
                    Transition(CombatState.Approach);
                break;

            case CombatState.Approach:   DoApproach(dist);    break;
            case CombatState.Intercept:  DoIntercept(dist);   break;
            case CombatState.LaunchReady: DoLaunchReady(dist); break;
            case CombatState.Launch:     DoLaunch();          break;
            case CombatState.Evade:      DoEvade();           break;
            case CombatState.Return:     DoReturn();          break;
        }
    }

    // ── 상태별 동작 ──────────────────────────────────────────────────────

    void DoPatrol()
    {
        if (Vector3.Distance(transform.position, _patrolWP) < 65f)
            _patrolWP = NextPatrolWP();
        FlyTo(_patrolWP, patrolSpeed);
    }

    void DoApproach(float dist)
    {
        if (dist > engageRange * 1.3f) { Transition(CombatState.Patrol);    return; }
        if (dist < launchRange * 1.2f) { Transition(CombatState.Intercept); return; }

        // 비스듬한 접근으로 위협 느낌 연출
        Vector3 flank = _target.position
            + _target.right * (transform.position.x > _target.position.x ? 90f : -90f)
            + Vector3.up * 30f;
        FlyTo(flank, DynSpeed);
    }

    void DoIntercept(float dist)
    {
        if (dist > engageRange * 1.5f)   { Transition(CombatState.Approach); return; }
        if (dist < minFlightDist)         { Transition(CombatState.Evade);    return; }

        // 예측 교점으로 비행 (lead pursuit)
        var   targetPC  = _target.GetComponent<PlayerController>();
        float targetSpd = targetPC != null ? targetPC.CurrentSpeed : 60f;
        float tof       = dist / Mathf.Max(DynSpeed, 1f);
        Vector3 predict = _target.position + _target.forward * (targetSpd * tof * 0.4f);
        FlyTo(predict, DynSpeed);

        if (_launchTimer <= 0f && dist < launchRange && _salvosFired < maxSalvos)
            Transition(CombatState.LaunchReady);
    }

    void DoLaunchReady(float dist)
    {
        // 발사 자세 정렬 (1.2초) — 타겟을 직접 겨냥
        FlyTo(_target.position, DynSpeed * 0.9f);
        if (_stateTimer >= 1.2f)
            Transition(CombatState.Launch);
    }

    void DoLaunch()
    {
        FireMissile();
        _launchTimer = launchCooldown;
        _salvosFired++;
        Transition(CombatState.Evade);
    }

    void DoEvade()
    {
        // 단순 직선 이탈 — 지그재그 제거로 탄 리드가 쉬워짐
        Vector3 away = (transform.position - _target.position).normalized;
        Vector3 dest = transform.position + away * 350f;
        FlyTo(dest, DynSpeed);

        if (_stateTimer >= 3.5f)
        {
            Transition(_salvosFired < maxSalvos ? CombatState.Intercept : CombatState.Return);
        }
    }

    void DoReturn()
    {
        FlyTo(_patrolCenter + Vector3.up * patrolAlt, patrolSpeed);
        if (Vector3.Distance(transform.position, _patrolCenter) < 130f)
        {
            _salvosFired = 0;
            Transition(CombatState.Patrol);
        }
    }

    // ── 미사일 발사 ────────────────────────────────────────────────────────

    void FireMissile()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null || _target == null) return;

        Vector3    launchPos = transform.position + transform.forward * 4f + transform.up * -0.4f;
        Quaternion launchRot = transform.rotation;

        // 미사일 오브젝트 생성 (호스트 로컬)
        GameObject prefab = _missilePrefab != null ? _missilePrefab : Resources.Load<GameObject>("MissilePrefab");
        GameObject go = prefab != null
            ? Instantiate(prefab, launchPos, launchRot)
            : CreateFallbackMissile(launchPos, launchRot);

        string missileId = $"AI_{_bot.Nickname}_{Time.time:F1}";
        var mc = go.GetComponent<MissileController>() ?? go.AddComponent<MissileController>();
        mc.Initialize(_target, _bot.Speed + 35f,
            MissileGuidanceType.HeatSeeking, missileId, _bot.Nickname);

        // 네트워크 브로드캐스트
        sc.SendMissileSpawn(missileId, _bot.Nickname, _targetNick,
            launchPos, launchRot.eulerAngles, (int)MissileGuidanceType.HeatSeeking);

        sc.SendMissileWarn(_bot.Nickname, _targetNick,
            (int)ThreatWarningSystem.ThreatLevel.MissileFired);

        Debug.Log($"[AI] {_bot.Nickname} fired missile → {_targetNick}  salvo={_salvosFired + 1}/{maxSalvos}");
    }

    static GameObject CreateFallbackMissile(Vector3 pos, Quaternion rot)
    {
        var go = new GameObject("AI_Missile");
        go.transform.SetPositionAndRotation(pos, rot);
        go.AddComponent<MissileController>();
        return go;
    }

    // ── 이동 헬퍼 ─────────────────────────────────────────────────────────

    void FlyTo(Vector3 dest, float speed)
    {
        Vector3 toTarget = dest - transform.position;
        if (toTarget.sqrMagnitude < 0.01f) { _bot.SetTarget(dest, transform.rotation, speed); return; }

        Vector3    fwd       = toTarget.normalized;
        // 선회 시 뱅크각 추가 (dot으로 좌/우 판별)
        float      bankAngle = Mathf.Clamp(Vector3.Dot(Vector3.Cross(Vector3.up, fwd).normalized,
                                                         transform.right) * 50f, -50f, 50f);
        Quaternion targetRot = Quaternion.LookRotation(fwd, Vector3.up)
                             * Quaternion.Euler(0f, 0f, -bankAngle);
        _bot.SetTarget(dest, targetRot, speed);
    }

    void Transition(CombatState next) { _state = next; _stateTimer = 0f; }

    Vector3 NextPatrolWP()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float r     = Random.Range(patrolRadius * 0.3f, patrolRadius);
        return _patrolCenter + new Vector3(
            Mathf.Cos(angle) * r,
            patrolAlt + Random.Range(-70f, 70f),
            Mathf.Sin(angle) * r);
    }
}

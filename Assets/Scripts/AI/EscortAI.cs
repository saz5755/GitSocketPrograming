using System.Collections.Generic;
using UnityEngine;

// 에스코트 편대 AI — 상태 머신 기반.
// OnDeck → Launching → Escorting ↔ FreeFlightZone → LandingApproach → BeingArrested / Landed
public class EscortAI : MonoBehaviour
{
    enum Phase
    {
        OnDeck, Launching, Escorting,
        FreeFlightZone,     // 항모 존 내 자유비행 (V자 추종 해제)
        FreeFlight,         // 타이머 기반 자유비행 (폴백 — 존 외부에서 착함 지시 시)
        LandingApproach,    // 파이널 어프로치 (웨이포인트 → 갑판)
        BeingArrested,      // 어레스팅 와이어 체결 후 감속 중
        Landed
    }

    // 어레스팅 와이어 시스템이 접근 중인 에스코트를 탐색할 수 있도록 전역 목록 노출
    public static readonly List<EscortAI> AllEscorts = new();

    [Header("편대 슬롯 (리더 기준 로컬 오프셋, X 부호로 좌/우 자동)")]
    [SerializeField] Vector3 _formationOffset = new Vector3(-32f, -5f, -40f);

    [Header("동작 튜닝 데이터")]
    [Tooltip("EscortBehaviorSO 에셋. Project 우클릭 → Create → KF21 → AI → Escort Behavior")]
    [SerializeField] EscortBehaviorSO _behavior;

    // SO 미할당 시 한 번만 만들어지는 폴백 인스턴스 — 모든 EscortAI가 공유
    static EscortBehaviorSO s_fallbackBehavior;
    EscortBehaviorSO Cfg
    {
        get
        {
            if (_behavior != null) return _behavior;
            if (s_fallbackBehavior == null)
            {
                s_fallbackBehavior = ScriptableObject.CreateInstance<EscortBehaviorSO>();
                if (Application.isPlaying)
                    Debug.LogWarning(
                        $"[EscortAI] '{name}' has no EscortBehaviorSO assigned — using built-in defaults. " +
                        "Create one via Project window → Right Click → Create → KF21 → AI → Escort Behavior."
                    );
            }
            return s_fallbackBehavior;
        }
    }

    Transform         _leader;
    AIBotController   _bot;
    Phase             _phase = Phase.OnDeck;
    CarrierController _cachedCarrier;
    PlayerController  _leaderPC;      // leader.GetComponent<PlayerController>() 캐시 — 매 프레임 호출 제거
    float             _launchTimer;
    float             _freeFlightTimer;

    // 착함 상태
    Transform _carrier;
    Vector3   _approachWaypoint;
    Vector3   _landingSpot;
    bool      _waypointReached;
    float     _currentSpeed;

    // 플레이어 경로 기반 어프로치
    Vector3 _playerApproachDir;
    Vector3 _playerWirePos;
    bool    _hasApproachInfo;

    // 편대 합류 후 속도 미세 조정값 (에스코트마다 고정 랜덤 — 자연스러운 간격 유지)
    float _speedJitter;

    // 리더 정지/하차 감지용 타이머
    float _leaderStopTimer;

    // FreeFlightZone 순찰 웨이포인트
    Vector3 _patrolWaypoint;
    bool    _hasPatrolWaypoint;

    // 어레스팅 와이어 체결 상태
    float   _arrestDeckY;
    float   _arrestSpeed;
    Vector3 _landingSlot;       // 착함 목표 슬롯 월드 좌표 — BeingArrested가 이 위치에 정지
    float   _modelBottomOffset; // transform.position.y ~ 모델 최하단까지 거리 (바퀴 관통 방지)

    // 편대 비행 회전 보간 — 매 프레임 raw 목표 회전을 즉시 적용하면 좌우 진동 발생
    Quaternion _smoothFormationRot;

    public Vector3 FormationOffset => _formationOffset;
    public bool    IsLeftSide      => _formationOffset.x < 0f;
    public bool    IsLaunching     => _phase == Phase.Launching;
    public bool    IsLanded        => _phase == Phase.Landed || _phase == Phase.BeingArrested;
    // ArrestingWireSystem이 체결 시도 여부를 판단하는 프로퍼티
    public bool    IsInApproachRun => _phase == Phase.LandingApproach;

    // ── 생명주기 ──────────────────────────────────────────────────────────────

    void Awake()
    {
        AllEscorts.Add(this);
    }

    void OnDestroy()
    {
        AllEscorts.Remove(this);
    }

    void Start()
    {
        if (_cachedCarrier == null)
            _cachedCarrier = Object.FindFirstObjectByType<CarrierController>();
    }

    // ── 외부 API ──────────────────────────────────────────────────────────────

    // AIManager가 SpawnEscorts에서 호출 — Initialize 전에 SO 주입
    public void SetBehavior(EscortBehaviorSO behavior)
    {
        if (behavior != null) _behavior = behavior;
    }

    public void Initialize(Transform leader, Vector3 formationOffset, bool spawnedOnDeck)
    {
        _leader           = leader;
        _leaderPC         = leader != null ? leader.GetComponent<PlayerController>() : null;
        _formationOffset  = formationOffset;
        _bot              = GetComponent<AIBotController>();
        _bot.PositionOverride = true;
        _bot.SetMaxSpeed(Cfg.maxSpeedOverride);
        _bot.SetTurnRate(Cfg.turnRateOverride);
        _speedJitter          = Random.Range(-Cfg.speedJitterRange, Cfg.speedJitterRange);
        _smoothFormationRot   = transform.rotation;
        _phase                = spawnedOnDeck ? Phase.OnDeck : Phase.Escorting;
        _cachedCarrier    = Object.FindFirstObjectByType<CarrierController>();

        // 모델 최하단 ~ 피벗 거리 계산 — 착함 시 바퀴 갑판 관통 방지
        float minY = float.MaxValue;
        foreach (var r in GetComponentsInChildren<Renderer>())
            if (r.bounds.min.y < minY) minY = r.bounds.min.y;
        _modelBottomOffset = minY < float.MaxValue ? transform.position.y - minY : 0f;
    }

    public void UpdateLeader(Transform newLeader)
    {
        _leader   = newLeader;
        _leaderPC = newLeader != null ? newLeader.GetComponent<PlayerController>() : null;
    }

    // EnableAICombat() → 플레이어 발진 직후 순차 호출
    // OnDeck(최초) 또는 Landed(재발진) 상태에서 호출 가능
    public void BeginLaunchSequence()
    {
        if (_phase != Phase.OnDeck && _phase != Phase.Landed) return;

        if (_phase == Phase.OnDeck)
        {
            var bt = GetComponent<AircraftBoardingTrigger>();
            if (bt != null) bt.enabled = false;
        }

        // 착함 시 항모에 부착된 경우 분리 — 재발진 전 월드 좌표 독립
        if (transform.parent != null)
            transform.SetParent(null, worldPositionStays: true);

        _launchTimer              = 0f;
        _bot.PositionOverride     = false;
        _phase                    = Phase.Launching;
    }

    // AIManager.BeginEscortLanding() → 플레이어 착함 직후 순차 호출
    // FreeFlightZone 상태면 즉시 어프로치, Escorting 상태면 5초 자유비행 후 어프로치
    public void BeginLanding(Transform carrier)
    {
        _carrier         = carrier;
        _hasApproachInfo = false;
        BeginLandingShared();
    }

    // AIManager.BeginEscortLandingFromWire() → 슬롯 위치 기반 착함
    public void BeginLandingWithPath(Transform carrier, Vector3 approachDir, Vector3 slotPos)
    {
        _carrier           = carrier != null ? carrier : _cachedCarrier?.transform;
        _playerApproachDir = approachDir;
        _playerWirePos     = slotPos;
        _landingSlot       = slotPos;
        _hasApproachInfo   = true;
        BeginLandingShared();
    }

    void BeginLandingShared()
    {
        // 발진 중(Launching) + 이미 착함 진행 중/갑판/완료 상태면 무시.
        // Launching은 6초 직진이 의도이므로 끊지 않음 (AIManager.EscortLandingCoroutine이
        // IsLaunching=false 될 때까지 대기 후 다시 호출).
        if (_phase == Phase.OnDeck || _phase == Phase.Landed || _phase == Phase.Launching ||
            _phase == Phase.BeingArrested || _phase == Phase.LandingApproach ||
            _phase == Phase.FreeFlight)
        {
            Debug.Log($"[EscortAI] {name}: BeginLanding IGNORED, phase={_phase}");
            return;
        }

        // Escorting / FreeFlightZone → FreeFlight (자유비행 후 어프로치)
        Phase prev = _phase;
        _freeFlightTimer      = 0f;
        _bot.PositionOverride = false;
        _phase                = Phase.FreeFlight;
        Debug.Log($"[EscortAI] {name}: BeginLanding {prev} → FreeFlight (hasPath={_hasApproachInfo})");
    }

    // AIManager.BeginEscortFreeFlightNow() 에서 호출 — 즉시 자유비행 전환 (Escorting/Launching 시)
    public void ForceFreeFlight()
    {
        if (_phase == Phase.Escorting || _phase == Phase.Launching)
        {
            _bot.PositionOverride  = false;
            _leaderStopTimer       = 0f;
            _hasPatrolWaypoint     = false;
            _phase                 = Phase.FreeFlightZone;
        }
    }

    // EscortZoneTrigger — 플레이어가 존에 진입할 때 호출 (Escorting/Launching → FreeFlightZone)
    public void OnLeaderEnteredZone()
    {
        if (_phase == Phase.Escorting || _phase == Phase.Launching)
        {
            _bot.PositionOverride  = false;
            _leaderStopTimer       = 0f;
            _hasPatrolWaypoint     = false;
            _phase                 = Phase.FreeFlightZone;
        }
    }

    // EscortZoneTrigger — 플레이어가 존을 이탈할 때 호출 (FreeFlightZone → Escorting)
    public void OnLeaderExitedZone()
    {
        if (_phase == Phase.FreeFlightZone)
        {
            _leaderStopTimer = 0f;
            _phase           = Phase.Escorting;
        }
    }

    // ArrestingWireSystem이 와이어 체결 시 호출 (또는 UpdateLandingApproach 내부에서 직접)
    public void OnWireCaught(float deckY)
    {
        if (_phase != Phase.LandingApproach) return;
        _arrestDeckY  = deckY + _modelBottomOffset; // 피벗 높이 보정 — 바퀴 갑판 관통 방지
        _arrestSpeed  = Cfg.arrestEntrySpeed;        // 고정 진입속도로 급감속 연출
        // CheckEscortWire 경로: _landingSlot이 미설정이면 현재 목표로 초기화
        if (_landingSlot == Vector3.zero) _landingSlot = _landingSpot;
        _bot.PositionOverride = true;
        _phase = Phase.BeingArrested;
        Debug.Log($"[EscortAI] {name} 와이어 체결! 진입속도={_arrestSpeed:F1}  목표슬롯={_landingSlot}");
    }

    // ArrestingWireSystem이 플레이어 와이어 잡힌 후 호출 — 진행 중인 에스코트의 경로 갱신.
    // 자유비행 중이면 다음 SetupApproach에서 사용,
    // LandingApproach 중이면 landingSpot만 갱신해 자연스럽게 와이어 위치로 향하도록 한다.
    public void UpdateApproachPath(Vector3 approachDir, Vector3 wirePos)
    {
        _playerApproachDir = approachDir;
        _playerWirePos     = wirePos;
        _hasApproachInfo   = true;

        if (_phase == Phase.LandingApproach)
        {
            // wp는 유지(이미 거쳤거나 향해 비행 중) — landingSpot만 와이어 위치 + 슬롯 측면 오프셋으로 갱신
            Vector3 sideDir = Vector3.Cross(approachDir.normalized, Vector3.up).normalized;
            _landingSpot    = wirePos + sideDir * Cfg.GetSideOffset(IsLeftSide);
            _landingSpot.y  = wirePos.y + 1f;
        }
    }

    // ── Unity Update ─────────────────────────────────────────────────────────

    void Update()
    {
        // ── 매 프레임 leader zone 위치 기반 페이즈 보정 ──────────────────────
        // EscortZoneTrigger 이벤트가 발화되지 못한 케이스(이륙 첫 프레임 등) 폴백.
        // Launching은 6초 직진 유지가 의도라 제외 — Escorting 중 leader가 zone 안에 있으면
        // 즉시 FreeFlightZone으로 전환해 V-formation 추종을 차단한다.
        if (_leader != null && EscortZoneTrigger.Instance != null && _phase == Phase.Escorting)
        {
            if (EscortZoneTrigger.Instance.IsInsideXZ(_leader.position))
            {
                _bot.PositionOverride  = false;
                _leaderStopTimer       = 0f;
                _hasPatrolWaypoint     = false;
                _phase                 = Phase.FreeFlightZone;
            }
        }

        switch (_phase)
        {
            case Phase.Launching:       UpdateLaunching();       break;
            case Phase.Escorting:       UpdateEscorting();       break;
            case Phase.FreeFlightZone:  UpdateFreeFlightZone();  break;
            case Phase.FreeFlight:      UpdateFreeFlight();      break;
            case Phase.LandingApproach: UpdateLandingApproach(); break;
            case Phase.BeingArrested:   UpdateBeingArrested();   break;
            case Phase.Landed:          UpdateLanded();          break;
        }
    }

    // ── 발진 ─────────────────────────────────────────────────────────────────

    void UpdateLaunching()
    {
        _launchTimer += Time.deltaTime;

        Quaternion launchRot = _cachedCarrier != null
            ? _cachedCarrier.transform.rotation
            : transform.rotation;

        Vector3 farAhead = transform.position + launchRot * Vector3.forward * 3000f;
        _bot.SetTarget(farAhead, launchRot, Cfg.launchSpeed);

        if (_launchTimer >= Cfg.launchDuration)
        {
            // PositionOverride는 발진 시작 시 이미 false — 유지
            _phase = Phase.Escorting;
        }
    }

    // ── V자 편대비행 ─────────────────────────────────────────────────────────

    void UpdateEscorting()
    {
        if (_leader == null || _bot == null) return;

        bool    isLeft      = IsLeftSide;
        Vector3 localOffset = _formationOffset;
        Vector3 targetPos   = _leader.TransformPoint(localOffset);
        Vector3 toTarget    = targetPos - transform.position;
        float   dist        = toTarget.magnitude;

        float leaderSpeed = _leaderPC != null ? _leaderPC.CurrentSpeed : 62f;
        float baseSpeed   = Mathf.Max(leaderSpeed, 40f);

        // ── 속도: 종방향(Z) 오차 기반 제어 ──────────────────────────────────
        // 단순 거리 기반 2배속은 앞지름 → 오버슈트를 유발.
        // 플레이어 로컬 Z 오차로 "앞서 나감/뒤처짐" 을 구분해 부드럽게 조정.
        Vector3 selfLocal = _leader.InverseTransformPoint(transform.position);
        float   zError    = selfLocal.z - localOffset.z;   // +: 슬롯보다 앞, -: 뒤

        float targetSpeed;
        if (zError > Cfg.slotAheadThreshold)
        {
            // 에스코트가 슬롯보다 앞서 나간 경우 → 감속해서 플레이어가 통과하게 함
            float t = Mathf.Clamp01(zError / 60f);
            targetSpeed = baseSpeed * Mathf.Lerp(1f, 0.75f, t);
        }
        else
        {
            // 뒤처지거나 정렬된 경우 → 거리에 비례한 완만한 가속 (최대 +20%)
            float t = Mathf.SmoothStep(0f, 0.2f, Mathf.Clamp01(dist / 100f));
            targetSpeed = baseSpeed * (1f + t);
        }
        // 슬롯 6m 이내: 완전히 리더 속도에 맞춤 (미세 진동 방지)
        if (dist < 6f)
            targetSpeed = baseSpeed + _speedJitter * (dist / 6f);

        // ── 기수 방향: 슬롯이 등 뒤일 때 U턴 금지 ────────────────────────────
        Quaternion leaderRot = _leader.rotation * Quaternion.Euler(0f, 0f, Cfg.GetBankOffset(isLeft));

        Quaternion rot;
        if (toTarget.sqrMagnitude < 0.01f)
        {
            rot = leaderRot;
        }
        else
        {
            // 슬롯이 현재 기수 기준 뒤쪽(dot < 0)이고 충분히 멀면
            // → 선회 없이 리더 방향으로 직진. 속도 차이로 위치가 자연히 보정됨.
            // → 루프/U턴을 방지하는 핵심 로직.
            float forwardDot = Vector3.Dot(transform.forward, toTarget.normalized);
            if (forwardDot < 0f && dist > 15f)
            {
                rot = leaderRot;
            }
            else
            {
                // 리더 로컬 좌표로 변환 후 횡방향(X) 성분을 줄임
                // — X 오차를 그대로 두면 오버슈트 → 반대 방향 수정 → 좌우 진동 반복
                Vector3 toTargetLocal = _leader.InverseTransformDirection(toTarget);
                toTargetLocal.x *= 0.25f;
                Quaternion lookRot = Quaternion.LookRotation(
                    _leader.TransformDirection(toTargetLocal).normalized, _leader.up);
                float blend = Mathf.Clamp01(1f - dist / Cfg.snapDistance);
                rot = Quaternion.Slerp(lookRot, leaderRot, blend);
            }
        }

        // 목표 회전을 매 프레임 즉시 적용하지 않고 서서히 추종 — 급반전에 의한 좌우 떨림 방지
        _smoothFormationRot = Quaternion.Slerp(_smoothFormationRot, rot, Cfg.rotSmoothSpeed * Time.deltaTime);
        _bot.PositionOverride = false;
        _bot.SetTarget(targetPos, _smoothFormationRot, targetSpeed);

        // ── 리더 정지 감지 → 자동 착함 시작 ────────────────────────────────────
        // FreeFlightZone과 동일한 보호 로직 — Escorting 상태에서도 leader가 멈추면
        // 일정 시간 후 BeginEscortLanding 호출 (zone 콜백이 IsFlying=false라 안 fire되는 경우 폴백)
        bool leaderFlying = _leaderPC != null && _leaderPC.IsFlying && _leaderPC.CurrentSpeed > 1f;
        if (!leaderFlying)
        {
            _leaderStopTimer += Time.deltaTime;
            if (_leaderStopTimer >= Cfg.leaderStopTimeout)
                AIManager.Instance?.BeginEscortLanding(_cachedCarrier?.transform);
        }
        else
        {
            _leaderStopTimer = 0f;
        }
    }

    // ── 항모 존 내 자유비행 (웨이포인트 순찰) ────────────────────────────────
    // 플레이어가 존 안에 있거나 착함한 동안 에스코트는 EscortZone 내에서 랜덤 웨이포인트를
    // 순차적으로 비행한다. 착함 시도나 플레이어 추종 없음.
    // 플레이어가 존을 벗어나면 OnLeaderExitedZone() → Escorting 으로 자동 복귀.

    void UpdateFreeFlightZone()
    {
        Vector3 zoneCenter = EscortZoneTrigger.Instance != null
            ? EscortZoneTrigger.Instance.transform.position
            : (_cachedCarrier != null ? _cachedCarrier.transform.position : transform.position);

        float outerR = EscortZoneTrigger.Instance != null
            ? EscortZoneTrigger.Instance.Radius
            : (_cachedCarrier != null ? _cachedCarrier.EscortZoneRadius : Cfg.outerRadiusFallback);
        float innerR = EscortInnerZoneTrigger.Instance != null
            ? EscortInnerZoneTrigger.Instance.Radius
            : (_cachedCarrier != null ? _cachedCarrier.InnerAvoidanceRadius : Cfg.innerRadiusFallback);

        // 웨이포인트 미설정 or 도착 시 새 순찰 지점 선택
        if (!_hasPatrolWaypoint ||
            Vector3.Distance(transform.position, _patrolWaypoint) < Cfg.waypointArriveDistance)
        {
            _patrolWaypoint    = PickPatrolWaypoint(zoneCenter, innerR, outerR);
            _hasPatrolWaypoint = true;
        }

        Vector3 dir = (_patrolWaypoint - transform.position).normalized;
        if (dir.sqrMagnitude < 0.01f) dir = transform.forward;

        _bot.SetTarget(_patrolWaypoint, Quaternion.LookRotation(dir, Vector3.up), Cfg.approachSpeed);
    }

    // 존 안에서 빙글빙글 돌지 않도록 내·외부 여유 반경 사이의 랜덤 지점을 반환.
    // 현재 위치에서 최소 200m 이상 떨어진 지점을 골라 즉각 U턴을 방지한다.
    // XZ 반경과 고도 오프셋이 함께 outerR 구 안에 들어오도록 3D 구 제약을 적용한다.
    Vector3 PickPatrolWaypoint(Vector3 zoneCenter, float innerR, float outerR)
    {
        float safeInner = innerR + 80f;
        float safeOuter = Mathf.Max(safeInner + 100f, outerR - 80f);

        for (int i = 0; i < 10; i++)
        {
            float angle  = Random.Range(0f, Mathf.PI * 2f);
            float radius = Random.Range(safeInner, safeOuter);
            // 3D 구 제약: xzDist^2 + altOffset^2 ≤ outerR^2
            float maxAltInSphere = Mathf.Sqrt(Mathf.Max(0f, outerR * outerR - radius * radius)) - 20f;
            float altOffset = Random.Range(80f, Mathf.Max(81f, maxAltInSphere));
            Vector3 candidate = new Vector3(
                zoneCenter.x + Mathf.Cos(angle) * radius,
                zoneCenter.y + altOffset,
                zoneCenter.z + Mathf.Sin(angle) * radius);
            if (Vector3.Distance(transform.position, candidate) > 200f)
                return candidate;
        }
        // 폴백: 반드시 반환
        float fa  = Random.Range(0f, Mathf.PI * 2f);
        float fr  = (safeInner + safeOuter) * 0.5f;
        float fma = Mathf.Sqrt(Mathf.Max(0f, outerR * outerR - fr * fr)) - 20f;
        return new Vector3(
            zoneCenter.x + Mathf.Cos(fa) * fr,
            zoneCenter.y + Mathf.Clamp(150f, 80f, fma),
            zoneCenter.z + Mathf.Sin(fa) * fr);
    }

    // ── 타이머 자유비행 (Escorting에서 BeginLanding 시 폴백) ─────────────────

    void UpdateFreeFlight()
    {
        _freeFlightTimer += Time.deltaTime;
        _bot.SetTarget(transform.position + transform.forward * 3000f, transform.rotation, Cfg.approachSpeed);

        if (_freeFlightTimer >= Cfg.freeFlightDuration)
        {
            SetupApproach();
            _bot.PositionOverride = true;
            _phase = Phase.LandingApproach;
            Debug.Log($"[EscortAI] {name}: FreeFlight → LandingApproach, wp={_approachWaypoint}, landingSpot={_landingSpot}, hasPath={_hasApproachInfo}");
        }
    }

    // ── 갑판 정지 유지 ───────────────────────────────────────────────────────
    // Landed 상태에서 매 프레임 PositionOverride를 강제 유지 — 외부 상태 변화 방어
    void UpdateLanded()
    {
        _bot.PositionOverride = true;
        _bot.SetTarget(transform.position, transform.rotation, 0f);
    }

    // ── 파이널 어프로치 & 착함 ───────────────────────────────────────────────

    void UpdateLandingApproach()
    {
        // _carrier가 null이어도 hasApproachInfo가 있으면 접근 가능 — 먼저 lazily 탐색 후 판단
        if (_carrier == null)
        {
            _carrier = _cachedCarrier?.transform
                    ?? Object.FindFirstObjectByType<CarrierController>()?.transform;
            if (_carrier == null && !_hasApproachInfo)
            { _phase = Phase.Escorting; return; }
        }

        // 각속도 기반 회전 (90°/s) — 직선 비행 중 옆으로 미끄러지는 모습 방지
        const float ApproachTurnRate = 90f;

        if (!_waypointReached)
        {
            float dist = Vector3.Distance(transform.position, _approachWaypoint);
            if (dist < Cfg.waypointArriveDistance) { _waypointReached = true; return; }

            Vector3 dir = (_approachWaypoint - transform.position).normalized;
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(dir), ApproachTurnRate * Time.deltaTime);
            transform.position = Vector3.MoveTowards(
                transform.position, _approachWaypoint, Cfg.approachSpeed * Time.deltaTime);
            _bot.SetTarget(_approachWaypoint, transform.rotation, Cfg.approachSpeed);
        }
        else
        {
            Vector3 toLanding = _landingSpot - transform.position;
            float   dist      = toLanding.magnitude;

            // 어레스팅 진입 트리거: 물리 공식 기반 런아웃 거리 이내 → BeingArrested 시작
            // catchDist = v²/(2a) — 이 거리에서 체결하면 arrestEntrySpeed로 감속해 슬롯에 정확히 정지
            float catchDist = Cfg.arrestEntrySpeed * Cfg.arrestEntrySpeed / (2f * Cfg.arrestDecelRate);
            if (dist < catchDist)
            {
                float deckY = _hasApproachInfo
                    ? _landingSlot.y
                    : (_carrier != null ? _carrier.position.y : _landingSpot.y);
                OnWireCaught(deckY);
                return;
            }

            float targetSpeed = Mathf.Lerp(Cfg.finalSpeed, Cfg.approachSpeed,
                                            Mathf.Clamp01(dist / 500f));
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, 10f * Time.deltaTime);

            Quaternion rot = Quaternion.LookRotation(toLanding.normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, rot,
                ApproachTurnRate * Time.deltaTime);
            transform.position = Vector3.MoveTowards(
                transform.position, _landingSpot, _currentSpeed * Time.deltaTime);
            _bot.SetTarget(_landingSpot, transform.rotation, _currentSpeed);
        }
    }

    // ── 와이어 체결 후 감속 ──────────────────────────────────────────────────

    void UpdateBeingArrested()
    {
        _arrestSpeed = Mathf.MoveTowards(_arrestSpeed, 0f, Cfg.arrestDecelRate * Time.deltaTime);

        // XZ: 슬롯 방향으로 MoveTowards — 오버슈트 없이 정확히 슬롯에 정지
        Vector3 flatSlot = new Vector3(_landingSlot.x, transform.position.y, _landingSlot.z);
        Vector3 pos = Vector3.MoveTowards(transform.position, flatSlot, _arrestSpeed * Time.deltaTime);

        // Y: 갑판 높이로 수렴 (피벗 오프셋 이미 _arrestDeckY에 포함)
        pos.y = Mathf.MoveTowards(transform.position.y, _arrestDeckY, Cfg.arrestYSnapRate * Time.deltaTime);
        transform.position = pos;

        // 피치/롤 수평 복귀
        float yaw = transform.eulerAngles.y;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.Euler(0f, yaw, 0f), 5f * Time.deltaTime);

        _bot.SetTarget(transform.position, transform.rotation, _arrestSpeed);

        // 정지 판정: 속도 임계 이하 또는 XZ 기준 슬롯 도착
        float xzDist = Mathf.Sqrt(
            (pos.x - _landingSlot.x) * (pos.x - _landingSlot.x) +
            (pos.z - _landingSlot.z) * (pos.z - _landingSlot.z));
        bool reached = _arrestSpeed < Cfg.arrestStopSpeed || xzDist < 0.05f;
        if (reached)
        {
            // XZ를 슬롯에 정확히 스냅, Y는 갑판 높이
            pos = new Vector3(_landingSlot.x, _arrestDeckY, _landingSlot.z);
            transform.position = pos;

            if (_carrier == null)
                _carrier = _cachedCarrier?.transform
                        ?? Object.FindFirstObjectByType<CarrierController>()?.transform;
            if (_carrier != null)
                transform.SetParent(_carrier, worldPositionStays: true);

            _bot.SetTarget(pos, transform.rotation, 0f);
            _phase = Phase.Landed;
            Debug.Log($"[EscortAI] {name} 착함 완료  슬롯={_landingSlot}");
        }
    }

    // ── 어프로치 셋업 (내부) ─────────────────────────────────────────────────

    void SetupApproach()
    {
        bool isLeft = IsLeftSide;

        if (_hasApproachInfo && _playerApproachDir.sqrMagnitude > 0.01f)
        {
            // 플레이어가 접근한 방향을 역산해 어프로치 웨이포인트 설정
            Vector3 backDir   = -_playerApproachDir.normalized;
            Vector3 slotBase  = _landingSlot;
            Vector3 wpBase    = slotBase + backDir * Cfg.approachDistance;
            _approachWaypoint = new Vector3(wpBase.x, slotBase.y + Cfg.approachAltitude, wpBase.z);

            // 슬롯 위치가 착함 목표 — 약간 높게 접근한 뒤 catchDist에서 BeingArrested 진입
            _landingSpot   = _landingSlot;
            _landingSpot.y = _landingSlot.y + 0.5f;
        }
        else
        {
            // 캐리어 기준 기본 어프로치 (폴백)
            if (_carrier == null) return;
            Vector3 approachBase = _carrier.position - _carrier.forward * Cfg.approachDistance;
            _approachWaypoint    = new Vector3(approachBase.x, _carrier.position.y + Cfg.approachAltitude, approachBase.z);

            // 슬롯 X 부호 기반 갑판 측면 오프셋 — V자 편대 N기 확장에도 대응
            float deckX = Mathf.Clamp(_formationOffset.x * 0.4f, -22f, 22f);
            Vector3 landLocal = new Vector3(deckX, 5.5f, -55f);
            _landingSpot = _carrier.TransformPoint(landLocal);
        }

        _waypointReached = false;
        _currentSpeed    = Cfg.approachSpeed;
    }
}

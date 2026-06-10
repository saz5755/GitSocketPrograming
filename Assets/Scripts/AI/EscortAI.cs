using System.Collections.Generic;
using UnityEngine;

// 에스코트 편대 AI — 상태 머신 기반.
// OnDeck → Launching → Escorting ↔ FreeFlightZone → LandingApproach → BeingArrested / Landed
public class EscortAI : MonoBehaviour
{
    public enum EscortSide { Left, Right }

    enum Phase
    {
        OnDeck, Launching, Escorting,
        FreeFlightZone,     // 항모 존 내 자유비행 (V자 추종 해제)
        FreeFlight,         // 타이머 기반 자유비행 (폴백 — 존 외부에서 착함 지시 시)
        LandingApproach,    // 파이널 어프로치 (웨이포인트 → 갑판)
        BeingArrested,      // 어레스팅 와이어 체결 후 감속 중
        Landed
    }

    static readonly Vector3 LeftOffset  = new Vector3(-32f, -5f, -40f);
    static readonly Vector3 RightOffset = new Vector3( 32f, -5f, -40f);

    // 어레스팅 와이어 시스템이 접근 중인 에스코트를 탐색할 수 있도록 전역 목록 노출
    public static readonly List<EscortAI> AllEscorts = new();

    [SerializeField] EscortSide _side = EscortSide.Left;

    [Header("편대비행 보간")]
    [SerializeField] float _lerpNear = 4f;
    [SerializeField] float _lerpFar  = 18f;
    [SerializeField] float _snapDist = 40f;
    [SerializeField] float _rotLerp  = 5f;

    [Header("발진 (캐터펄트 직진 후 편대 전환)")]
    [SerializeField] float _launchDuration = 6f;
    [SerializeField] float _launchSpeed    = 55f;

    [Header("착함")]
    [Tooltip("Escorting 상태에서 BeginLanding 호출 시 자유비행 시간 (폴백용)")]
    [SerializeField] float _freeFlightDuration = 5f;
    [SerializeField] float _approachSpeed  = 60f;
    [SerializeField] float _approachDist   = 1500f;
    [SerializeField] float _approachAlt    = 300f;
    [SerializeField] float _finalSpeed     = 25f;

    Transform         _leader;
    AIBotController   _bot;
    Phase             _phase = Phase.OnDeck;
    CarrierController _cachedCarrier;
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

    // 어레스팅 와이어 체결 상태
    float _arrestDeckY;
    float _arrestSpeed;

    public EscortSide Side           => _side;
    public bool       IsLanded       => _phase == Phase.Landed || _phase == Phase.BeingArrested;
    // ArrestingWireSystem이 체결 시도 여부를 판단하는 프로퍼티
    public bool       IsInApproachRun => _phase == Phase.LandingApproach;

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
            _cachedCarrier = Object.FindObjectOfType<CarrierController>();
    }

    // ── 외부 API ──────────────────────────────────────────────────────────────

    public void Initialize(Transform leader, EscortSide side, bool spawnedOnDeck)
    {
        _leader = leader;
        _side   = side;
        _bot    = GetComponent<AIBotController>();
        _bot.PositionOverride = true;
        _bot.SetMaxSpeed(200f);   // 추격 시 2배속을 허용하기 위해 상한 개방
        _bot.SetTurnRate(150f);   // 기본 72°/s → 더 빠른 선회로 편대 슬롯 이탈 방지
        _speedJitter   = Random.Range(-3f, 3f);
        _phase         = spawnedOnDeck ? Phase.OnDeck : Phase.Escorting;
        _cachedCarrier = Object.FindObjectOfType<CarrierController>();
    }

    public void UpdateLeader(Transform newLeader) => _leader = newLeader;

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

        _launchTimer          = 0f;
        _bot.PositionOverride = false;
        _phase                = Phase.Launching;
    }

    // AIManager.BeginEscortLanding() → 플레이어 착함 직후 순차 호출
    // FreeFlightZone 상태면 즉시 어프로치, Escorting 상태면 5초 자유비행 후 어프로치
    public void BeginLanding(Transform carrier)
    {
        _carrier         = carrier;
        _hasApproachInfo = false;
        BeginLandingShared();
    }

    // AIManager.BeginEscortLandingFromWire() → 와이어 체결 후 플레이어 경로 기반 착함
    public void BeginLandingWithPath(Transform carrier, Vector3 approachDir, Vector3 wirePos)
    {
        _carrier           = carrier != null ? carrier : _cachedCarrier?.transform;
        _playerApproachDir = approachDir;
        _playerWirePos     = wirePos;
        _hasApproachInfo   = true;
        BeginLandingShared();
    }

    void BeginLandingShared()
    {
        if (_phase == Phase.FreeFlightZone || _phase == Phase.Escorting)
        {
            _freeFlightTimer      = 0f;
            _bot.PositionOverride = false;
            _phase                = Phase.FreeFlight;
        }
        // 이미 어프로치 이후면 무시
    }

    // AIManager.BeginEscortFreeFlightNow() 에서 호출 — 즉시 자유비행 전환 (Escorting/Launching 시)
    public void ForceFreeFlight()
    {
        if (_phase == Phase.Escorting || _phase == Phase.Launching)
        {
            _bot.PositionOverride = false;
            _leaderStopTimer      = 0f;
            _phase                = Phase.FreeFlightZone;
        }
    }

    // EscortZoneTrigger — 플레이어가 존에 진입할 때 호출 (Escorting/Launching → FreeFlightZone)
    public void OnLeaderEnteredZone()
    {
        if (_phase == Phase.Escorting || _phase == Phase.Launching)
        {
            _bot.PositionOverride = false;
            _leaderStopTimer      = 0f;
            _phase                = Phase.FreeFlightZone;
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

    // ArrestingWireSystem이 와이어 체결 시 호출
    public void OnWireCaught(float deckY)
    {
        if (_phase != Phase.LandingApproach) return;
        _arrestDeckY  = deckY;
        _arrestSpeed  = _currentSpeed;
        _bot.PositionOverride = true;
        _phase = Phase.BeingArrested;
        Debug.Log($"[EscortAI] {name} 어레스팅 와이어 체결! 속도={_arrestSpeed:F1}");
    }

    // ── Unity Update ─────────────────────────────────────────────────────────

    void Update()
    {
        switch (_phase)
        {
            case Phase.Launching:       UpdateLaunching();       break;
            case Phase.Escorting:       UpdateEscorting();       break;
            case Phase.FreeFlightZone:  UpdateFreeFlightZone();  break;
            case Phase.FreeFlight:      UpdateFreeFlight();      break;
            case Phase.LandingApproach: UpdateLandingApproach(); break;
            case Phase.BeingArrested:   UpdateBeingArrested();   break;
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
        _bot.SetTarget(farAhead, launchRot, _launchSpeed);

        if (_launchTimer >= _launchDuration)
        {
            // PositionOverride는 발진 시작 시 이미 false — 유지
            _phase = Phase.Escorting;
        }
    }

    // ── V자 편대비행 ─────────────────────────────────────────────────────────

    void UpdateEscorting()
    {
        if (_leader == null || _bot == null) return;

        Vector3 localOffset = _side == EscortSide.Left ? LeftOffset : RightOffset;
        Vector3 targetPos   = _leader.TransformPoint(localOffset);
        Vector3 toTarget    = targetPos - transform.position;
        float   dist        = toTarget.magnitude;

        var   leaderPC    = _leader.GetComponent<PlayerController>();
        float leaderSpeed = leaderPC != null ? leaderPC.CurrentSpeed : 62f;
        float baseSpeed   = Mathf.Max(leaderSpeed, 40f);

        // ── 속도: 종방향(Z) 오차 기반 제어 ──────────────────────────────────
        // 단순 거리 기반 2배속은 앞지름 → 오버슈트를 유발.
        // 플레이어 로컬 Z 오차로 "앞서 나감/뒤처짐" 을 구분해 부드럽게 조정.
        Vector3 selfLocal = _leader.InverseTransformPoint(transform.position);
        float   zError    = selfLocal.z - localOffset.z;   // +: 슬롯보다 앞, -: 뒤

        float targetSpeed;
        if (zError > 5f)
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
        float      bankOffset = _side == EscortSide.Left ? 12f : -12f;
        Quaternion leaderRot  = _leader.rotation * Quaternion.Euler(0f, 0f, bankOffset);

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
                Quaternion lookRot = Quaternion.LookRotation(toTarget.normalized, _leader.up);
                float blend = Mathf.Clamp01(1f - dist / _snapDist);
                rot = Quaternion.Slerp(lookRot, leaderRot, blend);
            }
        }

        _bot.PositionOverride = false;
        _bot.SetTarget(targetPos, rot, targetSpeed);
    }

    // ── 항모 존 내 자유비행 (외부 존 ~ 내측 회피 존 사이 순회) ─────────────────

    void UpdateFreeFlightZone()
    {
        // ── 존 중심·반경 취득 ─────────────────────────────────────────────────
        Vector3 zoneCenter = EscortZoneTrigger.Instance != null
            ? EscortZoneTrigger.Instance.transform.position
            : (_cachedCarrier != null ? _cachedCarrier.transform.position : transform.position);

        float outerR = EscortZoneTrigger.Instance != null
            ? EscortZoneTrigger.Instance.Radius
            : (_cachedCarrier != null ? _cachedCarrier.EscortZoneRadius : 800f);
        float innerR = EscortInnerZoneTrigger.Instance != null
            ? EscortInnerZoneTrigger.Instance.Radius
            : (_cachedCarrier != null ? _cachedCarrier.InnerAvoidanceRadius : 350f);

        // XZ 거리만 사용 (고도 무관)
        Vector3 toCenter = zoneCenter - transform.position;
        toCenter.y = 0f;
        float distXZ = toCenter.magnitude;

        // ── 경계 회피 조향 ────────────────────────────────────────────────────
        const float outerBuffer = 160f;   // 외부 경계 도달 전부터 조향 시작
        const float innerBuffer = 110f;   // 내부 경계 접근 시 조향 시작

        Vector3 steerDir = transform.forward;
        steerDir.y = 0f;
        if (steerDir.sqrMagnitude < 0.01f) steerDir = transform.forward;
        steerDir.Normalize();

        if (distXZ > outerR - outerBuffer)
        {
            // 외부 경계 접근: 존 중심 방향으로 회전
            float t = Mathf.Clamp01((distXZ - (outerR - outerBuffer)) / outerBuffer);
            steerDir = Vector3.Slerp(steerDir, toCenter.normalized, t * 1.8f).normalized;
        }
        else if (distXZ < innerR + innerBuffer)
        {
            // 내부 회피 존 접근: 바깥 방향(접선 + 이탈)으로 회전
            float t        = Mathf.Clamp01(((innerR + innerBuffer) - distXZ) / innerBuffer);
            Vector3 outward = distXZ > 0.01f ? -toCenter.normalized : transform.right;
            // 현재 기수에서 이탈 방향 성분 블렌드 → 자연스러운 선회
            steerDir = Vector3.Slerp(steerDir, outward, t * 1.6f).normalized;
        }

        Quaternion targetRot = Quaternion.LookRotation(steerDir, Vector3.up);
        Vector3    targetPos = transform.position + steerDir * 1000f;

        _bot.SetTarget(targetPos, targetRot, _approachSpeed);

        // ── 리더 상태 감지 ────────────────────────────────────────────────────
        if (_leader == null)
        {
            _leaderStopTimer += Time.deltaTime;
            if (_leaderStopTimer >= 2f)
                AIManager.Instance?.BeginEscortLanding(_cachedCarrier?.transform);
            return;
        }

        // 존 진입/이탈은 EscortZoneTrigger.Update()의 콜백(OnLeaderEnteredZone/OnLeaderExitedZone)으로 처리

        var leaderPC = _leader.GetComponent<PlayerController>();
        // 리더 속도 0 이거나 비행 중 아닌 경우(하차 포함) → 일정 시간 후 착함
        bool leaderFlying = leaderPC != null && leaderPC.IsFlying && leaderPC.CurrentSpeed > 1f;
        if (!leaderFlying)
        {
            _leaderStopTimer += Time.deltaTime;
            if (_leaderStopTimer >= 2f)
                AIManager.Instance?.BeginEscortLanding(_cachedCarrier?.transform);
        }
        else
        {
            _leaderStopTimer = 0f;
        }
    }

    // ── 타이머 자유비행 (Escorting에서 BeginLanding 시 폴백) ─────────────────

    void UpdateFreeFlight()
    {
        _freeFlightTimer += Time.deltaTime;
        _bot.SetTarget(transform.position + transform.forward * 3000f, transform.rotation, _approachSpeed);

        if (_freeFlightTimer >= _freeFlightDuration)
        {
            SetupApproach();
            _bot.PositionOverride = true;
            _phase = Phase.LandingApproach;
        }
    }

    // ── 파이널 어프로치 & 착함 ───────────────────────────────────────────────

    void UpdateLandingApproach()
    {
        if (_carrier == null) { _phase = Phase.Escorting; return; }

        if (!_waypointReached)
        {
            float dist = Vector3.Distance(transform.position, _approachWaypoint);
            if (dist < 80f) { _waypointReached = true; return; }

            Vector3 dir = (_approachWaypoint - transform.position).normalized;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir), 2f * Time.deltaTime);
            transform.position = Vector3.MoveTowards(
                transform.position, _approachWaypoint, _approachSpeed * Time.deltaTime);
            _bot.SetTarget(_approachWaypoint, transform.rotation, _approachSpeed);
        }
        else
        {
            Vector3 toLanding = _landingSpot - transform.position;
            float   dist      = toLanding.magnitude;

            if (dist < 5f)
            {
                // 와이어에 안 잡힌 경우 — 직접 착함
                transform.SetPositionAndRotation(_landingSpot, _carrier.rotation);
                _bot.SetTarget(_landingSpot, _carrier.rotation, 0f);
                _phase = Phase.Landed;
                Debug.Log($"[EscortAI] {name} 직접 착함");
                return;
            }

            float targetSpeed = Mathf.Lerp(_finalSpeed, _approachSpeed,
                                            Mathf.Clamp01(dist / 500f));
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, 10f * Time.deltaTime);

            Quaternion rot = Quaternion.LookRotation(toLanding.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, 2f * Time.deltaTime);
            transform.position = Vector3.MoveTowards(
                transform.position, _landingSpot, _currentSpeed * Time.deltaTime);
            _bot.SetTarget(_landingSpot, transform.rotation, _currentSpeed);
        }
    }

    // ── 와이어 체결 후 감속 ──────────────────────────────────────────────────

    void UpdateBeingArrested()
    {
        _arrestSpeed = Mathf.MoveTowards(_arrestSpeed, 0f, 80f * Time.deltaTime);

        // 앞으로 미끄러지면서 감속
        transform.position += transform.forward * _arrestSpeed * Time.deltaTime;

        // 갑판 Y에 스냅
        var pos = transform.position;
        pos.y = Mathf.MoveTowards(pos.y, _arrestDeckY, 20f * Time.deltaTime);
        transform.position = pos;

        // 피치/롤 수평 복귀
        float yaw = transform.eulerAngles.y;
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.Euler(0f, yaw, 0f), 5f * Time.deltaTime);

        _bot.SetTarget(transform.position, transform.rotation, _arrestSpeed);

        if (_arrestSpeed < 0.2f)
        {
            pos = transform.position;
            pos.y = _arrestDeckY;
            transform.position = pos;
            _bot.SetTarget(pos, transform.rotation, 0f);
            _phase = Phase.Landed;
            Debug.Log($"[EscortAI] {name} 어레스팅 와이어 착함 완료");
        }
    }

    // ── 어프로치 셋업 (내부) ─────────────────────────────────────────────────

    void SetupApproach()
    {
        if (_hasApproachInfo && _playerApproachDir.sqrMagnitude > 0.01f)
        {
            // 플레이어가 접근한 방향을 역산해 어프로치 웨이포인트 설정
            Vector3 backDir   = -_playerApproachDir.normalized;
            Vector3 wpBase    = _playerWirePos + backDir * _approachDist;
            _approachWaypoint = new Vector3(wpBase.x, _playerWirePos.y + _approachAlt, wpBase.z);

            // 좌/우 에스코트가 겹치지 않도록 진행 방향 기준 측면 오프셋
            Vector3 sideDir = Vector3.Cross(_playerApproachDir.normalized, Vector3.up).normalized;
            float   sideOff = _side == EscortSide.Left ? -4f : 4f;
            _landingSpot    = _playerWirePos + sideDir * sideOff;
            _landingSpot.y  = _playerWirePos.y + 1f;
        }
        else
        {
            // 캐리어 기준 기본 어프로치 (폴백)
            if (_carrier == null) return;
            Vector3 approachBase = _carrier.position - _carrier.forward * _approachDist;
            _approachWaypoint    = new Vector3(approachBase.x, _carrier.position.y + _approachAlt, approachBase.z);

            Vector3 landLocal = _side == EscortSide.Left
                ? new Vector3(-22f, 5.5f, -55f)
                : new Vector3(-12f, 5.5f, -68f);
            _landingSpot = _carrier.TransformPoint(landLocal);
        }

        _waypointReached = false;
        _currentSpeed    = _approachSpeed;
    }
}

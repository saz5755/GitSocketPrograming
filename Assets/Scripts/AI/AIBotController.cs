using System.Collections.Generic;
using UnityEngine;

// 호스트가 AI 전투기를 직접 제어하는 컴포넌트.
// EscortAI 또는 EnemyAI가 SetTarget()으로 목표를 설정하면
// 부드럽게 이동·회전하며 UDP로 위치를 브로드캐스트한다.
// 원격 클라이언트는 PlayerController.RemoteUpdate()로 보간한다.
[RequireComponent(typeof(PlayerController))]
public class AIBotController : MonoBehaviour
{
    public static readonly List<AIBotController> All = new();

    public string Nickname        { get; private set; }
    public float  Speed          { get; private set; }
    // true 이면 position 이동을 외부(EscortAI 등)에서 직접 제어
    public bool   PositionOverride { get; set; } = false;

    [Header("동작 튜닝 데이터")]
    [Tooltip("AIBotConfigSO 에셋. Project 우클릭 → Create → KF21 → AI → Bot Config")]
    [SerializeField] AIBotConfigSO _config;

    // SO 미할당 시 한 번만 만들어지는 폴백 인스턴스 — 모든 봇이 공유
    static AIBotConfigSO s_fallbackConfig;
    AIBotConfigSO Cfg
    {
        get
        {
            if (_config != null) return _config;
            if (s_fallbackConfig == null)
            {
                s_fallbackConfig = ScriptableObject.CreateInstance<AIBotConfigSO>();
                if (Application.isPlaying)
                    Debug.LogWarning(
                        $"[AIBotController] '{name}' has no AIBotConfigSO assigned — using built-in defaults. " +
                        "Create one via Project window → Right Click → Create → KF21 → AI → Bot Config."
                    );
            }
            return s_fallbackConfig;
        }
    }

    // 런타임 인스턴스 값 (Initialize 시 SO에서 복사 — SetMaxSpeed/SetTurnRate로 덮어쓸 수 있음)
    float _maxSpeed;
    float _accel;
    float _decel;
    float _turnRate;
    float _netUpdateInterval;
    bool  _skipBroadcastWhenStationary;
    float _stationarySpeedThreshold;

    public void SetTurnRate(float deg) => _turnRate = deg;

    Vector3    _targetPos;
    Quaternion _targetRot;
    float      _targetSpeed;
    bool       _hasTarget;

    float _netTimer;
    Vector3    _lastBroadcastPos;
    Quaternion _lastBroadcastRot;
    bool       _hasLastBroadcast;

    static readonly int s_hashMove = Animator.StringToHash("Move");

    PlayerController _pc;
    Animator         _anim;

    void Awake()
    {
        All.Add(this);

        // AddComponent 직후 SetConfig 호출 전에는 _config가 null 일 수 있으므로
        // Cfg 프로퍼티(경고 발생) 대신 조용한 폴백으로 초기화. SetConfig가 즉시 재적용함.
        var c = _config ?? (s_fallbackConfig ??= ScriptableObject.CreateInstance<AIBotConfigSO>());
        _maxSpeed                    = c.maxSpeed;
        _accel                       = c.accel;
        _decel                       = c.decel;
        _turnRate                    = c.turnRate;
        _netUpdateInterval           = c.netUpdateInterval;
        _skipBroadcastWhenStationary = c.skipBroadcastWhenStationary;
        _stationarySpeedThreshold    = c.stationarySpeedThreshold;

        _pc = GetComponent<PlayerController>();
        _pc.isLocalPlayer = false;
        _pc.IsFlying      = true;
        _pc.enabled       = false;   // AIBotController가 Transform을 직접 제어

        _anim = GetComponentInChildren<Animator>();

        // 플레이어 전용 컴포넌트가 Player 프리팹 기반일 경우 비활성화
        DisableIfPresent<FlightHUD>();
        DisableIfPresent<TargetingSystem>();
        DisableIfPresent<MissileLauncher>();
        DisableIfPresent<GunSystem>();
        DisableIfPresent<GroundController>();
        DisableIfPresent<AircraftBoardingTrigger>();
    }

    void OnDestroy() => All.Remove(this);

    void DisableIfPresent<T>() where T : MonoBehaviour
    {
        var c = GetComponent<T>();
        if (c != null) c.enabled = false;
    }

    public void SetMaxSpeed(float v) => _maxSpeed = Mathf.Max(0f, v);

    // AIManager.BuildAIObject가 호출 — Awake 직후 SO 재할당 + 인스턴스 변수 재동기화
    public void SetConfig(AIBotConfigSO config)
    {
        if (config == null) return;
        _config                      = config;
        _maxSpeed                    = config.maxSpeed;
        _accel                       = config.accel;
        _decel                       = config.decel;
        _turnRate                    = config.turnRate;
        _netUpdateInterval           = config.netUpdateInterval;
        _skipBroadcastWhenStationary = config.skipBroadcastWhenStationary;
        _stationarySpeedThreshold    = config.stationarySpeedThreshold;
    }

    public void SetNickname(string nick)
    {
        Nickname     = nick;
        _pc.nickname = nick;
        gameObject.name = nick;
    }

    public void SetTarget(Vector3 pos, Quaternion rot, float speed)
    {
        _targetPos   = pos;
        _targetRot   = rot;
        _targetSpeed = Mathf.Clamp(speed, 0f, _maxSpeed);
        _hasTarget   = true;
    }

    void Update()
    {
        if (!_hasTarget) return;
        float dt = Time.deltaTime;

        float rate = Speed < _targetSpeed ? _accel : _decel;
        Speed = Mathf.MoveTowards(Speed, _targetSpeed, rate * dt);

        transform.rotation = Quaternion.RotateTowards(transform.rotation, _targetRot, _turnRate * dt);
        if (!PositionOverride)
            transform.position += transform.forward * Speed * dt;

        _anim?.SetBool(s_hashMove, Speed > 0.5f);

        _netTimer += dt;
        if (_netTimer >= _netUpdateInterval)
        {
            _netTimer -= _netUpdateInterval;
            BroadcastMoveIfChanged();
        }
    }

    // 정지 상태에서 직전과 동일한 pose면 전송 생략 — 대역폭/CPU 절약.
    // 위치/회전이 미세하게라도 바뀌면 다시 전송 시작.
    void BroadcastMoveIfChanged()
    {
        if (_skipBroadcastWhenStationary && Speed < _stationarySpeedThreshold && _hasLastBroadcast)
        {
            if ((transform.position - _lastBroadcastPos).sqrMagnitude < 0.001f &&
                Quaternion.Angle(transform.rotation, _lastBroadcastRot) < 0.1f)
                return;
        }

        NetworkManager.Instance?.socketClient?.SendAIMove(
            Nickname, transform.position, transform.eulerAngles, Speed);

        _lastBroadcastPos = transform.position;
        _lastBroadcastRot = transform.rotation;
        _hasLastBroadcast = true;
    }
}

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

    [SerializeField] float maxSpeed = 56f;   // 플레이어 최대 속도(80)의 70 %
    [SerializeField] float accel    = 22f;
    [SerializeField] float decel    = 30f;
    [SerializeField] float turnRate = 72f;  // deg/s — EnemyAI는 Initialize에서 낮게 설정

    public void SetTurnRate(float deg) => turnRate = deg;

    Vector3    _targetPos;
    Quaternion _targetRot;
    float      _targetSpeed;
    bool       _hasTarget;

    float       _netTimer;
    const float NetInterval = 0.05f;

    PlayerController _pc;
    Animator         _anim;

    void Awake()
    {
        All.Add(this);

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

    public void SetMaxSpeed(float v) => maxSpeed = Mathf.Max(0f, v);

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
        _targetSpeed = Mathf.Clamp(speed, 0f, maxSpeed);
        _hasTarget   = true;
    }

    void Update()
    {
        if (!_hasTarget) return;
        float dt = Time.deltaTime;

        float rate = Speed < _targetSpeed ? accel : decel;
        Speed = Mathf.MoveTowards(Speed, _targetSpeed, rate * dt);

        transform.rotation = Quaternion.RotateTowards(transform.rotation, _targetRot, turnRate * dt);
        if (!PositionOverride)
            transform.position += transform.forward * Speed * dt;

        _anim?.SetBool("Move", Speed > 0.5f);

        _netTimer += dt;
        if (_netTimer >= NetInterval)
        {
            _netTimer -= NetInterval;
            BroadcastMove();
        }
    }

    void BroadcastMove()
    {
        NetworkManager.Instance?.socketClient?.SendAIMove(
            Nickname, transform.position, transform.eulerAngles, Speed);
    }
}

using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public static readonly List<PlayerController> All = new List<PlayerController>(16);

    public string nickname;
    public bool   isLocalPlayer;
    public bool   IsFlying { get; set; }
    public int    lastReceivedTick;

    List<Snapshot> snapshots = new();

    static readonly int s_hashMove = Animator.StringToHash("Move");

    Animator anim;
    bool     _hasMoveParam;
    float    moveSendTimer;
    int      localTick;

    [Header("동작 튜닝 데이터")]
    [Tooltip("AircraftStatsSO 에셋. Project 우클릭 → Create → KF21 → Aircraft → Aircraft Stats")]
    [SerializeField] AircraftStatsSO _stats;

    // SO 미할당 시 폴백 인스턴스 — 한 번만 생성
    static AircraftStatsSO s_fallbackStats;
    AircraftStatsSO Cfg
    {
        get
        {
            if (_stats != null) return _stats;
            if (s_fallbackStats == null)
            {
                s_fallbackStats = ScriptableObject.CreateInstance<AircraftStatsSO>();
                if (Application.isPlaying)
                    Debug.LogWarning(
                        $"[PlayerController] '{name}' has no AircraftStatsSO assigned — using built-in defaults. " +
                        "Create one via Project window → Right Click → Create → KF21 → Aircraft → Aircraft Stats."
                    );
            }
            return s_fallbackStats;
        }
    }

    float currentSpeed;
    public float CurrentSpeed => currentSpeed;

    // ── 어레스팅 와이어 감속 ─────────────────────────────────────────────────
    bool _arrested;

    public void BeginArrest() => _arrested = true;
    public void EndArrest()   => _arrested = false;

    // ── 카타펄트 발진 ─────────────────────────────────────────────────────────
    bool  _catapulting;
    float _catapultTarget;
    float _catapultAccel;
    float _catapultHoldTimer;

    public void StartCatapult(float targetSpeed)
    {
        currentSpeed       = 0f;
        _catapultTarget    = targetSpeed;
        _catapultAccel     = Cfg.catapultAccel;
        _catapulting       = true;
        _catapultHoldTimer = 0f;
    }

    const int MaxHistory = 64;
    readonly Queue<TickRecord>  _inputHistory = new();
    readonly RaycastHit[]       _groundHits   = new RaycastHit[8];

    void Awake()
    {
        anim = GetComponent<Animator>();
        if (anim != null)
        {
            foreach (var p in anim.parameters)
                if (p.name == "Move") { _hasMoveParam = true; break; }
        }
        All.Add(this);
    }

    void OnEnable()
    {
        if (!isLocalPlayer) return;
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnMoveAck += HandleMoveAck;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    void OnDisable()
    {
        if (!isLocalPlayer) return;
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnMoveAck -= HandleMoveAck;
    }

    void OnDestroy()
    {
        All.Remove(this);
        if (!isLocalPlayer) return;
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnMoveAck -= HandleMoveAck;
    }

    void Update()
    {
        if (isLocalPlayer) LocalUpdate();
        else               RemoteUpdate();
    }

    // ── 로컬 플레이어 ──────────────────────────────────────────────────────────
    void LocalUpdate()
    {
        float dt = Time.deltaTime;

        // 어레스팅 와이어 감속 중 — 입력 차단, 강제 감속
        if (_arrested)
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, Cfg.arrestDecel * dt);
            transform.position += transform.forward * currentSpeed * dt;
            ConstrainToSurface();

            moveSendTimer += dt;
            if (moveSendTimer >= Cfg.sendInterval)
            {
                moveSendTimer -= Cfg.sendInterval;
                localTick++;
                Vector3 arrestEuler = transform.eulerAngles;
                NetworkManager.Instance.socketClient.SendMove(
                    transform.position.x, transform.position.y, transform.position.z,
                    arrestEuler.x, arrestEuler.y, arrestEuler.z,
                    currentSpeed > 0.5f,
                    GameModeManager.Instance != null && GameModeManager.Instance.IsFlying,
                    localTick);
            }
            return;
        }

        // 카타펄트 발진 — 속도·위치는 자동, 회전(pitch/yaw/roll)은 즉시 허용
        if (_catapulting)
        {
            if (currentSpeed < _catapultTarget)
            {
                currentSpeed = Mathf.MoveTowards(currentSpeed, _catapultTarget, _catapultAccel * dt);
            }
            else
            {
                currentSpeed = _catapultTarget;
                _catapultHoldTimer += dt;
                if (_catapultHoldTimer >= Cfg.catapultHoldSec)
                    _catapulting = false;
            }

            bool freeLookC = Input.GetMouseButton(1);
            float pitchC   = freeLookC ? 0f : -Input.GetAxis("Mouse Y") * Cfg.pitchSensitivity * dt;
            float yawC     = freeLookC ? 0f :  Input.GetAxis("Mouse X") * Cfg.yawSensitivity   * dt;
            float rollC    = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.Q)) rollC =  Cfg.rollSpeed * dt;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.E)) rollC = -Cfg.rollSpeed * dt;
            transform.Rotate(pitchC, yawC, rollC, Space.Self);

            transform.position += transform.forward * currentSpeed * dt;
            if (anim != null) anim.SetBool(s_hashMove, true);

            moveSendTimer += dt;
            if (moveSendTimer >= Cfg.sendInterval)
            {
                moveSendTimer -= Cfg.sendInterval;
                localTick++;
                Vector3 catEuler = transform.eulerAngles;
                NetworkManager.Instance.socketClient.SendMove(
                    transform.position.x, transform.position.y, transform.position.z,
                    catEuler.x, catEuler.y, catEuler.z,
                    true,
                    GameModeManager.Instance != null && GameModeManager.Instance.IsFlying,
                    localTick);
            }
            return;
        }

        float throttle = 0f;
        if (Input.GetKey(KeyCode.W))      throttle =  1f;
        else if (Input.GetKey(KeyCode.S)) throttle = -1f;

        if (throttle > 0f)
            currentSpeed = Mathf.MoveTowards(currentSpeed, Cfg.maxSpeed, Cfg.acceleration * dt);
        else if (throttle < 0f)
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, Cfg.acceleration * dt);
        else
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, Cfg.drag * dt);

        bool freeLook = Input.GetMouseButton(1);
        float pitch = freeLook ? 0f : -Input.GetAxis("Mouse Y") * Cfg.pitchSensitivity * dt;
        float yaw   = freeLook ? 0f :  Input.GetAxis("Mouse X") * Cfg.yawSensitivity   * dt;

        float roll = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.Q)) roll =  Cfg.rollSpeed * dt;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.E)) roll = -Cfg.rollSpeed * dt;

        transform.Rotate(pitch, yaw, roll, Space.Self);
        transform.position += transform.forward * currentSpeed * dt;
        if (currentSpeed > 0.1f) ConstrainToSurface();

        if (anim != null) anim.SetBool(s_hashMove, currentSpeed > 0.5f);

        moveSendTimer += dt;
        if (moveSendTimer >= Cfg.sendInterval)
        {
            moveSendTimer -= Cfg.sendInterval;
            localTick++;

            if (_inputHistory.Count >= MaxHistory) _inputHistory.Dequeue();
            _inputHistory.Enqueue(new TickRecord
            {
                tick       = localTick,
                dt         = Cfg.sendInterval,
                throttle   = throttle,
                pitch      = pitch, yaw = yaw, roll = roll,
                velAfter   = transform.forward * currentSpeed,
                posAfter   = transform.position
            });

            Vector3 euler = transform.eulerAngles;
            NetworkManager.Instance.socketClient.SendMove(
                transform.position.x, transform.position.y, transform.position.z,
                euler.x, euler.y, euler.z,
                currentSpeed > 0.5f,
                GameModeManager.Instance != null && GameModeManager.Instance.IsFlying,
                localTick);
        }
    }

    // ── 지면/갑판 관통 방지 ────────────────────────────────────────────────────────
    void ConstrainToSurface()
    {
        int count = Physics.RaycastNonAlloc(
            transform.position + Vector3.up * 3f, Vector3.down,
            _groundHits, 7f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        float bestY = float.MinValue;
        bool  found = false;
        for (int i = 0; i < count; i++)
        {
            if (_groundHits[i].transform.IsChildOf(transform)) continue;
            if (_groundHits[i].point.y > bestY) { bestY = _groundHits[i].point.y; found = true; }
        }
        if (!found) return;

        float floor = bestY - 0.097f;
        if (transform.position.y < floor)
        {
            var p = transform.position; p.y = floor; transform.position = p;
        }
    }

    // ── Reconciliation ─────────────────────────────────────────────────────────
    void HandleMoveAck(MoveAckPacket ack)
    {
        var serverPos = new Vector3(ack.posX, ack.posY, ack.posZ);

        float reconSpeed = currentSpeed;
        bool  found      = false;
        var   remaining  = new Queue<TickRecord>();

        while (_inputHistory.Count > 0)
        {
            var r = _inputHistory.Dequeue();
            if (r.tick == ack.tick)
            {
                float error = Vector3.Distance(r.posAfter, serverPos);
                if (error > Cfg.reconcileThreshold)
                {
                    reconSpeed = r.velAfter.magnitude;
                    found      = true;
                }
            }
            else if (r.tick > ack.tick)
            {
                remaining.Enqueue(r);
            }
        }

        if (!found)
        {
            while (remaining.Count > 0) _inputHistory.Enqueue(remaining.Dequeue());
            return;
        }

        var   pos = serverPos;
        var   rot = Quaternion.Euler(ack.rotX, ack.rotY, ack.rotZ);
        float spd = reconSpeed;

        foreach (var r in remaining)
        {
            if (r.throttle > 0.5f)
                spd = Mathf.MoveTowards(spd, Cfg.maxSpeed, Cfg.acceleration * r.dt);
            else if (r.throttle < -0.5f)
                spd = Mathf.MoveTowards(spd, 0f, Cfg.acceleration * r.dt);
            else
                spd = Mathf.MoveTowards(spd, 0f, Cfg.drag * r.dt);

            rot = rot * Quaternion.Euler(r.pitch, r.yaw, r.roll);
            pos += rot * Vector3.forward * spd * r.dt;

            _inputHistory.Enqueue(r);
        }

        transform.SetPositionAndRotation(pos, rot);
        currentSpeed = spd;
    }

    // ── 원격 플레이어: 스냅샷 보간 ────────────────────────────────────────────
    void RemoteUpdate()
    {
        if (snapshots.Count == 0) return;

        if (snapshots.Count == 1)
        {
            transform.position = snapshots[0].position;
            transform.rotation = snapshots[0].rotation;
            return;
        }

        float renderTime = Time.time - Cfg.interpolationDelay;

        while (snapshots.Count > 2 && snapshots[1].time <= renderTime)
            snapshots.RemoveAt(0);

        Snapshot from = snapshots[0];
        Snapshot to   = snapshots[1];

        float t = to.time > from.time
            ? Mathf.Clamp01(Mathf.InverseLerp(from.time, to.time, renderTime))
            : 1f;

        transform.position = Vector3.Lerp(from.position, to.position, t);
        transform.rotation = Quaternion.Slerp(from.rotation, to.rotation, t);

        if (anim != null && _hasMoveParam) anim.SetBool(s_hashMove, to.isMove);
    }

    // ── 스냅샷 ────────────────────────────────────────────────────────────────
    public void AddSnapshot(Vector3 pos, Quaternion rot, bool isMove)
    {
        snapshots.Add(new Snapshot
            { position = pos, rotation = rot, isMove = isMove, time = Time.time });
        if (snapshots.Count > 12) snapshots.RemoveAt(0);
    }

    public void ClearSnapshots() => snapshots.Clear();

    // GroundController가 단일 틱 시퀀스를 공유하도록 노출
    public int GetNextTick() => ++localTick;

    // 비행 상태 전환 시 즉시 한 번 전송 (비행 종료 시 isFlying=false 알림용)
    public void SendFlightState(bool flying)
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;
        Vector3 euler = transform.eulerAngles;
        sc.SendMove(
            transform.position.x, transform.position.y, transform.position.z,
            euler.x, euler.y, euler.z,
            currentSpeed > 0.5f, flying, localTick);
    }

    // 콕핏 탑승 상태 전환 — TCP로 전송해 서버 상태를 즉시·확실하게 갱신
    public void SendCockpitState(bool boarded)
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;
        sc.SendCockpitStateTCP(boarded, transform.position, transform.eulerAngles);
    }

    /// <summary>카타펄트 사출 등 외부에서 초기 속도를 설정.</summary>
    public void SetInitialSpeed(float speedMS) => currentSpeed = speedMS;
}

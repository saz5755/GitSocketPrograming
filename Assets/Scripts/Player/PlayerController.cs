using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public static readonly List<PlayerController> All = new List<PlayerController>(16);

    public string nickname;
    public bool   isLocalPlayer;
    public int    lastReceivedTick;

    List<Snapshot> snapshots = new();

    Animator anim;
    float    moveSendTimer;
    int      localTick;

    [Header("Throttle")]
    [SerializeField] float maxSpeed     = 80f;
    [SerializeField] float acceleration = 12f;
    [SerializeField] float drag         =  6f;

    [Header("Mouse Flight Control")]
    [SerializeField] float pitchSensitivity = 60f;
    [SerializeField] float yawSensitivity   = 60f;
    [SerializeField] float rollSpeed        = 85f;

    [Header("Network")]
    [SerializeField] float sendInterval = 0.05f;

    [Header("Interpolation")]
    [SerializeField] float interpolationDelay = 0.1f;

    [Header("Prediction & Reconciliation")]
    [SerializeField] float reconcileThreshold = 3f;

    float currentSpeed;
    public float CurrentSpeed => currentSpeed;

    // ── 어레스팅 와이어 감속 ─────────────────────────────────────────────────
    bool  _arrested;
    const float ArrestDecel = 50f; // m/s² — 약 1.5s 만에 240 kph → 0

    public void BeginArrest() => _arrested = true;
    public void EndArrest()   => _arrested = false;

    const int MaxHistory = 64;
    readonly Queue<TickRecord>  _inputHistory = new();
    readonly RaycastHit[]       _groundHits   = new RaycastHit[8];

    void Awake()
    {
        anim = GetComponent<Animator>();
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
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, ArrestDecel * dt);
            transform.position += transform.forward * currentSpeed * dt;
            ConstrainToSurface();
            return;
        }

        float throttle = 0f;
        if (Input.GetKey(KeyCode.W))      throttle =  1f;
        else if (Input.GetKey(KeyCode.S)) throttle = -1f;

        if (throttle > 0f)
            currentSpeed = Mathf.MoveTowards(currentSpeed, maxSpeed, acceleration * dt);
        else if (throttle < 0f)
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, acceleration * dt);
        else
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, drag * dt);

        bool freeLook = Input.GetMouseButton(1);
        float pitch = freeLook ? 0f : -Input.GetAxis("Mouse Y") * pitchSensitivity * dt;
        float yaw   = freeLook ? 0f :  Input.GetAxis("Mouse X") * yawSensitivity   * dt;

        float roll = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.Q)) roll =  rollSpeed * dt;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.E)) roll = -rollSpeed * dt;

        transform.Rotate(pitch, yaw, roll, Space.Self);
        transform.position += transform.forward * currentSpeed * dt;
        if (currentSpeed > 0.1f) ConstrainToSurface();

        if (anim != null) anim.SetBool("Move", currentSpeed > 0.5f);

        moveSendTimer += dt;
        if (moveSendTimer >= sendInterval)
        {
            moveSendTimer -= sendInterval;
            localTick++;

            if (_inputHistory.Count >= MaxHistory) _inputHistory.Dequeue();
            _inputHistory.Enqueue(new TickRecord
            {
                tick       = localTick,
                dt         = sendInterval,
                throttle   = throttle,
                pitch      = pitch, yaw = yaw, roll = roll,
                velAfter   = transform.forward * currentSpeed,
                posAfter   = transform.position
            });

            Vector3 euler = transform.eulerAngles;
            NetworkManager.Instance.socketClient.SendMove(
                transform.position.x, transform.position.y, transform.position.z,
                euler.x, euler.y, euler.z,
                currentSpeed > 0.5f, localTick);
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

        float floor = bestY + 0.3f;
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
                if (error > reconcileThreshold)
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
                spd = Mathf.MoveTowards(spd, maxSpeed, acceleration * r.dt);
            else if (r.throttle < -0.5f)
                spd = Mathf.MoveTowards(spd, 0f, acceleration * r.dt);
            else
                spd = Mathf.MoveTowards(spd, 0f, drag * r.dt);

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

        float renderTime = Time.time - interpolationDelay;

        while (snapshots.Count > 2 && snapshots[1].time <= renderTime)
            snapshots.RemoveAt(0);

        Snapshot from = snapshots[0];
        Snapshot to   = snapshots[1];

        float t = to.time > from.time
            ? Mathf.Clamp01(Mathf.InverseLerp(from.time, to.time, renderTime))
            : 1f;

        transform.position = Vector3.Lerp(from.position, to.position, t);
        transform.rotation = Quaternion.Slerp(from.rotation, to.rotation, t);

        if (anim != null) anim.SetBool("Move", to.isMove);
    }

    // ── 스냅샷 ────────────────────────────────────────────────────────────────
    public void AddSnapshot(Vector3 pos, Quaternion rot, bool isMove)
    {
        snapshots.Add(new Snapshot
            { position = pos, rotation = rot, isMove = isMove, time = Time.time });
        if (snapshots.Count > 12) snapshots.RemoveAt(0);
    }

    public void ClearSnapshots() => snapshots.Clear();

    /// <summary>카타펄트 사출 등 외부에서 초기 속도를 설정.</summary>
    public void SetInitialSpeed(float speedMS) => currentSpeed = speedMS;
}

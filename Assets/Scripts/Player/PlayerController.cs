using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public string nickname;
    public bool isLocalPlayer;
    public int lastReceivedTick;

    List<Snapshot> snapshots = new();

    Animator anim;
    float moveSendTimer;
    int localTick;

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

    float currentSpeed;
    public float CurrentSpeed => currentSpeed;

    void Awake()
    {
        anim = GetComponent<Animator>();
    }

    void Update()
    {
        if (isLocalPlayer) LocalUpdate();
        else               RemoteUpdate();
    }

    // ── 로컬 플레이어 ──────────────────────────────────────────────────────
    void LocalUpdate()
    {
        float dt = Time.deltaTime;

        // 스로틀: W 가속 / S 감속 / 미입력 시 자연 감속
        if (Input.GetKey(KeyCode.W))
            currentSpeed = Mathf.MoveTowards(currentSpeed, maxSpeed, acceleration * dt);
        else if (Input.GetKey(KeyCode.S))
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, acceleration * dt);
        else
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, drag * dt);

        // 우클릭 중이면 자유시점 모드 (비행 입력 차단)
        bool freeLook = Input.GetMouseButton(1);

        float pitch = 0f;
        float yaw   = 0f;

        if (!freeLook)
        {
            // 마우스로 피치/요 제어 (상용 비행 시뮬레이터 방식)
            pitch = -Input.GetAxis("Mouse Y") * pitchSensitivity * dt;
            yaw   =  Input.GetAxis("Mouse X") * yawSensitivity   * dt;
        }

        // 롤: A/Q = 왼쪽 뱅킹, D/E = 오른쪽 뱅킹
        float roll = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.Q)) roll =  rollSpeed * dt;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.E)) roll = -rollSpeed * dt;

        transform.Rotate(pitch, yaw, roll, Space.Self);
        transform.position += transform.forward * currentSpeed * dt;

        bool isMoving = currentSpeed > 0.5f;
        if (anim != null) anim.SetBool("Move", isMoving);

        moveSendTimer += dt;
        if (moveSendTimer >= sendInterval)
        {
            moveSendTimer = 0f;
            localTick++;
            Vector3 euler = transform.eulerAngles;
            NetworkManager.Instance.socketClient.SendMove(
                transform.position.x, transform.position.y, transform.position.z,
                euler.x, euler.y, euler.z,
                isMoving, localTick
            );
        }
    }

    // ── 원격 플레이어: 스냅샷 보간 ────────────────────────────────────────
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

        float t = (to.time > from.time)
            ? Mathf.Clamp01(Mathf.InverseLerp(from.time, to.time, renderTime))
            : 1f;

        transform.position = Vector3.Lerp(from.position, to.position, t);
        transform.rotation = Quaternion.Slerp(from.rotation, to.rotation, t);

        if (anim != null) anim.SetBool("Move", to.isMove);
    }

    // ── 스냅샷 ────────────────────────────────────────────────────────────
    public void AddSnapshot(Vector3 pos, Quaternion rot, bool isMove)
    {
        snapshots.Add(new Snapshot { position = pos, rotation = rot, isMove = isMove, time = Time.time });
        if (snapshots.Count > 12) snapshots.RemoveAt(0);
    }

    public void ClearSnapshots() => snapshots.Clear();
}

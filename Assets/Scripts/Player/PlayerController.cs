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
    [SerializeField] float maxSpeed     = 80f;   // kph 기준 체감, 내부는 m/s
    [SerializeField] float acceleration = 12f;   // m/s² 가속
    [SerializeField] float drag         =  6f;   // 스로틀 미입력 시 감속

    [Header("Attitude")]
    [SerializeField] float pitchSpeed = 55f;
    [SerializeField] float yawSpeed   = 40f;
    [SerializeField] float rollSpeed  = 85f;

    [Header("Network")]
    [SerializeField] float sendInterval = 0.05f; // 20 Hz

    [Header("Interpolation")]
    [SerializeField] float interpolationDelay = 0.1f;

    float currentSpeed;

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

        // 자세 제어: 위/아래 화살표=피치, A/D=요, Q/E=롤
        float pitch = 0f, yaw = 0f, roll = 0f;
        if (Input.GetKey(KeyCode.UpArrow))   pitch = -1f;
        if (Input.GetKey(KeyCode.DownArrow)) pitch =  1f;
        if (Input.GetKey(KeyCode.A))         yaw   = -1f;
        if (Input.GetKey(KeyCode.D))         yaw   =  1f;
        if (Input.GetKey(KeyCode.Q))         roll  =  1f;
        if (Input.GetKey(KeyCode.E))         roll  = -1f;

        transform.Rotate(
            pitch * pitchSpeed * dt,
            yaw   * yawSpeed   * dt,
            roll  * rollSpeed  * dt,
            Space.Self
        );

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

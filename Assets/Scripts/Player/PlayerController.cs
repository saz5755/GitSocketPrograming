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

    [Header("Flight Settings")]
    [SerializeField] float moveSpeed = 30f;
    [SerializeField] float pitchSpeed = 60f;
    [SerializeField] float yawSpeed = 45f;
    [SerializeField] float rollSpeed = 90f;
    [SerializeField] float sendInterval = 0.05f; // 20Hz

    [Header("Interpolation")]
    [SerializeField] float interpolationDelay = 0.1f;

    void Awake()
    {
        anim = GetComponent<Animator>();
    }

    void Update()
    {
        if (isLocalPlayer)
            LocalUpdate();
        else
            RemoteUpdate();
    }

    // ── 로컬 플레이어 ────────────────────────────────────────────────────
    void LocalUpdate()
    {
        float pitch = -Input.GetAxis("Vertical");
        float yaw   =  Input.GetAxis("Horizontal");
        float roll  = 0f;
        if (Input.GetKey(KeyCode.Q)) roll =  1f;
        if (Input.GetKey(KeyCode.E)) roll = -1f;

        transform.Rotate(
            pitch * pitchSpeed * Time.deltaTime,
            yaw   * yawSpeed   * Time.deltaTime,
            roll  * rollSpeed  * Time.deltaTime,
            Space.Self
        );

        transform.position += transform.forward * moveSpeed * Time.deltaTime;

        if (anim != null)
            anim.SetBool("Move", true);

        moveSendTimer += Time.deltaTime;
        if (moveSendTimer >= sendInterval)
        {
            moveSendTimer = 0f;
            localTick++;

            Vector3 euler = transform.eulerAngles;
            NetworkManager.Instance.socketClient.SendMove(
                transform.position.x, transform.position.y, transform.position.z,
                euler.x, euler.y, euler.z,
                true, localTick
            );
        }
    }

    // ── 원격 플레이어: 스냅샷 보간 ────────────────────────────────────────
    void RemoteUpdate()
    {
        if (snapshots.Count == 0)
            return;

        // 스냅샷 1개: 해당 위치/회전에 고정
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

        // from == to 이면 t = 0 → from 위치에 고정
        float t = (to.time > from.time)
            ? Mathf.Clamp01(Mathf.InverseLerp(from.time, to.time, renderTime))
            : 1f;

        transform.position = Vector3.Lerp(from.position, to.position, t);
        transform.rotation = Quaternion.Slerp(from.rotation, to.rotation, t);

        if (anim != null)
            anim.SetBool("Move", to.isMove);
    }

    // ── 스냅샷 추가 ───────────────────────────────────────────────────────
    public void AddSnapshot(Vector3 pos, Quaternion rot, bool isMove)
    {
        snapshots.Add(new Snapshot
        {
            position = pos,
            rotation = rot,
            isMove   = isMove,
            time     = Time.time
        });

        if (snapshots.Count > 12)
            snapshots.RemoveAt(0);
    }

    public void ClearSnapshots() => snapshots.Clear();
}

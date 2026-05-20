using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public string nickname;

    public bool isLocalPlayer;

    // 스냅샷으로 대체
    Vector3 targetPos;

    // SnapShot
    List<Snapshot> snapshots = new();
    public int lastReceivedTick;
    
    float targetRotY;
    bool targetIsMove;
    Animator anim;

    float sendTimer;
    
    [SerializeField]
    float moveSpeed = 5f;

    private void Awake()
    {
        anim =  GetComponent<Animator>();
    }

    void Update()
    {
        if (isLocalPlayer)
        {
            LocalUpdate();
        }
        else
        {
            RemoteUpdate();
        }
    }

    void LocalUpdate()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");
        float moveY = 0;

        if (Input.GetKey(KeyCode.E)) moveY = 1;
        if (Input.GetKey(KeyCode.Q)) moveY = -1;

        Vector3 dir = new Vector3(moveX, moveY, moveZ);

        bool isMove = dir != Vector3.zero;

        if (isMove)
            transform.rotation = Quaternion.LookRotation(dir.normalized);

        // 🔥 핵심: 즉시 이동 (client prediction)
        transform.position += dir * 5f * Time.deltaTime;

        // 서버 전송만
        NetworkManager.Instance.socketClient.SendMove(
            moveX, moveY, moveZ,
            transform.eulerAngles.y,
            isMove
        );

        Debug.Log($"[SEND] moveX={moveX}, moveY={moveY}, moveZ={moveZ}, pos={transform.position}");

        anim.SetBool("Move", isMove);
    }
    
    void RemoteUpdate()
    {
        if (snapshots.Count < 2)
        {
            return;
        }
        
        float renderTime = Time.time - 0.1f;

        // 안전하게 2개 유지
        while (snapshots.Count > 2 &&
               snapshots[1].time <= renderTime)
        {
            snapshots.RemoveAt(0);
        }

        // 다시 체크 (중요)
        if (snapshots.Count < 2)
            return;

        Snapshot from = snapshots[0];
        Snapshot to = snapshots[1];

        float t = Mathf.InverseLerp(
            from.time,
            to.time,
            renderTime
        );

        transform.position = Vector3.Lerp(
            from.position,
            to.position,
            t
        );

        transform.rotation = Quaternion.Lerp(
            Quaternion.Euler(0, from.rotY, 0),
            Quaternion.Euler(0, to.rotY, 0),
            t
        );
        
        anim.SetBool("Move", to.isMove);
        
    }

    public void SetTargetPosition(Vector3 pos, float rotY, bool isMove)
    {
        targetPos = pos;
        targetRotY = rotY;
        targetIsMove = isMove;
    }
    
    // Snapshot 
    public void AddSnapshot(Vector3 pos, float rotY, bool isMove)
    {
        snapshots.Add(new Snapshot
        {
            position = pos,
            rotY = rotY,
            isMove = isMove,
            time = Time.time
        });

        // 정렬 보장 (UDP 순서 꼬임 방지)
        snapshots.Sort((a, b) => a.time.CompareTo(b.time));

        if (snapshots.Count > 20)
            snapshots.RemoveAt(0);
    }
    
    public void ClearSnapshots()
    {
        snapshots.Clear();
    }
}
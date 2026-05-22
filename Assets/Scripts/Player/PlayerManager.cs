using System.Collections.Generic;
using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    public GameObject playerPrefab;

    readonly Dictionary<string, PlayerController> players = new();

    void Start()
    {
        SocketClient sc = NetworkManager.Instance?.socketClient;
        if (sc == null) { Debug.LogWarning("[PlayerManager] SocketClient not found"); return; }
        sc.OnSpawn          += HandleSpawn;
        sc.OnDespawn        += HandleDespawn;
        sc.OnMove           += HandleMove;
        sc.OnMissileDestroy += HandleMissileDestroy;
        sc.OnMissileWarn    += HandleMissileWarn;
    }

    void OnDestroy()
    {
        SocketClient sc = NetworkManager.Instance?.socketClient;
        if (sc != null)
        {
            sc.OnSpawn          -= HandleSpawn;
            sc.OnDespawn        -= HandleDespawn;
            sc.OnMove           -= HandleMove;
            sc.OnMissileDestroy -= HandleMissileDestroy;
            sc.OnMissileWarn    -= HandleMissileWarn;
        }
        ClearPlayers();
    }

    // ── 이벤트 핸들러 ─────────────────────────────────────────────────────
    void HandleSpawn(SpawnPacket p)
    {
        CreatePlayer(p.nickname,
            new Vector3(p.x, p.y, p.z),
            Quaternion.Euler(p.rotX, p.rotY, p.rotZ),
            p.isMove);
    }

    void HandleDespawn(string nickname) => RemovePlayer(nickname);

    void HandleMove(MoveBroadcastPacket p)
    {
        if (!players.TryGetValue(p.nickname, out var player)) return;
        player.AddSnapshot(
            new Vector3(p.posX, p.posY, p.posZ),
            Quaternion.Euler(p.rotX, p.rotY, p.rotZ),
            p.isMove);
    }

    void HandleMissileDestroy(MissileDestroyPacket p)
    {
        string myName = GameManager.Instance?.myNickname
                     ?? NetworkManager.Instance?.socketClient.myNickname
                     ?? "";
        // 발사자는 Detonate()에서 이미 로컬 이펙트 처리 → 서버 에코 무시
        if (p.shooterNickname == myName) return;

        var pos   = new Vector3(p.posX, p.posY, p.posZ);
        bool hitMe = !string.IsNullOrEmpty(p.hitNickname) && p.hitNickname == myName;
        HitEffectSystem.Instance?.TriggerHit(pos, hitMe);
    }

    void HandleMissileWarn(MissileWarnPacket p)
    {
        string myName = GameManager.Instance?.myNickname
                     ?? NetworkManager.Instance?.socketClient.myNickname
                     ?? "";
        if (p.targetNickname != myName) return;

        var threatWarn = FindObjectOfType<ThreatWarningSystem>();
        if (threatWarn == null) return;

        var level = (ThreatWarningSystem.ThreatLevel)Mathf.Clamp(p.lockLevel, 0, 4);

        Vector3 shooterPos = Vector3.zero;
        if (players.TryGetValue(p.shooterNickname, out var shooter))
            shooterPos = shooter.transform.position;

        threatWarn.ReportNetworkThreat(level, shooterPos);
    }

    // ── 플레이어 생성 / 제거 ──────────────────────────────────────────────
    void CreatePlayer(string nickname, Vector3 pos, Quaternion rot, bool isMove)
    {
        if (players.ContainsKey(nickname)) return;

        GameObject obj    = Instantiate(playerPrefab);
        PlayerController player = obj.GetComponent<PlayerController>();

        string myName = GameManager.Instance?.myNickname
                     ?? NetworkManager.Instance?.socketClient.myNickname;
        bool isLocal = nickname == myName;

        player.nickname      = nickname;
        player.isLocalPlayer = isLocal;
        player.transform.SetPositionAndRotation(pos, rot);
        player.ClearSnapshots();
        player.AddSnapshot(pos, rot, isMove);

        if (!isLocal)
        {
            var label = obj.AddComponent<PlayerLabel>();
            label.SetNickname(nickname);
        }

        players[nickname] = player;
        Debug.Log($"[Player] Spawned: {nickname}  local={isLocal}");
    }

    void RemovePlayer(string nickname)
    {
        if (!players.TryGetValue(nickname, out var player)) return;
        players.Remove(nickname);
        if (player != null) Destroy(player.gameObject);
        Debug.Log($"[Player] Despawned: {nickname}");
    }

    void ClearPlayers()
    {
        foreach (var p in players.Values)
            if (p != null) Destroy(p.gameObject);
        players.Clear();
    }

    public PlayerController GetPlayer(string nickname)
    {
        players.TryGetValue(nickname, out var p);
        return p;
    }
}

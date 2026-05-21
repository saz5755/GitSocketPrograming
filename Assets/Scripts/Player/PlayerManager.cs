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
        sc.OnSpawn   += HandleSpawn;
        sc.OnDespawn += HandleDespawn;
        sc.OnMove    += HandleMove;
    }

    void OnDestroy()
    {
        SocketClient sc = NetworkManager.Instance?.socketClient;
        if (sc != null)
        {
            sc.OnSpawn   -= HandleSpawn;
            sc.OnDespawn -= HandleDespawn;
            sc.OnMove    -= HandleMove;
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

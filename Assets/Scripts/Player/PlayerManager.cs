using System.Collections.Generic;
using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    public GameObject playerPrefab;

    readonly Dictionary<string, PlayerController>  players        = new();
    readonly Dictionary<string, RemoteMissileView> remoteMissiles = new();

    void Start()
    {
        SocketClient sc = NetworkManager.Instance?.socketClient;
        if (sc == null) { Debug.LogWarning("[PlayerManager] SocketClient not found"); return; }
        sc.OnSpawn          += HandleSpawn;
        sc.OnDespawn        += HandleDespawn;
        sc.OnMove           += HandleMove;
        sc.OnMissileSpawn   += HandleMissileSpawn;
        sc.OnMissileDestroy += HandleMissileDestroy;
        sc.OnMissileMove    += HandleMissileMove;
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
            sc.OnMissileSpawn   -= HandleMissileSpawn;
            sc.OnMissileDestroy -= HandleMissileDestroy;
            sc.OnMissileMove    -= HandleMissileMove;
            sc.OnMissileWarn    -= HandleMissileWarn;
        }
        ClearPlayers();
        ClearRemoteMissiles();
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

    // ── 원격 미사일 스폰: 상대방이 쏜 미사일을 시각적으로 생성 ─────────────
    void HandleMissileSpawn(MissileSpawnPacket p)
    {
        string myName = GameManager.Instance?.myNickname
                     ?? NetworkManager.Instance?.socketClient.myNickname ?? "";

        // 자신이 발사한 미사일은 MissileLauncher에서 이미 로컬 생성
        if (p.shooterNickname == myName) return;
        if (remoteMissiles.ContainsKey(p.missileId)) return;

        var go = new GameObject($"Missile_Remote_{p.missileId}");

        // 캡슐로 미사일 외형 (콜라이더 없음)
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.Destroy(body.GetComponent<CapsuleCollider>());
        body.transform.SetParent(go.transform, false);
        body.transform.localScale    = new Vector3(0.2f, 0.5f, 0.2f);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        go.transform.SetPositionAndRotation(
            new Vector3(p.posX, p.posY, p.posZ),
            Quaternion.Euler(p.rotX, p.rotY, p.rotZ));

        var view = go.AddComponent<RemoteMissileView>();
        view.AttachSmokeTrail();
        view.AddSnapshot(go.transform.position, go.transform.rotation);

        remoteMissiles[p.missileId] = view;
        Debug.Log($"[Missile] Remote spawned: {p.missileId} shooter={p.shooterNickname}");
    }

    // ── 원격 미사일 위치 업데이트 (UDP 10Hz) ──────────────────────────────
    void HandleMissileMove(MissileMovePacket p)
    {
        if (!remoteMissiles.TryGetValue(p.missileId, out var view)) return;
        if (view == null) { remoteMissiles.Remove(p.missileId); return; }

        view.AddSnapshot(
            new Vector3(p.posX, p.posY, p.posZ),
            Quaternion.Euler(p.rotX, p.rotY, p.rotZ));
    }

    void HandleMissileDestroy(MissileDestroyPacket p)
    {
        string myName = GameManager.Instance?.myNickname
                     ?? NetworkManager.Instance?.socketClient.myNickname
                     ?? "";
        // 발사자는 Detonate()에서 이미 로컬 이펙트 처리 → 서버 에코 무시
        if (p.shooterNickname == myName) return;

        // 원격 미사일 오브젝트 제거
        if (remoteMissiles.TryGetValue(p.missileId, out var view))
        {
            if (view != null) Destroy(view.gameObject);
            remoteMissiles.Remove(p.missileId);
        }

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

        var level = (ThreatWarningSystem.ThreatLevel)Mathf.Clamp(p.lockLevel, 0, 5);

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

        if (isLocal)
        {
            InitLocalPlayerGround(player, pos);
        }
        else
        {
            var label = obj.AddComponent<PlayerLabel>();
            label.SetNickname(nickname);
        }

        players[nickname] = player;
        Debug.Log($"[Player] Spawned: {nickname}  local={isLocal}");
    }

    void InitLocalPlayerGround(PlayerController pc, Vector3 spawnPos)
    {
        // GameModeManager 생성
        if (GameModeManager.Instance == null)
        {
            var gmGO = new GameObject("GameModeManager");
            gmGO.AddComponent<GameModeManager>();
        }

        // 보행 캐릭터 오브젝트 생성
        var charGO = BuildCharacterObject();
        var gc = charGO.GetComponent<GroundController>();

        // 이륙 존 (탑승 포인트) — Configure() 호출 후 Start()에서 비주얼 생성
        var takeoffZoneGO = new GameObject("TakeoffZone");
        takeoffZoneGO.transform.position = spawnPos;
        var takeoffZone = takeoffZoneGO.AddComponent<AircraftZone>();
        takeoffZone.Configure(AircraftZone.Type.Takeoff, 12f);

        // 착륙 존 (하차 포인트) — 같은 위치, 반경 더 크게 (비행 중 착지 허용 범위)
        var landingZoneGO = new GameObject("LandingZone");
        landingZoneGO.transform.position = spawnPos;
        var landingZone = landingZoneGO.AddComponent<AircraftZone>();
        landingZone.Configure(AircraftZone.Type.Landing, 30f);

        // 초기화
        GameModeManager.Instance.Init(gc, pc, spawnPos);
    }

    static GameObject BuildCharacterObject()
    {
        var root = new GameObject("LocalGroundCharacter");

        // CharacterController를 GroundController보다 먼저 추가
        var cc = root.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0f, 0.9f, 0f);

        // 인간형 파일럿 모델 (URP 셰이더 자동 선택)
        root.AddComponent<CharacterModelBuilder>();
        root.AddComponent<GroundController>();

        return root;
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

    void ClearRemoteMissiles()
    {
        foreach (var v in remoteMissiles.Values)
            if (v != null) Destroy(v.gameObject);
        remoteMissiles.Clear();
    }

    public PlayerController GetPlayer(string nickname)
    {
        players.TryGetValue(nickname, out var p);
        return p;
    }
}

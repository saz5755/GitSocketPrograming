using System.Collections.Generic;
using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    public GameObject playerPrefab;

    [Tooltip("지상 캐릭터용 FBX 프리팹 (Humanoid 리그). 비워두면 임시 프로시저럴 모델 사용.")]
    [SerializeField] GameObject groundCharPrefab;

    [Header("씬 사전 배치 (선택, 미설정 시 프리팹 인스턴스화로 폴백)")]
    [Tooltip("항모 위에 미리 배치해 둔 로컬 플레이어 전투기. 설정 시 Instantiate 생략.")]
    [SerializeField] PlayerController _localAircraftInScene;


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

        string myName = GameManager.Instance?.myNickname
                     ?? NetworkManager.Instance?.socketClient.myNickname;
        bool isLocal = nickname == myName;

        PlayerController player;

        if (isLocal && _localAircraftInScene != null)
        {
            // 씬 사전 배치 항공기 재사용 — 항모 자식으로 배치해도 런타임에 de-parent
            player = _localAircraftInScene;
            player.transform.SetParent(null, true);   // 월드 좌표 유지하며 최상위로
            Debug.Log("[Player] Using scene-placed local aircraft");
        }
        else
        {
            // 프리팹 인스턴스화 (기존 동작 — 폴백)
            GameObject obj = Instantiate(playerPrefab);
            player = obj.GetComponent<PlayerController>();
            player.transform.SetPositionAndRotation(pos, rot);
        }

        player.nickname      = nickname;
        player.isLocalPlayer = isLocal;
        player.ClearSnapshots();
        player.AddSnapshot(player.transform.position, player.transform.rotation, isMove);

        if (isLocal)
            InitLocalPlayerGround(player, player.transform.position);
        else
            player.gameObject.AddComponent<PlayerLabel>().SetNickname(nickname);

        players[nickname] = player;
        Debug.Log($"[Player] Spawned: {nickname}  local={isLocal}");
    }

    void InitLocalPlayerGround(PlayerController pc, Vector3 spawnPos)
    {
        if (GameModeManager.Instance == null)
        {
            var gmGO = new GameObject("GameModeManager");
            gmGO.AddComponent<GameModeManager>();
        }

        var charGO = BuildCharacterObject();
        var gc     = charGO.GetComponent<GroundController>();

        // spawnPos가 공중(항공기 높이)일 수 있으므로 지형 스냅
        Vector3 groundPos = SnapToGround(spawnPos);

        // TakeoffZone 제거 — 탑승은 AircraftBoardingTrigger(근접 감지)가 담당
        // LandingZone은 씬에서 수동 배치 (AircraftZone Type.Landing)

        GameModeManager.Instance.Init(gc, pc, spawnPos, groundPos);
    }

    // 주어진 위치 아래로 지형을 찾아 Y를 스냅. 지형 없으면 Y=0 사용.
    static Vector3 SnapToGround(Vector3 pos)
    {
        Vector3 origin = pos + Vector3.up * 500f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1000f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return hit.point;
        return new Vector3(pos.x, 0f, pos.z);
    }

    GameObject BuildCharacterObject()
    {
        // ── FBX 프리팹이 연결된 경우: Instantiate 후 필수 컴포넌트만 보완 ──
        if (groundCharPrefab != null)
        {
            var go = Instantiate(groundCharPrefab);
            go.name = "LocalGroundCharacter";

            if (go.GetComponent<CharacterController>() == null)
            {
                var cc = go.AddComponent<CharacterController>();
                cc.height = 1.8f; cc.radius = 0.3f;
                cc.center = new Vector3(0f, 0.9f, 0f);
            }
            if (go.GetComponent<GroundController>() == null)
                go.AddComponent<GroundController>();

            return go;
        }

        // ── 임시 프로시저럴 모델 (FBX 교체 전까지) ──────────────────────────
        var root = new GameObject("LocalGroundCharacter");

        var rootCc = root.AddComponent<CharacterController>();
        rootCc.height = 1.8f;
        rootCc.radius = 0.3f;
        rootCc.center = new Vector3(0f, 0.9f, 0f);

        root.AddComponent<CharacterModelBuilder>();
        root.AddComponent<GroundController>();
        root.AddComponent<ProceduralCharacterAnimator>();

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

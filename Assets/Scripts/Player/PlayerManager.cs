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

    // 로컬 플레이어의 PlayerController만 저장 (원격 플레이어 항공기 동적 생성 없음)
    readonly Dictionary<string, PlayerController>  players        = new();
    readonly Dictionary<string, RemoteMissileView> remoteMissiles = new();
    // 원격 플레이어 지상 캐릭터 (항공기 없이 지상 표현만 관리)
    readonly Dictionary<string, GameObject>        _groundChars   = new();
    // 콕핏 탑승 중인 원격 플레이어의 "READY" 항공기 상단 레이블
    readonly Dictionary<string, GameObject>              _readyLabels      = new();
    // nickname → 점령된 AircraftBoardingTrigger (IsOccupied 해제용)
    readonly Dictionary<string, AircraftBoardingTrigger> _occupiedAircraft = new();
    // 비행 중 퇴장한 플레이어의 방치 항공기 오브젝트
    readonly Dictionary<string, GameObject>              _abandonedAircraft = new();

    static Material _grayMat;

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
        ClearAll();
        ClearRemoteMissiles();
    }

    // ── 이벤트 핸들러 ─────────────────────────────────────────────────────
    void HandleSpawn(SpawnPacket p)
    {
        bool isLocal = p.nickname == GetMyName();

        if (isLocal)
            CreateLocalPlayer(p);
        else
        {
            CreateRemoteGroundPlayer(p);
            // 내가 콕핏/비행 중이면 새 입장자에게 현재 상태를 재전송
            GameModeManager.Instance?.BroadcastCurrentState();
        }
    }

    void HandleDespawn(DespawnPacket p)
    {
        if (players.TryGetValue(p.nickname, out var pc))
        {
            players.Remove(p.nickname);
            if (pc != null) Destroy(pc.gameObject);
        }

        if (_groundChars.TryGetValue(p.nickname, out var gc))
        {
            _groundChars.Remove(p.nickname);
            if (gc != null) Destroy(gc);
        }

        HideReadyLabel(p.nickname);

        // 비행 중 퇴장: 마지막 위치에 방치 항공기 생성
        if (p.wasFlying && (p.posX != 0f || p.posY != 0f || p.posZ != 0f))
        {
            SpawnAbandonedAircraft(
                p.nickname,
                new Vector3(p.posX, p.posY, p.posZ),
                Quaternion.Euler(p.rotX, p.rotY, p.rotZ));
        }

        Debug.Log($"[Player] Despawned: {p.nickname}  wasFlying={p.wasFlying}");
    }

    // 비행 중 퇴장한 플레이어의 전투기를 갑판에 자동 착함 후 탑승 가능 상태로 배치
    void SpawnAbandonedAircraft(string nickname, Vector3 pos, Quaternion rot)
    {
        GameObject go;

        if (playerPrefab != null)
        {
            go = Instantiate(playerPrefab, pos, rot);
            go.name = $"Abandoned_{nickname}";

            // AI·이동 컴포넌트만 제거 — PlayerController·AircraftBoardingTrigger는 탑승에 필요하므로 유지
            foreach (var t in new System.Type[]
                { typeof(AIBotController), typeof(EscortAI), typeof(EscortVFXController) })
            {
                var c = go.GetComponentInChildren(t);
                if (c != null) Destroy(c);
            }

            // 물리 낙하 방지 (코루틴으로 직접 이동하므로 Rigidbody 불필요)
            var rb = go.GetComponentInChildren<Rigidbody>();
            if (rb != null) { rb.isKinematic = true; Destroy(rb); }
        }
        else
        {
            go = new GameObject($"Abandoned_{nickname}");
            go.AddComponent<PlayerController>().enabled = false;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(body.GetComponent<CapsuleCollider>());
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = new Color(1f, 0.8f, 0.1f);
            body.GetComponent<Renderer>().sharedMaterial = mat;
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(1.2f, 0.28f, 3.8f);
            go.transform.SetPositionAndRotation(pos, rot);
        }

        // 탑승 트리거 추가 — [RequireComponent(PlayerController)] 충족 필요
        if (go.GetComponent<AircraftBoardingTrigger>() == null)
            go.AddComponent<AircraftBoardingTrigger>();

        go.AddComponent<PlayerLabel>().SetNickname($"{nickname} [OFFLINE]");
        _abandonedAircraft[nickname] = go;

        // 항모가 있으면 갑판으로 자동 착함 → 지상 플레이어가 F키 탑승 가능
        var carrier = Object.FindFirstObjectByType<CarrierController>();
        if (carrier != null)
            StartCoroutine(AutoLandAbandoned(go, carrier.transform));

        Debug.Log($"[PlayerManager] Abandoned aircraft spawned at {pos} for {nickname}");
    }

    // 방치 항공기를 항모 갑판으로 자동 비행·착함
    System.Collections.IEnumerator AutoLandAbandoned(GameObject aircraft, Transform carrier)
    {
        // 갑판 착함 목표 — EscortSlot 위치 우선, 없으면 항모 중심에서 추정
        Vector3 deckTarget;
        var slot = Object.FindFirstObjectByType<EscortSlot>();
        if (slot != null)
        {
            deckTarget = slot.transform.position;
        }
        else
        {
            Vector3 above = carrier.position + Vector3.up * 200f;
            deckTarget = Physics.Raycast(above, Vector3.down, out RaycastHit hit, 400f,
                                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                         ? hit.point + Vector3.up * 1.5f
                         : carrier.position + carrier.up * 8f;
        }

        const float speed = 90f; // m/s

        while (aircraft != null &&
               Vector3.Distance(aircraft.transform.position, deckTarget) > 1.5f)
        {
            aircraft.transform.position = Vector3.MoveTowards(
                aircraft.transform.position, deckTarget, speed * Time.deltaTime);
            aircraft.transform.rotation = Quaternion.Slerp(
                aircraft.transform.rotation, carrier.rotation, 3f * Time.deltaTime);
            yield return null;
        }

        if (aircraft != null)
        {
            aircraft.transform.SetPositionAndRotation(deckTarget, carrier.rotation);
            Debug.Log("[PlayerManager] Abandoned aircraft auto-landed on carrier deck");
        }
    }

    void HandleMove(MoveBroadcastPacket p)
    {
        // 자신의 패킷은 무시 (로컬에서 직접 제어)
        if (p.nickname == GetMyName()) return;

        if (!_groundChars.TryGetValue(p.nickname, out var gc) || gc == null) return;

        var pos = new Vector3(p.posX, p.posY, p.posZ);
        var rot = Quaternion.Euler(p.rotX, p.rotY, p.rotZ);

        if (p.isBoardedInCockpit)
        {
            // 콕핏 탑승 중: 지상 캐릭터 숨기고 항공기 위에 READY 레이블 표시
            if (gc.activeSelf) gc.SetActive(false);
            ShowReadyLabel(p.nickname, pos);
        }
        else if (p.isFlying)
        {
            // 비행 중: 지상 캐릭터·READY 레이블 모두 숨김
            if (gc.activeSelf) gc.SetActive(false);
            HideReadyLabel(p.nickname);
        }
        else
        {
            // 지상 모드: 지상 캐릭터 표시, READY 레이블 제거
            HideReadyLabel(p.nickname);
            if (!gc.activeSelf) gc.SetActive(true);
            gc.GetComponent<RemoteGroundInterp>()?.SetTarget(pos, rot, p.animState);
        }
    }

    // ── 원격 미사일 ────────────────────────────────────────────────────────
    void HandleMissileSpawn(MissileSpawnPacket p)
    {
        string myName = GetMyName();
        if (p.shooterNickname == myName) return;
        if (remoteMissiles.ContainsKey(p.missileId)) return;

        var go   = new GameObject($"Missile_Remote_{p.missileId}");
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.Destroy(body.GetComponent<CapsuleCollider>());
        body.GetComponent<Renderer>().sharedMaterial = GetGrayMaterial();
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
        string myName = GetMyName();
        if (p.shooterNickname == myName) return;

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
        string myName = GetMyName();
        if (p.targetNickname != myName) return;

        var threatWarn = ThreatWarningSystem.Instance;
        if (threatWarn == null) return;

        var level = (ThreatWarningSystem.ThreatLevel)Mathf.Clamp(p.lockLevel, 0, 5);

        Vector3 shooterPos = Vector3.zero;
        if (players.TryGetValue(p.shooterNickname, out var shooter) && shooter != null)
            shooterPos = shooter.transform.position;
        else if (_groundChars.TryGetValue(p.shooterNickname, out var gc) && gc != null)
            shooterPos = gc.transform.position;

        threatWarn.ReportNetworkThreat(level, shooterPos);
    }

    // ── 로컬 플레이어 생성 ─────────────────────────────────────────────────
    void CreateLocalPlayer(SpawnPacket p)
    {
        if (players.ContainsKey(p.nickname)) return;

        var pos = new Vector3(p.x, p.y, p.z);
        var rot = Quaternion.Euler(p.rotX, p.rotY, p.rotZ);

        PlayerController player;

        if (_localAircraftInScene != null)
        {
            player = _localAircraftInScene;
            player.transform.SetParent(null, true);
            Debug.Log("[Player] Using scene-placed local aircraft");
        }
        else
        {
            GameObject obj = Instantiate(playerPrefab);
            player = obj.GetComponent<PlayerController>();
            player.transform.SetPositionAndRotation(pos, rot);
        }

        player.nickname      = p.nickname;
        player.isLocalPlayer = true;
        player.ClearSnapshots();
        player.AddSnapshot(player.transform.position, player.transform.rotation, p.isMove);

        InitLocalPlayerGround(player, player.transform.position);

        players[p.nickname] = player;
        Debug.Log($"[Player] Spawned local: {p.nickname}");

        // AI 시스템이 씬에 있으면 호스트 초기화 시도
        AIManager.Instance?.TryInitializeAsHost(player);
    }

    // ── 원격 플레이어: 지상 캐릭터만 생성 (항공기 동적 생성 없음) ──────────
    void CreateRemoteGroundPlayer(SpawnPacket p)
    {
        if (_groundChars.ContainsKey(p.nickname)) return;

        var pos = new Vector3(p.x, p.y, p.z);
        var rot = Quaternion.Euler(p.rotX, p.rotY, p.rotZ);

        var go = CreateRemoteGroundChar(p.nickname, pos, rot);
        go.SetActive(false); // 항상 숨김으로 생성 — step-1.5 MOVE 패킷이 최종 상태 결정
        _groundChars[p.nickname] = go;

        // Spawn 패킷에 이미 콕핏 상태가 담겨 있으면 즉시 READY 레이블 표시
        if (p.isBoardedInCockpit) ShowReadyLabel(p.nickname, pos);

        Debug.Log($"[Player] Spawned remote ground: {p.nickname}  isFlying={p.isFlying}  inCockpit={p.isBoardedInCockpit}");
    }

    // ── 로컬 지상 캐릭터 초기화 ────────────────────────────────────────────
    void InitLocalPlayerGround(PlayerController pc, Vector3 spawnPos)
    {
        if (GameModeManager.Instance == null)
        {
            var gmGO = new GameObject("GameModeManager");
            gmGO.AddComponent<GameModeManager>();
        }

        var charGO = BuildCharacterObject();
        var gc     = charGO.GetComponent<GroundController>();

        Vector3 groundPos = SnapToGround(spawnPos);

        GameModeManager.Instance.Init(gc, pc, spawnPos, groundPos);
    }

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

    // 원격 지상 캐릭터 오브젝트 생성
    GameObject CreateRemoteGroundChar(string nickname, Vector3 pos, Quaternion rot)
    {
        GameObject go;
        if (groundCharPrefab != null)
        {
            go = Instantiate(groundCharPrefab);
            go.name = $"RemoteGround_{nickname}";
            var gc = go.GetComponent<GroundController>();
            if (gc != null) Destroy(gc);
            var cc = go.GetComponent<CharacterController>();
            if (cc != null) Destroy(cc);
            // 루트 모션이 RemoteGroundInterp의 위치 제어를 방해하지 않도록 비활성화
            var anim = go.GetComponentInChildren<Animator>();
            if (anim != null) anim.applyRootMotion = false;
        }
        else
        {
            go = new GameObject($"RemoteGround_{nickname}");
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(body.GetComponent<CapsuleCollider>());
            body.GetComponent<Renderer>().sharedMaterial = GetGrayMaterial();
            body.transform.SetParent(go.transform, false);
            body.transform.localScale    = new Vector3(0.5f, 0.9f, 0.5f);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        }
        go.transform.SetPositionAndRotation(pos, rot);
        go.AddComponent<RemoteGroundInterp>().SetTarget(pos, rot);
        go.AddComponent<PlayerLabel>().SetNickname(nickname);
        return go;
    }

    void ClearAll()
    {
        foreach (var p in players.Values)
            if (p != null) Destroy(p.gameObject);
        players.Clear();

        foreach (var gc in _groundChars.Values)
            if (gc != null) Destroy(gc);
        _groundChars.Clear();

        foreach (var rl in _readyLabels.Values)
            if (rl != null) Destroy(rl);
        _readyLabels.Clear();

        foreach (var t in _occupiedAircraft.Values)
            if (t != null) t.IsOccupied = false;
        _occupiedAircraft.Clear();

        foreach (var go in _abandonedAircraft.Values)
            if (go != null) Destroy(go);
        _abandonedAircraft.Clear();
    }

    void ClearRemoteMissiles()
    {
        foreach (var v in remoteMissiles.Values)
            if (v != null) Destroy(v.gameObject);
        remoteMissiles.Clear();
    }

    // ── READY 레이블: 원격 플레이어 콕핏 탑승 시 항공기 위에 표시 ───────────
    void ShowReadyLabel(string nickname, Vector3 aircraftPos)
    {
        // 이미 있으면 위치만 갱신
        if (_readyLabels.TryGetValue(nickname, out var existing) && existing != null)
        {
            existing.transform.position = aircraftPos + Vector3.up * 1.5f;
            return;
        }

        var go = new GameObject($"ReadyLabel_{nickname}");
        go.transform.position = aircraftPos + Vector3.up * 1.5f;

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;

        var crt = go.GetComponent<RectTransform>();
        crt.sizeDelta  = new Vector2(300f, 80f);
        crt.localScale = Vector3.one * 0.012f;

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(go.transform, false);
        var trt = textGO.AddComponent<RectTransform>();
        trt.sizeDelta         = new Vector2(300f, 80f);
        trt.anchorMin         = trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition  = Vector2.zero;

        var txt = textGO.AddComponent<UnityEngine.UI.Text>();
        txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.text      = "▶ READY..";
        txt.fontSize  = 32;
        txt.fontStyle = FontStyle.Bold;
        txt.color     = new Color(0.1f, 1f, 0.4f, 1f);
        txt.alignment = TextAnchor.MiddleCenter;

        go.AddComponent<ReadyLabelFaceCamera>();

        _readyLabels[nickname] = go;

        // 가장 가까운 탑승 트리거를 점령 상태로 표시
        if (!_occupiedAircraft.ContainsKey(nickname))
        {
            var trigger = FindNearestBoardingTrigger(aircraftPos);
            if (trigger != null)
            {
                trigger.IsOccupied = true;
                _occupiedAircraft[nickname] = trigger;
            }
        }
    }

    void HideReadyLabel(string nickname)
    {
        if (_readyLabels.TryGetValue(nickname, out var go))
        {
            _readyLabels.Remove(nickname);
            if (go != null) Destroy(go);
        }

        // 점령 해제
        if (_occupiedAircraft.TryGetValue(nickname, out var trigger))
        {
            if (trigger != null) trigger.IsOccupied = false;
            _occupiedAircraft.Remove(nickname);
        }
    }

    AircraftBoardingTrigger FindNearestBoardingTrigger(Vector3 pos, float maxDist = 20f)
    {
        AircraftBoardingTrigger nearest = null;
        float bestSqr = maxDist * maxDist;
        foreach (var t in AircraftBoardingTrigger.All)
        {
            float sqr = (t.transform.position - pos).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; nearest = t; }
        }
        return nearest;
    }

    // URP/Built-in 공용 회색 재질 (CreatePrimitive 마젠타 방지)
    static Material GetGrayMaterial()
    {
        if (_grayMat != null) return _grayMat;
        var shader = Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Standard");
        if (shader == null) return null;
        _grayMat       = new Material(shader);
        _grayMat.color = new Color(0.7f, 0.7f, 0.7f);
        return _grayMat;
    }

    string GetMyName() =>
        GameManager.Instance?.myNickname
        ?? NetworkManager.Instance?.socketClient?.myNickname
        ?? "";

    public PlayerController GetPlayer(string nickname)
    {
        players.TryGetValue(nickname, out var p);
        return p;
    }
}

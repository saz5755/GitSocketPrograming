using System.Collections.Generic;
using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    public GameObject playerPrefab;

    [Tooltip("지상 캐릭터용 FBX 프리팹 (Humanoid 리그). 비워두면 임시 프로시저럴 모델 사용.")]
    [SerializeField] GameObject groundCharPrefab;

    readonly Dictionary<string, PlayerController>  players        = new();
    readonly Dictionary<string, RemoteMissileView> remoteMissiles = new();
    // 원격 플레이어 지상 캐릭터
    readonly Dictionary<string, GameObject>        _groundChars   = new();
    // 원격 플레이어 비행 중 항공기 뷰 (NetworkAircraft 미사용 시 폴백)
    readonly Dictionary<string, PlayerController>  _remoteAircraft = new();
    // 콕핏 탑승 중인 원격 플레이어의 "READY" 항공기 상단 레이블
    readonly Dictionary<string, GameObject>              _readyLabels      = new();
    // nickname → 점령된 AircraftBoardingTrigger (IsOccupied 해제용)
    readonly Dictionary<string, AircraftBoardingTrigger> _occupiedAircraft = new();
    // 비행 중 퇴장한 플레이어의 방치 항공기 오브젝트
    readonly Dictionary<string, GameObject>              _abandonedAircraft = new();
    // 공유 항공기: nickname → networkId (BOARD_AIRCRAFT 수신 시 등록)
    readonly Dictionary<string, int>                     _boardedAircraftId = new();
    // 탑승/비행이 확인된 플레이어 집합 — UDP 지연 지상 패킷이 인간형을 다시 표시하지 않도록 차단
    readonly HashSet<string>                             _remoteBoarded     = new();

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
        sc.OnAircraftBoard  += HandleAircraftBoard;
        sc.OnAircraftLeave  += HandleAircraftLeave;
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
            sc.OnAircraftBoard  -= HandleAircraftBoard;
            sc.OnAircraftLeave  -= HandleAircraftLeave;
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
        HideRemoteAircraft(p.nickname);
        _remoteBoarded.Remove(p.nickname);

        // 공유 항공기 상태 해제 — LEAVE_AIRCRAFT 없이 퇴장한 경우(강제 종료·크래시) 대비
        // 해제하지 않으면 PC가 enabled=true 상태로 남아, 다음 탑승자의 OnEnable이 발동하지 않아
        // OnMoveAck 구독이 누락되고 서버 Reconciliation이 동작하지 않음
        if (_boardedAircraftId.TryGetValue(p.nickname, out int leftAircraftId))
        {
            _boardedAircraftId.Remove(p.nickname);
            var leftNa = NetworkAircraft.GetById(leftAircraftId);
            if (leftNa != null)
            {
                leftNa.PC.enabled = false;
                leftNa.PC.ClearSnapshots();
            }
        }

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

        if (!_groundChars.TryGetValue(p.nickname, out var gc) || gc == null)
        {
            // MOVE가 SPAWN보다 먼저 도착(UDP/TCP 타이밍 경쟁)한 경우:
            // 지상 모드일 때만 즉시 캐릭터 생성, 비행 중이면 aircraft 경로가 처리
            if (!p.isFlying && !p.isBoardedInCockpit)
            {
                var p0 = new Vector3(p.posX, p.posY, p.posZ);
                var r0 = Quaternion.Euler(p.rotX, p.rotY, p.rotZ);
                gc = CreateRemoteGroundChar(p.nickname, p0, r0);
                _groundChars[p.nickname] = gc;
                Debug.Log($"[PlayerManager] OnDemand ground char: {p.nickname}");
            }
            else return;
        }

        var pos = new Vector3(p.posX, p.posY, p.posZ);
        var rot = Quaternion.Euler(p.rotX, p.rotY, p.rotZ);

        // isFlying을 isBoardedInCockpit보다 먼저 확인:
        // 이륙 순간 서버의 isBoardedInCockpit이 아직 true일 수 있어 항공기 뷰가 누락되는 문제 방어
        if (p.isFlying)
        {
            // 비행 중: 지상 캐릭터·READY 레이블 모두 숨기고 항공기 뷰 업데이트
            _remoteBoarded.Add(p.nickname);
            if (gc.activeSelf) gc.SetActive(false);
            HideReadyLabel(p.nickname);

            // 공유 항공기 우선: BOARD_AIRCRAFT로 등록된 networkId로 씬 오브젝트를 직접 업데이트
            if (_boardedAircraftId.TryGetValue(p.nickname, out int aircraftId))
            {
                var na = NetworkAircraft.GetById(aircraftId);
                if (na != null)
                {
                    // BOARD_AIRCRAFT 수신 전 타이밍 경쟁으로 생성된 fallback 항공기 정리
                    HideRemoteAircraft(p.nickname);
                    na.PC.AddSnapshot(pos, rot, p.isMove);
                    return;
                }
            }

            // 폴백: BOARD_AIRCRAFT 미수신 시 동적 RemoteAircraft 생성 (이전 방식)
            if (!_remoteAircraft.TryGetValue(p.nickname, out var aircraft) || aircraft == null)
            {
                aircraft = CreateRemoteAircraft(p.nickname, pos, rot);
                _remoteAircraft[p.nickname] = aircraft;
                Debug.Log($"[PlayerManager] RemoteAircraft fallback for {p.nickname} at {pos}");
            }
            aircraft.AddSnapshot(pos, rot, p.isMove);
        }
        else if (p.isBoardedInCockpit)
        {
            // 콕핏 탑승 중(비행 전): 지상 캐릭터 숨기고 항공기 위에 READY 레이블 표시
            _remoteBoarded.Add(p.nickname);
            if (gc.activeSelf) gc.SetActive(false);
            HideRemoteAircraft(p.nickname);
            ShowReadyLabel(p.nickname, pos);
        }
        else
        {
            // 지상 모드: 지상 캐릭터 표시, READY 레이블·항공기 뷰 제거
            // LEAVE_AIRCRAFT(TCP)보다 MOVE(UDP)가 먼저 도착하는 타이밍 경쟁 방어:
            // 탑승/비행 확인 후 도착한 지연 UDP 패킷이 인간형을 다시 표시하지 않도록 차단
            if (_boardedAircraftId.ContainsKey(p.nickname) || _remoteBoarded.Contains(p.nickname))
            {
                if (gc.activeSelf) gc.SetActive(false);
                return;
            }
            HideReadyLabel(p.nickname);
            HideRemoteAircraft(p.nickname);
            if (!gc.activeSelf) gc.SetActive(true);
            gc.GetComponent<RemoteGroundInterp>()?.SetTarget(pos, rot, p.animState);
        }
    }

    // ── 공유 항공기 탑승/하차 ──────────────────────────────────────────────────
    void HandleAircraftBoard(AircraftBoardPacket p)
    {
        // 이전 항공기가 다른 것이면 먼저 해제 (LEAVE_AIRCRAFT 없이 다른 항공기로 이동하는 엣지케이스)
        if (_boardedAircraftId.TryGetValue(p.nickname, out int prevId) && prevId != p.aircraftId)
        {
            var prevNa = NetworkAircraft.GetById(prevId);
            if (prevNa != null)
            {
                prevNa.PC.enabled = false;
                prevNa.PC.ClearSnapshots();
            }
            if (_occupiedAircraft.TryGetValue(p.nickname, out var prevTrigger))
            {
                if (prevTrigger != null) prevTrigger.IsOccupied = false;
                _occupiedAircraft.Remove(p.nickname);
            }
        }

        _boardedAircraftId[p.nickname] = p.aircraftId;
        _remoteBoarded.Add(p.nickname);

        // 지상 캐릭터 즉시 숨김 — TCP 수신 시점에 확실히 처리
        if (_groundChars.TryGetValue(p.nickname, out var boardedGc) && boardedGc != null)
            boardedGc.SetActive(false);

        // 혹시 생성된 폴백 RemoteAircraft 제거
        HideRemoteAircraft(p.nickname);

        var na = NetworkAircraft.GetById(p.aircraftId);
        if (na == null) return;

        // 원격 모드로 전환: RemoteUpdate()가 MOVE 패킷 스냅샷을 소비
        na.PC.isLocalPlayer = false;
        na.PC.ClearSnapshots();
        na.PC.enabled = true;

        // 탑승 표시 — 다른 플레이어가 F키로 탑승 불가
        var trigger = na.GetComponent<AircraftBoardingTrigger>();
        if (trigger != null)
        {
            trigger.IsOccupied = true;
            _occupiedAircraft[p.nickname] = trigger;
        }

        Debug.Log($"[PlayerManager] Shared aircraft {p.aircraftId} → remote mode for {p.nickname}");
    }

    void HandleAircraftLeave(AircraftBoardPacket p)
    {
        // 폴백 RemoteAircraft 정리
        HideRemoteAircraft(p.nickname);

        if (_boardedAircraftId.TryGetValue(p.nickname, out int id))
        {
            _boardedAircraftId.Remove(p.nickname);
            var na = NetworkAircraft.GetById(id);
            if (na != null)
            {
                na.PC.enabled = false;
                na.PC.ClearSnapshots();
            }
        }

        // 점령 해제
        if (_occupiedAircraft.TryGetValue(p.nickname, out var trigger))
        {
            if (trigger != null) trigger.IsOccupied = false;
            _occupiedAircraft.Remove(p.nickname);
        }

        // 탑승 상태 해제 — 이후 지상 MOVE 패킷이 인간형을 다시 표시할 수 있도록
        _remoteBoarded.Remove(p.nickname);

        Debug.Log($"[PlayerManager] Shared aircraft released by {p.nickname}");
    }

    // 원격 비행 항공기 뷰 제거
    void HideRemoteAircraft(string nickname)
    {
        if (_remoteAircraft.TryGetValue(nickname, out var aircraft))
        {
            _remoteAircraft.Remove(nickname);
            if (aircraft != null) Destroy(aircraft.gameObject);
        }
    }

    // 원격 플레이어 비행 항공기 뷰 생성
    PlayerController CreateRemoteAircraft(string nickname, Vector3 pos, Quaternion rot)
    {
        GameObject go;
        if (playerPrefab != null)
        {
            go = Instantiate(playerPrefab, pos, rot);
            go.name = $"RemoteAircraft_{nickname}";

            // 로컬 전용 컴포넌트 비활성화 (전투·UI·물리)
            foreach (var t in new System.Type[]
            {
                typeof(GroundController), typeof(FlightHUD),
                typeof(TargetingSystem),  typeof(MissileLauncher),
                typeof(GunSystem),        typeof(AircraftBoardingTrigger)
            })
            {
                var c = go.GetComponentInChildren(t) as Behaviour;
                if (c != null) c.enabled = false;
            }

            var rb = go.GetComponentInChildren<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
        }
        else
        {
            go = new GameObject($"RemoteAircraft_{nickname}");
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.Destroy(body.GetComponent<CapsuleCollider>());
            body.GetComponent<Renderer>().sharedMaterial = GetGrayMaterial();
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(1.2f, 0.28f, 3.8f);
            go.transform.SetPositionAndRotation(pos, rot);
        }

        var pc = go.GetComponent<PlayerController>();
        if (pc == null) pc = go.AddComponent<PlayerController>();
        pc.nickname      = nickname;
        pc.isLocalPlayer = false;
        pc.IsFlying      = true;
        pc.enabled       = true;
        pc.AddSnapshot(pos, rot, true);

        go.AddComponent<PlayerLabel>().SetNickname(nickname);
        return pc;
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

        var spawnPos = new Vector3(p.x, p.y, p.z);

        // 공유 항공기 방식: 로컬 전용 항공기 없음 — 지상 캐릭터만 생성
        // 플레이어는 PlayerAircraftManager가 스폰한 공유 항공기를 직접 탑승한다
        InitLocalPlayerGround(spawnPos);

        // players 딕셔너리에는 nickname만 마킹 (항공기 탑승 전이므로 PC는 null)
        players[p.nickname] = null;
        Debug.Log($"[Player] Spawned local: {p.nickname}");

        // 호스트 초기화: AI 에스코트·적 스폰 (EscortSlot이 공유 항공기 역할 겸함)
        AIManager.Instance?.TryInitializeAsHost(null);
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
    void InitLocalPlayerGround(Vector3 spawnPos)
    {
        if (GameModeManager.Instance == null)
        {
            var gmGO = new GameObject("GameModeManager");
            gmGO.AddComponent<GameModeManager>();
        }

        var charGO = BuildCharacterObject();
        var gc     = charGO.GetComponent<GroundController>();

        Vector3 groundPos = SnapToGround(spawnPos);

        // pc = null: 로컬 전용 항공기 없음 — 플레이어가 공유 항공기를 탑승할 때 GameModeManager._pc 설정됨
        GameModeManager.Instance.Init(gc, null, spawnPos, groundPos);
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
            go.AddComponent<CharacterModelBuilder>();
            go.AddComponent<ProceduralCharacterAnimator>();
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

        foreach (var aircraft in _remoteAircraft.Values)
            if (aircraft != null) Destroy(aircraft.gameObject);
        _remoteAircraft.Clear();

        foreach (var rl in _readyLabels.Values)
            if (rl != null) Destroy(rl);
        _readyLabels.Clear();

        foreach (var t in _occupiedAircraft.Values)
            if (t != null) t.IsOccupied = false;
        _occupiedAircraft.Clear();

        foreach (var go in _abandonedAircraft.Values)
            if (go != null) Destroy(go);
        _abandonedAircraft.Clear();

        // 공유 항공기 원격 모드 해제
        foreach (var id in _boardedAircraftId.Values)
        {
            var na = NetworkAircraft.GetById(id);
            if (na != null) { na.PC.enabled = false; na.PC.ClearSnapshots(); }
        }
        _boardedAircraftId.Clear();
        _remoteBoarded.Clear();
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

    // URP/Built-in/HDRP 공용 회색 재질 (셰이더 없으면 씬 기존 재질 차용)
    static Material GetGrayMaterial()
    {
        if (_grayMat != null) return _grayMat;

        string[] candidates = {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Lightweight Render Pipeline/Lit",
            "HDRP/Lit", "Standard", "Diffuse",
        };
        foreach (string n in candidates)
        {
            var sh = Shader.Find(n);
            if (sh != null)
            {
                _grayMat       = new Material(sh);
                _grayMat.color = new Color(0.7f, 0.7f, 0.7f);
                return _grayMat;
            }
        }
        // 씬에 이미 있는 렌더러 재질을 차용 (Hidden 셰이더 제외)
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (r.sharedMaterial?.shader != null &&
                !r.sharedMaterial.shader.name.StartsWith("Hidden"))
            {
                _grayMat       = new Material(r.sharedMaterial);
                _grayMat.color = new Color(0.7f, 0.7f, 0.7f);
                return _grayMat;
            }
        }
        return new Material(Shader.Find("Sprites/Default"));
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

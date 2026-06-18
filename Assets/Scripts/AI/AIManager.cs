using System.Collections.Generic;
using UnityEngine;

// AI 전투기 관리자.
// ┌ 호스트 클라이언트: AI 오브젝트를 직접 생성·제어하고 위치를 UDP 브로드캐스트.
// └ 원격 클라이언트: AI_SPAWN/AI_MOVE 패킷을 받아 PlayerController 뷰를 갱신.
//
// 씬에 이 컴포넌트를 가진 GameObject를 배치하고 Inspector에서 설정하세요.
// 호스트 판정: EnterRoomResultPacket.hostNickname(서버 방장)이 일치하는 클라이언트만 AI를 스폰.
// _spawnEscorts / _spawnEnemies 는 "이 씬에서 해당 AI를 켤 것인가" 여부만 결정.
public class AIManager : MonoBehaviour
{
    public static AIManager Instance { get; private set; }

    [Header("AI 기체 프리팹 (없으면 기본 모델)")]
    [SerializeField] GameObject _aiPrefab;

    [Header("에스코트 AI — V자 편대 (슬롯 기반)")]
    [SerializeField] bool _spawnEscorts = false;
    [Tooltip("이 GameObject 아래의 EscortSlot 자식들을 모두 수집해 슬롯 수만큼 봇을 spawn한다.")]
    [SerializeField] Transform _escortSlotsRoot;

    [Header("AI 공통 설정 (모든 봇에 자동 주입)")]
    [Tooltip("EscortBehaviorSO — 미할당 시 런타임 폴백 인스턴스 생성")]
    [SerializeField] EscortBehaviorSO _escortBehavior;
    [Tooltip("AIBotConfigSO — 미할당 시 런타임 폴백 인스턴스 생성")]
    [SerializeField] AIBotConfigSO _botConfig;

    [Header("적 AI — 미사일 공격 + 위협 기동")]
    [SerializeField] bool _spawnEnemies = false;
    [SerializeField] [Range(1, 4)] int _enemyCount = 1;

    [Header("에스코트 착함 타이밍")]
    [Tooltip("에스코트 간 착함 시작 시차 (초) — 플레이어 F키 하차 후 1번기부터 이 간격으로 순차 착함")]
    [SerializeField] float _escortLandingStagger = 5f;

    bool      _isHost;
    bool      _initialized;
    bool      _escortLandingStarted;
    Coroutine _landingCoroutine;   // EscortLandingCoroutine 핸들 — 재시작 시 취소용

    // EnterRoomResult에서 수신한 방장 닉네임 — 호스트 판정에 사용
    string    _networkHostNickname = "";

    // 플레이어 어레스팅 와이어 경로 (와이어 정지 시 저장, 하차 시 사용)
    Vector3 _arrestedApproachDir;
    Vector3 _arrestedWirePos;
    bool    _hasArrestInfo;

    // 호스트가 관리하는 AI 봇 목록
    readonly List<AIBotController> _bots = new();

    CarrierController _cachedCarrier;

    // 로컬 플레이어의 원래 항공기 (에스코트 전환 기준)
    PlayerController _localPlayerAircraft;

    // 원격 클라이언트에서 AI를 표시하는 PlayerController 뷰
    readonly Dictionary<string, PlayerController> _remoteBots = new();

    // ── 생명주기 ──────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;
        sc.OnAISpawn         += HandleRemoteAISpawn;
        sc.OnAIDespawn       += HandleRemoteAIDespawn;
        sc.OnAIMove          += HandleRemoteAIMove;
        sc.OnHostChange      += HandleHostChange;
        sc.OnEnterRoomResult += HandleEnterRoomResult;
        sc.OnSpawn           += HandleRemotePlayerJoin;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;
        sc.OnAISpawn         -= HandleRemoteAISpawn;
        sc.OnAIDespawn       -= HandleRemoteAIDespawn;
        sc.OnAIMove          -= HandleRemoteAIMove;
        sc.OnHostChange      -= HandleHostChange;
        sc.OnEnterRoomResult -= HandleEnterRoomResult;
        sc.OnSpawn           -= HandleRemotePlayerJoin;
    }

    // ── 호스트 초기화 (PlayerManager.CreateLocalPlayer에서 호출) ───────────

    public void TryInitializeAsHost(PlayerController localPlayer)
    {
        if (_initialized) return;
        _initialized = true;

        // 서버가 지정한 방장(hostNickname)만 AI를 스폰한다.
        // EnterRoomResult가 아직 안 왔거나 빈 값이면 첫 접속자(싱글 테스트)로 간주.
        string myNick = NetworkManager.Instance?.socketClient?.myNickname ?? "";
        bool isNetworkHost = string.IsNullOrEmpty(_networkHostNickname)
                             || myNick == _networkHostNickname;
        if (!isNetworkHost) return;

        _isHost = true;
        // localPlayer는 공유 항공기 방식에서 null일 수 있음 — 각 스폰 함수가 null 처리
        if (_spawnEscorts) SpawnEscorts(localPlayer);
        if (_spawnEnemies && localPlayer != null) SpawnEnemies(localPlayer);  // 적 AI는 리더 필요
    }

    /// <summary>
    /// 플레이어가 항공기에 탑승해 이륙할 때 호출 — 에스코트 봇 리더를 현재 항공기로 설정.
    /// </summary>
    public void SetLocalPlayerAircraft(PlayerController pc)
    {
        _localPlayerAircraft = pc;
        foreach (var bot in _bots)
        {
            if (bot == null) continue;
            bot.GetComponent<EscortAI>()?.UpdateLeader(pc?.transform);
        }
    }

    // EnterRoomResult 수신 시 방장 닉네임 저장
    void HandleEnterRoomResult(EnterRoomResultPacket p)
    {
        if (p.success) _networkHostNickname = p.hostNickname ?? "";
    }

    // 새 플레이어 입장 시 호스트가 현재 AI 목록을 재전송 — 늦게 합류한 클라이언트 동기화
    void HandleRemotePlayerJoin(SpawnPacket p)
    {
        string myNick = NetworkManager.Instance?.socketClient?.myNickname ?? "";
        if (p.nickname == myNick) return;       // 자신의 스폰은 무시
        if (!_isHost || _bots.Count == 0) return;

        StartCoroutine(ReSendAISpawnToNewJoiner());
    }

    System.Collections.IEnumerator ReSendAISpawnToNewJoiner()
    {
        // 상대방이 씬을 완전히 초기화할 시간을 줌
        yield return new WaitForSeconds(1f);
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) yield break;

        foreach (var bot in _bots)
        {
            if (bot == null) continue;
            sc.SendAISpawn(bot.Nickname, bot.transform.position, bot.transform.eulerAngles,
                           IsEnemyBot(bot.Nickname) ? 1 : 0);
        }
        Debug.Log($"[AIManager] Re-sent AI_SPAWN for {_bots.Count} bot(s) to new joiner");
    }

    // ── 호스트: AI 스폰 ───────────────────────────────────────────────────

    void SpawnEscorts(PlayerController leader)
    {
        _localPlayerAircraft  = leader;  // null 허용 — 이륙 시 SetLocalPlayerAircraft()로 업데이트
        _escortLandingStarted = false;

        var carrier = FindFirstObjectByType<CarrierController>();
        _cachedCarrier = carrier;

        var slots = CollectEscortSlots();
        if (slots.Count == 0)
        {
            Debug.LogWarning("[AIManager] No EscortSlot found. Assign Escort Slots Root in Inspector or create child GameObjects with EscortSlot component.");
            return;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            string nick = string.IsNullOrEmpty(slot.nickname) ? $"BOT_E{i + 1}" : slot.nickname;

            Vector3    spawnPos = slot.transform.position;
            Quaternion spawnRot = carrier != null ? carrier.transform.rotation : slot.transform.rotation;

            var go     = BuildAIObject(nick, spawnPos, spawnRot);
            var escort = go.AddComponent<EscortAI>();
            escort.SetBehavior(_escortBehavior);
            escort.Initialize(leader?.transform, slot.formationOffset, spawnedOnDeck: true);

            // AIBotController.Awake() 이후에 추가해야 DisableIfPresent<> 영향을 받지 않음
            go.AddComponent<AircraftBoardingTrigger>();
            go.AddComponent<EscortVFXController>();

            // NetworkAircraft: 전 클라이언트에서 동일 오브젝트를 식별 (BOARD_AIRCRAFT 프로토콜)
            var na = go.GetComponent<NetworkAircraft>() ?? go.AddComponent<NetworkAircraft>();
            na.SetNetworkId(i + 1);  // BOT_E1→1, BOT_E2→2

            _bots.Add(go.GetComponent<AIBotController>());
            NetworkManager.Instance?.socketClient?.SendAISpawn(
                nick, spawnPos, spawnRot.eulerAngles, aiType: 0);

            Debug.Log($"[AIManager] Escort spawned: {nick}  formation={slot.formationOffset}");
        }
    }

    // EscortSlot 자동 수집 — 우선순위:
    //  1. _escortSlotsRoot 자식 (활성 상태만)
    //  2. 씬 전체 EscortSlot 탐색 (폴백)
    System.Collections.Generic.List<EscortSlot> CollectEscortSlots()
    {
        var list = new System.Collections.Generic.List<EscortSlot>();
        if (_escortSlotsRoot != null)
        {
            _escortSlotsRoot.GetComponentsInChildren(false, list);
        }
        if (list.Count == 0)
        {
            list.AddRange(FindObjectsByType<EscortSlot>(FindObjectsSortMode.None));
        }
        // 하이어라키 순서 유지 (sibling index 오름차순)
        list.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
        return list;
    }

    void SpawnEnemies(PlayerController leader)
    {
        for (int i = 0; i < _enemyCount; i++)
        {
            string  nick     = $"BOT_N{i + 1}";
            Vector3 spawnPos = leader.transform.position + new Vector3(
                Random.Range(-300f, 300f), Random.Range(30f, 80f), Random.Range(700f, 1400f));

            var go    = BuildAIObject(nick, spawnPos, leader.transform.rotation);
            var bot   = go.GetComponent<AIBotController>();
            var enemy = go.AddComponent<EnemyAI>();
            enemy.Initialize(leader.transform, leader.nickname);

            _bots.Add(bot);
            NetworkManager.Instance?.socketClient?.SendAISpawn(
                nick, spawnPos, leader.transform.eulerAngles, aiType: 1);

            Debug.Log($"[AIManager] Enemy spawned: {nick}");
        }
    }

    // ── 호스트 승계 ───────────────────────────────────────────────────────

    // 서버가 HOST_CHANGE 패킷을 보낼 때 호출 — 이전 호스트(방장) 퇴장 시
    void HandleHostChange(HostChangePacket p)
    {
        string myNick = NetworkManager.Instance?.socketClient?.myNickname ?? string.Empty;
        if (p.newHostNickname != myNick) return;

        Debug.Log("[AIManager] Host handoff received — taking over escort bots");
        TakeOverRemoteBotsAsHost();
    }

    // 원격 뷰(_remoteBots)로 있던 에스코트 봇을 로컬 AI 제어로 전환
    void TakeOverRemoteBotsAsHost()
    {
        _isHost = true;
        // 로컬 플레이어 조회
        PlayerController localPlayer = null;
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (pc.isLocalPlayer) { localPlayer = pc; break; }
        }

        if (localPlayer == null)
        {
            Debug.LogWarning("[AIManager] TakeOver: local PlayerController not found");
            return;
        }

        _localPlayerAircraft  = localPlayer;
        _cachedCarrier        = FindFirstObjectByType<CarrierController>();
        _escortLandingStarted = false;
        _hasArrestInfo        = false;

        var slots = CollectEscortSlots();

        // 에스코트 봇 닉네임 추출 (적 봇·점령 중 항공기 제외)
        var escortKeys = new System.Collections.Generic.List<string>();
        foreach (var kv in _remoteBots)
        {
            if (!IsAIBot(kv.Key) || IsEnemyBot(kv.Key)) continue;
            escortKeys.Add(kv.Key);
        }

        int idx = 0;
        foreach (string key in escortKeys)
        {
            if (!_remoteBots.TryGetValue(key, out var remotePC) || remotePC == null)
            {
                idx++; continue;
            }

            // 다른 플레이어가 탑승 중인 항공기는 AI로 전환하지 않음
            if (remotePC.GetComponent<AircraftBoardingTrigger>()?.IsOccupied == true)
            {
                idx++; continue;
            }

            _remoteBots.Remove(key);

            // 스냅샷 인터폴레이션 대신 EscortAI가 직접 위치를 제어하도록 전환
            var bot = remotePC.gameObject.AddComponent<AIBotController>();
            bot.SetConfig(_botConfig);
            bot.SetNickname(key);

            var escort = remotePC.gameObject.AddComponent<EscortAI>();
            escort.SetBehavior(_escortBehavior);
            Vector3 offset = idx < slots.Count
                ? slots[idx].formationOffset
                : new Vector3(-32f, -5f, -40f);
            // spawnedOnDeck: false → 즉시 Escorting 상태로 진입 (이미 비행 중)
            escort.Initialize(localPlayer.transform, offset, spawnedOnDeck: false);

            _bots.Add(bot);
            idx++;
        }

        Debug.Log($"[AIManager] Host takeover complete: {idx} escort bots now under local control");
    }

    // ── 원격 클라이언트: AI 패킷 핸들러 ──────────────────────────────────

    void HandleRemoteAISpawn(AISpawnPacket p)
    {
        if (_isHost) return;
        if (_remoteBots.ContainsKey(p.nickname)) return;

        var pos = new Vector3(p.posX, p.posY, p.posZ);
        var rot = Quaternion.Euler(p.rotX, p.rotY, p.rotZ);

        GameObject go = _aiPrefab != null
            ? Instantiate(_aiPrefab, pos, rot)
            : CreateFallbackAircraft(pos, rot, isEnemy: p.aiType == 1);
        go.name = p.nickname;

        var pc = go.GetComponent<PlayerController>();
        if (pc == null) pc = go.AddComponent<PlayerController>();
        pc.nickname      = p.nickname;
        pc.isLocalPlayer = false;
        pc.IsFlying      = true;
        pc.AddSnapshot(pos, rot, isMove: true);

        // 에스코트 봇(BOT_E*): 탑승 가능 + NetworkAircraft → BOARD_AIRCRAFT 프로토콜 연동
        if (p.aiType == 0 && IsEscortBot(p.nickname))
        {
            int id = ParseBotNetworkId(p.nickname);
            if (id >= 0)
            {
                var na = go.GetComponent<NetworkAircraft>() ?? go.AddComponent<NetworkAircraft>();
                na.SetNetworkId(id);
            }
            if (go.GetComponent<AircraftBoardingTrigger>() == null)
                go.AddComponent<AircraftBoardingTrigger>();
            pc.enabled = true;  // AircraftBoardingTrigger.Awake()가 PC를 비활성화하므로 재활성화
        }

        _remoteBots[p.nickname] = pc;
        Debug.Log($"[AIManager] Remote AI view: {p.nickname} (aiType={p.aiType})");
    }

    void HandleRemoteAIDespawn(string nickname)
    {
        if (_remoteBots.TryGetValue(nickname, out var pc))
        {
            _remoteBots.Remove(nickname);
            if (pc != null) Destroy(pc.gameObject);
        }
    }

    void HandleRemoteAIMove(AIMovePacket p)
    {
        if (!_remoteBots.TryGetValue(p.nickname, out var pc) || pc == null) return;
        pc.AddSnapshot(
            new Vector3(p.posX, p.posY, p.posZ),
            Quaternion.Euler(p.rotX, p.rotY, p.rotZ),
            isMove: p.isMove);
    }

    // ── 유틸리티 ──────────────────────────────────────────────────────────

    // 플레이어 발진 후 호출 — 적 AI 전투 시작 + 에스코트 순차 발진
    public void EnableAICombat()
    {
        _escortLandingStarted = false;
        _hasArrestInfo        = false;
        // 적 전투 활성
        foreach (var bot in _bots)
            bot?.GetComponent<EnemyAI>()?.EnableCombat();

        // 에스코트 순차 발진 (Slot_01 → Slot_02 → ... spawn 순서대로, 4초 간격)
        var escorts = new System.Collections.Generic.List<EscortAI>();
        foreach (var bot in _bots)
        {
            if (bot == null) continue;
            var escort = bot.GetComponent<EscortAI>();
            if (escort != null) escorts.Add(escort);
        }

        for (int i = 0; i < escorts.Count; i++)
        {
            var e = escorts[i];
            if (i == 0) e.BeginLaunchSequence();
            else        StartCoroutine(DelayedLaunch(e, i * 4f));
        }
    }

    System.Collections.IEnumerator DelayedLaunch(EscortAI escort, float delay)
    {
        yield return new WaitForSeconds(delay);
        escort.BeginLaunchSequence();
    }

    // 어레스팅 와이어로 플레이어 정지 시 호출 — 경로만 저장 (착함 트리거는 하차 시)
    public void StoreArrestInfo(Vector3 approachDir, Vector3 wirePos)
    {
        _arrestedApproachDir = approachDir;
        _arrestedWirePos     = wirePos;
        _hasArrestInfo       = true;
    }

    // GameModeManager.ExitFlight에서 착함 트리거 판단용
    public bool HasArrestInfo => _hasArrestInfo;

    // 플레이어 F키 하차(ExitFlight) 시 호출 — 저장된 경로로 에스코트 순차 착함 시작.
    // 와이어 체결 정보가 있을 때만 동작하며, 폴백 코루틴이 이미 실행 중이더라도 덮어씀.
    public void BeginEscortLandingAfterDismount()
    {
        if (!_isHost || !_hasArrestInfo) return;
        // 폴백 코루틴(UpdateEscorting leaderStopTimer)이 먼저 시작됐을 수 있으므로
        // arrest info가 있을 때는 기존 착함 코루틴을 취소하고 올바른 경로로 재시작한다.
        if (_landingCoroutine != null) { StopCoroutine(_landingCoroutine); _landingCoroutine = null; }
        _escortLandingStarted = true;
        var carrierTr = _cachedCarrier?.transform ?? FindFirstObjectByType<CarrierController>()?.transform;
        Debug.Log($"[AIManager] BeginEscortLandingAfterDismount: carrier={(carrierTr != null ? carrierTr.name : "null")}  dir={_arrestedApproachDir}  wirePos={_arrestedWirePos}");
        _landingCoroutine = StartCoroutine(EscortLandingCoroutine(carrierTr, _arrestedApproachDir, _arrestedWirePos, true));
    }

    // 플레이어 어레스팅 와이어 체결 직후 호출 — 에스코트 즉시 자유비행 전환
    public void BeginEscortFreeFlightNow()
    {
        if (!_isHost) return;
        foreach (var bot in _bots)
        {
            if (bot == null) continue;
            bot.GetComponent<EscortAI>()?.ForceFreeFlight();
        }
    }

    // 플레이어 항모 착함 후 호출 (폴백 경로 — 와이어 없이 착함, 또는 UpdateEscorting 폴백)
    public void BeginEscortLanding(Transform carrier)
    {
        Debug.Log($"[AIManager] BeginEscortLanding called  isHost={_isHost}  landingStarted={_escortLandingStarted}  carrier={(carrier != null ? carrier.name : "null")}");
        if (!_isHost || _escortLandingStarted) return;
        _escortLandingStarted = true;
        _landingCoroutine = StartCoroutine(EscortLandingCoroutine(carrier, Vector3.zero, Vector3.zero, false));
    }

    System.Collections.IEnumerator EscortLandingCoroutine(
        Transform carrier, Vector3 approachDir, Vector3 wirePos, bool hasPath)
    {
        var escorts = new System.Collections.Generic.List<EscortAI>();
        foreach (var bot in _bots)
        {
            if (bot == null) continue;
            var escort = bot.GetComponent<EscortAI>();
            if (escort != null) escorts.Add(escort);
        }

        // 착함 슬롯 목록 수집 — 에스코트 i번은 Slot_0(i+1)에 정지
        var slots = CollectEscortSlots();
        Debug.Log($"[AIManager] EscortLandingCoroutine: escorts={escorts.Count}  slots={slots.Count}  hasPath={hasPath}");

        // 슬롯 순서(1번→2번→...)로 순차 착함 시작.
        // 발진 중(Launching)인 에스코트는 발진 완료까지 대기 후 착함.
        for (int i = 0; i < escorts.Count; i++)
        {
            var escort = escorts[i];

            while (escort != null && escort.IsLaunching) yield return null;
            if (escort == null) continue;

            if (hasPath)
            {
                // 에스코트 i번 → Slot_0(i+1) 위치에 착함. 슬롯 부족 시 wirePos 폴백
                Vector3 slotPos = i < slots.Count ? slots[i].transform.position : wirePos;
                Debug.Log($"[AIManager] → BeginLandingWithPath '{escort.name}' → Slot_{i + 1}  pos={slotPos}");
                escort.BeginLandingWithPath(carrier, approachDir, slotPos);
            }
            else
            {
                Debug.Log($"[AIManager] → BeginLanding '{escort.name}' (slot {i + 1})");
                escort.BeginLanding(carrier);
            }

            if (i < escorts.Count - 1)
                yield return new WaitForSeconds(_escortLandingStagger);
        }
    }

    // ── 에스코트 AI 기체 탑승 전환 ─────────────────────────────────────────
    // newPlayerPC: 플레이어가 탑승할 에스코트 AI 기체
    // oldPlayerPC: 기존 플레이어 기체 (에스코트 AI로 전환됨)
    public void TakeOverEscortBot(PlayerController newPlayerPC, PlayerController oldPlayerPC)
    {
        if (!_isHost) return;

        string playerNickname = oldPlayerPC.nickname;

        // 1. 선택된 에스코트의 AI 컴포넌트 정보 저장 후 제거
        var escortAI  = newPlayerPC.GetComponent<EscortAI>();
        var escortBot = newPlayerPC.GetComponent<AIBotController>();
        string  botNick           = escortBot?.Nickname ?? "BOT_E1";
        Vector3 botFormationOffset = escortAI != null ? escortAI.FormationOffset : new Vector3(-32f, -5f, -40f);

        if (escortAI  != null) { escortAI.StopAllCoroutines();  Destroy(escortAI);  }
        if (escortBot != null) { _bots.Remove(escortBot); Destroy(escortBot); }

        // 2. 해당 기체를 로컬 플레이어 항공기로 전환
        //    BoardingTrigger 유지 — 하차 후 재탑승 가능 (IsOccupied로 다른 플레이어 차단)
        newPlayerPC.nickname      = playerNickname;
        newPlayerPC.isLocalPlayer = true;

        // 3. 기존 플레이어 기체를 에스코트 AI로 전환
        //    새 닉네임(BOT_SW_*) 사용 → 원격의 NetworkAircraft ID 충돌 방지
        string newEscortNick = $"BOT_SW_{playerNickname}";
        var newBot = oldPlayerPC.gameObject.AddComponent<AIBotController>();
        newBot.SetNickname(newEscortNick);
        newBot.PositionOverride = true;

        var newEscort = oldPlayerPC.gameObject.AddComponent<EscortAI>();
        newEscort.SetBehavior(_escortBehavior);
        newEscort.Initialize(newPlayerPC.transform, botFormationOffset, spawnedOnDeck: true);
        _bots.Add(newBot);

        // 4. 남은 에스코트 봇의 리더를 새 플레이어 기체로 업데이트
        foreach (var bot in _bots)
        {
            if (bot == null || bot == newBot) continue;
            bot.GetComponent<EscortAI>()?.UpdateLeader(newPlayerPC.transform);
        }

        // 5. 동기화: 기존 에스코트 뷰는 원격에서 BOARD_AIRCRAFT로 플레이어 항공기가 되므로
        //    AI_DESPAWN 불필요. 이전 플레이어 기체(새 에스코트)만 새 닉네임으로 알림.
        var sc = NetworkManager.Instance?.socketClient;
        sc?.SendAISpawn(newEscortNick, oldPlayerPC.transform.position,
                        oldPlayerPC.transform.eulerAngles, aiType: 0);

        _localPlayerAircraft = newPlayerPC;
        Debug.Log($"[AIManager] Escort takeover: '{botNick}' → player, old aircraft → '{newEscortNick}' escort");
    }

    /// <summary>
    /// 지상 캐릭터 상태(기존 항공기 없음)에서 에스코트 봇에 탑승.
    /// AI 컴포넌트를 제거하고 플레이어 항공기로 전환한다.
    /// 원격 클라이언트의 에스코트 뷰는 BOARD_AIRCRAFT 프로토콜로 인계되므로 AI_DESPAWN 불필요.
    /// </summary>
    public void BoardEscortFromGround(PlayerController escortPC)
    {
        if (!_isHost) return;

        var escortAI = escortPC.GetComponent<EscortAI>();
        var bot      = escortPC.GetComponent<AIBotController>();
        string nick  = bot?.Nickname ?? escortPC.name;

        if (escortAI != null) { escortAI.StopAllCoroutines(); Destroy(escortAI); }
        if (bot != null)      { _bots.Remove(bot);            Destroy(bot);      }

        // BoardingTrigger 유지 — 하차 후 재탑승 가능
        string myNick = NetworkManager.Instance?.socketClient?.myNickname ?? "";
        escortPC.nickname      = myNick;
        escortPC.isLocalPlayer = true;

        Debug.Log($"[AIManager] BoardEscortFromGround: '{nick}' AI 제거, 플레이어 탑승");
    }

    // BOT_E1 → 1, BOT_E2 → 2. 패턴 불일치 시 -1
    static int ParseBotNetworkId(string nick)
    {
        const string prefix = "BOT_E";
        if (nick.StartsWith(prefix) && int.TryParse(nick.Substring(prefix.Length), out int id))
            return id;
        return -1;
    }

    public static bool IsAIBot(string nickname)    => nickname.StartsWith("BOT_");
    public static bool IsEnemyBot(string nickname) => nickname.StartsWith("BOT_N");
    public static bool IsEscortBot(string nickname)=> nickname.StartsWith("BOT_E");

    public AIBotController GetBot(string nickname)
    {
        foreach (var b in _bots)
            if (b != null && b.Nickname == nickname) return b;
        return null;
    }

    // ── AI 오브젝트 빌드 (호스트) ─────────────────────────────────────────

    GameObject BuildAIObject(string nick, Vector3 pos, Quaternion rot)
    {
        GameObject go = _aiPrefab != null
            ? Instantiate(_aiPrefab, pos, rot)
            : CreateFallbackAircraft(pos, rot, isEnemy: nick.StartsWith("BOT_N"));
        go.name = nick;

        if (go.GetComponent<PlayerController>() == null)
            go.AddComponent<PlayerController>();

        var bot = go.AddComponent<AIBotController>();
        bot.SetConfig(_botConfig);   // 슬롯 비어있으면 AIBotController가 폴백 사용
        bot.SetNickname(nick);
        return go;
    }

    static GameObject CreateFallbackAircraft(Vector3 pos, Quaternion rot, bool isEnemy)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = new Vector3(1.2f, 0.28f, 3.8f);
        Object.Destroy(go.GetComponent<CapsuleCollider>());

        // 셰이더를 찾지 못하면 new Material(null) → 마젠타 발생하므로 null 체크
        var shader = Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                  ?? Shader.Find("Standard");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.color = isEnemy ? new Color(0.80f, 0.12f, 0.12f) : new Color(0.15f, 0.60f, 1.00f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }
        return go;
    }
}

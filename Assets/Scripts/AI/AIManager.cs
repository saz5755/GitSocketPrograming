using System.Collections.Generic;
using UnityEngine;

// AI 전투기 관리자.
// ┌ 호스트 클라이언트: AI 오브젝트를 직접 생성·제어하고 위치를 UDP 브로드캐스트.
// └ 원격 클라이언트: AI_SPAWN/AI_MOVE 패킷을 받아 PlayerController 뷰를 갱신.
//
// 씬에 이 컴포넌트를 가진 GameObject를 배치하고 Inspector에서 설정하세요.
// _spawnEscorts / _spawnEnemies 를 체크한 클라이언트가 호스트 역할을 합니다.
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
        sc.OnAISpawn    += HandleRemoteAISpawn;
        sc.OnAIDespawn  += HandleRemoteAIDespawn;
        sc.OnAIMove     += HandleRemoteAIMove;
        sc.OnHostChange += HandleHostChange;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;
        sc.OnAISpawn    -= HandleRemoteAISpawn;
        sc.OnAIDespawn  -= HandleRemoteAIDespawn;
        sc.OnAIMove     -= HandleRemoteAIMove;
        sc.OnHostChange -= HandleHostChange;
    }

    // ── 호스트 초기화 (PlayerManager.CreateLocalPlayer에서 호출) ───────────

    public void TryInitializeAsHost(PlayerController localPlayer)
    {
        if (_initialized) return;
        _initialized = true;
        _isHost = true;

        if (_spawnEscorts) SpawnEscorts(localPlayer);
        if (_spawnEnemies) SpawnEnemies(localPlayer);
    }

    // ── 호스트: AI 스폰 ───────────────────────────────────────────────────

    void SpawnEscorts(PlayerController leader)
    {
        _localPlayerAircraft  = leader;
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
            escort.Initialize(leader.transform, slot.formationOffset, spawnedOnDeck: true);

            // AIBotController.Awake() 이후에 추가해야 DisableIfPresent<> 영향을 받지 않음
            go.AddComponent<AircraftBoardingTrigger>();
            go.AddComponent<EscortVFXController>();

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

        // 에스코트 봇 닉네임만 추출 (순회 중 딕셔너리 수정 방지)
        var escortKeys = new System.Collections.Generic.List<string>();
        foreach (var kv in _remoteBots)
            if (IsEscortBot(kv.Key)) escortKeys.Add(kv.Key);

        int idx = 0;
        foreach (string key in escortKeys)
        {
            if (!_remoteBots.TryGetValue(key, out var remotePC) || remotePC == null)
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
        if (_isHost) return;              // 호스트는 이미 로컬에서 생성
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

        _remoteBots[p.nickname] = pc;
        Debug.Log($"[AIManager] Remote AI view created: {p.nickname}");
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

        // 탑승한 기체의 BoardingTrigger 제거 — 플레이어 기체에는 불필요
        var bt = newPlayerPC.GetComponent<AircraftBoardingTrigger>();
        if (bt != null) Destroy(bt);

        // 2. 해당 기체를 로컬 플레이어 항공기로 전환
        newPlayerPC.nickname      = playerNickname;
        newPlayerPC.isLocalPlayer = true;

        // 3. 기존 플레이어 기체를 에스코트 AI로 전환
        // AddComponent<AIBotController>() Awake가 AircraftBoardingTrigger를 자동 비활성화함
        var newBot = oldPlayerPC.gameObject.AddComponent<AIBotController>();
        newBot.SetNickname(botNick);
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

        // 5. 멀티플레이어 동기화: 기존 AI 제거 알림 + 새 AI(이전 플레이어 기체) 스폰 알림
        var sc = NetworkManager.Instance?.socketClient;
        sc?.SendAIDespawn(botNick);
        sc?.SendAISpawn(botNick, oldPlayerPC.transform.position,
                        oldPlayerPC.transform.eulerAngles, aiType: 0);

        _localPlayerAircraft = newPlayerPC;
        Debug.Log($"[AIManager] Escort takeover: '{botNick}' → player, player aircraft → '{botNick}' AI");
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

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.color = isEnemy ? new Color(0.80f, 0.12f, 0.12f) : new Color(0.15f, 0.60f, 1.00f);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }
}

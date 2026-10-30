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
    [Tooltip("격추된 적기가 같은 콜사인으로 다시 투입되기까지의 시간 (초)")]
    [SerializeField] float _enemyRespawnDelay = 40f;
    [Tooltip("적기 숙련도 (방 모드 에셋이 있으면 그 값 우선)")]
    [SerializeField] EnemyAI.Skill _enemySkill = EnemyAI.Skill.Veteran;
    [Tooltip("AI 기체 내구도 (플레이어 기체 maxHP 와 같은 스케일)")]
    [SerializeField] float _botMaxHP = 100f;

    [Header("에스코트 착함 타이밍")]
    [Tooltip("에스코트 간 착함 시작 시차 (초) — 플레이어 F키 하차 후 1번기부터 이 간격으로 순차 착함")]
    [SerializeField] float _escortLandingStagger = 5f;

    [Header("조종사 사망 시 에스코트")]
    [Tooltip("리더 사망 후 항모 상공 대기 시간 (초) — 이 안에 재출격하지 않으면 패턴 비행으로 착함")]
    [SerializeField] float _marshalHoldTime = 45f;
    Coroutine _recoveryCoroutine;

    bool      _isHost;
    bool      _initialized;
    bool      _escortLandingStarted;
    bool      _enemiesSpawned;     // 적 AI 중복 스폰 방지 플래그
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
        if (GetComponent<AirshowController>() == null) gameObject.AddComponent<AirshowController>();   // 에어쇼 편대장 지휘 (v4.4)
        if (GetComponent<AerialRefuelSystem>() == null) gameObject.AddComponent<AerialRefuelSystem>(); // 공중급유 (로컬 조종사)
    }

    void Start()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;

        // ENTER_ROOM_RESULT는 씬 전환 전(로비)에 수신되므로 OnEnterRoomResult 이벤트를 놓칠 수 있음.
        // SocketClient가 수신 스레드에서 즉시 저장한 roomHostNickname을 폴백으로 읽는다.
        if (!string.IsNullOrEmpty(sc.roomHostNickname))
            _networkHostNickname = sc.roomHostNickname;

        sc.OnAISpawn          += HandleRemoteAISpawn;
        sc.OnAIDespawn        += HandleRemoteAIDespawn;
        sc.OnAIMove           += HandleRemoteAIMove;
        sc.OnHostChange       += HandleHostChange;
        sc.OnEnterRoomResult  += HandleEnterRoomResult;
        sc.OnSpawn            += HandleRemotePlayerJoin;
        sc.OnAircraftPoolSync += HandleAircraftPoolSync;
        sc.OnArrestEvent      += HandleArrestEvent;
        sc.OnPlayerKilled     += HandleRemoteBotKilled;
        sc.OnRoomSetting      += HandleRoomSetting;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;
        sc.OnAISpawn          -= HandleRemoteAISpawn;
        sc.OnAIDespawn        -= HandleRemoteAIDespawn;
        sc.OnAIMove           -= HandleRemoteAIMove;
        sc.OnHostChange       -= HandleHostChange;
        sc.OnEnterRoomResult  -= HandleEnterRoomResult;
        sc.OnSpawn            -= HandleRemotePlayerJoin;
        sc.OnAircraftPoolSync -= HandleAircraftPoolSync;
        sc.OnArrestEvent      -= HandleArrestEvent;
        sc.OnPlayerKilled     -= HandleRemoteBotKilled;
        sc.OnRoomSetting      -= HandleRoomSetting;
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
        // 방 모드가 있으면 씬 Inspector 기본값을 덮어쓴다 (호스트가 AI 를 스폰하므로 게스트는 AI_SPAWN 으로 자동 동기화)
        var mode = RoomModeSO.Current;
        if (mode != null)
        {
            _spawnEscorts = mode.spawnEscorts;
            _spawnEnemies = mode.spawnEnemies;
        }
        // 에스코트: 갑판에서 대기하므로 localPlayer=null도 허용
        if (_spawnEscorts) SpawnEscorts(localPlayer);
        // 급유기: 두 모드 모두 — 연료가 떨어지면 공중급유로 계속 비행
        SpawnTanker();
        // 적 AI: 플레이어 항공기 위치가 필요하므로 SetLocalPlayerAircraft() 시점에 스폰
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

        // 적 AI: 플레이어 이륙 시점에 스폰 (스폰 위치가 리더 항공기 기준)
        if (_isHost && _spawnEnemies && !_enemiesSpawned && pc != null)
        {
            SpawnEnemies(pc);
            _enemiesSpawned = true;
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

    // 입장 시 서버 풀에서 AI 봇 항목을 일괄 처리 — 호스트는 이미 관리 중이므로 무시
    void HandleAircraftPoolSync(AircraftPoolSyncPacket p)
    {
        foreach (var entry in p.aircraft)
        {
            if (entry.aiType < 0) continue;  // 플레이어 기체 — PlayerManager가 처리
            HandleRemoteAISpawn(new AISpawnPacket
            {
                type     = PacketType.AI_SPAWN,
                nickname = entry.nickname,
                posX = entry.posX, posY = entry.posY, posZ = entry.posZ,
                rotX = entry.rotX, rotY = entry.rotY, rotZ = entry.rotZ,
                aiType   = entry.aiType
            });
        }
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
                           IsEnemyBot(bot.Nickname) ? 1 : IsTankerBot(bot.Nickname) ? 2 : 0);
        }
        DevLog.Log($"[AIManager] Re-sent AI_SPAWN for {_bots.Count} bot(s) to new joiner");
    }

    // ── 호스트: AI 스폰 ───────────────────────────────────────────────────

    void SpawnEscorts(PlayerController leader)
    {
        _localPlayerAircraft  = leader;  // null 허용 — 이륙 시 SetLocalPlayerAircraft()로 업데이트
        _escortLandingStarted = false;

        var carrier = FindAnyObjectByType<CarrierController>();
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

            DevLog.Log($"[AIManager] Escort spawned: {nick}  formation={slot.formationOffset}");
        }
    }

    // ── 급유기 (BOT_T1) ─────────────────────────────────────────────────────
    const string TankerNick = "BOT_T1";

    void SpawnTanker()
    {
        if (GetBot(TankerNick) != null) return;
        float seaY = FindAnyObjectByType<OceanSurface>()?.seaLevel ?? 0f;
        Vector3 pos  = TankerAI.PointAt(0f, seaY);
        Vector3 next = TankerAI.PointAt(50f, seaY);
        var rot = Quaternion.LookRotation((next - pos).normalized, Vector3.up);

        var go  = BuildAIObject(TankerNick, pos, rot);
        var bot = go.GetComponent<AIBotController>();
        go.AddComponent<TankerAI>().Initialize(seaY, 0f);
        TankerDrogueRig.Ensure(go);
        _bots.Add(bot);
        NetworkManager.Instance?.socketClient?.SendAISpawn(TankerNick, pos, rot.eulerAngles, aiType: 2);
        DevLog.Log($"[AIManager] Tanker spawned on track at {pos}");
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
            list.AddRange(FindObjectsByType<EscortSlot>(FindObjectsInactive.Exclude));
        }
        // 하이어라키 순서 유지 (sibling index 오름차순)
        list.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
        return list;
    }

    /// <summary>적기 숙련도 — 방장이 ESC 메뉴에서 정한 방 설정 > 방 모드 에셋 > Inspector</summary>
    public EnemyAI.Skill CurrentEnemySkill
    {
        get
        {
            int s = NetworkManager.Instance?.socketClient?.roomEnemySkill ?? -1;
            if (s >= 0) return (EnemyAI.Skill)Mathf.Clamp(s, 0, 2);
            return RoomModeSO.Current != null ? RoomModeSO.Current.enemySkill : _enemySkill;
        }
    }

    // 방장이 숙련도를 바꾸면 이미 비행 중인 적기에도 즉시 적용 (AI 는 호스트만 시뮬레이션)
    void HandleRoomSetting(RoomSettingPacket p)
    {
        if (!_isHost) return;
        foreach (var bot in _bots)
            bot?.GetComponent<EnemyAI>()?.SetSkill(CurrentEnemySkill);
        DevLog.Log($"[AIManager] Enemy skill → {CurrentEnemySkill}");
    }

    int EnemyCount => RoomModeSO.Current != null ? Mathf.Clamp(RoomModeSO.Current.enemyCount, 1, 4) : _enemyCount;

    void SpawnEnemies(PlayerController leader)
    {
        for (int i = 0; i < EnemyCount; i++)
            SpawnEnemy($"BOT_N{i + 1}", leader);
    }

    void SpawnEnemy(string nick, PlayerController leader)
    {
        // BVR 교전부터 시작하도록 리더 전방 5~7 km, 고도 600~1100 m 에 편대로 투입 (기수는 리더 쪽)
        Vector3 fwd = leader.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        int idx = int.TryParse(nick.Substring(5), out var n) ? n - 1 : 0;
        Vector3 spawnPos = leader.transform.position + fwd * Random.Range(5000f, 7000f)
                         + right * ((idx - (EnemyCount - 1) * 0.5f) * 700f + Random.Range(-150f, 150f));
        spawnPos.y = (FindAnyObjectByType<OceanSurface>()?.seaLevel ?? 0f) + Random.Range(600f, 1100f);
        var spawnRot = Quaternion.LookRotation(-fwd, Vector3.up);

        var go    = BuildAIObject(nick, spawnPos, spawnRot);
        var bot   = go.GetComponent<AIBotController>();
        var enemy = go.AddComponent<EnemyAI>();
        enemy.SetSkill(CurrentEnemySkill);
        enemy.Initialize(leader.transform, leader.nickname);

        _bots.Add(bot);
        _botHP[nick] = _botMaxHP;
        NetworkManager.Instance?.socketClient?.SendAISpawn(
            nick, spawnPos, spawnRot.eulerAngles, aiType: 1);

        DevLog.Log($"[AIManager] Enemy spawned: {nick}");
    }

    // ── 피격·격추 (호스트 권한) ──────────────────────────────────────────────
    // 에스코트(BOT_E*)는 탑승 가능한 공유 기체라 아직 피해를 받지 않는다 (아군 오사 처리는 별도 과제).
    readonly Dictionary<string, float> _botHP = new();

    /// <summary>AI 기체 잔여 내구도 비율 (0..1, 호스트 전용 — 모르면 1)</summary>
    public float GetBotHP01(string nick)
        => _botHP.TryGetValue(nick, out var hp) ? Mathf.Clamp01(hp / Mathf.Max(1f, _botMaxHP)) : 1f;

    public void DamageBot(string nick, float amount, string attacker, string weapon)
    {
        if (!_isHost || IsEscortBot(nick) || IsTankerBot(nick)) return;   // 아군 공유 기체·급유기는 피해 없음
        var bot = GetBot(nick);
        if (bot == null) return;

        float hp = (_botHP.TryGetValue(nick, out var cur) ? cur : _botMaxHP) - amount;
        _botHP[nick] = hp;
        if (hp <= 0f) KillBot(bot, attacker, weapon);
    }

    void KillBot(AIBotController bot, string attacker, string weapon)
    {
        string  nick = bot.Nickname;
        Vector3 pos  = bot.transform.position;
        Vector3 vel  = bot.transform.forward * Mathf.Max(bot.Speed, 40f);
        _botHP.Remove(nick);
        _bots.Remove(bot);

        // 사망 보고 → 원격은 자기 뷰를 잔해로 전환, 이어서 AI_DESPAWN 으로 서버 풀 정리
        var sc = NetworkManager.Instance?.socketClient;
        sc?.SendPlayerKilled(nick, attacker, weapon, PilotStatus.CauseShotDown, pos, -1);
        sc?.SendAIDespawn(nick);

        var go = bot.gameObject;
        StripAIComponents(go);
        ExplosionEffect.Spawn(pos);
        WreckController.Begin(go, vel, onFire: true, sendMoves: false,
            onImpact: _ => Destroy(go, 8f));

        if (IsEnemyBot(nick) && _spawnEnemies)
            StartCoroutine(RespawnEnemyLater(nick));
        DevLog.Log($"[AIManager] {nick} shot down by {attacker} ({weapon})");
    }

    System.Collections.IEnumerator RespawnEnemyLater(string nick)
    {
        yield return new WaitForSeconds(_enemyRespawnDelay);
        if (!_isHost || GetBot(nick) != null) yield break;
        // 리더(호스트 기체)가 없거나 비행 중이 아니면 다음 출격까지 대기
        while (_localPlayerAircraft == null || GameModeManager.Instance == null || !GameModeManager.Instance.IsFlying)
            yield return new WaitForSeconds(2f);
        SpawnEnemy(nick, _localPlayerAircraft);
        GetBot(nick)?.GetComponent<EnemyAI>()?.EnableCombat();
    }

    // 원격 클라이언트: 호스트가 보고한 AI 격추 → 뷰를 잔해로 전환 (이후 AI_DESPAWN 은 무시됨)
    void HandleRemoteBotKilled(PlayerKilledPacket p)
    {
        if (_isHost || !IsAIBot(p.victim)) return;
        if (!_remoteBots.TryGetValue(p.victim, out var pc)) return;
        _remoteBots.Remove(p.victim);
        if (pc == null) return;

        var go = pc.gameObject;
        StripAIComponents(go);
        ExplosionEffect.Spawn(new Vector3(p.posX, p.posY, p.posZ));
        WreckController.Begin(go, go.transform.forward * 120f, onFire: true, sendMoves: false,
            onImpact: _ => Destroy(go, 8f));
    }

    static void StripAIComponents(GameObject go)
    {
        foreach (var c in go.GetComponents<MonoBehaviour>())
            if (c is AIBotController || c is EnemyAI || c is EscortAI || c is AircraftBoardingTrigger)
                Destroy(c);
        var pc = go.GetComponent<PlayerController>();
        // IsFlying 해제 — 잔해가 락온·리드 표시 대상에서 빠지도록
        if (pc != null) { pc.ClearSnapshots(); pc.IsFlying = false; pc.enabled = false; }
    }

    /// <summary>공유 기체(networkId)의 갑판 주기 위치 — 에스코트 슬롯 i 가 networkId i+1.</summary>
    public bool TryGetDeckPose(int networkId, out Vector3 pos, out Quaternion rot)
    {
        var slots = CollectEscortSlots();
        int i = networkId - 1;
        if (i >= 0 && i < slots.Count)
        {
            pos = slots[i].transform.position;
            rot = slots[i].transform.rotation;
            return true;
        }
        pos = default; rot = Quaternion.identity;
        return false;
    }

    // ── 호스트 승계 ───────────────────────────────────────────────────────

    // 서버가 HOST_CHANGE 패킷을 보낼 때 호출 — 이전 호스트(방장) 퇴장 시
    void HandleHostChange(HostChangePacket p)
    {
        string myNick = NetworkManager.Instance?.socketClient?.myNickname ?? string.Empty;
        if (p.newHostNickname != myNick) return;

        DevLog.Log("[AIManager] Host handoff received — taking over escort bots");
        TakeOverRemoteBotsAsHost();
    }

    // 원격 뷰(_remoteBots)로 있던 에스코트 봇을 로컬 AI 제어로 전환
    void TakeOverRemoteBotsAsHost()
    {
        _isHost = true;
        // 로컬 플레이어 조회
        PlayerController localPlayer = null;
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude))
        {
            if (pc.isLocalPlayer) { localPlayer = pc; break; }
        }

        // 적기는 지상에 있어도(기체 미탑승) 넘겨받는다 — 안 그러면 원격 뷰가 공중에 멈추고 숙련도 변경도 적용될 대상이 없다
        TakeOverRemoteEnemies(localPlayer);

        if (localPlayer == null)
        {
            Debug.LogWarning("[AIManager] TakeOver: local PlayerController not found");
            return;
        }

        _localPlayerAircraft  = localPlayer;
        _cachedCarrier        = FindAnyObjectByType<CarrierController>();
        _escortLandingStarted = false;
        _hasArrestInfo        = false;

        var slots = CollectEscortSlots();

        // 에스코트 봇 닉네임 추출 (적 봇·점령 중 항공기 제외)
        var escortKeys = new System.Collections.Generic.List<string>();
        foreach (var kv in _remoteBots)
        {
            if (!IsAIBot(kv.Key) || IsEnemyBot(kv.Key)) continue;
            if (IsTankerBot(kv.Key)) { TakeOverTanker(kv.Key); continue; }
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

        DevLog.Log($"[AIManager] Host takeover complete: {idx} escort bots now under local control");
    }

    // 이전 방장이 몰던 급유기를 이어받아 궤도 비행을 계속한다
    void TakeOverTanker(string key)
    {
        if (!_remoteBots.TryGetValue(key, out var pc) || pc == null) return;
        var bot = pc.gameObject.AddComponent<AIBotController>();
        bot.SetConfig(_botConfig);
        bot.SetNickname(key);
        pc.gameObject.AddComponent<TankerAI>().InitializeFromCurrent(FindAnyObjectByType<OceanSurface>()?.seaLevel ?? 0f);
        _bots.Add(bot);
        DevLog.Log($"[AIManager] Host takeover: tanker {key}");
    }

    // 이전 방장이 시뮬레이션하던 적기(BOT_N*) 원격 뷰를 로컬 EnemyAI 제어로 전환.
    // 이전 방장 쪽에서 재투입 대기 중이던 적기는 여기서 재투입 코루틴을 다시 건다.
    void TakeOverRemoteEnemies(PlayerController localPlayer)
    {
        // 게스트였던 클라이언트는 방 모드 설정을 적용한 적이 없다 — 여기서 적용
        var mode = RoomModeSO.Current;
        if (mode != null) { _spawnEscorts = mode.spawnEscorts; _spawnEnemies = mode.spawnEnemies; }
        if (!_spawnEnemies) return;
        var keys = new List<string>();
        foreach (var kv in _remoteBots) if (IsEnemyBot(kv.Key)) keys.Add(kv.Key);

        foreach (string nick in keys)
        {
            var pc = _remoteBots[nick];
            _remoteBots.Remove(nick);
            if (pc == null || !pc.IsFlying) continue;   // 격추된 잔해

            Vector3 vel = pc.transform.forward * 80f;
            pc.ClearSnapshots();
            var bot = pc.gameObject.AddComponent<AIBotController>();
            bot.SetConfig(_botConfig);
            bot.SetNickname(nick);
            bot.SetInitialSpeed(vel.magnitude);   // 0 에서 다시 가속하면 공중에서 멈칫한다

            var enemy = pc.gameObject.AddComponent<EnemyAI>();
            enemy.SetSkill(CurrentEnemySkill);
            enemy.Initialize(localPlayer != null ? localPlayer.transform : null, localPlayer != null ? localPlayer.nickname : "");
            enemy.EnableCombat();

            _bots.Add(bot);
            _botHP[nick] = _botMaxHP;   // 이전 방장의 잔여 내구도는 전달되지 않는다
        }

        // 투입 수만큼 없으면 (격추 후 재투입 대기 중이었다면) 재투입 예약
        for (int i = 0; i < EnemyCount; i++)
        {
            string nick = $"BOT_N{i + 1}";
            if (GetBot(nick) == null) StartCoroutine(RespawnEnemyLater(nick));
        }
        _enemiesSpawned = true;   // 이륙 시 SpawnEnemies 가 같은 콜사인을 중복 생성하지 않게
        DevLog.Log($"[AIManager] Host takeover: {keys.Count} enemy view(s) → local EnemyAI ({CurrentEnemySkill})");
    }

    // ── 호스트: AI 위치 배치 송신 (v4.4) ─────────────────────────────────
    // 봇마다 따로 보내면 패킷마다 지연이 달라 게스트 화면에서 밀집 편대가 서로 출렁인다.
    // 한 틱의 AI 전원을 같은 송신 시각으로 한 패킷에 묶는다 — 패킷 수도 봇 수 → 1 로 준다.
    readonly byte[] _batchBuf = new byte[1400];
    readonly Dictionary<AIBotController, PlayerController> _botPC = new();
    float _batchTimer;
    const int BatchSplitAt = 1200;   // MTU 여유 — 넘으면 같은 시각으로 패킷을 나눈다

    void Update()
    {
        if (!_isHost || _bots.Count == 0) return;
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;

        float interval = _botConfig != null ? Mathf.Max(0.02f, _botConfig.netUpdateInterval) : 0.05f;
        _batchTimer += Time.unscaledDeltaTime;
        if (_batchTimer < interval) return;
        _batchTimer = Mathf.Min(_batchTimer - interval, interval);

        uint now = NetClock.FrameMs;
        int n = BinaryProtocol.BeginAIBatch(_batchBuf, now);
        foreach (var bot in _bots)
        {
            if (bot == null || !bot.isActiveAndEnabled || !bot.WantsBroadcast(1f)) continue;
            if (n > BatchSplitAt)
            {
                sc.SendAIBatch(_batchBuf, n);
                n = BinaryProtocol.BeginAIBatch(_batchBuf, now);
            }
            if (!_botPC.TryGetValue(bot, out var pc)) _botPC[bot] = pc = bot.GetComponent<PlayerController>();
            var t = bot.transform;
            Vector3 p = t.position, e = t.eulerAngles;
            n = BinaryProtocol.AppendAIBatch(_batchBuf, n, bot.Nickname, p.x, p.y, p.z, e.x, e.y, e.z,
                                             bot.Speed, pc != null ? pc.SmokeFx : (byte)0);
            bot.MarkBroadcast();
        }
        if (_batchBuf[BinaryProtocol.AIBatchCountIndex] > 0) sc.SendAIBatch(_batchBuf, n);
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

        if (IsTankerBot(p.nickname)) TankerDrogueRig.Ensure(go);   // 호스·드로그는 각자 그린다

        _remoteBots[p.nickname] = pc;
        DevLog.Log($"[AIManager] Remote AI view: {p.nickname} (aiType={p.aiType})");
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
        // 시계 키 = 방장 — 리더(방장 MOVE)와 같은 타임라인·지연으로 보간해 편대 간격이 유지된다
        pc.AddSnapshot(
            new Vector3(p.posX, p.posY, p.posZ),
            Quaternion.Euler(p.rotX, p.rotY, p.rotZ),
            p.isMove, p.localTime, p.fx, NetworkManager.Instance?.socketClient?.roomHostNickname);
    }

    // ── 유틸리티 ──────────────────────────────────────────────────────────

    // 플레이어 발진 후 호출 — 적 AI 전투 시작 + 에스코트 순차 발진
    public void EnableAICombat()
    {
        _escortLandingStarted = false;
        _hasArrestInfo        = false;
        if (_recoveryCoroutine != null) { StopCoroutine(_recoveryCoroutine); _recoveryCoroutine = null; }
        // 적 전투 활성
        foreach (var bot in _bots)
            bot?.GetComponent<EnemyAI>()?.EnableCombat();

        // 갑판 기체는 순차 발진 (Slot_01 → Slot_02 → ... spawn 순서대로, 4초 간격),
        // 공중 기체(대기·패턴 중)는 리더가 항모 존을 벗어나면 재합류
        var escorts = GetEscorts();
        int launchIdx = 0;
        for (int i = 0; i < escorts.Count; i++)
        {
            var e = escorts[i];
            if (e.CanLaunch)
            {
                if (launchIdx == 0) e.BeginLaunchSequence();
                else                StartCoroutine(DelayedLaunch(e, launchIdx * 4f));
                launchIdx++;
            }
            else
            {
                GetMarshalStation(i, out var c, out float alt, out float rad);
                e.RequestRejoin(c, alt, rad);
            }
        }
    }

    // ── 리더(로컬 조종사) 사망 ───────────────────────────────────────────────
    // 에스코트는 편대를 풀고 항모 상공에 고도·반경을 달리해 대기 선회한다.
    // 대기 시간 안에 재출격하면 재합류, 아니면 Case I 패턴으로 차례로 착함.
    public void OnLeaderDown()
    {
        if (!_isHost) return;
        if (_landingCoroutine != null) { StopCoroutine(_landingCoroutine); _landingCoroutine = null; }
        _escortLandingStarted = true;   // 리더 정지 감지 폴백 착함(BeginEscortLanding) 차단

        var escorts = GetEscorts();
        for (int i = 0; i < escorts.Count; i++)
        {
            GetMarshalStation(i, out var c, out float alt, out float rad);
            escorts[i].BeginMarshal(c, alt, rad);
        }

        if (_recoveryCoroutine != null) StopCoroutine(_recoveryCoroutine);
        _recoveryCoroutine = StartCoroutine(MarshalRecoveryCoroutine());
        DevLog.Log($"[AIManager] Leader down — {escorts.Count} escort(s) to marshal");
    }

    System.Collections.IEnumerator MarshalRecoveryCoroutine()
    {
        yield return new WaitForSeconds(_marshalHoldTime);

        var aws    = ArrestingWireSystem.Instance;
        var anchor = CarrierAnchor;
        Vector3 dir = aws != null ? aws.LandingDirection : (anchor != null ? anchor.forward : Vector3.forward);
        var slots   = CollectEscortSlots();
        var escorts = GetEscorts();
        for (int i = 0; i < escorts.Count; i++)
        {
            var e = escorts[i];
            if (!e.IsHolding) continue;
            // 기체마다 자기 주기 슬롯으로 — 재출격한 플레이어 기체(자기 networkId 슬롯)와 겹치지 않는다
            int id = e.GetComponent<NetworkAircraft>()?.networkId ?? -1;
            Vector3 slot;
            if (TryGetDeckPose(id, out var p, out _)) slot = p;
            else if (i < slots.Count)                slot = slots[i].transform.position;
            else                                     slot = aws != null ? aws.WireCenter : (anchor != null ? anchor.position : Vector3.zero);
            e.BeginRecovery(slot, dir);
        }
        _recoveryCoroutine = null;
    }

    Transform CarrierAnchor => _cachedCarrier != null ? _cachedCarrier.transform : ArrestingWireSystem.Instance?.transform.parent;

    // 대기 선회 스테이션 — 항모(와이어 중심) 상공, 기체 i 마다 고도·반경을 한 칸씩 쌓아 충돌 방지
    void GetMarshalStation(int i, out Vector3 center, out float altitude, out float radius)
    {
        var aws    = ArrestingWireSystem.Instance;
        var anchor = CarrierAnchor;
        center   = aws != null ? aws.WireCenter : (anchor != null ? anchor.position : Vector3.zero);
        var cfg  = _escortBehavior != null ? _escortBehavior : s_defaultBehavior ??= ScriptableObject.CreateInstance<EscortBehaviorSO>();
        altitude = center.y + cfg.marshalBaseAltitude + cfg.marshalAltitudeStep * i;
        radius   = cfg.marshalRadius + cfg.marshalRadiusStep * i;
    }
    static EscortBehaviorSO s_defaultBehavior;

    System.Collections.Generic.List<EscortAI> GetEscorts()
    {
        var escorts = new System.Collections.Generic.List<EscortAI>();
        foreach (var bot in _bots)
        {
            if (bot == null) continue;
            var escort = bot.GetComponent<EscortAI>();
            if (escort != null) escorts.Add(escort);
        }
        return escorts;
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

    // 외부(ArrestingWireSystem, GameModeManager)에서 호스트 여부 판정용
    public bool IsHost => _isHost;

    // 와이어 체결 경로 정보 조회 (게스트가 호스트에게 전달)
    public (Vector3 approachDir, Vector3 wirePos) GetArrestInfo() => (_arrestedApproachDir, _arrestedWirePos);

    // 플레이어 F키 하차(ExitFlight) 시 호출 — 저장된 경로로 에스코트 순차 착함 시작.
    // 와이어 체결 정보가 있을 때만 동작하며, 폴백 코루틴이 이미 실행 중이더라도 덮어씀.
    public void BeginEscortLandingAfterDismount()
    {
        if (!_isHost || !_hasArrestInfo) return;
        // 폴백 코루틴(UpdateEscorting leaderStopTimer)이 먼저 시작됐을 수 있으므로
        // arrest info가 있을 때는 기존 착함 코루틴을 취소하고 올바른 경로로 재시작한다.
        if (_landingCoroutine != null) { StopCoroutine(_landingCoroutine); _landingCoroutine = null; }
        _escortLandingStarted = true;
        var carrierTr = _cachedCarrier?.transform ?? FindAnyObjectByType<CarrierController>()?.transform;
        DevLog.Log($"[AIManager] BeginEscortLandingAfterDismount: carrier={(carrierTr != null ? carrierTr.name : "null")}  dir={_arrestedApproachDir}  wirePos={_arrestedWirePos}");
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

    // 게스트 착함 이벤트 (ARREST_EVENT) — 에스코트 AI 는 호스트에만 있으므로 호스트가 대신 처리
    void HandleArrestEvent(ArrestEventPacket p)
    {
        if (!_isHost) return;
        switch (p.eventType)
        {
            case 0:
                BeginEscortFreeFlightNow();
                break;
            case 1:
                StoreArrestInfo(new Vector3(p.approachDirX, p.approachDirY, p.approachDirZ),
                                new Vector3(p.wirePosX, p.wirePosY, p.wirePosZ));
                BeginEscortLandingAfterDismount();
                break;
            case 2:
                BeginEscortLanding(null);
                break;
        }
    }

    // 플레이어 항모 착함 후 호출 (폴백 경로 — 와이어 없이 착함, 또는 UpdateEscorting 폴백)
    public void BeginEscortLanding(Transform carrier)
    {
        if (!_isHost || _escortLandingStarted) return;
        _escortLandingStarted = true;

        // 씬에 CarrierController 가 없어 carrier 가 null 로 들어온다 → 기존엔 착함 목표가 (0,0,0) 이 되어
        // 에스코트가 해수면 아래로 날아갔다. 와이어 시스템이 있으면 와이어 체결 경로와 같은 슬롯 착함을 쓴다.
        var aws = ArrestingWireSystem.Instance;
        if (aws != null)
        {
            if (carrier == null) carrier = aws.transform.parent;
            DevLog.Log($"[AIManager] BeginEscortLanding: wire path  carrier={(carrier != null ? carrier.name : "null")}  wire={aws.WireCenter}");
            _landingCoroutine = StartCoroutine(EscortLandingCoroutine(carrier, aws.LandingDirection, aws.WireCenter, true));
            return;
        }

        DevLog.Log($"[AIManager] BeginEscortLanding: starting landing coroutine  carrier={(carrier != null ? carrier.name : "null")}");
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
        DevLog.Log($"[AIManager] EscortLandingCoroutine: escorts={escorts.Count}  slots={slots.Count}  hasPath={hasPath}");

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
                DevLog.Log($"[AIManager] → BeginLandingWithPath '{escort.name}' → Slot_{i + 1}  pos={slotPos}");
                escort.BeginLandingWithPath(carrier, approachDir, slotPos);
            }
            else
            {
                DevLog.Log($"[AIManager] → BeginLanding '{escort.name}' (slot {i + 1})");
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
        RecalculateEscortFormation();
        DevLog.Log($"[AIManager] Escort takeover: '{botNick}' → player, old aircraft → '{newEscortNick}' escort");
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

        RecalculateEscortFormation();
        DevLog.Log($"[AIManager] BoardEscortFromGround: '{nick}' AI 제거, 플레이어 탑승");
    }

    // 플레이어 탑승 후 남은 에스코트들의 편대 오프셋을 좌우 대칭으로 재배분.
    // 오름차순(x 기준) 정렬 → 왼쪽 절반·오른쪽 절반 쌍 배치, 홀수 나머지 1기는 정후방.
    void RecalculateEscortFormation()
    {
        var escorts = new System.Collections.Generic.List<EscortAI>();
        foreach (var bot in _bots)
        {
            if (bot == null) continue;
            var e = bot.GetComponent<EscortAI>();
            if (e != null) escorts.Add(e);
        }

        int N = escorts.Count;
        if (N == 0) return;

        // 기존 FormationOffset.x 기준 오름차순 (좌→우) — 좌우 소속을 유지하며 재배분
        escorts.Sort((a, b) => a.FormationOffset.x.CompareTo(b.FormationOffset.x));

        const float LateralBase = 32f;   // 안쪽 쌍 측면 거리 (m)
        const float LateralStep = 28f;   // 쌍 rank 증가당 측면 추가
        const float RearBase    = 40f;   // 안쪽 쌍 후방 거리 (m)
        const float RearStep    = 40f;   // 쌍 rank 증가당 후방 추가
        const float AltBase     = -5f;   // 기본 고도 차 (m)
        const float AltStep     = -3f;   // rank 증가당 고도 감소

        int  pairs     = N / 2;
        bool hasBehind = (N % 2) == 1;

        // 왼쪽 절반 (indices 0..pairs-1) — 가장 왼쪽(i=0)이 바깥 rank
        for (int i = 0; i < pairs; i++)
        {
            int   rank    = pairs - i;                          // 바깥=pairs, 안쪽=1
            float lateral = LateralBase + (rank - 1) * LateralStep;
            float rear    = RearBase    + (rank - 1) * RearStep;
            float alt     = AltBase     + (rank - 1) * AltStep;
            escorts[i].SetFormationOffset(new Vector3(-lateral, alt, -rear));
        }

        // 정후방 (홀수 남은 1기)
        if (hasBehind)
        {
            float rear = RearBase + pairs * RearStep;
            float alt  = AltBase  + pairs * AltStep;
            escorts[pairs].SetFormationOffset(new Vector3(0f, alt, -rear));
        }

        // 오른쪽 절반 — 안쪽(rank 1)부터 시작
        int rightStart = hasBehind ? pairs + 1 : pairs;
        for (int i = rightStart; i < N; i++)
        {
            int   rank    = i - rightStart + 1;
            float lateral = LateralBase + (rank - 1) * LateralStep;
            float rear    = RearBase    + (rank - 1) * RearStep;
            float alt     = AltBase     + (rank - 1) * AltStep;
            escorts[i].SetFormationOffset(new Vector3(lateral, alt, -rear));
        }

        if (Application.isPlaying)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"[AIManager] 편대 재배분 N={N}  쌍={pairs}  후방={hasBehind}\n");
            foreach (var e in escorts)
                sb.Append($"  {e.name}: {e.FormationOffset}\n");
            DevLog.Log(sb.ToString());
        }
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
    public static bool IsTankerBot(string nickname)=> nickname.StartsWith("BOT_T");

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

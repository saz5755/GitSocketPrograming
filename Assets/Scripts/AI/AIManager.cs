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

    [Header("에스코트 AI — 학익진 편대")]
    [SerializeField] bool _spawnEscorts = false;
    [SerializeField] [Range(1, 2)] int _escortCount = 2;

    [Header("에스코트 갑판 대기 위치 (씬 오브젝트로 지정)")]
    [Tooltip("하이어라키의 Escort01_Position 오브젝트를 드래그")]
    [SerializeField] Transform _escortPos0;
    [Tooltip("하이어라키의 Escort02_Position 오브젝트를 드래그")]
    [SerializeField] Transform _escortPos1;

    [Header("AI 공통 설정 (모든 봇에 자동 주입)")]
    [Tooltip("EscortBehaviorSO — 미할당 시 런타임 폴백 인스턴스 생성")]
    [SerializeField] EscortBehaviorSO _escortBehavior;
    [Tooltip("AIBotConfigSO — 미할당 시 런타임 폴백 인스턴스 생성")]
    [SerializeField] AIBotConfigSO _botConfig;

    [Header("적 AI — 미사일 공격 + 위협 기동")]
    [SerializeField] bool _spawnEnemies = false;
    [SerializeField] [Range(1, 4)] int _enemyCount = 1;

    bool _isHost;
    bool _initialized;
    bool _escortLandingStarted;

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
        sc.OnAISpawn   += HandleRemoteAISpawn;
        sc.OnAIDespawn += HandleRemoteAIDespawn;
        sc.OnAIMove    += HandleRemoteAIMove;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;
        sc.OnAISpawn   -= HandleRemoteAISpawn;
        sc.OnAIDespawn -= HandleRemoteAIDespawn;
        sc.OnAIMove    -= HandleRemoteAIMove;
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

        var carrier = FindObjectOfType<CarrierController>();
        _cachedCarrier = carrier;
        var sides   = new[] { EscortAI.EscortSide.Left, EscortAI.EscortSide.Right };
        int count   = Mathf.Clamp(_escortCount, 1, 2);

        // Inspector 미연결 시 씬 전체에서 이름으로 자동 탐색
        if (_escortPos0 == null) _escortPos0 = GameObject.Find("Escort01_Position")?.transform;
        if (_escortPos1 == null) _escortPos1 = GameObject.Find("Escort02_Position")?.transform;

        var posTrs = new[] { _escortPos0, _escortPos1 };

        for (int i = 0; i < count; i++)
        {
            string nick = $"BOT_E{i + 1}";

            Vector3    spawnPos;
            Quaternion spawnRot;

            if (posTrs[i] != null)
            {
                spawnPos = posTrs[i].position;
                spawnRot = carrier != null ? carrier.transform.rotation : leader.transform.rotation;
            }
            else if (carrier != null)
            {
                Vector3 localOff = i == 0 ? new Vector3(-15f, 5.5f, 60f) : new Vector3(-18.5f, 5.5f, 40f);
                spawnPos = carrier.transform.TransformPoint(localOff);
                spawnRot = carrier.transform.rotation;
            }
            else
            {
                Vector3 off = i == 0 ? new Vector3(-32f, -5f, -40f) : new Vector3(32f, -5f, -40f);
                spawnPos = leader.transform.TransformPoint(off);
                spawnRot = leader.transform.rotation;
            }

            bool onDeck = posTrs[i] != null || carrier != null;
            var go     = BuildAIObject(nick, spawnPos, spawnRot);
            var escort = go.AddComponent<EscortAI>();
            escort.SetBehavior(_escortBehavior);   // 슬롯 비어있으면 EscortAI가 폴백 사용
            escort.Initialize(leader.transform, sides[i], spawnedOnDeck: onDeck);

            // AIBotController.Awake() 이후에 추가해야 DisableIfPresent<> 영향을 받지 않음
            go.AddComponent<AircraftBoardingTrigger>();
            go.AddComponent<EscortVFXController>();

            _bots.Add(go.GetComponent<AIBotController>());
            NetworkManager.Instance?.socketClient?.SendAISpawn(
                nick, spawnPos, spawnRot.eulerAngles, aiType: 0);

            Debug.Log($"[AIManager] Escort spawned: {nick}");
        }
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
        // 적 전투 활성
        foreach (var bot in _bots)
            bot?.GetComponent<EnemyAI>()?.EnableCombat();

        // 에스코트 순차 발진: Left → Right 순, 4초 간격으로 캐터펄트 발진
        var escorts = new System.Collections.Generic.List<EscortAI>();
        foreach (var bot in _bots)
        {
            if (bot == null) continue;
            var escort = bot.GetComponent<EscortAI>();
            if (escort != null) escorts.Add(escort);
        }
        escorts.Sort((a, b) => a.Side == EscortAI.EscortSide.Left ? -1 : 1);

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

    // 플레이어 항모 착함 후 호출 (GameModeManager 경로) — 에스코트 순차 착함
    public void BeginEscortLanding(Transform carrier)
    {
        Debug.Log($"[AIManager] BeginEscortLanding called  isHost={_isHost}  landingStarted={_escortLandingStarted}  carrier={(carrier != null ? carrier.name : "null")}");
        if (!_isHost || _escortLandingStarted) return;
        _escortLandingStarted = true;
        StartCoroutine(EscortLandingCoroutine(carrier, Vector3.zero, Vector3.zero, false));
    }

    // 어레스팅 와이어 정지 후 호출 (ArrestingWireSystem 경로) — 플레이어 경로로 순차 착함
    public void BeginEscortLandingFromWire(Vector3 approachDir, Vector3 wirePos)
    {
        Debug.Log($"[AIManager] BeginEscortLandingFromWire called  isHost={_isHost}  landingStarted={_escortLandingStarted}  dir={approachDir}  wirePos={wirePos}");
        if (!_isHost || _escortLandingStarted) return;
        _escortLandingStarted = true;
        var carrierTr = _cachedCarrier?.transform ?? FindObjectOfType<CarrierController>()?.transform;
        StartCoroutine(EscortLandingCoroutine(carrierTr, approachDir, wirePos, true));
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
        escorts.Sort((a, b) => a.Side == EscortAI.EscortSide.Left ? -1 : 1);
        Debug.Log($"[AIManager] EscortLandingCoroutine started  escorts={escorts.Count}  hasPath={hasPath}");

        foreach (var escort in escorts)
        {
            Debug.Log($"[AIManager] → Calling BeginLanding on '{escort.name}'");
            if (hasPath) escort.BeginLandingWithPath(carrier, approachDir, wirePos);
            else         escort.BeginLanding(carrier);

            // 착함 완료까지 대기 (최대 90초)
            float timeout = 90f;
            while (!escort.IsLanded && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            yield return new WaitForSeconds(3f);
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
        string         botNick = escortBot?.Nickname ?? "BOT_E1";
        EscortAI.EscortSide botSide = escortAI?.Side ?? EscortAI.EscortSide.Left;

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
        newEscort.Initialize(newPlayerPC.transform, botSide, spawnedOnDeck: true);
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

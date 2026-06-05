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

    [Header("적 AI — 미사일 공격 + 위협 기동")]
    [SerializeField] bool _spawnEnemies = false;
    [SerializeField] [Range(1, 4)] int _enemyCount = 1;

    bool _isHost;
    bool _initialized;

    // 호스트가 관리하는 AI 봇 목록
    readonly List<AIBotController> _bots = new();

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
        var carrier = FindObjectOfType<CarrierController>();
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
                // 씬 오브젝트 위치 사용 (월드 좌표)
                spawnPos = posTrs[i].position;
                spawnRot = carrier != null ? carrier.transform.rotation : leader.transform.rotation;
            }
            else if (carrier != null)
            {
                // 폴백: 항모 로컬 기본값
                Vector3 localOff = i == 0 ? new Vector3(-15f, 5.5f, 60f) : new Vector3(-18.5f, 5.5f, 40f);
                spawnPos = carrier.transform.TransformPoint(localOff);
                spawnRot = carrier.transform.rotation;
            }
            else
            {
                // 폴백: 리더 기준 오프셋
                Vector3 off = i == 0 ? new Vector3(-32f, -5f, -40f) : new Vector3(32f, -5f, -40f);
                spawnPos = leader.transform.TransformPoint(off);
                spawnRot = leader.transform.rotation;
            }

            bool onDeck = posTrs[i] != null || carrier != null;
            var go     = BuildAIObject(nick, spawnPos, spawnRot);
            var escort = go.AddComponent<EscortAI>();
            escort.Initialize(leader.transform, sides[i], spawnedOnDeck: onDeck);

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
        // 적 전투 활성
        foreach (var bot in _bots)
            bot?.GetComponent<EnemyAI>()?.EnableCombat();

        // 로컬 플레이어 탐색
        PlayerController localPlayer = null;
        foreach (var pc in PlayerController.All)
            if (pc != null && pc.isLocalPlayer) { localPlayer = pc; break; }
        if (localPlayer == null) return;

        // BOT_E1 먼저 찾아두기 (BOT_E2의 경로 기준)
        Transform bot1Transform = null;
        foreach (var bot in _bots)
            if (bot != null && bot.Nickname == "BOT_E1") { bot1Transform = bot.transform; break; }

        // 에스코트 순차 발진
        // BOT_E1 → 플레이어 경로를 launchDelay 초 지연 재생
        // BOT_E2 → BOT_E1 경로를 launchDelay 초 지연 재생 (= 플레이어로부터 2×launchDelay 지연)
        foreach (var bot in _bots)
        {
            if (bot == null) continue;
            var escort = bot.GetComponent<EscortAI>();
            if (escort == null) continue;

            if (bot.Nickname == "BOT_E1")
                escort.BeginLaunchSequence(localPlayer.transform);
            else if (bot.Nickname == "BOT_E2")
                escort.BeginLaunchSequence(bot1Transform ?? localPlayer.transform);
        }
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

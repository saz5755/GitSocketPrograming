using UnityEngine;

/// <summary>
/// 로컬 조종사의 생존 상태 — 피격 귀속, 추락·충돌·사출 판정, 사망 연출, 재출격.
///
/// 사망 원인 (실기 사고 분류에 맞춤):
///   SHOT_DOWN    — 피격으로 기체 내구도 0
///   WATER_IMPACT — 해수면 충돌
///   CRASH        — 갑판·구조물·다른 기체와 충돌, 과도한 침하율 착지
///   EJECTED      — 사출 (Ctrl+E ×3, DCS 방식 안전장치)
/// 피격 후 15초 안에 추락·사출하면 마지막 공격자에게 격추가 귀속된다 (상용 시뮬과 동일한 관례).
/// </summary>
public class PilotStatus : MonoBehaviour
{
    public static PilotStatus Instance { get; private set; }

    public const string CauseShotDown    = "SHOT_DOWN";
    public const string CauseWaterImpact = "WATER_IMPACT";
    public const string CauseCrash       = "CRASH";
    public const string CauseEjected     = "EJECTED";

    const float KillCreditWindow  = 15f;   // 피격 후 이 시간 안의 자멸은 공격자 격추로 귀속
    const float EjectPullWindow   = 1.5f;  // 사출 핸들 3회를 이 시간 안에
    const float LaunchGrace       = 4f;    // 발진 직후 충돌 판정 유예 (카타펄트·갑판 이탈)
    const float HardLandingSink   = 12f;   // m/s — 이 이상 침하율로 접지하면 기체 파손
    const float CollisionSpeed    = 25f;   // m/s — 이 이상으로 구조물 정면 충돌 시 파손
    const float RespawnDelay      = 4f;    // 추락 후 재출격 가능까지

    public bool   IsDead       { get; private set; }
    public string DeathCause   { get; private set; } = "";
    public string Killer       { get; private set; } = "";
    public string KillerWeapon { get; private set; } = "";
    /// <summary>잔해 충돌 후 재출격 가능 시각 (그 전엔 &lt;0)</summary>
    public float  RespawnReadyAt { get; private set; } = -1f;
    public float  SortieTime   { get; private set; }
    public int    SortieKills  { get; private set; }

    string _lastAttacker = "", _lastWeapon = "";
    float  _lastHitTime  = -999f;
    int    _ejectPulls;
    float  _ejectFirstPull;

    Vector3 _prevPos, _prevVel;
    bool    _prevValid;
    float   _sortieStart;
    PlayerController _deadPc;
    int     _deadAircraftId = -1;
    OceanSurface _ocean;
    readonly RaycastHit[] _hits = new RaycastHit[8];

    static string MyNick => NetworkManager.Instance?.socketClient?.myNickname ?? "";
    GameModeManager GM => GameModeManager.Instance;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnEnable()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnPlayerKilled += HandleKillReport;
    }

    void OnDisable()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnPlayerKilled -= HandleKillReport;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start() => _ocean = FindAnyObjectByType<OceanSurface>();

    // ── 피격 (CombatDamage 가 소유자 확인 후 호출) ──────────────────────────
    public void TakeHit(float amount, string attacker, string weapon)
    {
        if (IsDead || GM == null || !GM.IsFlying) return;
        var pc = GM.LocalPC;
        if (pc == null) return;

        pc.TakeDamage(amount);
        if (!string.IsNullOrEmpty(attacker) && attacker != MyNick)
        {
            _lastAttacker = attacker;
            _lastWeapon   = weapon;
            _lastHitTime  = Time.time;
        }
        if (pc.IsDestroyed) Die(CauseShotDown);
    }

    // 내가 격추한 수 (이번 출격) — 서버 중계로 확인된 것만
    void HandleKillReport(PlayerKilledPacket p)
    {
        if (p.killer == MyNick && p.victim != MyNick) SortieKills++;
    }

    void Update()
    {
        if (GM == null) return;

        if (IsDead)
        {
            if (RespawnReadyAt > 0f && Time.time >= RespawnReadyAt && SimInput.ConfirmPressed)
                Respawn();
            return;
        }

        if (!GM.IsFlying) { _prevValid = false; _ejectPulls = 0; return; }

        var pc = GM.LocalPC;
        if (pc == null) return;
        if (_prevValid == false) _sortieStart = GM.FlightStartTime;
        SortieTime = Time.time - _sortieStart;

        UpdateEjectHandle();
        if (!IsDead) DetectImpact(pc);
    }

    // ── 사출 핸들: 3회 연속 당김 (오조작 방지 안전장치) ──────────────────────
    void UpdateEjectHandle()
    {
        if (_ejectPulls > 0 && Time.time - _ejectFirstPull > EjectPullWindow) _ejectPulls = 0;
        if (!SimInput.EjectHandlePulled) return;

        if (_ejectPulls == 0) _ejectFirstPull = Time.time;
        _ejectPulls++;
        if (_ejectPulls >= 3) { _ejectPulls = 0; Die(CauseEjected); return; }
        CombatFeedUI.Instance?.ShowNotice($"EJECT  {_ejectPulls}/3", warn: true);
    }

    // ── 추락·충돌 판정 ──────────────────────────────────────────────────────
    void DetectImpact(PlayerController pc)
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Vector3 pos = pc.transform.position;
        Vector3 vel = _prevValid ? (pos - _prevPos) / dt : pc.transform.forward * pc.CurrentSpeed;

        // 해수면 — PlayerController 가 수면 +1.5m 에서 막으므로 그 근처면 충돌로 본다
        if (_ocean != null && pos.y <= _ocean.seaLevel + 2f)
        {
            Die(CauseWaterImpact);
            return;
        }

        bool grace = Time.time - GM.FlightStartTime < LaunchGrace || pc.IsArrested;
        if (!grace && _prevValid)
        {
            // 정면 충돌: 이동 경로 앞 구조물 (갑판 위 미끄러짐처럼 비스듬한 접촉은 제외)
            float speed = vel.magnitude;
            if (speed > CollisionSpeed)
            {
                Vector3 dir = vel / speed;
                int n = Physics.SphereCastNonAlloc(_prevPos, 1.2f, dir, _hits, speed * dt + 1.5f,
                                                   Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var h = _hits[i];
                    if (h.transform.IsChildOf(pc.transform) || h.distance <= 0f) continue;
                    if (Vector3.Dot(dir, -h.normal) > 0.6f) { Die(CauseCrash); return; }
                }
            }

            // 과도한 침하율 접지 — 직전 프레임 하강 속도로 판단 (접지 프레임은 지면에 눌려 0 이 된다)
            if (-_prevVel.y > HardLandingSink && HasSurfaceBelow(pc, 1.5f))
            {
                Die(CauseCrash);
                return;
            }
        }

        _prevVel   = vel;
        _prevPos   = pos;
        _prevValid = true;
    }

    bool HasSurfaceBelow(PlayerController pc, float range)
    {
        int n = Physics.RaycastNonAlloc(pc.transform.position + Vector3.up, Vector3.down, _hits, range + 1f,
                                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (!_hits[i].transform.IsChildOf(pc.transform)) return true;
        return false;
    }

    // ── 사망 ────────────────────────────────────────────────────────────────
    void Die(string cause)
    {
        if (IsDead || GM == null) return;
        var pc = GM.LocalPC;
        if (pc == null) return;

        IsDead     = true;
        DeathCause = cause;
        bool credited = cause == CauseShotDown || Time.time - _lastHitTime <= KillCreditWindow;
        Killer       = credited ? _lastAttacker : "";
        KillerWeapon = credited ? _lastWeapon   : "";

        Vector3 pos = pc.transform.position;
        Vector3 vel = _prevValid ? _prevVel : pc.transform.forward * pc.CurrentSpeed;
        _deadPc         = pc;
        _deadAircraftId = pc.GetComponent<NetworkAircraft>()?.networkId ?? -1;
        RespawnReadyAt  = -1f;

        NetworkManager.Instance?.socketClient?.SendPlayerKilled(MyNick, Killer, KillerWeapon, cause, pos, _deadAircraftId);

        GM.EnterDeathState();
        pc.enabled = false;   // 조종 입력·이동 송신 중지 — 이후 위치는 잔해가 송신

        var fc = FindAnyObjectByType<FlightCamera>();
        fc?.BeginDeathCam(pc.transform);
        fc?.TriggerShake(0.5f, 0.6f);

        // 피격·사출은 잔해가 떨어지고, 수면·충돌은 그 자리에서 끝난다
        bool falls = cause == CauseShotDown || cause == CauseEjected;
        var wreck = WreckController.Begin(pc.gameObject, vel, onFire: cause == CauseShotDown,
                                          sendMoves: falls, onImpact: OnWreckImpact);
        if (!falls) wreck.ImpactNow();

        CombatFeedUI.Instance?.ShowDeathScreen();
        Debug.Log($"[Pilot] DEAD cause={cause} killer={Killer} weapon={KillerWeapon}");
    }

    void OnWreckImpact(Vector3 point)
    {
        NetworkManager.Instance?.socketClient?.SendAircraftState(_deadAircraftId, 0, point);
        RespawnReadyAt = Time.time + RespawnDelay;
    }

    // ── 재출격: 기체는 갑판 슬롯에서 정비·재무장, 조종사는 갑판으로 ──────────
    void Respawn()
    {
        var pc = _deadPc;
        if (pc != null)
        {
            var wreck = pc.GetComponent<WreckController>();
            if (wreck != null) wreck.Restore();

            if (AIManager.Instance != null && AIManager.Instance.TryGetDeckPose(_deadAircraftId, out var dp, out var dr))
                pc.transform.SetPositionAndRotation(dp, dr);
            pc.ResetForNewSortie();
            NetworkManager.Instance?.socketClient?.SendAircraftState(_deadAircraftId, 1, pc.transform.position);
        }

        // 무장 시스템은 FlightHUD 오브젝트에 있다
        var hud = FindAnyObjectByType<FlightHUD>();
        if (hud != null)
        {
            hud.GetComponent<MissileLauncher>()?.Rearm();
            hud.GetComponent<GunSystem>()?.Rearm();
            hud.GetComponent<CountermeasureSystem>()?.Rearm();
        }

        IsDead         = false;
        DeathCause     = Killer = KillerWeapon = "";
        RespawnReadyAt = -1f;
        SortieKills    = 0;
        SortieTime     = 0f;
        _lastAttacker  = _lastWeapon = "";
        _lastHitTime   = -999f;
        _prevValid     = false;
        _deadPc        = null;

        CombatFeedUI.Instance?.HideDeathScreen();
        GM.RespawnOnDeck();
    }
}

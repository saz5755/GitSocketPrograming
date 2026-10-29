using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;   // insideUnitSphere 등 로컬 VFX 전용

public class CountermeasureSystem : MonoBehaviour
{

    // 모든 수치는 GameplayConfigSO (Resources/GameplayConfig.asset)에서 중앙 관리.
    // 재고, 쿨다운, 수명, 자동 발사 간격/burst, 분산 등 모두 Config에서 조정.
    public float AutoFlareInterval => GameplayConfigSO.Instance.autoFlareInterval;

    // ── 전역 액티브 디코이 목록 ────────────────────────────────────────────
    static readonly List<CountermeasureDecoy> _active = new List<CountermeasureDecoy>(64);
    public static IReadOnlyList<CountermeasureDecoy> Active => _active;

    public static void Register(CountermeasureDecoy d)   { if (!_active.Contains(d)) _active.Add(d); }
    public static void Unregister(CountermeasureDecoy d) { _active.Remove(d); }

    // 투하 알림 이벤트 (FlightHUD 등 구독)
    public static event Action<CountermeasureType> OnDeploy;

    public int FlareRemaining { get; private set; }
    public int ChaffRemaining { get; private set; }

    PlayerController _local;
    float            _flareCd;
    float            _chaffCd;

    void Start()
    {
        Rearm();
        FindLocal();
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnCountermeasure += HandleRemoteCountermeasure;
    }

    void OnDestroy()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnCountermeasure -= HandleRemoteCountermeasure;
    }

    /// <summary>새 출격 — 플레어·채프 만재</summary>
    public void Rearm()
    {
        var cfg = GameplayConfigSO.Instance;
        FlareRemaining = cfg.flareStockMax;
        ChaffRemaining = cfg.chaffStockMax;
    }

    void FindLocal()
    {
        foreach (var pc in PlayerController.All)
            if (pc != null && pc.isLocalPlayer) { _local = pc; break; }
    }

    void Update()
    {
        if (_local == null) { FindLocal(); return; }
        // 비행 모드에서만 대응책 사용 가능 — 탑승 F키와 겹침 방지
        if (GameModeManager.Instance != null && !GameModeManager.Instance.IsFlying) return;

        if (_flareCd > 0f) _flareCd -= Time.deltaTime;
        if (_chaffCd > 0f) _chaffCd -= Time.deltaTime;

        if (SimInput.Pressed(SimAction.Flare) && FlareRemaining > 0 && _flareCd <= 0f)
            Deploy(CountermeasureType.Flare);
        if (SimInput.Pressed(SimAction.Chaff) && ChaffRemaining > 0 && _chaffCd <= 0f)
            Deploy(CountermeasureType.Chaff);
    }

    /// <summary>자동 기동(배럴롤 등)에서 호출. 쿨다운/재고 체크 포함, 가능하면 발사.</summary>
    public bool TryDeployFlare()
    {
        if (FlareRemaining <= 0 || _flareCd > 0f) return false;
        Deploy(CountermeasureType.Flare);
        return true;
    }

    /// <summary>
    /// 자동 발사 burst — GameplayConfig.autoFlareBurst 만큼 연속 발사.
    /// 쿨다운 체크 생략 — Q/E 홀드 자동 발사율은 autoFlareInterval만으로 결정.
    /// (수동 X 발사는 여전히 쿨다운 적용. Deploy()가 _flareCd를 리셋하므로
    ///  자동 발사 직후 수동 X는 짧게 지연됨 — 매크로/수동 조합 방지)
    /// </summary>
    public int TryDeployAutoBurst()
    {
        if (FlareRemaining <= 0) return 0;
        int burst = GameplayConfigSO.Instance.autoFlareBurst;
        int fired = 0;
        for (int i = 0; i < burst && FlareRemaining > 0; i++)
        {
            Deploy(CountermeasureType.Flare);
            fired++;
        }
        return fired;
    }

    void Deploy(CountermeasureType type)
    {
        var cfg = GameplayConfigSO.Instance;
        if (type == CountermeasureType.Flare) { FlareRemaining--; _flareCd = cfg.flareCooldown; }
        else                                  { ChaffRemaining--; _chaffCd = cfg.chaffCooldown; }

        DeployFrom(_local.nickname, _local.transform, _local.CurrentSpeed, type);
        OnDeploy?.Invoke(type);
    }

    /// <summary>
    /// 기체 후방으로 기만체 1발 사출 + 방 전체 중계. 재고·쿨다운은 호출자가 관리한다 (플레이어: Deploy, AI: EnemyAI).
    /// </summary>
    public static void DeployFrom(string owner, Transform aircraft, float speed, CountermeasureType type)
    {
        if (aircraft == null) return;
        var  cfg     = GameplayConfigSO.Instance;
        bool isFlare = type == CountermeasureType.Flare;

        // 항공기 후방으로 디코이 발사 — 플레어는 Config에서 분산 조정 (커튼 전개)
        float posScatter     = isFlare ? cfg.flareScatterPos : 0.6f;
        float velScatter     = isFlare ? cfg.flareScatterVel : 4f;
        float lateralKickMag = cfg.flareLateralKick;

        Vector3 spawnPos = aircraft.position
                         - aircraft.forward * 2.5f
                         + Random.insideUnitSphere * posScatter;
        Vector3 lateralKick = isFlare
            ? (aircraft.right * Random.Range(-lateralKickMag, lateralKickMag)
              + aircraft.up * Random.Range(-lateralKickMag * 0.67f, lateralKickMag * 0.67f))
            : Vector3.zero;
        // 플레어는 동체 하부 디스펜서에서 아래로 사출된다 — 배럴롤 중엔 기체 회전을 따라 나선 커튼이 펼쳐짐
        Vector3 ejectKick = isFlare ? -aircraft.up * Random.Range(4f, 8f) : Vector3.zero;
        Vector3 velocity = -aircraft.forward * (speed * 0.4f)
                         + lateralKick
                         + ejectKick
                         + Random.insideUnitSphere * velScatter;

        SpawnDecoy(owner, type, spawnPos, velocity);
        NetworkManager.Instance?.socketClient?.SendCountermeasure(owner, (int)type, spawnPos, velocity);
    }

    static void SpawnDecoy(string owner, CountermeasureType type, Vector3 pos, Vector3 velocity)
    {
        var   cfg      = GameplayConfigSO.Instance;
        bool  isFlare  = type == CountermeasureType.Flare;
        float lifetime = isFlare ? cfg.flareLifetime : cfg.chaffLifetime;

        var go = new GameObject($"{type}Decoy_{owner}");
        go.transform.position = pos;
        go.AddComponent<CountermeasureDecoy>().Initialize(type, lifetime, velocity, owner);

        if (isFlare) FlareDecoyVFX.Attach(go, lifetime);
        else         SpawnChaffVFX(go.transform, lifetime);
    }

    // 원격 기체(다른 플레이어·호스트의 AI)가 뿌린 기만체 — 이 클라이언트가 시뮬레이션하는 미사일도 속을 수 있게 로컬에 생성
    void HandleRemoteCountermeasure(CountermeasurePacket p)
    {
        if (CombatDamage.Owns(p.ownerNickname)) return;   // 내가 뿌린 것(또는 호스트로서 AI 가 뿌린 것)은 이미 생성됨
        SpawnDecoy(p.ownerNickname, (CountermeasureType)p.cmType,
                   new Vector3(p.posX, p.posY, p.posZ), new Vector3(p.velX, p.velY, p.velZ));
    }

    // ── 채프 VFX (은색 금속 입자, 파티클 유지) ──────────────────────────
    static void SpawnChaffVFX(Transform parent, float lifetime)
    {
        var go = new GameObject("ChaffVFX");
        go.transform.SetParent(parent, false);

        var ps   = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop         = true;
        main.startLifetime= new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
        main.startSpeed   = new ParticleSystem.MinMaxCurve(1f, 5f);
        main.startSize    = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor   = new ParticleSystem.MinMaxGradient(
            new Color(0.85f, 0.92f, 1.0f, 0.9f), new Color(0.60f, 0.70f, 0.85f, 0.7f));

        var em = ps.emission;
        em.rateOverTime = 80f;

        ApplyURPParticleMaterial(ps);
        ps.Play();
        Destroy(go, lifetime);
    }

    internal static void ApplyURPParticleMaterial(ParticleSystem ps)
    {
        var r = ps.GetComponent<ParticleSystemRenderer>();
        if (r == null) return;
        foreach (string n in new[]{ "Universal Render Pipeline/Particles/Unlit",
                                    "Particles/Standard Unlit", "Sprites/Default" })
        {
            var sh = Shader.Find(n);
            if (sh != null) { r.material = new Material(sh); return; }
        }
    }
}

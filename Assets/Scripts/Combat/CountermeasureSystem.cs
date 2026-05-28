using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;   // insideUnitSphere 등 로컬 VFX 전용

public class CountermeasureSystem : MonoBehaviour
{
    [Header("Keys")]
    [SerializeField] KeyCode flareKey = KeyCode.F;
    [SerializeField] KeyCode chaffKey = KeyCode.X;

    [Header("Stock")]
    [SerializeField] int   maxFlares       = 30;
    [SerializeField] int   maxChaff        = 30;
    [SerializeField] float flareCooldown   = 0.35f;
    [SerializeField] float chaffCooldown   = 0.35f;
    [SerializeField] float flareLifetime   = 4.0f;
    [SerializeField] float chaffLifetime   = 6.0f;

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

    void Start()  { FlareRemaining = maxFlares; ChaffRemaining = maxChaff; FindLocal(); }

    void FindLocal()
    {
        foreach (var pc in FindObjectsOfType<PlayerController>())
            if (pc.isLocalPlayer) { _local = pc; break; }
    }

    void Update()
    {
        if (_local == null) { FindLocal(); return; }
        // 비행 모드에서만 대응책 사용 가능 — 탑승 F키와 겹침 방지
        if (GameModeManager.Instance != null && !GameModeManager.Instance.IsFlying) return;

        if (_flareCd > 0f) _flareCd -= Time.deltaTime;
        if (_chaffCd > 0f) _chaffCd -= Time.deltaTime;

        if (Input.GetKeyDown(flareKey) && FlareRemaining > 0 && _flareCd <= 0f)
            Deploy(CountermeasureType.Flare);
        if (Input.GetKeyDown(chaffKey) && ChaffRemaining > 0 && _chaffCd <= 0f)
            Deploy(CountermeasureType.Chaff);
    }

    void Deploy(CountermeasureType type)
    {
        bool  isFlare  = type == CountermeasureType.Flare;
        float lifetime = isFlare ? flareLifetime : chaffLifetime;

        if (isFlare) { FlareRemaining--; _flareCd = flareCooldown; }
        else         { ChaffRemaining--; _chaffCd = chaffCooldown; }

        // 항공기 후방으로 디코이 발사
        Vector3 spawnPos = _local.transform.position
                         - _local.transform.forward * 2.5f
                         + Random.insideUnitSphere * 0.6f;
        Vector3 velocity = -_local.transform.forward * (_local.CurrentSpeed * 0.4f)
                         + Random.insideUnitSphere * 4f;

        var go = new GameObject($"{type}Decoy");
        go.transform.position = spawnPos;
        go.AddComponent<CountermeasureDecoy>().Initialize(type, lifetime, velocity);

        if (isFlare)
            FlareDecoyVFX.Attach(go, lifetime);
        else
            SpawnChaffVFX(go.transform, lifetime);

        OnDeploy?.Invoke(type);
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

using UnityEngine;

// 에스코트 AI 전투기 엔진 VFX.
// F35VFXController와 동일한 파티클 구성 — 플레이어 입력 대신 AIBotController.Speed로 제어.
public class EscortVFXController : MonoBehaviour
{
    [SerializeField] Vector3 nozzleLocalPos      = new Vector3(0f, 0f, -4.0f);
    [SerializeField] float   afterburnerThreshold = 15f;

    [Header("VFX Prefab (선택 — 미할당 시 절차적 폴백)")]
    [Tooltip("EngineExhaustAssembly 컴포넌트가 부착된 애프터버너 VFX 프리팹. F35VFXController와 같은 프리팹 사용 권장")]
    [SerializeField] GameObject _afterburnerVFXPrefab;

    [Header("Materials (Resources.Load 대체)")]
    [SerializeField] Material _additiveMaterial;
    [SerializeField] Material _shockDiamondMaterial;

    EngineExhaustAssembly _afterburnerAssembly;

    AIBotController  _bot;
    PlayerController _playerFallback;  // TakeOver 이후 _bot이 destroy되면 폴백

    void Start()
    {
        _bot = GetComponent<AIBotController>();

        // 같은 프리팹에 F35VFXController가 있으면 비활성화 (봇용으로 대체)
        var f35 = GetComponent<F35VFXController>();
        if (f35 != null) f35.enabled = false;

        // 플레이어 항공기와 동일한 노즐 위치를 공유 (F35VFXController.Start 이후에 호출됨)
        nozzleLocalPos = F35VFXController.SharedNozzleLocalPos;

        SetupAfterburner();
    }

    void SetupAfterburner()
    {
        // 이미 생성된 EngineNozzle 자식이 있으면 재사용, 없으면 새로 생성
        var existingNozzle = transform.Find("EngineNozzle");
        Transform nozzle;
        if (existingNozzle != null)
        {
            nozzle = existingNozzle;
        }
        else
        {
            var nozzleGO = new GameObject("EngineNozzle");
            nozzleGO.transform.SetParent(transform, false);
            nozzleGO.transform.localPosition = nozzleLocalPos;
            nozzle = nozzleGO.transform;
        }

        // ── 프리팹 경로 ────────────────────────────────────────────────────────
        if (_afterburnerVFXPrefab != null)
        {
            var inst = Instantiate(_afterburnerVFXPrefab, nozzle);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.rotation = Quaternion.LookRotation(-transform.forward, transform.up);   // 방출축 = 기체 후방
            _afterburnerAssembly = inst.GetComponent<EngineExhaustAssembly>();
            if (_afterburnerAssembly == null)
                Debug.LogWarning($"[EscortVFXController] _afterburnerVFXPrefab '{_afterburnerVFXPrefab.name}' has no EngineExhaustAssembly component on its root.");
            return;
        }

        // ── 절차적 폴백 ────────────────────────────────────────────────────────
        Material addMat     = _additiveMaterial     != null ? _additiveMaterial     : FXLib.Glow;
        Material diamondMat = _shockDiamondMaterial != null ? _shockDiamondMaterial : FXLib.Spark;
        _nozzleGlow = FXLib.MakeFlare("NozzleGlow", nozzle, new Vector3(0f, 0f, -0.2f), 0.9f, 0.0035f, 0.15f);

        // ── 외부 글로우 (주황/적색) ─────────────────────────────────────────
        var abObj = new GameObject("AfterburnerVFX");
        abObj.transform.SetParent(nozzle, false);
        // Euler(180,0,0): 방출 방향을 항공기 후방(-Z)으로 명시. World 공간에서 기체 뒤로 파티클이 남는다.
        // 방출축을 월드 기준으로 기체 후방에 맞춘다 — 공유 EngineNozzle 은 이미 후방을 보고 있어서
        // 예전처럼 로컬 180° 를 더 돌리면 화염이 기체 앞(동체 속)으로 뿜어졌다
        abObj.transform.rotation = Quaternion.LookRotation(-transform.forward, transform.up);

        var afterburnerPS = abObj.AddComponent<ParticleSystem>();
        afterburnerPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main       = afterburnerPS.main;
        main.duration  = 1f; main.loop = true;
        main.startLifetime = 0.15f; main.startSpeed = 35f; main.startSize = 1.5f;
        main.startColor = new Color(1f, 0.4f, 0.05f, 0.8f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;   // 화염이 노즐에 붙어 있도록 (월드면 고속에서 뒤로 흩어짐)

        var em = afterburnerPS.emission; em.rateOverTime = 80f;
        var sh = afterburnerPS.shape;
        sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 1f; sh.radius = 0.35f;

        var sz = afterburnerPS.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0.2f)));

        var rend = afterburnerPS.GetComponent<ParticleSystemRenderer>();
        if (addMat != null) rend.material = addMat;
        rend.renderMode = ParticleSystemRenderMode.Stretch; rend.velocityScale = 0.08f;

        // ── 내부 코어 (청/백색 쇼크 다이아몬드) ────────────────────────────
        var coreObj = new GameObject("AfterburnerCore");
        coreObj.transform.SetParent(abObj.transform, false);

        var corePS = coreObj.AddComponent<ParticleSystem>();
        corePS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var cm = corePS.main;
        cm.duration = 1f; cm.loop = true;
        cm.startLifetime = 0.12f; cm.startSpeed = 45f; cm.startSize = 0.8f;
        cm.startColor = new Color(1f, 0.86f, 0.62f, 1f);   // 쇼크 다이아몬드 — 주간엔 백황색 (청록은 어두운 동체 위에서 남색으로 보였다)
        cm.simulationSpace = ParticleSystemSimulationSpace.Local;

        var cem = corePS.emission; cem.rateOverTime = 60f;
        var csh = corePS.shape;
        csh.shapeType = ParticleSystemShapeType.Cone; csh.angle = 0f; csh.radius = 0.15f;

        var csz = corePS.sizeOverLifetime; csz.enabled = true;
        csz.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(
                new Keyframe(0f,  1f),  new Keyframe(0.2f, 0.4f),
                new Keyframe(0.4f, 1f), new Keyframe(0.6f, 0.4f),
                new Keyframe(0.8f, 1f), new Keyframe(1f,  0.2f)));

        var crend = corePS.GetComponent<ParticleSystemRenderer>();
        if (diamondMat != null) crend.material = diamondMat;
        else if (addMat != null) crend.material = addMat;
        crend.renderMode = ParticleSystemRenderMode.Stretch; crend.velocityScale = 0.05f;

        // ── 엔진 광원 ────────────────────────────────────────────────────────
        var afterburnerLight             = abObj.AddComponent<Light>();
        afterburnerLight.type           = LightType.Point;
        afterburnerLight.color          = new Color(1f, 0.6f, 0.2f);
        afterburnerLight.intensity      = 0f;
        afterburnerLight.range          = 20f;

        // 동적으로 어셈블리 부착해서 통일된 API로 제어
        _afterburnerAssembly = abObj.AddComponent<EngineExhaustAssembly>();
        _afterburnerAssembly.Configure(afterburnerPS, corePS, afterburnerLight);
    }

    FXLib.Flare _nozzleGlow;
    F35VFXController _f35;
    float _lastSpeed, _accel, _glow;
    float _flickSeed = -1f;

    void Update()
    {
        bool active;
        float dt = Time.deltaTime;
        if (_bot != null)
        {
            // AI 봇 모드 — 고속 유지·급가속 때만 (순항 중 늘 AB 를 켜고 다니지 않게)
            if (dt > 0f) _accel = Mathf.Lerp(_accel, (_bot.Speed - _lastSpeed) / dt, 1f - Mathf.Exp(-dt * 3f));
            _lastSpeed = _bot.Speed;
            active = _bot.Speed > afterburnerThreshold && (_bot.Speed > 95f || _accel > 4f);
        }
        else
        {
            // 플레이어가 이 기체에 탑승 — 배기 연출은 F35VFXController 가 맡는다 (켜 두면 화염이 두 겹)
            if (_f35 == null) _f35 = GetComponent<F35VFXController>();
            if (_f35 != null && _f35.enabled)
            {
                _afterburnerAssembly?.ForceOff();
                _nozzleGlow?.Set(Color.black, 0f);
                return;
            }
            // TakeOverEscortBot 이후 — PlayerController로 폴백, F35와 동일 조건 (속도 + W키)
            if (_playerFallback == null) _playerFallback = GetComponent<PlayerController>();
            active = _playerFallback != null
                  && _playerFallback.isLocalPlayer
                  && _playerFallback.CurrentSpeed > 60f
                  && SimInput.Throttle > 0.5f;
        }
        _afterburnerAssembly?.SetActive(active, Time.deltaTime);

        bool running = _bot != null ? _bot.Speed > 1f : (_playerFallback != null && _playerFallback.CurrentSpeed > 1f);
        float target = !running ? 0f : active ? 1f : 0.22f;
        _glow = Mathf.MoveTowards(_glow, target, dt * (target > _glow ? 4f : 1.5f));
        Color c = Color.Lerp(new Color(1f, 0.32f, 0.08f), new Color(1f, 0.72f, 0.42f), Mathf.InverseLerp(0.3f, 1f, _glow));
        if (_flickSeed < 0f) _flickSeed = Random.Range(0f, 100f);
        _nozzleGlow?.Set(c, _glow * 7f * (0.9f + 0.2f * Mathf.PerlinNoise(Time.time * 18f, _flickSeed)));
    }
}

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
            inst.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
            _afterburnerAssembly = inst.GetComponent<EngineExhaustAssembly>();
            if (_afterburnerAssembly == null)
                Debug.LogWarning($"[EscortVFXController] _afterburnerVFXPrefab '{_afterburnerVFXPrefab.name}' has no EngineExhaustAssembly component on its root.");
            return;
        }

        // ── 절차적 폴백 ────────────────────────────────────────────────────────
        Material addMat     = _additiveMaterial     != null ? _additiveMaterial     : Resources.Load<Material>("VFX/Mat_Additive");
        Material diamondMat = _shockDiamondMaterial != null ? _shockDiamondMaterial : Resources.Load<Material>("VFX/Mat_ShockDiamond");

        // ── 외부 글로우 (주황/적색) ─────────────────────────────────────────
        var abObj = new GameObject("AfterburnerVFX");
        abObj.transform.SetParent(nozzle, false);
        // Euler(180,0,0): 방출 방향을 항공기 후방(-Z)으로 명시. World 공간에서 기체 뒤로 파티클이 남는다.
        abObj.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);

        var afterburnerPS = abObj.AddComponent<ParticleSystem>();
        afterburnerPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main       = afterburnerPS.main;
        main.duration  = 1f; main.loop = true;
        main.startLifetime = 0.15f; main.startSpeed = 35f; main.startSize = 1.5f;
        main.startColor = new Color(1f, 0.4f, 0.05f, 0.8f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;

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
        cm.startColor = new Color(0.7f, 0.9f, 1f, 1f);
        cm.simulationSpace = ParticleSystemSimulationSpace.World;

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

    void Update()
    {
        bool active;
        if (_bot != null)
        {
            // AI 봇 모드 — 단순히 봇 속도 기반
            active = _bot.Speed > afterburnerThreshold;
        }
        else
        {
            // TakeOverEscortBot 이후 — PlayerController로 폴백, F35와 동일 조건 (속도 + W키)
            if (_playerFallback == null) _playerFallback = GetComponent<PlayerController>();
            active = _playerFallback != null
                  && _playerFallback.isLocalPlayer
                  && _playerFallback.CurrentSpeed > 60f
                  && Input.GetKey(KeyCode.W);
        }
        _afterburnerAssembly?.SetActive(active, Time.deltaTime);
    }
}

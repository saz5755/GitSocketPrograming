using UnityEngine;

public class F35VFXController : MonoBehaviour
{
    // EscortVFXController가 동일 위치를 참조할 수 있도록 노즐 로컬 좌표를 공유
    public static Vector3 SharedNozzleLocalPos = new Vector3(0f, 1.2f, -4.0f);

    // ── 아트 튜닝용 설정 클래스 (인스펙터에서 모두 수정 가능) ───────────────────

    [System.Serializable]
    public class ContrailConfig
    {
        public float    trailTime         = 1.5f;
        public float    startWidth        = 0.4f;
        public float    endWidth          = 2.0f;
        public float    minVertexDistance = 0.2f;
        public Gradient colorGradient;

        public ContrailConfig()
        {
            colorGradient = new Gradient();
            colorGradient.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(Color.white,                  0f),
                    new GradientColorKey(new Color(0.8f, 0.8f, 0.8f), 1f) },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(0.5f, 0f),
                    new GradientAlphaKey(0f,   1f) });
        }
    }

    [System.Serializable]
    public class IdleExhaustConfig
    {
        [Header("수명 · 속도 · 크기")]
        public float lifetimeMin    = 0.4f;
        public float lifetimeMax    = 0.8f;
        public float speedMin       = 6f;
        public float speedMax       = 14f;
        public float sizeMin        = 0.25f;
        public float sizeMax        = 0.55f;
        [Header("색상")]
        public Color colorA         = new Color(1f,    0.55f, 0.15f, 0.65f);
        public Color colorB         = new Color(0.75f, 0.30f, 0.05f, 0.30f);
        [Header("방출 · 형태")]
        public float emissionRate   = 30f;
        public float coneAngle      = 4f;
        public float coneRadius     = 0.28f;
        [Header("크기 변화")]
        public AnimationCurve sizeOverLifetime = new AnimationCurve(
            new Keyframe(0f, 0.4f), new Keyframe(1f, 1.8f));
        [Header("색상 변화")]
        public Gradient colorOverLifetime;
        [Header("조명")]
        public Color lightColor     = new Color(1f, 0.50f, 0.10f);
        public float lightIntensity = 1.8f;
        public float lightRange     = 7f;

        public IdleExhaustConfig()
        {
            colorOverLifetime = new Gradient();
            colorOverLifetime.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(new Color(1.0f,  0.65f, 0.25f), 0f),
                    new GradientColorKey(new Color(0.55f, 0.55f, 0.55f), 1f) },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(0.7f, 0f),
                    new GradientAlphaKey(0.0f, 1f) });
        }
    }

    [System.Serializable]
    public class HeatHazeConfig
    {
        [Header("수명 · 속도 · 크기")]
        public float lifetimeMin      = 0.8f;
        public float lifetimeMax      = 1.6f;
        public float speedMin         = 12f;
        public float speedMax         = 28f;
        public float sizeMin          = 0.6f;
        public float sizeMax          = 1.4f;
        [Header("색상")]
        public Color colorA           = new Color(1.0f,  0.92f, 0.75f, 0.10f);
        public Color colorB           = new Color(0.90f, 0.88f, 0.82f, 0.05f);
        [Header("방출 · 형태")]
        public float emissionRate     = 18f;
        public float coneAngle        = 10f;
        public float coneRadius       = 0.35f;
        [Header("크기 변화")]
        public AnimationCurve sizeOverLifetime = new AnimationCurve(
            new Keyframe(0f, 0.5f), new Keyframe(1f, 3.5f));
        [Header("색상 변화")]
        public Gradient colorOverLifetime;
        [Header("노이즈 (아지랑이 물결)")]
        public float noiseStrength    = 0.35f;
        public float noiseFrequency   = 0.6f;
        public float noiseScrollSpeed = 0.4f;

        public HeatHazeConfig()
        {
            colorOverLifetime = new Gradient();
            colorOverLifetime.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(new Color(1.0f,  0.95f, 0.80f), 0.0f),
                    new GradientColorKey(new Color(0.92f, 0.90f, 0.85f), 0.4f),
                    new GradientColorKey(new Color(0.85f, 0.85f, 0.85f), 1.0f) },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(0.0f,  0.0f),
                    new GradientAlphaKey(0.12f, 0.15f),
                    new GradientAlphaKey(0.0f,  1.0f) });
        }
    }

    [System.Serializable]
    public class AfterburnerProceduralConfig
    {
        [Header("외부 불꽃 (Outer Glow)")]
        public float outerLifetime    = 0.12f;
        public float outerSpeed       = 40f;
        public float outerSize        = 1.5f;
        public Color outerColor       = new Color(1f, 0.4f, 0.05f, 0.8f);
        public float outerEmission    = 80f;
        public float outerConeAngle   = 1f;
        public float outerConeRadius  = 0.35f;
        [Header("내부 코어 (Shock Diamond)")]
        public float innerLifetime    = 0.1f;
        public float innerSpeed       = 50f;
        public float innerSize        = 0.8f;
        public Color innerColor       = new Color(0.7f, 0.9f, 1f, 1f);
        public float innerEmission    = 60f;
        public AnimationCurve innerSizeOverLifetime = new AnimationCurve(
            new Keyframe(0f,   1f),
            new Keyframe(0.2f, 0.4f),
            new Keyframe(0.4f, 1f),
            new Keyframe(0.6f, 0.4f),
            new Keyframe(0.8f, 1f),
            new Keyframe(1f,   0.2f));
        [Header("조명")]
        public Color lightColor       = new Color(1f, 0.6f, 0.2f);
        public float lightRange       = 20f;
    }

    [System.Serializable]
    public class CatapultConfig
    {
        [Tooltip("캐터펄트 발진 직후 자동 애프터버너 유지 시간 (초)")]
        public float boostDuration       = 8f;
        [Tooltip("자동 부스트 작동 최소 속도")]
        public float minBoostSpeed       = 5f;
        [Tooltip("일반 비행 중 애프터버너 작동 최소 속도")]
        public float afterburnerMinSpeed = 60f;
    }

    // ── 인스펙터 슬롯 ──────────────────────────────────────────────────────────

    [Header("Contrails")]
    [SerializeField] Transform leftWingtip;
    [SerializeField] Transform rightWingtip;
    [SerializeField] float contrailThreshold = 15f;
    [SerializeField] ContrailConfig contrail = new ContrailConfig();

    [Header("VFX Prefabs (선택 — 미할당 시 절차적 폴백)")]
    [Tooltip("EngineExhaustAssembly 컴포넌트가 부착된 애프터버너 VFX 프리팹")]
    [SerializeField] GameObject _afterburnerVFXPrefab;
    [Tooltip("엔진 노즐 로컬 좌표 — 프리팹/절차 빌드 양쪽 사용")]
    [SerializeField] Transform engineNozzle;

    [Header("Materials (Resources.Load 대체)")]
    [Tooltip("애프터버너 외부 글로우용 가산 머티리얼")]
    [SerializeField] Material _additiveMaterial;
    [Tooltip("애프터버너 내부 쇼크 다이아몬드용 머티리얼")]
    [SerializeField] Material _shockDiamondMaterial;
    [Tooltip("Contrail · HeatHaze용 알파 머티리얼")]
    [SerializeField] Material _alphaMaterial;

    [Header("Engine Idle Exhaust")]
    [SerializeField] IdleExhaustConfig idleExhaust = new IdleExhaustConfig();

    [Header("Heat Haze (아지랑이)")]
    [SerializeField] HeatHazeConfig heatHaze = new HeatHazeConfig();

    [Header("Afterburner — 절차적 폴백 전용 (프리팹 미할당 시)")]
    [SerializeField] AfterburnerProceduralConfig afterburnerCfg = new AfterburnerProceduralConfig();

    [Header("Catapult Boost")]
    [SerializeField] CatapultConfig catapultCfg = new CatapultConfig();

    // ── 런타임 ─────────────────────────────────────────────────────────────────

    TrailRenderer leftContrail;
    TrailRenderer rightContrail;
    EngineExhaustAssembly _afterburnerAssembly;

    ParticleSystem _idleExhaustPS;
    ParticleSystem _heatHazePS;
    Light          _idleLight;
    bool           _engineIdleOn;
    float          _catapultBoostTimer;

    PlayerController playerController;
    Vector3 lastForward;

    void Start()
    {
        playerController = GetComponent<PlayerController>();
        lastForward = transform.forward;

        EnsureEngineNozzle();
        SetupContrails();
        SetupAfterburner();
        SetupIdleExhaust();
        SetupHeatHaze();
    }

    void EnsureEngineNozzle()
    {
        if (engineNozzle == null)
        {
            GameObject nozzle = new GameObject("EngineNozzle");
            nozzle.transform.SetParent(transform);
            nozzle.transform.localPosition = new Vector3(0f, 1.2f, -4.0f);
            engineNozzle = nozzle.transform;
        }
        SharedNozzleLocalPos = engineNozzle.localPosition;
    }

    void SetupContrails()
    {
        if (leftWingtip == null)
        {
            GameObject lw = new GameObject("LeftWingtip");
            lw.transform.SetParent(transform);
            lw.transform.localPosition = new Vector3(-2.8f, 0f, -1.0f);
            leftWingtip = lw.transform;
        }
        if (rightWingtip == null)
        {
            GameObject rw = new GameObject("RightWingtip");
            rw.transform.SetParent(transform);
            rw.transform.localPosition = new Vector3(2.8f, 0f, -1.0f);
            rightWingtip = rw.transform;
        }

        leftContrail  = CreateContrail(leftWingtip);
        rightContrail = CreateContrail(rightWingtip);
    }

    TrailRenderer CreateContrail(Transform parent)
    {
        var tr = parent.gameObject.AddComponent<TrailRenderer>();
        tr.time               = contrail.trailTime;
        tr.startWidth         = contrail.startWidth;
        tr.endWidth           = contrail.endWidth;
        tr.minVertexDistance  = contrail.minVertexDistance;
        tr.emitting           = false;
        tr.colorGradient      = contrail.colorGradient;

        Material alphaMat = _alphaMaterial != null ? _alphaMaterial : Resources.Load<Material>("VFX/Mat_Alpha");
        if (alphaMat != null) tr.material = alphaMat;

        return tr;
    }

    void SetupAfterburner()
    {
        if (_afterburnerVFXPrefab != null)
        {
            var inst = Instantiate(_afterburnerVFXPrefab, engineNozzle);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            _afterburnerAssembly = inst.GetComponent<EngineExhaustAssembly>();
            if (_afterburnerAssembly == null)
                Debug.LogWarning($"[F35VFXController] _afterburnerVFXPrefab '{_afterburnerVFXPrefab.name}' has no EngineExhaustAssembly component on its root.");
            return;
        }

        // ── 절차적 폴백 (Inspector 수치 사용) ─────────────────────────────────
        Material addMat     = _additiveMaterial     != null ? _additiveMaterial     : Resources.Load<Material>("VFX/Mat_Additive");
        Material diamondMat = _shockDiamondMaterial != null ? _shockDiamondMaterial : Resources.Load<Material>("VFX/Mat_ShockDiamond");

        GameObject abObj = new GameObject("AfterburnerVFX");
        abObj.transform.SetParent(engineNozzle);
        abObj.transform.localPosition = Vector3.zero;
        abObj.transform.localRotation = Quaternion.identity;

        // Outer Glow
        var ab = abObj.AddComponent<ParticleSystem>();
        ab.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var abMain        = ab.main;
        abMain.duration       = 1f;
        abMain.loop           = true;
        abMain.startLifetime  = afterburnerCfg.outerLifetime;
        abMain.startSpeed     = afterburnerCfg.outerSpeed;
        abMain.startSize      = afterburnerCfg.outerSize;
        abMain.startColor     = afterburnerCfg.outerColor;
        abMain.simulationSpace= ParticleSystemSimulationSpace.Local;
        abMain.playOnAwake    = true;
        var abEm              = ab.emission;
        abEm.rateOverTime     = afterburnerCfg.outerEmission;
        var abSh              = ab.shape;
        abSh.shapeType        = ParticleSystemShapeType.Cone;
        abSh.angle            = afterburnerCfg.outerConeAngle;
        abSh.radius           = afterburnerCfg.outerConeRadius;
        var abSize            = ab.sizeOverLifetime;
        abSize.enabled        = true;
        abSize.size           = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0.2f)));
        var abRend            = ab.GetComponent<ParticleSystemRenderer>();
        if (addMat != null) abRend.material = addMat;
        abRend.renderMode     = ParticleSystemRenderMode.Stretch;
        abRend.velocityScale  = 0.08f;

        // Inner Core (Shock Diamond)
        GameObject coreObj = new GameObject("AfterburnerCore");
        coreObj.transform.SetParent(abObj.transform);
        coreObj.transform.localPosition = Vector3.zero;
        coreObj.transform.localRotation = Quaternion.identity;
        var core              = coreObj.AddComponent<ParticleSystem>();
        core.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var cMain             = core.main;
        cMain.duration        = 1f;
        cMain.loop            = true;
        cMain.startLifetime   = afterburnerCfg.innerLifetime;
        cMain.startSpeed      = afterburnerCfg.innerSpeed;
        cMain.startSize       = afterburnerCfg.innerSize;
        cMain.startColor      = afterburnerCfg.innerColor;
        cMain.simulationSpace = ParticleSystemSimulationSpace.Local;
        cMain.playOnAwake     = true;
        var cEm               = core.emission;
        cEm.rateOverTime      = afterburnerCfg.innerEmission;
        var cSh               = core.shape;
        cSh.shapeType         = ParticleSystemShapeType.Cone;
        cSh.angle             = 0f;
        cSh.radius            = 0.15f;
        var cSize             = core.sizeOverLifetime;
        cSize.enabled         = true;
        cSize.size            = new ParticleSystem.MinMaxCurve(1f, afterburnerCfg.innerSizeOverLifetime);
        var cRend             = core.GetComponent<ParticleSystemRenderer>();
        if (diamondMat != null) cRend.material = diamondMat;
        else if (addMat != null) cRend.material = addMat;
        cRend.renderMode      = ParticleSystemRenderMode.Stretch;
        cRend.velocityScale   = 0.05f;

        var light          = abObj.AddComponent<Light>();
        light.type         = LightType.Point;
        light.color        = afterburnerCfg.lightColor;
        light.intensity    = 0f;
        light.range        = afterburnerCfg.lightRange;

        _afterburnerAssembly = abObj.AddComponent<EngineExhaustAssembly>();
        _afterburnerAssembly.Configure(ab, core, light);
    }

    void SetupIdleExhaust()
    {
        if (engineNozzle == null) return;

        var go = new GameObject("IdleExhaustVFX");
        go.transform.SetParent(engineNozzle);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        _idleExhaustPS = go.AddComponent<ParticleSystem>();
        _idleExhaustPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var m              = _idleExhaustPS.main;
        m.duration         = 1f;
        m.loop             = true;
        m.startLifetime    = new ParticleSystem.MinMaxCurve(idleExhaust.lifetimeMin, idleExhaust.lifetimeMax);
        m.startSpeed       = new ParticleSystem.MinMaxCurve(idleExhaust.speedMin,    idleExhaust.speedMax);
        m.startSize        = new ParticleSystem.MinMaxCurve(idleExhaust.sizeMin,     idleExhaust.sizeMax);
        m.startColor       = new ParticleSystem.MinMaxGradient(idleExhaust.colorA,   idleExhaust.colorB);
        m.simulationSpace  = ParticleSystemSimulationSpace.World;
        m.playOnAwake      = false;

        var em             = _idleExhaustPS.emission;
        em.rateOverTime    = idleExhaust.emissionRate;

        var sh             = _idleExhaustPS.shape;
        sh.shapeType       = ParticleSystemShapeType.Cone;
        sh.angle           = idleExhaust.coneAngle;
        sh.radius          = idleExhaust.coneRadius;

        var col            = _idleExhaustPS.colorOverLifetime;
        col.enabled        = true;
        col.color          = idleExhaust.colorOverLifetime;

        var sz             = _idleExhaustPS.sizeOverLifetime;
        sz.enabled         = true;
        sz.size            = new ParticleSystem.MinMaxCurve(1f, idleExhaust.sizeOverLifetime);

        var rend           = _idleExhaustPS.GetComponent<ParticleSystemRenderer>();
        var addMat         = _additiveMaterial != null ? _additiveMaterial : Resources.Load<Material>("VFX/Mat_Additive");
        if (addMat == null)
        {
            var sh2 = Shader.Find("Particles/Additive") ?? Shader.Find("Sprites/Default");
            addMat  = new Material(sh2);
        }
        rend.material      = addMat;
        rend.renderMode    = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = 0.06f;

        _idleLight         = go.AddComponent<Light>();
        _idleLight.type    = LightType.Point;
        _idleLight.color   = idleExhaust.lightColor;
        _idleLight.range   = idleExhaust.lightRange;
        _idleLight.intensity = 0f;
    }

    void SetupHeatHaze()
    {
        if (engineNozzle == null) return;

        var go = new GameObject("HeatHazeVFX");
        go.transform.SetParent(engineNozzle);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        _heatHazePS = go.AddComponent<ParticleSystem>();
        _heatHazePS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var m              = _heatHazePS.main;
        m.duration         = 1f;
        m.loop             = true;
        m.startLifetime    = new ParticleSystem.MinMaxCurve(heatHaze.lifetimeMin, heatHaze.lifetimeMax);
        m.startSpeed       = new ParticleSystem.MinMaxCurve(heatHaze.speedMin,    heatHaze.speedMax);
        m.startSize        = new ParticleSystem.MinMaxCurve(heatHaze.sizeMin,     heatHaze.sizeMax);
        m.startColor       = new ParticleSystem.MinMaxGradient(heatHaze.colorA,   heatHaze.colorB);
        m.simulationSpace  = ParticleSystemSimulationSpace.World;
        m.playOnAwake      = false;

        var em             = _heatHazePS.emission;
        em.rateOverTime    = heatHaze.emissionRate;

        var sh             = _heatHazePS.shape;
        sh.shapeType       = ParticleSystemShapeType.Cone;
        sh.angle           = heatHaze.coneAngle;
        sh.radius          = heatHaze.coneRadius;

        var sz             = _heatHazePS.sizeOverLifetime;
        sz.enabled         = true;
        sz.size            = new ParticleSystem.MinMaxCurve(1f, heatHaze.sizeOverLifetime);

        var col            = _heatHazePS.colorOverLifetime;
        col.enabled        = true;
        col.color          = heatHaze.colorOverLifetime;

        var noise          = _heatHazePS.noise;
        noise.enabled      = true;
        noise.strength     = new ParticleSystem.MinMaxCurve(heatHaze.noiseStrength);
        noise.frequency    = heatHaze.noiseFrequency;
        noise.scrollSpeed  = new ParticleSystem.MinMaxCurve(heatHaze.noiseScrollSpeed);
        noise.damping      = true;

        var rend           = _heatHazePS.GetComponent<ParticleSystemRenderer>();
        Material alphaMat  = _alphaMaterial != null ? _alphaMaterial : Resources.Load<Material>("VFX/Mat_Alpha");
        if (alphaMat != null) rend.material = alphaMat;
        rend.renderMode    = ParticleSystemRenderMode.Billboard;
    }

    // 지상 프리플라이트 중 엔진 IDLE 상태 배기 VFX 제어
    public void SetEngineIdle(bool on)
    {
        _engineIdleOn = on;
        if (on)
        {
            if (_idleExhaustPS != null && !_idleExhaustPS.isPlaying) _idleExhaustPS.Play();
            if (_heatHazePS    != null && !_heatHazePS.isPlaying)    _heatHazePS.Play();
            if (_idleLight     != null) _idleLight.intensity = idleExhaust.lightIntensity;
        }
        else
        {
            _idleExhaustPS?.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            _heatHazePS?.Stop(false,    ParticleSystemStopBehavior.StopEmitting);
            if (_idleLight != null) _idleLight.intensity = 0f;
        }
    }

    // 캐터펄트 발진 직후 호출 — W키 없이 duration 초 동안 애프터버너 자동 활성
    public void TriggerCatapultBoost(float duration = -1f)
    {
        _catapultBoostTimer = duration >= 0f ? duration : catapultCfg.boostDuration;
    }

    void Update()
    {
        if (playerController == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0) return;

        float angle    = Vector3.Angle(lastForward, transform.forward);
        float turnRate = angle / dt;
        lastForward    = transform.forward;

        bool shouldEmitContrails = turnRate > contrailThreshold || playerController.CurrentSpeed > 70f;
        leftContrail.emitting  = shouldEmitContrails;
        rightContrail.emitting = shouldEmitContrails;

        if (_catapultBoostTimer > 0f) _catapultBoostTimer -= dt;
        bool catapultBoosting    = _catapultBoostTimer > 0f && playerController.CurrentSpeed > catapultCfg.minBoostSpeed;
        bool isAfterburnerActive = catapultBoosting || (playerController.CurrentSpeed > catapultCfg.afterburnerMinSpeed && Input.GetKey(KeyCode.W));
        _afterburnerAssembly?.SetActive(isAfterburnerActive, dt);
    }
}

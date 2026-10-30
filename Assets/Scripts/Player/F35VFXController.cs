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
        public Color innerColor       = new Color(1f, 0.86f, 0.62f, 1f);   // 쇼크 다이아몬드 — 주간엔 백황색
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
    bool           _flightVfxStopped;

    PlayerController playerController;
    Vector3 lastForward;

    FXLib.Flare _nozzleGlow;
    float       _glowLevel;
    float       _flickSeed;
    bool        _hazeFlightMode;

    /// <summary>지상 프리플라이트 엔진 IDLE 상태 (엔진 사운드가 참조)</summary>
    public bool EngineIdleOn => _engineIdleOn;
    /// <summary>이번 프레임 애프터버너 연출이 켜져 있는가 (캐터펄트 자동 부스트 포함)</summary>
    public bool AfterburnerOn { get; private set; }

    void Start()
    {
        playerController = GetComponent<PlayerController>();
        lastForward = transform.forward;

        EnsureEngineNozzle();
        SetupContrails();
        SetupAfterburner();
        SetupIdleExhaust();
        SetupHeatHaze();
        SetupNozzleGlow();
        _flickSeed = Random.Range(0f, 100f);
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
        // KF-21 은 VaporTrailRig(실측 날개 끝·물리 기반 응결 조건)가 맡는다 — 같은 자리에 흰 줄이 겹치지 않게
        if (GetComponent<VaporTrailRig>() != null) return;
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
        // HDR 가산 화염 (블룸) — 인스펙터 지정이 없으면 FXLib 절차 셰이더
        Material addMat     = _additiveMaterial     != null ? _additiveMaterial     : FXLib.Glow;
        Material diamondMat = _shockDiamondMaterial != null ? _shockDiamondMaterial : FXLib.Spark;

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

    // 노즐 안쪽 열기 — 엔진 가동 중 항상, AB 에서 강하게 (멀리서도 보이는 꼬리 불빛)
    void SetupNozzleGlow()
    {
        if (engineNozzle == null) return;
        _nozzleGlow = FXLib.MakeFlare("NozzleGlow", engineNozzle, new Vector3(0f, 0f, -0.2f), 0.9f, 0.0035f, 0.15f);
    }

    void UpdateNozzleGlow(bool running, bool ab, float dt)
    {
        if (_nozzleGlow == null) return;
        float thr = playerController != null && playerController.isLocalPlayer ? playerController.Throttle01 : 0.6f;
        float target = !running ? 0f : ab ? 1f : Mathf.Lerp(0.12f, 0.3f, thr);
        _glowLevel = Mathf.MoveTowards(_glowLevel, target, dt * (target > _glowLevel ? 4f : 1.5f));
        // 저출력은 검붉은 열기, AB 는 주황→백열
        Color c = Color.Lerp(new Color(1f, 0.32f, 0.08f), new Color(1f, 0.72f, 0.42f), Mathf.InverseLerp(0.3f, 1f, _glowLevel));
        float flick = 0.9f + 0.2f * Mathf.PerlinNoise(Time.time * 18f, _flickSeed);
        _nozzleGlow.Set(c, _glowLevel * 7f * flick);
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
        // 아이들 배기는 불꽃이 아니라 희미한 열기 — 부드러운 저강도 글로우
        rend.sharedMaterial = _additiveMaterial != null ? _additiveMaterial : FXLib.GlowSoft;
        rend.renderMode    = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = 0.06f;

        _idleLight         = go.AddComponent<Light>();
        _idleLight.type    = LightType.Point;
        _idleLight.color   = idleExhaust.lightColor;
        _idleLight.range   = idleExhaust.lightRange;
        _idleLight.intensity = 0f;
        _idleLight.enabled   = false;
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

        // 실제 굴절 아지랑이 (불투명 텍스처 왜곡) — 색이 아니라 배경이 일렁인다
        m.startColor = Color.white;
        col.color = FXLib.Grad(new[] { Color.white, Color.white }, new[] { 0f, 1f },
                               new[] { 0f, 0.85f, 0.4f, 0f }, new[] { 0f, 0.12f, 0.5f, 1f });
        var rend           = _heatHazePS.GetComponent<ParticleSystemRenderer>();
        rend.sharedMaterial = _alphaMaterial != null ? _alphaMaterial : FXLib.HeatHaze;
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
            if (_idleLight     != null) { _idleLight.intensity = idleExhaust.lightIntensity; _idleLight.enabled = true; }
        }
        else
        {
            _idleExhaustPS?.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            _heatHazePS?.Stop(false,    ParticleSystemStopBehavior.StopEmitting);
            if (_idleLight != null) { _idleLight.intensity = 0f; _idleLight.enabled = false; }
        }
    }

    // 캐터펄트 발진 직후 호출 — W키 없이 duration 초 동안 애프터버너 자동 활성
    public void TriggerCatapultBoost(float duration = -1f)
    {
        _catapultBoostTimer = duration >= 0f ? duration : catapultCfg.boostDuration;
    }

    // 하차·재출격 시 GameModeManager 가 이 컴포넌트를 끈다 — Update 가 멈추면 마지막 애프터버너 상태가
    // 그대로 남아 꺼진 기체 꼬리에서 불꽃이 계속 타므로, 비활성 시점에 반드시 소등한다.
    void OnDisable()
    {
        StopFlightVFX();
    }

    // 열 아지랑이는 지상(갑판 아이들)에서만. 굴절 셰이더는 불투명 화면 사본을 쓰는데 구름층은 반투명이라
    // 사본에 없다 — 비행 중 배기 뒤로 구름이 있으면 그 자리에 구름 빠진 짙은 남색 하늘이 패치처럼 드러났다.
    void SetHazeFlightMode(bool flying)
    {
        if (_heatHazePS == null || flying == _hazeFlightMode) return;
        _hazeFlightMode = flying;
        if (flying) _heatHazePS.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        else if (_engineIdleOn && !_heatHazePS.isPlaying) _heatHazePS.Play();
    }

    void StopFlightVFX()
    {
        _catapultBoostTimer = 0f;
        if (leftContrail  != null) leftContrail.emitting  = false;
        if (rightContrail != null) rightContrail.emitting = false;
        _afterburnerAssembly?.ForceOff();
        AfterburnerOn = false;
        _flightVfxStopped = true;
    }

    void Update()
    {
        if (playerController == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0) return;

        // 조종 중이 아닐 때(콕핏 탑승 대기·사망 잔해) 는 비행 VFX 를 끈다 — 엔진 IDLE 배기는 별도 제어
        if (!playerController.enabled)
        {
            if (!_flightVfxStopped) StopFlightVFX();
            UpdateNozzleGlow(_engineIdleOn, false, dt);
            SetHazeFlightMode(false);
            lastForward = transform.forward;
            return;
        }
        _flightVfxStopped = false;

        float angle    = Vector3.Angle(lastForward, transform.forward);
        float turnRate = angle / dt;
        lastForward    = transform.forward;

        bool shouldEmitContrails = turnRate > contrailThreshold || playerController.CurrentSpeed > 70f;
        if (leftContrail  != null) leftContrail.emitting  = shouldEmitContrails;
        if (rightContrail != null) rightContrail.emitting = shouldEmitContrails;

        if (_catapultBoostTimer > 0f) _catapultBoostTimer -= dt;
        bool catapultBoosting    = _catapultBoostTimer > 0f && playerController.CurrentSpeed > catapultCfg.minBoostSpeed;
        // Shift 홀드 애프터버너 (연료 있을 때)가 켜지면 VFX 강제 활성.
        // 폴백: 기존 로직 (고속 + W 홀드).
        // SimInput 은 이 PC 의 조종 입력 — 원격 기체에 적용하면 내가 스로틀을 밀 때 남의 AB 가 켜진다
        bool local = playerController.isLocalPlayer;
        bool isAfterburnerActive = catapultBoosting
            || playerController.AfterburnerActive
            || (local && playerController.CurrentSpeed > catapultCfg.afterburnerMinSpeed && SimInput.Throttle > 0.5f);
        AfterburnerOn = isAfterburnerActive;
        _afterburnerAssembly?.SetActive(isAfterburnerActive, dt);

        bool running = playerController.CurrentSpeed > 1f || _engineIdleOn;
        UpdateNozzleGlow(running, isAfterburnerActive, dt);
        SetHazeFlightMode(playerController.CurrentSpeed > 20f);
    }
}

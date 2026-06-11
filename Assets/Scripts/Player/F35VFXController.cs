using UnityEngine;

public class F35VFXController : MonoBehaviour
{
    // EscortVFXController가 동일 위치를 참조할 수 있도록 노즐 로컬 좌표를 공유
    public static Vector3 SharedNozzleLocalPos = new Vector3(0f, 0f, -4.0f);

    [Header("Contrails")]
    [SerializeField] Transform leftWingtip;
    [SerializeField] Transform rightWingtip;
    [SerializeField] float contrailThreshold = 15f;

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
    [Tooltip("Contrail용 알파 머티리얼")]
    [SerializeField] Material _alphaMaterial;

    TrailRenderer leftContrail;
    TrailRenderer rightContrail;

    EngineExhaustAssembly _afterburnerAssembly;

    [Header("Engine Idle Exhaust")]
    ParticleSystem _idleExhaustPS;
    ParticleSystem _heatHazePS;
    Light          _idleLight;
    bool           _engineIdleOn;

    // 캐터펄트 발진 후 W키 없이 자동 애프터버너 유지 타이머
    float _catapultBoostTimer;

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
            nozzle.transform.localPosition = new Vector3(0f, 0f, -4.0f);
            engineNozzle = nozzle.transform;
        }
        // 에스코트 VFX가 동일 위치를 사용할 수 있도록 기록
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

        leftContrail = CreateContrail(leftWingtip);
        rightContrail = CreateContrail(rightWingtip);
    }

    TrailRenderer CreateContrail(Transform parent)
    {
        var tr = parent.gameObject.AddComponent<TrailRenderer>();
        tr.time = 1.5f;
        tr.startWidth = 0.4f;
        tr.endWidth = 2.0f;
        tr.minVertexDistance = 0.2f;
        tr.emitting = false;

        Material alphaMat = _alphaMaterial != null ? _alphaMaterial : Resources.Load<Material>("VFX/Mat_Alpha");
        if (alphaMat != null) tr.material = alphaMat;

        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.8f, 0.8f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        tr.colorGradient = grad;

        return tr;
    }

    // 프리팹이 있으면 그것 인스턴스화 후 EngineExhaustAssembly 사용,
    // 없으면 절차 빌드 후 동적으로 어셈블리 부착.
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

        // ── 절차적 폴백 ────────────────────────────────────────────────────────
        Material addMat     = _additiveMaterial     != null ? _additiveMaterial     : Resources.Load<Material>("VFX/Mat_Additive");
        Material diamondMat = _shockDiamondMaterial != null ? _shockDiamondMaterial : Resources.Load<Material>("VFX/Mat_ShockDiamond");

        GameObject abObj = new GameObject("AfterburnerVFX");
        abObj.transform.SetParent(engineNozzle);
        abObj.transform.localPosition = Vector3.zero;
        abObj.transform.localRotation = Quaternion.identity;

        // Outer Glow (Orange/Red)
        var afterburnerPS = abObj.AddComponent<ParticleSystem>();
        afterburnerPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = afterburnerPS.main;
        main.duration = 1f;
        main.loop = true;
        main.startLifetime = 0.12f;
        main.startSpeed = 40f;
        main.startSize = 1.5f;
        main.startColor = new Color(1f, 0.4f, 0.05f, 0.8f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.playOnAwake = true;

        var emission = afterburnerPS.emission;
        emission.rateOverTime = 80f;

        var shape = afterburnerPS.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 1f;
        shape.radius = 0.35f;

        var size = afterburnerPS.sizeOverLifetime;
        size.enabled = true;
        AnimationCurve curve = new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0.2f));
        size.size = new ParticleSystem.MinMaxCurve(1.0f, curve);

        var rend = afterburnerPS.GetComponent<ParticleSystemRenderer>();
        if (addMat != null) rend.material = addMat;
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = 0.08f;

        // Inner Core (Blue/White shock diamonds)
        GameObject coreObj = new GameObject("AfterburnerCore");
        coreObj.transform.SetParent(abObj.transform);
        coreObj.transform.localPosition = Vector3.zero;
        coreObj.transform.localRotation = Quaternion.identity;

        var corePS = coreObj.AddComponent<ParticleSystem>();
        corePS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var cMain = corePS.main;
        cMain.duration = 1f;
        cMain.loop = true;
        cMain.startLifetime = 0.1f;
        cMain.startSpeed = 50f;
        cMain.startSize = 0.8f;
        cMain.startColor = new Color(0.7f, 0.9f, 1f, 1f);
        cMain.simulationSpace = ParticleSystemSimulationSpace.Local;
        cMain.playOnAwake = true;

        var cEmission = corePS.emission;
        cEmission.rateOverTime = 60f;

        var cShape = corePS.shape;
        cShape.shapeType = ParticleSystemShapeType.Cone;
        cShape.angle = 0f;
        cShape.radius = 0.15f;

        var cSize = corePS.sizeOverLifetime;
        cSize.enabled = true;
        AnimationCurve cCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.2f, 0.4f),
            new Keyframe(0.4f, 1f),
            new Keyframe(0.6f, 0.4f),
            new Keyframe(0.8f, 1f),
            new Keyframe(1f, 0.2f)
        );
        cSize.size = new ParticleSystem.MinMaxCurve(1.0f, cCurve);

        var cRend = corePS.GetComponent<ParticleSystemRenderer>();
        if (diamondMat != null) cRend.material = diamondMat;
        else if (addMat != null) cRend.material = addMat;
        cRend.renderMode = ParticleSystemRenderMode.Stretch;
        cRend.velocityScale = 0.05f;

        var afterburnerLight = abObj.AddComponent<Light>();
        afterburnerLight.type = LightType.Point;
        afterburnerLight.color = new Color(1f, 0.6f, 0.2f);
        afterburnerLight.intensity = 0f;
        afterburnerLight.range = 20f;

        // 동적으로 어셈블리 부착해서 통일된 API로 제어
        _afterburnerAssembly = abObj.AddComponent<EngineExhaustAssembly>();
        _afterburnerAssembly.Configure(afterburnerPS, corePS, afterburnerLight);
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

        var m = _idleExhaustPS.main;
        m.duration        = 1f;
        m.loop            = true;
        m.startLifetime   = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        m.startSpeed      = new ParticleSystem.MinMaxCurve(6f, 14f);
        m.startSize       = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        m.startColor      = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.55f, 0.15f, 0.65f),
            new Color(0.75f, 0.30f, 0.05f, 0.30f));
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.playOnAwake     = false;

        var em = _idleExhaustPS.emission;
        em.rateOverTime = 30f;

        var sh = _idleExhaustPS.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle     = 4f;
        sh.radius    = 0.28f;

        var col = _idleExhaustPS.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(1.0f, 0.65f, 0.25f), 0f),
                new GradientColorKey(new Color(0.55f, 0.55f, 0.55f), 1f) },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.7f, 0f),
                new GradientAlphaKey(0.0f, 1f) });
        col.color = g;

        var sz = _idleExhaustPS.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 1.8f)));

        var rend = _idleExhaustPS.GetComponent<ParticleSystemRenderer>();
        var addMat = _additiveMaterial != null ? _additiveMaterial : Resources.Load<Material>("VFX/Mat_Additive");
        if (addMat == null)
        {
            var fallbackSh = Shader.Find("Particles/Additive")
                          ?? Shader.Find("Sprites/Default");
            addMat = new Material(fallbackSh);
        }
        rend.material   = addMat;
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = 0.06f;

        _idleLight = go.AddComponent<Light>();
        _idleLight.type      = LightType.Point;
        _idleLight.color     = new Color(1f, 0.50f, 0.10f);
        _idleLight.range     = 7f;
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

        var m = _heatHazePS.main;
        m.duration        = 1f;
        m.loop            = true;
        m.startLifetime   = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        m.startSpeed      = new ParticleSystem.MinMaxCurve(12f, 28f);
        m.startSize       = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        // 거의 투명한 따뜻한 흰색 — 열기 아지랑이 느낌
        m.startColor      = new ParticleSystem.MinMaxGradient(
            new Color(1.0f, 0.92f, 0.75f, 0.10f),
            new Color(0.90f, 0.88f, 0.82f, 0.05f));
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.playOnAwake     = false;

        var em = _heatHazePS.emission;
        em.rateOverTime = 18f;

        var sh = _heatHazePS.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle     = 10f;
        sh.radius    = 0.35f;

        // 크기가 점점 커지며 퍼지는 아지랑이
        var sz = _heatHazePS.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 3.5f)));

        // 투명도: 시작에서 급격히 나타났다 서서히 사라짐
        var col = _heatHazePS.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(1.0f, 0.95f, 0.80f), 0.0f),
                new GradientColorKey(new Color(0.92f, 0.90f, 0.85f), 0.4f),
                new GradientColorKey(new Color(0.85f, 0.85f, 0.85f), 1.0f) },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(0.12f, 0.15f),
                new GradientAlphaKey(0.0f, 1.0f) });
        col.color = g;

        // 노이즈 모듈 — 아지랑이 물결 움직임
        var noise = _heatHazePS.noise;
        noise.enabled   = true;
        noise.strength  = new ParticleSystem.MinMaxCurve(0.35f);
        noise.frequency = 0.6f;
        noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.4f);
        noise.damping   = true;

        var rend = _heatHazePS.GetComponent<ParticleSystemRenderer>();
        Material alphaMat = _alphaMaterial != null ? _alphaMaterial : Resources.Load<Material>("VFX/Mat_Alpha");
        if (alphaMat != null) rend.material = alphaMat;
        rend.renderMode = ParticleSystemRenderMode.Billboard;
    }

    // 지상 프리플라이트 중 엔진 IDLE 상태 배기 VFX 제어
    public void SetEngineIdle(bool on)
    {
        _engineIdleOn = on;
        if (on)
        {
            if (_idleExhaustPS != null && !_idleExhaustPS.isPlaying) _idleExhaustPS.Play();
            if (_heatHazePS    != null && !_heatHazePS.isPlaying)    _heatHazePS.Play();
            if (_idleLight     != null) _idleLight.intensity = 1.8f;
        }
        else
        {
            _idleExhaustPS?.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            _heatHazePS?.Stop(false,    ParticleSystemStopBehavior.StopEmitting);
            if (_idleLight != null) _idleLight.intensity = 0f;
        }
    }

    // 캐터펄트 발진 직후 호출 — W키 없이 duration 초 동안 애프터버너 자동 활성
    public void TriggerCatapultBoost(float duration = 8f)
    {
        _catapultBoostTimer = duration;
    }

    void Update()
    {
        if (playerController == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0) return;

        float angle = Vector3.Angle(lastForward, transform.forward);
        float turnRate = angle / dt;
        lastForward = transform.forward;

        bool shouldEmitContrails = turnRate > contrailThreshold || playerController.CurrentSpeed > 70f;
        leftContrail.emitting = shouldEmitContrails;
        rightContrail.emitting = shouldEmitContrails;

        if (_catapultBoostTimer > 0f) _catapultBoostTimer -= dt;
        bool catapultBoosting    = _catapultBoostTimer > 0f && playerController.CurrentSpeed > 5f;
        bool isAfterburnerActive = catapultBoosting || (playerController.CurrentSpeed > 60f && Input.GetKey(KeyCode.W));
        _afterburnerAssembly?.SetActive(isAfterburnerActive, dt);
    }
}

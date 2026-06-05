using UnityEngine;

public class F35VFXController : MonoBehaviour
{
    [Header("Contrails")]
    [SerializeField] Transform leftWingtip;
    [SerializeField] Transform rightWingtip;
    [SerializeField] float contrailThreshold = 15f;

    TrailRenderer leftContrail;
    TrailRenderer rightContrail;

    [Header("Afterburner")]
    [SerializeField] Transform engineNozzle;
    ParticleSystem afterburnerPS;
    ParticleSystem corePS;
    Light afterburnerLight;

    [Header("Engine Idle Exhaust")]
    ParticleSystem _idleExhaustPS;
    Light          _idleLight;
    bool           _engineIdleOn;

    PlayerController playerController;
    Vector3 lastForward;

    void Start()
    {
        playerController = GetComponent<PlayerController>();
        lastForward = transform.forward;

        SetupContrails();
        SetupAfterburner();
        SetupIdleExhaust();
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
        
        Material alphaMat = Resources.Load<Material>("VFX/Mat_Alpha");
        if (alphaMat != null) tr.material = alphaMat;

        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.8f, 0.8f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        tr.colorGradient = grad;

        return tr;
    }

    void SetupAfterburner()
    {
        if (engineNozzle == null)
        {
            GameObject nozzle = new GameObject("EngineNozzle");
            nozzle.transform.SetParent(transform);
            nozzle.transform.localPosition = new Vector3(0f, 0f, -4.0f);
            engineNozzle = nozzle.transform;
        }

        Material addMat = Resources.Load<Material>("VFX/Mat_Additive");
        Material diamondMat = Resources.Load<Material>("VFX/Mat_ShockDiamond");

        GameObject abObj = new GameObject("AfterburnerVFX");
        abObj.transform.SetParent(engineNozzle);
        abObj.transform.localPosition = Vector3.zero;
        abObj.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);

        // Outer Glow (Orange/Red)
        afterburnerPS = abObj.AddComponent<ParticleSystem>();
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

        corePS = coreObj.AddComponent<ParticleSystem>();
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

        afterburnerLight = abObj.AddComponent<Light>();
        afterburnerLight.type = LightType.Point;
        afterburnerLight.color = new Color(1f, 0.6f, 0.2f);
        afterburnerLight.intensity = 0f;
        afterburnerLight.range = 20f;
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
        var addMat = Resources.Load<Material>("VFX/Mat_Additive");
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

    // 지상 프리플라이트 중 엔진 IDLE 상태 배기 VFX 제어
    public void SetEngineIdle(bool on)
    {
        _engineIdleOn = on;
        if (_idleExhaustPS == null) return;
        if (on)
        {
            if (!_idleExhaustPS.isPlaying) _idleExhaustPS.Play();
            _idleLight.intensity = 1.8f;
        }
        else
        {
            _idleExhaustPS.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            _idleLight.intensity = 0f;
        }
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

        bool isAfterburnerActive = playerController.CurrentSpeed > 60f && Input.GetKey(KeyCode.W);
        
        if (isAfterburnerActive)
        {
            if (!afterburnerPS.isPlaying) afterburnerPS.Play();
            if (corePS != null && !corePS.isPlaying) corePS.Play();
            afterburnerLight.intensity = Mathf.Lerp(afterburnerLight.intensity, 5f, dt * 10f);
        }
        else
        {
            if (afterburnerPS.isPlaying) afterburnerPS.Stop();
            if (corePS != null && corePS.isPlaying) corePS.Stop();
            afterburnerLight.intensity = Mathf.Lerp(afterburnerLight.intensity, 0f, dt * 10f);
        }
    }
}

using UnityEngine;

// 에스코트 AI 전투기 엔진 VFX.
// F35VFXController와 동일한 파티클 구성 — 플레이어 입력 대신 AIBotController.Speed로 제어.
public class EscortVFXController : MonoBehaviour
{
    [SerializeField] Vector3 nozzleLocalPos      = new Vector3(0f, 0f, -4.0f);
    [SerializeField] float   afterburnerThreshold = 15f;

    ParticleSystem _afterburnerPS;
    ParticleSystem _corePS;
    Light          _afterburnerLight;

    AIBotController _bot;

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

        Material addMat     = Resources.Load<Material>("VFX/Mat_Additive");
        Material diamondMat = Resources.Load<Material>("VFX/Mat_ShockDiamond");

        // ── 외부 글로우 (주황/적색) ─────────────────────────────────────────
        var abObj = new GameObject("AfterburnerVFX");
        abObj.transform.SetParent(nozzle, false);
        // Euler(180,0,0): 방출 방향을 항공기 후방(-Z)으로 명시. World 공간에서 기체 뒤로 파티클이 남는다.
        abObj.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);

        _afterburnerPS = abObj.AddComponent<ParticleSystem>();
        _afterburnerPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main       = _afterburnerPS.main;
        main.duration  = 1f; main.loop = true;
        main.startLifetime = 0.15f; main.startSpeed = 35f; main.startSize = 1.5f;
        main.startColor = new Color(1f, 0.4f, 0.05f, 0.8f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = _afterburnerPS.emission; em.rateOverTime = 80f;
        var sh = _afterburnerPS.shape;
        sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 1f; sh.radius = 0.35f;

        var sz = _afterburnerPS.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0.2f)));

        var rend = _afterburnerPS.GetComponent<ParticleSystemRenderer>();
        if (addMat != null) rend.material = addMat;
        rend.renderMode = ParticleSystemRenderMode.Stretch; rend.velocityScale = 0.08f;

        // ── 내부 코어 (청/백색 쇼크 다이아몬드) ────────────────────────────
        var coreObj = new GameObject("AfterburnerCore");
        coreObj.transform.SetParent(abObj.transform, false);

        _corePS = coreObj.AddComponent<ParticleSystem>();
        _corePS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var cm = _corePS.main;
        cm.duration = 1f; cm.loop = true;
        cm.startLifetime = 0.12f; cm.startSpeed = 45f; cm.startSize = 0.8f;
        cm.startColor = new Color(0.7f, 0.9f, 1f, 1f);
        cm.simulationSpace = ParticleSystemSimulationSpace.World;

        var cem = _corePS.emission; cem.rateOverTime = 60f;
        var csh = _corePS.shape;
        csh.shapeType = ParticleSystemShapeType.Cone; csh.angle = 0f; csh.radius = 0.15f;

        var csz = _corePS.sizeOverLifetime; csz.enabled = true;
        csz.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(
                new Keyframe(0f,  1f),  new Keyframe(0.2f, 0.4f),
                new Keyframe(0.4f, 1f), new Keyframe(0.6f, 0.4f),
                new Keyframe(0.8f, 1f), new Keyframe(1f,  0.2f)));

        var crend = _corePS.GetComponent<ParticleSystemRenderer>();
        if (diamondMat != null) crend.material = diamondMat;
        else if (addMat != null) crend.material = addMat;
        crend.renderMode = ParticleSystemRenderMode.Stretch; crend.velocityScale = 0.05f;

        // ── 엔진 광원 ────────────────────────────────────────────────────────
        _afterburnerLight                = abObj.AddComponent<Light>();
        _afterburnerLight.type           = LightType.Point;
        _afterburnerLight.color          = new Color(1f, 0.6f, 0.2f);
        _afterburnerLight.intensity      = 0f;
        _afterburnerLight.range          = 20f;
    }

    void Update()
    {
        if (_bot == null) return;
        float dt     = Time.deltaTime;
        bool  active = _bot.Speed > afterburnerThreshold;

        if (active)
        {
            if (_afterburnerPS != null && !_afterburnerPS.isPlaying) _afterburnerPS.Play();
            if (_corePS        != null && !_corePS.isPlaying)        _corePS.Play();
            if (_afterburnerLight != null)
                _afterburnerLight.intensity = Mathf.Lerp(_afterburnerLight.intensity, 5f, dt * 10f);
        }
        else
        {
            if (_afterburnerPS != null && _afterburnerPS.isPlaying) _afterburnerPS.Stop();
            if (_corePS        != null && _corePS.isPlaying)        _corePS.Stop();
            if (_afterburnerLight != null)
                _afterburnerLight.intensity = Mathf.Lerp(_afterburnerLight.intensity, 0f, dt * 10f);
        }
    }
}

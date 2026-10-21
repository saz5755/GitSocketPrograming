using UnityEngine;

/// <summary>
/// 플레어 VFX — 실제 마그네슘 플레어 레퍼런스 기반 다층 구조.
///   1) Core        — 작고 매우 밝은 백색 핵 (Local)
///   2) HotShell    — 황색 가스 쉘 (Local, Noise)
///   3) GlowHalo    — 외곽 광륜, 멀리서도 보이는 glow (Local)
///   4) HotTrail    — Trail Renderer, 뜨거운 궤적 자취
///   5) Sparks      — 비산 불꽃 입자 (World, 중력)
///   6) Smoke       — 연기 기둥 (World, 상승)
///   7) Light       — Point Light with intensity flicker + 감쇠 커브
/// </summary>
public class FlareDecoyVFX : MonoBehaviour
{
    Material _injectedAddMat;
    Material _injectedAlphaMat;
    Light    _light;
    float    _lightTime;
    float    _lifetime;

    public static void Attach(GameObject host, float lifetime,
                              Material additiveMatOverride = null, Material alphaMatOverride = null)
    {
        var vfx = host.AddComponent<FlareDecoyVFX>();
        vfx._injectedAddMat   = additiveMatOverride;
        vfx._injectedAlphaMat = alphaMatOverride;
        vfx.InitVFX(lifetime);
    }

    static ParticleSystem MakePS(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        // AddComponent는 즉시 Play → duration/모듈 설정 전 반드시 Stop
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return ps;
    }

    void InitVFX(float lifetime)
    {
        _lifetime = lifetime;

        Material addMat   = _injectedAddMat   != null ? _injectedAddMat   : Resources.Load<Material>("VFX/Mat_Additive");
        Material alphaMat = _injectedAlphaMat != null ? _injectedAlphaMat : Resources.Load<Material>("VFX/Mat_Alpha");

        BuildCore(lifetime, addMat);
        BuildHotShell(lifetime, addMat);
        BuildGlowHalo(lifetime, addMat);
        BuildHotTrail(lifetime);
        BuildSparks(lifetime, addMat);
        BuildSecondarySparkBurst(addMat);
        BuildSmoke(lifetime, alphaMat);
        BuildLight();

        Destroy(gameObject, lifetime + 6.0f);
    }

    // 1) Core — 작고 밝은 백색 핵. 실제 마그네슘은 백색이 지배적.
    void BuildCore(float lifetime, Material addMat)
    {
        var ps   = MakePS(gameObject, "Flare_Core");
        var main = ps.main;
        main.duration        = lifetime;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
        main.startSpeed      = 0f;
        main.startSize       = new ParticleSystem.MinMaxCurve(0.4f, 0.65f);
        main.startColor      = new Color(1f, 1f, 1f, 1f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var em = ps.emission; em.rateOverTime = 220f;    // 320 → 220 (덜 과함)
        var sh = ps.shape;    sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.1f;

        // 색상 거의 백색만 유지 — 끝에 미세한 황색 트랜지션
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 1f, 1f),         0f),
                new GradientColorKey(new Color(1f, 0.99f, 0.95f),   0.5f),
                new GradientColorKey(new Color(1f, 0.95f, 0.8f),    1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var siz = ps.sizeOverLifetime; siz.enabled = true;
        siz.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 1.2f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.4f)));

        var r = ps.GetComponent<ParticleSystemRenderer>();
        if (addMat != null) r.material = addMat;
        ps.Play();
    }

    // 2) HotShell — 황백색 가스 쉘. 주황색 과잉 제거, 투명도 낮춤.
    void BuildHotShell(float lifetime, Material addMat)
    {
        var ps   = MakePS(gameObject, "Flare_HotShell");
        var main = ps.main;
        main.duration        = lifetime;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.1f, 0.18f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);  // 1.3~2.0 → 작게
        main.startColor      = new Color(1f, 0.95f, 0.75f, 1f);             // 더 백색
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var em = ps.emission; em.rateOverTime = 70f;   // 150 → 70 (덜 과함)
        var sh = ps.shape;    sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.25f;

        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 0.98f, 0.9f),   0f),
                new GradientColorKey(new Color(1f, 0.9f,  0.6f),   0.5f),
                new GradientColorKey(new Color(1f, 0.75f, 0.35f),  1f),   // 끝에만 미세한 황색
            },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.5f, 0.2f),
                    new GradientAlphaKey(0.3f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var siz = ps.sizeOverLifetime; siz.enabled = true;
        siz.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.6f)));

        var noise = ps.noise;
        noise.enabled      = true;
        noise.strength     = 0.6f;
        noise.frequency    = 2.5f;
        noise.scrollSpeed  = 1.5f;
        noise.quality      = ParticleSystemNoiseQuality.High;

        var r = ps.GetComponent<ParticleSystemRenderer>();
        if (addMat != null) r.material = addMat;
        ps.Play();
    }

    // 3) GlowHalo — 외곽 광륜. 반투명 황백색 glow (주황색 제거).
    void BuildGlowHalo(float lifetime, Material addMat)
    {
        var ps   = MakePS(gameObject, "Flare_GlowHalo");
        var main = ps.main;
        main.duration        = lifetime;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
        main.startSpeed      = 0f;
        main.startSize       = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);  // 5~8 → 작게
        main.startColor      = new Color(1f, 0.9f, 0.65f, 0.25f);           // 황백색 + 투명도 낮춤
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var em = ps.emission; em.rateOverTime = 25f;   // 55 → 25

        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 0.95f, 0.8f),  0f),
                new GradientColorKey(new Color(1f, 0.8f,  0.5f),  0.5f),
                new GradientColorKey(new Color(1f, 0.65f, 0.3f),  1f),
            },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.3f, 0.3f),
                    new GradientAlphaKey(0.15f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var siz = ps.sizeOverLifetime; siz.enabled = true;
        siz.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.5f, 1.3f), new Keyframe(1f, 1.8f)));

        var r = ps.GetComponent<ParticleSystemRenderer>();
        if (addMat != null) r.material = addMat;
        ps.Play();
    }

    // 4) HotTrail — Trail Renderer로 뜨거운 궤적 자취 생성.
    void BuildHotTrail(float lifetime)
    {
        var go = new GameObject("Flare_HotTrail");
        go.transform.SetParent(transform, false);
        var tr = go.AddComponent<TrailRenderer>();
        tr.time           = 0.45f;
        tr.startWidth     = 0.6f;
        tr.endWidth       = 0.05f;
        tr.minVertexDistance = 0.08f;
        tr.autodestruct   = false;

        // URP Unlit 반투명 — Resources 폴백
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Particles/Standard Unlit")
                 ?? Shader.Find("Sprites/Default");
        if (sh != null)
        {
            var mat = new Material(sh);
            tr.material = mat;
        }

        var grad = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 0.98f, 0.9f),  0f),
                new GradientColorKey(new Color(1f, 0.7f, 0.3f),   0.4f),
                new GradientColorKey(new Color(1f, 0.3f, 0.05f),  1f),
            },
            new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.4f, 0.6f),
                    new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = grad;

        Destroy(go, lifetime + 1.0f);
    }

    // 5) Sparks — 비산 불꽃. 백색/황색 중심, 수 감소.
    void BuildSparks(float lifetime, Material addMat)
    {
        var ps   = MakePS(gameObject, "Flare_Sparks");
        var main = ps.main;
        main.duration        = lifetime;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.5f, 1.8f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(5f, 14f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
        main.startColor      = new Color(1f, 0.98f, 0.85f, 1f);  // 더 백색
        main.gravityModifier = 1.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission; em.rateOverTime = 95f;   // 180 → 95 (덜 과함)
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius    = 0.3f;

        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 1f, 0.95f),    0f),
                new GradientColorKey(new Color(1f, 0.9f, 0.55f),  0.4f),
                new GradientColorKey(new Color(1f, 0.6f, 0.15f),  0.85f),  // 끝에만 소량 주황
                new GradientColorKey(new Color(0.3f, 0.1f, 0f),   1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f),
                    new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var siz = ps.sizeOverLifetime; siz.enabled = true;
        siz.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.8f, 0.4f), new Keyframe(1f, 0f)));

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space   = ParticleSystemSimulationSpace.World;
        // x/y/z 커브는 모두 같은 모드여야 함 — 모두 TwoConstants로 통일 (random 범위).
        vel.x       = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
        vel.y       = new ParticleSystem.MinMaxCurve(-1f, 1f);
        vel.z       = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        if (addMat != null) r.material = addMat;
        r.renderMode    = ParticleSystemRenderMode.Stretch;
        r.lengthScale   = 4f;
        r.velocityScale = 0.08f;
        ps.Play();
    }

    // 5b) 초기 폭발적 스파크 — 발사 순간 짧은 급방출 (burst). 백색 중심.
    void BuildSecondarySparkBurst(Material addMat)
    {
        var ps   = MakePS(gameObject, "Flare_InitialBurst");
        var main = ps.main;
        main.duration        = 0.3f;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(10f, 24f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.1f, 0.25f);
        main.startColor      = new Color(1f, 1f, 0.95f, 1f);
        main.gravityModifier = 0.8f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });   // 60 → 30

        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.2f;

        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(Color.white,                 0f),
                new GradientColorKey(new Color(1f, 0.9f, 0.5f),   0.6f),  // 황색 (주황 → 황색)
                new GradientColorKey(new Color(0.3f, 0.2f, 0.1f), 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var r = ps.GetComponent<ParticleSystemRenderer>();
        if (addMat != null) r.material = addMat;
        r.renderMode    = ParticleSystemRenderMode.Stretch;
        r.lengthScale   = 5f;
        r.velocityScale = 0.1f;
        ps.Play();
    }

    // 6) Smoke — 뭉게뭉게 연기 기둥. World 공간, 상승 velocity.
    void BuildSmoke(float lifetime, Material alphaMat)
    {
        var ps   = MakePS(gameObject, "Flare_Smoke");
        var main = ps.main;
        main.duration        = lifetime;
        main.loop            = false;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(3.5f, 7f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        main.startSize       = new ParticleSystem.MinMaxCurve(2.5f, 6f);
        main.startColor      = new Color(0.95f, 0.92f, 0.88f, 0.9f);
        main.startRotation   = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission; em.rateOverTime = 55f;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius    = 0.6f;

        // 상승 velocity + 느린 수평 산포
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space   = ParticleSystemSimulationSpace.World;
        vel.y       = new ParticleSystem.MinMaxCurve(0.8f, 2.5f);
        vel.x       = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
        vel.z       = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 0.95f, 0.85f),   0f),   // 뜨거운 흰색
                new GradientColorKey(new Color(0.75f, 0.75f, 0.75f), 0.3f), // 밝은 회색
                new GradientColorKey(new Color(0.45f, 0.45f, 0.45f), 1f),   // 어두운 회색
            },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.08f),
                    new GradientAlphaKey(0.55f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var siz = ps.sizeOverLifetime; siz.enabled = true;
        siz.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(0.4f, 0.8f), new Keyframe(1f, 2.2f)));

        var noise = ps.noise;
        noise.enabled     = true;
        noise.strength    = 0.4f;
        noise.frequency   = 0.6f;
        noise.scrollSpeed = 0.3f;
        noise.quality     = ParticleSystemNoiseQuality.Medium;

        var rot = ps.rotationOverLifetime; rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-15f * Mathf.Deg2Rad, 15f * Mathf.Deg2Rad);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        if (alphaMat != null) r.material = alphaMat;
        ps.Play();
    }

    // 7) Light — 동적 포인트 라이트. 백색 중심, intensity flicker + 시간 감쇠.
    void BuildLight()
    {
        _light           = gameObject.AddComponent<Light>();
        _light.type      = LightType.Point;
        _light.color     = new Color(1f, 0.95f, 0.8f);   // 더 백색 (주황빛 완화)
        _light.intensity = 15f;                           // 18 → 15
        _light.range     = 100f;                          // 120 → 100
        _light.shadows   = LightShadows.None;
        _lightTime       = 0f;
    }

    void Update()
    {
        if (_light == null || _lifetime <= 0f) return;
        _lightTime += Time.deltaTime;
        float t     = Mathf.Clamp01(_lightTime / _lifetime);

        // 감쇠 커브: 초반 밝음 → 중반 유지 → 후반 급감
        float decay = t < 0.1f ? Mathf.Lerp(0.6f, 1f, t / 0.1f)
                     : t < 0.7f ? 1f
                     : Mathf.Lerp(1f, 0f, (t - 0.7f) / 0.3f);

        // 플리커: 마그네슘 연소 특유의 불안정 발광
        float flicker = 0.85f + 0.25f * Mathf.PerlinNoise(_lightTime * 18f, 0f);

        _light.intensity = 15f * decay * flicker;
        _light.range     = 100f * Mathf.Max(0.3f, decay);
    }
}

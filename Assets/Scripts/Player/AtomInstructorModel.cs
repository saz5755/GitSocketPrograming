using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 비행 교관 NPC 의 홀로그램 원자 모델 (프로젝트 favicon 로고 모티브) — 전부 절차적으로 생성한다.
///
///   · 코어   : 청록색 발광 구체 + 내부 백색 핵 + 빌보드 헤일로 + 점광원 (맥동)
///   · 궤도   : 타원 3개 (로고처럼 0° / ±60° 교차), 가는 실선 + 넓은 글로우 선 2겹, 그라디언트로 앞뒤 깊이감
///   · 전자   : 라벤더 발광 구체 + 헤일로 + 짧은 꼬리, 각 궤도를 따라 공전
///   · 연출   : 둥실 떠오름, 전체 세차 회전, 바닥 투영 링과 떠오르는 입자
/// 상태(Mood) — Idle: 느긋하게 / Attentive: 플레이어 접근 시 밝아지고 빨라짐 / Talking: 브리핑 중 강하게 맥동
/// </summary>
public class AtomInstructorModel : MonoBehaviour
{
    public enum Mood { Idle, Attentive, Talking }

    [Header("형태")]
    [SerializeField] float orbitMajor   = 0.78f;   // 타원 장반경 (m)
    [SerializeField] float orbitMinor   = 0.30f;   // 타원 단반경
    [SerializeField] float coreRadius   = 0.17f;
    [SerializeField] float hoverHeight  = 1.75f;   // 바닥에서 코어 중심까지

    [Header("색")]
    [SerializeField] Color coreColor     = new Color(0.30f, 0.85f, 1.00f);
    [SerializeField] Color orbitColor    = new Color(0.45f, 0.80f, 1.00f);
    [SerializeField] Color orbitDeep     = new Color(0.18f, 0.32f, 0.95f);
    [SerializeField] Color electronColor = new Color(0.78f, 0.74f, 1.00f);

    public Mood CurrentMood { get; set; } = Mood.Idle;

    const int OrbitSegments = 96;
    static readonly float[] OrbitTilts = { 0f, 60f, -60f };   // 로고의 교차 각도

    Transform _pivot, _spin, _core;
    readonly Transform[] _orbits    = new Transform[3];
    readonly Transform[] _electrons = new Transform[3];
    readonly LineRenderer[] _orbitLines = new LineRenderer[6];
    Transform[] _billboards;
    Transform   _glint;
    Light       _light;
    Material    _coreMat, _haloMat, _orbitMat, _orbitGlowMat, _electronMat;
    LineRenderer _floorRing;
    Transform   _camT;
    float       _energy = 0.6f, _phase;
    readonly float[] _eT = { 0.15f, 0.55f, 0.85f };

    static Texture2D s_glowTex;

    void Awake() => Build();

    void Update()
    {
        float dt = Time.deltaTime;
        float targetEnergy = CurrentMood switch { Mood.Talking => 1.35f, Mood.Attentive => 1.0f, _ => 0.6f };
        _energy = Mathf.MoveTowards(_energy, targetEnergy, dt * 1.2f);
        _phase += dt;

        // 둥실 + 세차 회전 (가까이 오면 빨라짐)
        _pivot.localPosition = Vector3.up * (hoverHeight + Mathf.Sin(_phase * 1.3f) * 0.06f);
        _spin.Rotate(Vector3.up, (14f + 26f * _energy) * dt, Space.Self);
        _spin.localRotation *= Quaternion.Euler(Mathf.Sin(_phase * 0.4f) * 4f * dt, 0f, 0f);

        // 궤도 자체도 미세하게 흔들린다
        for (int i = 0; i < 3; i++)
            _orbits[i].localRotation = Quaternion.Euler(Mathf.Sin(_phase * 0.7f + i * 2.1f) * 6f, i * 25f, OrbitTilts[i]);

        // 전자 공전
        float speed = 0.22f + 0.30f * _energy;
        for (int i = 0; i < 3; i++)
        {
            _eT[i] = Mathf.Repeat(_eT[i] + dt * speed * (i == 1 ? 0.85f : 1f), 1f);
            _electrons[i].localPosition = EllipsePoint(_eT[i] * Mathf.PI * 2f);
        }

        // 코어 맥동 (브리핑 중이면 빠르고 강하게)
        float pulseRate = CurrentMood == Mood.Talking ? 5.5f : 2.0f;
        float pulse = 1f + Mathf.Sin(_phase * pulseRate) * (0.05f + 0.05f * _energy);
        _core.localScale = Vector3.one * coreRadius * 2f * pulse;
        // HDR 은 1 을 살짝 넘기는 정도 — 블룸이 있으면 번지고, 없어도 색이 하얗게 날아가지 않는다
        _coreMat.SetColor("_EmissionColor", coreColor * (0.6f + 0.5f * _energy) * pulse);
        _electronMat.SetColor("_EmissionColor", electronColor * (0.7f + 0.6f * _energy));
        _haloMat.SetColor("_BaseColor", WithA(coreColor, 0.30f + 0.25f * _energy));
        _orbitGlowMat.SetColor("_BaseColor", WithA(orbitColor, 0.06f + 0.08f * _energy));
        _light.intensity = (1.2f + 1.8f * _energy) * pulse;

        // 바닥 링
        float ring = 0.55f + Mathf.Sin(_phase * 1.6f) * 0.06f;
        _floorRing.transform.localScale = Vector3.one * ring;
        _floorRing.startColor = _floorRing.endColor = WithA(coreColor, 0.25f + 0.25f * _energy);

        FaceBillboards();
    }

    Vector3 EllipsePoint(float t) => new Vector3(Mathf.Cos(t) * orbitMinor, Mathf.Sin(t) * orbitMajor, 0f);

    /// <summary>빌보드가 바라볼 카메라 (기본: Camera.main)</summary>
    public void SetViewer(Transform cam) => _camT = cam;

    void FaceBillboards()
    {
        if (_camT == null) { var c = Camera.main; if (c == null) return; _camT = c.transform; }
        foreach (var b in _billboards)
            if (b != null) b.rotation = Quaternion.LookRotation(b.position - _camT.position);
        // 광점은 코어 표면 중 카메라 쪽 좌상단에 붙인다
        Vector3 toCam = (_camT.position - _pivot.position).normalized;
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, toCam).normalized;
        Vector3 left = Vector3.Cross(toCam, up);
        _glint.position = _pivot.position + (toCam * 0.9f + up * 0.45f + left * 0.35f).normalized * coreRadius * 1.02f;
    }

    static Color WithA(Color c, float a) { c.a = a; return c; }

    // ── 구성 ──────────────────────────────────────────────────────────────
    void Build()
    {
        _coreMat      = LitEmissive(coreColor, 0.92f, 0.6f);   // 조명 음영이 보이도록 발광은 절반만
        _electronMat  = LitEmissive(electronColor * 0.5f, 0.9f, 1.1f);
        _haloMat      = Additive(WithA(coreColor, 0.45f), GlowTex);
        _orbitMat     = Additive(Color.white, null);
        _orbitGlowMat = Additive(WithA(orbitColor, 0.15f), null);
        var eHaloMat  = Additive(WithA(electronColor, 0.55f), GlowTex);

        _pivot = Child("AtomPivot", transform);
        _spin  = Child("Spin", _pivot);
        var billboards = new System.Collections.Generic.List<Transform>();

        // 코어: 발광 구체 + 내부 백색 핵 + 헤일로 2겹
        _core = Sphere("Core", _spin, Vector3.zero, coreRadius * 2f, _coreMat);
        Sphere("Nucleus", _core, Vector3.zero, 0.5f, LitEmissive(new Color(0.85f, 1f, 1f), 1f, 1.6f));
        billboards.Add(Quad("CoreHalo",  _pivot, Vector3.zero, 1.25f, _haloMat));
        billboards.Add(Quad("CoreFlare", _pivot, Vector3.zero, 0.55f, Additive(new Color(0.9f, 1f, 1f, 0.45f), GlowTex)));
        // 로고 코어의 좌상단 하이라이트 — 카메라 쪽 표면 위에 작은 광점
        _glint = Quad("Glint", _pivot, Vector3.zero, 0.12f, Additive(new Color(1f, 1f, 1f, 0.9f), GlowTex));
        billboards.Add(_glint);

        // 궤도 3개 + 전자
        for (int i = 0; i < 3; i++)
        {
            var orbit = Child($"Orbit{i}", _spin);
            orbit.localRotation = Quaternion.Euler(0f, i * 25f, OrbitTilts[i]);
            _orbits[i] = orbit;

            _orbitLines[i * 2]     = OrbitLine(orbit, "Line", 0.022f, _orbitMat, true);
            _orbitLines[i * 2 + 1] = OrbitLine(orbit, "Glow", 0.075f, _orbitGlowMat, false);

            var e = Sphere($"Electron{i}", orbit, EllipsePoint(_eT[i] * Mathf.PI * 2f), 0.085f, _electronMat);
            billboards.Add(Quad("Halo", e, Vector3.zero, 3.2f, eHaloMat));   // 부모 스케일(0.085) 기준
            AddTrail(e.gameObject);
            _electrons[i] = e;
        }

        // 점광원 — 주변 갑판을 청록으로 물들인다
        var lgo = new GameObject("CoreLight");
        lgo.transform.SetParent(_pivot, false);
        _light = lgo.AddComponent<Light>();
        _light.type = LightType.Point; _light.color = coreColor; _light.range = 6f; _light.intensity = 2f;
        _light.shadows = LightShadows.None;

        // 바닥 투영 링 + 상승 입자
        _floorRing = Circle("FloorRing", transform, 1f, 0.035f, Additive(Color.white, null));
        _floorRing.transform.localPosition = Vector3.up * 0.03f;
        BuildMotes();

        _billboards = billboards.ToArray();
    }

    LineRenderer OrbitLine(Transform parent, string name, float width, Material mat, bool depthGradient)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; lr.loop = true;
        lr.positionCount = OrbitSegments;
        for (int k = 0; k < OrbitSegments; k++) lr.SetPosition(k, EllipsePoint(k / (float)OrbitSegments * Mathf.PI * 2f));
        lr.widthMultiplier = width;
        lr.sharedMaterial = mat;
        lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
        lr.numCapVertices = 2;
        var g = new Gradient();
        if (depthGradient)
            // 로고처럼 위는 밝은 하늘색, 아래로 갈수록 짙은 파랑
            g.SetKeys(new[] { new GradientColorKey(orbitColor, 0f), new GradientColorKey(orbitDeep, 0.5f), new GradientColorKey(orbitColor, 1f) },
                      new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.75f, 0.5f), new GradientAlphaKey(0.95f, 1f) });
        else
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        lr.colorGradient = g;
        return lr;
    }

    LineRenderer Circle(string name, Transform parent, float radius, float width, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; lr.loop = true; lr.positionCount = 64;
        for (int k = 0; k < 64; k++)
        {
            float a = k / 64f * Mathf.PI * 2f;
            lr.SetPosition(k, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }
        lr.widthMultiplier = width; lr.sharedMaterial = mat;
        lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
        return lr;
    }

    void AddTrail(GameObject go)
    {
        var tr = go.AddComponent<TrailRenderer>();
        tr.time = 0.45f; tr.minVertexDistance = 0.02f;
        tr.widthMultiplier = 0.05f;
        tr.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        tr.sharedMaterial = Additive(Color.white, null);
        tr.shadowCastingMode = ShadowCastingMode.Off; tr.receiveShadows = false;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(electronColor, 0f), new GradientColorKey(orbitDeep, 1f) },
                  new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = g;
    }

    void BuildMotes()
    {
        var go = new GameObject("Motes");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 0.1f;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // 위로 방출
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
        main.startSpeed    = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startSize     = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
        main.startColor    = new ParticleSystem.MinMaxGradient(WithA(coreColor, 0.8f), WithA(electronColor, 0.8f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 60;
        var em = ps.emission; em.rateOverTime = 14f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 0.5f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = Additive(Color.white, GlowTex);
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        ps.Play();
    }

    // ── 프리미티브 헬퍼 ───────────────────────────────────────────────────
    static Transform Child(string n, Transform p)
    {
        var t = new GameObject(n).transform;
        t.SetParent(p, false);
        return t;
    }

    static Transform Sphere(string n, Transform p, Vector3 pos, float size, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = n;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(p, false);
        go.transform.localPosition = pos;
        go.transform.localScale = Vector3.one * size;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        return go.transform;
    }

    static Transform Quad(string n, Transform p, Vector3 pos, float size, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = n;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(p, false);
        go.transform.localPosition = pos;
        go.transform.localScale = Vector3.one * size;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        return go.transform;
    }

    // ── 머티리얼 ──────────────────────────────────────────────────────────
    static Material LitEmissive(Color c, float smooth, float emission = 2f)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh);
        m.SetColor("_BaseColor", c); m.color = c;
        m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0.1f);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        m.SetColor("_EmissionColor", c * emission);
        return m;
    }

    // 가산 블렌딩 (URP Particles/Unlit) — 블룸이 없어도 빛나 보이게
    static Material Additive(Color c, Texture tex)
    {
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) { var f = new Material(Shader.Find("Sprites/Default")) { color = c }; if (tex) f.mainTexture = tex; return f; }
        var m = new Material(sh);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 2f);   // Additive
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetColor("_BaseColor", c);
        if (tex != null) m.SetTexture("_BaseMap", tex);
        return m;
    }

    // 부드러운 원형 그라디언트 (헤일로·입자)
    static Texture2D GlowTex
    {
        get
        {
            if (s_glowTex != null) return s_glowTex;
            const int n = 64;
            s_glowTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "AtomGlow" };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                px[y * n + x] = new Color(1f, 1f, 1f, a * a * a);
            }
            s_glowTex.SetPixels(px);
            s_glowTex.Apply();
            return s_glowTex;
        }
    }
}

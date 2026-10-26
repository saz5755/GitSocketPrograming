using UnityEngine;

/// <summary>
/// 플레어 VFX — 파티클 없이 셰이더 기반으로 구성한 적열 연소체.
///   1) Burn   — 카메라 정렬 빌보드 (Custom/FlareBurn): 백열 핵 + 적색 화염 + 광륜 + 렌즈 스트릭
///   2) Hot    — 짧은 연소 궤적 리본 (Custom/FlareTrail, Additive)
///   3) Smoke  — 긴 백색 연기 리본 (Custom/FlareSmoke): 머리 쪽은 붉게 물듦
///   4) Light  — 적색 포인트 라이트 (동시 최대 MaxLights개 — 대량 살포 시 라이트 폭증 방지)
/// 연소 종료 직전에 리본을 떼어내 디코이가 파괴돼도 연기가 자연스럽게 사라지게 한다.
/// </summary>
public class FlareDecoyVFX : MonoBehaviour
{
    const int   MaxLights      = 6;
    const float LightIntensity = 9f;
    const float LightRange     = 45f;
    const float HotTrailTime   = 0.35f;
    const float SmokeTrailTime = 3.2f;

    static int      s_lightCount;
    static Mesh     s_quad;
    static Material s_burnMat, s_hotMat, s_smokeMat;

    static readonly int ID_Age  = Shader.PropertyToID("_Age");
    static readonly int ID_Seed = Shader.PropertyToID("_Seed");
    static readonly int ID_Fade = Shader.PropertyToID("_Fade");

    MaterialPropertyBlock _mpb;
    MeshRenderer  _burn;
    TrailRenderer _hot, _smoke;
    Light         _light;
    float         _age, _lifetime, _seed;
    bool          _detached;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => s_lightCount = 0;

    public static void Attach(GameObject host, float lifetime)
    {
        host.AddComponent<FlareDecoyVFX>().Init(lifetime);
    }

    void Init(float lifetime)
    {
        _lifetime = Mathf.Max(0.1f, lifetime);
        _seed     = Random.value * 100f;
        _mpb      = new MaterialPropertyBlock();
        EnsureShared();

        BuildBurn();
        _hot   = BuildTrail("Flare_HotTrail", s_hotMat, HotTrailTime, 0.42f,
                            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.08f)),
                            HotGradient());
        _smoke = BuildTrail("Flare_Smoke", s_smokeMat, SmokeTrailTime, 2.6f,
                            new AnimationCurve(new Keyframe(0f, 0.12f), new Keyframe(0.25f, 0.45f), new Keyframe(1f, 1f)),
                            SmokeGradient());
        BuildLight();
        Apply();
    }

    static void EnsureShared()
    {
        if (s_quad == null)
        {
            s_quad = new Mesh { name = "FlareQuad" };
            s_quad.SetVertices(new[] {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f,  0.5f, 0f),  new Vector3(-0.5f, 0.5f, 0f) });
            s_quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            // 버텍스 셰이더가 거리에 따라 빌보드를 키우므로 컬링 바운드를 넉넉히
            s_quad.bounds = new Bounds(Vector3.zero, Vector3.one * 12f);
        }
        if (s_burnMat  == null) s_burnMat  = MakeMat("Custom/FlareBurn");
        if (s_hotMat   == null) s_hotMat   = MakeMat("Custom/FlareTrail");
        if (s_smokeMat == null) s_smokeMat = MakeMat("Custom/FlareSmoke");
    }

    static Material MakeMat(string shaderName)
    {
        var sh = Shader.Find(shaderName);
        if (sh == null)
        {
            Debug.LogWarning($"[FlareDecoyVFX] 셰이더 '{shaderName}'를 찾지 못했습니다 — Sprites/Default로 대체");
            sh = Shader.Find("Sprites/Default");
        }
        return new Material(sh) { name = shaderName.Replace("Custom/", "") };
    }

    void BuildBurn()
    {
        var go = new GameObject("Flare_Burn");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = s_quad;
        _burn = go.AddComponent<MeshRenderer>();
        _burn.sharedMaterial       = s_burnMat;
        _burn.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
        _burn.receiveShadows       = false;
        _burn.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
        _burn.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    TrailRenderer BuildTrail(string name, Material mat, float time, float width,
                             AnimationCurve widthCurve, Gradient gradient)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var tr = go.AddComponent<TrailRenderer>();
        tr.sharedMaterial       = mat;
        tr.time                 = time;
        tr.widthMultiplier      = width;
        tr.widthCurve           = widthCurve;
        tr.colorGradient        = gradient;
        tr.minVertexDistance    = 0.25f;
        tr.numCapVertices       = 0;
        tr.alignment            = LineAlignment.View;
        tr.textureMode          = LineTextureMode.Stretch;
        tr.autodestruct         = false;
        tr.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows       = false;
        tr.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
        tr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return tr;
    }

    static Gradient HotGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 0.62f, 0.28f), 0f),
                new GradientColorKey(new Color(1f, 0.20f, 0.05f), 0.35f),
                new GradientColorKey(new Color(0.45f, 0.03f, 0.01f), 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    static Gradient SmokeGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.9f, 0.9f, 0.92f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.4f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    void BuildLight()
    {
        if (s_lightCount >= MaxLights) return;
        s_lightCount++;
        _light           = gameObject.AddComponent<Light>();
        _light.type      = LightType.Point;
        _light.color     = new Color(1f, 0.34f, 0.12f);
        _light.range     = LightRange;
        _light.intensity = 0f;
        _light.shadows   = LightShadows.None;
    }

    void Update()
    {
        _age += Time.deltaTime;
        Apply();

        // 디코이(CountermeasureDecoy)가 수명 끝에 파괴되기 직전 리본을 떼어내 자연 소멸시킨다
        if (!_detached && _age >= _lifetime - 0.12f) DetachTrails();
    }

    void Apply()
    {
        float t = Mathf.Clamp01(_age / _lifetime);

        _mpb.Clear();
        _mpb.SetFloat(ID_Age,  t);
        _mpb.SetFloat(ID_Seed, _seed);
        _burn.SetPropertyBlock(_mpb);

        // 연소가 식으면 연소 궤적도 함께 어두워진다
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, t));
        _mpb.SetFloat(ID_Fade, fade);
        if (_hot   != null) _hot.SetPropertyBlock(_mpb);
        _mpb.SetFloat(ID_Fade, 1f);
        if (_smoke != null) _smoke.SetPropertyBlock(_mpb);

        if (_light != null)
        {
            // 점화 플래시 → 안정 연소 → 냉각
            float curve = t < 0.04f ? Mathf.Lerp(1.8f, 1f, t / 0.04f)
                        : t < 0.72f ? 1f
                        : Mathf.Lerp(1f, 0f, (t - 0.72f) / 0.28f);
            float flicker = 0.8f + 0.35f * Mathf.PerlinNoise(_age * 22f, _seed);
            _light.intensity = LightIntensity * curve * flicker;
            _light.range     = LightRange * Mathf.Max(0.35f, curve);
        }
    }

    void DetachTrails()
    {
        _detached = true;
        Release(_hot,   HotTrailTime);
        Release(_smoke, SmokeTrailTime);
        _hot = _smoke = null;
    }

    static void Release(TrailRenderer tr, float time)
    {
        if (tr == null) return;
        tr.transform.SetParent(null, true);
        tr.emitting = false;
        Destroy(tr.gameObject, time + 0.2f);
    }

    void OnDestroy()
    {
        if (_light != null) s_lightCount = Mathf.Max(0, s_lightCount - 1);
    }
}

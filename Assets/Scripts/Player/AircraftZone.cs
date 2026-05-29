using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 항공기 탑승(이륙) / 하차(착륙) 존.
/// [ExecuteAlways] — 에디터에서도 디스크·파티클·라벨이 보임.
/// AddComponent() 후 Configure()를 호출하면 Start()에서 올바른 비주얼을 생성.
/// </summary>
[ExecuteAlways]
public class AircraftZone : MonoBehaviour
{
    public enum Type { Takeoff, Landing, Carrier }

    Type  _zoneType  = Type.Takeoff;
    float _radius    = 12f;
    float _radiusSq  = 144f;

    public Type ZoneType => _zoneType;

    Renderer _discRenderer;
    Material _discMaterial;
    bool     _inRange;
    Color    _lastAppliedColor = Color.clear;

    // 반투명 색상 (alpha로 불투명도 제어)
    static readonly Color ColTakeoffIdle  = new Color(0.00f, 0.80f, 1.00f, 0.60f); // Cyan/Blue for Takeoff
    static readonly Color ColTakeoffOn    = new Color(0.00f, 1.00f, 0.80f, 1.00f);
    static readonly Color ColLandIdle     = new Color(1.00f, 0.60f, 0.00f, 0.60f); // Orange/Gold for Landing
    static readonly Color ColLandOn       = new Color(1.00f, 0.80f, 0.00f, 1.00f);
    static readonly Color ColCarrierIdle  = new Color(0.80f, 0.20f, 1.00f, 0.60f); // Purple for Carrier
    static readonly Color ColCarrierOn    = new Color(1.00f, 0.40f, 1.00f, 1.00f);

    ParticleSystem _portalParticles;
    Light _portalLight;

    /// <summary>AddComponent() 직후 반드시 호출.</summary>
    public void Configure(Type zoneType, float radius)
    {
        _zoneType = zoneType;
        _radius   = radius;
        _radiusSq = radius * radius;
    }

    void Start()
    {
        BuildDisc();
        BuildParticles();
        BuildWorldLabel();
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        var gm = GameModeManager.Instance;
        if (gm == null) return;

        bool nowInRange = CheckInRange(gm);

        if (nowInRange != _inRange)
        {
            _inRange = nowInRange;
            if (_inRange) gm.NotifyZoneEnter(this);
            else          gm.NotifyZoneExit(this);
        }

        // 디스크 색 및 파티클 갱신
        if (_discMaterial != null)
        {
            Color targetColor = _zoneType switch
            {
                Type.Landing => _inRange ? ColLandOn    : ColLandIdle,
                Type.Carrier => _inRange ? ColCarrierOn : ColCarrierIdle,
                _            => _inRange ? ColTakeoffOn : ColTakeoffIdle,
            };

            Color currentColor = _lastAppliedColor == Color.clear
                ? _discMaterial.color
                : _lastAppliedColor;
            Color newColor = Color.Lerp(currentColor, targetColor, Time.deltaTime * 5f);

            if (newColor != _lastAppliedColor)
            {
                _discMaterial.color = newColor;
                _lastAppliedColor = newColor;

                if (_portalLight != null)
                    _portalLight.color = newColor;

                if (_portalParticles != null)
                {
                    var main = _portalParticles.main;
                    main.startColor = newColor;
                }
            }

            if (_portalLight != null)
                _portalLight.intensity = Mathf.Lerp(_portalLight.intensity, _inRange ? 8f : 3f, Time.deltaTime * 5f);

            if (_portalParticles != null)
            {
                var em = _portalParticles.emission;
                em.rateOverTime = _inRange ? 100f : 30f;
            }
        }
    }

    bool CheckInRange(GameModeManager gm)
    {
        if (_zoneType == Type.Takeoff && !gm.IsFlying && gm.GroundCharacter != null)
            return (gm.GroundCharacter.position - transform.position).sqrMagnitude <= _radiusSq;

        if ((_zoneType == Type.Landing || _zoneType == Type.Carrier) && gm.IsFlying && gm.LocalAircraft != null)
            return (gm.LocalAircraft.position - transform.position).sqrMagnitude <= _radiusSq;

        return false;
    }

    // ── 디스크 비주얼 ────────────────────────────────────────────────────────
    void BuildDisc()
    {
        var disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
        disc.name = "PortalDisc";
        var discCol = disc.GetComponent<Collider>();
        if (discCol != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(discCol);
            else
#endif
            Destroy(discCol);
        }
        disc.transform.SetParent(transform, false);
        disc.transform.localScale    = new Vector3(_radius * 2f, _radius * 2f, 1f);
        disc.transform.localPosition = new Vector3(0f, 0.1f, 0f); // 약간 띄움
        disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 바닥에 눕힘

        Color baseColor = _zoneType == Type.Takeoff ? ColTakeoffIdle : ColLandIdle;
        
        Shader portalShader = Shader.Find("Custom/PortalZone");
        Material mat;
        if (portalShader != null)
        {
            mat = new Material(portalShader);
            mat.SetColor("_Color", baseColor);
            mat.SetFloat("_Speed", 0.5f);
            mat.SetFloat("_RingWidth", 0.15f);
            mat.SetFloat("_Glow", 2.5f);
        }
        else
        {
            mat = MakeTransparentMaterial(baseColor);
        }

        _discRenderer = disc.GetComponent<Renderer>();
        _discRenderer.sharedMaterial = mat;
        _discRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _discRenderer.receiveShadows    = false;
        _discMaterial = mat;

        // 조명 추가
        _portalLight = disc.AddComponent<Light>();
        _portalLight.type = LightType.Point;
        _portalLight.color = baseColor;
        _portalLight.intensity = 3f;
        _portalLight.range = _radius * 1.5f;
    }

    void BuildParticles()
    {
        var pObj = new GameObject("PortalParticles");
        pObj.transform.SetParent(transform, false);
        pObj.transform.localPosition = Vector3.zero;
        pObj.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        _portalParticles = pObj.AddComponent<ParticleSystem>();
        var main = _portalParticles.main;
        main.duration = 1f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
        main.startColor = _zoneType == Type.Takeoff ? ColTakeoffIdle : ColLandIdle;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;

        var em = _portalParticles.emission;
        em.rateOverTime = 30f;

        var shape = _portalParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = _radius * 0.9f;
        shape.arc = 360f;

        var col = _portalParticles.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = grad;

        var size = _portalParticles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0f)));

        var rend = _portalParticles.GetComponent<ParticleSystemRenderer>();
        Shader pShader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Particles/Additive")
                      ?? Shader.Find("Mobile/Particles/Additive")
                      ?? Shader.Find("Sprites/Default");
        rend.material = new Material(pShader);
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.lengthScale = 2f;
        rend.velocityScale = 0.1f;
    }

    // ── 월드 라벨 (Billboard) ────────────────────────────────────────────────
    void BuildWorldLabel()
    {
        string label = _zoneType switch
        {
            Type.Landing => "LANDING ZONE",
            Type.Carrier => "CARRIER ZONE",
            _            => "TAKEOFF ZONE",
        };
        Color col = _zoneType switch
        {
            Type.Landing => new Color(1.0f, 0.8f, 0.0f, 1f),
            Type.Carrier => new Color(0.8f, 0.2f, 1.0f, 1f),
            _            => new Color(0.0f, 0.8f, 1.0f, 1f),
        };

        var go = new GameObject("ZoneLabel");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 3.5f, 0f);
        go.transform.localScale    = Vector3.one * 0.025f;

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(300f, 50f);

        var txtGO = new GameObject("Txt");
        txtGO.transform.SetParent(go.transform, false);
        var tRT = txtGO.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = tRT.offsetMax = Vector2.zero;

        var txt = txtGO.AddComponent<Text>();
        txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize  = 24;
        txt.fontStyle = FontStyle.Bold;
        txt.color     = col;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.text      = label;

        go.AddComponent<ZoneLabelBillboard>();
    }

    // ── 투명 재질 생성 (URP / Standard 겸용) ─────────────────────────────────
    static Material MakeTransparentMaterial(Color color)
    {
        // PlaneModelBuilder와 동일한 셰이더 우선순위
        string[] candidates = {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Lightweight Render Pipeline/Lit",
            "HDRP/Lit", "Standard",
        };
        Shader sh = null;
        foreach (string n in candidates) { sh = Shader.Find(n); if (sh != null) break; }
        sh ??= Shader.Find("Sprites/Default");

        var mat = new Material(sh);
        mat.color = color;

        // URP 투명 설정 (CockpitBuilder HUDGlass 방식)
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);          // 0=Opaque, 1=Transparent
            mat.SetFloat("_Blend",   0f);           // 0=Alpha
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        }
        else // Standard 폴백
        {
            mat.SetFloat("_Mode", 3f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite",   0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }
        mat.renderQueue = 3000;
        return mat;
    }
}

/// <summary>월드 스페이스 라벨을 항상 카메라 방향으로 회전.</summary>
public class ZoneLabelBillboard : MonoBehaviour
{
    void LateUpdate()
    {
        if (Camera.main == null) return;
        transform.LookAt(Camera.main.transform);
        transform.Rotate(0f, 180f, 0f);
    }
}

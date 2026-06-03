using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 항공기 하차(착륙) 존.
/// [ExecuteAlways] — 에디터에서도 디스크·라벨이 보임.
/// </summary>
[ExecuteAlways]
public class AircraftZone : MonoBehaviour
{
    public enum Type { Takeoff, Landing, Carrier }

    [SerializeField] Type  _zoneType  = Type.Takeoff;
    [SerializeField] float _radius    = 12f;
    float _radiusSq;

    [Tooltip("이 존에 연결된 항공기. 진입 시 GameModeManager의 활성 항공기가 이것으로 전환됨.")]
    [SerializeField] PlayerController _linkedAircraft;

    public Type             ZoneType       => _zoneType;
    public PlayerController LinkedAircraft => _linkedAircraft;

    Renderer _discRenderer;
    Material _discMaterial;
    Light    _portalLight;
    bool     _inRange;
    Color    _lastAppliedColor = Color.clear;

    static readonly Color ColLandIdle    = new Color(1.00f, 0.60f, 0.00f, 0.60f);
    static readonly Color ColLandOn      = new Color(1.00f, 0.80f, 0.00f, 1.00f);
    static readonly Color ColCarrierIdle = new Color(0.80f, 0.20f, 1.00f, 0.60f);
    static readonly Color ColCarrierOn   = new Color(1.00f, 0.40f, 1.00f, 1.00f);
    static readonly Color ColTakeoffIdle = new Color(0.00f, 0.80f, 1.00f, 0.60f);
    static readonly Color ColTakeoffOn   = new Color(0.00f, 1.00f, 0.80f, 1.00f);

    public void Configure(Type zoneType, float radius, PlayerController linkedAircraft = null)
    {
        _zoneType = zoneType;
        _radius   = radius;
        _radiusSq = radius * radius;
        if (linkedAircraft != null) _linkedAircraft = linkedAircraft;
    }

    void Start()
    {
        _radiusSq = _radius * _radius;

        if (transform.Find("PortalDisc") == null) BuildDisc();
        else                                       CacheDisc();
        if (transform.Find("ZoneLabel") == null)  BuildWorldLabel();
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

        if (_discMaterial != null)
        {
            Color targetColor = _zoneType switch
            {
                Type.Landing => _inRange ? ColLandOn    : ColLandIdle,
                Type.Carrier => _inRange ? ColCarrierOn : ColCarrierIdle,
                _            => _inRange ? ColTakeoffOn : ColTakeoffIdle,
            };

            Color current  = _lastAppliedColor == Color.clear ? _discMaterial.color : _lastAppliedColor;
            Color newColor = Color.Lerp(current, targetColor, Time.deltaTime * 5f);

            if (newColor != _lastAppliedColor)
            {
                _discMaterial.color = newColor;
                _lastAppliedColor   = newColor;
                if (_portalLight != null) _portalLight.color = newColor;
            }

            if (_portalLight != null)
                _portalLight.intensity = Mathf.Lerp(_portalLight.intensity, _inRange ? 8f : 3f, Time.deltaTime * 5f);
        }
    }

    bool CheckInRange(GameModeManager gm)
    {
        if ((_zoneType == Type.Landing || _zoneType == Type.Carrier) && gm.IsFlying && gm.LocalAircraft != null)
            return (gm.LocalAircraft.position - transform.position).sqrMagnitude <= _radiusSq;
        return false;
    }

    // ── 기존 자식 참조 캐시 ──────────────────────────────────────────────────
    void CacheDisc()
    {
        var disc = transform.Find("PortalDisc");
        if (disc == null) return;
        _discRenderer = disc.GetComponent<Renderer>();
        _discMaterial = _discRenderer != null ? _discRenderer.sharedMaterial : null;
        _portalLight  = disc.GetComponent<Light>();
    }

    // ── 디스크 비주얼 ────────────────────────────────────────────────────────
    void BuildDisc()
    {
        var disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
        disc.name = "PortalDisc";
        var col = disc.GetComponent<Collider>();
        if (col != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(col);
            else
#endif
            Destroy(col);
        }
        disc.transform.SetParent(transform, false);
        disc.transform.localScale    = new Vector3(_radius * 2f, _radius * 2f, 1f);
        disc.transform.localPosition = new Vector3(0f, 0.1f, 0f);
        disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        Color baseColor = _zoneType switch
        {
            Type.Landing => ColLandIdle,
            Type.Carrier => ColCarrierIdle,
            _            => ColTakeoffIdle,
        };

        Material mat;
        var portalShader = Shader.Find("Custom/PortalZone");
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
        _discRenderer.sharedMaterial        = mat;
        _discRenderer.shadowCastingMode     = UnityEngine.Rendering.ShadowCastingMode.Off;
        _discRenderer.receiveShadows        = false;
        _discMaterial = mat;

        _portalLight           = disc.AddComponent<Light>();
        _portalLight.type      = LightType.Point;
        _portalLight.color     = baseColor;
        _portalLight.intensity = 3f;
        _portalLight.range     = _radius * 1.5f;
    }

    // ── 월드 라벨 ────────────────────────────────────────────────────────────
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

    static Material MakeTransparentMaterial(Color color)
    {
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

        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend",   0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        }
        else
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

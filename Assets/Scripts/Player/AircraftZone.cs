using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 항공기 탑승(이륙) / 하차(착륙) 존.
/// AddComponent() 후 Configure()를 호출하면 Start()에서 올바른 비주얼을 생성.
/// </summary>
public class AircraftZone : MonoBehaviour
{
    public enum Type { Takeoff, Landing }

    Type  _zoneType = Type.Takeoff;
    float _radius   = 12f;

    public Type ZoneType => _zoneType;

    Renderer _discRenderer;
    bool     _inRange;

    // 반투명 색상 (alpha로 불투명도 제어)
    static readonly Color ColTakeoffIdle = new Color(0.00f, 0.90f, 0.50f, 0.20f);
    static readonly Color ColTakeoffOn   = new Color(0.00f, 1.00f, 0.55f, 0.45f);
    static readonly Color ColLandIdle    = new Color(1.00f, 0.75f, 0.00f, 0.22f);
    static readonly Color ColLandOn      = new Color(1.00f, 0.85f, 0.00f, 0.50f);

    /// <summary>AddComponent() 직후 반드시 호출.</summary>
    public void Configure(Type zoneType, float radius)
    {
        _zoneType = zoneType;
        _radius   = radius;
    }

    void Start()
    {
        BuildDisc();
        BuildWorldLabel();
    }

    void Update()
    {
        var gm = GameModeManager.Instance;
        if (gm == null) return;

        bool nowInRange = CheckInRange(gm);

        if (nowInRange != _inRange)
        {
            _inRange = nowInRange;
            if (_inRange) gm.NotifyZoneEnter(this);
            else          gm.NotifyZoneExit(this);
        }

        // 디스크 색 갱신
        if (_discRenderer != null)
        {
            bool land = _zoneType == Type.Landing;
            Color c = _inRange
                ? (land ? ColLandOn  : ColTakeoffOn)
                : (land ? ColLandIdle : ColTakeoffIdle);
            _discRenderer.material.color = c;
        }
    }

    bool CheckInRange(GameModeManager gm)
    {
        if (_zoneType == Type.Takeoff && !gm.IsFlying && gm.GroundCharacter != null)
            return Vector3.Distance(gm.GroundCharacter.position, transform.position) <= _radius;

        if (_zoneType == Type.Landing && gm.IsFlying && gm.LocalAircraft != null)
            return Vector3.Distance(gm.LocalAircraft.position, transform.position) <= _radius;

        return false;
    }

    // ── 디스크 비주얼 ────────────────────────────────────────────────────────
    void BuildDisc()
    {
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "ZoneDisc";
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(transform, false);
        disc.transform.localScale    = new Vector3(_radius * 2f, 0.04f, _radius * 2f);
        disc.transform.localPosition = Vector3.zero;

        Color baseColor = _zoneType == Type.Takeoff ? ColTakeoffIdle : ColLandIdle;
        var mat = MakeTransparentMaterial(baseColor);

        _discRenderer = disc.GetComponent<Renderer>();
        _discRenderer.material = mat;
        _discRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _discRenderer.receiveShadows    = false;
    }

    // ── 월드 라벨 (Billboard) ────────────────────────────────────────────────
    void BuildWorldLabel()
    {
        string label = _zoneType == Type.Takeoff ? "TAKEOFF ZONE" : "LANDING ZONE";
        Color  col   = _zoneType == Type.Takeoff
            ? new Color(0.0f, 1.0f, 0.5f,  1f)
            : new Color(1.0f, 0.85f, 0.0f, 1f);

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

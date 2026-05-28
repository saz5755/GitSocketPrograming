using UnityEngine;

/// <summary>
/// KF-21 보라매 조종석 내부 3D 지오메트리.
/// FlightCamera에 부착, 코크핏 모드일 때만 활성화.
/// 모든 요소는 카메라 로컬 좌표에 배치 — 항상 올바른 파일럿 시점 유지.
/// </summary>
[DisallowMultipleComponent]
public class CockpitBuilder : MonoBehaviour
{
    // ── 조종석 색상 ──────────────────────────────────────────────────────────
    static readonly Color PanelDark  = new Color(0.050f, 0.055f, 0.065f); // 계기판 주 색
    static readonly Color PanelMid   = new Color(0.085f, 0.095f, 0.110f); // 사이드 콘솔
    static readonly Color ScreenOff  = new Color(0.010f, 0.025f, 0.012f); // MFD 꺼진 상태
    static readonly Color ScreenOn   = new Color(0.004f, 0.160f, 0.030f); // MFD 켜진 상태
    static readonly Color ScreenSide = new Color(0.005f, 0.100f, 0.120f); // 측면 MFD (청록)
    static readonly Color MetalDark  = new Color(0.115f, 0.125f, 0.140f); // 금속 프레임
    static readonly Color MetalMid   = new Color(0.175f, 0.185f, 0.205f); // 금속 하이라이트
    static readonly Color GlassTint  = new Color(0.000f, 0.750f, 0.320f); // HUD 콤바이너 글래스
    static readonly Color SeatDark   = new Color(0.038f, 0.038f, 0.046f); // 사출좌석
    static readonly Color AccentKAI  = new Color(0.100f, 0.160f, 0.260f); // KAI 강조색 (짙은 청색)

    Transform interiorRoot;
    Material  baseMat;

    public void SetVisible(bool v)
    {
        if (interiorRoot != null)
            interiorRoot.gameObject.SetActive(v);
    }

    void Awake()
    {
        baseMat = FindBaseMaterial();
        var go = new GameObject("CockpitInterior");
        go.transform.SetParent(transform, false);
        interiorRoot = go.transform;
        Build();
        interiorRoot.gameObject.SetActive(false);
    }

    // 모든 위치는 카메라 로컬 좌표 (0,0,0 = 파일럿 눈 위치)
    // +Z = 앞, +Y = 위, +X = 오른쪽
    void Build()
    {
        // ── 주 계기판 패널 (파일럿 정면 아래) ───────────────────────────────
        // KF-21은 F-35보다 넓은 계기판
        P("IP_Panel",       V( 0.00f,-0.28f, 0.40f), V(0.76f, 0.34f, 0.025f), PanelDark);

        // 글레어쉴드 (MFD 위, HUD 아래)
        P("Glareshield",    V( 0.00f,-0.13f, 0.27f), V(0.74f, 0.030f, 0.16f), PanelDark, rot: Q(-12f,0f,0f));

        // ── KF-21 MFD 2개 (대형 좌우 배치) ─────────────────────────────────
        // 왼쪽 대형 MFD
        P("Bezel_L",        V(-0.22f,-0.27f, 0.405f), V(0.30f, 0.22f, 0.012f), PanelDark);
        P("Screen_L",       V(-0.22f,-0.27f, 0.412f), V(0.262f,0.185f,0.008f), ScreenOn);

        // 오른쪽 대형 MFD
        P("Bezel_R",        V( 0.22f,-0.27f, 0.405f), V(0.30f, 0.22f, 0.012f), PanelDark);
        P("Screen_R",       V( 0.22f,-0.27f, 0.412f), V(0.262f,0.185f,0.008f), ScreenSide);

        // ── 센터 상단 디스플레이 (UFC / 무장 관리) ──────────────────────────
        P("UFC_Panel",      V( 0.00f,-0.14f, 0.405f), V(0.22f, 0.090f, 0.012f), PanelMid);
        P("UFC_Screen",     V( 0.00f,-0.14f, 0.412f), V(0.185f,0.062f, 0.008f), ScreenOn);

        // ── 센터 콘솔 (랜딩기어, 플랩, 무장 선택) ───────────────────────────
        P("CenterConsole",  V( 0.00f,-0.38f, 0.28f), V(0.18f, 0.060f, 0.22f), PanelMid);
        P("CC_Switches",    V( 0.00f,-0.36f, 0.24f), V(0.14f, 0.018f, 0.16f), MetalDark);

        // ── 좌측 사이드 콘솔 (스로틀 레버) ──────────────────────────────────
        P("LSC_Body",       V(-0.42f,-0.24f, 0.22f), V(0.09f, 0.24f, 0.30f), PanelMid);
        P("LSC_Top",        V(-0.42f,-0.12f, 0.22f), V(0.09f, 0.032f, 0.30f), MetalDark);
        // HOTAS 스로틀 레버
        P("Throttle",       V(-0.44f,-0.10f, 0.18f), V(0.038f,0.065f, 0.16f), MetalDark, rot: Q(20f,0f,0f));
        P("ThrottleGrip",   V(-0.44f,-0.07f, 0.11f), V(0.055f,0.055f, 0.060f), MetalMid);
        P("ThrottleBtn",    V(-0.44f,-0.05f, 0.10f), V(0.030f,0.020f, 0.030f), AccentKAI);

        // ── 우측 사이드 콘솔 (사이드스틱) ────────────────────────────────────
        P("RSC_Body",       V( 0.42f,-0.24f, 0.22f), V(0.09f, 0.24f, 0.30f), PanelMid);
        P("RSC_Top",        V( 0.42f,-0.12f, 0.22f), V(0.09f, 0.032f, 0.30f), MetalDark);
        // HOTAS 사이드스틱
        P("Sidestick",      V( 0.44f,-0.20f, 0.32f), V(0.042f, 0.150f, 0.042f), MetalDark, rot: Q(-10f,0f,0f));
        P("StickGrip",      V( 0.44f,-0.09f, 0.29f), V(0.058f, 0.065f, 0.058f), MetalMid);
        P("StickBtn_T",     V( 0.44f,-0.06f, 0.27f), V(0.025f, 0.018f, 0.025f), AccentKAI);

        // ── 캐노피 프레임 (KF-21 버블 캐노피) ───────────────────────────────
        // 정면 캐노피 보우 (상단 와이드)
        P("CanopyBow_F",    V( 0.00f, 0.45f, 0.32f), V(0.90f, 0.060f, 0.060f), MetalDark);
        // 좌측 캐노피 사이드 프레임
        P("CanopyBow_L",    V(-0.48f, 0.28f, 0.18f), V(0.060f, 0.32f, 0.35f),  MetalDark, rot: Q(0f,0f,-8f));
        // 우측 캐노피 사이드 프레임
        P("CanopyBow_R",    V( 0.48f, 0.28f, 0.18f), V(0.060f, 0.32f, 0.35f),  MetalDark, rot: Q(0f,0f, 8f));
        // 후방 캐노피 보우 (어깨 위)
        P("CanopyBow_Rear", V( 0.00f, 0.32f,-0.08f), V(0.78f, 0.055f, 0.055f), MetalDark);

        // ── 계기판 측면 림 ────────────────────────────────────────────────────
        P("IPRim_L",        V(-0.40f,-0.24f, 0.38f), V(0.032f, 0.34f, 0.032f), MetalMid);
        P("IPRim_R",        V( 0.40f,-0.24f, 0.38f), V(0.032f, 0.34f, 0.032f), MetalMid);

        // ── HUD 콤바이너 글래스 (파일럿 눈 앞) ──────────────────────────────
        P("HUDGlass",       V( 0.00f, 0.025f, 0.24f), V(0.32f, 0.210f, 0.006f), GlassTint, rot: Q(-8f,0f,0f));

        // ── 사출 좌석 헤드레스트 ─────────────────────────────────────────────
        P("Headrest",       V( 0.00f, 0.46f,-0.10f), V(0.26f, 0.13f, 0.075f), SeatDark);
        P("ShoulderL",      V(-0.22f, 0.27f,-0.05f), V(0.10f, 0.24f, 0.060f), SeatDark);
        P("ShoulderR",      V( 0.22f, 0.27f,-0.05f), V(0.10f, 0.24f, 0.060f), SeatDark);

        // ── 무릎 차단 패널 ────────────────────────────────────────────────────
        P("KneePanel",      V( 0.00f,-0.42f, 0.30f), V(0.66f, 0.052f, 0.20f), PanelDark);

        // ── KAI 로고 플레이트 (센터 콘솔 앞쪽) ─────────────────────────────
        P("KAI_Badge",      V( 0.00f,-0.37f, 0.34f), V(0.060f, 0.025f, 0.008f), AccentKAI);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────────────────
    static Vector3    V(float x, float y, float z) => new Vector3(x, y, z);
    static Quaternion Q(float x, float y, float z) => Quaternion.Euler(x, y, z);

    void P(string partName, Vector3 pos, Vector3 scale, Color color,
           PrimitiveType type = PrimitiveType.Cube, Quaternion rot = default)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = partName;
        go.transform.SetParent(interiorRoot, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot == default ? Quaternion.identity : rot;
        go.transform.localScale    = scale;
        Destroy(go.GetComponent<Collider>());

        var r   = go.GetComponent<Renderer>();
        var mat = new Material(baseMat);
        mat.color = color;
        if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   0.50f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.32f);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.32f);

        if (partName == "HUDGlass")
        {
            mat.color = new Color(color.r, color.g, color.b, 0.22f);
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend",   0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            else
            {
                mat.SetFloat("_Mode", 3f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite",   0);
                mat.EnableKeyword("_ALPHABLEND_ON");
            }
            mat.renderQueue = 3000;
        }
        r.material = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows    = false;
    }

    static Material FindBaseMaterial()
    {
        string[] candidates = {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "HDRP/Lit", "Standard", "Diffuse",
        };
        foreach (string n in candidates)
        {
            var sh = Shader.Find(n);
            if (sh != null) return new Material(sh);
        }
        foreach (var r in FindObjectsOfType<Renderer>())
            if (r.sharedMaterial?.shader != null &&
                !r.sharedMaterial.shader.name.StartsWith("Hidden"))
                return new Material(r.sharedMaterial);
        return new Material(Shader.Find("Sprites/Default"));
    }
}

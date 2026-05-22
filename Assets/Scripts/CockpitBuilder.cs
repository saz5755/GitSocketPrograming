using UnityEngine;

/// <summary>
/// F-35A 조종석 내부 3D 지오메트리.
/// FlightCamera에 부착, 코크핏 모드일 때만 활성화.
/// 모든 요소는 카메라 로컬 좌표에 배치 — 항상 올바른 파일럿 시점 유지.
/// </summary>
[DisallowMultipleComponent]
public class CockpitBuilder : MonoBehaviour
{
    // ── 조종석 색상 ──────────────────────────────────────────────────────────
    static readonly Color PanelDark = new Color(0.055f, 0.060f, 0.070f); // 계기판 주 색
    static readonly Color PanelMid  = new Color(0.090f, 0.100f, 0.115f); // 사이드 콘솔
    static readonly Color ScreenOff = new Color(0.010f, 0.028f, 0.014f); // MFD 꺼진 상태
    static readonly Color ScreenOn  = new Color(0.005f, 0.150f, 0.025f); // MFD 켜진 상태
    static readonly Color MetalDark = new Color(0.120f, 0.130f, 0.145f); // 금속 프레임
    static readonly Color MetalMid  = new Color(0.180f, 0.190f, 0.210f); // 금속 하이라이트
    static readonly Color GlassTint = new Color(0.000f, 0.720f, 0.300f); // HUD 콤바이너 글래스
    static readonly Color SeatDark  = new Color(0.040f, 0.040f, 0.048f); // 사출좌석

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

    // 모든 위치는 카메라 로컬 좌표 (0,0,0=카메라 눈 위치)
    // +Z = 앞, +Y = 위, +X = 오른쪽
    void Build()
    {
        // ── 주 계기판 패널 (파일럿 정면 아래) ───────────────────────────────
        P("IP_Panel",      V( 0.00f,-0.28f, 0.38f), V(0.66f, 0.32f, 0.025f), PanelDark);
        // 계기판 상단 레지 (글레어실드 탑)
        P("Glareshield",   V( 0.00f,-0.13f, 0.26f), V(0.64f, 0.028f, 0.14f), PanelDark, rot: Q(-14f,0f,0f));

        // ── MFD 베젤 3개 ─────────────────────────────────────────────────────
        P("Bezel_L",       V(-0.21f,-0.26f, 0.385f), V(0.19f, 0.16f, 0.010f), PanelDark);
        P("Bezel_C",       V( 0.00f,-0.26f, 0.385f), V(0.21f, 0.16f, 0.010f), PanelDark);
        P("Bezel_R",       V( 0.21f,-0.26f, 0.385f), V(0.19f, 0.16f, 0.010f), PanelDark);

        // ── MFD 스크린 (베젤 뒤에) ───────────────────────────────────────────
        P("Screen_L",      V(-0.21f,-0.26f, 0.390f), V(0.155f,0.130f, 0.008f), ScreenOn);
        P("Screen_C",      V( 0.00f,-0.26f, 0.390f), V(0.175f,0.130f, 0.008f), ScreenOn);
        P("Screen_R",      V( 0.21f,-0.26f, 0.390f), V(0.155f,0.130f, 0.008f), ScreenOn);

        // ── UFC (Up-Front Controller: 센터 상단) ─────────────────────────────
        P("UFC",           V( 0.00f,-0.15f, 0.390f), V(0.18f, 0.070f, 0.010f), PanelMid);

        // ── 좌측 사이드 콘솔 (스로틀) ────────────────────────────────────────
        P("LSC_Body",      V(-0.38f,-0.22f, 0.22f), V(0.08f, 0.22f, 0.28f), PanelMid);
        P("LSC_Top",       V(-0.38f,-0.11f, 0.22f), V(0.08f, 0.030f, 0.28f), MetalDark);
        // 스로틀 레버
        P("Throttle",      V(-0.40f,-0.09f, 0.18f), V(0.035f,0.060f, 0.14f), MetalDark, rot: Q(18f,0f,0f));
        P("ThrottleGrip",  V(-0.40f,-0.07f, 0.12f), V(0.050f,0.050f, 0.055f), MetalMid);

        // ── 우측 사이드 콘솔 (사이드스틱) ────────────────────────────────────
        P("RSC_Body",      V( 0.38f,-0.22f, 0.22f), V(0.08f, 0.22f, 0.28f), PanelMid);
        P("RSC_Top",       V( 0.38f,-0.11f, 0.22f), V(0.08f, 0.030f, 0.28f), MetalDark);
        // 사이드스틱 (HOTAS)
        P("Sidestick",     V( 0.40f,-0.18f, 0.30f), V(0.040f, 0.140f, 0.040f), MetalDark, rot: Q(-8f,0f,0f));
        P("StickGrip",     V( 0.40f,-0.08f, 0.28f), V(0.055f, 0.060f, 0.055f), MetalMid);

        // ── 캐노피 프론트 보우 (상단 정면 프레임) ────────────────────────────
        P("CanopyBow_F",   V( 0.00f, 0.44f, 0.30f), V(0.84f, 0.056f, 0.056f), MetalDark);
        // 캐노피 사이드 보우 (좌)
        P("CanopyBow_L",   V(-0.44f, 0.30f, 0.20f), V(0.056f, 0.30f, 0.32f),  MetalDark, rot: Q(0f,0f,-10f));
        // 캐노피 사이드 보우 (우)
        P("CanopyBow_R",   V( 0.44f, 0.30f, 0.20f), V(0.056f, 0.30f, 0.32f),  MetalDark, rot: Q(0f,0f, 10f));
        // 캐노피 리어 보우 (뒤 프레임 — 어깨 위)
        P("CanopyBow_Rear",V( 0.00f, 0.30f,-0.06f), V(0.72f, 0.050f, 0.050f), MetalDark);

        // ── 계기판 측면 림 (좌우) ────────────────────────────────────────────
        P("IPRim_L",       V(-0.34f,-0.22f, 0.36f), V(0.030f, 0.32f, 0.030f), MetalMid);
        P("IPRim_R",       V( 0.34f,-0.22f, 0.36f), V(0.030f, 0.32f, 0.030f), MetalMid);

        // ── HUD 콤바이너 글래스 (파일럿 눈 바로 앞) ─────────────────────────
        P("HUDGlass",      V( 0.00f, 0.020f, 0.24f), V(0.30f, 0.200f, 0.006f), GlassTint, rot: Q(-10f,0f,0f));

        // ── 사출 좌석 헤드레스트 (뒤에서 보이는 부분) ────────────────────────
        P("Headrest",      V( 0.00f, 0.44f,-0.08f), V(0.24f, 0.12f, 0.072f), SeatDark);
        P("ShoulderL",     V(-0.20f, 0.26f,-0.04f), V(0.09f, 0.22f, 0.058f), SeatDark);
        P("ShoulderR",     V( 0.20f, 0.26f,-0.04f), V(0.09f, 0.22f, 0.058f), SeatDark);

        // ── 계기판 하단 (무릎 차단 패널) ─────────────────────────────────────
        P("KneePanel",     V( 0.00f,-0.40f, 0.30f), V(0.60f, 0.05f, 0.18f), PanelDark);
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
        if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   0.45f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.28f);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.28f);

        // HUD 글래스: 반투명
        if (partName == "HUDGlass")
        {
            mat.color = new Color(color.r, color.g, color.b, 0.22f);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend",   0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
        }
        r.material = mat;
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

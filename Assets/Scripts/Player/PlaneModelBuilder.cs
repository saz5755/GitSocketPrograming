using UnityEngine;

/// <summary>
/// F-35A Lightning II 스텔스 전투기 모델.
/// 단일 수직미익, 단일 DSI 인테이크, F135 단발 엔진을 재현합니다.
/// </summary>
[DisallowMultipleComponent]
public class PlaneModelBuilder : MonoBehaviour
{
    // ── F-35A 색상 팔레트 ──────────────────────────────────────────────────
    // 레이더흡수코팅(RAM) 특유의 무광 회색조
    static readonly Color BodyGray   = new Color(0.29f, 0.31f, 0.33f); // 동체 주요 색
    static readonly Color PanelGray  = new Color(0.19f, 0.21f, 0.23f); // 패널라인 / 제어면
    static readonly Color WingGray   = new Color(0.31f, 0.33f, 0.35f); // 주익 상면
    static readonly Color Cockpit    = new Color(0.04f, 0.07f, 0.18f); // 캐노피 (청색 반사)
    static readonly Color IntakeDark = new Color(0.16f, 0.17f, 0.19f); // DSI 인테이크 하우징
    static readonly Color Black      = new Color(0.03f, 0.03f, 0.04f); // 인테이크 내부 / 노즐 내부
    static readonly Color Exhaust    = new Color(0.24f, 0.22f, 0.20f); // F135 노즐 페탈
    static readonly Color ExhGlow    = new Color(0.95f, 0.55f, 0.12f); // 애프터버너 글로우

    Material baseMat;

    void Awake()
    {
        var mr = GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;
        var cc = GetComponent<CapsuleCollider>();
        if (cc != null) cc.enabled = false;

        baseMat = FindBaseMaterial();
        Build();
    }

    void Build()
    {
        // ── 메인 동체 (F-35 특유의 두꺼운 타원 단면) ─────────────────────────
        P("Fuselage",       V( 0f,     0f,     0f),    V(0.62f, 0.43f, 3.20f), BodyGray);
        // 배면 (폭 넓고 편평한 F-35 하부)
        P("Belly",          V( 0f,    -0.19f,  0.12f), V(0.70f, 0.20f, 2.75f), PanelGray);
        P("BellyRear",      V( 0f,    -0.15f, -1.05f), V(0.54f, 0.17f, 0.78f), PanelGray);
        // 척추 능선 (F-35 특유의 두꺼운 dorsal spine)
        P("DorsalSpine",    V( 0f,     0.25f, -0.42f), V(0.22f, 0.19f, 1.88f), PanelGray);

        // ── 기수 (F-35의 짧고 굵은 뭉툭한 기수) ────────────────────────────────
        P("Nose",           V( 0f,     0.03f,  1.95f), V(0.49f, 0.39f, 0.70f), BodyGray, PrimitiveType.Sphere);
        P("NoseTip",        V( 0f,     0.01f,  2.33f), V(0.21f, 0.19f, 0.22f), PanelGray, PrimitiveType.Sphere);
        // 기수 채인: 스텔스 각진 측면
        P("ChineL",         V(-0.34f, -0.07f,  1.20f), V(0.18f, 0.10f, 1.52f), PanelGray, rot: Q(0f,  0f, -18f));
        P("ChineR",         V( 0.34f, -0.07f,  1.20f), V(0.18f, 0.10f, 1.52f), PanelGray, rot: Q(0f,  0f,  18f));

        // ── 조종석 캐노피 (F-35의 넓고 앞에 위치한 버블 캐노피) ───────────────────
        P("CanopyBase",     V( 0f,     0.23f,  1.22f), V(0.42f, 0.15f, 0.90f), BodyGray);
        P("Canopy",         V( 0f,     0.39f,  1.07f), V(0.36f, 0.31f, 0.74f), Cockpit, PrimitiveType.Sphere);
        P("CanopyFairing",  V( 0f,     0.27f,  0.58f), V(0.30f, 0.16f, 0.46f), PanelGray);
        // EO/DAS 광학 센서 돔 (기수 상부)
        P("EODome",         V( 0f,     0.28f,  1.66f), V(0.12f, 0.10f, 0.12f), Black, PrimitiveType.Sphere);

        // ── LERX (Leading Edge Root Extension) ──────────────────────────────────
        // F-35의 뚜렷한 strake: 주익과 동체를 부드럽게 연결
        P("LERX_L",         V(-0.56f,  0.01f,  0.83f), V(0.36f, 0.068f, 1.58f), PanelGray, rot: Q(0f, 16f, -2f));
        P("LERX_R",         V( 0.56f,  0.01f,  0.83f), V(0.36f, 0.068f, 1.58f), PanelGray, rot: Q(0f,-16f,  2f));

        // ── 주익 (35° 후퇴각 사다리꼴 델타) ─────────────────────────────────────
        P("WingInnL",       V(-1.13f, -0.01f, -0.06f), V(1.70f, 0.074f, 1.90f), WingGray, rot: Q(0f, 17f, -2f));
        P("WingInnR",       V( 1.13f, -0.01f, -0.06f), V(1.70f, 0.074f, 1.90f), WingGray, rot: Q(0f,-17f,  2f));
        P("WingOutL",       V(-2.30f, -0.01f, -0.52f), V(1.12f, 0.070f, 1.06f), WingGray, rot: Q(0f,  6f, -1.5f));
        P("WingOutR",       V( 2.30f, -0.01f, -0.52f), V(1.12f, 0.070f, 1.06f), WingGray, rot: Q(0f, -6f,  1.5f));
        // 날개 끝: F-35 사각형 클리핑 팁
        P("WingTipL",       V(-2.96f, -0.01f, -0.76f), V(0.30f, 0.068f, 0.40f), PanelGray);
        P("WingTipR",       V( 2.96f, -0.01f, -0.76f), V(0.30f, 0.068f, 0.40f), PanelGray);

        // ── 전동수평미익 (All-moving stabilators, 대형 델타형) ──────────────────
        P("StabL",          V(-0.84f, -0.13f, -1.74f), V(1.32f, 0.065f, 0.90f), WingGray, rot: Q(0f, 21f, -5f));
        P("StabR",          V( 0.84f, -0.13f, -1.74f), V(1.32f, 0.065f, 0.90f), WingGray, rot: Q(0f,-21f,  5f));

        // ── 단일 수직미익 (F-35A/B/C 공통 — F-22의 쌍발과 차별점) ─────────────
        P("VTail",          V( 0f,     0.72f, -1.24f), V(0.11f, 1.18f, 0.80f), BodyGray);
        P("VTailRudder",    V( 0f,     0.74f, -1.64f), V(0.10f, 0.88f, 0.23f), PanelGray);
        P("VTailRoot",      V( 0f,     0.17f, -1.08f), V(0.18f, 0.28f, 0.40f), PanelGray);

        // ── DSI 인테이크 (F-35 특징: 동체 하부 단일 대형 인테이크) ──────────────
        // Diverterless Supersonic Inlet — 사각형 입구 + 볼록 범프
        P("IntakeHousing",  V( 0f,    -0.30f,  0.50f), V(0.60f, 0.33f, 1.18f), IntakeDark);
        P("IntakeLip",      V( 0f,    -0.22f,  1.06f), V(0.62f, 0.14f, 0.16f), PanelGray);
        P("DSIBump",        V( 0f,    -0.16f,  0.70f), V(0.28f, 0.12f, 0.64f), IntakeDark); // 경계층 분리 범프
        P("IntakeHole",     V( 0f,    -0.32f,  0.58f), V(0.46f, 0.24f, 0.78f), Black);

        // ── F135 단발 엔진 노즐 (원형, 중앙) ─────────────────────────────────
        P("Nozzle",         V( 0f,    -0.02f, -1.80f), V(0.41f, 0.38f, 0.45f), Exhaust);
        P("NozzleInner",    V( 0f,    -0.02f, -1.93f), V(0.28f, 0.26f, 0.22f), Black);
        P("ExhaustGlow",    V( 0f,    -0.02f, -2.05f), V(0.20f, 0.18f, 0.10f), ExhGlow);

        // ── 내부 무장창 외형 (Internal Weapons Bay 돌출부) ───────────────────
        P("WpnBayL",        V(-0.22f, -0.26f,  0.06f), V(0.16f, 0.05f, 0.82f), PanelGray);
        P("WpnBayR",        V( 0.22f, -0.26f,  0.06f), V(0.16f, 0.05f, 0.82f), PanelGray);

        // ── 콜라이더 ──────────────────────────────────────────────────────────
        var col      = gameObject.AddComponent<CapsuleCollider>();
        col.direction = 2;
        col.height    = 4.0f;
        col.radius    = 0.30f;
        col.center    = new Vector3(0f, 0f, 0.05f);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────────────────
    static Material FindBaseMaterial()
    {
        string[] candidates = {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Lightweight Render Pipeline/Lit",
            "HDRP/Lit",
            "Standard",
            "Diffuse",
        };
        foreach (string n in candidates)
        {
            var sh = Shader.Find(n);
            if (sh != null) return new Material(sh);
        }
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r.sharedMaterial?.shader != null &&
                !r.sharedMaterial.shader.name.StartsWith("Hidden"))
                return new Material(r.sharedMaterial);
        }
        return new Material(Shader.Find("Sprites/Default"));
    }

    static Vector3    V(float x, float y, float z) => new Vector3(x, y, z);
    static Quaternion Q(float x, float y, float z) => Quaternion.Euler(x, y, z);

    void P(string partName, Vector3 pos, Vector3 scale, Color color,
           PrimitiveType type = PrimitiveType.Cube, Quaternion rot = default)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = partName;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot == default ? Quaternion.identity : rot;
        go.transform.localScale    = scale;

        Destroy(go.GetComponent<Collider>());

        var r   = go.GetComponent<Renderer>();
        var mat = new Material(baseMat);
        mat.color = color;

        // RAM 코팅: 낮은 반사율, 낮은 광택
        if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   0.38f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.18f);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.18f);

        r.material = mat;
    }
}

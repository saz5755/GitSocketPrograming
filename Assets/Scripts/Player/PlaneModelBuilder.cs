using UnityEngine;

/// <summary>
/// F-22 Raptor 스타일 비행기 모델을 Unity 기본 프리미티브로 구성합니다.
/// 마젠타 방지: 렌더 파이프라인(Standard/URP/HDRP)에 상관없이
/// CreatePrimitive의 sharedMaterial을 복제하여 올바른 셰이더를 유지합니다.
/// </summary>
[DisallowMultipleComponent]
public class PlaneModelBuilder : MonoBehaviour
{
    // ── F-22 스텔스 색상 팔레트 ─────────────────────────────────────────
    static readonly Color BodyGray   = new Color(0.28f, 0.30f, 0.33f); // 공중우세 회색
    static readonly Color PanelGray  = new Color(0.21f, 0.22f, 0.25f); // 어두운 패널
    static readonly Color WingGray   = new Color(0.32f, 0.33f, 0.36f); // 날개 (약간 밝음)
    static readonly Color Cockpit    = new Color(0.04f, 0.06f, 0.14f); // 조종석 틴트 (거의 검정)
    static readonly Color IntakeDark = new Color(0.16f, 0.17f, 0.19f); // 인테이크 외부
    static readonly Color Black      = new Color(0.04f, 0.04f, 0.05f); // 인테이크 내부
    static readonly Color Exhaust    = new Color(0.24f, 0.22f, 0.20f); // 배기 노즐
    static readonly Color ExhGlow    = new Color(0.90f, 0.50f, 0.12f); // 배기열 발광

    // 렌더 파이프라인 호환 기본 머티리얼 (Build()에서 한번만 가져옴)
    Material baseMat;

    void Awake()
    {
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;
        CapsuleCollider cc = GetComponent<CapsuleCollider>();
        if (cc != null) cc.enabled = false;

        // 빌드에서 Standard 셰이더가 스트립되는 문제 방지:
        // 렌더 파이프라인 순서대로 명시적으로 셰이더 탐색
        baseMat = FindBaseMaterial();

        Build();
    }

    void Build()
    {
        // ── 동체 ────────────────────────────────────────────────────────
        P("Fuselage",      V(0f,    0f,     0f),    V(0.50f, 0.36f, 3.80f), BodyGray);
        P("Belly",         V(0f,   -0.15f,  0.15f), V(0.66f, 0.12f, 3.50f), PanelGray);
        P("NoseSection",   V(0f,   -0.02f,  2.10f), V(0.38f, 0.28f, 0.90f), BodyGray,   PrimitiveType.Sphere);
        P("NoseTip",       V(0f,   -0.04f,  2.56f), V(0.14f, 0.12f, 0.28f), PanelGray,  PrimitiveType.Sphere);

        // 기수 채인 (스텔스 각진 측면 블렌딩)
        P("ChineL",        V(-0.30f,-0.07f, 1.40f), V(0.16f, 0.08f, 1.80f), PanelGray,  rot: Q(0f, 0f,-16f));
        P("ChineR",        V( 0.30f,-0.07f, 1.40f), V(0.16f, 0.08f, 1.80f), PanelGray,  rot: Q(0f, 0f, 16f));

        // ── 조종석 ──────────────────────────────────────────────────────
        P("CanopyBase",    V(0f,    0.20f,  1.05f), V(0.34f, 0.13f, 0.85f), BodyGray);
        P("Canopy",        V(0f,    0.31f,  0.95f), V(0.27f, 0.21f, 0.72f), Cockpit,    PrimitiveType.Sphere);

        // ── 주익 (크랭크드 애로우 델타) ──────────────────────────────────
        // 내측 주익 (후퇴각 ~42°)
        P("WingInnL",      V(-1.05f,-0.02f, 0.15f), V(1.80f, 0.068f, 2.20f), WingGray, rot: Q(0f, 22f,-1.5f));
        P("WingInnR",      V( 1.05f,-0.02f, 0.15f), V(1.80f, 0.068f, 2.20f), WingGray, rot: Q(0f,-22f, 1.5f));
        // 외측 주익 (후퇴각 ~18°)
        P("WingOutL",      V(-2.55f,-0.02f,-0.55f), V(1.25f, 0.065f, 1.10f), WingGray, rot: Q(0f,  8f,-1.0f));
        P("WingOutR",      V( 2.55f,-0.02f,-0.55f), V(1.25f, 0.065f, 1.10f), WingGray, rot: Q(0f, -8f, 1.0f));
        // 날개 끝 (잘린 형태)
        P("WingTipL",      V(-3.35f,-0.02f,-0.82f), V(0.35f, 0.064f, 0.45f), PanelGray);
        P("WingTipR",      V( 3.35f,-0.02f,-0.82f), V(0.35f, 0.064f, 0.45f), PanelGray);

        // ── 수평 미익 (전동 수평미익, 약간 내려감) ─────────────────────
        P("HTailL",        V(-1.05f,-0.11f,-1.72f), V(1.20f, 0.062f, 0.72f), WingGray, rot: Q(0f, 18f,-6f));
        P("HTailR",        V( 1.05f,-0.11f,-1.72f), V(1.20f, 0.062f, 0.72f), WingGray, rot: Q(0f,-18f, 6f));

        // ── 수직 미익 (쌍발, 외경 약 27° 경사) ─────────────────────────
        P("VTailL",        V(-0.42f, 0.52f,-1.32f), V(0.08f, 0.82f, 0.88f), BodyGray, rot: Q(0f,-10f,-27f));
        P("VTailR",        V( 0.42f, 0.52f,-1.32f), V(0.08f, 0.82f, 0.88f), BodyGray, rot: Q(0f, 10f, 27f));

        // ── DSI 인테이크 (동체 아래 측면) ───────────────────────────────
        P("IntakeL",       V(-0.42f,-0.20f, 0.72f), V(0.30f, 0.22f, 1.05f), IntakeDark);
        P("IntakeR",       V( 0.42f,-0.20f, 0.72f), V(0.30f, 0.22f, 1.05f), IntakeDark);
        P("IntakeHoleL",   V(-0.42f,-0.21f, 0.85f), V(0.22f, 0.16f, 0.55f), Black);
        P("IntakeHoleR",   V( 0.42f,-0.21f, 0.85f), V(0.22f, 0.16f, 0.55f), Black);

        // ── 엔진 노즐 (F-22 직사각형 노즐 형태) ────────────────────────
        P("NozzleL",       V(-0.22f, 0f,   -1.72f), V(0.22f, 0.20f, 0.40f), Exhaust);
        P("NozzleR",       V( 0.22f, 0f,   -1.72f), V(0.22f, 0.20f, 0.40f), Exhaust);
        // 배기열 (안쪽 발광)
        P("ExhaustL",      V(-0.22f, 0f,   -1.94f), V(0.15f, 0.13f, 0.08f), ExhGlow);
        P("ExhaustR",      V( 0.22f, 0f,   -1.94f), V(0.15f, 0.13f, 0.08f), ExhGlow);

        // ── 물리 콜라이더 ────────────────────────────────────────────────
        CapsuleCollider col = gameObject.AddComponent<CapsuleCollider>();
        col.direction = 2;
        col.height    = 4.4f;
        col.radius    = 0.26f;
        col.center    = new Vector3(0f, 0f, 0.1f);
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────
    static Material FindBaseMaterial()
    {
        // URP → HDRP → Standard → Diffuse 순서로 탐색
        // Shader.Find()는 빌드에 포함된 셰이더만 반환하므로 URP 빌드에서 Standard가 없어도 안전
        string[] candidates = {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Lightweight Render Pipeline/Lit",   // 구 LWRP/URP
            "HDRP/Lit",
            "Standard",
            "Diffuse",
        };
        foreach (string name in candidates)
        {
            Shader sh = Shader.Find(name);
            if (sh != null) return new Material(sh);
        }
        // 마지막 수단: 씬의 기존 렌더러에서 복제
        foreach (var r in FindObjectsOfType<Renderer>())
        {
            if (r.sharedMaterial?.shader != null &&
                !r.sharedMaterial.shader.name.StartsWith("Hidden"))
                return new Material(r.sharedMaterial);
        }
        return new Material(Shader.Find("Sprites/Default"));
    }

    static Vector3    V(float x, float y, float z) => new Vector3(x, y, z);
    static Quaternion Q(float x, float y, float z) => Quaternion.Euler(x, y, z);

    void P(string partName,
           Vector3       pos,
           Vector3       scale,
           Color         color,
           PrimitiveType type = PrimitiveType.Cube,
           Quaternion    rot  = default)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = partName;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot == default ? Quaternion.identity : rot;
        go.transform.localScale    = scale;

        // 시각 전용: 개별 콜라이더 제거
        Collider c = go.GetComponent<Collider>();
        if (c != null) Destroy(c);

        // 렌더 파이프라인 호환 머티리얼 (Standard/URP/HDRP 모두 마젠타 없음)
        Renderer r   = go.GetComponent<Renderer>();
        Material mat = new Material(baseMat);
        mat.color    = color;

        // 메탈릭/스무스 (Standard & URP 공통 프로퍼티명)
        if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   0.55f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.38f);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.38f);

        r.material = mat;
    }
}

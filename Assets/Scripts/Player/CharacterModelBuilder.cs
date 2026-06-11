using UnityEngine;

/// <summary>
/// 전투기 파일럿 인간형 모델.
/// PlaneModelBuilder 패턴을 동일하게 적용 — URP/Lit 우선 셰이더.
/// CharacterController와 같은 GameObject에 추가.
/// </summary>
[DisallowMultipleComponent]
public class CharacterModelBuilder : MonoBehaviour
{
    // ── 파일럿 색상 팔레트 ────────────────────────────────────────────────────
    static readonly Color SuitColor    = new Color(0.22f, 0.25f, 0.17f); // 올리브 드랍 비행복
    static readonly Color HelmetShell  = new Color(0.13f, 0.14f, 0.16f); // 헬멧 셸
    static readonly Color VisorColor   = new Color(0.04f, 0.07f, 0.13f); // 다크 선팅 바이저
    static readonly Color VestColor    = new Color(0.17f, 0.20f, 0.14f); // 전술 조끼
    static readonly Color GloveColor   = new Color(0.12f, 0.10f, 0.09f); // 장갑
    static readonly Color BootColor    = new Color(0.14f, 0.10f, 0.07f); // 전투화
    static readonly Color SkinColor    = new Color(0.76f, 0.60f, 0.47f); // 피부 (얼굴 일부)
    static readonly Color NeckColor    = new Color(0.20f, 0.23f, 0.16f); // 넥 가드

    Material _baseMat;

    void Awake()
    {
        // 루트 기본 렌더러/콜라이더 숨김 (PlaneModelBuilder 동일)
        var mr = GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;

        _baseMat = FindBaseMaterial();
        Build();
    }

    void Build()
    {
        // ── 머리·헬멧 ──────────────────────────────────────────────────────────
        // 바이저 안에 보이는 피부
        P("Face",         V( 0.00f, 1.720f,  0.12f), V(0.20f, 0.18f, 0.06f), SkinColor);
        // 헬멧 셸 (머리보다 살짝 큰 구체)
        P("Helmet",       V( 0.00f, 1.740f,  0.00f), V(0.30f, 0.29f, 0.30f), HelmetShell, PrimitiveType.Sphere);
        // 바이저 플레이트
        P("Visor",        V( 0.00f, 1.700f,  0.13f), V(0.23f, 0.13f, 0.04f), VisorColor);
        // 헬멧 친 스트랩 (왼·오른 작은 박스)
        P("ChinL",        V(-0.09f, 1.640f,  0.10f), V(0.04f, 0.08f, 0.04f), HelmetShell);
        P("ChinR",        V( 0.09f, 1.640f,  0.10f), V(0.04f, 0.08f, 0.04f), HelmetShell);
        // 산소 마스크 하단 돌출
        P("OxyMask",      V( 0.00f, 1.650f,  0.14f), V(0.14f, 0.06f, 0.06f), HelmetShell);

        // ── 목 ─────────────────────────────────────────────────────────────────
        P("Neck",         V( 0.00f, 1.570f,  0.00f), V(0.10f, 0.16f, 0.10f), NeckColor, PrimitiveType.Cylinder);

        // ── 상체 ───────────────────────────────────────────────────────────────
        P("UpperChest",   V( 0.00f, 1.400f,  0.00f), V(0.52f, 0.34f, 0.23f), SuitColor);
        // 전술 조끼 오버레이 (앞면)
        P("Vest",         V( 0.00f, 1.410f,  0.07f), V(0.46f, 0.28f, 0.06f), VestColor);
        // 조끼 포켓 (왼·오른)
        P("PocketL",      V(-0.16f, 1.450f,  0.10f), V(0.10f, 0.09f, 0.04f), HelmetShell);
        P("PocketR",      V( 0.16f, 1.450f,  0.10f), V(0.10f, 0.09f, 0.04f), HelmetShell);
        P("LowerChest",   V( 0.00f, 1.130f,  0.00f), V(0.42f, 0.24f, 0.22f), SuitColor);
        P("Hips",         V( 0.00f, 0.970f,  0.00f), V(0.44f, 0.18f, 0.24f), SuitColor);

        // ── 왼쪽 팔 ────────────────────────────────────────────────────────────
        P("LShoulderJoint", V(-0.31f, 1.540f, 0.00f), V(0.16f, 0.16f, 0.16f), SuitColor, PrimitiveType.Sphere);
        P("LUpperArm",      V(-0.35f, 1.360f, 0.00f), V(0.13f, 0.34f, 0.13f), SuitColor, PrimitiveType.Capsule);
        P("LForearm",       V(-0.36f, 1.020f, 0.01f), V(0.11f, 0.30f, 0.11f), SuitColor, PrimitiveType.Capsule);
        P("LHand",          V(-0.37f, 0.840f, 0.01f), V(0.10f, 0.14f, 0.09f), GloveColor);

        // ── 오른쪽 팔 ──────────────────────────────────────────────────────────
        P("RShoulderJoint", V( 0.31f, 1.540f, 0.00f), V(0.16f, 0.16f, 0.16f), SuitColor, PrimitiveType.Sphere);
        P("RUpperArm",      V( 0.35f, 1.360f, 0.00f), V(0.13f, 0.34f, 0.13f), SuitColor, PrimitiveType.Capsule);
        P("RForearm",       V( 0.36f, 1.020f, 0.01f), V(0.11f, 0.30f, 0.11f), SuitColor, PrimitiveType.Capsule);
        P("RHand",          V( 0.37f, 0.840f, 0.01f), V(0.10f, 0.14f, 0.09f), GloveColor);

        // ── 왼쪽 다리 ──────────────────────────────────────────────────────────
        P("LUpperLeg",    V(-0.14f, 0.710f,  0.00f), V(0.18f, 0.42f, 0.19f), SuitColor, PrimitiveType.Capsule);
        P("LLowerLeg",    V(-0.14f, 0.345f,  0.00f), V(0.14f, 0.32f, 0.14f), SuitColor, PrimitiveType.Capsule);
        // 전투화 (앞쪽으로 살짝 튀어나옴)
        P("LBoot",        V(-0.14f, 0.090f,  0.05f), V(0.16f, 0.14f, 0.28f), BootColor);
        P("LBootSole",    V(-0.14f, 0.020f,  0.05f), V(0.17f, 0.03f, 0.29f), HelmetShell);

        // ── 오른쪽 다리 ────────────────────────────────────────────────────────
        P("RUpperLeg",    V( 0.14f, 0.710f,  0.00f), V(0.18f, 0.42f, 0.19f), SuitColor, PrimitiveType.Capsule);
        P("RLowerLeg",    V( 0.14f, 0.345f,  0.00f), V(0.14f, 0.32f, 0.14f), SuitColor, PrimitiveType.Capsule);
        P("RBoot",        V( 0.14f, 0.090f,  0.05f), V(0.16f, 0.14f, 0.28f), BootColor);
        P("RBootSole",    V( 0.14f, 0.020f,  0.05f), V(0.17f, 0.03f, 0.29f), HelmetShell);
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────────
    void P(string partName, Vector3 pos, Vector3 scale, Color color,
           PrimitiveType type = PrimitiveType.Cube)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = partName;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale    = scale;

        var mat = new Material(_baseMat);
        mat.color = color;
        if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   0.0f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.3f);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.3f);

        go.GetComponent<Renderer>().material = mat;
    }

    static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

    static Material FindBaseMaterial()
    {
        string[] candidates = {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Lightweight Render Pipeline/Lit",
            "HDRP/Lit", "Standard", "Diffuse",
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
}

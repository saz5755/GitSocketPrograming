using UnityEngine;

/// <summary>
/// Player 프리팹에 추가하면 Awake 시 비행기 형태의 메시를 자동 생성합니다.
/// 기존 Capsule MeshRenderer/Collider는 자동으로 비활성화됩니다.
/// </summary>
[DisallowMultipleComponent]
public class PlaneModelBuilder : MonoBehaviour
{
    [Header("Colors")]
    public Color bodyColor    = new Color(0.15f, 0.35f, 0.75f);
    public Color wingColor    = new Color(0.20f, 0.45f, 0.85f);
    public Color cockpitColor = new Color(0.55f, 0.85f, 1.00f, 0.8f);
    public Color exhaustColor = new Color(0.25f, 0.25f, 0.25f);

    void Awake()
    {
        DisableDefaultCapsule();
        Build();
    }

    void DisableDefaultCapsule()
    {
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;

        CapsuleCollider cc = GetComponent<CapsuleCollider>();
        if (cc != null) cc.enabled = false;
    }

    void Build()
    {
        // ── 동체 (Fuselage) ──────────────────────────────
        // 앞뒤로 긴 몸통 (Z 축이 전방)
        Part("Fuselage",       new Vector3(0f,    0f,    0f),    new Vector3(0.45f, 0.42f, 3.2f),  PrimitiveType.Cube,    bodyColor);

        // 기수 (앞 덮개)
        Part("Nose",           new Vector3(0f,   -0.04f, 1.85f), new Vector3(0.32f, 0.30f, 0.90f), PrimitiveType.Sphere,  bodyColor);

        // 조종석 캐노피
        Part("Canopy",         new Vector3(0f,    0.26f, 0.80f), new Vector3(0.28f, 0.22f, 0.60f), PrimitiveType.Cube,    cockpitColor);

        // ── 주익 (Main Wings) ────────────────────────────
        Part("WingLeft",       new Vector3(-2.2f, 0f,    0.10f), new Vector3(3.5f,  0.07f, 1.10f), PrimitiveType.Cube,    wingColor);
        Part("WingRight",      new Vector3( 2.2f, 0f,    0.10f), new Vector3(3.5f,  0.07f, 1.10f), PrimitiveType.Cube,    wingColor);

        // 에일러론 (날개 끝 색상 강조)
        Part("AileronLeft",    new Vector3(-3.5f, 0f,   -0.05f), new Vector3(0.90f, 0.07f, 0.50f), PrimitiveType.Cube,    bodyColor);
        Part("AileronRight",   new Vector3( 3.5f, 0f,   -0.05f), new Vector3(0.90f, 0.07f, 0.50f), PrimitiveType.Cube,    bodyColor);

        // ── 꼬리날개 (Tail) ──────────────────────────────
        Part("HorizontalTail", new Vector3(0f,    0f,   -1.45f), new Vector3(1.80f, 0.06f, 0.60f), PrimitiveType.Cube,    wingColor);
        Part("VerticalTail",   new Vector3(0f,    0.45f,-1.30f), new Vector3(0.06f, 0.72f, 0.70f), PrimitiveType.Cube,    bodyColor);

        // ── 엔진 / 배기구 ────────────────────────────────
        Part("Engine",         new Vector3(0f,    0f,   -1.65f), new Vector3(0.36f, 0.36f, 0.22f), PrimitiveType.Cylinder, exhaustColor);

        // ── 물리 콜라이더 ────────────────────────────────
        CapsuleCollider col = gameObject.AddComponent<CapsuleCollider>();
        col.direction = 2;     // Z축 (전방)
        col.height    = 3.4f;
        col.radius    = 0.28f;
        col.center    = Vector3.zero;
    }

    GameObject Part(string partName, Vector3 localPos, Vector3 scale, PrimitiveType type, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = partName;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = scale;

        // 시각용 파트의 콜라이더 제거 (루트에만 사용)
        Collider c = go.GetComponent<Collider>();
        if (c != null) Destroy(c);

        // 머티리얼 설정
        Renderer r = go.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = color;

        // 조종석 반투명 처리
        if (color.a < 1f)
        {
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = 3000;
        }

        r.material = mat;

        return go;
    }
}

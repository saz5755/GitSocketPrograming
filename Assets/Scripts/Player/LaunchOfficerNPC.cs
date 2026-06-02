using UnityEngine;

/// <summary>
/// 항모 카타펄트 오피서 NPC (노란 저지).
/// CarrierController.BuildZones()에서 생성.
/// PreflightSystem.ReadyForLaunch 시 TriggerLaunchSignal()로 손 신호 애니메이션 시작.
/// </summary>
public class LaunchOfficerNPC : MonoBehaviour
{
    public static LaunchOfficerNPC Instance { get; private set; }

    // ── 팔 피벗 Transform ─────────────────────────────────────────────────────
    Transform _lArmPivot;   // 왼쪽 어깨 피벗 (이 GO를 회전)
    Transform _rArmPivot;   // 오른쪽 어깨 피벗

    // ── 상태 ──────────────────────────────────────────────────────────────────
    enum SignalState { Idle, Signaling }
    SignalState _state = SignalState.Idle;
    float       _phase;

    // ── 색상 ──────────────────────────────────────────────────────────────────
    static readonly Color Jersey     = new Color(1.00f, 0.75f, 0.00f); // 노란 저지 (카타펄트 오피서)
    static readonly Color Pants      = new Color(0.14f, 0.14f, 0.20f);
    static readonly Color HelmetCol  = new Color(1.00f, 0.88f, 0.00f);
    static readonly Color VisorCol   = new Color(0.06f, 0.08f, 0.14f);
    static readonly Color Glove      = new Color(0.08f, 0.08f, 0.10f);
    static readonly Color Boot       = new Color(0.13f, 0.10f, 0.07f);
    static readonly Color Skin       = new Color(0.76f, 0.60f, 0.47f);
    static readonly Color WandGreen  = new Color(0.10f, 0.90f, 0.25f); // 신호봉 녹색

    Material _mat;

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _mat     = MakeBaseMat();
        Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (_state == SignalState.Signaling)
            AnimateSignal();
        else
            AnimateIdle();
    }

    // ── 외부 API ─────────────────────────────────────────────────────────────
    public void TriggerLaunchSignal()  => _state = SignalState.Signaling;
    public void StopSignaling()        => _state = SignalState.Idle;

    // ── 애니메이션 ────────────────────────────────────────────────────────────
    void AnimateIdle()
    {
        // 미세한 호흡 흔들림
        float breath = Mathf.Sin(Time.time * 0.9f) * 3f;
        if (_lArmPivot != null)
            _lArmPivot.localRotation = Quaternion.Euler(breath, 0f, 0f);
        if (_rArmPivot != null)
            _rArmPivot.localRotation = Quaternion.Euler(breath, 0f, 0f);
    }

    void AnimateSignal()
    {
        _phase += Time.deltaTime;

        // 오른팔: 위로 들어 앞으로 휘두르는 발진 신호
        // −90° 기준 (정면 위) 에서 −40° ~ −120° 범위 스윙
        float rX = -80f + Mathf.Sin(_phase * 2.8f) * 35f;
        float rZ = -15f + Mathf.Sin(_phase * 2.8f + 0.4f) * 10f;

        // 왼팔: 약간 올려 포인팅
        float lX = -25f + Mathf.Sin(_phase * 1.4f) * 12f;

        if (_rArmPivot != null)
            _rArmPivot.localRotation = Quaternion.Euler(rX, 8f, rZ);
        if (_lArmPivot != null)
            _lArmPivot.localRotation = Quaternion.Euler(lX, -5f, 0f);
    }

    // ── 모델 빌드 ─────────────────────────────────────────────────────────────
    void Build()
    {
        // ── 몸통 ──────────────────────────────────────────────────────────────
        Mesh("Body",    new Vector3(0f,   1.12f,  0f),  new Vector3(0.40f, 0.48f, 0.24f), Jersey);
        Mesh("Hips",    new Vector3(0f,   0.82f,  0f),  new Vector3(0.36f, 0.18f, 0.22f), Pants);

        // ── 머리·헬멧 ─────────────────────────────────────────────────────────
        Mesh("Head",    new Vector3(0f,   1.62f,  0f),  new Vector3(0.23f, 0.24f, 0.23f), Skin,      PrimitiveType.Sphere);
        Mesh("Helmet",  new Vector3(0f,   1.66f,  0f),  new Vector3(0.30f, 0.30f, 0.30f), HelmetCol, PrimitiveType.Sphere);
        Mesh("Visor",   new Vector3(0f,   1.62f,  0.12f), new Vector3(0.22f, 0.14f, 0.04f), VisorCol);

        // ── 왼팔 (피벗 기반 — 어깨에서 회전) ──────────────────────────────────
        var lPivotGO = new GameObject("LArmPivot");
        lPivotGO.transform.SetParent(transform, false);
        lPivotGO.transform.localPosition = new Vector3(-0.25f, 1.45f, 0f);
        _lArmPivot = lPivotGO.transform;

        MeshInParent("LUpperArm", _lArmPivot, new Vector3(0f, -0.18f, 0f), new Vector3(0.12f, 0.34f, 0.12f), Jersey,  PrimitiveType.Capsule);
        MeshInParent("LForearm",  _lArmPivot, new Vector3(0f, -0.50f, 0f), new Vector3(0.10f, 0.28f, 0.10f), Jersey,  PrimitiveType.Capsule);
        MeshInParent("LHand",     _lArmPivot, new Vector3(0f, -0.70f, 0f), new Vector3(0.09f, 0.12f, 0.08f), Glove);

        // ── 오른팔 (피벗 기반) ────────────────────────────────────────────────
        var rPivotGO = new GameObject("RArmPivot");
        rPivotGO.transform.SetParent(transform, false);
        rPivotGO.transform.localPosition = new Vector3(0.25f, 1.45f, 0f);
        _rArmPivot = rPivotGO.transform;

        MeshInParent("RUpperArm", _rArmPivot, new Vector3(0f, -0.18f, 0f), new Vector3(0.12f, 0.34f, 0.12f), Jersey,  PrimitiveType.Capsule);
        MeshInParent("RForearm",  _rArmPivot, new Vector3(0f, -0.50f, 0f), new Vector3(0.10f, 0.28f, 0.10f), Jersey,  PrimitiveType.Capsule);
        MeshInParent("RHand",     _rArmPivot, new Vector3(0f, -0.70f, 0f), new Vector3(0.09f, 0.12f, 0.08f), Glove);

        // 신호봉 (오른손에 부착)
        var wand = new GameObject("Wand");
        wand.transform.SetParent(_rArmPivot, false);
        wand.transform.localPosition = new Vector3(0f, -0.86f, 0f);
        wand.transform.localScale    = new Vector3(0.025f, 0.28f, 0.025f);
        ApplyMesh(wand, WandGreen, PrimitiveType.Cylinder);

        // ── 다리 ──────────────────────────────────────────────────────────────
        Mesh("LUpperLeg", new Vector3(-0.11f, 0.57f,  0f),  new Vector3(0.16f, 0.38f, 0.16f), Pants, PrimitiveType.Capsule);
        Mesh("RUpperLeg", new Vector3( 0.11f, 0.57f,  0f),  new Vector3(0.16f, 0.38f, 0.16f), Pants, PrimitiveType.Capsule);
        Mesh("LLowerLeg", new Vector3(-0.11f, 0.27f,  0f),  new Vector3(0.13f, 0.28f, 0.13f), Pants, PrimitiveType.Capsule);
        Mesh("RLowerLeg", new Vector3( 0.11f, 0.27f,  0f),  new Vector3(0.13f, 0.28f, 0.13f), Pants, PrimitiveType.Capsule);
        Mesh("LBoot",     new Vector3(-0.11f, 0.07f,  0.04f), new Vector3(0.14f, 0.12f, 0.24f), Boot);
        Mesh("RBoot",     new Vector3( 0.11f, 0.07f,  0.04f), new Vector3(0.14f, 0.12f, 0.24f), Boot);

        // 조끼 리플렉터 테이프 (시인성)
        Mesh("ReflL", new Vector3(-0.14f, 1.15f, 0.13f), new Vector3(0.04f, 0.28f, 0.02f), Color.white);
        Mesh("ReflR", new Vector3( 0.14f, 1.15f, 0.13f), new Vector3(0.04f, 0.28f, 0.02f), Color.white);
    }

    // ── 빌드 헬퍼 ─────────────────────────────────────────────────────────────
    void Mesh(string name, Vector3 localPos, Vector3 size, Color col,
              PrimitiveType ptype = PrimitiveType.Cube)
    {
        var go = CreatePart(name, transform, localPos, size, col, ptype);
        _ = go;
    }

    void MeshInParent(string name, Transform parent, Vector3 localPos, Vector3 size, Color col,
                      PrimitiveType ptype = PrimitiveType.Cube)
    {
        CreatePart(name, parent, localPos, size, col, ptype);
    }

    GameObject CreatePart(string name, Transform parent, Vector3 localPos, Vector3 size,
                           Color col, PrimitiveType ptype)
    {
        var go = GameObject.CreatePrimitive(ptype);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = size;
        Destroy(go.GetComponent<Collider>());
        ApplyMesh(go, col, ptype);
        return go;
    }

    void ApplyMesh(GameObject go, Color col, PrimitiveType ptype)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) return;
        var mat = new Material(_mat) { color = col };
        mr.sharedMaterial        = mat;
        mr.shadowCastingMode     = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows        = false;
    }

    static Material MakeBaseMat()
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit")
              ?? Shader.Find("Standard");
        return new Material(sh);
    }
}

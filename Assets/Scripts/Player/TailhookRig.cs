using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 전투기 테일훅 — 후방 동체 하부에 접혀 있다가 항모 접근 시 내려오는 어레스팅 훅.
/// ArrestingWireSystem이 항모 주변 항공기에 자동 부착한다 (KF21_Model 보유 기체).
///   · 접근 판정: 착함 방향으로 정렬해 항모 후방에서 저고도 접근 중일 때 전개 (실제로는 조종사가 HOOK 레버 조작).
///     에스코트 AI는 착함 접근/체결 단계에서만 전개.
///   · 전개 시 감쇠 스프링으로 살짝 튕기며 내려오고, 갑판에 닿으면 훅 끝이 갑판을 긁으며 따라 들림
///   · 와이어 체결 중엔 고정, 정지 후 회수
/// 메쉬는 흑백 띠 도장의 훅 암 + 강철 훅 포인트(슈/목) — 재질별로 병합해 렌더러 3개.
/// </summary>
public class TailhookRig : MonoBehaviour
{
    // ── 치수 (기체 루트 로컬, m) — KF21 모델(약 8.4m) 후방 하부 실측 기준 ─────
    static readonly Vector3 PivotLocal = new Vector3(0f, 0.80f, -2.25f);
    const float ArmLength     = 1.25f;
    const float StowedAngle   = -8f;    // 수평 기준, +가 아래 — 동체 하부에 붙음
    const float DeployedAngle = 46f;    // 공중 자유 전개 각 (바퀴보다 낮게 늘어짐)
    const float DeckContactY  = 0.02f;  // 훅 끝이 갑판 위로 유지할 높이

    // ── 전개 판정 ────────────────────────────────────────────────────────────
    const float DeployRange   = 2500f;  // 와이어 후방 거리
    const float DeployLateral = 350f;   // 착함 축에서 좌우 이탈 허용
    const float DeployHeight  = 160f;
    const float DeploySpeed   = 22f;
    const float DeployAlign   = 0.6f;   // 기수와 착함 방향 내적 (약 53° 이내)
    const int   ArmStripes    = 7;

    static Material s_whiteMat, s_blackMat, s_steelMat;
    static readonly RaycastHit[] s_hits = new RaycastHit[6];

    PlayerController _pc;
    EscortAI         _escort;

    Transform _pivot;
    float     _angle = StowedAngle, _angVel;
    bool      _engaged;
    float     _stowHold;          // 체결 해제 후 회수까지 대기
    Vector3   _lastPos;
    float     _speed;

    public bool    IsEngaged  => _engaged;
    public bool    IsDeployed => _angle > DeployedAngle * 0.5f;
    /// <summary>훅 포인트(케이블이 걸리는 목) 월드 좌표</summary>
    public Vector3 TipWorld   => _pivot != null ? _pivot.TransformPoint(new Vector3(0f, -0.07f, -ArmLength - 0.05f)) : transform.position;

    public static TailhookRig Ensure(Transform aircraft)
    {
        var rig = aircraft.GetComponent<TailhookRig>();
        if (rig == null && aircraft.Find("KF21_Model") != null)
            rig = aircraft.gameObject.AddComponent<TailhookRig>();
        return rig;
    }

    public void SetEngaged(bool engaged)
    {
        if (_engaged && !engaged) _stowHold = 2.2f;   // 롤백 후 잠시 뒤 훅을 들어올린다
        _engaged = engaged;
    }

    void Awake()
    {
        EnsureShared();

        // 모델 가시성(탑승/하차 시 렌더러 토글)을 그대로 따르도록 KF21_Model 아래에 붙인다
        Transform model = transform.Find("KF21_Model");
        var root = new GameObject("Tailhook").transform;
        root.SetParent(model != null ? model : transform, false);
        root.position = transform.TransformPoint(PivotLocal);
        root.rotation = transform.rotation;
        Vector3 ls = root.lossyScale;
        root.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z) * transform.lossyScale.x;

        _pivot = new GameObject("HookPivot").transform;
        _pivot.SetParent(root, false);
        BuildHookMesh();
        ApplyAngle();

        _lastPos = transform.position;
        _pc      = GetComponent<PlayerController>();
        _escort  = GetComponent<EscortAI>();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        _speed   = Mathf.Lerp(_speed, (transform.position - _lastPos).magnitude / dt, 1f - Mathf.Exp(-dt * 6f));
        _lastPos = transform.position;
        if (_stowHold > 0f) _stowHold -= dt;

        bool deploy = _engaged || (_stowHold <= 0f && WantsDeploy());
        float target = deploy ? DeployedAngle : StowedAngle;

        // 유압 댐퍼 — 내릴 땐 중력으로 빠르게 + 약간의 튕김, 올릴 땐 천천히
        float k = deploy ? 38f : 14f, c = deploy ? 6f : 7.5f;   // 전개: 저감쇠(튕김), 회수: 임계 감쇠
        _angVel += ((target - _angle) * k - _angVel * c) * dt;
        _angle  += _angVel * dt;

        if (_angle > 5f) ConstrainToDeck();
        ApplyAngle();
    }

    bool WantsDeploy()
    {
        var aws = ArrestingWireSystem.Instance;
        if (aws == null) return false;

        // 직접 조종 중이 아닌 에스코트는 AI 단계로 판단 — 항모 주변 자유비행 중엔 접어 둔다
        if (_escort != null && (_pc == null || !_pc.isLocalPlayer))
            return _escort.IsInApproachRun || _escort.IsBeingArrested;

        if (_speed < DeploySpeed) return false;
        Vector3 dir   = aws.LandingDirection;
        Vector3 rel   = transform.position - aws.WireCenter;
        float along   = Vector3.Dot(rel, dir);                       // 음수 = 와이어 후방(접근 중)
        float lateral = Vector3.Cross(dir, rel).magnitude;
        bool aligned  = Vector3.Dot(transform.forward, dir) > DeployAlign;
        return aligned && along > -DeployRange && along < 60f && lateral < DeployLateral && rel.y < DeployHeight;
    }

    // 훅 끝이 갑판(또는 지면)을 뚫지 않게 — 닿으면 각도를 줄여 갑판 위를 긁으며 끌린다
    void ConstrainToDeck()
    {
        Vector3 pivotW = _pivot.position;
        int n = Physics.RaycastNonAlloc(pivotW, Vector3.down, s_hits, ArmLength + 0.5f,
                                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float deckY = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            if (s_hits[i].transform.IsChildOf(transform)) continue;
            if (s_hits[i].point.y > deckY) deckY = s_hits[i].point.y;
        }
        if (deckY <= float.MinValue) return;

        float scale = _pivot.lossyScale.y;
        float drop  = pivotW.y - (deckY + DeckContactY) - 0.1f * scale;   // 힐이 암 축보다 약 10cm 아래
        float maxAngle = drop <= 0f ? 0f : Mathf.Asin(Mathf.Clamp01(drop / (ArmLength * scale))) * Mathf.Rad2Deg;
        if (_angle > maxAngle)
        {
            _angle  = maxAngle;
            if (_angVel > 0f) _angVel = -_angVel * 0.25f;   // 갑판 충격에 살짝 튕김
        }
    }

    // 암이 -z로 뻗어 있으므로 X축 음의 회전이 끝을 아래로 내린다
    void ApplyAngle() => _pivot.localRotation = Quaternion.Euler(-_angle, 0f, 0f);

    // ── 메쉬 ─────────────────────────────────────────────────────────────────

    static void EnsureShared()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (s_whiteMat == null) s_whiteMat = MakeLit(lit, "TailhookWhite", new Color(0.86f, 0.86f, 0.84f), 0.15f, 0.55f);
        if (s_blackMat == null) s_blackMat = MakeLit(lit, "TailhookBlack", new Color(0.05f, 0.05f, 0.055f), 0.15f, 0.5f);
        if (s_steelMat == null) s_steelMat = MakeLit(lit, "TailhookSteel", new Color(0.62f, 0.61f, 0.6f), 1f, 0.72f);
    }

    static Material MakeLit(Shader sh, string name, Color c, float metallic, float smoothness)
    {
        var m = new Material(sh) { name = name };
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        return m;
    }

    void BuildHookMesh()
    {
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var cyl  = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");

        // 훅 암 — 피벗에서 후방(-z)으로 뻗은 원통을 흑백 띠 마디로 나눠 도장 (텍스처 없이 선명)
        var white = new System.Collections.Generic.List<CombineInstance>();
        var black = new System.Collections.Generic.List<CombineInstance>();
        float seg = ArmLength / ArmStripes;
        for (int i = 0; i < ArmStripes; i++)
        {
            float r = Mathf.Lerp(0.078f, 0.066f, (float)i / ArmStripes);   // 끝으로 갈수록 가늘게
            var part = Part(cyl, new Vector3(0f, 0f, -seg * (i + 0.5f)), Quaternion.Euler(90f, 0f, 0f), new Vector3(r, seg * 0.5f, r));
            (i % 2 == 0 ? white : black).Add(part);
        }

        var steel = new[]
        {
            // 피벗 힌지
            Part(cyl,  Vector3.zero, Quaternion.Euler(0f, 0f, 90f), new Vector3(0.11f, 0.09f, 0.11f)),
            // 훅 섕크 (암 끝의 강철부)
            Part(cyl,  new Vector3(0f, 0f, -ArmLength - 0.02f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.068f, 0.07f, 0.068f)),
            // 훅 포인트 — 아래로 꺾인 목(throat): 케이블이 앞쪽에서 걸려 들어오는 갈고리
            Part(cube, new Vector3(0f, -0.05f, -ArmLength - 0.09f), Quaternion.Euler(-35f, 0f, 0f), new Vector3(0.07f, 0.13f, 0.055f)),
            Part(cube, new Vector3(0f, -0.10f, -ArmLength - 0.01f), Quaternion.Euler(20f, 0f, 0f), new Vector3(0.065f, 0.035f, 0.11f)),
            // 갑판을 긁는 둥근 힐(슈)
            Part(cyl,  new Vector3(0f, -0.085f, -ArmLength - 0.13f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.075f, 0.036f, 0.075f)),
        };

        MakeCombined("HookArmWhite", white.ToArray(), s_whiteMat);
        MakeCombined("HookArmBlack", black.ToArray(), s_blackMat);
        MakeCombined("HookPoint",    steel, s_steelMat);
    }

    static CombineInstance Part(Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale)
        => new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(pos, rot, scale) };

    void MakeCombined(string name, CombineInstance[] parts, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_pivot, false);
        var m = new Mesh { name = name };
        m.CombineMeshes(parts, true, true);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial    = mat;
        r.shadowCastingMode = ShadowCastingMode.On;
    }
}

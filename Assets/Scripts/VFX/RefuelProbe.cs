using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 공중급유 프로브 — 조종석 오른쪽 앞 동체 옆에 붙은 페어링(덮개)에서 안쪽 관이 앞으로 늘어나는 접이식 급유관.
///   · 페어링은 동체 옆면에 반쯤 묻혀 항상 붙어 있다 (접힌 상태 = 앞이 둥근 짧은 덮개만 보임)
///   · 펴지면 안쪽 관이 페어링 앞에서 미끄러져 나와 끝이 기수보다 약 1m 앞까지 뻗는다 (끝에 노즐)
///   로컬: AerialRefuelSystem 이 급유기 근처에서 펴고 접는다 (Extended)
///   원격: 급유 접속 중이거나 급유기에 가까우면 편다 — 다른 화면에서도 프로브가 바스켓에 물린 모습이 보인다
/// 기수 위치는 KF21 모델 바운드로 계산한다 (KF21_Model 앞 = +Z).
/// </summary>
public class RefuelProbe : MonoBehaviour
{
    public static readonly List<RefuelProbe> All = new();

    // 기체 루트 로컬 (KF21_Model scale 0.5 기준 — 기수 끝 z ≈ 4.2, 조종석 z ≈ 1.9~3.1)
    const float SideX      = 0.34f;    // 동체 옆면에 반쯤 묻히는 위치
    const float AxisY      = 1.24f;    // 조종석 턱(sill) 아래 동체 옆 — 캐노피 유리에 겹치지 않게
    const float FairBack   = 2.75f;    // 페어링 뒤끝 = 기수 − 2.75
    const float FairFront  = 1.05f;    // 페어링 앞끝 = 기수 − 1.05
    const float TipAhead   = 0.95f;    // 전개 시 노즐 끝 = 기수 + 0.95
    const float FairRadius = 0.075f, TubeRadius = 0.04f;
    const float ExtendTime = 1.6f;     // 완전 전개 시간 (초)

    public PlayerController Owner { get; private set; }
    /// <summary>로컬 기체에서 AerialRefuelSystem 이 지정</summary>
    public bool  Extended { get; set; }
    public float Deploy01 => _deploy;
    public bool  Ready => _deploy > 0.98f;
    /// <summary>현재 노즐 끝 (월드)</summary>
    public Vector3 TipWorld => transform.TransformPoint(TipLocal(Ease(_deploy)));
    /// <summary>완전히 펴졌을 때의 노즐 끝 (월드) — 급유기 동행이 미리 겨냥할 때</summary>
    public Vector3 DeployedTipWorld => transform.TransformPoint(TipLocal(1f));

    float _noseZ = 4.2f;
    Transform _fairing, _fairCap, _fairTail, _tube, _collar, _nozzle;
    float _deploy, _applied = -1f;

    public static RefuelProbe Ensure(Transform aircraft)
    {
        if (aircraft == null || aircraft.Find("KF21_Model") == null) return null;
        var p = aircraft.GetComponent<RefuelProbe>();
        return p != null ? p : aircraft.gameObject.AddComponent<RefuelProbe>();
    }

    void Awake() => All.Add(this);
    void OnDestroy() => All.Remove(this);

    float Z0 => _noseZ - FairBack;
    float Z1 => _noseZ - FairFront;
    Vector3 TipLocal(float e) => new Vector3(SideX, AxisY, Mathf.Lerp(Z1 + 0.04f, _noseZ + TipAhead, e));
    static float Ease(float t) => t * t * (3f - 2f * t);   // 미끄러져 나올 때 시작·끝을 부드럽게

    void Start()
    {
        Owner = GetComponent<PlayerController>();
        var model = transform.Find("KF21_Model");
        if (model != null)
        {
            // 렌더러 바운드 8 꼭짓점을 기체 로컬로 옮겨 최전방 z
            float maxZ = float.MinValue;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    maxZ = Mathf.Max(maxZ, transform.InverseTransformPoint(c).z);
                }
            }
            // 바운드가 비정상이면(다른 자세로 계산되는 등) 기본값 유지
            if (maxZ > 2.5f && maxZ < 8f) _noseZ = maxZ;
        }

        var shell = NewMat(new Color(0.16f, 0.17f, 0.18f), 0.35f, 0.45f);   // 동체색 페어링
        var metal = NewMat(new Color(0.58f, 0.60f, 0.63f), 0.75f, 0.60f);   // 금속 관
        var dark  = NewMat(new Color(0.10f, 0.10f, 0.11f), 0.5f, 0.5f);

        // 페어링: 원통 + 앞 둥근 캡 + 뒤 길게 늘인 꼬리(동체로 녹아드는 모양)
        float flen = Z1 - Z0;
        _fairing = MakePart(PrimitiveType.Cylinder, "ProbeFairing", shell);
        _fairing.localRotation = Quaternion.Euler(90f, 0f, 0f);
        _fairing.localPosition = new Vector3(SideX, AxisY, (Z0 + Z1) * 0.5f);
        _fairing.localScale = new Vector3(FairRadius * 2f, flen * 0.5f, FairRadius * 2f);
        _fairCap = MakePart(PrimitiveType.Sphere, "ProbeFairingCap", shell);
        _fairCap.localPosition = new Vector3(SideX, AxisY, Z1);
        _fairCap.localScale = new Vector3(FairRadius * 2f, FairRadius * 2f, FairRadius * 2.4f);
        _fairTail = MakePart(PrimitiveType.Sphere, "ProbeFairingTail", shell);
        _fairTail.localPosition = new Vector3(SideX - 0.02f, AxisY, Z0);
        _fairTail.localScale = new Vector3(FairRadius * 1.8f, FairRadius * 1.8f, 0.9f);

        // 안쪽 관 + 이음 고리 + 노즐
        _tube   = MakePart(PrimitiveType.Cylinder, "ProbeTube", metal);
        _tube.localRotation = Quaternion.Euler(90f, 0f, 0f);
        _collar = MakePart(PrimitiveType.Cylinder, "ProbeCollar", dark);
        _collar.localRotation = Quaternion.Euler(90f, 0f, 0f);
        _collar.localScale = new Vector3(FairRadius * 1.6f, 0.03f, FairRadius * 1.6f);
        _collar.localPosition = new Vector3(SideX, AxisY, Z1 + 0.06f);
        _nozzle = MakePart(PrimitiveType.Sphere, "ProbeNozzle", metal);
        _nozzle.localScale = new Vector3(0.12f, 0.12f, 0.2f);
        Apply();
    }

    static Material NewMat(Color c, float metallic, float smooth)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { hideFlags = HideFlags.DontSave };
        m.SetColor("_BaseColor", c); m.SetColor("_Color", c);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smooth);
        return m;
    }

    Transform MakePart(PrimitiveType type, string n, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = n;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        return go.transform;
    }

    void Update()
    {
        if (Owner == null) return;
        bool want = Extended;
        if (!Owner.isLocalPlayer)
        {
            want = Owner.FxRefuelContact;
            if (!want)
            {
                var rig = TankerDrogueRig.Nearest(transform.position, out float d);
                want = rig != null && rig.transform != transform && d < 400f;
            }
        }
        _deploy = Mathf.MoveTowards(_deploy, want ? 1f : 0f, Time.deltaTime / ExtendTime);
        if (!Mathf.Approximately(_deploy, _applied)) Apply();
    }

    // 안쪽 관은 페어링 안(뒤끝 근처)에서 시작해 노즐까지 — 접히면 노즐만 페어링 앞에 붙어 있다
    void Apply()
    {
        if (_tube == null) return;
        _applied = _deploy;
        float e = Ease(_deploy);
        Vector3 tip = TipLocal(e);
        float back = Z0 + 0.3f;
        float len = tip.z - back;
        _tube.localPosition = new Vector3(SideX, AxisY, back + len * 0.5f);
        _tube.localScale = new Vector3(TubeRadius * 2f, len * 0.5f, TubeRadius * 2f);
        _nozzle.localPosition = tip;
        _tube.gameObject.SetActive(e > 0.01f);
        _collar.gameObject.SetActive(e > 0.01f);
    }
}

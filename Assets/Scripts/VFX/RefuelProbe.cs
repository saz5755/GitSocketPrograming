using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 공중급유 프로브 — 기수 오른쪽에서 앞으로 펴지는 접이식 급유관 (프로브-드로그 수급 장비).
///   로컬: AerialRefuelSystem 이 급유기 근처에서 펴고 접는다 (Extended)
///   원격: 급유 접속 중이거나 급유기에 가까우면 편다 — 다른 화면에서도 프로브가 바스켓에 물린 모습이 보인다
/// 기수 위치는 KF21 모델 바운드로 계산한다.
/// </summary>
public class RefuelProbe : MonoBehaviour
{
    public static readonly List<RefuelProbe> All = new();

    const float Length = 3.6f, Radius = 0.07f, ExtendSpeed = 0.7f;    // 완전 전개 약 1.4초 — 끝이 기수보다 약 1m 앞

    public PlayerController Owner { get; private set; }
    /// <summary>로컬 기체에서 AerialRefuelSystem 이 지정</summary>
    public bool  Extended { get; set; }
    public float Deploy01 => _deploy;
    public bool  Ready => _deploy > 0.98f;
    public Vector3 TipWorld => transform.TransformPoint(_base + Vector3.forward * (Length * _deploy + 0.12f));

    Vector3 _base;
    Transform _tube, _tip;
    float _deploy;

    public static RefuelProbe Ensure(Transform aircraft)
    {
        if (aircraft == null || aircraft.Find("KF21_Model") == null) return null;
        var p = aircraft.GetComponent<RefuelProbe>();
        return p != null ? p : aircraft.gameObject.AddComponent<RefuelProbe>();
    }

    void Awake() => All.Add(this);
    void OnDestroy() => All.Remove(this);

    void Start()
    {
        Owner = GetComponent<PlayerController>();
        // 기수 끝 = 모델 바운드 최전방. 프로브는 조종석 오른쪽 앞 동체 위에서 앞으로 뻗는다
        float noseZ = 7f, topY = 1.6f;
        var model = transform.Find("KF21_Model");
        if (model != null)
        {
            float maxZ = float.MinValue;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>())
            {
                Vector3 c = transform.InverseTransformPoint(r.bounds.center);
                Vector3 e = transform.InverseTransformVector(r.bounds.extents);
                maxZ = Mathf.Max(maxZ, c.z + Mathf.Abs(e.z));
            }
            if (maxZ > 0f) noseZ = maxZ;
        }
        // 조종석 앞 동체 오른쪽에서 나와 기수보다 앞으로 뻗는다 — 바스켓이 기수 앞에서 물리고 호스가 캐노피를 가리지 않게
        _base = new Vector3(0.62f, topY, noseZ - 2.8f);

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { hideFlags = HideFlags.DontSave };
        mat.SetColor("_BaseColor", new Color(0.55f, 0.57f, 0.6f));
        mat.SetFloat("_Metallic", 0.6f);
        mat.SetFloat("_Smoothness", 0.55f);

        _tube = MakePart(PrimitiveType.Cylinder, "RefuelProbe", mat);
        _tip  = MakePart(PrimitiveType.Sphere, "ProbeNozzle", mat);
        _tip.localScale = Vector3.one * 0.2f;
        Apply();
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
        float target = want ? 1f : 0f;
        if (Mathf.Approximately(_deploy, target)) return;
        _deploy = Mathf.MoveTowards(_deploy, target, ExtendSpeed * Time.deltaTime);
        Apply();
    }

    // 원통(높이 2, 축 Y) 을 앞(+Z)으로 눕혀 base 에서 현재 길이만큼 뻗는다. 접히면 동체 속으로 숨는다
    void Apply()
    {
        if (_tube == null) return;
        float len = Mathf.Max(0.01f, Length * _deploy);
        _tube.localRotation = Quaternion.Euler(90f, 0f, 0f);
        _tube.localPosition = _base + Vector3.forward * (len * 0.5f);
        _tube.localScale = new Vector3(Radius * 2f, len * 0.5f, Radius * 2f);
        _tip.localPosition = _base + Vector3.forward * (len + 0.05f);
        bool vis = _deploy > 0.02f;
        _tube.gameObject.SetActive(vis);
        _tip.gameObject.SetActive(vis);
    }
}

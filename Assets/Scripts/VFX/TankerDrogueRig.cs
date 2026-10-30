using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 급유기 호스·드로그(바스켓) — 프로브-드로그 방식 공중급유 (해군 함재기·버디 탱킹).
///   · 동체 아래 급유 포드에서 호스가 늘어져 뒤쪽 아래로 바스켓이 끌려간다. 기류에 따라 천천히 흔들린다
///   · 포드 표시등: 황색 = 급유 준비, 녹색 = 연료 이송 중
///   · 접속하면 바스켓이 수급기 프로브 끝에 물린 채 따라가고, 호스는 밀려 들어간 만큼 처진다
/// 모든 클라이언트가 급유기 위치로부터 같은 모양을 각자 그린다 (호스는 동기화하지 않음).
/// 접속 상태는 수급기 MOVE 의 fx 비트(PlayerController.FxRefuelContact)로 전해진다.
/// </summary>
public class TankerDrogueRig : MonoBehaviour
{
    public static readonly List<TankerDrogueRig> All = new();

    // 급유기 루트 로컬 — 동체 중심선 아래 버디 급유 포드, 호스 출구는 포드 뒤끝
    static readonly Vector3 PodPos      = new Vector3(0f, 0.45f, -1.2f);
    static readonly Vector3 HoseExit    = new Vector3(0f, 0.30f, -3.0f);
    static readonly Vector3 BasketRest  = new Vector3(0f, -2.4f, -19.5f);   // 호스 약 17m 전개

    const int HosePoints = 18;

    public Transform  Tanker => transform;
    public Vector3    RestBasketWorld => transform.TransformPoint(BasketRest);
    public Vector3    BasketWorld => _basket != null ? _basket.position : RestBasketWorld;
    public Vector3    Velocity { get; private set; }
    /// <summary>현재 바스켓을 물고 있는 프로브 (없으면 null)</summary>
    public RefuelProbe Attached { get; private set; }
    public bool       Flowing { get; set; }

    Transform    _basket;
    LineRenderer _hose;
    FXLib.Flare  _amber, _green;
    Vector3      _lastPos, _basketVel;
    float        _seed;

    public static TankerDrogueRig Ensure(GameObject tanker)
    {
        var r = tanker.GetComponent<TankerDrogueRig>();
        return r != null ? r : tanker.AddComponent<TankerDrogueRig>();
    }

    public static TankerDrogueRig Nearest(Vector3 p, out float dist)
    {
        TankerDrogueRig best = null; dist = float.MaxValue;
        foreach (var r in All)
        {
            if (r == null || !r.isActiveAndEnabled) continue;
            float d = Vector3.Distance(p, r.transform.position);
            if (d < dist) { dist = d; best = r; }
        }
        return best;
    }

    void Awake()
    {
        All.Add(this);
        _seed = Random.Range(0f, 100f);
        Build();
        _lastPos = transform.position;
    }

    void OnDestroy()
    {
        All.Remove(this);
        if (_basket != null) Destroy(_basket.gameObject);
        if (_hose != null) Destroy(_hose.gameObject);
    }

    static Material s_hoseMat, s_basketMat, s_podMat;

    static Material Lit(Color c, float smooth, bool twoSided)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh) { hideFlags = HideFlags.DontSave };
        m.SetColor("_BaseColor", c);
        m.color = c;
        m.SetFloat("_Smoothness", smooth);
        if (twoSided) m.SetFloat("_Cull", 0f);
        return m;
    }

    void Build()
    {
        s_hoseMat   ??= Lit(new Color(0.08f, 0.08f, 0.085f), 0.35f, false);
        s_basketMat ??= Lit(new Color(0.86f, 0.86f, 0.84f), 0.25f, true);
        s_podMat    ??= Lit(new Color(0.42f, 0.44f, 0.46f), 0.45f, false);

        // 버디 급유 포드 (동체 아래 매달린 원통)
        var pod = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        pod.name = "RefuelPod";
        Destroy(pod.GetComponent<Collider>());
        pod.transform.SetParent(transform, false);
        pod.transform.localPosition = PodPos;
        pod.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        pod.transform.localScale = new Vector3(0.55f, 1.7f, 0.55f);
        var pr = pod.GetComponent<MeshRenderer>();
        pr.sharedMaterial = s_podMat;
        pr.shadowCastingMode = ShadowCastingMode.Off;

        _amber = FXLib.MakeFlare("PodAmber", transform, HoseExit + new Vector3(-0.22f, -0.1f, 0.1f), 0.12f, 0.0012f);
        _green = FXLib.MakeFlare("PodGreen", transform, HoseExit + new Vector3(0.22f, -0.1f, 0.1f), 0.12f, 0.0012f);

        // 호스 — 월드 공간 선 (급유기와 바스켓 사이를 매 프레임 다시 그린다)
        var hgo = new GameObject("RefuelHose_" + name);
        _hose = hgo.AddComponent<LineRenderer>();
        _hose.sharedMaterial = s_hoseMat;
        _hose.useWorldSpace = true;
        _hose.positionCount = HosePoints;
        _hose.widthMultiplier = 0.13f;
        _hose.numCapVertices = 2;
        _hose.shadowCastingMode = ShadowCastingMode.Off;
        _hose.receiveShadows = false;

        // 바스켓 — 호스 쪽이 좁고 뒤로 넓게 벌어진 원뿔 갓 + 안쪽 커플링
        var bgo = new GameObject("Drogue_" + name);
        _basket = bgo.transform;
        bgo.AddComponent<MeshFilter>().sharedMesh = BasketMesh;
        var br = bgo.AddComponent<MeshRenderer>();
        br.sharedMaterial = s_basketMat;
        br.shadowCastingMode = ShadowCastingMode.Off;
        _basket.SetPositionAndRotation(RestBasketWorld, transform.rotation);
    }

    static Mesh s_basketMesh;
    static Mesh BasketMesh
    {
        get
        {
            if (s_basketMesh != null) return s_basketMesh;
            // 원뿔대 두 겹: 커플링(좁은 원통) + 갓(벌어지는 깔때기). +Z 가 급유기 쪽
            const int N = 20;
            // 실물 비율 — 바스켓 입구 지름 약 0.8m, 커플링 포함 길이 약 0.9m
            var rings = new (float z, float r)[] { (0.42f, 0.08f), (0.18f, 0.10f), (0.12f, 0.15f), (-0.42f, 0.40f), (-0.46f, 0.40f) };
            var v = new List<Vector3>(); var t = new List<int>(); var nrm = new List<Vector3>();
            for (int k = 0; k < rings.Length; k++)
                for (int i = 0; i <= N; i++)
                {
                    float a = i / (float)N * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(a) * rings[k].r, Mathf.Sin(a) * rings[k].r, rings[k].z));
                    nrm.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
                }
            for (int k = 0; k < rings.Length - 1; k++)
                for (int i = 0; i < N; i++)
                {
                    int a = k * (N + 1) + i, b = a + 1, c = a + N + 1, d = c + 1;
                    t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
                }
            s_basketMesh = new Mesh { name = "DrogueBasket", hideFlags = HideFlags.DontSave };
            s_basketMesh.SetVertices(v); s_basketMesh.SetNormals(nrm); s_basketMesh.SetTriangles(t, 0);
            s_basketMesh.RecalculateBounds();
            return s_basketMesh;
        }
    }

    /// <summary>수급기 프로브가 바스켓을 물었다 (null = 분리)</summary>
    public void Attach(RefuelProbe probe) => Attached = probe;

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || _basket == null) return;
        Vector3 v = (transform.position - _lastPos) / dt;
        if (v.sqrMagnitude < 600f * 600f) Velocity = Vector3.Lerp(Velocity, v, 1f - Mathf.Exp(-dt * 4f));
        _lastPos = transform.position;

        // 다른 클라이언트의 수급기가 접속 중이면 그 프로브에 붙인다
        if (Attached == null || !Attached.isActiveAndEnabled)
        {
            Attached = null;
            foreach (var p in RefuelProbe.All)
            {
                if (p == null || p.Owner == null || p.Owner.isLocalPlayer) continue;
                if (!p.Owner.FxRefuelContact) continue;
                if ((p.TipWorld - RestBasketWorld).sqrMagnitude < 9f * 9f) { Attached = p; break; }
            }
        }

        Vector3 exit = transform.TransformPoint(HoseExit);
        Vector3 target;
        if (Attached != null)
        {
            target = Attached.TipWorld + Attached.transform.forward * 0.05f;   // 프로브 끝이 커플링에 물림
            _basket.position = target;
        }
        else
        {
            // 기류 속 흔들림 — 느린 요동(바스켓 항력 변화) + 잔떨림
            float t = Time.time + _seed;
            Vector3 wob = new Vector3(Mathf.PerlinNoise(t * 0.35f, 1.7f) - 0.5f, Mathf.PerlinNoise(t * 0.3f, 7.3f) - 0.5f, 0f) * 0.9f
                        + new Vector3(Mathf.PerlinNoise(t * 3f, 3.1f) - 0.5f, Mathf.PerlinNoise(t * 3f, 9.9f) - 0.5f, 0f) * 0.08f;
            target = transform.TransformPoint(BasketRest + wob);
            // 급유기가 선회하면 관성으로 바깥쪽으로 늦게 따라온다
            Vector3 desiredVel = (target - _basket.position) / dt;
            _basketVel = Vector3.Lerp(_basketVel, desiredVel, 1f - Mathf.Exp(-dt * 6f));
            _basket.position += _basketVel * dt;
            if ((_basket.position - target).sqrMagnitude > 25f * 25f) { _basket.position = target; _basketVel = Velocity; }
        }
        // 바스켓은 호스 방향을 따라 눕는다 (+Z = 호스 쪽)
        Vector3 toHose = exit - _basket.position;
        if (toHose.sqrMagnitude > 0.01f) _basket.rotation = Quaternion.LookRotation(toHose.normalized, transform.up);

        // 호스 — 출구에서 뒤로 뻗었다가 바스켓으로 처지는 2차 베지어. 밀려 들어오면(접속) 더 처진다
        Vector3 b = _basket.position + _basket.forward * 0.42f;
        float len = Vector3.Distance(exit, b);
        // 접속해 밀려 들어온 길이는 호스 릴이 감아 들여 장력을 유지한다 — 크게 처지지 않는다
        float slack = Mathf.Clamp(17f - len, 0f, 8f);
        Vector3 ctrl = Vector3.Lerp(exit, b, 0.45f) - transform.up * (0.6f + slack * 0.06f);
        for (int i = 0; i < HosePoints; i++)
        {
            float u = i / (float)(HosePoints - 1);
            Vector3 p = (1 - u) * (1 - u) * exit + 2 * (1 - u) * u * ctrl + u * u * b;
            _hose.SetPosition(i, p);
        }

        // 포드 표시등 — 접속 전 황색(준비), 연료 이송 중 녹색
        bool flowing = Attached != null && (Flowing || !Attached.Owner.isLocalPlayer);
        _amber.Set(new Color(1f, 0.62f, 0.12f), flowing ? 0f : 2.6f);
        _green.Set(new Color(0.2f, 1f, 0.35f), flowing ? 2.8f : 0f);
    }
}

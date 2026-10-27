using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 익수자 구조 (Man Overboard). 지상 캐릭터가 갑판 밖으로 떨어져 바다에 빠지면:
///   입수 물보라 → 입영(머리만 물 위, 파도를 타고 해류에 떠내려감) → 구조정 출동(시야 밖에서 활주 접근)
///   → 우현 옆에 정지 → 승조원이 줄 달린 구명환을 던짐 → 익수자가 잡으면 뱃전으로 끌어당김 → 암전 → 갑판 복귀.
/// GameModeManager.Init 에서 부착된다. 연출은 로컬 전용 (원격 플레이어는 캐릭터 이동만 본다).
/// </summary>
public class ManOverboardRescue : MonoBehaviour
{
    [Tooltip("입수 후 구조정이 출발하기까지 (초)")]
    public float launchDelay = 2.5f;
    [Tooltip("구조정 출발 거리 (m)")]
    public float boatStartDistance = 340f;
    [Tooltip("이 시간 안에 구조가 끝나지 않으면 강제로 갑판 복귀 (초)")]
    public float timeout = 150f;

    bool _active;
    Image _fade;

    void OnEnable()  => GroundController.EnteredWater += OnEnteredWater;
    void OnDisable() => GroundController.EnteredWater -= OnEnteredWater;

    void OnEnteredWater(GroundController gc, Vector3 pos, float impactSpeed)
    {
        var gm = GameModeManager.Instance;
        if (_active || gm == null || gm.IsFlying || gm.IsBoardedInCockpit) return;
        StartCoroutine(Rescue(gc, pos, impactSpeed));
    }

    IEnumerator Rescue(GroundController gc, Vector3 entry, float impactSpeed)
    {
        _active = true;
        var ocean = OceanSurface.Instance;
        // 항모 좌표계에서 떨어져 나와 물에 뜬다
        gc.transform.SetParent(null, true);
        gc.BeginSwim();
        gc.Drift = ocean != null ? ocean.WaterVelocity : Vector3.zero;

        WaterFx.Splash(entry, Mathf.Clamp01(impactSpeed / 14f));
        FindAnyObjectByType<FlightCamera>()?.TriggerShake(0.25f, 0.5f);
        CombatFeedUI.Instance?.ShowNotice("MAN OVERBOARD — 구조정 출동", warn: true);

        float t0 = Time.time;
        yield return new WaitForSeconds(launchDelay);

        // 구조정: 항모 반대쪽 먼 바다에서 출발해 익수자에게 곧장 — 항모 선체를 가로지르지 않도록
        Vector3 swimmer = gc.transform.position;
        var carrier = ArrestingWireSystem.Instance != null ? ArrestingWireSystem.Instance.transform.parent : null;
        Vector3 away = carrier != null ? swimmer - carrier.position : Vector3.right;
        away.y = 0f;
        if (away.sqrMagnitude < 1f) away = Vector3.right;
        away = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * away.normalized;
        Vector3 start = swimmer + away * boatStartDistance;
        var boat = RescueBoat.Spawn(start, swimmer - start);
        boat.SetApproachDirection(swimmer - start);

        LifeRing ring = null;
        bool grabbed = false;
        while (Time.time - t0 < timeout)
        {
            swimmer = gc.transform.position;
            boat.SetTarget(swimmer);
            gc.Drift = ocean != null ? ocean.WaterVelocity : Vector3.zero;

            if (boat.Arrived && ring == null)
            {
                // 익수자와 보트 사이, 익수자 1.2m 앞에 떨어지도록 던진다 (머리 위로 떨어뜨리지 않는 것이 구조 수칙)
                Vector3 toBoat = boat.transform.position - swimmer; toBoat.y = 0f;
                Vector3 landing = swimmer + toBoat.normalized * 1.2f;
                ring = LifeRing.Throw(boat.ThrowPoint, landing);
            }

            if (ring != null && ring.Landed && !grabbed)
            {
                // 구명환으로 헤엄쳐 간다
                gc.SwimTarget = ring.transform.position;
                gc.SwimTargetSpeed = 1.3f;
                Vector3 d = ring.transform.position - swimmer; d.y = 0f;
                if (d.magnitude < 0.7f)
                {
                    grabbed = true;
                    ring.AttachTo(gc.transform);
                    CombatFeedUI.Instance?.ShowNotice("구명환 확보 — 구조정으로 이동");
                }
            }

            if (grabbed)
            {
                // 승조원이 줄을 당겨 뱃전으로 끌어온다
                gc.SwimTarget = boat.BoardingPoint;
                gc.SwimTargetSpeed = 1.5f;
                Vector3 d = boat.BoardingPoint - swimmer; d.y = 0f;
                if (d.magnitude < 1.0f) break;
            }
            yield return null;
        }

        // 암전 → 갑판 복귀
        yield return Fade(1f, 0.7f);
        if (ring != null) Destroy(ring.gameObject);
        Destroy(boat.gameObject);
        GameModeManager.Instance?.RescueCharacterToDeck();
        CombatFeedUI.Instance?.ShowNotice("구조 완료 — 갑판 복귀");
        yield return new WaitForSeconds(0.3f);
        yield return Fade(0f, 0.8f);
        _active = false;
    }

    IEnumerator Fade(float to, float dur)
    {
        if (_fade == null)
        {
            var go = new GameObject("RescueFade");
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var img = new GameObject("Black").AddComponent<Image>();
            img.transform.SetParent(go.transform, false);
            var rt = img.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = false;
            _fade = img;
        }
        float from = _fade.color.a;
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            _fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / dur)));
            yield return null;
        }
        _fade.color = new Color(0f, 0f, 0f, to);
    }
}

/// <summary>줄 달린 구명환: 손에서 포물선으로 날아가 착수하고, 파도를 타며 해류에 떠내려간다.</summary>
public class LifeRing : MonoBehaviour
{
    public bool Landed { get; private set; }

    Transform _anchor;          // 줄이 매인 보트 쪽 지점
    Transform _holder;          // 잡은 익수자
    LineRenderer _rope;
    Vector3 _p0, _p1;
    float _t, _flight;
    float _spin;

    public static LifeRing Throw(Transform from, Vector3 landing)
    {
        var go = new GameObject("LifeRing");
        var r = go.AddComponent<LifeRing>();
        r._anchor = from;
        r._p0 = from.position;
        var ocean = OceanSurface.Instance;
        landing.y = ocean != null ? ocean.SampleHeight(landing.x, landing.z) : landing.y;
        r._p1 = landing;
        r._flight = Mathf.Clamp(Vector3.Distance(r._p0, landing) / 6f, 0.7f, 1.4f);
        go.transform.position = r._p0;
        r.BuildVisual();
        r.BuildRope();
        return r;
    }

    public void AttachTo(Transform swimmer) => _holder = swimmer;

    void Update()
    {
        var ocean = OceanSurface.Instance;
        float dt = Time.deltaTime;
        if (!Landed)
        {
            // 포물선: 수평 등속 + 수직 v0·t − ½gt² (도착 시각 고정). 던진 손·겨눈 수면 모두 물과 함께 떠내려가므로
            // 궤적 전체를 해류만큼 옮긴다 (안 그러면 착수점이 익수자보다 해류 × 비행시간만큼 상류에 떨어진다)
            _t += dt;
            if (ocean != null) { Vector3 w = ocean.WaterVelocity * dt; _p0 += w; _p1 += w; }
            float u = Mathf.Clamp01(_t / _flight);
            Vector3 p = Vector3.Lerp(_p0, _p1, u);
            float T = _flight, g = 9.81f;
            float vy0 = (_p1.y - _p0.y + 0.5f * g * T * T) / T;
            p.y = _p0.y + vy0 * (u * T) - 0.5f * g * (u * T) * (u * T);
            transform.position = p;
            _spin += 540f * dt;
            transform.rotation = Quaternion.Euler(70f, _spin, 15f);
            if (u >= 1f)
            {
                Landed = true;
                WaterFx.Splash(transform.position, 0.25f);
            }
        }
        else if (_holder != null)
        {
            // 익수자가 가슴에 끼고 수면에 떠 있다
            Vector3 p = _holder.position + _holder.forward * 0.15f;
            p.y = (ocean != null ? ocean.SampleHeight(p.x, p.z) : p.y) + 0.03f;
            transform.position = p;
            transform.rotation = Quaternion.Euler(8f, _holder.eulerAngles.y, 0f);
        }
        else if (ocean != null)
        {
            // 수면 위에 떠서 파도를 타고, 물과 함께 떠내려간다
            Vector3 p = transform.position + ocean.WaterVelocity * dt;
            float h = ocean.SampleHeight(p.x, p.z);
            float hx = ocean.SampleHeight(p.x + 0.4f, p.z), hz = ocean.SampleHeight(p.x, p.z + 0.4f);
            p.y = h + 0.02f + Mathf.Sin(Time.time * 2.4f) * 0.02f;
            transform.position = p;
            Vector3 n = new Vector3(-(hx - h) / 0.4f, 1f, -(hz - h) / 0.4f).normalized;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0f, _spin, 0f);
        }
        UpdateRope();
    }

    // 줄: 두 점 사이 처짐(현수선 근사)
    void UpdateRope()
    {
        if (_rope == null || _anchor == null) return;
        Vector3 a = _anchor.position, b = transform.position;
        float sag = Mathf.Clamp(Vector3.Distance(a, b) * 0.12f, 0.1f, 1.5f) * (Landed ? 1f : 0.3f);
        int n = _rope.positionCount;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);
            Vector3 p = Vector3.Lerp(a, b, t);
            p.y -= Mathf.Sin(t * Mathf.PI) * sag;
            var ocean = OceanSurface.Instance;
            if (ocean != null) p.y = Mathf.Max(p.y, ocean.SampleHeight(p.x, p.z) + 0.01f);   // 물 위에 걸쳐 뜬다
            _rope.SetPosition(i, p);
        }
    }

    void BuildRope()
    {
        _rope = gameObject.AddComponent<LineRenderer>();
        _rope.positionCount = 16;
        _rope.startWidth = _rope.endWidth = 0.025f;
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.93f, 0.85f, 0.55f) };
        _rope.sharedMaterial = m;
        _rope.useWorldSpace = true;
        _rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // 항모 모델에 있는 구명환 메쉬를 그대로 복제 (없으면 주황·흰 띠 토러스)
    void BuildVisual()
    {
        var carrier = ArrestingWireSystem.Instance != null ? ArrestingWireSystem.Instance.transform.parent : null;
        if (carrier != null)
        {
            foreach (var mr in carrier.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.sharedMaterial == null || !mr.sharedMaterial.name.ToLower().Contains("life_buoy")) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) * mr.transform.lossyScale.x;
                if (size < 0.3f || size > 2f) continue;   // 구명환 하나 크기(≈0.75m)인 메쉬만
                var vis = new GameObject("Ring");
                vis.transform.SetParent(transform, false);
                vis.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                vis.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
                vis.transform.localScale = mr.transform.lossyScale;
                // 메쉬 중심을 원점으로, 고리 면이 수평이 되도록 가장 얇은 축을 위로
                Vector3 s = b.size;
                Quaternion lay = s.y <= s.x && s.y <= s.z ? Quaternion.identity
                               : s.z <= s.x ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.Euler(0f, 0f, 90f);
                vis.transform.localRotation = lay;
                vis.transform.localPosition = -(lay * Vector3.Scale(b.center, vis.transform.localScale));
                return;
            }
        }
        BuildTorus();
    }

    void BuildTorus()
    {
        const int Seg = 32, Sides = 10; const float R = 0.36f, r = 0.085f;
        var v = new Vector3[(Seg + 1) * (Sides + 1)]; var c = new Color[v.Length]; var t = new int[Seg * Sides * 6];
        for (int i = 0; i <= Seg; i++)
        {
            float a = i / (float)Seg * Mathf.PI * 2f;
            bool white = (i * 4 / Seg) % 2 == 1;   // 네 구간 흰 띠 (반사 테이프)
            for (int j = 0; j <= Sides; j++)
            {
                float b = j / (float)Sides * Mathf.PI * 2f;
                float rr = R + r * Mathf.Cos(b);
                v[i * (Sides + 1) + j] = new Vector3(Mathf.Cos(a) * rr, r * Mathf.Sin(b), Mathf.Sin(a) * rr);
                c[i * (Sides + 1) + j] = white ? new Color(0.95f, 0.95f, 0.93f) : new Color(0.95f, 0.38f, 0.05f);
            }
        }
        int n = 0;
        for (int i = 0; i < Seg; i++)
            for (int j = 0; j < Sides; j++)
            {
                int a = i * (Sides + 1) + j, b = a + Sides + 1;
                t[n++] = a; t[n++] = a + 1; t[n++] = b; t[n++] = b; t[n++] = a + 1; t[n++] = b + 1;
            }
        var m = new Mesh { name = "LifeRingTorus", vertices = v, colors = c, triangles = t };
        m.RecalculateNormals();
        var go = new GameObject("Ring");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit"));
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }
}

/// <summary>수면 물보라: 입수·구명환 착수 때 위로 솟는 물방울 기둥 + 퍼지는 물안개 + 수면에 남는 거품 고리.</summary>
public static class WaterFx
{
    public static void Splash(Vector3 pos, float strength)
    {
        var mat = (Resources.Load<Material>("VFX/Mat_WaterSpray") ?? Resources.Load<Material>("VFX/Mat_Alpha"));
        var root = new GameObject("Splash");
        root.transform.position = pos;
        Object.Destroy(root, 4f);
        strength = Mathf.Clamp01(strength);

        // 물방울 기둥
        var drops = Emitter(root.transform, "Drops", mat);
        var m = drops.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(2f + 3f * strength, 5f + 6f * strength);
        m.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.3f);
        m.startColor = new Color(0.95f, 0.97f, 1f, 0.85f);
        m.gravityModifier = 1f;
        var sh = drops.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 22f; sh.radius = 0.35f;
        sh.rotation = new Vector3(-90f, 0f, 0f);
        Burst(drops, (short)(60 + 120 * strength));

        // 물안개
        var mist = Emitter(root.transform, "Mist", mat);
        var mm = mist.main;
        mm.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
        mm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
        mm.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.8f + strength);
        mm.startColor = new Color(0.92f, 0.95f, 0.98f, 0.35f);
        var msh = mist.shape; msh.shapeType = ParticleSystemShapeType.Hemisphere; msh.radius = 0.5f;
        var sz = mist.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 1.8f)));
        Burst(mist, (short)(14 + 20 * strength));

        // 수면 거품 고리 (수평으로 깔린 납작한 입자)
        var foam = Emitter(root.transform, "FoamRing", mat);
        var fm = foam.main;
        fm.startLifetime = new ParticleSystem.MinMaxCurve(2.0f, 3.2f);
        fm.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f + strength);
        fm.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
        fm.startColor = new Color(0.96f, 0.98f, 1f, 0.6f);
        var fsh = foam.shape; fsh.shapeType = ParticleSystemShapeType.Circle; fsh.radius = 0.4f;
        fsh.rotation = new Vector3(-90f, 0f, 0f);
        var fr = foam.GetComponent<ParticleSystemRenderer>();
        fr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        Burst(foam, (short)(24 + 30 * strength));
    }

    static ParticleSystem Emitter(Transform parent, string name, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.loop = false; m.playOnAwake = false; m.duration = 0.2f;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        var e = ps.emission; e.rateOverTime = 0f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        if (mat != null) r.sharedMaterial = mat;
        return ps;
    }

    static void Burst(ParticleSystem ps, short count)
    {
        var e = ps.emission;
        e.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
        ps.Play();
    }
}

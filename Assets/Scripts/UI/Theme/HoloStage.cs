using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// 로비/출격 연출 공용 3D 스테이지.
/// 디지털 터널(네온 라인, 게이트 링, 데이터 월) 속에 KF-21 홀로그램(틴트 유리 + 정밀 격자, 홀로그램디자인_03)이
/// HUD 프로젝션 데크 위에 떠 있다. 배경엔 초점이 나간 보케 광원.
/// 숨겨진 좌표(레이어 30)의 씬 → 전용 카메라 → RenderTexture → 전체 화면 RawImage.
/// </summary>
public class HoloStage : MonoBehaviour
{
    public static readonly Vector3 Origin = new Vector3(0f, 6000f, 0f);
    public const int Layer = 30;

    // 기체 로컬 기준 노즐 위치
    public static readonly Vector3[] Nozzles = { new Vector3(-0.62f, 2.27f, -7.35f), new Vector3(0.62f, 2.27f, -7.35f) };
    public const float ZMin = -9.0f, ZMax = 8.2f;
    public const float JetScale = 1.3f;

    public Transform Jet;
    public Camera Cam;
    public RenderTexture RT;
    public RawImage Raw;
    public bool Idle = true;                // true 면 스스로 기체/카메라를 연출(로비 대기)
    public float StreakSpeed = 7f;

    Transform _mirror;
    Renderer[] _rends, _mirrorRends;
    Material[][] _litMats;
    Material _holo, _holoMirror, _neon, _glow, _flameMat;
    Light[] _lights; float[] _lightBase;
    Transform _warm; int _warmFrames;
    Transform _holoOverlay; Renderer[] _ovRends; Material _ovMat; float _holoI = 1.45f;
    Transform _pad, _scanRing, _padGlow, _underGlow, _heatGlow;
    Material _archMat;
    Material _scanMat;
    Material _heatMat;
    Material _padMat;
    Transform[] _flames; Transform[] _flameGlow;
    Transform[] _streaks; float[] _streakSpd;
    float _t;
    bool _solid;
    readonly List<Object> _owned = new List<Object>();
    static readonly Vector3 PadPos = new Vector3(4.6f, 0.03f, 30f);

    // ── 생성 ──────────────────────────────────────────────────────────────
    public static HoloStage Create(Transform parent)
    {
        var prefab = Resources.Load<GameObject>("UI/LaunchJet");
        var holoSh = Resources.Load<Shader>("UI/KF21HoloWire");
        var neonSh = Resources.Load<Shader>("UI/KF21Neon");
        if (prefab == null || holoSh == null || neonSh == null) return null;
        _glassSh = Resources.Load<Shader>("UI/KF21HoloGlass");

        var go = new GameObject("Stage", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var raw = go.AddComponent<RawImage>();
        raw.raycastTarget = false;
        var st = go.AddComponent<HoloStage>();
        st.Raw = raw;
        st.Build(prefab, holoSh, neonSh);
        return st;
    }

    Material CloneGlow(Color c, float intensity)
    {
        var m = new Material(_glow); m.SetColor("_Color", c); m.SetFloat("_Intensity", intensity); return m;
    }

    Material Track(Material m) { m.hideFlags = HideFlags.DontSave; _owned.Add(m); return m; }
    static Shader _glassSh;

    void Build(GameObject prefab, Shader holoSh, Shader neonSh)
    {
        LaunchSequence.EnsureReflection();
        var root = new GameObject("[HoloStage]");
        root.transform.position = Origin;
        _stageRoot = root;

        // 터널 네온은 한 단계 낮춰 기체가 주인공이 되게 (레퍼런스의 차분한 배경)
        _neon = Track(new Material(neonSh)); _neon.SetFloat("_Mode", 0f); _neon.SetFloat("_Intensity", 1.1f); _neon.SetFloat("_Sharp", 5f);
        _neon.SetColor("_Color", Color.white);

        _glow = Track(new Material(neonSh)); _glow.SetFloat("_Mode", 1f); _glow.SetFloat("_Intensity", 1f);
        _glow.SetColor("_Color", Color.white);
        _flameMat = Track(new Material(neonSh)); _flameMat.SetFloat("_Mode", 2f); _flameMat.SetFloat("_Intensity", 2.4f);

        BuildEnvironment(root.transform);
        BuildJet(prefab, holoSh, root.transform);
        BuildFlames();
        BuildLights(root.transform);
        BeginPrewarm(prefab);
        BuildCamera(root.transform);
        SetLayerRecursive(root, Layer);
        BuildPost(root);
        UpdateIdle(0f);
    }

    GameObject _stageRoot;

    // ── 메시 빌더 ─────────────────────────────────────────────────────────
    class MB
    {
        public List<Vector3> v = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>();
        public List<Color> c = new List<Color>(); public List<int> t = new List<int>();

        public void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color col, float v0 = 0f, float v1 = 1f)
        {
            int b = v.Count;
            v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
            uv.Add(new Vector2(0, v0)); uv.Add(new Vector2(1, v0)); uv.Add(new Vector2(1, v1)); uv.Add(new Vector2(0, v1));
            c.Add(col); c.Add(col); c.Add(col); c.Add(col);
            t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
        }

        public void Ribbon(Vector3 a, Vector3 b, float width, Vector3 normal, Color col, float refl = 0f, bool fade = true)
        {
            Vector3 d = (b - a).normalized;
            Vector3 s = Vector3.Cross(d, normal).normalized * (width * 0.5f);
            float v0 = fade ? 0f : 0.5f, v1 = fade ? 1f : 0.5f;
            Quad(a - s, a + s, b + s, b - s, col, v0, v1);
            if (refl > 0f)
            {
                var m = new Color(col.r, col.g, col.b, col.a * refl);
                Vector3 am = new Vector3(a.x, -a.y, a.z), bm = new Vector3(b.x, -b.y, b.z);
                Vector3 sm = new Vector3(s.x, -s.y, s.z);
                Quad(am - sm, am + sm, bm + sm, bm - sm, m, v0, v1);
            }
        }

        // 평면 위 원: axisU/axisV 가 이루는 평면, normal 은 리본 면 법선
        public void Ring(Vector3 center, Vector3 axisU, Vector3 axisV, float radius, float width, int seg, Color col, float refl = 0f)
        {
            Vector3 n = Vector3.Cross(axisU, axisV);
            Vector3 prev = center + axisU * radius;
            for (int i = 1; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector3 p = center + (axisU * Mathf.Cos(a) + axisV * Mathf.Sin(a)) * radius;
                Vector3 d = (p - prev);
                // 이음새 틈 방지: 약간 겹치게
                Ribbon(prev - d * 0.05f, p + d * 0.05f, width, n, col, refl, false);
                prev = p;
            }
        }

        public void Glow(Vector3 center, Vector3 right, Vector3 up, Color col)
        {
            Quad(center - right - up, center + right - up, center + right + up, center - right + up, col, 0f, 1f);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            m.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(c); m.SetTriangles(t, 0);
            m.bounds = new Bounds(new Vector3(0, 10, 80), new Vector3(200, 100, 400));
            return m;
        }
    }

    GameObject MakeMesh(Transform parent, string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        _owned.Add(mesh);
        return go;
    }

    // ── 환경: 터널 ────────────────────────────────────────────────────────
    const float W = 18f, H = 20f, Z0 = -24f, Z1 = 190f;

    void BuildEnvironment(Transform parent)
    {
        var lines = new MB();
        var glows = new MB();
        var rng = new System.Random(21);
        System.Func<float> r01 = () => (float)rng.NextDouble();
        Color cy = new Color(0.25f, 0.75f, 1f, 1f);
        Color cyDim = new Color(0.18f, 0.55f, 1f, 1f);
        Color warm = new Color(1f, 0.52f, 0.22f, 1f);
        Vector3 up = Vector3.up, ex = Vector3.right;

        // 바닥: 종선 + 횡선 (원근으로 소실점에 수렴)
        for (float x = -W; x <= W + 0.01f; x += 4.5f)
        {
            float k = 1f - Mathf.Abs(x) / W * 0.55f;
            var c = cy; c.a = 0.26f * k;
            lines.Ribbon(new Vector3(x, 0.01f, Z0), new Vector3(x, 0.01f, Z1), 0.07f, up, c, 0f, false);
        }
        // 중앙 활주 라인 (밝게) + 양옆 앰버 가장자리
        lines.Ribbon(new Vector3(0, 0.02f, Z0), new Vector3(0, 0.02f, Z1), 0.18f, up, new Color(0.5f, 0.9f, 1f, 0.28f), 0f, false);
        lines.Ribbon(new Vector3(-9.4f, 0.02f, Z0), new Vector3(-9.4f, 0.02f, Z1), 0.18f, up, new Color(warm.r, warm.g, warm.b, 0.4f), 0f, false);
        lines.Ribbon(new Vector3(9.4f, 0.02f, Z0), new Vector3(9.4f, 0.02f, Z1), 0.18f, up, new Color(warm.r, warm.g, warm.b, 0.4f), 0f, false);

        // 벽: 가로 띠만 (좌/우, 반사 포함) — 짧은 세로 데이터 바는 비처럼 보여 제거
        float[] hy = { 0.5f, 4.4f, 10.5f };
        for (int s = -1; s <= 1; s += 2)
        {
            float x = s * W;
            for (int i = 0; i < hy.Length; i++)
            {
                bool acc = i == 0;
                var c = acc ? warm : cy; c.a = acc ? 0.55f : 0.22f;
                lines.Ribbon(new Vector3(x, hy[i], Z0), new Vector3(x, hy[i], Z1), acc ? 0.16f : 0.1f, ex, c, 0.35f, false);
            }
        }
        // 천장 라인
        for (float x = -W + 3f; x <= W - 2.9f; x += 7.2f)
        {
            var c = cy; c.a = 0.2f;
            lines.Ribbon(new Vector3(x, H, Z0), new Vector3(x, H, Z1), 0.12f, up, c, 0f, false);
        }

        // 출격 게이트로 좁혀 들어가는 아치 프레임 (깔때기 느낌) — 별도 재질: 대기 중엔 어둡게, 출격 때 점등
        var arches = new MB();
        _archMat = Track(new Material(_neon));
        float[] gz = { 34f, 52f, 70f, 86f };
        float[] ga = { GA + 4.6f, GA + 3.4f, GA + 2.3f, GA + 1.3f };
        for (int i = 0; i < gz.Length; i++)
        {
            float fade = 1f - i * 0.12f;
            var pts = Arch(ga[i], GHS + 1.5f - i * 0.4f);
            PolyRibbon(arches, pts, gz[i], 0.5f, new Color(0.30f, 0.82f, 1f, 0.9f * fade), 0.4f);
            PolyRibbon(arches, Arch(ga[i] - 0.8f, GHS + 1.5f - i * 0.4f - 0.0f), gz[i], 0.1f, new Color(0.7f, 0.96f, 1f, 0.55f * fade), 0.4f);
            PolyRibbon(arches, Arch(ga[i] + 0.7f, GHS + 1.5f - i * 0.4f), gz[i], 0.24f, new Color(0.18f, 0.5f, 1f, 0.5f * fade), 0.4f);
            glows.Glow(new Vector3(0f, GateH * 0.5f, gz[i]), ex * (ga[i] * 2f), up * (GateH * 1.0f), new Color(0.12f, 0.4f, 0.9f, 0.12f * fade));
        }
        // 벽면 사선 빔 (디지털 입구 느낌의 각진 구조)
        for (int s = -1; s <= 1; s += 2)
        {
            for (int k = 0; k < 3; k++)
            {
                float z = 30f + k * 40f + r01() * 6f, len = 18f + r01() * 20f;
                float y0 = 1f + r01() * 12f, dy = (r01() < 0.5f ? 1f : -1f) * (4f + r01() * 8f);
                bool wm = k % 3 == 0;
                var c = wm ? warm : cy; c.a = (wm ? 0.35f : 0.25f) * Mathf.Clamp01(1.2f - z / Z1);
                lines.Ribbon(new Vector3(s * W, y0, z), new Vector3(s * W, Mathf.Clamp(y0 + dy, 0.5f, 18f), z + len), wm ? 0.14f : 0.09f, ex, c, 0.25f, true);
            }
        }
        // 터널 끝 광원
        glows.Glow(new Vector3(0f, 7f, Z1 - 4f), ex * 26f, up * 26f, new Color(0.4f, 0.8f, 1f, 0.28f));
        glows.Glow(new Vector3(0f, 7f, Z1 - 6f), ex * 70f, up * 60f, new Color(0.1f, 0.35f, 0.9f, 0.25f));
        // 앰비언트 포그 (뒤쪽 벽)
        glows.Glow(new Vector3(8f, 4f, 60f), ex * 40f, up * 14f, new Color(0.08f, 0.25f, 0.6f, 0.22f));

        // 보케: 초점 밖 격납고 조명 — 따뜻한 앰버 + 차가운 시안 원반이 깊이에 따라 크기/밝기 차이
        for (int i = 0; i < 46; i++)
        {
            float kind = r01();
            bool warmB = kind < 0.62f, whiteB = kind > 0.88f;
            float z = 80f + r01() * 95f;   // 출격 연출 카메라 궤도(z ≤ 55)보다 충분히 뒤 — 가까운 원반이 화면을 덮지 않게
            float x = (r01() * 2f - 1f) * (W + 14f);
            float y = 4f + r01() * 20f;
            float sz = 2.2f + r01() * 4.5f + z * 0.02f;
            var bc = whiteB ? new Color(0.95f, 0.92f, 0.85f, 1f) : warmB ? new Color(1f, 0.56f, 0.24f, 1f) : new Color(0.45f, 0.78f, 1f, 1f);
            bc.a = (0.18f + r01() * 0.3f) * Mathf.Clamp01(1.3f - z / 190f);
            glows.Glow(new Vector3(x, y, z), ex * sz, up * sz, bc);
        }

        MakeMesh(parent, "Lines", lines.ToMesh("EnvLines"), _neon);
        MakeMesh(parent, "Arches", arches.ToMesh("EnvArches"), _archMat);
        BuildPortal(parent);
        MakeMesh(parent, "Glows", glows.ToMesh("EnvGlows"), _glow);

        // 홀로 패드 (기체 아래)
        var pad = new GameObject("Pad"); pad.transform.SetParent(parent, false); pad.transform.localPosition = PadPos; _pad = pad.transform;
        var pm = new MB();
        pm.Ring(Vector3.zero, ex, Vector3.forward, 7.8f, 0.26f, 180, new Color(0.45f, 0.9f, 1f, 0.85f));
        pm.Ring(Vector3.zero, ex, Vector3.forward, 6.6f, 0.16f, 180, new Color(0.25f, 0.75f, 1f, 0.55f));
        pm.Ring(Vector3.zero, ex, Vector3.forward, 4.2f, 0.14f, 144, new Color(0.25f, 0.75f, 1f, 0.38f));
        for (int k = 0; k < 4; k++)
        {
            float a = k * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            Vector3 d = ex * Mathf.Cos(a) + Vector3.forward * Mathf.Sin(a);
            pm.Ribbon(d * 4.4f, d * 6.4f, 0.16f, up, new Color(0.35f, 0.85f, 1f, 0.7f), 0f, false);
        }
        MakeMesh(pad.transform, "PadLines", pm.ToMesh("PadLines"), _neon);
        BuildDeck(parent);
        var pg = new MB();
        pg.Glow(Vector3.zero + up * 0.02f, Vector3.zero, Vector3.zero, Color.clear);   // 자리표시(사용 안 함)
        var disc = new MB();
        disc.Quad(new Vector3(-11, 0, -11), new Vector3(11, 0, -11), new Vector3(11, 0, 11), new Vector3(-11, 0, 11), new Color(0.15f, 0.5f, 1f, 0.55f));
        var dgo = MakeMesh(parent, "PadGlow", disc.ToMesh("PadGlow"), _glow); _padGlow = dgo.transform;
        dgo.transform.localPosition = PadPos + up * 0.015f;

        // 지면 밀착 글로우: 기체 아래(시안) + 노즐 뒤(앰버, 점화 시)
        {
            var q = new MB();
            q.Quad(new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1), Color.white);
            var qm = q.ToMesh("GroundGlow");
            var ug = MakeMesh(parent, "UnderGlow", qm, Track(CloneGlow(new Color(0.25f, 0.6f, 1f), 0.9f)));
            ug.transform.localScale = new Vector3(15f, 1f, 11f); ug.SetActive(false); _underGlow = ug.transform;
            _heatMat = Track(CloneGlow(new Color(1f, 0.55f, 0.2f), 0f));
            var hg = MakeMesh(parent, "HeatGlow", qm, _heatMat);
            hg.transform.localScale = new Vector3(3.2f, 1f, 6f); hg.SetActive(false); _heatGlow = hg.transform;
        }

        // 스캔 링: 굵은 단일 띠 대신 — 아주 가는 주 링 + 안쪽 보조 링 + 바깥 눈금 + 옅은 광륜 띠.
        // 전용 재질로 높이에 따라 밝기를 페이드한다 (UpdateIdle).
        var sr = new GameObject("ScanRing"); sr.transform.SetParent(parent, false); _scanRing = sr.transform;
        var sm = new MB();
        const float SR = 6.4f;
        sm.Ring(Vector3.zero, ex, Vector3.forward, SR, 0.12f, 180, new Color(0.85f, 1f, 1f, 0.9f));            // 주 링
        sm.Ring(Vector3.zero, ex, Vector3.forward, SR - 0.36f, 0.08f, 180, new Color(0.4f, 0.85f, 1f, 0.45f));  // 보조 링
        sm.Ring(Vector3.zero, ex, Vector3.forward, SR - 0.12f, 0.55f, 120, new Color(0.15f, 0.55f, 1f, 0.18f)); // 광륜
        // 4방위 앰버 마커
        for (int k = 0; k < 4; k++)
        {
            float a = k * Mathf.PI * 0.5f;
            Vector3 d = ex * Mathf.Cos(a) + Vector3.forward * Mathf.Sin(a);
            Vector3 tng = Vector3.Cross(up, d);
            sm.Ribbon(d * (SR + 0.05f) - tng * 0.35f, d * (SR + 0.05f) + tng * 0.35f, 0.06f, up, new Color(1f, 0.6f, 0.28f, 0.95f), 0f, false);
        }
        _scanMat = Track(new Material(_neon));
        MakeMesh(sr.transform, "ScanLine", sm.ToMesh("Scan"), _scanMat);

        // 흐르는 빛줄기
        int N = 22;
        _streaks = new Transform[N]; _streakSpd = new float[N];
        var one = new MB();
        one.Ribbon(new Vector3(0, 0, -3f), new Vector3(0, 0, 3f), 0.06f, up, new Color(0.7f, 0.95f, 1f, 1f), 0f, true);
        var streakMesh = one.ToMesh("Streak");
        for (int i = 0; i < N; i++)
        {
            var g = MakeMesh(parent, "Streak" + i, streakMesh, _neon);
            const bool floor = false;   // 바닥 빛줄기는 루프 순간 튀어 보여 쓰지 않는다
            float x = (r01() * 2f - 1f) * (W - 1f);
            float y = floor ? 0.05f : 0.4f + r01() * 15f;
            if (!floor) x = (r01() < 0.5f ? -1f : 1f) * (W - 0.1f);
            float len = 2f + r01() * 6f;
            g.transform.localPosition = new Vector3(x, y, Z0 + r01() * (Z1 - Z0));
            g.transform.localScale = new Vector3(1f, 1f, len / 3f);
            if (!floor) g.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);   // 벽 쪽: 폭 방향이 수평
            else g.transform.localRotation = Quaternion.identity;
            _streaks[i] = g.transform; _streakSpd[i] = 0.6f + r01() * 1.2f;
        }
    }

    // ── HUD 프로젝션 데크: 기체 아래 바닥에 깔린 정밀 그리드 패널 + 오렌지 데이터 행 (홀로그램디자인_03) ──
    void BuildDeck(Transform parent)
    {
        var d = new MB();
        var rng = new System.Random(5);
        System.Func<float> r01 = () => (float)rng.NextDouble();
        Vector3 up = Vector3.up;
        Color line = new Color(0.35f, 0.8f, 1f, 0.55f), faint = new Color(0.3f, 0.7f, 1f, 0.22f);
        Color amber = new Color(1f, 0.55f, 0.22f, 0.9f);
        float y = 0.02f;
        System.Action<float, float, float, float, Color, float> rect = (x0, z0, x1, z1, c, w) =>
        {
            d.Ribbon(new Vector3(x0, y, z0), new Vector3(x1, y, z0), w, up, c, 0f, false);
            d.Ribbon(new Vector3(x1, y, z0), new Vector3(x1, y, z1), w, up, c, 0f, false);
            d.Ribbon(new Vector3(x1, y, z1), new Vector3(x0, y, z1), w, up, c, 0f, false);
            d.Ribbon(new Vector3(x0, y, z1), new Vector3(x0, y, z0), w, up, c, 0f, false);
        };

        // 큰 데크 외곽 + 보조 격자
        rect(-13f, -12f, 13f, 12f, line, 0.14f);
        // (보조 격자·데이터 행·눈금 레일·조명 점은 카메라 이동 시 덜덜거려 제거 — 외곽선만 유지)
        var deck = MakeMesh(parent, "Deck", d.ToMesh("Deck"), _neon);
        deck.transform.localPosition = new Vector3(PadPos.x, 0f, PadPos.z);
        _deck = deck.transform;
    }
    Transform _deck;

    // ── 디지털 입구: 벽에 뚫린 아치형 출격 게이트. 중앙 이음선에서 두 도어 리프가 위/아래로 갈라져 벽 뒤로 사라지며 열린다.
    //    열리면 안쪽 아치 복도(깊이 링 + 측면 라인)와 빛의 막이 드러나고, 바닥엔 진입 유도등이 순차 점멸한다 ──
    public const float GateZ = 100f;
    const float GA = 6.8f, GHS = 9.0f;      // 반폭, 직선부 높이(+반원 높이 = GA)
    const int GuideRows = 13;
    static float GateH { get { return GHS + GA; } }
    public const float GateGroundY = 0.48f;                // 기체 바퀴가 바닥에 닿는 원점 높이(스케일 1.3 반영)

    Transform _portal, _leafTop, _leafBot, _curtain, _portalCore;
    Material _doorMat, _portalCoreMat, _curtainMat, _guideMat;
    Material[] _layerMats;
    Transform[] _tStreaks; float[] _tSpd, _tLen; Material _tunnelMat; float _tunnelSpeed;
    const float TunnelLen = 200f;
    Renderer[] _guideRends;
    MaterialPropertyBlock _mpb;
    float _open, _energy;
    float _guidePhase;
    const int Layers = 12;

    static System.Collections.Generic.List<Vector2> Arch(float a, float hs, int arcSeg = 28)
    {
        var l = new System.Collections.Generic.List<Vector2>();
        l.Add(new Vector2(a, 0f)); l.Add(new Vector2(a, hs));
        for (int i = 1; i < arcSeg; i++) { float t = Mathf.PI * i / arcSeg; l.Add(new Vector2(Mathf.Cos(t) * a, hs + Mathf.Sin(t) * a)); }
        l.Add(new Vector2(-a, hs)); l.Add(new Vector2(-a, 0f));
        return l;
    }

    static void PolyRibbon(MB mb, System.Collections.Generic.List<Vector2> pts, float z, float width, Color col, float refl = 0f)
    {
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            Vector3 p = new Vector3(pts[i].x, pts[i].y, z), q = new Vector3(pts[i + 1].x, pts[i + 1].y, z);
            Vector3 d = q - p;
            mb.Ribbon(p - d * 0.04f, q + d * 0.04f, width, Vector3.forward, col, refl, false);
        }
    }

    // c 에서 p 방향으로 사각형 경계까지의 점
    static Vector2 RayToRect(Vector2 c, Vector2 p, float xl, float yl, float yh)
    {
        Vector2 d = p - c; float k = 1000f;
        if (d.x > 1e-4f) k = Mathf.Min(k, (xl - c.x) / d.x); else if (d.x < -1e-4f) k = Mathf.Min(k, (-xl - c.x) / d.x);
        if (d.y > 1e-4f) k = Mathf.Min(k, (yh - c.y) / d.y); else if (d.y < -1e-4f) k = Mathf.Min(k, (yl - c.y) / d.y);
        return c + d * Mathf.Max(1f, k);
    }

    Mesh FanMesh(string name, System.Collections.Generic.List<Vector2> loop, Vector2 center, Color cCenter, Color cEdge, float z)
    {
        var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
        var vs = new System.Collections.Generic.List<Vector3>(); var cs = new System.Collections.Generic.List<Color>(); var uv = new System.Collections.Generic.List<Vector2>(); var ts = new System.Collections.Generic.List<int>();
        vs.Add(new Vector3(center.x, center.y, z)); cs.Add(cCenter); uv.Add(new Vector2(0.5f, center.y / GateH));
        foreach (var p in loop) { vs.Add(new Vector3(p.x, p.y, z)); cs.Add(cEdge); uv.Add(new Vector2(p.x / GA * 0.5f + 0.5f, p.y / GateH)); }
        for (int i = 1; i < loop.Count; i++) { ts.Add(0); ts.Add(i); ts.Add(i + 1); }
        ts.Add(0); ts.Add(loop.Count); ts.Add(1);   // 닫는 삼각형 (마지막 점 → 첫 점)
        m.SetVertices(vs); m.SetColors(cs); m.SetUVs(0, uv); m.SetTriangles(ts, 0);
        m.bounds = new Bounds(new Vector3(0, GateH * 0.5f, z), new Vector3(60, 60, 20));
        _owned.Add(m);
        return m;
    }

    void BuildPortal(Transform parent)
    {
        var solidSh = Resources.Load<Shader>("UI/KF21Solid");
        var root = new GameObject("Gate"); root.transform.SetParent(parent, false);
        root.transform.localPosition = new Vector3(0f, 0f, GateZ);
        _portal = root.transform;
        Vector3 up = Vector3.up;
        _doorMat = Track(new Material(solidSh));
        Vector2 ctr = new Vector2(0f, GateH * 0.5f);
        var arch = Arch(GA, GHS);

        // ── 벽: 터널 단면을 막는 불투명 벽 + 아치 구멍 (뒤쪽 라인을 가린다) ──
        {
            const float XL = 22f, YL = -20f, YH = 40f;
            var m = new Mesh { name = "GateWall", hideFlags = HideFlags.DontSave };
            var vs = new System.Collections.Generic.List<Vector3>(); var cs = new System.Collections.Generic.List<Color>(); var ts = new System.Collections.Generic.List<int>();
            Color cIn = new Color(0.008f, 0.02f, 0.04f), cOut = new Color(0.0008f, 0.0016f, 0.004f);
            for (int i = 0; i < arch.Count; i++)
            {
                Vector2 p = arch[i]; Vector2 q = RayToRect(ctr, p, XL, YL, YH);
                vs.Add(new Vector3(p.x, p.y, 0)); cs.Add(cIn); vs.Add(new Vector3(q.x, q.y, 0)); cs.Add(cOut);
            }
            for (int i = 0; i + 1 < arch.Count; i++) { int b = i * 2; ts.Add(b); ts.Add(b + 1); ts.Add(b + 3); ts.Add(b); ts.Add(b + 3); ts.Add(b + 2); }
            // 위쪽 모서리 보강 + 바닥 아래(구멍 아래) 영역
            int ci = vs.Count;
            vs.Add(new Vector3(0, 0, 0)); cs.Add(cIn);
            Vector2 qf = RayToRect(ctr, arch[0], XL, YL, YH), ql = RayToRect(ctr, arch[arch.Count - 1], XL, YL, YH);
            vs.Add(new Vector3(arch[0].x, 0, 0)); cs.Add(cIn); vs.Add(new Vector3(qf.x, qf.y, 0)); cs.Add(cOut);
            vs.Add(new Vector3(XL, YL, 0)); cs.Add(cOut); vs.Add(new Vector3(-XL, YL, 0)); cs.Add(cOut);
            vs.Add(new Vector3(ql.x, ql.y, 0)); cs.Add(cOut); vs.Add(new Vector3(arch[arch.Count - 1].x, 0, 0)); cs.Add(cIn);
            for (int i = 1; i < 6; i++) { ts.Add(ci); ts.Add(ci + i); ts.Add(ci + i + 1); }
            // 상단 모서리(아치 위쪽 사각 모서리 두 곳)
            int c2 = vs.Count;
            vs.Add(new Vector3(XL, YH, 0)); cs.Add(cOut); vs.Add(new Vector3(-XL, YH, 0)); cs.Add(cOut);
            Vector2 top = arch[arch.Count / 2]; Vector2 qt = RayToRect(ctr, top, XL, YL, YH);
            vs.Add(new Vector3(qt.x, qt.y, 0)); cs.Add(cOut);
            ts.Add(c2); ts.Add(c2 + 1); ts.Add(c2 + 2);
            m.SetVertices(vs); m.SetColors(cs); m.SetTriangles(ts, 0);
            m.bounds = new Bounds(new Vector3(0, 10, 0), new Vector3(60, 80, 4));
            _owned.Add(m);
            MakeMesh(root.transform, "Wall", m, _doorMat);
        }

        // ── 벽 장식/프레임 네온 ──
        {
            var fr = new MB(); float zf = -0.12f;
            PolyRibbon(fr, Arch(GA + 0.12f, GHS), zf, 0.22f, new Color(0.75f, 0.98f, 1f, 0.95f), 0.4f);
            PolyRibbon(fr, Arch(GA + 0.75f, GHS), zf, 0.5f, new Color(0.30f, 0.82f, 1f, 0.9f), 0.4f);
            PolyRibbon(fr, Arch(GA + 1.5f, GHS), zf, 0.1f, new Color(0.75f, 0.97f, 1f, 0.5f), 0.4f);
            PolyRibbon(fr, Arch(GA + 2.6f, GHS), zf, 0.3f, new Color(0.2f, 0.55f, 1f, 0.55f), 0.4f);
            PolyRibbon(fr, Arch(GA + 4.0f, GHS), zf, 0.08f, new Color(0.4f, 0.8f, 1f, 0.3f), 0.4f);
            // 방사 살(벽 패널 이음선)
            var o1 = Arch(GA + 2.7f, GHS, 20); var o2 = Arch(GA + 7.5f, GHS, 20);
            for (int i = 1; i < o1.Count - 1; i++)
                fr.Ribbon(new Vector3(o1[i].x, o1[i].y, zf), new Vector3(o2[i].x, o2[i].y, zf), 0.06f, Vector3.forward, new Color(0.45f, 0.85f, 1f, 0.32f), 0.2f, true);
            // 눈금
            var t1 = Arch(GA + 1.75f, GHS, 36); var t2 = Arch(GA + 2.1f, GHS, 36);
            for (int i = 1; i < t1.Count - 1; i++)
                fr.Ribbon(new Vector3(t1[i].x, t1[i].y, zf), new Vector3(t2[i].x, t2[i].y, zf), 0.07f, Vector3.forward, new Color(0.85f, 0.98f, 1f, i % 3 == 0 ? 0.9f : 0.45f), 0.2f, false);
            // 상단 마커 (게이트 식별용 3 칸 + 앰버 중앙)
            float yTop = GateH + GA * 0.0f + 5.2f;
            fr.Ribbon(new Vector3(-3.2f, yTop, zf), new Vector3(3.2f, yTop, zf), 0.12f, Vector3.forward, new Color(0.5f, 0.9f, 1f, 0.8f), 0.2f, false);
            fr.Ribbon(new Vector3(-0.8f, yTop, zf), new Vector3(0.8f, yTop, zf), 0.5f, Vector3.forward, new Color(1f, 0.55f, 0.25f, 0.95f), 0.2f, false);
            MakeMesh(root.transform, "Frame", fr.ToMesh("GateFrame"), _archMat);
        }

        // ── 복도: 깊이 아치 링 (순차 점등) + 측면 라인 ──
        {
            _layerMats = new Material[Layers];
            for (int l = 0; l < Layers; l++)
            {
                _layerMats[l] = Track(new Material(_neon));
                var lm = new MB();
                float z = 0.9f + l * 4.5f;
                PolyRibbon(lm, arch, z, 0.22f - l * 0.012f, new Color(0.55f, 0.93f, 1f, 1f - l * 0.075f), 0.35f);
                MakeMesh(root.transform, "Layer" + l, lm.ToMesh("GateLayer"), _layerMats[l]);
            }
            var jm = new MB(); var ar = Arch(GA - 0.05f, GHS, 40);
            for (int i = 0; i < ar.Count; i++)
            {
                Vector2 p = ar[i]; Vector2 inward = (ctr - p).normalized;
                jm.Ribbon(new Vector3(p.x, p.y, 1f), new Vector3(p.x, p.y, 26f), 0.07f, new Vector3(inward.x, inward.y, 0f), new Color(0.5f, 0.9f, 1f, 0.5f), 0.3f, true);
            }
            var jmat = Track(new Material(_neon)); jmat.SetFloat("_Intensity", 1.6f);
            MakeMesh(root.transform, "Jambs", jm.ToMesh("GateJambs"), jmat);
        }

        // ── 끝의 빛의 막 + 코어 글로우 ──
        _curtainMat = Track(new Material(_neon)); _curtainMat.SetFloat("_Mode", 3f); _curtainMat.SetColor("_Color", Color.white);
        var cur = MakeMesh(root.transform, "Curtain", FanMesh("GateCurtain", Arch(GA - 0.1f, GHS), ctr, new Color(0.85f, 0.98f, 1f, 1f), new Color(0.15f, 0.5f, 0.95f, 0.65f), 0f), _curtainMat);
        cur.transform.localPosition = new Vector3(0f, 0f, 120f); _curtain = cur.transform;
        var gm = new MB(); gm.Glow(Vector3.zero, Vector3.right, Vector3.up, Color.white);
        _portalCoreMat = Track(new Material(_glow)); _portalCoreMat.SetColor("_Color", new Color(0.6f, 0.9f, 1f));
        var core = MakeMesh(root.transform, "Core", gm.ToMesh("GateCore"), _portalCoreMat);
        core.transform.localPosition = new Vector3(0, GateH * 0.5f, 124f); _portalCore = core.transform;

        // ── 도어 리프 (벽 바로 뒤. 위/아래로 슬라이드하여 벽 뒤로 사라진다) ──
        {
            float mid = GateH * 0.5f;
            _leafTop = new GameObject("LeafTop").transform; _leafTop.SetParent(root.transform, false); _leafTop.localPosition = new Vector3(0, 0, 0.5f);
            _leafBot = new GameObject("LeafBot").transform; _leafBot.SetParent(root.transform, false); _leafBot.localPosition = new Vector3(0, 0, 0.5f);
            for (int side = 0; side < 2; side++)
            {
                bool top = side == 0;
                var loop = new System.Collections.Generic.List<Vector2>();
                var full = Arch(GA + 0.06f, GHS, 28);
                if (top) { loop.Add(new Vector2(GA + 0.06f, mid)); foreach (var p in full) if (p.y > mid + 0.001f || (p.y >= GHS - 0.001f)) loop.Add(p); loop.Add(new Vector2(-(GA + 0.06f), mid)); }
                else { loop.Add(new Vector2(-(GA + 0.06f), mid)); loop.Add(new Vector2(-(GA + 0.06f), 0f)); loop.Add(new Vector2(GA + 0.06f, 0f)); loop.Add(new Vector2(GA + 0.06f, mid)); }
                // 부채꼴이 볼록하지 않을 수 있어 중심은 구간 중앙
                float cy = top ? (mid + GateH) * 0.5f : mid * 0.5f;
                var face = FanMesh("Leaf", loop, new Vector2(0, cy), new Color(0.012f, 0.028f, 0.05f), new Color(0.010f, 0.024f, 0.045f), 0f);
                var tr = top ? _leafTop : _leafBot;
                MakeMesh(tr, "Face", face, _doorMat);
                // 네온 디테일
                var nb = new MB(); float zn = -0.04f;
                float s0 = top ? 1 : -1;
                nb.Ribbon(new Vector3(-GA, mid, zn), new Vector3(GA, mid, zn), 0.16f, Vector3.forward, new Color(0.7f, 0.97f, 1f, 1f), 0f, false);   // 이음선
                nb.Ribbon(new Vector3(-1.1f, mid + s0 * 0.22f, zn), new Vector3(1.1f, mid + s0 * 0.22f, zn), 0.22f, Vector3.forward, new Color(1f, 0.55f, 0.25f, 0.95f), 0f, false);
                for (int g = 1; g <= 5; g++)
                {
                    float y = mid + s0 * g * 1.3f; float w = GA * (1f - 0.04f * g);
                    if (top ? y < GHS : y > 0.4f)
                        nb.Ribbon(new Vector3(-w, y, zn), new Vector3(w, y, zn), 0.05f, Vector3.forward, new Color(0.4f, 0.85f, 1f, 0.4f - g * 0.04f), 0f, false);
                }
                var inset = Arch(GA - 0.55f, GHS - 0.55f, 24); var insetPts = new System.Collections.Generic.List<Vector2>();
                foreach (var p in inset) if (top ? p.y >= mid : p.y <= mid) insetPts.Add(p);
                PolyRibbon(nb, insetPts, zn, 0.09f, new Color(0.45f, 0.88f, 1f, 0.6f));
                MakeMesh(tr, "Trim", nb.ToMesh("LeafTrim"), _neon);
            }
        }

        // ── 바닥 진입 유도등 (순차 점멸) ──
        {
            _guideMat = Track(new Material(_neon)); _guideMat.SetFloat("_Intensity", 2.2f);
            _guideRends = new Renderer[GuideRows]; _mpb = new MaterialPropertyBlock();
            var row = new MB();
            for (int s = -1; s <= 1; s += 2)
                row.Ribbon(new Vector3(s * 3.4f, 0.03f, -0.9f), new Vector3(s * 3.4f, 0.03f, 0.9f), 0.5f, up, new Color(0.6f, 0.95f, 1f, 1f), 0f, true);
            row.Ribbon(new Vector3(-3.4f, 0.03f, 0f), new Vector3(3.4f, 0.03f, 0f), 0.06f, up, new Color(0.4f, 0.85f, 1f, 0.5f), 0f, false);
            var rowMesh = row.ToMesh("GuideRow");
            for (int i = 0; i < GuideRows; i++)
            {
                var g = MakeMesh(root.transform, "Guide" + i, rowMesh, _guideMat);
                g.transform.localPosition = new Vector3(0f, 0f, -4.2f * (i + 1) + 2f);
                _guideRends[i] = g.GetComponent<Renderer>();
            }
        }
        BuildTunnelStreaks(root.transform, ctr);
        SetPortalOpen(0f);
    }

    // 디지털입구_02: 벽/바닥/천장에 흐르는 수많은 빛줄기와 데이터 바 — 기체 속도에 따라 길어지며 빨려드는 느낌
    void BuildTunnelStreaks(Transform root, Vector2 ctr)
    {
        _tunnelMat = Track(new Material(_neon));
        var rng = new System.Random(77);
        Color[] cols = { new Color(0.55f, 0.92f, 1f, 1f), new Color(0.95f, 1f, 1f, 1f), new Color(0.25f, 0.6f, 1f, 1f), new Color(1f, 0.62f, 0.3f, 1f) };
        var meshes = new Mesh[cols.Length];
        for (int i = 0; i < cols.Length; i++)
        {
            var mb = new MB();
            mb.Ribbon(new Vector3(0, 0, -2f), new Vector3(0, 0, 2f), 0.09f, Vector3.up, cols[i], 0f, true);
            meshes[i] = mb.ToMesh("TunnelStreak" + i);
        }
        var ar = Arch(GA - 0.35f, GHS, 40);
        int N = 150;
        _tStreaks = new Transform[N]; _tSpd = new float[N]; _tLen = new float[N];
        for (int i = 0; i < N; i++)
        {
            double r = rng.NextDouble();
            Vector3 pos; Vector3 inward;
            if (false)   // 바닥 빛줄기는 덜덜거려 보여 사용하지 않음
            {
                pos = new Vector3((float)(rng.NextDouble() * 2 - 1) * (GA - 0.4f), 0.04f, 0f); inward = Vector3.up;
            }
            else
            {
                int idx = rng.Next(ar.Count - 1); float f = (float)rng.NextDouble();
                Vector2 p = Vector2.Lerp(ar[idx], ar[idx + 1], f);
                pos = new Vector3(p.x, p.y, 0f);
                Vector2 inw = (ctr - p).normalized; inward = new Vector3(inw.x, inw.y, 0f);
            }
            int ci = rng.NextDouble() < 0.12 ? 3 : (rng.NextDouble() < 0.25 ? 1 : (rng.NextDouble() < 0.5 ? 0 : 2));
            var g = MakeMesh(root, "TS" + i, meshes[ci], _tunnelMat);
            pos.z = 1.5f + (float)rng.NextDouble() * TunnelLen;
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.LookRotation(Vector3.forward, inward);
            _tLen[i] = 0.5f + (float)rng.NextDouble() * 2.2f;
            _tSpd[i] = 0.7f + (float)rng.NextDouble() * 0.8f;
            _tStreaks[i] = g.transform;
        }
    }

    /// <summary>터널 안 상대 속도(m/s). 빛줄기가 이 속도로 흘러 길게 늘어난다.</summary>
    public void SetTunnelSpeed(float v) { _tunnelSpeed = v; }

    /// <summary>0 = 닫힘(도어 리프가 아치를 막음), 1 = 활짝 열림(리프가 위/아래로 벽 뒤로 사라지고 복도+빛이 드러남)</summary>
    public void SetPortalOpen(float v)
    {
        _open = Mathf.Clamp01(v);
        if (_portal == null) return;
        float e = _open * _open * (3f - 2f * _open);
        float travel = GateH * 0.5f + 1.4f;
        _leafTop.localPosition = new Vector3(0f, e * travel, 0.5f);
        _leafBot.localPosition = new Vector3(0f, -e * travel, 0.5f);
        for (int l = 0; l < Layers; l++)
        {
            float el = Mathf.Clamp01(_open * 1.6f - l * 0.07f);
            _layerMats[l].SetFloat("_Intensity", Mathf.Lerp(0.05f, 2.6f, el * el * (3f - 2f * el)));
        }
        ApplyPortalEnergy();
    }

    /// <summary>열린 뒤 기체가 접근할수록 0→1 : 빛이 강해지고 유도등이 빨라진다 (빨려드는 느낌)</summary>
    public void SetPortalEnergy(float v) { _energy = Mathf.Clamp01(v); ApplyPortalEnergy(); }

    void ApplyPortalEnergy()
    {
        if (_portal == null) return;
        float e = _open * _open * (3f - 2f * _open);
        _curtainMat.SetFloat("_Intensity", Mathf.Lerp(0f, 0.55f, e) * (1f + _energy * 0.5f));
        _portalCoreMat.SetFloat("_Intensity", Mathf.Lerp(0f, 0.9f, e) * (1f + _energy * 0.8f));
        if (_tunnelMat != null) _tunnelMat.SetFloat("_Intensity", Mathf.Lerp(0f, 2.0f, e) * (0.85f + _energy * 0.5f));
        _portalCore.localScale = Vector3.one * Mathf.Lerp(14f, 36f, e) * (1f + _energy * 0.3f);
    }

    void UpdatePortal(float t)
    {
        if (_portal == null) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float tv = 6f + _tunnelSpeed;
        float stretch = 1f + Mathf.Min(_tunnelSpeed * 0.03f, 6f);
        for (int i = 0; i < _tStreaks.Length; i++)
        {
            var tr = _tStreaks[i]; var p = tr.localPosition;
            p.z -= dt * tv * _tSpd[i];
            if (p.z < 1.5f) p.z += TunnelLen;
            tr.localPosition = p;
            tr.localScale = new Vector3(1f, 1f, _tLen[i] * stretch);
        }
        _guidePhase += Time.unscaledDeltaTime * (3.5f + _open * 4f + _energy * 14f);
        float baseI = Mathf.Lerp(0.5f, 1.6f, _open);
        for (int i = 0; i < _guideRends.Length; i++)
        {
            // 게이트 쪽(작은 인덱스 → 먼 쪽)으로 흐르는 빛
            float k = Mathf.Max(0f, Mathf.Sin(_guidePhase - i * 0.55f));
            _guideRends[i].GetPropertyBlock(_mpb);
            _mpb.SetFloat("_Intensity", baseI * (0.35f + 2.4f * Mathf.Pow(k, 5f)));
            _guideRends[i].SetPropertyBlock(_mpb);
        }
    }

    // ── 기체 ──────────────────────────────────────────────────────────────
    void BuildJet(GameObject prefab, Shader holoSh, Transform parent)
    {
        // 본체: 틴트 유리 + 정밀 격자 (글래스 셰이더가 없으면 기존 와이어로 폴백)
        _holo = Track(new Material(_glassSh != null ? _glassSh : holoSh));
        _holo.SetColor("_Color", new Color(0.09f, 0.48f, 1f));
        _holo.SetColor("_Hot", new Color(0.62f, 0.93f, 1f));
        _holo.SetFloat("_Intensity", _holoI);
        // 바닥 반사: 깊이를 쓰지 않는 가산 와이어로 은은하게
        _holoMirror = Track(new Material(holoSh));
        _holoMirror.SetColor("_Color", new Color(0.12f, 0.55f, 1f));
        _holoMirror.SetColor("_Hot", new Color(0.6f, 0.9f, 1f));
        _holoMirror.SetFloat("_Intensity", 0.11f);

        Jet = Instantiate(prefab, parent).transform; Jet.name = "Jet"; Jet.localScale = Vector3.one * JetScale;
        _rends = Jet.GetComponentsInChildren<Renderer>();
        _litMats = new Material[_rends.Length][];
        for (int i = 0; i < _rends.Length; i++)
        {
            _litMats[i] = LaunchSequence.TuneMaterials(_rends[i].sharedMaterials);
            _rends[i].shadowCastingMode = ShadowCastingMode.Off; _rends[i].receiveShadows = false;
            SetMats(_rends[i], _holo);
        }
        // 바닥 반사용 복제 (y 반전 루트 아래)
        var mr = new GameObject("MirrorRoot"); mr.transform.SetParent(parent, false);
        mr.transform.localScale = new Vector3(1f, -1f, 1f);
        _mirror = Instantiate(prefab, mr.transform).transform; _mirror.name = "JetMirror"; _mirror.localScale = Vector3.one * JetScale;
        _mirrorRends = _mirror.GetComponentsInChildren<Renderer>();
        foreach (var r in _mirrorRends) { SetMats(r, _holoMirror); r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
        // 솔리드화 스윕 덮개: 아직 물질화되지 않은 부분(후미 쪽)을 홀로그램 유리로 덮어 솔리드를 가린다 (기체 자식, 동일 포즈)
        _ovMat = Track(new Material(_holo));
        _ovMat.SetFloat("_RevealDir", -1f);
        _ovMat.SetFloat("_Opacity", 0.94f);
        _holoOverlay = Instantiate(prefab, Jet).transform; _holoOverlay.name = "HoloOverlay";
        _holoOverlay.localPosition = Vector3.zero; _holoOverlay.localRotation = Quaternion.identity; _holoOverlay.localScale = Vector3.one;
        _ovRends = _holoOverlay.GetComponentsInChildren<Renderer>();
        foreach (var r in _ovRends) { SetMats(r, _ovMat); r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
        _holoOverlay.gameObject.SetActive(false);
    }

    static void SetMats(Renderer r, Material m)
    {
        var arr = new Material[r.sharedMaterials.Length];
        for (int i = 0; i < arr.Length; i++) arr[i] = m;
        r.sharedMaterials = arr;
    }

    // ── 화염 (3D, 가산) ────────────────────────────────────────────────────
    void BuildFlames()
    {
        _flames = new Transform[Nozzles.Length]; _flameGlow = new Transform[Nozzles.Length];
        var fm = new MB();
        // 노즐 축(z)을 포함하는 3장의 평면(60° 간격) — 어느 각도에서도 원뿔처럼 보인다. 길이 1, 폭 1 (스케일로 조절)
        for (int k = 0; k < 3; k++)
        {
            Quaternion q = Quaternion.Euler(0f, 0f, k * 60f);
            fm.Quad(q * new Vector3(-0.5f, 0, 0.15f), q * new Vector3(0.5f, 0, 0.15f), q * new Vector3(0.5f, 0, -1), q * new Vector3(-0.5f, 0, -1), Color.white, 0f, 1f);
        }
        var flameMesh = fm.ToMesh("Flame");
        var gm = new MB(); gm.Glow(Vector3.zero, Vector3.right, Vector3.up, Color.white);
        var glowMesh = gm.ToMesh("FlameGlow");
        var glowMat = Track(new Material(_glow)); glowMat.SetColor("_Color", new Color(1f, 0.6f, 0.3f)); glowMat.SetFloat("_Intensity", 0.5f);
        for (int i = 0; i < Nozzles.Length; i++)
        {
            var f = MakeMesh(Jet, "Flame" + i, flameMesh, _flameMat);
            f.transform.localPosition = Nozzles[i];
            f.transform.localScale = new Vector3(1.2f, 1.2f, 0.01f);
            f.SetActive(false);
            _flames[i] = f.transform;
            var g = MakeMesh(Jet, "FlameGlow" + i, glowMesh, glowMat);
            g.transform.localPosition = Nozzles[i] + new Vector3(0, 0, -0.1f);
            g.transform.localScale = Vector3.one * 0.01f;
            g.SetActive(false);
            _flameGlow[i] = g.transform;
        }
        SetLayerRecursive(Jet.gameObject, Layer);
    }

    // 솔리드 전환 때의 끊김(첫 렌더 시 셰이더 변형 컴파일 + 대형 텍스처 업로드)을 로비 시작 시점으로 옮긴다.
    // 아주 작은 Lit 기체를 카메라 앞에 몇 프레임 그린다(조명 존재, 강도 0). 카메라가 만들어진 뒤 Update 에서 정리.
    void BeginPrewarm(GameObject prefab)
    {
        _warm = Instantiate(prefab, _stageRoot.transform).transform; _warm.name = "Prewarm";
        _warm.localScale = Vector3.one * 0.02f;
        foreach (var r in _warm.GetComponentsInChildren<Renderer>())
        {
            r.sharedMaterials = LaunchSequence.TuneMaterials(r.sharedMaterials);
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }
        foreach (var l in _lights) { l.intensity = 0f; l.enabled = true; }
    }

    // ── 조명 / 카메라 / 포스트 ─────────────────────────────────────────────
    void BuildLights(Transform parent)
    {
        _lights = new Light[3];
        _lights[0] = MakeLight(parent, "Key", new Color(0.55f, 0.8f, 1f), 2.2f, Quaternion.Euler(24f, 205f, 0f));
        _lights[1] = MakeLight(parent, "Rim", new Color(1f, 0.6f, 0.3f), 1.6f, Quaternion.Euler(18f, -20f, 0f));
        _lights[2] = MakeLight(parent, "Top", new Color(0.7f, 0.85f, 1f), 0.8f, Quaternion.Euler(80f, 0f, 0f));
        _lightBase = new float[_lights.Length];
        for (int i = 0; i < _lights.Length; i++) _lightBase[i] = _lights[i].intensity;
        foreach (var l in _lights) l.enabled = false;   // 솔리드 전환 시에만 켠다
    }

    Light MakeLight(Transform parent, string n, Color c, float i, Quaternion rot)
    {
        var go = new GameObject(n); go.transform.SetParent(parent, false); go.transform.rotation = rot;
        var l = go.AddComponent<Light>(); l.type = LightType.Directional; l.color = c; l.intensity = i; l.shadows = LightShadows.None;
        return l;
    }

    void BuildCamera(Transform parent)
    {
        RT = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, hideFlags = HideFlags.DontSave };
        var cg = new GameObject("StageCam"); cg.transform.SetParent(parent, false);
        Cam = cg.AddComponent<Camera>();
        Cam.clearFlags = CameraClearFlags.SolidColor;
        Cam.backgroundColor = new Color(0.008f, 0.016f, 0.035f, 1f);
        Cam.cullingMask = 1 << Layer;
        Cam.nearClipPlane = 0.3f; Cam.farClipPlane = 400f;
        Cam.fieldOfView = 40f;
        Cam.allowHDR = true;
        Cam.targetTexture = RT;
        Raw.texture = RT;
    }

    void BuildPost(GameObject root)
    {
        var data = Cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.volumeLayerMask = 1 << Layer;
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        prof.hideFlags = HideFlags.DontSave; _owned.Add(prof);
        var bloom = prof.Add<Bloom>(true);
        bloom.threshold.Override(0.85f); bloom.intensity.Override(1.35f); bloom.scatter.Override(0.72f);
        // 솔리드 전환 중 캐노피의 극단적 스펙큘러가 화면 전체를 뿌옇게 번지게 하지 않도록 상한
        bloom.clamp.Override(8f);
        data.stopNaN = true;
        bloom.highQualityFiltering.Override(true);
        var vg = prof.Add<Vignette>(true);
        vg.intensity.Override(0.34f); vg.smoothness.Override(0.6f);
        // 필름 톤매핑: 과포화된 시안을 눌러 '장난감' 같은 플라스틱 색감 대신 영화적인 계조로
        var tm = prof.Add<Tonemapping>(true);
        tm.mode.Override(TonemappingMode.ACES);
        var ca = prof.Add<ColorAdjustments>(true);
        ca.postExposure.Override(0.2f); ca.contrast.Override(14f); ca.saturation.Override(-4f);
        var fg = prof.Add<FilmGrain>(true);
        fg.type.Override(FilmGrainLookup.Thin1); fg.intensity.Override(0.18f); fg.response.Override(0.8f);
        var chroma = prof.Add<ChromaticAberration>(true);
        chroma.intensity.Override(0.08f);
        var volGO = new GameObject("Volume"); volGO.transform.SetParent(root.transform, false); volGO.layer = Layer;
        var vol = volGO.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 50; vol.sharedProfile = prof;
    }

    static void SetLayerRecursive(GameObject g, int l)
    {
        g.layer = l;
        foreach (Transform c in g.transform) SetLayerRecursive(c.gameObject, l);
    }

    // ── 외부 제어 ─────────────────────────────────────────────────────────
    /// <summary>홀로그램 공개량 0~1 (기수→후미)</summary>
    public void SetReveal(float v) { _holo.SetFloat("_Reveal", v); _holoMirror.SetFloat("_Reveal", v); }
    public void SetHoloFill(float v) { _holo.SetFloat("_Fill", v); }
    public void SetHoloIntensity(float v) { _holoI = v; _holo.SetFloat("_Intensity", v); _holoMirror.SetFloat("_Intensity", v * 0.11f); }
    public bool Solid => _solid;

    /// <summary>솔리드(라이트 재질) ↔ 홀로그램 전환. 솔리드로 갈 땐 조명 0에서 시작하고 홀로그램 덮개가 전체를 가린 상태로 시작한다.</summary>
    public void SetSolid(bool solid)
    {
        if (_solid == solid) return;
        _solid = solid;
        for (int i = 0; i < _rends.Length; i++)
        {
            if (solid) _rends[i].sharedMaterials = _litMats[i]; else SetMats(_rends[i], _holo);
        }
        foreach (var r in _mirrorRends) r.enabled = !solid;
        foreach (var l in _lights) l.enabled = solid;
        if (solid)
        {
            _ovMat.CopyPropertiesFromMaterial(_holo);
            _ovMat.SetFloat("_RevealDir", -1f);
            _ovMat.SetFloat("_Opacity", 0.94f);
            _holoOverlay.gameObject.SetActive(true);
            SetMaterialize(0f);
            SetSolidBlend(0f);
        }
        else _holoOverlay.gameObject.SetActive(false);
    }

    /// <summary>물질화 스윕 0→1: 기수에서 후미로 밝은 경계면이 지나가며 지나간 자리가 솔리드 기체로 드러난다.</summary>
    public void SetMaterialize(float s)
    {
        s = Mathf.Clamp01(s);
        if (_holoOverlay == null) return;
        bool on = _solid && s < 0.999f;
        if (_holoOverlay.gameObject.activeSelf != on) _holoOverlay.gameObject.SetActive(on);
        if (on) { _ovMat.SetFloat("_Reveal", s); _ovMat.SetFloat("_Intensity", Mathf.Max(0.6f, _holoI) * 1.25f); }
    }

    /// <summary>솔리드 조명/반사 0 → 1</summary>
    public void SetSolidBlend(float b)
    {
        b = Mathf.Clamp01(b);
        for (int i = 0; i < _lights.Length; i++) _lights[i].intensity = _lightBase[i] * b;
        RenderSettings.reflectionIntensity = b;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.07f, 0.10f, 0.19f) * b;
    }

    public void ShowPad(bool on, float alpha = 1f)
    {
        _pad.gameObject.SetActive(on);
        if (_padGlow != null) _padGlow.gameObject.SetActive(on);
    }

    /// <summary>게이트 아치 네온 밝기 0(대기: 은은하게) ~ 1(출격: 완전 점등)</summary>
    public void SetArchGlow(float a) { if (_archMat != null) _archMat.SetFloat("_Intensity", 1.1f * Mathf.Lerp(0.14f, 1f, Mathf.Clamp01(a))); }

    /// <summary>HUD 데크 밝기 (출격 연출 중 서서히 꺼진다)</summary>
    public void SetDeck(float a)
    {
        if (_deck == null) return;
        _deck.gameObject.SetActive(a > 0.01f);
        _deck.localScale = new Vector3(1f, 1f, 1f);
        if (_deckMat == null)
        {
            _deckMat = Track(new Material(_neon));
            _deck.GetComponent<Renderer>().sharedMaterial = _deckMat;
        }
        _deckMat.SetFloat("_Intensity", 1.45f * a);
    }
    Material _deckMat;

    /// <summary>애프터버너 세기 0~1 (0 이면 끔)</summary>
    public void SetFlame(float amount, float boost = 1f)
    {
        bool on = amount > 0.01f;
        for (int i = 0; i < _flames.Length; i++)
        {
            _flames[i].gameObject.SetActive(on); _flameGlow[i].gameObject.SetActive(on);
            if (!on) continue;
            float w = Mathf.Lerp(0.3f, 1.0f, amount);
            _flames[i].localScale = new Vector3(w, w, Mathf.Lerp(0.6f, 7.5f, amount) * boost);
            _flameGlow[i].localScale = Vector3.one * Mathf.Lerp(0.4f, 1.5f, amount);
        }
        _flameMat.SetFloat("_Flame", Mathf.Clamp01(amount * 1.2f));
    }

    public void MoveJet(Vector3 stageLocalPos, Quaternion rot)
    {
        Jet.position = Origin + stageLocalPos;
        Jet.rotation = rot;
        _mirror.localPosition = stageLocalPos; _mirror.localRotation = rot;
        if (_underGlow != null && _underGlow.gameObject.activeSelf)
        {
            Vector3 fwd = rot * Vector3.forward; fwd.y = 0f; fwd.Normalize();
            _underGlow.localPosition = new Vector3(stageLocalPos.x, 0.03f, stageLocalPos.z + fwd.z * 0.5f);
            _underGlow.localRotation = Quaternion.LookRotation(fwd, Vector3.up);
            _heatGlow.localPosition = new Vector3(stageLocalPos.x, 0.035f, stageLocalPos.z - fwd.z * 9f);
            _heatGlow.localRotation = Quaternion.LookRotation(fwd, Vector3.up);
        }
    }

    /// <summary>기체가 지면에 있을 때 밀착 글로우 표시. heat = 애프터버너 열기(0~1)</summary>
    public void SetGrounded(bool on, float heat)
    {
        if (_underGlow == null) return;
        _underGlow.gameObject.SetActive(on); _heatGlow.gameObject.SetActive(on && heat > 0.01f);
        if (on) _heatMat.SetFloat("_Intensity", Mathf.Clamp01(heat) * 0.55f);
    }

    public void FinishFrame()
    {
        var m = Jet.worldToLocalMatrix;
        Shader.SetGlobalMatrix("_KF_W2J", m);
        Shader.SetGlobalVector("_KF_JetCenter", Jet.TransformPoint(new Vector3(0f, 1.6f, -0.5f)));
        // 글로우 스프라이트는 카메라를 바라보게
        for (int i = 0; i < _flameGlow.Length; i++)
            if (_flameGlow[i].gameObject.activeSelf) _flameGlow[i].rotation = Cam.transform.rotation;
    }

    public void SetCamera(Vector3 stageLocalPos, Quaternion rot, float fov)
    {
        Cam.transform.position = Origin + stageLocalPos;
        Cam.transform.rotation = rot;
        Cam.fieldOfView = fov;
    }

    // ── 대기 연출(로비) ───────────────────────────────────────────────────
    void Update()
    {
        _t += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        if (_warm != null)
        {
            _warm.position = Cam.transform.position + Cam.transform.forward * 8f;    // 항상 시야 중앙
            if (++_warmFrames > 4)
            {
                Destroy(_warm.gameObject); _warm = null;
                if (!_solid) foreach (var l in _lights) l.enabled = false;
            }
        }
        AnimateEnv(Time.unscaledDeltaTime, StreakSpeed);
        UpdatePortal(_t);
        if (Idle) UpdateIdle(_t);
        FinishFrame();
    }

    public Vector3 IdleCamPos => new Vector3(2.4f, 6.2f, 8.0f);   // 살짝 내려다보는 앵글: 기체(z≈30)를 화면 우측에 크게 — UI 패널은 좌측
    public Quaternion IdleCamRot => Quaternion.Euler(9.5f, -6.5f, 0f);

    void UpdateIdle(float t)
    {
        // 기체는 히어로 자세로 고정한 채 아주 느리게 부유(호흡)만 하고,
        // 카메라가 기체를 중심으로 천천히 호를 그리며 돈다(±9°, 약 90초 주기) — 쇼룸 턴테이블 같은 시차감.
        // 카메라를 기체 중심 기준으로 강체 회전시키므로 화면 속 기체 위치(우측)는 그대로 유지된다.
        float bob = Mathf.Sin(t * 0.55f) * 0.07f;
        MoveJet(PadPos + new Vector3(0f, 0.9f + bob, 0f),
                Quaternion.Euler(-0.5f + Mathf.Sin(t * 0.4f) * 0.3f, 196f, Mathf.Sin(t * 0.33f) * 0.6f));
        Vector3 pivot = PadPos + new Vector3(0f, 2.2f, 0f);
        if (_archMat != null) SetArchGlow(0f);
        float orbit = Mathf.Sin(t * 0.07f) * 4f;   // 어색한 비스듬한 각도가 나오지 않도록 좁게
        float dolly = 1f + Mathf.Sin(t * 0.05f + 1.1f) * 0.035f;
        Quaternion q = Quaternion.Euler(0f, orbit, 0f);
        SetCamera(pivot + q * (IdleCamPos - pivot) * dolly, q * IdleCamRot, 40f);
        _pad.localRotation = Quaternion.Euler(0f, t * 4f, 0f);   // 느린 회전 (선이 충분히 굵어 반짝임 없음)
        // 스캔 링: 4.5초 주기로 데크에서 기체 높이까지 천천히 올라가며 살짝 좁아지고,
        // 시작/끝에서 밝기가 0 으로 부드럽게 페이드 → 주기가 바뀌는 순간 끊김이 보이지 않는다.
        float k = Mathf.Repeat(t / 4.5f, 1f);
        float rise = 1f - (1f - k) * (1f - k);
        _scanRing.localPosition = PadPos + new Vector3(0f, 0.08f + rise * 5.6f, 0f);
        _scanRing.localScale = Vector3.one * Mathf.Lerp(1.0f, 0.86f, rise);
        _scanRing.localRotation = Quaternion.Euler(0f, -t * 9f, 0f);
        float fade = Mathf.SmoothStep(0f, 1f, k / 0.12f) * (1f - Mathf.SmoothStep(0f, 1f, (k - 0.5f) / 0.42f));
        _scanMat.SetFloat("_Intensity", 1.6f * fade);
        _scanRing.gameObject.SetActive(!_solid && fade > 0.001f);
    }

    public void AnimateEnv(float dt, float speed)
    {
        for (int i = 0; i < _streaks.Length; i++)
        {
            var p = _streaks[i].localPosition;
            p.z -= dt * speed * _streakSpd[i];
            if (p.z < Z0) p.z += Z1 - Z0;
            else if (p.z > Z1) p.z -= Z1 - Z0;
            _streaks[i].localPosition = p;
        }
    }

    public void HideScan() { _scanRing.gameObject.SetActive(false); }

    void OnDestroy()
    {
        if (RT != null) { RT.Release(); Destroy(RT); }
        if (_stageRoot != null) Destroy(_stageRoot);
        foreach (var o in _owned) if (o != null) Destroy(o);
    }
}

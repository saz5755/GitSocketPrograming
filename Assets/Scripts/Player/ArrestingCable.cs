using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 어레스팅 기어 한 세트의 비주얼 — 크로스덱 펜던트(와이어로프) + 갑판 하드웨어.
///   · 케이블: 매 프레임 재구성되는 튜브 메쉬 (Custom/WireRope). 평시엔 보우 스프링 서포트 위에 걸려 있고,
///             훅이 걸리면 양쪽 시브에서 풀려나오며 V자로 늘어나고, 해제되면 갑판 위로 늘어진 채 원위치로 회수된다.
///   · 시브 하우징: 갑판 가장자리 덮개판 + 반쯤 묻힌 시브 휠 + 측판 + 볼트 (정적 — 재질별 메쉬 1개로 병합)
///   · 와이어 서포트: 케이블을 갑판에서 띄우는 휜 판스프링. 기체가 지나가면 눌려 납작해진다.
///   · 터미널: 펜던트와 퍼체스 케이블을 잇는 소켓 피팅 (케이블을 따라 이동)
/// 좌표는 와이어 루트 로컬 기준 — x: 갑판 가로(케이블 방향), y: 갑판 면 = 0, z: 착함 진행 축.
/// </summary>
public class ArrestingCable
{
    public enum State { Rest, Engaged, Releasing }

    // ── 치수 (m) ─────────────────────────────────────────────────────────────
    const float Radius        = 0.028f;   // 케이블 반경 — 실물(약 1.4in)보다 약간 굵게, 원거리 가독성
    const float RestHeight    = 0.11f;    // 평시 케이블 중심 높이 (서포트 위)
    const float WheelRadius   = 0.2f;
    const int   SpanPoints    = 72;       // 갑판 위 구간 분할
    const int   RingSides     = 10;
    const int   SupportCount  = 6;
    const float TerminalDist  = 1.3f;     // 시브에서 터미널까지 케이블 길이

    public State CurrentState { get; private set; } = State.Rest;
    public Vector3 RestApexLocal => new Vector3(0f, RestHeight, 0f);

    readonly Transform _root;
    readonly float     _halfSpan;

    // 케이블 메쉬
    readonly Mesh       _mesh;
    readonly List<Vector3> _pts   = new List<Vector3>(SpanPoints + 8);
    readonly List<Vector3> _verts = new List<Vector3>();
    readonly List<Vector3> _norms = new List<Vector3>();
    readonly List<Vector4> _tans  = new List<Vector4>();
    readonly List<Vector2> _uvs   = new List<Vector2>();
    readonly float[]       _arc   = new float[SpanPoints + 8];

    readonly Transform   _supports;
    readonly Transform[] _terminals = new Transform[2];

    // 동역학
    Vector3 _apex, _apexVel;
    float   _waveT, _waveAmp;
    float   _collapse;          // 서포트 눌림 0..1
    bool    _dirty = true;
    Vector3 _engageFrom;        // 체결 순간 케이블 위치 — 훅으로 짧게 블렌드
    float   _engageBlend;

    public ArrestingCable(Transform root, float span, Material cableMat, Material paintMat,
                          Material steelMat, Material darkMat)
    {
        _root     = root;
        _halfSpan = span * 0.5f;

        // ── 케이블 ────────────────────────────────────────────────────────
        var cable = new GameObject("PendantCable");
        cable.transform.SetParent(root, false);
        _mesh = new Mesh { name = "PendantCable" };
        _mesh.MarkDynamic();
        cable.AddComponent<MeshFilter>().sharedMesh = _mesh;
        var mr = cable.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = cableMat;
        mr.shadowCastingMode = ShadowCastingMode.On;
        mr.receiveShadows    = true;

        BuildRestPoints();
        BuildTopology();
        RebuildMesh();

        // ── 정적 하드웨어 (재질별 병합) ────────────────────────────────────
        BuildHousings(paintMat, steelMat, darkMat);

        // ── 서포트 (전체를 한 번에 눌리도록 하나의 메쉬) ────────────────────
        _supports = BuildSupports(paintMat);

        // ── 터미널 ───────────────────────────────────────────────────────
        var cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        for (int i = 0; i < 2; i++)
        {
            var t = new GameObject(i == 0 ? "TerminalL" : "TerminalR");
            t.transform.SetParent(root, false);
            t.AddComponent<MeshFilter>().sharedMesh = cyl;
            var tr = t.AddComponent<MeshRenderer>();
            tr.sharedMaterial    = steelMat;
            tr.shadowCastingMode = ShadowCastingMode.On;
            t.transform.localScale = new Vector3(0.095f, 0.19f, 0.095f);   // 직경 9.5cm, 길이 38cm
            _terminals[i] = t.transform;
        }
        PlaceTerminals();

        _apex = RestApexLocal;
    }

    // ── 외부 API ─────────────────────────────────────────────────────────────

    /// <summary>훅이 케이블을 낚아챔 — 꼭짓점이 훅 끝을 따라가기 시작한다.</summary>
    public void Engage(Vector3 hookWorld)
    {
        CurrentState = State.Engaged;
        Vector3 h = _root.InverseTransformPoint(hookWorld);
        _apex     = new Vector3(Mathf.Clamp(h.x, -_halfSpan + 1f, _halfSpan - 1f), RestHeight, 0f);
        _engageFrom  = _apex;
        _engageBlend = 0f;
        _apexVel  = Vector3.zero;
        _waveT    = 0f;
        _waveAmp  = 0.1f;
        _dirty    = true;
    }

    /// <summary>훅이 케이블을 놓음 — 갑판 위에 늘어진 채 원위치로 회수된다.</summary>
    public void Release()
    {
        if (CurrentState != State.Engaged) return;
        CurrentState = State.Releasing;
        _apexVel     = Vector3.zero;
        _waveT       = 0f;
        _waveAmp     = 0.06f;   // 장력이 풀리며 튕기는 작은 출렁임
    }

    /// <param name="hookWorld">Engaged 상태일 때 훅 끝 월드 좌표</param>
    public void Tick(float dt, Vector3 hookWorld)
    {
        switch (CurrentState)
        {
            case State.Engaged:
            {
                Vector3 target = _root.InverseTransformPoint(hookWorld);
                target.y = Mathf.Max(target.y, Radius);
                // 고장력 케이블 — 체결 첫 60ms만 제자리에서 훅으로 블렌드하고 이후엔 훅 끝에 고정
                // (월드 공간 지수 보간은 고속에서 훅 뒤로 1m 이상 처지므로 쓰지 않는다)
                _engageBlend = Mathf.Min(1f, _engageBlend + dt / 0.06f);
                _apex = Vector3.Lerp(_engageFrom, target, Mathf.SmoothStep(0f, 1f, _engageBlend));
                _collapse = Mathf.MoveTowards(_collapse, 1f, dt * 12f);
                _waveT += dt;
                _dirty = true;
                break;
            }
            case State.Releasing:
            {
                // 덱 엔진이 케이블을 끌어당겨 원위치 — 임계 감쇠 스프링 (~2.5초)
                const float w = 2.4f;
                Vector3 rest = RestApexLocal;
                Vector3 acc  = (rest - _apex) * (w * w) - _apexVel * (2f * w);
                _apexVel += acc * dt;
                _apex    += _apexVel * dt;
                _waveT   += dt;

                float remain = (rest - _apex).magnitude;
                // 케이블이 거의 제자리에 오면 서포트가 다시 튀어올라 케이블을 받친다
                if (remain < 1.2f) _collapse = Mathf.MoveTowards(_collapse, 0f, dt * 2.5f);

                if (remain < 0.01f && _collapse <= 0f)
                {
                    CurrentState = State.Rest;
                    _apex = rest; _apexVel = Vector3.zero;
                    BuildRestPoints();
                    RebuildMesh();
                    PlaceTerminals();
                    ApplySupportCollapse();
                    return;
                }
                _dirty = true;
                break;
            }
            default:
                if (_collapse > 0f) { _collapse = Mathf.MoveTowards(_collapse, 0f, dt * 2.5f); _dirty = true; }
                if (!_dirty) return;
                BuildRestPoints();
                RebuildMesh();
                PlaceTerminals();
                ApplySupportCollapse();
                _dirty = false;
                return;
        }

        BuildVPoints();
        RebuildMesh();
        PlaceTerminals();
        ApplySupportCollapse();
    }

    // ── 케이블 경로 ─────────────────────────────────────────────────────────

    // 시브 휠을 감아 갑판 아래로 들어가는 양 끝 구간 (s = -1 왼쪽, +1 오른쪽)
    void AddSheaveRun(int s, bool inbound)
    {
        float xe = s * _halfSpan;
        Vector3 top  = new Vector3(xe + s * 0.20f, RestHeight, 0f);
        Vector3 bend = new Vector3(xe + s * 0.34f, RestHeight - 0.09f, 0f);
        Vector3 into = new Vector3(xe + s * 0.42f, -0.4f, 0f);
        if (inbound) { _pts.Add(into); _pts.Add(bend); _pts.Add(top); }
        else         { _pts.Add(top);  _pts.Add(bend); _pts.Add(into); }
    }

    void BuildRestPoints()
    {
        _pts.Clear();
        AddSheaveRun(-1, true);
        float spacing = (2f * _halfSpan - 4f) / (SupportCount - 1);
        for (int i = 0; i <= SpanPoints; i++)
        {
            float x = Mathf.Lerp(-_halfSpan, _halfSpan, (float)i / SpanPoints);
            // 서포트 사이 미세한 처짐
            float u   = Mathf.Repeat((x - (-_halfSpan + 2f)) / spacing, 1f);
            float sag = 0.012f * Mathf.Sin(u * Mathf.PI) * (1f - _collapse);
            float y   = Mathf.Lerp(RestHeight - sag, Radius, _collapse);
            _pts.Add(new Vector3(x, y, 0f));
        }
        AddSheaveRun(1, false);
    }

    void BuildVPoints()
    {
        _pts.Clear();
        AddSheaveRun(-1, true);

        Vector3 L = new Vector3(-_halfSpan, RestHeight, 0f);
        Vector3 R = new Vector3( _halfSpan, RestHeight, 0f);
        int half = SpanPoints / 2;
        float decay = Mathf.Exp(-_waveT * 3.5f);
        bool slack  = CurrentState == State.Releasing;

        for (int i = 0; i <= SpanPoints; i++)
        {
            bool left = i <= half;
            float t   = left ? (float)i / half : (float)(i - half) / (SpanPoints - half);
            Vector3 p = left ? Vector3.Lerp(L, _apex, t) : Vector3.Lerp(_apex, R, t);

            // 끝단(시브)에서 0, 꼭짓점 쪽으로 커지는 횡파 — 체결 순간 케이블이 채찍처럼 출렁임
            float fromApex = left ? (1f - t) : t;
            float env      = Mathf.Sin(Mathf.Clamp01(1f - fromApex) * Mathf.PI);
            float segLen   = Vector3.Distance(left ? L : R, _apex);
            float phase    = fromApex * segLen / 5.5f * Mathf.PI * 2f - _waveT * 30f;
            float wave     = _waveAmp * decay * env * Mathf.Sin(phase);
            p.y += wave;
            p.z += wave * 0.15f * (left ? 1f : -1f);

            // 해제 후엔 장력이 없어 갑판 위에 늘어진다 — 서포트가 다시 서면서 받쳐 올림
            if (slack) p.y = Mathf.Lerp(p.y, Radius, Mathf.Sin(t * Mathf.PI) * _collapse);

            p.y = Mathf.Max(p.y, Radius);
            _pts.Add(p);
        }
        AddSheaveRun(1, false);
    }

    // ── 튜브 메쉬 ───────────────────────────────────────────────────────────

    void BuildTopology()
    {
        int rings = _pts.Count;
        var tris  = new int[(rings - 1) * RingSides * 6];
        int k = 0, stride = RingSides + 1;
        for (int i = 0; i < rings - 1; i++)
            for (int j = 0; j < RingSides; j++)
            {
                int a = i * stride + j, b = a + 1, c = a + stride, d = c + 1;
                tris[k++] = a; tris[k++] = c; tris[k++] = b;
                tris[k++] = b; tris[k++] = c; tris[k++] = d;
            }

        _verts.Clear(); _norms.Clear(); _tans.Clear(); _uvs.Clear();
        for (int i = 0; i < rings * stride; i++)
        {
            _verts.Add(Vector3.zero); _norms.Add(Vector3.up);
            _tans.Add(new Vector4(1, 0, 0, 1)); _uvs.Add(Vector2.zero);
        }
        _mesh.SetVertices(_verts);
        _mesh.SetTriangles(tris, 0);
    }

    void RebuildMesh()
    {
        int rings = _pts.Count, stride = RingSides + 1;

        _arc[0] = 0f;
        for (int i = 1; i < rings; i++) _arc[i] = _arc[i - 1] + Vector3.Distance(_pts[i - 1], _pts[i]);

        // 평행 이동 프레임 — 수직 구간(시브 → 갑판 아래)에서도 꼬이지 않음
        Vector3 prevN = Vector3.forward;
        for (int i = 0; i < rings; i++)
        {
            Vector3 a = _pts[Mathf.Max(0, i - 1)], b = _pts[Mathf.Min(rings - 1, i + 1)];
            Vector3 T = (b - a).normalized;
            Vector3 N = prevN - T * Vector3.Dot(prevN, T);
            if (N.sqrMagnitude < 1e-6f) N = Vector3.Cross(T, Vector3.right);
            N.Normalize();
            prevN = N;
            Vector3 B = Vector3.Cross(T, N);

            for (int j = 0; j <= RingSides; j++)
            {
                float ang = (float)j / RingSides * Mathf.PI * 2f;
                Vector3 n = N * Mathf.Cos(ang) + B * Mathf.Sin(ang);
                int v = i * stride + j;
                _verts[v] = _pts[i] + n * Radius;
                _norms[v] = n;
                _tans[v]  = new Vector4(T.x, T.y, T.z, 1f);
                _uvs[v]   = new Vector2(_arc[i], (float)j / RingSides);
            }
        }

        _mesh.SetVertices(_verts);
        _mesh.SetNormals(_norms);
        _mesh.SetTangents(_tans);
        _mesh.SetUVs(0, _uvs);
        _mesh.RecalculateBounds();
    }

    // 시브에서 일정 케이블 길이만큼 떨어진 곳에 터미널 피팅을 놓는다
    void PlaceTerminals()
    {
        int last = _pts.Count - 1;
        float total = _arc[last];
        PlaceAtArc(_terminals[0], _arc[2] + TerminalDist);
        PlaceAtArc(_terminals[1], total - _arc[2] - TerminalDist);
    }

    void PlaceAtArc(Transform t, float s)
    {
        int n = _pts.Count;
        for (int i = 1; i < n; i++)
        {
            if (_arc[i] < s && i < n - 1) continue;
            float seg = Mathf.Max(1e-4f, _arc[i] - _arc[i - 1]);
            float u   = Mathf.Clamp01((s - _arc[i - 1]) / seg);
            Vector3 dir = _pts[i] - _pts[i - 1];
            t.localPosition = Vector3.Lerp(_pts[i - 1], _pts[i], u);
            if (dir.sqrMagnitude > 1e-8f)
                t.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            return;
        }
    }

    void ApplySupportCollapse()
    {
        // 판스프링이 눌리면 높이가 줄고 약간 넓게 퍼진다
        _supports.localScale = new Vector3(1f, Mathf.Lerp(1f, 0.18f, _collapse), Mathf.Lerp(1f, 1.12f, _collapse));
    }

    // ── 정적 하드웨어 ───────────────────────────────────────────────────────

    void BuildHousings(Material paint, Material steel, Material dark)
    {
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var cyl  = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        var paintParts = new List<CombineInstance>();
        var steelParts = new List<CombineInstance>();
        var darkParts  = new List<CombineInstance>();

        for (int s = -1; s <= 1; s += 2)
        {
            float xe = s * _halfSpan;
            float xc = xe + s * 0.25f;

            // 덮개판 (논슬립 도장) + 테두리 베벨 대용 얇은 단
            paintParts.Add(Part(cube, new Vector3(xc, 0.012f, 0f), Quaternion.identity, new Vector3(1.35f, 0.024f, 0.95f)));
            paintParts.Add(Part(cube, new Vector3(xc, 0.026f, 0f), Quaternion.identity, new Vector3(1.2f, 0.008f, 0.82f)));

            // 케이블 출입구 슬롯 (어두운 개구부)
            darkParts.Add(Part(cube, new Vector3(xe + s * 0.30f, 0.031f, 0f), Quaternion.identity, new Vector3(0.5f, 0.002f, 0.18f)));

            // 시브 휠 — 반쯤 묻힘, 상단이 케이블 높이
            Vector3 wc = new Vector3(xe + s * 0.20f, RestHeight - WheelRadius, 0f);
            steelParts.Add(Part(cyl, wc, Quaternion.Euler(90f, 0f, 0f), new Vector3(WheelRadius * 2f, 0.05f, WheelRadius * 2f)));
            // 휠 허브
            darkParts.Add(Part(cyl, wc, Quaternion.Euler(90f, 0f, 0f), new Vector3(0.12f, 0.075f, 0.12f)));

            // 측판 (휠 양옆 하우징)
            for (int side = -1; side <= 1; side += 2)
            {
                paintParts.Add(Part(cube, new Vector3(xe + s * 0.20f, 0.06f, side * 0.085f), Quaternion.identity, new Vector3(0.52f, 0.07f, 0.022f)));
                // 측판 상단 라운드 캡
                paintParts.Add(Part(cyl, new Vector3(xe + s * 0.20f, 0.095f, side * 0.085f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.022f, 0.26f, 0.022f)));
            }

            // 덮개판 고정 볼트 6개
            for (int bx = -1; bx <= 1; bx++)
                for (int bz = -1; bz <= 1; bz += 2)
                    steelParts.Add(Part(cyl, new Vector3(xc + bx * 0.55f, 0.031f, bz * 0.39f), Quaternion.identity, new Vector3(0.045f, 0.006f, 0.045f)));
        }

        MakeCombined("SheaveHousing_Paint", paintParts, paint);
        MakeCombined("SheaveHousing_Steel", steelParts, steel);
        MakeCombined("SheaveHousing_Dark",  darkParts,  dark);
    }

    Transform BuildSupports(Material paint)
    {
        var parts = new List<CombineInstance>();
        var leaf  = BuildLeafSpringMesh();
        var cube  = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        for (int k = 0; k < SupportCount; k++)
        {
            float x = Mathf.Lerp(-_halfSpan + 2f, _halfSpan - 2f, (float)k / (SupportCount - 1));
            parts.Add(Part(leaf, new Vector3(x, 0f, 0f), Quaternion.identity, Vector3.one));
            // 갑판 고정 패드
            for (int side = -1; side <= 1; side += 2)
                parts.Add(Part(cube, new Vector3(x, 0.006f, side * 0.33f), Quaternion.identity, new Vector3(0.12f, 0.012f, 0.1f)));
        }
        var go = MakeCombined("WireSupports", parts, paint);
        Object.Destroy(leaf);
        return go.transform;
    }

    // 휜 판스프링 — z 방향으로 아치, 정점이 케이블 바로 아래
    static Mesh BuildLeafSpringMesh()
    {
        const int   seg   = 12;
        const float halfL = 0.33f, width = 0.075f, thick = 0.01f;
        float top = RestHeight - Radius;

        var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
        for (int face = 0; face < 2; face++)     // 0 = 윗면, 1 = 아랫면
        {
            int baseIdx = v.Count;
            for (int i = 0; i <= seg; i++)
            {
                float u  = (float)i / seg * 2f - 1f;
                float z  = u * halfL;
                float y  = top * (1f - u * u);
                float dy = -2f * u * top / halfL;               // dy/dz
                Vector3 nrm = new Vector3(0f, 1f, -dy).normalized;
                if (face == 1) { y -= thick; nrm = -nrm; }
                v.Add(new Vector3(-width * 0.5f, y, z)); n.Add(nrm);
                v.Add(new Vector3( width * 0.5f, y, z)); n.Add(nrm);
            }
            for (int i = 0; i < seg; i++)
            {
                int a = baseIdx + i * 2, b = a + 1, c = a + 2, d = a + 3;
                if (face == 0) { t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d); }
                else           { t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c); }
            }
        }
        var m = new Mesh { name = "LeafSpring" };
        m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0);
        return m;
    }

    static CombineInstance Part(Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale)
        => new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(pos, rot, scale) };

    GameObject MakeCombined(string name, List<CombineInstance> parts, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root, false);
        var m = new Mesh { name = name };
        m.CombineMeshes(parts.ToArray(), true, true);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial    = mat;
        r.shadowCastingMode = ShadowCastingMode.On;
        r.receiveShadows    = true;
        return go;
    }
}

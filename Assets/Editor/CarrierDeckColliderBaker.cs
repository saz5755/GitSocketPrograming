using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 항모 비행갑판의 실제 윤곽대로 걷기·착지용 콜라이더를 굽는다.
/// 기존 Ground 박스(갑판 외곽 직사각형 전체)는 갑판이 없는 바깥 공간까지 덮어, 갑판 밖으로 걸어 나가도 공중에 서 있게 된다.
///  1. 항모 렌더 메쉬에 임시 MeshCollider 를 붙이고 (메쉬가 CPU 읽기 불가라 콜라이더로 형상 조회)
///  2. 위에서 0.5m 격자로 레이를 쏴 갑판 높이(±0.6m)에 맞는 면이 있는 칸만 갑판으로 표시 (주기된 모델 아래도 갑판으로 인정)
///  3. 같은 행의 연속 칸 → 구간, 같은 구간이 이어지는 행끼리 합쳐 사각형으로 만들고 윗면·아랫면 메쉬를 생성
///  4. 메쉬 에셋으로 저장해 Ground 의 MeshCollider 에 넣고 박스 콜라이더는 끈다 (되돌리려면 박스를 다시 켜면 된다)
/// 항모 모델을 바꾸면 메뉴에서 다시 실행한다.
/// </summary>
public static class CarrierDeckColliderBaker
{
    const string MeshPath = "Assets/Models/AirCraftCarrier/CarrierDeckCollider.asset";
    const float Cell = 0.5f;
    const float Band = 0.6f;

    [MenuItem("Tools/Carrier/Bake Deck Collider")]
    public static void BakeMenu() => Debug.Log(Bake(save: true));

    public static string Bake(bool save)
    {
        var wire = Object.FindAnyObjectByType<ArrestingWireSystem>();
        var carrier = wire != null ? wire.transform.parent : null;
        if (carrier == null) return "항모(ArrestingWireSystem 부모)를 찾지 못함";
        var ground = carrier.Find("Ground");
        var box = ground != null ? ground.GetComponent<BoxCollider>() : null;
        if (ground == null || box == null) return "Ground 박스 콜라이더 없음";

        // 박스는 굽고 나면 꺼져 있어 bounds 가 0 — 중심·크기로 윗면 높이를 계산한다
        float deckY = ground.TransformPoint(box.center + Vector3.up * box.size.y * 0.5f).y;
        var temp = new List<MeshCollider>();
        foreach (var mf in carrier.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || mf.transform == ground || mf.GetComponent<Renderer>() == null) continue;
            if (mf.transform.IsChildOf(ground)) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            temp.Add(mc);
        }
        Physics.SyncTransforms();

        // 항모 로컬 격자 (렌더러 경계 + 여유)
        var rb = new Bounds(carrier.position, Vector3.zero);
        foreach (var r in carrier.GetComponentsInChildren<Renderer>()) if (!r.transform.IsChildOf(ground)) rb.Encapsulate(r.bounds);
        Vector3 lmin = carrier.InverseTransformPoint(rb.min), lmax = carrier.InverseTransformPoint(rb.max);
        float x0 = Mathf.Min(lmin.x, lmax.x) - 2f, x1 = Mathf.Max(lmin.x, lmax.x) + 2f;
        float z0 = Mathf.Min(lmin.z, lmax.z) - 2f, z1 = Mathf.Max(lmin.z, lmax.z) + 2f;
        int nx = Mathf.CeilToInt((x1 - x0) / Cell), nz = Mathf.CeilToInt((z1 - z0) / Cell);
        var deck = new bool[nx, nz];
        int deckCells = 0;
        try
        {
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    Vector3 o = carrier.TransformPoint(new Vector3(x0 + (ix + 0.5f) * Cell, 0f, z0 + (iz + 0.5f) * Cell));
                    o.y = deckY + 60f;
                    var ray = new Ray(o, Vector3.down);
                    foreach (var c in temp)
                    {
                        if (!c.bounds.IntersectRay(ray)) continue;
                        if (c.Raycast(ray, out var h, 120f) && Mathf.Abs(h.point.y - deckY) <= Band) { deck[ix, iz] = true; break; }
                        // 위쪽 면에 먼저 맞아도 그 아래 갑판 면을 찾도록 한 번 더 (갑판 바로 위에서 다시 쏨)
                        var low = new Ray(new Vector3(o.x, deckY + Band, o.z), Vector3.down);
                        if (c.Raycast(low, out h, Band * 2f + 0.1f)) { deck[ix, iz] = true; break; }
                    }
                    if (deck[ix, iz]) deckCells++;
                }
        }
        finally { foreach (var c in temp) Object.DestroyImmediate(c); }

        // 고립된 작은 조각(난간 끝 등) 제거: 이웃 칸이 2개 미만인 칸은 지운다
        for (int iz = 1; iz < nz - 1; iz++)
            for (int ix = 1; ix < nx - 1; ix++)
            {
                if (!deck[ix, iz]) continue;
                int nb = (deck[ix - 1, iz] ? 1 : 0) + (deck[ix + 1, iz] ? 1 : 0) + (deck[ix, iz - 1] ? 1 : 0) + (deck[ix, iz + 1] ? 1 : 0);
                if (nb < 2) deck[ix, iz] = false;
            }

        // 갑판에 둘러싸인 구멍(함교 바닥, 메쉬 이음매 틈)은 메운다 — 바깥에서 flood fill 로 닿지 않는 빈 칸
        var outside = new bool[nx, nz];
        var stack = new Stack<(int, int)>();
        for (int ix = 0; ix < nx; ix++) { stack.Push((ix, 0)); stack.Push((ix, nz - 1)); }
        for (int iz = 0; iz < nz; iz++) { stack.Push((0, iz)); stack.Push((nx - 1, iz)); }
        while (stack.Count > 0)
        {
            var (cx, cz) = stack.Pop();
            if (cx < 0 || cz < 0 || cx >= nx || cz >= nz || outside[cx, cz] || deck[cx, cz]) continue;
            outside[cx, cz] = true;
            stack.Push((cx + 1, cz)); stack.Push((cx - 1, cz)); stack.Push((cx, cz + 1)); stack.Push((cx, cz - 1));
        }
        int filled = 0;
        for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
                if (!deck[ix, iz] && !outside[ix, iz]) { deck[ix, iz] = true; filled++; }

        // 행별 구간 → 같은 구간이 이어지는 행끼리 사각형으로 병합
        var rects = new List<(int ix0, int ix1, int iz0, int iz1)>();
        var open = new Dictionary<(int, int), int>();   // (ix0, ix1) → rects 인덱스
        for (int iz = 0; iz < nz; iz++)
        {
            var runs = new List<(int, int)>();
            for (int ix = 0; ix < nx; ix++)
            {
                if (!deck[ix, iz]) continue;
                int s = ix; while (ix + 1 < nx && deck[ix + 1, iz]) ix++;
                runs.Add((s, ix));
            }
            var next = new Dictionary<(int, int), int>();
            foreach (var run in runs)
            {
                if (open.TryGetValue(run, out int ri)) { var r = rects[ri]; rects[ri] = (r.ix0, r.ix1, r.iz0, iz); next[run] = ri; }
                else { rects.Add((run.Item1, run.Item2, iz, iz)); next[run] = rects.Count - 1; }
            }
            open = next;
        }

        // 메쉬 (Ground 로컬 좌표 — Ground 는 비균등 스케일이라 월드 → 로컬 변환)
        var verts = new List<Vector3>(); var tris = new List<int>();
        const float Thick = 0.5f;
        foreach (var r in rects)
        {
            float ax = x0 + r.ix0 * Cell, bx = x0 + (r.ix1 + 1) * Cell, az = z0 + r.iz0 * Cell, bz = z0 + (r.iz1 + 1) * Cell;
            Vector3 L(float x, float y, float z) { Vector3 w = carrier.TransformPoint(new Vector3(x, 0f, z)); w.y = y; return ground.InverseTransformPoint(w); }
            int b = verts.Count;
            verts.Add(L(ax, deckY, az)); verts.Add(L(ax, deckY, bz)); verts.Add(L(bx, deckY, bz)); verts.Add(L(bx, deckY, az));
            verts.Add(L(ax, deckY - Thick, az)); verts.Add(L(ax, deckY - Thick, bz)); verts.Add(L(bx, deckY - Thick, bz)); verts.Add(L(bx, deckY - Thick, az));
            tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });           // 윗면
            tris.AddRange(new[] { b + 4, b + 6, b + 5, b + 4, b + 7, b + 6 });   // 아랫면
        }
        var mesh = new Mesh { name = "CarrierDeckCollider", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();

        string summary = $"갑판 칸 {deckCells} / {nx * nz} (0.5m), 메운 구멍 {filled}칸, 사각형 {rects.Count}, 삼각형 {tris.Count / 3}, 갑판 높이 {deckY:F2}";
        if (!save) return summary + "\n" + AsciiMap(deck, nx, nz);

        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (existing != null) { EditorUtility.CopySerialized(mesh, existing); mesh = existing; }
        else AssetDatabase.CreateAsset(mesh, MeshPath);
        AssetDatabase.SaveAssets();

        Undo.RecordObject(box, "Bake Deck Collider");
        box.enabled = false;
        var mcol = ground.GetComponent<MeshCollider>();   // 유니티 가짜 null 이라 ?? 로 받을 수 없다
        if (mcol == null) mcol = Undo.AddComponent<MeshCollider>(ground.gameObject);
        Undo.RecordObject(mcol, "Bake Deck Collider");
        mcol.sharedMesh = mesh;
        mcol.convex = false;
        EditorUtility.SetDirty(mcol);
        EditorSceneManager.MarkSceneDirty(ground.gameObject.scene);
        return summary + $"\n저장: {MeshPath} → Ground MeshCollider (BoxCollider 비활성)";
    }

    // 확인용 축소 지도 (2m 단위, # = 갑판)
    static string AsciiMap(bool[,] deck, int nx, int nz)
    {
        var sb = new System.Text.StringBuilder();
        for (int iz = nz - 1; iz >= 0; iz -= 8)
        {
            for (int ix = 0; ix < nx; ix += 4)
            {
                bool any = false;
                for (int dz = 0; dz < 8 && iz - dz >= 0 && !any; dz++)
                    for (int dx = 0; dx < 4 && ix + dx < nx && !any; dx++) any = deck[ix + dx, iz - dz];
                sb.Append(any ? '#' : '.');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

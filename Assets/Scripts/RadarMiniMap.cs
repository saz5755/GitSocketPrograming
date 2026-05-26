using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 레이더 미니맵 v2: TextMeshPro + 원형 스코프 + Image.Filled 스윕 아크
public class RadarMiniMap : MonoBehaviour
{
    // ── Color palette ─────────────────────────────────────────────────────────
    static readonly Color HG    = new(0.00f, 1.00f, 0.45f, 0.95f);  // 포스포 그린
    static readonly Color HGD   = new(0.00f, 0.90f, 0.40f, 0.75f);
    static readonly Color HGX   = new(0.00f, 0.75f, 0.35f, 0.42f);
    static readonly Color WARN  = new(1.00f, 0.82f, 0.00f, 0.95f);  // 락온 타겟 황색
    static readonly Color ENEMY = new(1.00f, 0.28f, 0.08f, 0.92f);  // 비락온 적기 적색
    static readonly Color MFD   = new(0.01f, 0.05f, 0.01f, 0.97f);  // MFD 배경
    static readonly Color SWEEP = new(0.00f, 1.00f, 0.45f, 0.16f);  // 스윕 아크

    const float Radius     = 110f;
    const float RadarRange = 20000f;
    const float SweepSpeed = 72f;    // 5초 1회전

    Canvas        _canvas;
    TMP_FontAsset _tmpFont;
    RectTransform _radarContent;
    RectTransform _sweepPivot;
    TextMeshProUGUI _selfIcon;

    readonly Dictionary<string, RectTransform>   _blipRoots  = new();
    readonly Dictionary<string, TextMeshProUGUI> _iconTexts  = new();
    readonly Dictionary<string, TextMeshProUGUI> _nickLabels = new();
    readonly Dictionary<string, Image>           _blipImgs   = new();

    PlayerController _local;
    TargetingSystem  _targeting;

    float _sweepAngle;
    float _iconTimer;
    const float IconInterval = 0.05f;

    Sprite _spFilled;   // 256px 채운 원
    Sprite _spSmall;    //  32px 채운 원 (블립 배경)
    Sprite _spRing;     // 256px 링

    // ── 초기화 ────────────────────────────────────────────────────────────────
    void Start()
    {
        _tmpFont  = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        _spFilled = MakeCircle(256, false);
        _spSmall  = MakeCircle(32,  false);
        _spRing   = MakeCircle(256, true);
        BuildCanvas();
        BuildScope();
    }

    void FindRefs()
    {
        foreach (var pc in FindObjectsOfType<PlayerController>())
            if (pc.isLocalPlayer) { _local = pc; break; }
        if (_local != null)
            _targeting = FindObjectOfType<TargetingSystem>();
    }

    void Update()
    {
        if (_local == null) { FindRefs(); return; }

        _sweepAngle = (_sweepAngle + SweepSpeed * Time.deltaTime) % 360f;
        _sweepPivot.localRotation = Quaternion.Euler(0f, 0f, -_sweepAngle);
        _selfIcon.rectTransform.localRotation =
            Quaternion.Euler(0f, 0f, -_local.transform.eulerAngles.y);

        _iconTimer += Time.deltaTime;
        if (_iconTimer >= IconInterval) { _iconTimer = 0f; UpdateIcons(); }
    }

    // ── 원형 스프라이트 생성 (PCM과 동일한 방식으로 코드에서 직접 합성) ──────────
    static Sprite MakeCircle(int size, bool ringOnly)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear };
        float r  = size * 0.5f - 1f;
        float cx = size * 0.5f, cy = size * 0.5f;
        float thick = r * 0.09f;   // 링 두께: 반경의 9%
        const float aa = 1.5f;     // 안티앨리어싱 픽셀 수
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - cx + .5f) * (x - cx + .5f) +
                                  (y - cy + .5f) * (y - cy + .5f));
            float a = ringOnly
                ? Mathf.Clamp01(Mathf.Min((d - (r - thick)) / aa, (r - d) / aa))
                : Mathf.Clamp01((r - d) / aa);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        tex.SetPixels32(px);
        tex.Apply(false);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    // ── Canvas ───────────────────────────────────────────────────────────────
    void BuildCanvas()
    {
        var go = new GameObject("Radar_Canvas");
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 15;
        var cs = go.AddComponent<CanvasScaler>();
        cs.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.matchWidthOrHeight  = 0.5f;
        go.AddComponent<GraphicRaycaster>();
    }

    // ── Scope 구성 ────────────────────────────────────────────────────────────
    void BuildScope()
    {
        float D = Radius * 2f;
        var center = new Vector2(780f, -430f);

        // 외부 글로우 헤일로
        CircleImg("Glow",   _canvas.transform, center, D + 38f,
                  new Color(HG.r, HG.g, HG.b, 0.07f), _spFilled);
        // 메인 어두운 배경
        CircleImg("BG",     _canvas.transform, center, D + 4f,
                  MFD, _spFilled);
        // 밝은 테두리 링
        CircleImg("Border", _canvas.transform, center, D + 10f,
                  new Color(HG.r, HG.g, HG.b, 0.88f), _spRing);

        // 헤더 / 탐지거리 레이블
        MkTMP(_canvas.transform, "RADAR",
              center + new Vector2(0f, Radius + 23f), new Vector2(D, 20f), 13, HGD);
        MkTMP(_canvas.transform, $"{RadarRange / 1000f:F0} km",
              center + new Vector2(0f, -(Radius + 19f)), new Vector2(D, 15f), 10, HGX);

        // 원형 마스크 (클리핑)
        var maskGO = new GameObject("RadarMask");
        maskGO.transform.SetParent(_canvas.transform, false);
        var maskRT = maskGO.AddComponent<RectTransform>();
        maskRT.anchorMin = maskRT.anchorMax = maskRT.pivot = new Vector2(0.5f, 0.5f);
        maskRT.anchoredPosition = center;
        maskRT.sizeDelta        = new Vector2(D, D);
        var maskImg = maskGO.AddComponent<Image>();
        maskImg.sprite = _spFilled;
        maskImg.color  = new Color(0f, 0f, 0f, 0.01f);
        maskGO.AddComponent<Mask>().showMaskGraphic = false;

        _radarContent = MkRect("Content", maskRT, Vector2.zero, new Vector2(D, D));

        // 거리 링 (3개)
        RangeRing(Radius * 0.50f, 0.18f);
        RangeRing(Radius * 0.75f, 0.11f);
        RangeRing(Radius,          0.30f);

        // 십자선
        PlainImg(MkRect("CH", _radarContent, Vector2.zero, new Vector2(D, 1f)),
                 new Color(HG.r, HG.g, HG.b, 0.12f));
        PlainImg(MkRect("CV", _radarContent, Vector2.zero, new Vector2(1f, D)),
                 new Color(HG.r, HG.g, HG.b, 0.12f));

        // 방위 레이블 (North-Up 고정)
        float lb = Radius - 15f;
        MkTMP(_radarContent, "N", new Vector2(0f,  lb), new Vector2(22f, 18f), 12, HGX);
        MkTMP(_radarContent, "S", new Vector2(0f, -lb), new Vector2(22f, 18f), 12, HGX);
        MkTMP(_radarContent, "E", new Vector2( lb, 0f), new Vector2(22f, 18f), 12, HGX);
        MkTMP(_radarContent, "W", new Vector2(-lb, 0f), new Vector2(22f, 18f), 12, HGX);

        // 스윕 피벗
        _sweepPivot = MkRect("SP", _radarContent, Vector2.zero, new Vector2(D, D));

        // 스윕 아크 (30° Image.Filled Radial360 — 잔광 효과)
        var arcGO = new GameObject("Arc");
        arcGO.transform.SetParent(_sweepPivot, false);
        var arcRT = arcGO.AddComponent<RectTransform>();
        arcRT.anchorMin = arcRT.anchorMax = arcRT.pivot = new Vector2(0.5f, 0.5f);
        arcRT.anchoredPosition = Vector2.zero;
        arcRT.sizeDelta        = new Vector2(D, D);
        var arcImg = arcGO.AddComponent<Image>();
        arcImg.sprite        = _spFilled;
        arcImg.type          = Image.Type.Filled;
        arcImg.fillMethod    = Image.FillMethod.Radial360;
        arcImg.fillOrigin    = (int)Image.Origin360.Top;
        arcImg.fillClockwise = true;
        arcImg.fillAmount    = 0.083f;   // ≈ 30°
        arcImg.color         = SWEEP;
        arcImg.raycastTarget = false;

        // 스윕 선 (리딩 엣지, 선명한 라인)
        var sl = MkRect("SL", _sweepPivot, Vector2.zero, new Vector2(1.5f, Radius));
        sl.pivot            = new Vector2(0.5f, 0f);
        sl.anchoredPosition = Vector2.zero;
        PlainImg(sl, new Color(HG.r, HG.g, HG.b, 0.85f));

        // 자신 아이콘 (중앙, heading에 따라 회전)
        _selfIcon = MkTMP(_radarContent, "▲", Vector2.zero, new Vector2(24f, 24f), 20, HG);
        _selfIcon.fontStyle = FontStyles.Bold;
    }

    void RangeRing(float r, float alpha)
    {
        var rt  = MkRect($"Ring{r:F0}", _radarContent, Vector2.zero, new Vector2(r * 2, r * 2));
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = _spRing;
        img.color  = new Color(HG.r, HG.g, HG.b, alpha);
        img.raycastTarget = false;
    }

    // ── 아이콘 갱신 (20Hz) ────────────────────────────────────────────────────
    void UpdateIcons()
    {
        string targetNick = _targeting?.Target?.nickname ?? "";
        var seen = new HashSet<string>();

        foreach (var pc in FindObjectsOfType<PlayerController>())
        {
            if (pc.isLocalPlayer) continue;
            string nick = pc.nickname;
            seen.Add(nick);

            Vector3 delta  = pc.transform.position - _local.transform.position;
            float   xzDist = new Vector2(delta.x, delta.z).magnitude;
            if (xzDist > RadarRange) { SetVisible(nick, false); continue; }

            float ix = delta.x / RadarRange * Radius;
            float iy = delta.z / RadarRange * Radius;

            bool  locked = nick == targetNick;
            Color col    = locked ? WARN : ENEMY;

            EnsureBlip(nick);

            var root = _blipRoots[nick];
            root.anchoredPosition = new Vector2(ix, iy);
            root.localRotation    = Quaternion.Euler(0f, 0f, -pc.transform.eulerAngles.y);

            _iconTexts[nick].color = col;
            _blipImgs[nick].color  = new Color(col.r, col.g, col.b, locked ? 0.40f : 0.22f);

            if (_nickLabels.TryGetValue(nick, out var lbl))
            {
                string alt = Mathf.Abs(delta.y) > 80f ? (delta.y > 0 ? " ▲" : " ▼") : "";
                lbl.text  = (nick.Length > 6 ? nick[..6] : nick) + alt;
                lbl.color = new Color(col.r, col.g, col.b, 0.72f);
            }

            SetVisible(nick, true);
        }

        foreach (var k in new List<string>(_blipRoots.Keys))
            if (!seen.Contains(k)) SetVisible(k, false);
    }

    // 적기 블립 최초 생성
    void EnsureBlip(string nick)
    {
        if (_blipRoots.ContainsKey(nick)) return;

        // 루트 컨테이너 (위치 이동 대상)
        var root = MkRect("B_" + nick, _radarContent, Vector2.zero, new Vector2(24f, 24f));
        _blipRoots[nick] = root;

        // 원형 글로우 배경
        var ci  = MkRect("C", root, Vector2.zero, new Vector2(24f, 24f));
        var img = ci.gameObject.AddComponent<Image>();
        img.sprite = _spSmall;
        img.color  = new Color(ENEMY.r, ENEMY.g, ENEMY.b, 0.22f);
        img.raycastTarget = false;
        _blipImgs[nick] = img;

        // 적기 아이콘 (◆ 다이아몬드 — 자기 ▲와 명확히 구분)
        var icon = MkTMP(root, "◆", Vector2.zero, new Vector2(20f, 20f), 17, ENEMY);
        icon.fontStyle = FontStyles.Bold;
        _iconTexts[nick] = icon;

        // 닉네임 레이블
        var lbl = MkTMP(root, "", new Vector2(0f, -17f), new Vector2(76f, 14f), 11, HGX);
        _nickLabels[nick] = lbl;
    }

    void SetVisible(string nick, bool on)
    {
        if (_blipRoots.TryGetValue(nick, out var rt)) rt.gameObject.SetActive(on);
    }

    // ── UI 헬퍼 ──────────────────────────────────────────────────────────────
    void CircleImg(string name, Transform parent, Vector2 pos, float size, Color color, Sprite sp)
    {
        var rt  = MkRect(name, parent, pos, new Vector2(size, size));
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sp;
        img.color  = color;
        img.raycastTarget = false;
    }

    RectTransform MkRect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        return rt;
    }

    void PlainImg(RectTransform rt, Color c)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color         = c;
        img.raycastTarget = false;
    }

    TextMeshProUGUI MkTMP(Transform parent, string content, Vector2 pos, Vector2 size,
                          float fs, Color color)
    {
        var go = new GameObject("T");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font                = _tmpFont;
        tmp.text                = content;
        tmp.fontSize            = fs;
        tmp.color               = color;
        tmp.alignment           = TextAlignmentOptions.Center;
        tmp.raycastTarget       = false;
        tmp.enableWordWrapping  = false;
        tmp.overflowMode        = TextOverflowModes.Overflow;
        return tmp;
    }

#if UNITY_EDITOR
    [ContextMenu("Spawn Test Enemy")]
    void SpawnTestEnemy()
    {
        if (_local == null) { Debug.LogWarning("[RadarMiniMap] Play 모드에서만 사용"); return; }
        var go = new GameObject("TestEnemy");
        go.transform.position = _local.transform.position + new Vector3(3000f, 0f, 5000f);
        var pc = go.AddComponent<PlayerController>();
        pc.nickname      = "TestPilot";
        pc.isLocalPlayer = false;
        Debug.Log("[RadarMiniMap] 테스트 적기 생성 (+3km E / +5km N)");
    }

    [ContextMenu("Remove Test Enemy")]
    void RemoveTestEnemy()
    {
        var go = GameObject.Find("TestEnemy");
        if (go != null) DestroyImmediate(go);
    }
#endif
}

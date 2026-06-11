using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 코크핏 MFD 패널 — 좌: 레이더 스코프(RadarMiniMap 동일 스타일), 우: 무장, 중앙: 비행 데이터
// FlightCamera.SetCockpit() 호출 시 SetVisible()로 표시/숨김
public class MFDController : MonoBehaviour
{
    // ── 패널 위치 (플레이어 로컬; 조종사 눈 Y=0.52, Z=2.45) ─────────────────────
    static readonly Vector3 LeftMFDPos   = new Vector3(0.0878f, 0.25784f, 2.8413f);
    static readonly Vector3 RightMFDPos  = new Vector3( 0.22f, 0.13f, 2.80f);
    static readonly Vector3 CenterMFDPos = new Vector3( 0.00f, 0.24f, 2.73f);

    const int   CanvasRes = 256;
    const float MFDSize   = 0.18f;   // 좌/우 MFD 실물 크기 (m)
    const float UFCWidth  = 0.22f;   // 중앙 UFC 가로
    const float UFCHeight = 0.08f;   // 중앙 UFC 세로

    // ── 레이더 상수 ──────────────────────────────────────────────────────────────
    const float Radius       = 100f;
    const float RadarRange   = 20000f;
    const float SweepSpeed   = 72f;      // 도/초 (5초 1회전)
    const float IconInterval = 0.05f;   // 20Hz 블립 갱신

    // ── RadarMiniMap 동일 색상 팔레트 ─────────────────────────────────────────
    static readonly Color HG    = new(0.00f, 1.00f, 0.45f, 0.95f);
    static readonly Color HGD   = new(0.00f, 0.90f, 0.40f, 0.75f);
    static readonly Color HGX   = new(0.00f, 0.75f, 0.35f, 0.42f);
    static readonly Color WARN  = new(1.00f, 0.82f, 0.00f, 0.95f);
    static readonly Color ENEMY = new(1.00f, 0.28f, 0.08f, 0.92f);
    static readonly Color MFDBg = new(0.01f, 0.05f, 0.01f, 0.97f);
    static readonly Color SWEEP = new(0.00f, 1.00f, 0.45f, 0.16f);

    // ── 참조 ────────────────────────────────────────────────────────────────────
    PlayerController _local;
    MissileLauncher  _missileLauncher;
    GunSystem        _gunSystem;
    TargetingSystem  _targeting;
    TMP_FontAsset    _tmpFont;

    // ── 스프라이트 (RadarMiniMap.MakeCircle과 동일 방식) ─────────────────────────
    Sprite _spFilled, _spSmall, _spRing;

    // ── 패널 루트 ────────────────────────────────────────────────────────────────
    Canvas _leftCanvas, _rightCanvas, _centerCanvas;

    // ── 좌측 MFD: 레이더 오브젝트 ────────────────────────────────────────────────
    RectTransform   _radarContent;
    RectTransform   _sweepPivot;
    TextMeshProUGUI _selfIcon;
    float _sweepAngle;
    float _iconTimer;

    readonly Dictionary<string, RectTransform>   _blipRoots  = new();
    readonly Dictionary<string, TextMeshProUGUI> _iconTexts  = new();
    readonly Dictionary<string, TextMeshProUGUI> _nickLabels = new();
    readonly Dictionary<string, Image>           _blipImgs   = new();

    // ── 우측 MFD: 무장 텍스트 ────────────────────────────────────────────────────
    TextMeshProUGUI _missileText, _gunText, _lockText;

    // ── 중앙 UFC: 비행 데이터 텍스트 ─────────────────────────────────────────────
    TextMeshProUGUI _speedText, _altText, _hdgText;

    float _dataTimer;
    const float DataInterval = 0.10f;
    bool  _initialized;

    // ═══════════════════════════════════════════════════════════════════════════
    // 초기화 (FlightCamera.BuildRendererLists에서 호출)
    // ═══════════════════════════════════════════════════════════════════════════
    public void Initialize(PlayerController pc)
    {
        _local           = pc;
        _missileLauncher = pc.GetComponent<MissileLauncher>();
        _targeting       = pc.GetComponent<TargetingSystem>();

        _tmpFont  = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        _spFilled = MakeCircle(256, false);
        _spSmall  = MakeCircle(32,  false);
        _spRing   = MakeCircle(256, true);

        BuildPanels();
        SetVisible(false);
        _initialized = true;
    }

    public void SetVisible(bool visible)
    {
        if (_leftCanvas   != null) _leftCanvas.gameObject.SetActive(visible);
        if (_rightCanvas  != null) _rightCanvas.gameObject.SetActive(visible);
        if (_centerCanvas != null) _centerCanvas.gameObject.SetActive(visible);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 업데이트
    // ═══════════════════════════════════════════════════════════════════════════
    void Update()
    {
        if (!_initialized || _local == null) return;

        // 스윕 매 프레임 (RadarMiniMap과 동일)
        _sweepAngle = (_sweepAngle + SweepSpeed * Time.deltaTime) % 360f;
        if (_sweepPivot != null)
            _sweepPivot.localRotation = Quaternion.Euler(0f, 0f, -_sweepAngle);

        // 블립 20Hz
        _iconTimer += Time.deltaTime;
        if (_iconTimer >= IconInterval) { _iconTimer = 0f; UpdateRadarIcons(); }

        // 무장·비행 10Hz
        _dataTimer += Time.deltaTime;
        if (_dataTimer >= DataInterval)
        {
            _dataTimer = 0f;
            if (_gunSystem == null) _gunSystem = FindFirstObjectByType<GunSystem>();
            UpdateWeapons();
            UpdateFlightData();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 패널 생성
    // ═══════════════════════════════════════════════════════════════════════════
    void BuildPanels()
    {
        Transform parent = _local.transform;

        _leftCanvas   = CreateMFDCanvas("MFD_Left",   parent, LeftMFDPos,   MFDSize,  MFDSize);
        _leftCanvas.transform.localRotation = Quaternion.Euler(7f, 0f, 0f);
        _rightCanvas  = CreateMFDCanvas("MFD_Right",  parent, RightMFDPos,  MFDSize,  MFDSize);
        _centerCanvas = CreateMFDCanvas("MFD_Center", parent, CenterMFDPos, UFCWidth, UFCHeight);

        BuildRadarScope(_leftCanvas.transform);
        BuildWeaponPanel(_rightCanvas.transform);
        BuildFlightPanel(_centerCanvas.transform);
    }

    Canvas CreateMFDCanvas(string name, Transform parent,
                           Vector3 localPos, float physW, float physH)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;  // 파일럿이 전방을 보면 패널 앞면이 보임

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;

        var rt = canvas.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(CanvasRes, CanvasRes);

        go.transform.localScale = new Vector3(/*physW / CanvasRes*/0.00022f, /*physH / CanvasRes*/0.00022f, 1f);

        // 배경
        var bg  = new GameObject("BG");
        bg.transform.SetParent(go.transform, false);
        var bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;
        var bgImg = bg.AddComponent<Image>();
        bgImg.color         = MFDBg;
        bgImg.raycastTarget = false;

        return canvas;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 좌측 MFD: 레이더 스코프 (RadarMiniMap.BuildScope 구조 동일)
    // ═══════════════════════════════════════════════════════════════════════════
    void BuildRadarScope(Transform root)
    {
        float D = Radius * 2f;
        Vector2 c = Vector2.zero;

        // 외부 글로우 헤일로
        CircleImg("Glow",   root, c, D + 16f, new Color(HG.r, HG.g, HG.b, 0.07f), _spFilled);
        // 메인 배경
        CircleImg("BG",     root, c, D + 2f,  MFDBg, _spFilled);
        // 테두리 링
        CircleImg("Border", root, c, D + 6f,  new Color(HG.r, HG.g, HG.b, 0.88f), _spRing);

        // 헤더·범위 레이블
        MkTMP(root, "MFD  TRK-UP",
              c + new Vector2(0f,  Radius + 14f), new Vector2(D, 16f), 9f,  HGD);
        MkTMP(root, $"{RadarRange / 1000f:F0} km",
              c + new Vector2(0f, -(Radius + 12f)), new Vector2(D, 12f), 7f, HGX);

        // 원형 마스크 (RadarMiniMap과 동일)
        var maskGO = new GameObject("RadarMask");
        maskGO.transform.SetParent(root, false);
        var maskRT = maskGO.AddComponent<RectTransform>();
        maskRT.anchorMin = maskRT.anchorMax = maskRT.pivot = new Vector2(0.5f, 0.5f);
        maskRT.anchoredPosition = c;
        maskRT.sizeDelta        = new Vector2(D, D);
        var maskImg = maskGO.AddComponent<Image>();
        maskImg.sprite = _spFilled;
        maskImg.color  = new Color(0f, 0f, 0f, 0.01f);
        maskGO.AddComponent<Mask>().showMaskGraphic = false;

        _radarContent = MkRect("Content", maskRT, Vector2.zero, new Vector2(D, D));

        // 거리 링 3단계
        RangeRing(Radius * 0.50f, 0.18f);
        RangeRing(Radius * 0.75f, 0.11f);
        RangeRing(Radius,          0.30f);

        // 십자선
        PlainImg(MkRect("CH", _radarContent, Vector2.zero, new Vector2(D, 1f)),
                 new Color(HG.r, HG.g, HG.b, 0.12f));
        PlainImg(MkRect("CV", _radarContent, Vector2.zero, new Vector2(1f, D)),
                 new Color(HG.r, HG.g, HG.b, 0.12f));

        // 방향 레이블 (Track-Up: 전방=위)
        float lb = Radius - 14f;
        MkTMP(_radarContent, "FWD", new Vector2(0f,  lb), new Vector2(34f, 16f), 8f, HGX);
        MkTMP(_radarContent, "AFT", new Vector2(0f, -lb), new Vector2(34f, 16f), 8f, HGX);
        MkTMP(_radarContent, "R",   new Vector2( lb, 0f), new Vector2(22f, 16f), 8f, HGX);
        MkTMP(_radarContent, "L",   new Vector2(-lb, 0f), new Vector2(22f, 16f), 8f, HGX);

        // 스윕 피벗
        _sweepPivot = MkRect("SP", _radarContent, Vector2.zero, new Vector2(D, D));

        // 스윕 아크 (Image.Filled Radial360 — RadarMiniMap과 동일)
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

        // 스윕 선 (리딩 엣지)
        var sl = MkRect("SL", _sweepPivot, Vector2.zero, new Vector2(1.5f, Radius));
        sl.pivot            = new Vector2(0.5f, 0f);
        sl.anchoredPosition = Vector2.zero;
        PlainImg(sl, new Color(HG.r, HG.g, HG.b, 0.85f));

        // 자신 아이콘 (▲, track-up이므로 항상 위를 향함)
        _selfIcon = MkTMP(_radarContent, "▲", Vector2.zero, new Vector2(24f, 24f), 18f, HG);
        _selfIcon.fontStyle = FontStyles.Bold;
    }

    void RangeRing(float r, float alpha)
    {
        var rt  = MkRect($"Ring{r:F0}", _radarContent, Vector2.zero, new Vector2(r * 2f, r * 2f));
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite        = _spRing;
        img.color         = new Color(HG.r, HG.g, HG.b, alpha);
        img.raycastTarget = false;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 우측 MFD: 무장 상태
    // ═══════════════════════════════════════════════════════════════════════════
    void BuildWeaponPanel(Transform root)
    {
        MkTMP(root, "WEAPONS", new Vector2(0f, 106f), new Vector2(230f, 18f), 11f, HGD);

        PlainImg(MkRect("Line", root, new Vector2(0f, 92f), new Vector2(220f, 1f)),
                 new Color(HG.r, HG.g, HG.b, 0.25f));

        MkTMP(root,  "AIM-120",  new Vector2(-50f,  62f), new Vector2(130f, 20f), 10f, HGX);
        _missileText = MkTMP(root, "4", new Vector2(72f, 62f), new Vector2(60f, 20f), 16f, HG);

        MkTMP(root,  "GUN",      new Vector2(-50f,  30f), new Vector2(90f,  20f), 10f, HGX);
        _gunText     = MkTMP(root, "500", new Vector2(72f, 30f), new Vector2(60f, 20f), 16f, HG);

        MkTMP(root,  "LOCK",     new Vector2(-50f,  -4f), new Vector2(90f,  20f), 10f, HGX);
        _lockText    = MkTMP(root, "--", new Vector2(72f, -4f), new Vector2(60f, 20f), 16f, HG);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 중앙 UFC: 비행 데이터
    // ═══════════════════════════════════════════════════════════════════════════
    void BuildFlightPanel(Transform root)
    {
        _speedText = MkTMP(root, "SPD  ---", new Vector2(-14f,  22f), new Vector2(230f, 20f), 12f, HG);
        _altText   = MkTMP(root, "ALT  ---", new Vector2(-14f,   0f), new Vector2(230f, 20f), 12f, HG);
        _hdgText   = MkTMP(root, "HDG  ---", new Vector2(-14f, -22f), new Vector2(230f, 20f), 12f, HG);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 레이더 아이콘 갱신 (RadarMiniMap.UpdateIcons 구조 동일, Track-Up 좌표계)
    // ═══════════════════════════════════════════════════════════════════════════
    void UpdateRadarIcons()
    {
        if (_local == null || _radarContent == null) return;
        string targetNick = _targeting?.Target?.nickname ?? "";
        var seen = new HashSet<string>();

        foreach (var pc in PlayerController.All)
        {
            if (pc == null || pc.isLocalPlayer) continue;
            string nick = pc.nickname;
            seen.Add(nick);

            // Track-Up: 플레이어 로컬 좌표계 변환 (전방 = 레이더 위쪽)
            Vector3 worldDelta = pc.transform.position - _local.transform.position;
            Vector3 local      = _local.transform.InverseTransformDirection(worldDelta);

            float xzDist = new Vector2(local.x, local.z).magnitude;
            if (xzDist > RadarRange) { SetBlipActive(nick, false); continue; }

            float ix = local.x / RadarRange * Radius;  // 우 = 레이더 우
            float iy = local.z / RadarRange * Radius;  // 전방 = 레이더 위

            bool  locked = nick == targetNick;
            Color col    = locked ? WARN : ENEMY;

            EnsureBlip(nick);
            var root = _blipRoots[nick];
            root.anchoredPosition = new Vector2(ix, iy);

            // 적기 기수 방향 (상대 헤딩)
            float relYaw = pc.transform.eulerAngles.y - _local.transform.eulerAngles.y;
            root.localRotation = Quaternion.Euler(0f, 0f, -relYaw);

            _iconTexts[nick].color = col;
            _blipImgs[nick].color  = new Color(col.r, col.g, col.b, locked ? 0.40f : 0.22f);

            if (_nickLabels.TryGetValue(nick, out var lbl))
            {
                string alt = Mathf.Abs(local.y) > 80f ? (local.y > 0f ? " ▲" : " ▼") : "";
                lbl.text  = (nick.Length > 6 ? nick[..6] : nick) + alt;
                lbl.color = new Color(col.r, col.g, col.b, 0.72f);
            }
            SetBlipActive(nick, true);
        }

        foreach (var k in new List<string>(_blipRoots.Keys))
            if (!seen.Contains(k)) SetBlipActive(k, false);
    }

    // 최초 등장 시 블립 UI 생성 (RadarMiniMap.EnsureBlip과 동일 구조)
    void EnsureBlip(string nick)
    {
        if (_blipRoots.ContainsKey(nick)) return;

        var root = MkRect("B_" + nick, _radarContent, Vector2.zero, new Vector2(24f, 24f));
        _blipRoots[nick] = root;

        var ci  = MkRect("C", root, Vector2.zero, new Vector2(24f, 24f));
        var img = ci.gameObject.AddComponent<Image>();
        img.sprite        = _spSmall;
        img.color         = new Color(ENEMY.r, ENEMY.g, ENEMY.b, 0.22f);
        img.raycastTarget = false;
        _blipImgs[nick]   = img;

        var icon = MkTMP(root, "◆", Vector2.zero, new Vector2(20f, 20f), 15f, ENEMY);
        icon.fontStyle    = FontStyles.Bold;
        _iconTexts[nick]  = icon;

        var lbl = MkTMP(root, "", new Vector2(0f, -17f), new Vector2(76f, 14f), 9f, HGX);
        _nickLabels[nick] = lbl;
    }

    void SetBlipActive(string nick, bool on)
    {
        if (_blipRoots.TryGetValue(nick, out var rt)) rt.gameObject.SetActive(on);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 무장 상태 갱신
    // ═══════════════════════════════════════════════════════════════════════════
    void UpdateWeapons()
    {
        if (_missileText != null && _missileLauncher != null)
        {
            int cnt = _missileLauncher.MissileCount;
            _missileText.text  = cnt > 0 ? cnt.ToString() : "EMPTY";
            _missileText.color = cnt > 0 ? HG : ENEMY;
        }

        if (_gunText != null && _gunSystem != null)
        {
            int ammo = _gunSystem.Ammo;
            _gunText.text  = ammo > 0 ? ammo.ToString("D3") : "EMPTY";
            _gunText.color = ammo > 50 ? HG : (ammo > 0 ? WARN : ENEMY);
        }

        if (_lockText != null && _targeting != null)
        {
            switch (_targeting.State)
            {
                case TargetingSystem.LockState.None:
                    _lockText.text  = "--";      _lockText.color = HGX;   break;
                case TargetingSystem.LockState.Searching:
                    _lockText.text  = "SRCH";   _lockText.color = WARN;  break;
                case TargetingSystem.LockState.Locked:
                    _lockText.text  = "LOCK";   _lockText.color = ENEMY; break;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 비행 데이터 갱신
    // ═══════════════════════════════════════════════════════════════════════════
    void UpdateFlightData()
    {
        if (_local == null) return;
        float kts = _local.CurrentSpeed * 1.944f;
        float ft  = _local.transform.position.y * 3.281f;
        float hdg = _local.transform.eulerAngles.y;

        if (_speedText != null) _speedText.text = $"SPD  {kts:F0}kts";
        if (_altText   != null) _altText.text   = $"ALT  {ft:F0}ft";
        if (_hdgText   != null) _hdgText.text   = $"HDG  {hdg:F0}°";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 스프라이트 생성 (RadarMiniMap.MakeCircle과 완전 동일)
    // ═══════════════════════════════════════════════════════════════════════════
    static Sprite MakeCircle(int size, bool ringOnly)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear };
        float r  = size * 0.5f - 1f;
        float cx = size * 0.5f, cy = size * 0.5f;
        float thick = r * 0.09f;
        const float aa = 1.5f;
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

    // ═══════════════════════════════════════════════════════════════════════════
    // UI 헬퍼 (RadarMiniMap와 동일 패턴)
    // ═══════════════════════════════════════════════════════════════════════════
    void CircleImg(string name, Transform parent, Vector2 pos, float size, Color color, Sprite sp)
    {
        var rt  = MkRect(name, parent, pos, new Vector2(size, size));
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite        = sp;
        img.color         = color;
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
        tmp.font               = _tmpFont;
        tmp.text               = content;
        tmp.fontSize           = fs;
        tmp.color              = color;
        tmp.alignment          = TextAlignmentOptions.Center;
        tmp.raycastTarget      = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode       = TextOverflowModes.Overflow;
        return tmp;
    }
}

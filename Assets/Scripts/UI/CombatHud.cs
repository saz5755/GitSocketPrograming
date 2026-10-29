using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 3인칭(체이스) 비행 시점의 전투 HUD — 디자인 시안 "전투 중 HUD":
/// 좌측 SPD / 우측 ALT 테이프, 중앙 크로스헤어, 좌상단 방위, 우하단 무장 카드, 우상단 소티 정보(레이더는 좌하단).
/// 콕핏 시점에서는 기존 콕핏 HUD(FlightHUD)가 대신하므로 숨는다.
/// 콕핏 HUD 와 같은 스프라이트 키트(Resources/HUD)·글래스 카드 스타일을 쓴다 (v4.2 U-1).
/// </summary>
public class CombatHud : MonoBehaviour
{
    static readonly Color HG   = new Color(0.20f, 1.00f, 0.55f, 0.95f);
    static readonly Color HGD  = new Color(0.20f, 1.00f, 0.55f, 0.62f);
    static readonly Color HGX  = new Color(0.20f, 1.00f, 0.55f, 0.28f);
    static readonly Color Ink  = new Color(0.90f, 1.00f, 0.95f, 0.96f);
    static readonly Color Glass = new Color(0.01f, 0.05f, 0.04f, 0.62f);
    static readonly Color Edge  = new Color(0.20f, 1.00f, 0.55f, 0.30f);
    static readonly Color Warn  = new Color(1.00f, 0.72f, 0.25f, 1f);
    static readonly Color Crit  = new Color(1.00f, 0.36f, 0.40f, 1f);

    const float TickPx = 22f;
    const int   TickCount = 15;

    Canvas _canvas;
    CanvasGroup _group;
    Font _font;

    PlayerController _local;
    FlightCamera _cam;
    GunSystem _gun;
    MissileLauncher _launcher;
    CountermeasureSystem _cms;
    float _refTimer;

    Vector3 _prevPos; bool _hasPrev;
    float _kph, _vvi;
    float _time;

    // 테이프
    RectTransform _spdTicks, _altTicks;
    Text[] _spdNums = new Text[7], _altNums = new Text[7];
    Text _spdVal, _altVal, _vviText, _hdgVal, _hdgDir, _timeText, _pilotText;
    Text _gunVal, _aimVal, _flareVal, _chaffVal;
    Image _gunBar;
    RectTransform _hdgTicks;
    Image[] _aimPips = new Image[6];
    Image[] _aim9Pips = new Image[4];
    Text _aim9Val, _aim9Label, _aimLabel, _cueText, _dlzText, _centerCue;
    Text _gVal, _thrVal, _warnText;

    void Start()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build();
        GunSystem.HideAmmoText = true;
        SetShown(false);
    }

    void OnDestroy() { GunSystem.HideAmmoText = false; }

    // ── 구성 ──────────────────────────────────────────────────────────────
    void Build()
    {
        var go = new GameObject("CombatHud_Canvas");
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 14;
        var cs = go.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.matchWidthOrHeight = 0.5f;
        _group = go.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false; _group.interactable = false;
        var root = go.transform;

        BuildTape(root, "SPD", "km/h", new Vector2(-800f, 20f), false, out _spdTicks, _spdNums, out _spdVal);
        BuildTape(root, "ALT", "m", new Vector2(800f, 20f), true, out _altTicks, _altNums, out _altVal);
        _vviText = Txt(root, "VVI +0", new Vector2(800f, -176f), new Vector2(150f, 22f), 13, HGD, TextAnchor.MiddleCenter);
        BuildCrosshair(root);
        BuildCenterCue(root);
        BuildHeading(root);
        BuildWeapons(root);
        BuildSortie(root);
        BuildFlightState(root);
    }

    RectTransform R(string n, Transform p, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(p, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    // radius > 0 이면 캡슐(9-slice pill) 스프라이트 — 콕핏 HUD 와 같은 모서리
    Image Box(RectTransform rt, Color c, int radius = 0)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c; img.raycastTarget = false;
        if (radius > 0) { img.sprite = HudSprites.Get(HudSprites.Pill); img.type = Image.Type.Sliced; }
        return img;
    }

    Image Spr(RectTransform rt, string sprite, Color c, Image.Type type = Image.Type.Simple)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = HudSprites.Get(sprite); img.type = type;
        img.color = c; img.raycastTarget = false;
        return img;
    }

    RectTransform Fill(string n, RectTransform p)
    {
        var rt = R(n, p, Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    Text Txt(Transform p, string s, Vector2 pos, Vector2 size, int fs, Color c, TextAnchor a, FontStyle st = FontStyle.Normal)
    {
        var rt = R("T", p, pos, size);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = _font; t.text = s; t.fontSize = fs; t.color = c; t.alignment = a; t.fontStyle = st;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        // 좌/우 정렬 텍스트는 pos 를 가장자리 기준으로 (카드 안쪽 여백 맞추기 쉽게)
        if (a == TextAnchor.MiddleLeft)  { rt.pivot = new Vector2(0f, 0.5f); }
        if (a == TextAnchor.MiddleRight) { rt.pivot = new Vector2(1f, 0.5f); }
        return t;
    }

    RectTransform Card(string n, Transform p, Vector2 pos, Vector2 size)
    {
        // 콕핏 HUD 글래스 카드: 챔퍼 면 + 상단 광택 + 헤어라인 테두리 + 코너 액센트
        var rt = R(n, p, pos, size);
        Spr(rt, HudSprites.Panel, Glass, Image.Type.Sliced);
        var sheen = Fill("Sheen", rt);
        sheen.offsetMin = new Vector2(2f, size.y * 0.45f); sheen.offsetMax = new Vector2(-2f, -2f);
        Spr(sheen, HudSprites.Sheen, new Color(HG.r, HG.g, HG.b, 0.05f));
        Spr(Fill("Line", rt), HudSprites.PanelLine, Edge, Image.Type.Sliced);
        Spr(Fill("Accent", rt), HudSprites.PanelAccent, new Color(HG.r, HG.g, HG.b, 0.75f), Image.Type.Sliced);
        return rt;
    }

    // ── SPD / ALT 테이프: 카드 + 스크롤 눈금 + 현재값 ────────────────────────
    void BuildTape(Transform root, string label, string unit, Vector2 pos, bool right,
                   out RectTransform ticks, Text[] nums, out Text value)
    {
        var card = Card(label + "Card", root, pos, new Vector2(150f, 340f));
        float side = right ? -1f : 1f;
        Txt(card, label, new Vector2(0f, 150f), new Vector2(130f, 22f), 15, HG, TextAnchor.MiddleCenter, FontStyle.Bold);

        // 눈금 영역 (마스크)
        var mask = R("Mask", card, new Vector2(-side * 38f, -8f), new Vector2(50f, TickPx * (TickCount - 1)));
        mask.gameObject.AddComponent<RectMask2D>();
        ticks = R("Ticks", mask, Vector2.zero, new Vector2(50f, TickPx * TickCount));
        for (int i = 0; i < TickCount; i++)
        {
            float y = (i - TickCount / 2) * TickPx;
            bool major = (i - TickCount / 2) % 2 == 0;
            var t = R("t", ticks, new Vector2(side * (major ? 14f : 18f), y), new Vector2(major ? 22f : 12f, major ? 2f : 1f));
            Box(t, major ? HGD : HGX);
        }
        // 숫자 (큰 눈금마다)
        for (int k = 0; k < nums.Length; k++)
            nums[k] = Txt(card, "0", new Vector2(side * 18f - side * 38f * 0f + (right ? -2f : 2f), 0f), new Vector2(60f, 18f), 11, HGD,
                          right ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
        foreach (var n in nums) n.gameObject.SetActive(false);   // 숫자 눈금은 단순화를 위해 값 박스만 사용

        // 현재값 박스
        var vb = R("Val", card, new Vector2(0f, -8f), new Vector2(124f, 40f));
        Spr(vb, HudSprites.ReadoutFill, new Color(0.01f, 0.05f, 0.03f, 0.85f), Image.Type.Sliced);
        Spr(Fill("Line", vb), HudSprites.Readout, new Color(HG.r, HG.g, HG.b, 0.85f), Image.Type.Sliced);
        value = Txt(vb, "0", Vector2.zero, new Vector2(118f, 40f), 26, Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
        // 포인터
        var ptr = R("Ptr", card, new Vector2(-side * 70f, -8f), new Vector2(12f, 12f));
        ptr.localRotation = Quaternion.Euler(0, 0, right ? 90f : -90f);
        Spr(ptr, HudSprites.Caret, HG);

        Txt(card, unit, new Vector2(0f, -150f), new Vector2(130f, 20f), 12, HGD, TextAnchor.MiddleCenter);
    }

    // ── 중앙 크로스헤어 ───────────────────────────────────────────────────
    void BuildCrosshair(Transform root)
    {
        var c = R("Crosshair", root, Vector2.zero, new Vector2(120f, 120f));
        var ring = R("Ring", c, Vector2.zero, new Vector2(46f, 46f));
        Spr(ring, HudSprites.RingThin, new Color(HG.r, HG.g, HG.b, 0.75f));
        Spr(R("dot", c, Vector2.zero, new Vector2(5f, 5f)), HudSprites.Disc, HG);
        Color tc = new Color(HG.r, HG.g, HG.b, 0.8f);
        Box(R("l", c, new Vector2(-42f, 0f), new Vector2(26f, 2f)), tc);
        Box(R("r", c, new Vector2(42f, 0f), new Vector2(26f, 2f)), tc);
        Box(R("u", c, new Vector2(0f, 40f), new Vector2(2f, 18f)), tc);
        Box(R("d", c, new Vector2(0f, -40f), new Vector2(2f, 18f)), tc);
        Box(R("ll", c, new Vector2(-58f, 0f), new Vector2(2f, 10f)), tc);
        Box(R("rr", c, new Vector2(58f, 0f), new Vector2(2f, 10f)), tc);
    }

    // ── 방위 (상단 중앙) ──────────────────────────────────────────────────
    void BuildHeading(Transform root)
    {
        var card = Card("HdgCard", root, new Vector2(0f, 470f), new Vector2(300f, 54f));
        var mask = R("Mask", card, new Vector2(0f, -12f), new Vector2(276f, 22f));
        mask.gameObject.AddComponent<RectMask2D>();
        _hdgTicks = R("Ticks", mask, Vector2.zero, new Vector2(900f, 22f));
        for (int i = -20; i <= 20; i++)
        {
            bool major = i % 5 == 0;
            Box(R("t", _hdgTicks, new Vector2(i * 14f, 0f), new Vector2(major ? 2f : 1f, major ? 12f : 6f)), major ? HGD : HGX);
        }
        _hdgVal = Txt(card, "000", new Vector2(-6f, 13f), new Vector2(80f, 24f), 20, Ink, TextAnchor.MiddleRight, FontStyle.Bold);
        _hdgDir = Txt(card, "N", new Vector2(8f, 13f), new Vector2(50f, 24f), 14, HG, TextAnchor.MiddleLeft, FontStyle.Bold);
        Box(R("mark", card, new Vector2(0f, -22f), new Vector2(2f, 8f)), HG);
    }

    // ── 우하단 무장 카드 ──────────────────────────────────────────────────
    void BuildWeapons(Transform root)
    {
        var card = Card("WeaponCard", root, new Vector2(760f, -372f), new Vector2(340f, 196f));
        Txt(card, "WEAPON", new Vector2(-154f, 80f), new Vector2(100f, 18f), 11, HGD, TextAnchor.MiddleLeft, FontStyle.Bold);
        Txt(card, "[R] SELECT", new Vector2(154f, 80f), new Vector2(120f, 18f), 10, HGX, TextAnchor.MiddleRight);
        Box(R("sep", card, new Vector2(0f, 68f), new Vector2(308f, 1f)), HGX);

        // GUN
        Txt(card, "GUN", new Vector2(-154f, 46f), new Vector2(70f, 22f), 14, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
        var bar = R("GunBarBG", card, new Vector2(-10f, 46f), new Vector2(120f, 6f));
        Box(bar, HGX, 3);
        var fill = R("GunBar", bar, Vector2.zero, new Vector2(120f, 6f));
        fill.anchorMin = new Vector2(0, 0.5f); fill.anchorMax = new Vector2(0, 0.5f); fill.pivot = new Vector2(0, 0.5f);
        fill.anchoredPosition = Vector2.zero;
        _gunBar = Box(fill, HG, 3);
        _gunVal = Txt(card, "500", new Vector2(154f, 46f), new Vector2(80f, 24f), 20, Ink, TextAnchor.MiddleRight, FontStyle.Bold);

        // AIM-9X / AIM-120 — 선택된 무장은 ▶ 표시 + 밝게
        _aim9Label = Txt(card, "AIM-9X", new Vector2(-154f, 16f), new Vector2(100f, 22f), 14, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
        for (int i = 0; i < _aim9Pips.Length; i++)
            _aim9Pips[i] = Box(R("pip9", card, new Vector2(-40f + i * 18f, 16f), new Vector2(12f, 7f)), HG, 3);
        _aim9Val = Txt(card, "2", new Vector2(154f, 16f), new Vector2(80f, 24f), 20, Ink, TextAnchor.MiddleRight, FontStyle.Bold);

        _aimLabel = Txt(card, "AIM-120", new Vector2(-154f, -14f), new Vector2(100f, 22f), 14, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
        for (int i = 0; i < _aimPips.Length; i++)
            _aimPips[i] = Box(R("pip", card, new Vector2(-40f + i * 18f, -14f), new Vector2(12f, 7f)), HG, 3);
        _aimVal = Txt(card, "4", new Vector2(154f, -14f), new Vector2(80f, 24f), 20, Ink, TextAnchor.MiddleRight, FontStyle.Bold);

        // CMS
        Txt(card, "FLARE", new Vector2(-154f, -46f), new Vector2(60f, 18f), 11, HGD, TextAnchor.MiddleLeft);
        _flareVal = Txt(card, "0", new Vector2(-98f, -46f), new Vector2(40f, 20f), 15, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
        Txt(card, "CHAFF", new Vector2(0f, -46f), new Vector2(60f, 18f), 11, HGD, TextAnchor.MiddleLeft);
        _chaffVal = Txt(card, "0", new Vector2(56f, -46f), new Vector2(40f, 20f), 15, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);

        // 발사 큐 + DLZ (표적 거리 / Rmax / Rne)
        Box(R("sep2", card, new Vector2(0f, -64f), new Vector2(308f, 1f)), HGX);
        _cueText = Txt(card, "", new Vector2(-154f, -80f), new Vector2(120f, 20f), 15, HG, TextAnchor.MiddleLeft, FontStyle.Bold);
        _dlzText = Txt(card, "", new Vector2(154f, -80f), new Vector2(200f, 20f), 12, HGD, TextAnchor.MiddleRight);
    }

    // 크로스헤어 아래 발사 큐 (체이스 시점에는 콕핏 TDB 가 없으므로)
    void BuildCenterCue(Transform root)
    {
        _centerCue = Txt(root, "", new Vector2(0f, -78f), new Vector2(300f, 24f), 17, HG, TextAnchor.MiddleCenter, FontStyle.Bold);
    }

    // ── 우상단 소티 정보 ──────────────────────────────────────────────────
    void BuildSortie(Transform root)
    {
        var card = Card("SortieCard", root, new Vector2(760f, 470f), new Vector2(340f, 64f));
        Txt(card, "SORTIE", new Vector2(-154f, 16f), new Vector2(90f, 18f), 11, HGD, TextAnchor.MiddleLeft, FontStyle.Bold);
        _timeText = Txt(card, "00:00:00", new Vector2(-154f, -10f), new Vector2(140f, 28f), 22, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
        Txt(card, "PILOTS", new Vector2(50f, 16f), new Vector2(80f, 18f), 11, HGD, TextAnchor.MiddleLeft, FontStyle.Bold);
        _pilotText = Txt(card, "1", new Vector2(50f, -10f), new Vector2(100f, 28f), 22, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
    }

    // ── G / 스로틀 / 경고 (SPD 카드 아래) ─────────────────────────────────
    void BuildFlightState(Transform root)
    {
        var card = Card("StateCard", root, new Vector2(-800f, -205f), new Vector2(150f, 62f));
        Txt(card, "G", new Vector2(-62f, 12f), new Vector2(30f, 18f), 11, HGD, TextAnchor.MiddleLeft, FontStyle.Bold);
        _gVal = Txt(card, "1.0", new Vector2(62f, 12f), new Vector2(80f, 22f), 18, Ink, TextAnchor.MiddleRight, FontStyle.Bold);
        Txt(card, "THR", new Vector2(-62f, -14f), new Vector2(40f, 18f), 11, HGD, TextAnchor.MiddleLeft, FontStyle.Bold);
        _thrVal = Txt(card, "IDLE", new Vector2(62f, -14f), new Vector2(80f, 22f), 15, Ink, TextAnchor.MiddleRight, FontStyle.Bold);
        _warnText = Txt(root, "", new Vector2(0f, -150f), new Vector2(520f, 30f), 22, Warn, TextAnchor.MiddleCenter, FontStyle.Bold);
    }

    // ── 갱신 ──────────────────────────────────────────────────────────────
    void SetShown(bool on)
    {
        if (_group == null) return;
        _group.alpha = Mathf.MoveTowards(_group.alpha, on ? 1f : 0f, Time.unscaledDeltaTime * 6f);
        if (_canvas != null && _canvas.enabled != (_group.alpha > 0.01f)) _canvas.enabled = _group.alpha > 0.01f;
    }

    void FindRefs()
    {
        foreach (var pc in PlayerController.All)
            if (pc != null && pc.isLocalPlayer) { _local = pc; break; }
        // 무장 시스템은 기체가 아니라 FlightHUD 오브젝트에 붙는다 (기체에서만 찾으면 항상 null → 패널이 자리표시 값 4/0/0 고정)
        if (_local != null)
        {
            _gun      = _local.GetComponentInChildren<GunSystem>();
            _launcher = _local.GetComponentInChildren<MissileLauncher>();
            _cms      = _local.GetComponentInChildren<CountermeasureSystem>();
        }
        if (_gun == null)      _gun      = FindAnyObjectByType<GunSystem>();
        if (_launcher == null) _launcher = FindAnyObjectByType<MissileLauncher>();
        if (_cms == null)      _cms      = FindAnyObjectByType<CountermeasureSystem>();
        if (_cam == null) _cam = FindAnyObjectByType<FlightCamera>();
    }

    void Update()
    {
        _refTimer -= Time.unscaledDeltaTime;
        if (_local == null || _cam == null || _refTimer < 0f && (_gun == null && _launcher == null))
        {
            if (_refTimer <= 0f) { FindRefs(); _refTimer = 1f; }
        }
        bool flying = GameModeManager.Instance != null && GameModeManager.Instance.IsFlying;
        bool show = _local != null && flying && (_cam == null || !_cam.IsCockpit);
        GunSystem.HideAmmoText = show;
        SetShown(show);
        if (!show) { _hasPrev = false; return; }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Transform t = _local.transform;
        if (!_hasPrev) { _prevPos = t.position; _hasPrev = true; }
        Vector3 vel = (t.position - _prevPos) / dt;
        _prevPos = t.position;
        _kph = Mathf.Lerp(_kph, vel.magnitude * 3.6f, dt * 8f);
        _vvi = Mathf.Lerp(_vvi, vel.y, dt * 6f);
        float alt = t.position.y;

        _spdVal.text = Mathf.RoundToInt(_kph).ToString("N0");
        _altVal.text = Mathf.RoundToInt(alt).ToString("N0");
        ScrollTicks(_spdTicks, _kph, 20f);
        ScrollTicks(_altTicks, alt, 100f);
        _vviText.text = $"VVI {(_vvi >= 0 ? "+" : "")}{_vvi:F0}";
        _vviText.color = Mathf.Abs(_vvi) > 30f ? Warn : HGD;

        float yaw = (t.eulerAngles.y % 360f + 360f) % 360f;
        _hdgVal.text = Mathf.RoundToInt(yaw).ToString("D3");
        _hdgDir.text = Compass(yaw);
        float step = 14f;   // 2도당 14px → 1도 7px
        _hdgTicks.anchoredPosition = new Vector2(-Mathf.Repeat(yaw, 10f) * (step / 2f) * 1f + 0f, 0f);

        if (_gun != null)
        {
            int ammo = _gun.Ammo, max = Mathf.Max(1, _gun.MaxAmmo);
            _gunVal.text = ammo > 0 ? ammo.ToString() : "EMPTY";
            _gunVal.color = ammo <= 50 ? Warn : Ink;
            _gunBar.rectTransform.sizeDelta = new Vector2(120f * Mathf.Clamp01(ammo / (float)max), 6f);
            _gunBar.color = ammo <= 50 ? Warn : HG;
        }
        if (_launcher != null) UpdateMissiles();
        if (_cms != null) { _flareVal.text = _cms.FlareRemaining.ToString(); _chaffVal.text = _cms.ChaffRemaining.ToString(); }

        float g = _local.PilotG;
        _gVal.text  = g.ToString("F1");
        _gVal.color = (g > 7f || g < -2f) ? Crit : (g > 4f || _local.GLimiterActive) ? Warn : Ink;
        _thrVal.text  = _local.AfterburnerActive ? "AB" : _local.Throttle01 < 0.02f ? "IDLE" : $"{_local.Throttle01 * 100f:F0}%";
        _thrVal.color = _local.AfterburnerActive ? Warn : Ink;
        var gloc = GLocSystem.Instance;
        string warn = ""; Color wc = Warn;
        if      (_local.IsCriticalDamage)               { warn = "CRITICAL DAMAGE"; wc = Crit; }
        else if (gloc != null && gloc.Stress > 0.55f)   { warn = "OVER G";          wc = Crit; }
        else if (_local.IsStalling)                     { warn = "STALL";           wc = Crit; }
        else if (_local.IsFuelEmpty)                    { warn = "FUEL EMPTY";      wc = Crit; }
        else if (_local.StallWarning)                   { warn = "STALL WARN"; }
        else if (_local.IsFuelLow)                      { warn = "BINGO FUEL"; }
        _warnText.text = warn; _warnText.color = wc;

        _time += Time.deltaTime;
        int ts = (int)_time;
        _timeText.text = $"{ts / 3600:D2}:{ts / 60 % 60:D2}:{ts % 60:D2}";
        _pilotText.text = PlayerController.All.Count.ToString();
    }

    void UpdateMissiles()
    {
        int m9 = _launcher.Aim9Count, m120 = _launcher.Aim120Count;
        bool sel9 = _launcher.Selected == MissileLauncher.Weapon.AIM9X;
        _aim9Val.text = m9.ToString();
        _aimVal.text  = m120.ToString();
        for (int i = 0; i < _aim9Pips.Length; i++) _aim9Pips[i].color = i < m9 ? (sel9 ? HG : HGD) : HGX;
        for (int i = 0; i < _aimPips.Length; i++)  _aimPips[i].color  = i < m120 ? (!sel9 ? HG : HGD) : HGX;
        _aim9Label.text  = sel9 ? "> AIM-9X" : "  AIM-9X";
        _aimLabel.text   = !sel9 ? "> AIM-120" : "  AIM-120";
        _aim9Label.color = sel9 ? Ink : HGD;
        _aimLabel.color  = !sel9 ? Ink : HGD;

        bool blink = Time.time % 0.5f < 0.25f;
        bool   urgent = false;
        string cue    = RoomModeSO.WeaponsEnabled ? _launcher.Cue(out urgent) : "SAFE";
        Color cc = urgent ? (blink ? Crit : Warn) : cue is "IN RNG" or "TONE" ? HG : cue is "SRCH" or "NO LOCK" or "SAFE" ? HGD : Warn;
        _cueText.text = cue; _cueText.color = cc;

        string dlz = "";
        if (_launcher.ShotTarget != null || _launcher.Range > 0f)
            dlz = $"{Km(_launcher.Range)}  Rmax {Km(_launcher.RMax)}  Rne {Km(_launcher.RNe)}";
        _dlzText.text = dlz;

        bool showCenter = cue is "SHOOT" or "IN RNG" or "TONE" or "OUT RNG" or "MIN RNG" or "STEER";
        _centerCue.text  = showCenter ? $"{_launcher.SelectedSpec.displayName}  {cue}" : "";
        _centerCue.color = cc;
    }

    static string Km(float m) => m >= 1000f ? $"{m / 1000f:F1}k" : $"{m:F0}";

    static void ScrollTicks(RectTransform ticks, float value, float step)
    {
        // 눈금 2칸 = step. 값이 커지면 눈금은 아래로 흐른다
        float frac = Mathf.Repeat(value, step) / step;
        ticks.anchoredPosition = new Vector2(0f, -frac * TickPx * 2f);
    }

    static string Compass(float yaw)
    {
        string[] n = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        return n[Mathf.RoundToInt(yaw / 45f) % 8];
    }
}

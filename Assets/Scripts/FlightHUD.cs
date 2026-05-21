using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Military-grade procedural HUD. Attach to any persistent GO in the GameScene.
/// </summary>
public class FlightHUD : MonoBehaviour
{
    // ── 색상 ──────────────────────────────────────────────────────────────────
    static readonly Color HG  = new Color(0.00f, 1.00f, 0.40f, 0.92f);   // HUD green
    static readonly Color HGD = new Color(0.00f, 1.00f, 0.40f, 0.45f);   // HUD green dim
    static readonly Color HGX = new Color(0.00f, 1.00f, 0.40f, 0.22f);   // HUD green ghost
    static readonly Color SKY = new Color(0.08f, 0.22f, 0.55f, 0.75f);
    static readonly Color GND = new Color(0.30f, 0.18f, 0.04f, 0.75f);
    static readonly Color WARN = new Color(1.00f, 0.80f, 0.00f, 0.95f);
    static readonly Color CRIT = new Color(1.00f, 0.20f, 0.10f, 0.95f);

    // ── 피치 래더 파라미터 ──────────────────────────────────────────────────
    static readonly int[] PitchMarks = { -60,-50,-40,-30,-20,-10,-5, 5,10,20,30,40,50,60 };
    const float PX_PER_DEG = 7f;

    // ── 테이프 파라미터 ────────────────────────────────────────────────────
    const int   SPD_LINES  = 9;
    const float SPD_STEP   = 20f;   // kph
    const float SPD_PX     = 34f;   // px between marks

    const int   ALT_LINES  = 9;
    const float ALT_STEP   = 100f;  // m
    const float ALT_PX     = 34f;

    const int   HDG_LINES  = 15;
    const float HDG_STEP   = 10f;   // degrees
    const float HDG_PX     = 44f;   // px between marks

    // ── 런타임 ────────────────────────────────────────────────────────────
    Canvas       hudCanvas;
    PlayerController localPlayer;
    Font         fnt;

    // Artificial horizon
    RectTransform ahRoot;
    RectTransform ahSlide;

    // Speed tape
    Text[]         spdLabels    = new Text[SPD_LINES];
    RectTransform[] spdLabelRTs = new RectTransform[SPD_LINES];
    Text           spdCurrent;
    Text           machText;
    RectTransform  throttleBar;

    // Alt tape
    Text[]         altLabels    = new Text[ALT_LINES];
    RectTransform[] altLabelRTs = new RectTransform[ALT_LINES];
    Text           altCurrent;
    Text           vsiText;

    // Heading tape
    Text[]         hdgLabels    = new Text[HDG_LINES];
    RectTransform[] hdgLabelRTs = new RectTransform[HDG_LINES];
    Text           hdgCurrent;

    // Status
    Text aoaText;
    Text gText;
    Text warnText;

    // G-force smoothing
    Vector3 prevVel;
    float   smoothG = 1f;
    Vector3 prevPos;
    bool    prevPosSet;

    // Throttle (read from PlayerController via reflection-free access)
    float throttleDisplay;

    void Start() => BuildHUD();

    void FindLocalPlayer()
    {
        foreach (var pc in FindObjectsOfType<PlayerController>())
            if (pc.isLocalPlayer) { localPlayer = pc; return; }
    }

    void Update()
    {
        if (localPlayer == null) { FindLocalPlayer(); return; }
        UpdateHUD();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  BUILD
    // ══════════════════════════════════════════════════════════════════════════
    void BuildHUD()
    {
        fnt = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var go = new GameObject("HUD_Canvas");
        hudCanvas = go.AddComponent<Canvas>();
        hudCanvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        hudCanvas.sortingOrder = 20;
        var sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight  = 0.5f;
        go.AddComponent<GraphicRaycaster>();

        BuildVignette();
        BuildArtificialHorizon();
        BuildSpeedTape();
        BuildAltTape();
        BuildHeadingStrip();
        BuildStatusStrip();
        BuildBoresight();
        BuildBankAngleArc();
    }

    // ── 비네트 ────────────────────────────────────────────────────────────────
    void BuildVignette()
    {
        var rt = MakeStretch("Vignette", hudCanvas.transform);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0.28f);
        img.raycastTarget = false;
    }

    // ── 인공 수평선 ───────────────────────────────────────────────────────────
    void BuildArtificialHorizon()
    {
        // 마스크 컨테이너
        var mask = Rect("AH_Mask", hudCanvas.transform, Vector2.zero, new Vector2(340, 340));
        var maskImg = mask.gameObject.AddComponent<Image>();
        maskImg.color = new Color(0, 0, 0, 0.01f);
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        // 회전 루트 (롤)
        ahRoot = Rect("AH_Root", mask, Vector2.zero, new Vector2(340, 340));

        // 슬라이드 (피치)
        ahSlide = Rect("AH_Slide", ahRoot, Vector2.zero, new Vector2(340, 800));

        // 하늘
        var sky = Rect("Sky", ahSlide, new Vector2(0,  300), new Vector2(340, 600));
        Img(sky, SKY);
        // 대지
        var gnd = Rect("Gnd", ahSlide, new Vector2(0, -300), new Vector2(340, 600));
        Img(gnd, GND);

        // 수평선
        var hl = Rect("HL", ahSlide, Vector2.zero, new Vector2(340, 2));
        Img(hl, HG);

        // 피치 래더
        var ladder = Rect("Ladder", ahSlide, Vector2.zero, new Vector2(340, 800));
        foreach (int deg in PitchMarks)
        {
            float y = deg * PX_PER_DEG;
            bool major = Mathf.Abs(deg) % 10 == 0;
            float w = major ? 140 : 70;

            var bar = Rect($"P{deg}", ladder, new Vector2(0, y), new Vector2(w, 2));
            Img(bar, major ? HGD : HGX);

            if (major)
            {
                string lbl = deg > 0 ? $"+{deg}" : $"{deg}";
                AddTxt(ladder, lbl, new Vector2(-88, y), new Vector2(70, 20), 10, HGD, TextAnchor.MiddleRight);
                AddTxt(ladder, lbl, new Vector2( 88, y), new Vector2(70, 20), 10, HGD, TextAnchor.MiddleLeft);
            }
        }

        // 윙 마커 (고정, ahRoot에 부착)
        BuildWingMarkers();

        // AH 테두리 (원형 아웃라인 모사)
        var border = Rect("AH_Border", hudCanvas.transform, Vector2.zero, new Vector2(344, 344));
        var bImg   = border.gameObject.AddComponent<Image>();
        bImg.color = Color.clear;
        var ol     = border.gameObject.AddComponent<Outline>();
        ol.effectColor    = new Color(HG.r, HG.g, HG.b, 0.5f);
        ol.effectDistance = new Vector2(2, 2);
    }

    void BuildWingMarkers()
    {
        // 중앙 삼각형 (피치 지시 삼각형)
        var tri = Rect("WingTri", ahRoot, Vector2.zero, new Vector2(16, 16));
        Img(tri, HG);

        // 왼쪽 윙
        var wL = Rect("WingL", ahRoot, new Vector2(-90, 0), new Vector2(70, 3));
        Img(wL, HG);
        var wLtip = Rect("WingLTip", ahRoot, new Vector2(-125, 0), new Vector2(8, 8));
        Img(wLtip, HG);

        // 오른쪽 윙
        var wR = Rect("WingR", ahRoot, new Vector2( 90, 0), new Vector2(70, 3));
        Img(wR, HG);
        var wRtip = Rect("WingRTip", ahRoot, new Vector2( 125, 0), new Vector2(8, 8));
        Img(wRtip, HG);
    }

    // ── 뱅크 앵글 호 ──────────────────────────────────────────────────────────
    void BuildBankAngleArc()
    {
        int[] marks = { -60,-45,-30,-20,-10, 0, 10, 20, 30, 45, 60 };
        float radius = 195f;

        foreach (int deg in marks)
        {
            float rad = (90 - deg) * Mathf.Deg2Rad;
            float x = radius * Mathf.Cos(rad);
            float y = radius * Mathf.Sin(rad);

            float tickH = (deg % 30 == 0) ? 14f : 8f;
            var tick = Rect($"BAnk{deg}", hudCanvas.transform, new Vector2(x, y), new Vector2(2, tickH));
            tick.localRotation = Quaternion.Euler(0, 0, -deg);
            Img(tick, HGD);

            if (deg % 30 == 0 && deg != 0)
                AddTxt(hudCanvas.transform, $"{Mathf.Abs(deg)}",
                    new Vector2(x * 1.12f, y * 1.12f), new Vector2(32, 18), 9, HGD, TextAnchor.MiddleCenter);
        }
    }

    // ── 속도 테이프 (왼쪽) ───────────────────────────────────────────────────
    void BuildSpeedTape()
    {
        float px = -780f;

        // 배경 패널
        var panel = Rect("SpdPanel", hudCanvas.transform, new Vector2(px, 0), new Vector2(130, SPD_LINES * SPD_PX + 20));
        Img(panel, new Color(0, 0, 0, 0.55f));

        // 레이블 (마스크 안 스크롤)
        var maskRT = Rect("SpdMask", panel, Vector2.zero, new Vector2(110, SPD_LINES * SPD_PX));
        var mImg = maskRT.gameObject.AddComponent<Image>();
        mImg.color = new Color(0, 0, 0, 0.01f);
        maskRT.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        var scrollArea = Rect("SpdScroll", maskRT, Vector2.zero, new Vector2(110, SPD_LINES * SPD_PX));

        for (int i = 0; i < SPD_LINES; i++)
        {
            float y = (SPD_LINES / 2 - i) * SPD_PX;
            var lbl = AddTxt(scrollArea, "0",
                new Vector2(0, y), new Vector2(100, SPD_PX - 4), 13,
                i == SPD_LINES / 2 ? HG : HGD, TextAnchor.MiddleRight);
            spdLabels[i] = lbl;
            spdLabelRTs[i] = lbl.rectTransform;

            // 눈금 선
            var tick = Rect($"SpdTick{i}", scrollArea, new Vector2(52, y), new Vector2(12, 1));
            Img(tick, i == SPD_LINES / 2 ? HG : HGX);
        }

        // 현재값 하이라이트 박스
        var curBox = Rect("SpdCurBox", panel, Vector2.zero, new Vector2(130, 32));
        Img(curBox, new Color(HG.r * 0.15f, HG.g * 0.15f, HG.b * 0.15f, 0.6f));
        var curBorder = Rect("SpdCurBorder", panel, Vector2.zero, new Vector2(130, 32));
        var cbo = curBorder.gameObject.AddComponent<Outline>();
        cbo.effectColor    = new Color(HG.r, HG.g, HG.b, 0.8f);
        cbo.effectDistance = new Vector2(1, 1);
        curBorder.gameObject.AddComponent<Image>().color = Color.clear;

        spdCurrent = AddTxt(panel, "0", Vector2.zero, new Vector2(110, 32), 20, HG, TextAnchor.MiddleRight);

        // 포인터 삼각형
        AddTxt(panel, "◀", new Vector2(60, 0), new Vector2(24, 32), 14, HG, TextAnchor.MiddleLeft);

        // 헤더
        AddTxt(panel, "IAS", new Vector2(0, (SPD_LINES * SPD_PX / 2f) + 14),
            new Vector2(110, 22), 10, HGD, TextAnchor.MiddleCenter);
        AddTxt(panel, "KPH", new Vector2(0, -(SPD_LINES * SPD_PX / 2f) - 14),
            new Vector2(110, 22), 10, HGD, TextAnchor.MiddleCenter);

        // MACH 표시
        var machPanel = Rect("MachPanel", hudCanvas.transform,
            new Vector2(px, -(SPD_LINES * SPD_PX / 2f + 52)), new Vector2(130, 32));
        Img(machPanel, new Color(0, 0, 0, 0.45f));
        machText = AddTxt(machPanel, "M 0.00", Vector2.zero, new Vector2(110, 32), 12, HGD, TextAnchor.MiddleCenter);

        // 스로틀 바
        BuildThrottleBar(px - 22);
    }

    void BuildThrottleBar(float px)
    {
        var panel = Rect("ThrPanel", hudCanvas.transform, new Vector2(px, 0), new Vector2(14, SPD_LINES * SPD_PX + 20));
        Img(panel, new Color(0, 0, 0, 0.45f));

        AddTxt(panel, "THR", new Vector2(0, (SPD_LINES * SPD_PX / 2f) + 14),
            new Vector2(40, 20), 8, HGD, TextAnchor.MiddleCenter);

        // 바 배경
        var barBG = Rect("ThrBG", panel, new Vector2(0, 0), new Vector2(8, SPD_LINES * SPD_PX));
        Img(barBG, new Color(HG.r * 0.08f, HG.g * 0.08f, HG.b * 0.08f, 0.8f));

        // 실제 채워지는 부분
        throttleBar = Rect("ThrFill", panel, new Vector2(0, -(SPD_LINES * SPD_PX / 2f)), new Vector2(8, 0));
        throttleBar.pivot = new Vector2(0.5f, 0);
        Img(throttleBar, HG);
    }

    // ── 고도 테이프 (오른쪽) ─────────────────────────────────────────────────
    void BuildAltTape()
    {
        float px = 780f;

        var panel = Rect("AltPanel", hudCanvas.transform, new Vector2(px, 0), new Vector2(130, ALT_LINES * ALT_PX + 20));
        Img(panel, new Color(0, 0, 0, 0.55f));

        var maskRT = Rect("AltMask", panel, Vector2.zero, new Vector2(110, ALT_LINES * ALT_PX));
        var mImg = maskRT.gameObject.AddComponent<Image>();
        mImg.color = new Color(0, 0, 0, 0.01f);
        maskRT.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        var scrollArea = Rect("AltScroll", maskRT, Vector2.zero, new Vector2(110, ALT_LINES * ALT_PX));

        for (int i = 0; i < ALT_LINES; i++)
        {
            float y = (ALT_LINES / 2 - i) * ALT_PX;
            var lbl = AddTxt(scrollArea, "0",
                new Vector2(0, y), new Vector2(100, ALT_PX - 4), 13,
                i == ALT_LINES / 2 ? HG : HGD, TextAnchor.MiddleLeft);
            altLabels[i] = lbl;
            altLabelRTs[i] = lbl.rectTransform;

            var tick = Rect($"AltTick{i}", scrollArea, new Vector2(-52, y), new Vector2(12, 1));
            Img(tick, i == ALT_LINES / 2 ? HG : HGX);
        }

        var curBox = Rect("AltCurBox", panel, Vector2.zero, new Vector2(130, 32));
        Img(curBox, new Color(HG.r * 0.15f, HG.g * 0.15f, HG.b * 0.15f, 0.6f));
        var cbo = curBox.gameObject.AddComponent<Outline>();
        cbo.effectColor = new Color(HG.r, HG.g, HG.b, 0.8f);
        cbo.effectDistance = new Vector2(1, 1);

        altCurrent = AddTxt(panel, "0", Vector2.zero, new Vector2(110, 32), 20, HG, TextAnchor.MiddleLeft);
        AddTxt(panel, "▶", new Vector2(-54, 0), new Vector2(24, 32), 14, HG, TextAnchor.MiddleRight);

        AddTxt(panel, "ALT", new Vector2(0, (ALT_LINES * ALT_PX / 2f) + 14),
            new Vector2(110, 22), 10, HGD, TextAnchor.MiddleCenter);
        AddTxt(panel, "MSL m", new Vector2(0, -(ALT_LINES * ALT_PX / 2f) - 14),
            new Vector2(110, 22), 10, HGD, TextAnchor.MiddleCenter);

        var vsiPanel = Rect("VSIPanel", hudCanvas.transform,
            new Vector2(px, -(ALT_LINES * ALT_PX / 2f + 52)), new Vector2(130, 32));
        Img(vsiPanel, new Color(0, 0, 0, 0.45f));
        vsiText = AddTxt(vsiPanel, "VVI  +0", Vector2.zero, new Vector2(120, 32), 12, HGD, TextAnchor.MiddleCenter);
    }

    // ── 헤딩 스트립 (상단) ────────────────────────────────────────────────────
    void BuildHeadingStrip()
    {
        var panel = Rect("HdgPanel", hudCanvas.transform, new Vector2(0, 465), new Vector2(HDG_LINES * HDG_PX + 20, 52));
        Img(panel, new Color(0, 0, 0, 0.55f));

        var maskRT = Rect("HdgMask", panel, Vector2.zero, new Vector2(HDG_LINES * HDG_PX, 44));
        var mImg = maskRT.gameObject.AddComponent<Image>();
        mImg.color = new Color(0, 0, 0, 0.01f);
        maskRT.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        var scrollArea = Rect("HdgScroll", maskRT, Vector2.zero, new Vector2(HDG_LINES * HDG_PX, 44));

        for (int i = 0; i < HDG_LINES; i++)
        {
            float x = (i - HDG_LINES / 2) * HDG_PX;
            var lbl = AddTxt(scrollArea, "N",
                new Vector2(x, 6), new Vector2(HDG_PX - 4, 20), 11,
                HGD, TextAnchor.MiddleCenter);
            hdgLabels[i] = lbl;
            hdgLabelRTs[i] = lbl.rectTransform;

            var tick = Rect($"HdgTick{i}", scrollArea, new Vector2(x, -14), new Vector2(1, 8));
            Img(tick, HGX);
        }

        // 중앙 포인터
        var ptr = Rect("HdgPtr", panel, new Vector2(0, -20), new Vector2(2, 12));
        Img(ptr, HG);

        // 현재 헤딩 박스
        var curBox = Rect("HdgCurBox", panel, new Vector2(0, -6), new Vector2(70, 26));
        Img(curBox, new Color(HG.r * 0.15f, HG.g * 0.15f, HG.b * 0.15f, 0.7f));
        var cbo = curBox.gameObject.AddComponent<Outline>();
        cbo.effectColor = new Color(HG.r, HG.g, HG.b, 0.6f);
        cbo.effectDistance = new Vector2(1, 1);

        hdgCurrent = AddTxt(panel, "HDG 000°", new Vector2(0, -6), new Vector2(64, 24), 13, HG, TextAnchor.MiddleCenter);
    }

    // ── 상태 스트립 (하단) ────────────────────────────────────────────────────
    void BuildStatusStrip()
    {
        var panel = Rect("StatusPanel", hudCanvas.transform, new Vector2(0, -465), new Vector2(560, 38));
        Img(panel, new Color(0, 0, 0, 0.50f));
        var bdr = panel.gameObject.AddComponent<Outline>();
        bdr.effectColor    = new Color(HG.r, HG.g, HG.b, 0.25f);
        bdr.effectDistance = new Vector2(1, 1);

        aoaText  = AddTxt(panel, "AOA  +0.0°",
            new Vector2(-185, 0), new Vector2(160, 38), 12, HGD, TextAnchor.MiddleCenter);
        gText    = AddTxt(panel, "G  1.0",
            new Vector2(0, 0), new Vector2(110, 38), 14, HG, TextAnchor.MiddleCenter);
        AddTxt(panel, "SAFE", new Vector2(185, 0), new Vector2(100, 38), 11, HGD, TextAnchor.MiddleCenter);

        // 경고 텍스트 (중앙 상단)
        var warnPanel = Rect("WarnPanel", hudCanvas.transform, new Vector2(0, 380), new Vector2(320, 36));
        Img(warnPanel, new Color(0, 0, 0, 0));
        warnText = AddTxt(warnPanel, "", Vector2.zero, new Vector2(320, 36), 16, WARN, TextAnchor.MiddleCenter);
    }

    // ── 조준점 ────────────────────────────────────────────────────────────────
    void BuildBoresight()
    {
        Color c = new Color(HG.r, HG.g, HG.b, 0.75f);

        // 중앙 원 (조준 서클)
        var ctr = Rect("BSCircle", hudCanvas.transform, Vector2.zero, new Vector2(18, 18));
        var cImg = ctr.gameObject.AddComponent<Image>();
        cImg.color = Color.clear;
        var cOl = ctr.gameObject.AddComponent<Outline>();
        cOl.effectColor    = c;
        cOl.effectDistance = new Vector2(2, 2);

        // 수평선
        var h1 = Rect("BS_HL", hudCanvas.transform, new Vector2(-32, 0), new Vector2(20, 2));
        Img(h1, c);
        var h2 = Rect("BS_HR", hudCanvas.transform, new Vector2( 32, 0), new Vector2(20, 2));
        Img(h2, c);

        // 수직선
        var v1 = Rect("BS_VU", hudCanvas.transform, new Vector2(0,  32), new Vector2(2, 20));
        Img(v1, c);
        var v2 = Rect("BS_VD", hudCanvas.transform, new Vector2(0, -32), new Vector2(2, 20));
        Img(v2, c);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  UPDATE
    // ══════════════════════════════════════════════════════════════════════════
    void UpdateHUD()
    {
        Transform t  = localPlayer.transform;
        float dt = Time.deltaTime;

        if (!prevPosSet) { prevPos = t.position; prevPosSet = true; }

        // ── 자세 계산 ────────────────────────────────────────────────────────
        Vector3 euler = t.eulerAngles;
        float pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        float roll  = euler.z > 180f ? euler.z - 360f : euler.z;
        float yaw   = euler.y;

        // ── 속도 계산 ────────────────────────────────────────────────────────
        Vector3 vel  = (t.position - prevPos) / dt;
        float   kph  = vel.magnitude * 3.6f;
        float   mach = vel.magnitude / 343f;

        // ── G-force ──────────────────────────────────────────────────────────
        Vector3 accel = dt > 0f ? (vel - prevVel) / dt : Vector3.zero;
        float g = (accel - Physics.gravity).magnitude / 9.81f;
        smoothG = Mathf.Lerp(smoothG, g, dt * 4f);

        // ── AoA (간략화: vel와 forward의 각도) ──────────────────────────────
        float aoa = 0f;
        if (vel.magnitude > 0.5f)
            aoa = Vector3.SignedAngle(
                Vector3.ProjectOnPlane(vel.normalized, t.right),
                t.forward, t.right);

        prevVel = vel; prevPos = t.position;

        // ── 인공 수평선 ──────────────────────────────────────────────────────
        ahRoot.localRotation     = Quaternion.Euler(0, 0, roll);
        ahSlide.anchoredPosition = new Vector2(0, pitch * PX_PER_DEG);

        // ── 속도 테이프 ──────────────────────────────────────────────────────
        UpdateTape(spdLabels, spdLabelRTs, kph, SPD_STEP, SPD_PX, SPD_LINES, "F0", false);
        spdCurrent.text = $"{kph:F0}";
        machText.text   = $"M  {mach:F2}";

        // 스로틀 바 (PlayerController 필드 직접 접근 대신 추정치 사용)
        float thr = Mathf.Clamp01(kph / (80f * 3.6f)); // 최대속도 기준 비율
        if (throttleBar != null)
            throttleBar.sizeDelta = new Vector2(8, thr * (SPD_LINES * SPD_PX));

        // ── 고도 테이프 ──────────────────────────────────────────────────────
        UpdateTape(altLabels, altLabelRTs, t.position.y, ALT_STEP, ALT_PX, ALT_LINES, "F0", true);
        altCurrent.text = $"{t.position.y:F0}";

        float vvi = vel.y;
        string vviSign = vvi >= 0 ? "+" : "";
        vsiText.text = $"VVI  {vviSign}{vvi:F0} m/s";
        vsiText.color = Mathf.Abs(vvi) > 30f ? WARN : HGD;

        // ── 헤딩 스트립 ──────────────────────────────────────────────────────
        UpdateHeadingTape(yaw);
        hdgCurrent.text = HeadingStr(yaw);

        // ── 상태 스트립 ──────────────────────────────────────────────────────
        aoaText.text  = $"AOA  {(aoa >= 0 ? "+" : "")}{aoa:F1}°";
        gText.text    = $"G  {smoothG:F1}";
        gText.color   = smoothG > 7f ? CRIT : smoothG > 4f ? WARN : HG;

        // 경고
        bool stallWarn = aoa > 18f || kph < 50f;
        bool gWarn     = smoothG > 7f;
        warnText.text  = gWarn ? "⚠  G-LOCK WARNING" : stallWarn ? "⚠  STALL" : "";
        warnText.color = gWarn ? CRIT : WARN;
    }

    void UpdateTape(Text[] labels, RectTransform[] rts, float val,
                    float step, float px, int count, string fmt, bool leftAlign)
    {
        int half    = count / 2;
        int baseval = Mathf.FloorToInt(val / step) * (int)step;
        float frac  = (val - baseval) / step;

        for (int i = 0; i < count; i++)
        {
            int v = baseval + (half - i) * (int)step;
            labels[i].text = v >= 0 ? v.ToString(fmt) : "";
            float y = (half - i) * px + frac * px;
            rts[i].anchoredPosition = new Vector2(rts[i].anchoredPosition.x, y);
            labels[i].color = i == half ? HG : HGD;
            labels[i].fontSize = i == half ? 15 : 12;
        }
    }

    void UpdateHeadingTape(float yaw)
    {
        int half    = HDG_LINES / 2;
        int baseHdg = Mathf.FloorToInt(yaw / HDG_STEP) * (int)HDG_STEP;
        float frac  = (yaw - baseHdg) / HDG_STEP;

        for (int i = 0; i < HDG_LINES; i++)
        {
            int v   = ((baseHdg + (i - half) * (int)HDG_STEP) % 360 + 360) % 360;
            hdgLabels[i].text  = HeadingLabel(v);
            float x = (i - half) * HDG_PX - frac * HDG_PX;
            hdgLabelRTs[i].anchoredPosition = new Vector2(x, hdgLabelRTs[i].anchoredPosition.y);

            bool cardinal = v == 0 || v == 90 || v == 180 || v == 270;
            hdgLabels[i].color     = i == half ? HG : cardinal ? HG : HGD;
            hdgLabels[i].fontStyle = cardinal ? FontStyle.Bold : FontStyle.Normal;
        }
    }

    static string HeadingLabel(int deg)
    {
        return deg switch { 0 => "N", 90 => "E", 180 => "S", 270 => "W",
            _ => deg % 30 == 0 ? (deg / 10).ToString() : "·" };
    }

    static string HeadingStr(float yaw)
    {
        int h = ((int)yaw % 360 + 360) % 360;
        string dir = h switch {
            >= 337 or < 23  => "N",
            >= 23  and < 68  => "NE",
            >= 68  and < 113 => "E",
            >= 113 and < 158 => "SE",
            >= 158 and < 203 => "S",
            >= 203 and < 248 => "SW",
            >= 248 and < 293 => "W",
            _                => "NW",
        };
        return $"{h:D3}°  {dir}";
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════════════════
    RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        return rt;
    }

    RectTransform MakeStretch(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    void Img(RectTransform rt, Color c)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
    }

    Text AddTxt(Transform parent, string content, Vector2 pos, Vector2 size,
                int fs, Color color, TextAnchor anchor)
    {
        var go = new GameObject("T");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        var txt = go.AddComponent<Text>();
        txt.text = content; txt.font = fnt;
        txt.fontSize = fs; txt.color = color;
        txt.alignment = anchor;
        txt.raycastTarget = false;
        return txt;
    }
}

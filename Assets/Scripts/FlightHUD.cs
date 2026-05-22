using UnityEngine;
using UnityEngine.UI;

public class FlightHUD : MonoBehaviour
{
    // ── 색상 팔레트 ────────────────────────────────────────────────────────────
    static readonly Color HG    = new Color(0.00f, 0.98f, 0.42f, 0.92f);
    static readonly Color HGD   = new Color(0.00f, 0.98f, 0.42f, 0.50f);
    static readonly Color HGX   = new Color(0.00f, 0.98f, 0.42f, 0.22f);
    static readonly Color FPVC  = new Color(0.00f, 0.98f, 0.42f, 0.88f);
    static readonly Color WARN  = new Color(1.00f, 0.82f, 0.00f, 0.95f);
    static readonly Color CRIT  = new Color(1.00f, 0.18f, 0.08f, 0.95f);
    static readonly Color PANEL = new Color(0.04f, 0.05f, 0.07f, 0.97f);
    static readonly Color MFD   = new Color(0.02f, 0.07f, 0.04f, 0.97f);
    static readonly Color ExhGlow = new Color(0.95f, 0.55f, 0.12f, 1.00f);

    static readonly int[] PitchMarks = { -60,-50,-40,-30,-20,-10,-5, 5,10,20,30,40,50,60 };
    const float PX_PER_DEG = 7.5f;

    const int   SPD_LINES = 9;  const float SPD_STEP =  20f; const float SPD_PX = 34f;
    const int   ALT_LINES = 9;  const float ALT_STEP = 100f; const float ALT_PX = 34f;
    const int   HDG_LINES = 15; const float HDG_STEP =  10f; const float HDG_PX = 44f;

    Canvas           hudCanvas;
    PlayerController localPlayer;
    FlightCamera     flightCamera;
    Font             fnt;

    RectTransform ahRoot, ahSlide, bankPivot;

    Text[]          spdLabels   = new Text[SPD_LINES];
    RectTransform[] spdLabelRTs = new RectTransform[SPD_LINES];
    Text            spdCurrent, machText;
    RectTransform   throttleBar;

    Text[]          altLabels   = new Text[ALT_LINES];
    RectTransform[] altLabelRTs = new RectTransform[ALT_LINES];
    Text            altCurrent, vsiText, raltText;

    Text[]          hdgLabels   = new Text[HDG_LINES];
    RectTransform[] hdgLabelRTs = new RectTransform[HDG_LINES];
    Text            hdgCurrent;

    Text aoaText, gText, warnText;
    RectTransform fpvRoot;

    Text n1Text, fuelText, clockText, modeText, weaponText, gearText;
    RectTransform n1Fill, fuelFill;

    // ── 타겟팅 오버레이 ──────────────────────────────────────────────────────
    TargetingSystem targeting;
    MissileLauncher launcher;
    RectTransform   tdbRoot;
    RectTransform[] tdbArms = new RectTransform[8];
    Text            tdbLabel, tdbRange, tdbClosure, tdbAspect, tdbName;
    RectTransform   offScreenRoot;
    Text            offScreenDist;
    Text            aim120CountText;
    float           tdbFlash;

    Vector3 prevPos; bool prevPosSet;
    Vector3 prevVel;
    float smoothG = 1f, missionTime = 0f, fuelLevel = 100f;
    const float FUEL_RATE = 1.8f;

    void Start() => BuildHUD();

    void FindRefs()
    {
        foreach (var pc in FindObjectsOfType<PlayerController>())
            if (pc.isLocalPlayer) { localPlayer = pc; break; }
        flightCamera = FindObjectOfType<FlightCamera>();
    }

    void Update()
    {
        if (localPlayer == null || flightCamera == null) { FindRefs(); return; }
        bool cockpit = flightCamera.IsCockpit;
        if (hudCanvas != null) hudCanvas.enabled = cockpit;
        if (!cockpit) return;
        UpdateHUD();
    }

    // ═══════════════════════════════════════════════════════════════════════
    void BuildHUD()
    {
        fnt = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var go = new GameObject("HUD_Canvas");
        hudCanvas = go.AddComponent<Canvas>();
        hudCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        hudCanvas.sortingOrder = 20;
        var sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight  = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        hudCanvas.enabled = false;

        // 렌더 순서: AH(배경없는 라인) → 프레임 → HUD심볼
        BuildArtificialHorizon();
        BuildCockpitFrame();
        BuildHUDGlassBorder();
        BuildSpeedTape();
        BuildAltTape();
        BuildHeadingStrip();
        BuildStatusStrip();
        BuildBoresight();
        BuildFPV();
        BuildBankAngleArc();

        // 타겟팅·미사일 시스템을 같은 GameObject에 추가
        targeting = GetComponent<TargetingSystem>();
        if (targeting == null) targeting = gameObject.AddComponent<TargetingSystem>();
        launcher = GetComponent<MissileLauncher>();
        if (launcher == null) launcher = gameObject.AddComponent<MissileLauncher>();

        BuildTargetingOverlay();
    }

    // ── 인공수평선: 배경 없음, 라인만 ─────────────────────────────────────────
    // 글래스 영역: 좌우 ±872, 상하 501(탑바아래)~-240(글레어실드위)
    // 마스크 center=(0,131), size=(1744,741)
    void BuildArtificialHorizon()
    {
        var mask = Rect("AH_Mask", hudCanvas.transform, new Vector2(0f, 131f), new Vector2(1744f, 741f));
        mask.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        ahRoot  = Rect("AH_Root",  mask,   Vector2.zero, new Vector2(1744f, 741f));
        ahSlide = Rect("AH_Slide", ahRoot, Vector2.zero, new Vector2(1744f, 3200f));

        // 지평선 (하늘↔대지 경계선만)
        Img(Rect("HL", ahSlide, Vector2.zero, new Vector2(1744f, 2f)), HG);

        // 피치 래더 (중앙 GAP=155 보호)
        var ladder = Rect("Ladder", ahSlide, Vector2.zero, new Vector2(1744f, 3200f));
        const float GAP = 155f;
        foreach (int deg in PitchMarks)
        {
            float y    = deg * PX_PER_DEG;
            bool major = Mathf.Abs(deg) % 10 == 0;
            float arm  = major ? 280f : 140f;
            float thk  = major ? 2f   : 1.5f;
            Color col  = major ? HGD  : HGX;
            Img(Rect($"PL{deg}", ladder, new Vector2(-(GAP + arm * 0.5f), y), new Vector2(arm, thk)), col);
            Img(Rect($"PR{deg}", ladder, new Vector2( (GAP + arm * 0.5f), y), new Vector2(arm, thk)), col);
            if (major)
            {
                string lbl = deg > 0 ? $"+{deg}" : $"{deg}";
                AddTxt(ladder, lbl, new Vector2(-(GAP+arm+12f), y), new Vector2(54f,20f), 10, HGD, TextAnchor.MiddleRight);
                AddTxt(ladder, lbl, new Vector2( (GAP+arm+12f), y), new Vector2(54f,20f), 10, HGD, TextAnchor.MiddleLeft);
            }
        }
        // 0도 수평 보조선 (gap 내부)
        Img(Rect("HL_L", ladder, new Vector2(-(GAP*0.6f), 0f), new Vector2(GAP*0.8f, 2f)), HG);
        Img(Rect("HL_R", ladder, new Vector2( (GAP*0.6f), 0f), new Vector2(GAP*0.8f, 2f)), HG);

        BuildWingMarkers();
    }

    void BuildWingMarkers()
    {
        var box = Rect("WM_Box", ahRoot, Vector2.zero, new Vector2(14f, 14f));
        box.gameObject.AddComponent<Image>().color = Color.clear;
        var ol = box.gameObject.AddComponent<Outline>();
        ol.effectColor = HG; ol.effectDistance = new Vector2(1.5f, 1.5f);
        Img(Rect("WM_LI", ahRoot, new Vector2( -88f, 0f), new Vector2(60f, 3f)), HG);
        Img(Rect("WM_RI", ahRoot, new Vector2(  88f, 0f), new Vector2(60f, 3f)), HG);
        Img(Rect("WM_LO", ahRoot, new Vector2(-132f, 0f), new Vector2(18f, 3f)), HGD);
        Img(Rect("WM_RO", ahRoot, new Vector2( 132f, 0f), new Vector2(18f, 3f)), HGD);
        Img(Rect("WM_LT", ahRoot, new Vector2(-141f, -5f), new Vector2(3f, 13f)), HG);
        Img(Rect("WM_RT", ahRoot, new Vector2( 141f, -5f), new Vector2(3f, 13f)), HG);
        Img(Rect("WM_Up", ahRoot, new Vector2(0f, 21f),   new Vector2(3f, 20f)), HG);
    }

    // ── 콕핏 프레임 (좁은 캐노피 보우 + 글레어실드) ─────────────────────────
    void BuildCockpitFrame()
    {
        // 좌우 캐노피 보우 (88px, 좌우 시야 최소화)
        var L = Rect("FrameL", hudCanvas.transform, new Vector2(-916f, 0f), new Vector2(88f, 1080f));
        Img(L, PANEL);
        // 안쪽 하이라이트 (캐노피 구조재 엣지 반사)
        Img(Rect("FLE", hudCanvas.transform, new Vector2(-872f, 0f), new Vector2(2f, 1080f)),
            new Color(HG.r, HG.g, HG.b, 0.14f));

        var R = Rect("FrameR", hudCanvas.transform, new Vector2( 916f, 0f), new Vector2(88f, 1080f));
        Img(R, PANEL);
        Img(Rect("FRE", hudCanvas.transform, new Vector2( 872f, 0f), new Vector2(2f, 1080f)),
            new Color(HG.r, HG.g, HG.b, 0.14f));

        // 상단 캐노피 바 (32px)
        var T = Rect("CanopyTop", hudCanvas.transform, new Vector2(0f, 517f), new Vector2(1920f, 32f));
        Img(T, PANEL);
        AddTxt(T, "LOCKHEED MARTIN  F-35A LIGHTNING II",
            Vector2.zero, new Vector2(700f, 28f), 10, HGX, TextAnchor.MiddleCenter);
        // 하단 엣지 선
        Img(Rect("TopEdge", hudCanvas.transform, new Vector2(0f, 501f), new Vector2(1920f, 1f)),
            new Color(HG.r, HG.g, HG.b, 0.18f));

        // 하단 글레어실드 (300px)
        var B = Rect("Glareshield", hudCanvas.transform, new Vector2(0f, -390f), new Vector2(1920f, 300f));
        Img(B, PANEL);
        Img(Rect("GsEdge", hudCanvas.transform, new Vector2(0f, -240f), new Vector2(1920f, 1f)),
            new Color(HG.r, HG.g, HG.b, 0.18f));
        BuildGlareshield(B);
    }

    void BuildGlareshield(RectTransform p)
    {
        // ── 좌측: 엔진/연료/클럭 ─────────────────────────────────────────────
        AddTxt(p, "ENGINE", new Vector2(-700f, 118f), new Vector2(110f, 20f), 9, HGD, TextAnchor.MiddleCenter);
        Img(Rect("LD1", p, new Vector2(-700f, 107f), new Vector2(100f, 1f)), HGX);
        AddTxt(p, "N1", new Vector2(-730f, 88f), new Vector2(38f, 20f), 9, HGD, TextAnchor.MiddleLeft);
        n1Text = AddTxt(p, "30%", new Vector2(-675f, 88f), new Vector2(58f, 20f), 10, HG, TextAnchor.MiddleRight);
        var n1BG = Rect("N1BG", p, new Vector2(-700f, 72f), new Vector2(88f, 8f));
        Img(n1BG, new Color(HG.r*.1f, HG.g*.1f, HG.b*.1f, 0.8f));
        n1Fill = Rect("N1F", p, new Vector2(-744f, 72f), new Vector2(0f, 8f));
        n1Fill.pivot = new Vector2(0f, 0.5f); Img(n1Fill, HG);
        AddTxt(p, "FUEL", new Vector2(-700f, 46f), new Vector2(110f, 20f), 9, HGD, TextAnchor.MiddleCenter);
        Img(Rect("LD2", p, new Vector2(-700f, 35f), new Vector2(100f, 1f)), HGX);
        fuelText = AddTxt(p, "100.0%", new Vector2(-700f, 17f), new Vector2(100f, 20f), 11, HG, TextAnchor.MiddleCenter);
        var fBG = Rect("FBG", p, new Vector2(-700f, -1f), new Vector2(88f, 8f));
        Img(fBG, new Color(HG.r*.1f, HG.g*.1f, HG.b*.1f, 0.8f));
        fuelFill = Rect("FF", p, new Vector2(-744f, -1f), new Vector2(88f, 8f));
        fuelFill.pivot = new Vector2(0f, 0.5f); Img(fuelFill, HG);
        AddTxt(p, "MIS TIME", new Vector2(-700f, -28f), new Vector2(100f, 16f), 7, HGX, TextAnchor.MiddleCenter);
        clockText = AddTxt(p, "00:00:00", new Vector2(-700f, -46f), new Vector2(100f, 20f), 10, HGD, TextAnchor.MiddleCenter);
        modeText  = AddTxt(p, "NAV", new Vector2(-700f, -78f), new Vector2(100f, 26f), 13, HG, TextAnchor.MiddleCenter);
        AddTxt(p, "A/P OFF", new Vector2(-700f, -102f), new Vector2(100f, 18f), 8, HGX, TextAnchor.MiddleCenter);

        // ── 좌측 MFD (SA/EW) ─────────────────────────────────────────────────
        var lMFD = Rect("MFDL", p, new Vector2(-452f, 8f), new Vector2(220f, 170f));
        Img(lMFD, MFD);
        var lOl = lMFD.gameObject.AddComponent<Outline>();
        lOl.effectColor = new Color(HG.r,HG.g,HG.b,.3f); lOl.effectDistance = new Vector2(1,1);
        AddTxt(lMFD, "SA / EW", new Vector2(0f, 72f), new Vector2(200f,18f), 8, HGD, TextAnchor.MiddleCenter);
        Img(Rect("LMH", lMFD, Vector2.zero, new Vector2(110f,1f)), HGX);
        Img(Rect("LMV", lMFD, Vector2.zero, new Vector2(1f,90f)), HGX);
        var mc = Rect("LMC", lMFD, Vector2.zero, new Vector2(10f,10f));
        mc.gameObject.AddComponent<Image>().color = Color.clear;
        mc.gameObject.AddComponent<Outline>().effectColor = new Color(HG.r,HG.g,HG.b,.5f);
        AddTxt(lMFD, "NO THREAT", new Vector2(0f,-68f), new Vector2(190f,16f), 8, HGX, TextAnchor.MiddleCenter);

        // ── 중앙: 스로틀/UFC ─────────────────────────────────────────────────
        AddTxt(p, "THROTTLE", new Vector2(0f,118f), new Vector2(120f,18f), 7, HGX, TextAnchor.MiddleCenter);
        var tBG = Rect("TBG", p, new Vector2(0f,50f), new Vector2(90f,80f));
        Img(tBG, new Color(0f,0f,0f,.5f));
        AddTxt(p, "IDLE", new Vector2(0f, 12f), new Vector2(70f,16f), 7, HGX, TextAnchor.MiddleCenter);
        AddTxt(p, "MIL",  new Vector2(0f, 82f), new Vector2(70f,16f), 7, HGX, TextAnchor.MiddleCenter);
        var ab = Rect("AB", p, new Vector2(36f,100f), new Vector2(10f,18f));
        Img(ab, new Color(ExhGlow.r*.3f, ExhGlow.g*.3f, ExhGlow.b*.3f, .6f));
        AddTxt(p, "UFC", new Vector2(0f,-32f), new Vector2(120f,18f), 7, HGX, TextAnchor.MiddleCenter);
        var ufc = Rect("UFC", p, new Vector2(0f,-80f), new Vector2(100f,60f));
        Img(ufc, new Color(.02f,.04f,.03f,.9f));
        ufc.gameObject.AddComponent<Outline>().effectColor = new Color(HG.r,HG.g,HG.b,.2f);

        // ── 우측 MFD (NAV/HSI) ───────────────────────────────────────────────
        var rMFD = Rect("MFDR", p, new Vector2(452f, 8f), new Vector2(220f,170f));
        Img(rMFD, MFD);
        var rOl = rMFD.gameObject.AddComponent<Outline>();
        rOl.effectColor = new Color(HG.r,HG.g,HG.b,.3f); rOl.effectDistance = new Vector2(1,1);
        AddTxt(rMFD, "NAV / HSI", new Vector2(0f, 72f), new Vector2(200f,18f), 8, HGD, TextAnchor.MiddleCenter);
        var hsi = Rect("HSI", rMFD, new Vector2(0f,-10f), new Vector2(85f,85f));
        hsi.gameObject.AddComponent<Image>().color = Color.clear;
        hsi.gameObject.AddComponent<Outline>().effectColor = new Color(HG.r,HG.g,HG.b,.35f);
        AddTxt(rMFD, "N", new Vector2(0f, 32f), new Vector2(18f,16f), 8, HGD, TextAnchor.MiddleCenter);
        AddTxt(rMFD, "S", new Vector2(0f,-54f), new Vector2(18f,16f), 8, HGD, TextAnchor.MiddleCenter);
        AddTxt(rMFD, "E", new Vector2( 34f,-12f), new Vector2(18f,16f), 8, HGD, TextAnchor.MiddleCenter);
        AddTxt(rMFD, "W", new Vector2(-34f,-12f), new Vector2(18f,16f), 8, HGD, TextAnchor.MiddleCenter);

        // ── 우측: 무장/시스템 ─────────────────────────────────────────────────
        AddTxt(p, "WEAPON", new Vector2(700f, 118f), new Vector2(110f,20f), 9, HGD, TextAnchor.MiddleCenter);
        Img(Rect("RD1", p, new Vector2(700f,107f), new Vector2(100f,1f)), HGX);
        weaponText = AddTxt(p, "SAFE", new Vector2(700f,88f), new Vector2(100f,26f), 13, HGD, TextAnchor.MiddleCenter);
        aim120CountText = AddTxt(p, "AIM-120  x4", new Vector2(700f,66f), new Vector2(110f,18f), 8, HGX, TextAnchor.MiddleCenter);
        AddTxt(p, "AIM-9X   x2", new Vector2(700f,48f), new Vector2(110f,18f), 8, HGX, TextAnchor.MiddleCenter);
        AddTxt(p, "SYSTEMS", new Vector2(700f,20f), new Vector2(110f,20f), 9, HGD, TextAnchor.MiddleCenter);
        Img(Rect("RD2", p, new Vector2(700f,9f), new Vector2(100f,1f)), HGX);
        gearText = AddTxt(p, "GEAR  UP",  new Vector2(700f,-10f), new Vector2(110f,20f), 9, HGD, TextAnchor.MiddleCenter);
        AddTxt(p, "FLAP  RET",  new Vector2(700f,-30f), new Vector2(110f,20f), 9, HGD, TextAnchor.MiddleCenter);
        AddTxt(p, "ECM  STBY",  new Vector2(700f,-50f), new Vector2(110f,20f), 9, HGD, TextAnchor.MiddleCenter);
        AddTxt(p, "IFF  ON",    new Vector2(700f,-70f), new Vector2(110f,20f), 9, HGD, TextAnchor.MiddleCenter);
        AddTxt(p, "APG-81  ACT",new Vector2(700f,-96f), new Vector2(110f,20f), 9, HGD, TextAnchor.MiddleCenter);
    }

    // ── HUD 글래스 코너 브래킷 ────────────────────────────────────────────────
    void BuildHUDGlassBorder()
    {
        float hw=860f, hh=430f, arm=60f, thk=2f;
        Color bc = new Color(HG.r, HG.g, HG.b, 0.50f);
        MkBracket(-hw,  hh,  1f,-1f, arm, thk, bc);
        MkBracket( hw,  hh, -1f,-1f, arm, thk, bc);
        MkBracket(-hw, -hh,  1f, 1f, arm, thk, bc);
        MkBracket( hw, -hh, -1f, 1f, arm, thk, bc);
        // 상단 중앙 V마커
        Color bm = new Color(HG.r,HG.g,HG.b,0.38f);
        Img(Rect("TML",hudCanvas.transform,new Vector2(-46f, hh),new Vector2(38f,thk)),bm);
        Img(Rect("TMR",hudCanvas.transform,new Vector2( 46f, hh),new Vector2(38f,thk)),bm);
        Img(Rect("TMC",hudCanvas.transform,new Vector2(0f,hh+5f),new Vector2(thk,12f)),bm);
        Img(Rect("BML",hudCanvas.transform,new Vector2(-46f,-hh),new Vector2(38f,thk)),bm);
        Img(Rect("BMR",hudCanvas.transform,new Vector2( 46f,-hh),new Vector2(38f,thk)),bm);
        Img(Rect("BMC",hudCanvas.transform,new Vector2(0f,-hh-5f),new Vector2(thk,12f)),bm);
    }

    void MkBracket(float cx, float cy, float sx, float sy, float arm, float thk, Color c)
    {
        Img(Rect("BH", hudCanvas.transform, new Vector2(cx+sx*arm*.5f, cy), new Vector2(arm, thk)), c);
        Img(Rect("BV", hudCanvas.transform, new Vector2(cx, cy+sy*arm*.5f), new Vector2(thk, arm)), c);
    }

    // ── 속도 테이프 ──────────────────────────────────────────────────────────
    void BuildSpeedTape()
    {
        float px = -752f;
        var panel = Rect("SpdPanel", hudCanvas.transform, new Vector2(px,0f), new Vector2(130f,SPD_LINES*SPD_PX+24f));
        Img(panel, new Color(0f,0f,0f,.60f));
        var msk = Rect("SM", panel, Vector2.zero, new Vector2(110f,SPD_LINES*SPD_PX));
        msk.gameObject.AddComponent<Image>().color = new Color(0f,0f,0f,.01f);
        msk.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var scr = Rect("SS", msk, Vector2.zero, new Vector2(110f,SPD_LINES*SPD_PX));
        for (int i=0;i<SPD_LINES;i++)
        {
            float y=(SPD_LINES/2-i)*SPD_PX;
            spdLabels[i]   = AddTxt(scr,"0",new Vector2(0f,y),new Vector2(100f,SPD_PX-4f),i==SPD_LINES/2?15:12,i==SPD_LINES/2?HG:HGD,TextAnchor.MiddleRight);
            spdLabelRTs[i] = spdLabels[i].rectTransform;
            Img(Rect($"ST{i}",scr,new Vector2(52f,y),new Vector2(12f,1f)),i==SPD_LINES/2?HG:HGX);
        }
        var cB=Rect("SCB",panel,Vector2.zero,new Vector2(130f,32f));
        Img(cB,new Color(HG.r*.14f,HG.g*.14f,HG.b*.14f,.65f));
        cB.gameObject.AddComponent<Outline>().effectColor=new Color(HG.r,HG.g,HG.b,.80f);
        spdCurrent=AddTxt(panel,"0",Vector2.zero,new Vector2(108f,32f),21,HG,TextAnchor.MiddleRight);
        AddTxt(panel,"◀",new Vector2(58f,0f),new Vector2(24f,32f),14,HG,TextAnchor.MiddleLeft);
        AddTxt(panel,"IAS KPH",new Vector2(0f,SPD_LINES*SPD_PX/2f+14f),new Vector2(110f,18f),8,HGD,TextAnchor.MiddleCenter);
        var mach=Rect("MP",hudCanvas.transform,new Vector2(px,-(SPD_LINES*SPD_PX/2f+50f)),new Vector2(130f,30f));
        Img(mach,new Color(0f,0f,0f,.50f));
        machText=AddTxt(mach,"M 0.00",Vector2.zero,new Vector2(110f,30f),12,HGD,TextAnchor.MiddleCenter);
        BuildThrottleBar(px-22f);
    }

    void BuildThrottleBar(float px)
    {
        var p=Rect("TP",hudCanvas.transform,new Vector2(px,0f),new Vector2(14f,SPD_LINES*SPD_PX+24f));
        Img(p,new Color(0f,0f,0f,.50f));
        var bg=Rect("TBG",p,Vector2.zero,new Vector2(8f,SPD_LINES*SPD_PX));
        Img(bg,new Color(HG.r*.07f,HG.g*.07f,HG.b*.07f,.9f));
        throttleBar=Rect("TF",p,new Vector2(0f,-SPD_LINES*SPD_PX/2f),new Vector2(8f,0f));
        throttleBar.pivot=new Vector2(.5f,0f); Img(throttleBar,HG);
    }

    // ── 고도 테이프 ──────────────────────────────────────────────────────────
    void BuildAltTape()
    {
        float px=752f;
        var panel=Rect("AltPanel",hudCanvas.transform,new Vector2(px,0f),new Vector2(130f,ALT_LINES*ALT_PX+24f));
        Img(panel,new Color(0f,0f,0f,.60f));
        var msk=Rect("AM",panel,Vector2.zero,new Vector2(110f,ALT_LINES*ALT_PX));
        msk.gameObject.AddComponent<Image>().color=new Color(0f,0f,0f,.01f);
        msk.gameObject.AddComponent<Mask>().showMaskGraphic=false;
        var scr=Rect("AS",msk,Vector2.zero,new Vector2(110f,ALT_LINES*ALT_PX));
        for(int i=0;i<ALT_LINES;i++)
        {
            float y=(ALT_LINES/2-i)*ALT_PX;
            altLabels[i]   =AddTxt(scr,"0",new Vector2(0f,y),new Vector2(100f,ALT_PX-4f),i==ALT_LINES/2?15:12,i==ALT_LINES/2?HG:HGD,TextAnchor.MiddleLeft);
            altLabelRTs[i] =altLabels[i].rectTransform;
            Img(Rect($"AT{i}",scr,new Vector2(-52f,y),new Vector2(12f,1f)),i==ALT_LINES/2?HG:HGX);
        }
        var cB=Rect("ACB",panel,Vector2.zero,new Vector2(130f,32f));
        Img(cB,new Color(HG.r*.14f,HG.g*.14f,HG.b*.14f,.65f));
        cB.gameObject.AddComponent<Outline>().effectColor=new Color(HG.r,HG.g,HG.b,.80f);
        altCurrent=AddTxt(panel,"0",Vector2.zero,new Vector2(108f,32f),21,HG,TextAnchor.MiddleLeft);
        AddTxt(panel,"▶",new Vector2(-56f,0f),new Vector2(24f,32f),14,HG,TextAnchor.MiddleRight);
        AddTxt(panel,"ALT m",new Vector2(0f,ALT_LINES*ALT_PX/2f+14f),new Vector2(110f,18f),8,HGD,TextAnchor.MiddleCenter);
        var vsi=Rect("VSI",hudCanvas.transform,new Vector2(px,-(ALT_LINES*ALT_PX/2f+48f)),new Vector2(130f,30f));
        Img(vsi,new Color(0f,0f,0f,.50f));
        vsiText=AddTxt(vsi,"VVI  +0",Vector2.zero,new Vector2(120f,30f),11,HGD,TextAnchor.MiddleCenter);
        var ralt=Rect("RALT",hudCanvas.transform,new Vector2(px,-(ALT_LINES*ALT_PX/2f+84f)),new Vector2(130f,28f));
        Img(ralt,new Color(0f,0f,0f,.44f));
        raltText=AddTxt(ralt,"RALT ---",Vector2.zero,new Vector2(120f,28f),10,HGD,TextAnchor.MiddleCenter);
    }

    // ── 헤딩 스트립 ──────────────────────────────────────────────────────────
    void BuildHeadingStrip()
    {
        var panel=Rect("HdgPanel",hudCanvas.transform,new Vector2(0f,430f),new Vector2(HDG_LINES*HDG_PX+24f,50f));
        Img(panel,new Color(0f,0f,0f,.60f));
        var msk=Rect("HM",panel,Vector2.zero,new Vector2(HDG_LINES*HDG_PX,42f));
        msk.gameObject.AddComponent<Image>().color=new Color(0f,0f,0f,.01f);
        msk.gameObject.AddComponent<Mask>().showMaskGraphic=false;
        var scr=Rect("HS",msk,Vector2.zero,new Vector2(HDG_LINES*HDG_PX,42f));
        for(int i=0;i<HDG_LINES;i++)
        {
            float x=(i-HDG_LINES/2)*HDG_PX;
            hdgLabels[i]  =AddTxt(scr,"N",new Vector2(x,5f),new Vector2(HDG_PX-4f,18f),10,HGD,TextAnchor.MiddleCenter);
            hdgLabelRTs[i]=hdgLabels[i].rectTransform;
            Img(Rect($"HT{i}",scr,new Vector2(x,-14f),new Vector2(1f,7f)),HGX);
        }
        Img(Rect("HPtr",panel,new Vector2(0f,-19f),new Vector2(2f,11f)),HG);
        var cB=Rect("HCB",panel,new Vector2(0f,-6f),new Vector2(70f,24f));
        Img(cB,new Color(HG.r*.14f,HG.g*.14f,HG.b*.14f,.70f));
        cB.gameObject.AddComponent<Outline>().effectColor=new Color(HG.r,HG.g,HG.b,.65f);
        hdgCurrent=AddTxt(panel,"000°",new Vector2(0f,-6f),new Vector2(64f,22f),13,HG,TextAnchor.MiddleCenter);
    }

    // ── 상태 스트립 ──────────────────────────────────────────────────────────
    void BuildStatusStrip()
    {
        var panel=Rect("Status",hudCanvas.transform,new Vector2(0f,-218f),new Vector2(540f,36f));
        Img(panel,new Color(0f,0f,0f,.54f));
        panel.gameObject.AddComponent<Outline>().effectColor=new Color(HG.r,HG.g,HG.b,.22f);
        aoaText=AddTxt(panel,"AOA +0.0°",new Vector2(-180f,0f),new Vector2(155f,36f),11,HGD,TextAnchor.MiddleCenter);
        gText  =AddTxt(panel,"G  1.0",  new Vector2(0f,0f),   new Vector2(110f,36f),15,HG, TextAnchor.MiddleCenter);
        AddTxt(panel,"SAFE",new Vector2(180f,0f),new Vector2(100f,36f),10,HGD,TextAnchor.MiddleCenter);
        var wp=Rect("Warn",hudCanvas.transform,new Vector2(0f,364f),new Vector2(360f,38f));
        Img(wp,new Color(0f,0f,0f,0f));
        warnText=AddTxt(wp,"",Vector2.zero,new Vector2(360f,38f),16,WARN,TextAnchor.MiddleCenter);
    }

    // ── 조준점 ────────────────────────────────────────────────────────────────
    void BuildBoresight()
    {
        Color c=new Color(HG.r,HG.g,HG.b,.72f);
        var ct=Rect("BS",hudCanvas.transform,Vector2.zero,new Vector2(20f,20f));
        ct.gameObject.AddComponent<Image>().color=Color.clear;
        ct.gameObject.AddComponent<Outline>().effectColor=c;
        ct.GetComponent<Outline>().effectDistance=new Vector2(2f,2f);
        Img(Rect("BHL",hudCanvas.transform,new Vector2(-34f,0f),new Vector2(22f,2f)),c);
        Img(Rect("BHR",hudCanvas.transform,new Vector2( 34f,0f),new Vector2(22f,2f)),c);
        Img(Rect("BVU",hudCanvas.transform,new Vector2(0f, 34f),new Vector2(2f,22f)),c);
        Img(Rect("BVD",hudCanvas.transform,new Vector2(0f,-34f),new Vector2(2f,22f)),c);
    }

    // ── FPV ──────────────────────────────────────────────────────────────────
    void BuildFPV()
    {
        fpvRoot=Rect("FPV",hudCanvas.transform,Vector2.zero,Vector2.zero);
        var ci=Rect("FC",fpvRoot,Vector2.zero,new Vector2(18f,18f));
        ci.gameObject.AddComponent<Image>().color=Color.clear;
        var ol=ci.gameObject.AddComponent<Outline>();
        ol.effectColor=FPVC; ol.effectDistance=new Vector2(1.5f,1.5f);
        Img(Rect("FL",fpvRoot,new Vector2(-24f,0f),new Vector2(18f,2f)),FPVC);
        Img(Rect("FR",fpvRoot,new Vector2( 24f,0f),new Vector2(18f,2f)),FPVC);
        Img(Rect("FT",fpvRoot,new Vector2(0f,16f), new Vector2(2f,12f)),FPVC);
    }

    // ── 뱅크각 호 + 포인터 ──────────────────────────────────────────────────
    void BuildBankAngleArc()
    {
        int[] marks={-60,-45,-30,-20,-10,0,10,20,30,45,60};
        float r=210f;
        foreach(int deg in marks)
        {
            float rad=(90-deg)*Mathf.Deg2Rad;
            float x=r*Mathf.Cos(rad),y=r*Mathf.Sin(rad);
            float h=deg%30==0?16f:9f;
            var tick=Rect($"BA{deg}",hudCanvas.transform,new Vector2(x,y),new Vector2(2f,h));
            tick.localRotation=Quaternion.Euler(0f,0f,-deg);
            Img(tick,HGD);
            if(deg%30==0&&deg!=0)
                AddTxt(hudCanvas.transform,$"{Mathf.Abs(deg)}",new Vector2(x*1.14f,y*1.14f),new Vector2(30f,16f),8,HGD,TextAnchor.MiddleCenter);
        }
        bankPivot=Rect("BankPivot",hudCanvas.transform,Vector2.zero,Vector2.zero);
        var ptr=Rect("BPtr",bankPivot,new Vector2(0f,r-2f),new Vector2(10f,14f));
        Img(ptr,HG);
    }

    // ═══════════════════════════════════════════════════════════════════════
    void UpdateHUD()
    {
        Transform t=localPlayer.transform;
        float dt=Time.deltaTime;
        if(!prevPosSet){prevPos=t.position;prevPosSet=true;}

        Vector3 euler=t.eulerAngles;
        float pitch=euler.x>180f?euler.x-360f:euler.x;
        float roll =euler.z>180f?euler.z-360f:euler.z;
        float yaw  =euler.y;

        Vector3 vel=(t.position-prevPos)/dt;
        float kph=vel.magnitude*3.6f, mach=vel.magnitude/343f;

        Vector3 accel=dt>0f?(vel-prevVel)/dt:Vector3.zero;
        float g=(accel-Physics.gravity).magnitude/9.81f;
        smoothG=Mathf.Lerp(smoothG,g,dt*4f);

        float aoa=0f;
        if(vel.magnitude>0.5f)
            aoa=Vector3.SignedAngle(Vector3.ProjectOnPlane(vel.normalized,t.right),t.forward,t.right);

        prevVel=vel; prevPos=t.position;

        // FPV
        if(fpvRoot!=null)
        {
            if(vel.magnitude>1.5f)
            {
                Vector3 lv=t.InverseTransformDirection(vel.normalized);
                float fx=Mathf.Atan2(lv.x,lv.z)*Mathf.Rad2Deg*PX_PER_DEG;
                float fy=Mathf.Atan2(lv.y,lv.z)*Mathf.Rad2Deg*PX_PER_DEG;
                fpvRoot.anchoredPosition=new Vector2(Mathf.Clamp(fx,-110f,110f),Mathf.Clamp(fy,-110f,110f));
                fpvRoot.gameObject.SetActive(true);
            }
            else fpvRoot.gameObject.SetActive(false);
        }

        ahRoot.localRotation    =Quaternion.Euler(0f,0f,roll);
        ahSlide.anchoredPosition=new Vector2(0f,pitch*PX_PER_DEG);
        if(bankPivot!=null) bankPivot.localRotation=Quaternion.Euler(0f,0f,roll);

        UpdateTape(spdLabels,spdLabelRTs,kph,SPD_STEP,SPD_PX,SPD_LINES,false);
        spdCurrent.text=$"{kph:F0}";
        machText.text  =$"M  {mach:F3}";
        float thr=Mathf.Clamp01(localPlayer.CurrentSpeed/80f);
        if(throttleBar!=null) throttleBar.sizeDelta=new Vector2(8f,thr*SPD_LINES*SPD_PX);

        UpdateTape(altLabels,altLabelRTs,t.position.y,ALT_STEP,ALT_PX,ALT_LINES,true);
        altCurrent.text=$"{t.position.y:F0}";
        float vvi=vel.y;
        vsiText.text =$"VVI  {(vvi>=0?"+":"")}{vvi:F0}";
        vsiText.color=Mathf.Abs(vvi)>30f?WARN:HGD;
        if(Physics.Raycast(t.position,Vector3.down,out RaycastHit hit,5000f))
            raltText.text=$"RALT {hit.distance:F0}m";
        else raltText.text="RALT ---";

        UpdateHeadingTape(yaw);
        hdgCurrent.text=$"{((int)yaw%360+360)%360:D3}°";

        aoaText.text=$"AOA  {(aoa>=0?"+":"")}{aoa:F1}°";
        gText.text  =$"G  {smoothG:F1}";
        gText.color =smoothG>7f?CRIT:smoothG>4f?WARN:HG;

        bool stallWarn=kph<80f&&localPlayer.CurrentSpeed>1f;
        bool gWarn    =smoothG>7f;
        warnText.text =gWarn?"  G-LOCK WARNING":stallWarn?"  STALL":"";
        warnText.color=gWarn?CRIT:WARN;

        missionTime+=dt;
        fuelLevel-=(FUEL_RATE/60f)*dt*(0.25f+thr*0.75f);
        fuelLevel=Mathf.Max(0f,fuelLevel);
        float n1=28f+thr*72f;

        if(n1Text  !=null) n1Text.text  =$"{n1:F0}%";
        if(n1Fill  !=null) n1Fill.sizeDelta  =new Vector2(88f*n1/100f,8f);
        if(fuelText!=null) fuelText.text=$"{fuelLevel:F1}%";
        if(fuelFill!=null) fuelFill.sizeDelta=new Vector2(88f*fuelLevel/100f,8f);
        int ts=(int)missionTime;
        if(clockText!=null) clockText.text=$"{ts/3600:D2}:{ts/60%60:D2}:{ts%60:D2}";
        if(fuelText !=null) fuelText.color=fuelLevel<15f?WARN:HG;
        if(n1Text   !=null) n1Text.color =n1>95f?WARN:HG;
        if(fuelLevel<10f&&warnText!=null&&!gWarn&&!stallWarn)
        {warnText.text="  BINGO FUEL";warnText.color=WARN;}

        UpdateTargetingHUD();
    }

    void UpdateTape(Text[] labels, RectTransform[] rts, float val,
                    float step, float px, int count, bool leftAlign)
    {
        int half=count/2;
        int base_=Mathf.FloorToInt(val/step)*(int)step;
        float frac=(val-base_)/step;
        for(int i=0;i<count;i++)
        {
            int v=base_+(half-i)*(int)step;
            labels[i].text=v>=0?v.ToString("F0"):"";
            float y=(half-i)*px+frac*px;
            rts[i].anchoredPosition=new Vector2(rts[i].anchoredPosition.x,y);
            labels[i].color   =i==half?HG:HGD;
            labels[i].fontSize=i==half?15:12;
        }
    }

    void UpdateHeadingTape(float yaw)
    {
        int half=HDG_LINES/2;
        int baseH=Mathf.FloorToInt(yaw/HDG_STEP)*(int)HDG_STEP;
        float frac=(yaw-baseH)/HDG_STEP;
        for(int i=0;i<HDG_LINES;i++)
        {
            int v=((baseH+(i-half)*(int)HDG_STEP)%360+360)%360;
            hdgLabels[i].text=HeadingLabel(v);
            float x=(i-half)*HDG_PX-frac*HDG_PX;
            hdgLabelRTs[i].anchoredPosition=new Vector2(x,hdgLabelRTs[i].anchoredPosition.y);
            bool card=v==0||v==90||v==180||v==270;
            hdgLabels[i].color    =i==half?HG:card?HG:HGD;
            hdgLabels[i].fontStyle=card?FontStyle.Bold:FontStyle.Normal;
        }
    }

    static string HeadingLabel(int d)=>d switch{0=>"N",90=>"E",180=>"S",270=>"W",_=>d%30==0?(d/10).ToString():"·"};

    // ═══════════════════════════════════════════════════════════════════════
    // ── 타겟팅 오버레이 빌드 ───────────────────────────────────────────────
    void BuildTargetingOverlay()
    {
        BuildTargetBox();
        BuildOffScreenIndicator();
    }

    void BuildTargetBox()
    {
        tdbRoot = Rect("TDB_Root", hudCanvas.transform, Vector2.zero, Vector2.zero);
        tdbRoot.gameObject.SetActive(false);

        // 8개 브래킷 암 (코너당 2개: H + V)
        for (int i = 0; i < 8; i++)
        {
            tdbArms[i] = Rect($"TDB_A{i}", tdbRoot, Vector2.zero, new Vector2(20f, 2f));
            Img(tdbArms[i], HG);
        }

        // 타겟 이름 (박스 위)
        tdbName    = AddTxt(tdbRoot, "", new Vector2(0f, 58f),  new Vector2(160f, 16f), 9,  HGD, TextAnchor.MiddleCenter);
        // 락온 상태 레이블 (박스 아래)
        tdbLabel   = AddTxt(tdbRoot, "", new Vector2(0f, -62f), new Vector2(120f, 18f), 11, HG,  TextAnchor.MiddleCenter);
        // 거리
        tdbRange   = AddTxt(tdbRoot, "", new Vector2(0f, -80f), new Vector2(160f, 16f), 10, HGD, TextAnchor.MiddleCenter);
        // 폐쇄율
        tdbClosure = AddTxt(tdbRoot, "", new Vector2(0f, -96f), new Vector2(160f, 16f), 9,  HGD, TextAnchor.MiddleCenter);
        // 어스펙트
        tdbAspect  = AddTxt(tdbRoot, "", new Vector2(0f,-112f), new Vector2(160f, 16f), 9,  HGD, TextAnchor.MiddleCenter);
    }

    void BuildOffScreenIndicator()
    {
        offScreenRoot = Rect("OSI_Root", hudCanvas.transform, Vector2.zero, new Vector2(28f, 28f));
        offScreenRoot.gameObject.SetActive(false);
        // 화살표: 텍스트 "▲" 회전으로 방향 표시
        AddTxt(offScreenRoot, "▲", Vector2.zero, new Vector2(28f, 28f), 16, HG, TextAnchor.MiddleCenter);
        offScreenDist = AddTxt(offScreenRoot, "", new Vector2(0f, -26f), new Vector2(90f, 18f), 9, HGD, TextAnchor.MiddleCenter);
    }

    void LayoutTDBArms(float halfSize)
    {
        float arm = 22f, thk = 2f;
        // 좌상 코너
        tdbArms[0].anchoredPosition = new Vector2(-halfSize + arm * 0.5f,  halfSize);
        tdbArms[0].sizeDelta        = new Vector2(arm, thk);
        tdbArms[1].anchoredPosition = new Vector2(-halfSize,  halfSize - arm * 0.5f);
        tdbArms[1].sizeDelta        = new Vector2(thk, arm);
        // 우상 코너
        tdbArms[2].anchoredPosition = new Vector2( halfSize - arm * 0.5f,  halfSize);
        tdbArms[2].sizeDelta        = new Vector2(arm, thk);
        tdbArms[3].anchoredPosition = new Vector2( halfSize,  halfSize - arm * 0.5f);
        tdbArms[3].sizeDelta        = new Vector2(thk, arm);
        // 좌하 코너
        tdbArms[4].anchoredPosition = new Vector2(-halfSize + arm * 0.5f, -halfSize);
        tdbArms[4].sizeDelta        = new Vector2(arm, thk);
        tdbArms[5].anchoredPosition = new Vector2(-halfSize, -halfSize + arm * 0.5f);
        tdbArms[5].sizeDelta        = new Vector2(thk, arm);
        // 우하 코너
        tdbArms[6].anchoredPosition = new Vector2( halfSize - arm * 0.5f, -halfSize);
        tdbArms[6].sizeDelta        = new Vector2(arm, thk);
        tdbArms[7].anchoredPosition = new Vector2( halfSize, -halfSize + arm * 0.5f);
        tdbArms[7].sizeDelta        = new Vector2(thk, arm);
    }

    // ── 타겟팅 HUD 업데이트 ────────────────────────────────────────────────
    void UpdateTargetingHUD()
    {
        if (targeting == null) return;

        tdbFlash += Time.deltaTime;
        bool blink = tdbFlash % 0.5f < 0.25f;  // 2Hz 점멸

        bool hasTarget = targeting.Target != null;

        if (hasTarget && targeting.IsTargetOnScreen)
        {
            tdbRoot.gameObject.SetActive(true);
            offScreenRoot.gameObject.SetActive(false);

            // 캔버스 위치로 이동
            tdbRoot.anchoredPosition = targeting.TargetCanvasPos;

            bool   locked   = targeting.State == TargetingSystem.LockState.Locked;
            float  halfSize = Mathf.Lerp(65f, 42f, targeting.LockProgress);
            LayoutTDBArms(halfSize);

            // 브래킷 색상: Locked=밝은 녹, Searching=점멸
            Color armCol = locked ? HG : (blink ? HGD : HGX);
            foreach (var arm in tdbArms)
            {
                var img = arm.GetComponent<Image>();
                if (img != null) img.color = armCol;
            }

            // 레이블
            tdbLabel.text  = locked ? "◆ TRK" : "SRCH";
            tdbLabel.color = locked ? HG : (blink ? WARN : HGX);

            float km = targeting.TargetRange / 1000f;
            tdbRange.text   = km >= 1f ? $"{km:F1} km" : $"{targeting.TargetRange:F0} m";
            tdbClosure.text = $"CL  {(targeting.ClosureRate >= 0f ? "+" : "")}{targeting.ClosureRate:F0} m/s";
            tdbAspect.text  = $"ASP  {targeting.TargetAspect:F0}°  {AspectLabel(targeting.TargetAspect)}";
            tdbName.text    = targeting.Target.nickname;

            // 무장 상태 업데이트
            if (weaponText != null)
            {
                if (locked)
                { weaponText.text = "SHOOT"; weaponText.color = blink ? CRIT : WARN; }
                else
                { weaponText.text = "ACQR"; weaponText.color = WARN; }
            }
        }
        else if (hasTarget && !targeting.IsTargetOnScreen)
        {
            tdbRoot.gameObject.SetActive(false);
            offScreenRoot.gameObject.SetActive(true);

            // 오프스크린 화살표: 화면 엣지에 배치
            Vector2 dir = targeting.TargetCanvasPos;
            if (dir.sqrMagnitude < 0.001f) dir = Vector2.up;
            float   maxX  = 830f, maxY = 370f;
            float   sX    = Mathf.Abs(dir.x) > 0.001f ? maxX / Mathf.Abs(dir.x) : float.MaxValue;
            float   sY    = Mathf.Abs(dir.y) > 0.001f ? maxY / Mathf.Abs(dir.y) : float.MaxValue;
            offScreenRoot.anchoredPosition = dir * Mathf.Min(sX, sY);

            // 화살표 방향 (▲ 기준 = 위쪽, 회전으로 방향 설정)
            float angle = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            offScreenRoot.localRotation = Quaternion.Euler(0f, 0f, -angle);

            float km = targeting.TargetRange / 1000f;
            offScreenDist.text = km >= 1f ? $"{km:F1}km" : $"{targeting.TargetRange:F0}m";

            if (weaponText != null) { weaponText.text = "ACQR"; weaponText.color = WARN; }
        }
        else
        {
            tdbRoot.gameObject.SetActive(false);
            offScreenRoot.gameObject.SetActive(false);
            if (weaponText != null) { weaponText.text = "SAFE"; weaponText.color = HGD; }
        }

        // AIM-120 카운트 동적 갱신
        if (aim120CountText != null && launcher != null)
            aim120CountText.text = $"AIM-120  x{launcher.MissileCount}";
    }

    static string AspectLabel(float deg)
    {
        if (deg < 30f)  return "TAIL";
        if (deg < 90f)  return "BEAM";
        if (deg < 150f) return "NOSE";
        return "HTB";
    }

    RectTransform Rect(string name,Transform parent,Vector2 pos,Vector2 size)
    {
        var go=new GameObject(name); go.transform.SetParent(parent,false);
        var rt=go.AddComponent<RectTransform>();
        rt.anchorMin=rt.anchorMax=new Vector2(.5f,.5f);
        rt.anchoredPosition=pos; rt.sizeDelta=size; return rt;
    }

    RectTransform MakeStretch(string name,Transform parent)
    {
        var go=new GameObject(name); go.transform.SetParent(parent,false);
        var rt=go.AddComponent<RectTransform>();
        rt.anchorMin=Vector2.zero; rt.anchorMax=Vector2.one;
        rt.offsetMin=rt.offsetMax=Vector2.zero; return rt;
    }

    void Img(RectTransform rt,Color c)
    {
        var img=rt.gameObject.AddComponent<Image>();
        img.color=c; img.raycastTarget=false;
    }

    Text AddTxt(Transform parent,string content,Vector2 pos,Vector2 size,
                int fs,Color color,TextAnchor anchor)
    {
        var go=new GameObject("T"); go.transform.SetParent(parent,false);
        var rt=go.AddComponent<RectTransform>();
        rt.anchorMin=rt.anchorMax=new Vector2(.5f,.5f);
        rt.anchoredPosition=pos; rt.sizeDelta=size;
        var txt=go.AddComponent<Text>();
        txt.text=content; txt.font=fnt;
        txt.fontSize=fs; txt.color=color;
        txt.alignment=anchor; txt.raycastTarget=false; return txt;
    }
}

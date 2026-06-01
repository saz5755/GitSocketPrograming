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

    [SerializeField] Canvas    hudCanvas;       // HUD_Canvas — Inspector에서 미리 배치된 Canvas 할당 가능
    [SerializeField] Canvas    _hudGlassCanvas; // HUD_GlassCanvas (WorldSpace) — Inspector에서 할당 가능
    [SerializeField] Canvas    warningCanvas;   // Warning_Canvas — Inspector에서 미리 배치된 Canvas 할당 가능
    [SerializeField] Transform hudRoot;         // HUD_Canvas·Warning_Canvas·GunOverlay_Canvas 공통 부모

    public Transform HudRoot => hudRoot;
    bool _hudGlassBuilt;

    // ── 레이아웃 — 콕핏 프레임 ───────────────────────────────────────────────────
    [Header("Layout — Cockpit Frame")]
    [SerializeField] Vector2 _frameLPos     = new Vector2(-916f,   0f);
    [SerializeField] Vector2 _frameLSize    = new Vector2(  88f, 1080f);
    [SerializeField] Vector2 _frameRPos     = new Vector2( 916f,   0f);
    [SerializeField] Vector2 _frameRSize    = new Vector2(  88f, 1080f);
    [SerializeField] Vector2 _canopyTopPos  = new Vector2(   0f,  517f);
    [SerializeField] Vector2 _canopyTopSize = new Vector2(1920f,   32f);

    // ── 레이아웃 — 글레어실드 ─────────────────────────────────────────────────────
    [Header("Layout — Glareshield")]
    [SerializeField] Vector2 _gsLPos   = new Vector2(-605f, -390f);
    [SerializeField] Vector2 _gsLSize  = new Vector2( 710f,  300f);
    [SerializeField] Vector2 _gsRPos   = new Vector2( 605f, -390f);
    [SerializeField] Vector2 _gsRSize  = new Vector2( 710f,  300f);
    [SerializeField] float   _engScrX  = -700f;
    [SerializeField] float   _mfdLScrX = -452f;
    [SerializeField] float   _mfdRScrX =  452f;
    [SerializeField] float   _wpnScrX  =  700f;

    // ── 레이아웃 — HUD 유리 캔버스 ──────────────────────────────────────────────
    [Header("Layout — HUD Glass Canvas")]
    [SerializeField] Vector3 _glassLocalPos   = new Vector3(0f, 0.45f, 2.90f);
    [SerializeField] float   _glassLocalScale = 0.0003f;
    [SerializeField] Vector2 _glassSizeDelta  = new Vector2(1920f, 1080f);
    [SerializeField] float   _glassBorderHW   =  860f;
    [SerializeField] float   _glassBorderHH   =  430f;

    // ── 레이아웃 — 계기 패널 ─────────────────────────────────────────────────────
    [Header("Layout — Instruments")]
    [SerializeField] float   _spdTapeX     = -752f;
    [SerializeField] float   _altTapeX     =  752f;
    [SerializeField] float   _hdgStripY    =  430f;
    [SerializeField] float   _statusStripY = -218f;
    [SerializeField] float   _warnBannerY  =  364f;
    [SerializeField] Vector2 _ahMaskSize   = new Vector2(1744f, 741f);

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

    // ── 글레어실드 UI refs ────────────────────────────────────────────────────
    [Header("Refs — Glareshield Left")]
    [SerializeField] Text          n1Text, fuelText, clockText, modeText;
    [SerializeField] RectTransform n1Fill, fuelFill;

    [Header("Refs — Glareshield Right")]
    [SerializeField] Text          weaponText, gearText;
    [SerializeField] Text          aim120CountText, flareCountText, chaffCountText;

    // ── 타겟팅 오버레이 UI refs ───────────────────────────────────────────────
    [Header("Refs — Targeting")]
    TargetingSystem    targeting;
    MissileLauncher    launcher;
    ThreatWarningSystem threatWarn;
    CountermeasureSystem cmsys;
    [SerializeField] RectTransform   tdbRoot;
    RectTransform[] tdbArms = new RectTransform[8];
    [SerializeField] Text            tdbLabel, tdbRange, tdbClosure, tdbAspect, tdbName;
    [SerializeField] RectTransform   offScreenRoot;
    [SerializeField] Text            offScreenDist;
    float           tdbFlash;

    // ── 위협 경고 UI refs ─────────────────────────────────────────────────────
    [Header("Refs — Threat / RWR")]
    [SerializeField] Text          threatWarningText;
    [SerializeField] RectTransform rwrNeedle;

    // ── 경고 캔버스 UI refs ───────────────────────────────────────────────────
    [Header("Refs — Warning Canvas")]
    [SerializeField] Image   missileAlertOverlay;
    [SerializeField] Text    missileAlertText;
    [SerializeField] Text    missileDistText;
    [SerializeField] Text    cmsDeployText;
    float   _deployTimer;
    string  _deployMsg;

    Vector3 prevPos; bool prevPosSet;
    Vector3 prevVel;
    float smoothG = 1f, missionTime = 0f, fuelLevel = 100f;
    const float FUEL_RATE = 1.8f;

    float   _raltTimer;
    string  _raltCache = "RALT ---";
    const float RALT_INTERVAL = 0.1f;

    // ── ILS 인디케이터 UI refs ────────────────────────────────────────────────
    [Header("Refs — ILS Indicator")]
    [SerializeField] RectTransform _ilsPanel;
    [SerializeField] RectTransform _ilsLocBar;
    [SerializeField] RectTransform _ilsGsBar;
    [SerializeField] Image         _ilsLocImg;
    [SerializeField] Image         _ilsGsImg;
    [SerializeField] Text          _ilsDistText;
    [SerializeField] Text          _ilsLabel;

    ILSBeacon _activeILS;
    float     _ilsScanTimer;
    const float ILS_SCAN_INTERVAL  = 1.0f;
    const float ILS_DEFLECT_PX     = 52f;   // ±52 px = 풀 스케일 편차
    const float ILS_DEFLECT_M      = 200f;  // ±200 m = 풀 스케일 기준

    void Start()
    {
        CountermeasureSystem.OnDeploy += OnCMSDeploy;
        BuildHUD();
        BuildWarningCanvas();

        if (GetComponent<RadarMiniMap>()     == null) gameObject.AddComponent<RadarMiniMap>();
        if (GetComponent<FlightAudioSystem>() == null) gameObject.AddComponent<FlightAudioSystem>();
    }

    void OnDestroy()
    {
        CountermeasureSystem.OnDeploy -= OnCMSDeploy;
    }

    public void SetVisible(bool visible)
    {
        // 레이더: 비행 중 항상 표시, 지상 전환 시 숨김
        GetComponent<RadarMiniMap>()?.SetVisible(visible);

        // HUD/Warning/GunOverlay: 콕핏 모드 전용 — SetCockpitGlass가 제어
        // 비행 이탈 시에만 강제 끔
        if (!visible)
        {
            if (hudCanvas     != null) hudCanvas.enabled     = false;
            if (warningCanvas != null) warningCanvas.enabled = false;
            GetComponent<GunSystem>()?.SetHUDVisible(false);
        }
    }

    /// <summary>
    /// 코크핏 모드: 콕핏 프레임+타겟팅 캔버스를 ScreenSpaceCamera로 전환하고,
    /// WorldSpace HUD 유리 캔버스를 활성화.
    /// </summary>
    public void SetCockpitGlass(bool cockpitMode, Camera cam)
    {
        if (hudCanvas == null) return;
        if (cockpitMode && cam != null)
        {
            hudCanvas.enabled       = true;
            hudCanvas.renderMode    = RenderMode.ScreenSpaceCamera;
            hudCanvas.worldCamera   = cam;
            hudCanvas.planeDistance = 0.5f;

            if (warningCanvas != null)
            {
                warningCanvas.enabled       = true;
                warningCanvas.renderMode    = RenderMode.ScreenSpaceCamera;
                warningCanvas.worldCamera   = cam;
                warningCanvas.planeDistance = 0.5f;
            }

            GetComponent<GunSystem>()?.SetHUDVisible(true);
            GetComponent<RadarMiniMap>()?.SetVisible(false);

            // HUD 유리 캔버스 최초 진입 시 빌드 (localPlayer transform이 확보된 뒤)
            if (!_hudGlassBuilt)
            {
                if (localPlayer == null) FindRefs();
                if (localPlayer != null)
                    BuildHUDGlass(localPlayer.transform);
            }
            if (_hudGlassCanvas != null) _hudGlassCanvas.gameObject.SetActive(true);
        }
        else
        {
            hudCanvas.enabled     = false;
            hudCanvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            hudCanvas.worldCamera = null;

            if (warningCanvas != null)
            {
                warningCanvas.enabled     = false;
                warningCanvas.renderMode  = RenderMode.ScreenSpaceOverlay;
                warningCanvas.worldCamera = null;
            }

            GetComponent<GunSystem>()?.SetHUDVisible(false);
            GetComponent<RadarMiniMap>()?.SetVisible(true);

            if (_hudGlassCanvas != null) _hudGlassCanvas.gameObject.SetActive(false);
        }
    }

    // ── WorldSpace HUD 콤바이너 유리 캔버스 빌드 ─────────────────────────────
    // 조종사 시점 기준 (0, 0.50, 2.80) 로컬 위치에 실제 HUD 유리처럼 배치.
    // 모든 비행 계기(AH/FPV/Speed/Alt/Hdg/Status/Boresight)를 이 유리면 위에 렌더링.
    void BuildHUDGlass(Transform playerTransform)
    {
        if (_hudGlassCanvas == null)
        {
            var go = new GameObject("HUD_GlassCanvas");
            go.transform.SetParent(playerTransform, false);
            go.transform.localPosition = _glassLocalPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = Vector3.one * _glassLocalScale;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.WorldSpace;
            canvas.sortingOrder = 15;

            var rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = _glassSizeDelta;

            _hudGlassCanvas = canvas;
        }
        else
        {
            // Inspector에서 미리 배치된 경우 플레이어 하위로 이동
            _hudGlassCanvas.transform.SetParent(playerTransform, false);
        }

        Transform p = _hudGlassCanvas.transform;
        BuildHUDGlassBorder(p);
        BuildArtificialHorizon(p);
        BuildFPV(p);
        BuildBankAngleArc(p);
        BuildSpeedTape(p);
        BuildAltTape(p);
        BuildHeadingStrip(p);
        BuildStatusStrip(p);
        BuildBoresight(p);

        _hudGlassBuilt = true;
        _hudGlassCanvas.gameObject.SetActive(false);
    }

    void OnCMSDeploy(CountermeasureType type)
    {
        _deployMsg   = type == CountermeasureType.Flare ? "◎  FLARE" : "≋  CHAFF";
        _deployTimer = 1.6f;
    }

    void FindRefs()
    {
        foreach (var pc in PlayerController.All)
            if (pc != null && pc.isLocalPlayer) { localPlayer = pc; break; }
        flightCamera = FindObjectOfType<FlightCamera>();
    }

    void Update()
    {
        if (localPlayer == null || flightCamera == null) { FindRefs(); return; }
        if (GameModeManager.Instance == null || !GameModeManager.Instance.IsFlying) return;

        bool cockpit = flightCamera.IsCockpit;
        if (hudCanvas != null) hudCanvas.enabled = cockpit;

        UpdateWarningCanvas();

        if (!cockpit) return;
        UpdateHUD();
    }

    // ── 에디터에서 우클릭 → "Build HUD" 로 씬에 미리 빌드 ───────────────────────
    [ContextMenu("Build HUD")]
    void BuildHUDFromEditor()
    {
        fnt = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (hudCanvas     != null) ClearChildren(hudCanvas.transform);
        if (warningCanvas != null) ClearChildren(warningCanvas.transform);
        BuildHUD();
        BuildWarningCanvas();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(gameObject);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ── 상시 표시 경고 캔버스 빌드 ─────────────────────────────────────────
    void BuildWarningCanvas()
    {
        if (warningCanvas == null)
        {
            var go = new GameObject("Warning_Canvas");
            go.transform.SetParent(hudRoot, false);
            warningCanvas = go.AddComponent<Canvas>();
            warningCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            warningCanvas.sortingOrder = 30;
            var cs = go.AddComponent<CanvasScaler>();
            cs.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920, 1080);
            cs.matchWidthOrHeight  = 0.5f;
            go.AddComponent<GraphicRaycaster>();
        }
        else
        {
            warningCanvas.transform.SetParent(hudRoot, false);
        }

        if (missileAlertOverlay == null)
        {
            var overlay = MakeStretch("MissileAlert", warningCanvas.transform);
            missileAlertOverlay = overlay.gameObject.AddComponent<Image>();
            missileAlertOverlay.color = Color.clear;
            missileAlertOverlay.raycastTarget = false;
        }
        if (missileAlertText == null)
        {
            missileAlertText = AddTxt(warningCanvas.transform, "",
                new Vector2(0f, 400f), new Vector2(900f, 60f), 28, CRIT, TextAnchor.MiddleCenter);
            missileAlertText.fontStyle = FontStyle.Bold;
        }
        if (missileDistText == null)
            missileDistText = AddTxt(warningCanvas.transform, "",
                new Vector2(0f, 360f), new Vector2(400f, 30f), 14, WARN, TextAnchor.MiddleCenter);
        if (cmsDeployText == null)
        {
            cmsDeployText = AddTxt(warningCanvas.transform, "",
                new Vector2(700f, 380f), new Vector2(200f, 36f), 18, WARN, TextAnchor.MiddleCenter);
            cmsDeployText.fontStyle = FontStyle.Bold;
        }

        BuildWarningRWR();
        BuildILSIndicator();
    }

    [Header("Refs — Warning RWR")]
    [SerializeField] RectTransform _warnRwrNeedle;
    [SerializeField] Text          _warnRwrLabel;

    void BuildWarningRWR()
    {
        if (_warnRwrNeedle != null) return;
        var root = Rect("WRWR", warningCanvas.transform, new Vector2(-800f, 0f), new Vector2(90f, 90f));
        Img(root, new Color(0f, 0f, 0f, 0.60f));
        var ol = root.gameObject.AddComponent<Outline>();
        ol.effectColor    = new Color(HG.r, HG.g, HG.b, 0.55f);
        ol.effectDistance = new Vector2(1.5f, 1.5f);

        AddTxt(root, "RWR", new Vector2(0f, 30f), new Vector2(80f, 16f), 8, HGX, TextAnchor.MiddleCenter);
        Img(Rect("WH", root, Vector2.zero, new Vector2(60f, 1f)), new Color(HG.r, HG.g, HG.b, 0.22f));
        Img(Rect("WV", root, Vector2.zero, new Vector2(1f, 60f)), new Color(HG.r, HG.g, HG.b, 0.22f));
        var ring = Rect("Ring", root, Vector2.zero, new Vector2(54f, 54f));
        ring.gameObject.AddComponent<Image>().color = Color.clear;
        var ringOl = ring.gameObject.AddComponent<Outline>();
        ringOl.effectColor    = new Color(HG.r, HG.g, HG.b, 0.20f);
        ringOl.effectDistance = new Vector2(1f, 1f);

        _warnRwrNeedle = Rect("WNeedle", root, new Vector2(0f, 10f), new Vector2(3f, 28f));
        _warnRwrNeedle.pivot = new Vector2(0.5f, 0f);
        Img(_warnRwrNeedle, CRIT);
        _warnRwrNeedle.gameObject.SetActive(false);

        _warnRwrLabel = AddTxt(root, "", new Vector2(0f, -32f), new Vector2(80f, 14f), 7, HGD, TextAnchor.MiddleCenter);
    }

    // ── 상시 경고 캔버스 업데이트 ──────────────────────────────────────────
    void UpdateWarningCanvas()
    {
        if (warningCanvas == null || threatWarn == null) return;

        float t = Time.time;
        bool  missileInbound = threatWarn.MissileIncoming;

        if (missileInbound)
        {
            float pulse = (Mathf.Sin(t * 9f) + 1f) * 0.5f;
            missileAlertOverlay.color = new Color(CRIT.r, CRIT.g, CRIT.b, pulse * 0.14f);
        }
        else
        {
            missileAlertOverlay.color = Color.clear;
        }

        if (missileAlertText != null)
        {
            if (missileInbound)
            {
                bool blink = (t % 0.22f) < 0.11f;
                missileAlertText.text  = blink ? "⚠  MISSILE INBOUND  ⚠" : "";
                missileAlertText.color = CRIT;
            }
            else if (threatWarn.Threat >= ThreatWarningSystem.ThreatLevel.Locked)
            {
                bool blink = (t % 0.45f) < 0.225f;
                missileAlertText.text  = blink ? "◉  LOCK WARNING" : "";
                missileAlertText.color = CRIT;
            }
            else if (threatWarn.Threat == ThreatWarningSystem.ThreatLevel.Tracked)
            {
                missileAlertText.text  = "RADAR TRACKING";
                missileAlertText.color = WARN;
            }
            else
            {
                missileAlertText.text = "";
            }
        }

        if (missileDistText != null)
        {
            if (missileInbound && threatWarn.NearestMissileDist < float.MaxValue)
            {
                float d = threatWarn.NearestMissileDist;
                missileDistText.text  = d < 1000f ? $"IMPACT  {d:F0} m" : $"IMPACT  {d/1000f:F1} km";
                missileDistText.color = d < 500f ? CRIT : WARN;
            }
            else
            {
                missileDistText.text = "";
            }
        }

        if (cmsDeployText != null)
        {
            if (_deployTimer > 0f)
            {
                _deployTimer -= Time.deltaTime;
                float alpha   = Mathf.Clamp01(_deployTimer);
                cmsDeployText.text  = _deployMsg;
                cmsDeployText.color = new Color(WARN.r, WARN.g, WARN.b, alpha);
            }
            else
            {
                cmsDeployText.text = "";
            }
        }

        if (_warnRwrNeedle != null)
        {
            bool show = threatWarn.Threat > ThreatWarningSystem.ThreatLevel.None;
            _warnRwrNeedle.gameObject.SetActive(show);
            if (show)
            {
                _warnRwrNeedle.localRotation = Quaternion.Euler(0f, 0f, -threatWarn.ThreatBearing);
                var needleImg = _warnRwrNeedle.GetComponent<Image>();
                if (needleImg != null)
                    needleImg.color = missileInbound ? CRIT : WARN;
            }
            if (_warnRwrLabel != null)
            {
                _warnRwrLabel.text = threatWarn.Threat switch
                {
                    ThreatWarningSystem.ThreatLevel.MissileActive => "ARH",
                    ThreatWarningSystem.ThreatLevel.MissileFired  => "MSL",
                    ThreatWarningSystem.ThreatLevel.Locked        => "LCK",
                    ThreatWarningSystem.ThreatLevel.Tracked       => "TRK",
                    ThreatWarningSystem.ThreatLevel.Detected      => "DET",
                    _                                              => ""
                };
                _warnRwrLabel.color = missileInbound ? CRIT : WARN;
            }
        }

        UpdateILS();
    }

    // ── ILS 인디케이터 빌드 ──────────────────────────────────────────────────
    // 화면 하단 중앙에 로컬라이저(좌우)·글라이드슬로프(상하) 십자 편차 표시.
    // ILS 비컨 수신 범위 내 진입 시 자동 표시, 벗어나면 자동 숨김.
    void BuildILSIndicator()
    {
        if (_ilsPanel != null) return;
        float pw = 160f, ph = 160f;
        _ilsPanel = Rect("ILS_Panel", warningCanvas.transform, new Vector2(-680f, -330f), new Vector2(pw, ph));
        Img(_ilsPanel, new Color(0f, 0f, 0f, 0.65f));

        // 스케일 도트 (±1/2 풀스케일)
        float half = ILS_DEFLECT_PX * 0.5f;
        Color dot  = new Color(HG.r, HG.g, HG.b, 0.45f);
        for (int s = -1; s <= 1; s += 2)
        {
            Img(Rect($"ILS_DH{s}", _ilsPanel, new Vector2(half * s, 0f), new Vector2(4f, 4f)), dot);
            Img(Rect($"ILS_DV{s}", _ilsPanel, new Vector2(0f, half * s), new Vector2(4f, 4f)), dot);
        }

        // 중심 고정 십자선
        Img(Rect("ILS_CX", _ilsPanel, Vector2.zero, new Vector2(28f, 2f)), HGD);
        Img(Rect("ILS_CY", _ilsPanel, Vector2.zero, new Vector2(2f, 28f)), HGD);

        // 중심 다이아몬드
        var diam = Rect("ILS_Diam", _ilsPanel, Vector2.zero, new Vector2(8f, 8f));
        diam.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Img(diam, HG);

        // 로컬라이저 바 (수직 막대, 좌우로 이동)
        _ilsLocBar = Rect("ILS_Loc", _ilsPanel, Vector2.zero, new Vector2(4f, 56f));
        Img(_ilsLocBar, HG);
        _ilsLocImg = _ilsLocBar.GetComponent<Image>();

        // 글라이드슬로프 바 (수평 막대, 상하로 이동)
        _ilsGsBar = Rect("ILS_GS", _ilsPanel, Vector2.zero, new Vector2(56f, 4f));
        Img(_ilsGsBar, HG);
        _ilsGsImg = _ilsGsBar.GetComponent<Image>();

        // 레이블
        _ilsLabel    = AddTxt(_ilsPanel, "ILS",  new Vector2(0f,  ph * 0.5f - 14f), new Vector2(pw, 18f), 10, HG,  TextAnchor.MiddleCenter);
        _ilsDistText = AddTxt(_ilsPanel, "",      new Vector2(0f, -ph * 0.5f + 10f), new Vector2(pw, 16f), 9,  HGD, TextAnchor.MiddleCenter);

        _ilsPanel.gameObject.SetActive(false);
    }

    // ── ILS 인디케이터 업데이트 ──────────────────────────────────────────────
    void UpdateILS()
    {
        if (_ilsPanel == null) return;

        bool flying = GameModeManager.Instance != null && GameModeManager.Instance.IsFlying;
        if (!flying || localPlayer == null)
        {
            _ilsPanel.gameObject.SetActive(false);
            return;
        }

        // 1초마다 가장 가까운 ILS 비컨 탐색
        _ilsScanTimer -= Time.deltaTime;
        if (_ilsScanTimer <= 0f)
        {
            _ilsScanTimer = ILS_SCAN_INTERVAL;
            _activeILS    = FindNearestILS(localPlayer.transform.position);
        }

        if (_activeILS == null)
        {
            _ilsPanel.gameObject.SetActive(false);
            return;
        }

        _ilsPanel.gameObject.SetActive(true);

        Vector3 pos  = localPlayer.transform.position;
        float   dist = Vector3.Distance(pos, _activeILS.transform.position);

        float latDev = _activeILS.LateralDeviation(pos);
        float vDev   = _activeILS.VerticalDeviation(pos);

        // 편차를 픽셀로 변환 (로컬라이저: 좌우 반전 없음, 글라이드슬로프: 위 = 강하 필요)
        float locPx = Mathf.Clamp(-latDev / ILS_DEFLECT_M * ILS_DEFLECT_PX, -ILS_DEFLECT_PX, ILS_DEFLECT_PX);
        float gsPx  = Mathf.Clamp(-vDev   / ILS_DEFLECT_M * ILS_DEFLECT_PX, -ILS_DEFLECT_PX, ILS_DEFLECT_PX);

        if (_ilsLocBar != null) _ilsLocBar.anchoredPosition = new Vector2(locPx, 0f);
        if (_ilsGsBar  != null) _ilsGsBar.anchoredPosition  = new Vector2(0f, gsPx);

        // 큰 편차 시 경고색
        bool offScale = Mathf.Abs(latDev) > ILS_DEFLECT_M * 0.75f
                     || Mathf.Abs(vDev)   > ILS_DEFLECT_M * 0.75f;
        Color barCol = offScale ? WARN : HG;
        if (_ilsLocImg != null) _ilsLocImg.color = barCol;
        if (_ilsGsImg  != null) _ilsGsImg.color  = barCol;

        if (_ilsDistText != null)
            _ilsDistText.text = dist < 1000f ? $"{dist:F0} m" : $"{dist / 1000f:F1} km";
    }

    ILSBeacon FindNearestILS(Vector3 pos)
    {
        var       beacons = FindObjectsOfType<ILSBeacon>();
        ILSBeacon nearest = null;
        float     minDist = float.MaxValue;
        foreach (var b in beacons)
        {
            if (!b.IsInRange(pos)) continue;
            float d = Vector3.Distance(pos, b.transform.position);
            if (d < minDist) { minDist = d; nearest = b; }
        }
        return nearest;
    }

    // ═══════════════════════════════════════════════════════════════════════
    void BuildHUD()
    {
        fnt = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        if (hudRoot == null)
        {
            var rootGO = new GameObject("HUD");
            hudRoot = rootGO.transform;
        }

        if (hudCanvas == null)
        {
            var go = new GameObject("HUD_Canvas");
            go.transform.SetParent(hudRoot, false);
            hudCanvas = go.AddComponent<Canvas>();
            hudCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            hudCanvas.sortingOrder = 20;
            var sc = go.AddComponent<CanvasScaler>();
            sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight  = 0.5f;
            go.AddComponent<GraphicRaycaster>();
        }
        else
        {
            hudCanvas.transform.SetParent(hudRoot, false);
        }

        hudCanvas.enabled = false;

        // 콕핏 프레임(캐노피·글레어실드)만 hudCanvas에 — 계기류는 BuildHUDGlass에서 WorldSpace에 빌드
        BuildCockpitFrame();

        targeting  = GetComponent<TargetingSystem>()      ?? gameObject.AddComponent<TargetingSystem>();
        launcher   = GetComponent<MissileLauncher>()      ?? gameObject.AddComponent<MissileLauncher>();
        threatWarn = GetComponent<ThreatWarningSystem>()  ?? gameObject.AddComponent<ThreatWarningSystem>();
        cmsys      = GetComponent<CountermeasureSystem>() ?? gameObject.AddComponent<CountermeasureSystem>();
        if (GetComponent<HitEffectSystem>() == null) gameObject.AddComponent<HitEffectSystem>();
        if (GetComponent<GunSystem>()       == null) gameObject.AddComponent<GunSystem>();

        BuildTargetingOverlay();
    }

    // ── 인공수평선: WorldSpace 유리 캔버스(parent)에 빌드 ──────────────────────
    void BuildArtificialHorizon(Transform parent)
    {
        var mask = Rect("AH_Mask", parent, Vector2.zero, _ahMaskSize);
        mask.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        ahRoot  = Rect("AH_Root",  mask,   Vector2.zero, _ahMaskSize);
        ahSlide = Rect("AH_Slide", ahRoot, Vector2.zero, new Vector2(_ahMaskSize.x, 3200f));

        Img(Rect("HL", ahSlide, Vector2.zero, new Vector2(_ahMaskSize.x, 2f)), HG);

        var ladder = Rect("Ladder", ahSlide, Vector2.zero, new Vector2(_ahMaskSize.x, 3200f));
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

    // ── 콕핏 프레임 (캐노피 보우 + 글레어실드) ─────────────────────────────
    void BuildCockpitFrame()
    {
        // 프레임 구조 — 씬에 없을 때만 생성
        if (hudCanvas.transform.Find("FrameL") == null)
        {
            var L = Rect("FrameL", hudCanvas.transform, _frameLPos, _frameLSize);
            Img(L, PANEL);
            Img(Rect("FLE", hudCanvas.transform, new Vector2(_frameLPos.x + 44f, 0f), new Vector2(2f, _frameLSize.y)),
                new Color(HG.r, HG.g, HG.b, 0.14f));

            var R = Rect("FrameR", hudCanvas.transform, _frameRPos, _frameRSize);
            Img(R, PANEL);
            Img(Rect("FRE", hudCanvas.transform, new Vector2(_frameRPos.x - 44f, 0f), new Vector2(2f, _frameRSize.y)),
                new Color(HG.r, HG.g, HG.b, 0.14f));

            var T = Rect("CanopyTop", hudCanvas.transform, _canopyTopPos, _canopyTopSize);
            Img(T, PANEL);
            AddTxt(T, "KAI  KF-21 BORAMAE",
                Vector2.zero, new Vector2(700f, 28f), 10, HGX, TextAnchor.MiddleCenter);
            Img(Rect("TopEdge", hudCanvas.transform, new Vector2(0f, _canopyTopPos.y - 16f), new Vector2(_canopyTopSize.x, 1f)),
                new Color(HG.r, HG.g, HG.b, 0.18f));
        }

        // 글레어실드 L — 씬에 있으면 Find, 없으면 생성
        var gsLt = hudCanvas.transform.Find("GlareshieldL");
        var BL = gsLt != null ? gsLt.GetComponent<RectTransform>()
                              : Rect("GlareshieldL", hudCanvas.transform, _gsLPos, _gsLSize);
        if (gsLt == null)
        {
            Img(BL, PANEL);
            Img(Rect("GsEdgeL", hudCanvas.transform, new Vector2(_gsLPos.x, _gsLPos.y + _gsLSize.y * 0.5f), new Vector2(_gsLSize.x, 1f)),
                new Color(HG.r, HG.g, HG.b, 0.18f));
        }
        BuildGlareshieldLeft(BL);

        // 글레어실드 R
        var gsRt = hudCanvas.transform.Find("GlareshieldR");
        var BR = gsRt != null ? gsRt.GetComponent<RectTransform>()
                              : Rect("GlareshieldR", hudCanvas.transform, _gsRPos, _gsRSize);
        if (gsRt == null)
        {
            Img(BR, PANEL);
            Img(Rect("GsEdgeR", hudCanvas.transform, new Vector2(_gsRPos.x, _gsRPos.y + _gsRSize.y * 0.5f), new Vector2(_gsRSize.x, 1f)),
                new Color(HG.r, HG.g, HG.b, 0.18f));
        }
        BuildGlareshieldRight(BR);
    }

    // ── 좌측 글레어실드 (엔진/연료/클럭 + SA/EW MFD) ─────────────────────────
    void BuildGlareshieldLeft(RectTransform p)
    {
        float ex = _engScrX  - _gsLPos.x;  // 엔진 섹션 로컬 x
        float lx = _mfdLScrX - _gsLPos.x;  // 좌측 MFD 로컬 x

        // 엔진·N1 섹션
        if (n1Text == null)
        {
            AddTxt(p, "ENGINE", new Vector2(ex, 118f), new Vector2(110f, 20f), 9, HGD, TextAnchor.MiddleCenter);
            Img(Rect("LD1", p, new Vector2(ex, 107f), new Vector2(100f, 1f)), HGX);
            AddTxt(p, "N1", new Vector2(ex - 30f, 88f), new Vector2(38f, 20f), 9, HGD, TextAnchor.MiddleLeft);
            n1Text = AddTxt(p, "30%", new Vector2(ex + 25f, 88f), new Vector2(58f, 20f), 10, HG, TextAnchor.MiddleRight);
            var n1BG = Rect("N1BG", p, new Vector2(ex, 72f), new Vector2(88f, 8f));
            Img(n1BG, new Color(HG.r*.1f, HG.g*.1f, HG.b*.1f, 0.8f));
            n1Fill = Rect("N1F", p, new Vector2(ex - 44f, 72f), new Vector2(0f, 8f));
            n1Fill.pivot = new Vector2(0f, 0.5f); Img(n1Fill, HG);
        }

        // 연료 섹션
        if (fuelText == null)
        {
            AddTxt(p, "FUEL", new Vector2(ex, 46f), new Vector2(110f, 20f), 9, HGD, TextAnchor.MiddleCenter);
            Img(Rect("LD2", p, new Vector2(ex, 35f), new Vector2(100f, 1f)), HGX);
            fuelText = AddTxt(p, "100.0%", new Vector2(ex, 17f), new Vector2(100f, 20f), 11, HG, TextAnchor.MiddleCenter);
            var fBG = Rect("FBG", p, new Vector2(ex, -1f), new Vector2(88f, 8f));
            Img(fBG, new Color(HG.r*.1f, HG.g*.1f, HG.b*.1f, 0.8f));
            fuelFill = Rect("FF", p, new Vector2(ex - 44f, -1f), new Vector2(88f, 8f));
            fuelFill.pivot = new Vector2(0f, 0.5f); Img(fuelFill, HG);
        }

        // 미션 클럭·모드
        if (clockText == null)
        {
            AddTxt(p, "MIS TIME", new Vector2(ex, -28f), new Vector2(100f, 16f), 7, HGX, TextAnchor.MiddleCenter);
            clockText = AddTxt(p, "00:00:00", new Vector2(ex, -46f), new Vector2(100f, 20f), 10, HGD, TextAnchor.MiddleCenter);
        }
        if (modeText == null)
        {
            modeText = AddTxt(p, "NAV", new Vector2(ex, -78f), new Vector2(100f, 26f), 13, HG, TextAnchor.MiddleCenter);
            AddTxt(p, "A/P OFF", new Vector2(ex, -102f), new Vector2(100f, 18f), 8, HGX, TextAnchor.MiddleCenter);
        }

        // SA/EW MFD
        if (p.Find("MFDL") != null) return;
        var lMFD = Rect("MFDL", p, new Vector2(lx, 8f), new Vector2(220f, 170f));
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
    }

    // ── 우측 글레어실드 (NAV/HSI MFD + 무장/시스템) ──────────────────────────
    void BuildGlareshieldRight(RectTransform p)
    {
        float rx = _mfdRScrX - _gsRPos.x;  // 우측 MFD 로컬 x
        float wx = _wpnScrX  - _gsRPos.x;  // 무장 섹션 로컬 x

        // NAV/HSI MFD
        if (p.Find("MFDR") == null)
        {
            var rMFD = Rect("MFDR", p, new Vector2(rx, 8f), new Vector2(220f,170f));
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
        }

        // 무장 섹션
        if (weaponText == null)
        {
            AddTxt(p, "WEAPON", new Vector2(wx, 118f), new Vector2(110f,20f), 9, HGD, TextAnchor.MiddleCenter);
            Img(Rect("RD1", p, new Vector2(wx,107f), new Vector2(100f,1f)), HGX);
            weaponText = AddTxt(p, "SAFE", new Vector2(wx,88f), new Vector2(100f,26f), 13, HGD, TextAnchor.MiddleCenter);
            if (aim120CountText == null)
                aim120CountText = AddTxt(p, "AIM-120  x4", new Vector2(wx,66f), new Vector2(110f,18f), 8, HGX, TextAnchor.MiddleCenter);
            AddTxt(p, "AIM-9X   x2", new Vector2(wx,48f), new Vector2(110f,18f), 8, HGX, TextAnchor.MiddleCenter);
            AddTxt(p, "SYSTEMS", new Vector2(wx,20f), new Vector2(110f,20f), 9, HGD, TextAnchor.MiddleCenter);
            Img(Rect("RD2", p, new Vector2(wx,9f), new Vector2(100f,1f)), HGX);
            if (gearText == null)
                gearText = AddTxt(p, "GEAR  UP", new Vector2(wx,-10f), new Vector2(110f,20f), 9, HGD, TextAnchor.MiddleCenter);
            AddTxt(p, "FLAP  RET",  new Vector2(wx,-30f), new Vector2(110f,20f), 9, HGD, TextAnchor.MiddleCenter);
            AddTxt(p, "ECM  STBY",  new Vector2(wx,-50f), new Vector2(110f,20f), 9, HGD, TextAnchor.MiddleCenter);
            AddTxt(p, "IFF  ON",    new Vector2(wx,-70f), new Vector2(110f,20f), 9, HGD, TextAnchor.MiddleCenter);
            AddTxt(p, "APG-81  ACT",new Vector2(wx,-96f), new Vector2(110f,20f), 9, HGD, TextAnchor.MiddleCenter);
        }

        // 카운터메저 섹션
        if (flareCountText == null)
        {
            AddTxt(p, "COUNTERMEASURE", new Vector2(wx,-116f), new Vector2(130f,14f), 7, HGX, TextAnchor.MiddleCenter);
            Img(Rect("CMD", p, new Vector2(wx,-126f), new Vector2(110f,1f)), HGX);
            flareCountText = AddTxt(p, "FLARE  30", new Vector2(wx,-138f), new Vector2(110f,16f), 9, HGD, TextAnchor.MiddleCenter);
            chaffCountText = AddTxt(p, "CHAFF  30", new Vector2(wx,-152f), new Vector2(110f,16f), 9, HGD, TextAnchor.MiddleCenter);
        }
    }

    // ── HUD 유리 코너 브래킷 (WorldSpace 유리 캔버스 경계 표시) ───────────────
    void BuildHUDGlassBorder(Transform parent)
    {
        float hw=_glassBorderHW, hh=_glassBorderHH, arm=60f, thk=2f;
        Color bc = new Color(HG.r, HG.g, HG.b, 0.50f);
        MkBracket(parent, -hw,  hh,  1f,-1f, arm, thk, bc);
        MkBracket(parent,  hw,  hh, -1f,-1f, arm, thk, bc);
        MkBracket(parent, -hw, -hh,  1f, 1f, arm, thk, bc);
        MkBracket(parent,  hw, -hh, -1f, 1f, arm, thk, bc);
        Color bm = new Color(HG.r,HG.g,HG.b,0.38f);
        Img(Rect("TML",parent,new Vector2(-46f, hh),new Vector2(38f,thk)),bm);
        Img(Rect("TMR",parent,new Vector2( 46f, hh),new Vector2(38f,thk)),bm);
        Img(Rect("TMC",parent,new Vector2(0f,hh+5f),new Vector2(thk,12f)),bm);
        Img(Rect("BML",parent,new Vector2(-46f,-hh),new Vector2(38f,thk)),bm);
        Img(Rect("BMR",parent,new Vector2( 46f,-hh),new Vector2(38f,thk)),bm);
        Img(Rect("BMC",parent,new Vector2(0f,-hh-5f),new Vector2(thk,12f)),bm);
    }

    void MkBracket(Transform parent, float cx, float cy, float sx, float sy, float arm, float thk, Color c)
    {
        Img(Rect("BH", parent, new Vector2(cx+sx*arm*.5f, cy), new Vector2(arm, thk)), c);
        Img(Rect("BV", parent, new Vector2(cx, cy+sy*arm*.5f), new Vector2(thk, arm)), c);
    }

    // ── 속도 테이프 ──────────────────────────────────────────────────────────
    void BuildSpeedTape(Transform parent)
    {
        float px = _spdTapeX;
        var panel = Rect("SpdPanel", parent, new Vector2(px,0f), new Vector2(130f,SPD_LINES*SPD_PX+24f));
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
        var mach=Rect("MP",parent,new Vector2(px,-(SPD_LINES*SPD_PX/2f+50f)),new Vector2(130f,30f));
        Img(mach,new Color(0f,0f,0f,.50f));
        machText=AddTxt(mach,"M 0.00",Vector2.zero,new Vector2(110f,30f),12,HGD,TextAnchor.MiddleCenter);
        BuildThrottleBar(parent, px-22f);
    }

    void BuildThrottleBar(Transform parent, float px)
    {
        var p=Rect("TP",parent,new Vector2(px,0f),new Vector2(14f,SPD_LINES*SPD_PX+24f));
        Img(p,new Color(0f,0f,0f,.50f));
        var bg=Rect("TBG",p,Vector2.zero,new Vector2(8f,SPD_LINES*SPD_PX));
        Img(bg,new Color(HG.r*.07f,HG.g*.07f,HG.b*.07f,.9f));
        throttleBar=Rect("TF",p,new Vector2(0f,-SPD_LINES*SPD_PX/2f),new Vector2(8f,0f));
        throttleBar.pivot=new Vector2(.5f,0f); Img(throttleBar,HG);
    }

    // ── 고도 테이프 ──────────────────────────────────────────────────────────
    void BuildAltTape(Transform parent)
    {
        float px = _altTapeX;
        var panel=Rect("AltPanel",parent,new Vector2(px,0f),new Vector2(130f,ALT_LINES*ALT_PX+24f));
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
        var vsi=Rect("VSI",parent,new Vector2(px,-(ALT_LINES*ALT_PX/2f+48f)),new Vector2(130f,30f));
        Img(vsi,new Color(0f,0f,0f,.50f));
        vsiText=AddTxt(vsi,"VVI  +0",Vector2.zero,new Vector2(120f,30f),11,HGD,TextAnchor.MiddleCenter);
        var ralt=Rect("RALT",parent,new Vector2(px,-(ALT_LINES*ALT_PX/2f+84f)),new Vector2(130f,28f));
        Img(ralt,new Color(0f,0f,0f,.44f));
        raltText=AddTxt(ralt,"RALT ---",Vector2.zero,new Vector2(120f,28f),10,HGD,TextAnchor.MiddleCenter);
    }

    // ── 헤딩 스트립 ──────────────────────────────────────────────────────────
    void BuildHeadingStrip(Transform parent)
    {
        var panel=Rect("HdgPanel",parent,new Vector2(0f,_hdgStripY),new Vector2(HDG_LINES*HDG_PX+24f,50f));
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
    void BuildStatusStrip(Transform parent)
    {
        var panel=Rect("Status",parent,new Vector2(0f,_statusStripY),new Vector2(540f,36f));
        Img(panel,new Color(0f,0f,0f,.54f));
        panel.gameObject.AddComponent<Outline>().effectColor=new Color(HG.r,HG.g,HG.b,.22f);
        aoaText=AddTxt(panel,"AOA +0.0°",new Vector2(-180f,0f),new Vector2(155f,36f),11,HGD,TextAnchor.MiddleCenter);
        gText  =AddTxt(panel,"G  1.0",  new Vector2(0f,0f),   new Vector2(110f,36f),15,HG, TextAnchor.MiddleCenter);
        AddTxt(panel,"SAFE",new Vector2(180f,0f),new Vector2(100f,36f),10,HGD,TextAnchor.MiddleCenter);
        var wp=Rect("Warn",parent,new Vector2(0f,_warnBannerY),new Vector2(360f,38f));
        Img(wp,new Color(0f,0f,0f,0f));
        warnText=AddTxt(wp,"",Vector2.zero,new Vector2(360f,38f),16,WARN,TextAnchor.MiddleCenter);
    }

    // ── 조준점 ────────────────────────────────────────────────────────────────
    void BuildBoresight(Transform parent)
    {
        Color c=new Color(HG.r,HG.g,HG.b,.72f);
        var ct=Rect("BS",parent,Vector2.zero,new Vector2(20f,20f));
        ct.gameObject.AddComponent<Image>().color=Color.clear;
        ct.gameObject.AddComponent<Outline>().effectColor=c;
        ct.GetComponent<Outline>().effectDistance=new Vector2(2f,2f);
        Img(Rect("BHL",parent,new Vector2(-34f,0f),new Vector2(22f,2f)),c);
        Img(Rect("BHR",parent,new Vector2( 34f,0f),new Vector2(22f,2f)),c);
        Img(Rect("BVU",parent,new Vector2(0f, 34f),new Vector2(2f,22f)),c);
        Img(Rect("BVD",parent,new Vector2(0f,-34f),new Vector2(2f,22f)),c);
    }

    // ── FPV ──────────────────────────────────────────────────────────────────
    void BuildFPV(Transform parent)
    {
        fpvRoot=Rect("FPV",parent,Vector2.zero,Vector2.zero);
        var ci=Rect("FC",fpvRoot,Vector2.zero,new Vector2(18f,18f));
        ci.gameObject.AddComponent<Image>().color=Color.clear;
        var ol=ci.gameObject.AddComponent<Outline>();
        ol.effectColor=FPVC; ol.effectDistance=new Vector2(1.5f,1.5f);
        Img(Rect("FL",fpvRoot,new Vector2(-24f,0f),new Vector2(18f,2f)),FPVC);
        Img(Rect("FR",fpvRoot,new Vector2( 24f,0f),new Vector2(18f,2f)),FPVC);
        Img(Rect("FT",fpvRoot,new Vector2(0f,16f), new Vector2(2f,12f)),FPVC);
    }

    // ── 뱅크각 호 + 포인터 ──────────────────────────────────────────────────
    void BuildBankAngleArc(Transform parent)
    {
        int[] marks={-60,-45,-30,-20,-10,0,10,20,30,45,60};
        float r=210f;
        foreach(int deg in marks)
        {
            float rad=(90-deg)*Mathf.Deg2Rad;
            float x=r*Mathf.Cos(rad),y=r*Mathf.Sin(rad);
            float h=deg%30==0?16f:9f;
            var tick=Rect($"BA{deg}",parent,new Vector2(x,y),new Vector2(2f,h));
            tick.localRotation=Quaternion.Euler(0f,0f,-deg);
            Img(tick,HGD);
            if(deg%30==0&&deg!=0)
                AddTxt(parent,$"{Mathf.Abs(deg)}",new Vector2(x*1.14f,y*1.14f),new Vector2(30f,16f),8,HGD,TextAnchor.MiddleCenter);
        }
        bankPivot=Rect("BankPivot",parent,Vector2.zero,Vector2.zero);
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

        if(ahRoot  !=null) ahRoot.localRotation    =Quaternion.Euler(0f,0f,roll);
        if(ahSlide !=null) ahSlide.anchoredPosition=new Vector2(0f,pitch*PX_PER_DEG);
        if(bankPivot!=null) bankPivot.localRotation=Quaternion.Euler(0f,0f,roll);

        UpdateTape(spdLabels,spdLabelRTs,kph,SPD_STEP,SPD_PX,SPD_LINES,false);
        if(spdCurrent!=null) spdCurrent.text=$"{kph:F0}";
        if(machText  !=null) machText.text  =$"M  {mach:F3}";
        float thr=Mathf.Clamp01(localPlayer.CurrentSpeed/80f);
        if(throttleBar!=null) throttleBar.sizeDelta=new Vector2(8f,thr*SPD_LINES*SPD_PX);

        UpdateTape(altLabels,altLabelRTs,t.position.y,ALT_STEP,ALT_PX,ALT_LINES,true);
        if(altCurrent!=null) altCurrent.text=$"{t.position.y:F0}";
        float vvi=vel.y;
        if(vsiText!=null){vsiText.text =$"VVI  {(vvi>=0?"+":"")}{vvi:F0}";vsiText.color=Mathf.Abs(vvi)>30f?WARN:HGD;}
        _raltTimer -= dt;
        if (_raltTimer <= 0f)
        {
            _raltTimer = RALT_INTERVAL;
            _raltCache = Physics.Raycast(t.position, Vector3.down, out RaycastHit hit, 5000f)
                ? $"RALT {hit.distance:F0}m"
                : "RALT ---";
        }
        if(raltText!=null) raltText.text = _raltCache;

        UpdateHeadingTape(yaw);
        if(hdgCurrent!=null) hdgCurrent.text=$"{((int)yaw%360+360)%360:D3}°";

        if(aoaText!=null) aoaText.text=$"AOA  {(aoa>=0?"+":"")}{aoa:F1}°";
        if(gText  !=null){gText.text=$"G  {smoothG:F1}";gText.color=smoothG>7f?CRIT:smoothG>4f?WARN:HG;}

        bool stallWarn=kph<80f&&localPlayer.CurrentSpeed>1f;
        bool gWarn    =smoothG>7f;
        if(warnText!=null){warnText.text=gWarn?"  G-LOCK WARNING":stallWarn?"  STALL":"";warnText.color=gWarn?CRIT:WARN;}

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
        BuildThreatWarningUI();
    }

    void BuildThreatWarningUI()
    {
        if (threatWarningText != null && rwrNeedle != null) return;
        var wp = Rect("ThreatWarn", hudCanvas.transform, new Vector2(0f, 475f), new Vector2(700f, 38f));
        Img(wp, new Color(0f, 0f, 0f, 0f));
        threatWarningText = AddTxt(wp, "", Vector2.zero, new Vector2(700f, 38f), 18, CRIT, TextAnchor.MiddleCenter);

        var rwrRoot = Rect("RWR_Root", hudCanvas.transform, new Vector2(-740f, 200f), new Vector2(60f, 60f));
        Img(rwrRoot, new Color(0f, 0f, 0f, 0.55f));
        var rwrOl = rwrRoot.gameObject.AddComponent<UnityEngine.UI.Outline>();
        rwrOl.effectColor = new Color(HG.r, HG.g, HG.b, 0.5f);
        rwrOl.effectDistance = new Vector2(1f, 1f);
        AddTxt(rwrRoot, "RWR", new Vector2(0f, 20f), new Vector2(56f, 14f), 7, HGX, TextAnchor.MiddleCenter);
        Img(Rect("RH", rwrRoot, Vector2.zero, new Vector2(40f, 1f)), new Color(HG.r,HG.g,HG.b,0.25f));
        Img(Rect("RV", rwrRoot, Vector2.zero, new Vector2(1f, 40f)), new Color(HG.r,HG.g,HG.b,0.25f));
        rwrNeedle = Rect("RWR_Needle", rwrRoot, new Vector2(0f, 8f), new Vector2(2f, 20f));
        rwrNeedle.pivot = new Vector2(0.5f, 0f);
        Img(rwrNeedle, CRIT);
        rwrNeedle.gameObject.SetActive(false);
    }

    void BuildTargetBox()
    {
        if (tdbRoot != null) return;
        tdbRoot = Rect("TDB_Root", hudCanvas.transform, Vector2.zero, Vector2.zero);
        tdbRoot.gameObject.SetActive(false);

        for (int i = 0; i < 8; i++)
        {
            tdbArms[i] = Rect($"TDB_A{i}", tdbRoot, Vector2.zero, new Vector2(20f, 2f));
            Img(tdbArms[i], HG);
        }

        tdbName    = AddTxt(tdbRoot, "", new Vector2(0f, 58f),  new Vector2(160f, 16f), 9,  HGD, TextAnchor.MiddleCenter);
        tdbLabel   = AddTxt(tdbRoot, "", new Vector2(0f, -62f), new Vector2(120f, 18f), 11, HG,  TextAnchor.MiddleCenter);
        tdbRange   = AddTxt(tdbRoot, "", new Vector2(0f, -80f), new Vector2(160f, 16f), 10, HGD, TextAnchor.MiddleCenter);
        tdbClosure = AddTxt(tdbRoot, "", new Vector2(0f, -96f), new Vector2(160f, 16f), 9,  HGD, TextAnchor.MiddleCenter);
        tdbAspect  = AddTxt(tdbRoot, "", new Vector2(0f,-112f), new Vector2(160f, 16f), 9,  HGD, TextAnchor.MiddleCenter);
    }

    void BuildOffScreenIndicator()
    {
        if (offScreenRoot != null) return;
        offScreenRoot = Rect("OSI_Root", hudCanvas.transform, Vector2.zero, new Vector2(28f, 28f));
        offScreenRoot.gameObject.SetActive(false);
        AddTxt(offScreenRoot, "▲", Vector2.zero, new Vector2(28f, 28f), 16, HG, TextAnchor.MiddleCenter);
        offScreenDist = AddTxt(offScreenRoot, "", new Vector2(0f, -26f), new Vector2(90f, 18f), 9, HGD, TextAnchor.MiddleCenter);
    }

    void LayoutTDBArms(float halfSize)
    {
        float arm = 22f, thk = 2f;
        tdbArms[0].anchoredPosition = new Vector2(-halfSize + arm * 0.5f,  halfSize);
        tdbArms[0].sizeDelta        = new Vector2(arm, thk);
        tdbArms[1].anchoredPosition = new Vector2(-halfSize,  halfSize - arm * 0.5f);
        tdbArms[1].sizeDelta        = new Vector2(thk, arm);
        tdbArms[2].anchoredPosition = new Vector2( halfSize - arm * 0.5f,  halfSize);
        tdbArms[2].sizeDelta        = new Vector2(arm, thk);
        tdbArms[3].anchoredPosition = new Vector2( halfSize,  halfSize - arm * 0.5f);
        tdbArms[3].sizeDelta        = new Vector2(thk, arm);
        tdbArms[4].anchoredPosition = new Vector2(-halfSize + arm * 0.5f, -halfSize);
        tdbArms[4].sizeDelta        = new Vector2(arm, thk);
        tdbArms[5].anchoredPosition = new Vector2(-halfSize, -halfSize + arm * 0.5f);
        tdbArms[5].sizeDelta        = new Vector2(thk, arm);
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
        bool blink = tdbFlash % 0.5f < 0.25f;

        bool hasTarget   = targeting.Target != null;
        bool locked      = targeting.State == TargetingSystem.LockState.Locked;
        bool inFireZone  = locked && targeting.IsInFireZone;

        if (hasTarget && targeting.IsTargetOnScreen)
        {
            tdbRoot.gameObject.SetActive(true);
            offScreenRoot.gameObject.SetActive(false);

            tdbRoot.anchoredPosition = targeting.TargetCanvasPos;

            float  halfSize = Mathf.Lerp(65f, 42f, targeting.LockProgress);
            LayoutTDBArms(halfSize);

            Color armCol;
            if (inFireZone)
                armCol = blink ? WARN : new Color(WARN.r, WARN.g, WARN.b, 0.5f);
            else if (locked)
                armCol = HG;
            else
                armCol = blink ? HGD : HGX;

            foreach (var arm in tdbArms)
            {
                var img = arm.GetComponent<Image>();
                if (img != null) img.color = armCol;
            }

            if (inFireZone)
            {
                tdbLabel.text  = blink ? "◉  FIRE" : "◉  FIRE";
                tdbLabel.color = blink ? CRIT : WARN;
            }
            else if (locked)
            {
                tdbLabel.text  = $"STEER  {targeting.BoresightAngle:F0}°";
                tdbLabel.color = WARN;
            }
            else
            {
                tdbLabel.text  = "SRCH";
                tdbLabel.color = blink ? WARN : HGX;
            }

            float km = targeting.TargetRange / 1000f;
            tdbRange.text   = km >= 1f ? $"{km:F1} km" : $"{targeting.TargetRange:F0} m";
            tdbClosure.text = $"CL  {(targeting.ClosureRate >= 0f ? "+" : "")}{targeting.ClosureRate:F0} m/s";
            tdbAspect.text  = $"ASP  {targeting.TargetAspect:F0}°  {AspectLabel(targeting.TargetAspect)}";
            tdbName.text    = targeting.Target.nickname;

            if (weaponText != null)
            {
                if (inFireZone)
                { weaponText.text = "SHOOT"; weaponText.color = blink ? CRIT : WARN; }
                else if (locked)
                { weaponText.text = "LOCK"; weaponText.color = HG; }
                else
                { weaponText.text = "ACQR"; weaponText.color = WARN; }
            }
        }
        else if (hasTarget && !targeting.IsTargetOnScreen)
        {
            tdbRoot.gameObject.SetActive(false);
            offScreenRoot.gameObject.SetActive(true);

            Vector2 dir = targeting.TargetCanvasPos;
            if (dir.sqrMagnitude < 0.001f) dir = Vector2.up;
            float   maxX  = 830f, maxY = 370f;
            float   sX    = Mathf.Abs(dir.x) > 0.001f ? maxX / Mathf.Abs(dir.x) : float.MaxValue;
            float   sY    = Mathf.Abs(dir.y) > 0.001f ? maxY / Mathf.Abs(dir.y) : float.MaxValue;
            offScreenRoot.anchoredPosition = dir * Mathf.Min(sX, sY);

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

        if (aim120CountText != null && launcher != null)
            aim120CountText.text = $"AIM-120  x{launcher.MissileCount}";

        if (cmsys != null)
        {
            if (flareCountText != null)
            {
                flareCountText.text  = $"FLARE  {cmsys.FlareRemaining:D2}";
                flareCountText.color = cmsys.FlareRemaining <= 5 ? WARN : HGD;
            }
            if (chaffCountText != null)
            {
                chaffCountText.text  = $"CHAFF  {cmsys.ChaffRemaining:D2}";
                chaffCountText.color = cmsys.ChaffRemaining <= 5 ? WARN : HGD;
            }
        }

        UpdateThreatWarningUI();
    }

    void UpdateThreatWarningUI()
    {
        if (threatWarn == null || threatWarningText == null) return;

        bool blink = tdbFlash % 0.35f < 0.175f;

        switch (threatWarn.Threat)
        {
            case ThreatWarningSystem.ThreatLevel.MissileActive:
                threatWarningText.text  = blink ? "⚠  SEEKER ACTIVE  ⚠" : "  SEEKER ACTIVE  ";
                threatWarningText.color = CRIT;
                break;
            case ThreatWarningSystem.ThreatLevel.MissileFired:
                threatWarningText.text  = blink ? "⚠  MISSILE INBOUND  ⚠" : "  MISSILE INBOUND  ";
                threatWarningText.color = CRIT;
                break;
            case ThreatWarningSystem.ThreatLevel.Locked:
                threatWarningText.text  = blink ? "◉  LOCK WARNING  ◉" : "";
                threatWarningText.color = CRIT;
                break;
            case ThreatWarningSystem.ThreatLevel.Tracked:
                threatWarningText.text  = blink ? "RADAR TRACKING" : "";
                threatWarningText.color = WARN;
                break;
            case ThreatWarningSystem.ThreatLevel.Detected:
                threatWarningText.text  = "DETECTED";
                threatWarningText.color = HGD;
                break;
            default:
                threatWarningText.text  = "";
                break;
        }

        if (rwrNeedle != null)
        {
            bool showRWR = threatWarn.Threat > ThreatWarningSystem.ThreatLevel.None;
            rwrNeedle.gameObject.SetActive(showRWR);
            if (showRWR)
            {
                rwrNeedle.localRotation = Quaternion.Euler(0f, 0f, -threatWarn.ThreatBearing);
                var img = rwrNeedle.GetComponent<UnityEngine.UI.Image>();
                if (img != null) img.color = threatWarn.MissileIncoming ? CRIT : WARN;
            }
        }
    }

    static string AspectLabel(float deg)
    {
        if (deg < 30f)  return "TAIL";
        if (deg < 90f)  return "BEAM";
        if (deg < 150f) return "NOSE";
        return "HTB";
    }

    static void ClearChildren(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--)
            DestroyImmediate(t.GetChild(i).gameObject);
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

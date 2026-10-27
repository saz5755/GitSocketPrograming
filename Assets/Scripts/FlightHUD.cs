using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 콕핏 시점 HUD.
///  - HUD_Canvas (ScreenSpaceCamera, 씬에 베이크): 시스템/무장 카드, RWR, 위협 배너, 타겟 박스, 경고, ILS.
///  - HUD_GlassCanvas (WorldSpace, 런타임 빌드): 조종사 시선 앞 HUD 유리 — 피치 래더/FPV/속도·고도 테이프/방위.
/// 그래픽은 Resources/HUD 이미지 키트(HudSprites)를 틴트해서 쓴다 — 에셋 스프라이트라 씬 베이크 후에도 유지된다.
/// 콕핏 3D 모델과 MFD 를 가리는 장식 프레임은 두지 않는다 (시야 우선).
/// </summary>
public class FlightHUD : MonoBehaviour
{
    // ── 색상 팔레트 (CombatHud 와 같은 HUD 녹색, 경고/위험은 디자인 시스템 Warning/Error) ──────
    static readonly Color HG    = new Color(0.20f, 1.00f, 0.55f, 0.95f);
    static readonly Color HGD   = new Color(0.20f, 1.00f, 0.55f, 0.66f);
    static readonly Color HGX   = new Color(0.20f, 1.00f, 0.55f, 0.32f);
    static readonly Color INK   = new Color(0.90f, 1.00f, 0.95f, 0.96f);
    static readonly Color FPVC  = new Color(0.20f, 1.00f, 0.55f, 0.92f);
    static readonly Color WARN  = new Color(0.96f, 0.72f, 0.29f, 1.00f);   // #F4B84A
    static readonly Color CRIT  = new Color(0.94f, 0.36f, 0.41f, 1.00f);   // #F05B68
    static readonly Color GLASS = new Color(0.02f, 0.05f, 0.08f, 0.62f);   // 카드 면 (#06111F 계열)
    static readonly Color EDGE  = new Color(0.20f, 1.00f, 0.55f, 0.26f);
    static readonly Color HALO  = new Color(0.00f, 0.05f, 0.03f, 0.75f);
    static readonly Color HGL   = new Color(0.20f, 1.00f, 0.55f, 0.85f);   // 유리 위 보조 글자 (구름 배경에서도 읽히게)   // 구름 위 글자 가독성용 외곽
    static readonly Color ExhGlow = new Color(0.95f, 0.55f, 0.12f, 1.00f);

    static readonly int[] PitchMarks = { -60,-50,-40,-30,-20,-10,-5, 5,10,20,30,40,50,60 };
    const float PX_PER_DEG = 7.5f;

    const int   SPD_LINES = 9;  const float SPD_STEP =  20f; const float SPD_PX = 34f;
    const int   ALT_LINES = 9;  const float ALT_STEP = 100f; const float ALT_PX = 34f;
    const int   HDG_LINES = 15; const float HDG_STEP =  10f; const float HDG_PX = 44f;

    [SerializeField] Canvas    hudCanvas;       // HUD_Canvas — Inspector에서 미리 배치된 Canvas 할당 가능
    [SerializeField] Canvas    _hudGlassCanvas; // HUD_GlassCanvas (WorldSpace) — Inspector에서 할당 가능
    [SerializeField] Canvas    warningCanvas;   // 비우면 경고 요소는 HUD_Canvas/Warning 그룹에 둔다
    [SerializeField] Transform hudRoot;         // HUD_Canvas·GunOverlay_Canvas 공통 부모

    public Transform HudRoot => hudRoot;
    bool _hudGlassBuilt;

    // ── 레이아웃 — 콕핏 패널 (HUD_Canvas, 1920x1080 기준) ─────────────────────
    [Header("Layout — Cockpit Panels")]
    [SerializeField] Vector2 _sysCardPos    = new Vector2(-700f, -372f);
    [SerializeField] Vector2 _wpnCardPos    = new Vector2( 700f, -372f);
    [SerializeField] Vector2 _cardSize      = new Vector2( 264f,  176f);
    [SerializeField] Vector2 _rwrPos        = new Vector2(-790f,  130f);
    [SerializeField] float   _rwrSize       = 150f;
    [SerializeField] Vector2 _ilsPos        = new Vector2(-455f, -360f);
    [SerializeField] float   _threatBannerY = 478f;

    // ── 레이아웃 — HUD 유리 캔버스 ──────────────────────────────────────────────
    // 조종사 눈(FlightCamera.CockpitOffset) 정면 _glassDistance(m)에 세운다. 1 캔버스 px ≈ 0.8 화면 px (1080p).
    [Header("Layout — HUD Glass Canvas")]
    [SerializeField] float   _glassDistance  = 1.10f;
    [SerializeField] float   _glassScale     = 0.00085f;
    [SerializeField] Vector2 _glassSizeDelta = new Vector2(1920f, 1080f);
    [SerializeField] float   _glassCornerHW  = 700f;
    [SerializeField] float   _glassCornerHH  = 420f;

    // ── 레이아웃 — 유리 계기 ─────────────────────────────────────────────────────
    [Header("Layout — Glass Instruments")]
    [SerializeField] float   _spdX       = -560f;
    [SerializeField] float   _altX       =  560f;
    [SerializeField] float   _hdgY       =  290f;
    [SerializeField] float   _statusY    = -330f;
    [SerializeField] float   _warnY      =  262f;
    [SerializeField] Vector2 _ahSize     = new Vector2(1000f, 500f);   // 위쪽은 헤딩 스트립과 겹치지 않게

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

    // ── 시스템 카드 (좌하단) ──────────────────────────────────────────────────
    [Header("Refs — Systems Card")]
    [SerializeField] Text  n1Text, fuelText, clockText, modeText;
    [SerializeField] Image n1Bar, fuelBar;

    // ── 무장 카드 (우하단) ────────────────────────────────────────────────────
    [Header("Refs — Stores Card")]
    [SerializeField] Text weaponText, gearText;
    [SerializeField] Text aim120CountText, flareCountText, chaffCountText;

    // ── 타겟팅 오버레이 UI refs ───────────────────────────────────────────────
    [Header("Refs — Targeting")]
    TargetingSystem    targeting;
    MissileLauncher    launcher;
    ThreatWarningSystem threatWarn;
    CountermeasureSystem cmsys;
    [SerializeField] RectTransform   tdbRoot;
    Image[]         tdbCorners = new Image[4];
    [SerializeField] Text            tdbLabel, tdbRange, tdbClosure, tdbAspect, tdbName;
    [SerializeField] RectTransform   offScreenRoot;
    [SerializeField] Text            offScreenDist;
    float           tdbFlash;

    static readonly float[] CornerRot = { 0f, -90f, 180f, 90f };   // TL, TR, BR, BL

    // ── 적기·아군 마커 (비행 중인 모든 비로컬 플레이어에 표시) ─────────────────
    const int MAX_ENEMY_MARKERS = 8;
    RectTransform[] _emRoots  = new RectTransform[MAX_ENEMY_MARKERS];
    Text[]          _emNicks  = new Text[MAX_ENEMY_MARKERS];
    Image[]         _emIcons  = new Image[MAX_ENEMY_MARKERS];  // 심볼 (AI 적기 vs 인간)

    // ── 위협 경고 / RWR ───────────────────────────────────────────────────────
    [Header("Refs — Threat / RWR")]
    [SerializeField] Text          threatWarningText;
    [SerializeField] Image         _threatPlate, _threatPlateLine;
    [SerializeField] RectTransform rwrNeedle;      // 위협 방위로 회전하는 컨테이너 (자식: 라인 + 블립)
    [SerializeField] Text          _warnRwrLabel;  // 위협 단계 (DET/TRK/LCK/MSL/ARH)
    Image _rwrLine, _rwrBlip;

    // ── 경고 그룹 UI refs ─────────────────────────────────────────────────────
    [Header("Refs — Warning")]
    [SerializeField] Image   missileAlertOverlay;   // 가장자리 비네트 (화면 중앙은 가리지 않음)
    [SerializeField] Text    missileDistText;
    [SerializeField] Text    cmsDeployText;
    float   _deployTimer;
    string  _deployMsg;

    Vector3 prevPos; bool prevPosSet;
    Vector3 prevVel;
    int   _lastHudFrame, _gSettle;
    float smoothG = 1f, missionTime = 0f, fuelLevel = 100f;

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
        if (GetComponent<CombatHud>()        == null) gameObject.AddComponent<CombatHud>();
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
    /// 코크핏 모드: 콕핏 패널+타겟팅 캔버스를 ScreenSpaceCamera로 전환하고,
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
    // 조종사 눈 정면(기수 방향) _glassDistance 위치에 세워 보어사이트가 기수 축과 일치하게 한다.
    // 모든 비행 계기(AH/FPV/Speed/Alt/Hdg/Status/Boresight)를 이 유리면 위에 렌더링.
    void BuildHUDGlass(Transform playerTransform)
    {
        if (_hudGlassCanvas == null)
        {
            var go = new GameObject("HUD_GlassCanvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.WorldSpace;
            canvas.sortingOrder = 15;
            _hudGlassCanvas = canvas;
        }
        AttachHUDGlass(playerTransform);
        Transform p = _hudGlassCanvas.transform;

        BuildHUDGlassCorners(p);
        BuildArtificialHorizon(p);
        BuildBoresight(p);
        BuildFPV(p);
        BuildBankAngleArc(p);
        BuildSpeedTape(p);
        BuildAltTape(p);
        BuildHeadingStrip(p);
        BuildStatusStrip(p);

        _hudGlassBuilt = true;
        _hudGlassCanvas.gameObject.SetActive(false);
    }

    // HUD 유리는 기체 자식 — 조종사 눈 앞에 고정
    void AttachHUDGlass(Transform playerTransform)
    {
        Vector3 eye  = flightCamera != null ? flightCamera.CockpitOffset   : new Vector3(0f, 1.75f, 2.45f);
        float   tilt = flightCamera != null ? flightCamera.CockpitDownTilt : 8f;

        Transform p = _hudGlassCanvas.transform;
        p.SetParent(playerTransform, false);
        p.localPosition = eye + Vector3.forward * _glassDistance;
        p.localRotation = Quaternion.Euler(tilt, 0f, 0f);   // 시선(하향 틸트)과 평행 — 원근 사다리꼴 왜곡 제거
        p.localScale    = Vector3.one * _glassScale;
        ((RectTransform)p).sizeDelta = _glassSizeDelta;
    }

    /// <summary>
    /// 로컬 조종 기체 지정 — 재출격 후 다른 기체(갑판의 에스코트 등)에 탑승하면 계기 값과
    /// HUD 유리가 이전 기체에 남아 C키 콕핏 시점에서 HUD 가 보이지 않으므로 새 기체로 옮긴다.
    /// </summary>
    public void BindLocalPlayer(PlayerController pc)
    {
        if (pc == null || pc == localPlayer) return;
        localPlayer = pc;
        if (flightCamera == null) flightCamera = FindAnyObjectByType<FlightCamera>();
        if (_hudGlassBuilt && _hudGlassCanvas != null && _hudGlassCanvas.transform.parent != pc.transform)
            AttachHUDGlass(pc.transform);
    }

    void OnCMSDeploy(CountermeasureType type)
    {
        _deployMsg   = type == CountermeasureType.Flare ? "FLARE" : "CHAFF";
        _deployTimer = 1.6f;
    }

    void FindRefs()
    {
        foreach (var pc in PlayerController.All)
            if (pc != null && pc.isLocalPlayer) { localPlayer = pc; break; }
        flightCamera = FindAnyObjectByType<FlightCamera>();
    }

    // 지상 모드에서 매 프레임 FindFirstObjectByType 호출을 쿨다운 (0.5s)
    float _findRefsCooldown;

    void Update()
    {
        // 기체 교체(에스코트 인계·재출격) 추적 — GameModeManager 가 현재 조종 기체의 기준
        var gmPc = GameModeManager.Instance != null ? GameModeManager.Instance.LocalPC : null;
        if (gmPc != null && gmPc != localPlayer) BindLocalPlayer(gmPc);

        if (localPlayer == null || flightCamera == null)
        {
            _findRefsCooldown -= Time.deltaTime;
            if (_findRefsCooldown <= 0f)
            {
                FindRefs();
                _findRefsCooldown = 0.5f;
            }
            return;
        }
        if (GameModeManager.Instance == null || !GameModeManager.Instance.IsFlying) return;

        bool cockpit = flightCamera.IsCockpit;
        if (hudCanvas != null) hudCanvas.enabled = cockpit;

        UpdateWarningCanvas();

        if (!cockpit) return;
        UpdateHUD();
    }

    // ── 에디터에서 우클릭 → "Build HUD" 로 씬에 미리 빌드 ───────────────────────
    // 기존 자식을 모두 지우고 새 디자인으로 다시 만든다. 캔버스는 꺼진 채 저장 (콕핏 진입 시 런타임에서 켬).
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
    // ── 경고 요소 빌드 (미사일 비네트 / 충돌 거리 / CMS 투하 / ILS) ─────────
    Transform WarningRoot()
    {
        if (warningCanvas != null)
        {
            warningCanvas.transform.SetParent(hudRoot, false);
            return warningCanvas.transform;
        }
        var t = hudCanvas.transform.Find("Warning");
        if (t != null) return t;
        var g = MakeStretch("Warning", hudCanvas.transform);
        return g;
    }

    void BuildWarningCanvas()
    {
        if (hudCanvas == null) return;
        var root = WarningRoot();

        if (missileAlertOverlay == null)
        {
            var overlay = MakeStretch("MissileAlert", root);
            overlay.SetAsFirstSibling();
            missileAlertOverlay = Spr(overlay, HudSprites.Vignette, Color.clear);
        }
        if (missileDistText == null)
        {
            missileDistText = AddTxt(root, "", new Vector2(0f, _threatBannerY - 36f), new Vector2(400f, 26f),
                                     15, WARN, TextAnchor.MiddleCenter, FontStyle.Bold, true);
        }
        if (cmsDeployText == null)
        {
            cmsDeployText = AddTxt(root, "",
                new Vector2(_wpnCardPos.x, _wpnCardPos.y + _cardSize.y * 0.5f + 24f), new Vector2(200f, 30f),
                17, WARN, TextAnchor.MiddleCenter, FontStyle.Bold, true);
        }

        BuildILSIndicator(root);
    }

    // ── 경고 업데이트 ──────────────────────────────────────────────────────
    void UpdateWarningCanvas()
    {
        if (threatWarn == null) return;

        float t = Time.time;
        bool  missileInbound = threatWarn.MissileIncoming;

        if (missileAlertOverlay != null)
        {
            if (missileInbound)
            {
                float pulse = (Mathf.Sin(t * 9f) + 1f) * 0.5f;
                missileAlertOverlay.color = new Color(CRIT.r, CRIT.g, CRIT.b, 0.10f + pulse * 0.25f);
            }
            else missileAlertOverlay.color = Color.clear;
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

        if (_warnRwrLabel != null)
        {
            _warnRwrLabel.text = threatWarn.Threat switch
            {
                ThreatWarningSystem.ThreatLevel.MissileActive => "ARH",
                ThreatWarningSystem.ThreatLevel.MissileFired  => "MSL",
                ThreatWarningSystem.ThreatLevel.Locked        => "LCK",
                ThreatWarningSystem.ThreatLevel.Tracked       => "TRK",
                ThreatWarningSystem.ThreatLevel.Detected      => "DET",
                _                                              => "CLEAR"
            };
            _warnRwrLabel.color = missileInbound ? CRIT
                                : threatWarn.Threat > ThreatWarningSystem.ThreatLevel.None ? WARN : HGD;
        }

        UpdateILS();
    }

    // ── ILS 인디케이터 빌드 ──────────────────────────────────────────────────
    // 로컬라이저(좌우)·글라이드슬로프(상하) 십자 편차 표시.
    // ILS 비컨 수신 범위 내 진입 시 자동 표시, 벗어나면 자동 숨김.
    void BuildILSIndicator(Transform root)
    {
        if (_ilsPanel != null) return;
        float pw = 156f, ph = 172f;
        _ilsPanel = GlassCard("ILS_Panel", root, _ilsPos, new Vector2(pw, ph));

        _ilsLabel    = AddTxt(_ilsPanel, "ILS", new Vector2(-pw * 0.5f + 14f, ph * 0.5f - 18f), new Vector2(60f, 20f),
                              13, HG, TextAnchor.MiddleLeft, FontStyle.Bold);
        _ilsDistText = AddTxt(_ilsPanel, "", new Vector2(pw * 0.5f - 14f, ph * 0.5f - 18f), new Vector2(80f, 20f),
                              13, INK, TextAnchor.MiddleRight);
        Spr(Rect("Div", _ilsPanel, new Vector2(0f, ph * 0.5f - 34f), new Vector2(pw - 24f, 2f)), HudSprites.GlowLine, EDGE);

        var scope = Rect("Scope", _ilsPanel, new Vector2(0f, -14f), new Vector2(120f, 120f));
        Spr(Rect("Ring", scope, Vector2.zero, new Vector2(112f, 112f)), HudSprites.RingThin, HGX);

        // 스케일 도트 (±1/2, ±1 풀스케일)
        for (int s = -2; s <= 2; s++)
        {
            if (s == 0) continue;
            float o = ILS_DEFLECT_PX * 0.5f * s;
            Color dc = Mathf.Abs(s) == 2 ? HGX : HGD;
            Spr(Rect($"DH{s}", scope, new Vector2(o, 0f), new Vector2(6f, 6f)), HudSprites.Disc, dc);
            Spr(Rect($"DV{s}", scope, new Vector2(0f, o), new Vector2(6f, 6f)), HudSprites.Disc, dc);
        }
        Spr(Rect("Center", scope, Vector2.zero, new Vector2(14f, 14f)), HudSprites.Diamond, HGD);

        // 로컬라이저 바 (수직 막대, 좌우로 이동) / 글라이드슬로프 바 (수평 막대, 상하로 이동)
        _ilsLocBar = Rect("ILS_Loc", scope, Vector2.zero, new Vector2(3f, 96f));
        _ilsLocImg = Img(_ilsLocBar, HG);
        _ilsGsBar  = Rect("ILS_GS", scope, Vector2.zero, new Vector2(96f, 3f));
        _ilsGsImg  = Img(_ilsGsBar, HG);

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
        var       beacons = FindObjectsByType<ILSBeacon>(FindObjectsInactive.Exclude);
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

        // 콕핏 패널만 hudCanvas에 — 비행 계기류는 BuildHUDGlass에서 WorldSpace에 빌드
        BuildSystemsCard();
        BuildStoresCard();
        BuildRwr();

        // 시스템 컴포넌트·적 마커는 런타임 전용 — 에디터 "Build HUD" 베이크 때 씬에 남기지 않는다
        if (!Application.isPlaying) { BuildTargetBox(); BuildOffScreenIndicator(); BuildThreatWarningUI(); return; }

        targeting  = GetComponent<TargetingSystem>()      ?? gameObject.AddComponent<TargetingSystem>();
        launcher   = GetComponent<MissileLauncher>()      ?? gameObject.AddComponent<MissileLauncher>();
        threatWarn = GetComponent<ThreatWarningSystem>()  ?? gameObject.AddComponent<ThreatWarningSystem>();
        cmsys      = GetComponent<CountermeasureSystem>() ?? gameObject.AddComponent<CountermeasureSystem>();
        if (GetComponent<HitEffectSystem>() == null) gameObject.AddComponent<HitEffectSystem>();
        if (GetComponent<GunSystem>()       == null) gameObject.AddComponent<GunSystem>();

        BuildTargetingOverlay();
    }

    // ── 시스템 카드 (좌하단): 추력·연료 세그먼트 게이지 + 모드 + 미션 시간 ────
    void BuildSystemsCard()
    {
        if (n1Text != null) return;
        var c = GlassCard("SysCard", hudCanvas.transform, _sysCardPos, _cardSize);
        float hw = _cardSize.x * 0.5f - 16f, top = _cardSize.y * 0.5f;

        CardTitle(c, "SYSTEMS", hw, top - 20f);
        modeText = Pill(c, "NAV", new Vector2(hw - 40f, top - 20f), new Vector2(60f, 22f), HG);
        Divider(c, top - 38f, hw * 2f);

        n1Text   = Row(c, "THR",  "30%",    hw, top - 56f);
        n1Bar    = SegBar(c, "N1Bar",   top - 75f, hw * 2f);
        fuelText = Row(c, "FUEL", "100.0%", hw, top - 98f);
        fuelBar  = SegBar(c, "FuelBar", top - 117f, hw * 2f);
        fuelBar.fillAmount = 1f;

        Divider(c, top - 136f, hw * 2f);
        AddTxt(c, "MSN", new Vector2(-hw, top - 156f), new Vector2(40f, 20f), 11, HGD, TextAnchor.MiddleLeft);
        clockText = AddTxt(c, "00:00:00", new Vector2(-hw + 40f, top - 156f), new Vector2(110f, 20f),
                           14, INK, TextAnchor.MiddleLeft);
        AddTxt(c, "A/P OFF", new Vector2(hw, top - 156f), new Vector2(90f, 20f), 11, HGX, TextAnchor.MiddleRight);
    }

    // ── 무장 카드 (우하단): 마스터 상태 + 미사일 + 카운터메저 ─────────────────
    void BuildStoresCard()
    {
        if (weaponText != null) return;
        var c = GlassCard("WpnCard", hudCanvas.transform, _wpnCardPos, _cardSize);
        float hw = _cardSize.x * 0.5f - 16f, top = _cardSize.y * 0.5f;

        CardTitle(c, "STORES", hw, top - 20f);
        weaponText = Pill(c, "SAFE", new Vector2(hw - 46f, top - 20f), new Vector2(72f, 22f), HGD);
        Divider(c, top - 38f, hw * 2f);

        aim120CountText = MissileRow(c, "AIM-120", "4", hw, top - 57f);
        MissileRow(c, "AIM-9X", "2", hw, top - 81f);

        Divider(c, top - 100f, hw * 2f);
        flareCountText = Row(c, "FLARE", "30", hw, top - 117f);
        chaffCountText = Row(c, "CHAFF", "30", hw, top - 137f);

        Divider(c, top - 152f, hw * 2f);
        gearText = AddTxt(c, "GEAR  UP", new Vector2(-hw, top - 166f), new Vector2(110f, 18f), 11, HGD, TextAnchor.MiddleLeft);
        AddTxt(c, "ECM  STBY", new Vector2(hw, top - 166f), new Vector2(110f, 18f), 11, HGX, TextAnchor.MiddleRight);
    }

    // ── RWR 스코프 (좌측): 위협 방위 블립 + 단계 라벨 ─────────────────────────
    void BuildRwr()
    {
        if (rwrNeedle != null) return;
        float s = _rwrSize, r = s * 0.5f;
        var root = Rect("RWR", hudCanvas.transform, _rwrPos, new Vector2(s, s));
        Spr(root, HudSprites.Disc, GLASS);
        Spr(Stretch("Scope", root), HudSprites.Rwr, new Color(HG.r, HG.g, HG.b, 0.55f));
        var own = Rect("Own", root, Vector2.zero, new Vector2(16f, 12f));
        Spr(own, HudSprites.Chevron, HGD);

        AddTxt(root, "RWR", new Vector2(0f, r + 14f), new Vector2(80f, 18f), 13, HG, TextAnchor.MiddleCenter, FontStyle.Bold, true);
        _warnRwrLabel = AddTxt(root, "CLEAR", new Vector2(0f, -r - 14f), new Vector2(100f, 18f), 13, HGD,
                               TextAnchor.MiddleCenter, FontStyle.Bold, true);

        rwrNeedle = Rect("RWR_Threat", root, Vector2.zero, Vector2.zero);
        float br = r * 0.66f;
        var line = Rect("Line", rwrNeedle, new Vector2(0f, 8f), new Vector2(2f, br - 18f));
        line.pivot = new Vector2(0.5f, 0f);
        Img(line, new Color(CRIT.r, CRIT.g, CRIT.b, 0.45f));
        Spr(Rect("Blip", rwrNeedle, new Vector2(0f, br), new Vector2(20f, 20f)), HudSprites.Diamond, CRIT);
        rwrNeedle.gameObject.SetActive(false);
    }

    // ── HUD 유리 코너 브래킷 ────────────────────────────────────────────────
    void BuildHUDGlassCorners(Transform parent)
    {
        float hw = _glassCornerHW, hh = _glassCornerHH;
        Vector2[] pos = { new Vector2(-hw, hh), new Vector2(hw, hh), new Vector2(hw, -hh), new Vector2(-hw, -hh) };
        for (int i = 0; i < 4; i++)
        {
            var c = Rect($"GC{i}", parent, pos[i], new Vector2(34f, 34f));
            c.pivot = new Vector2(0f, 1f);
            c.localRotation = Quaternion.Euler(0f, 0f, CornerRot[i]);
            Spr(c, HudSprites.Corner, HGX);
        }
    }

    // ── 인공수평선 / 피치 래더 (+ 실선, − 점선, 끝 틱은 수평선 쪽) ────────────
    void BuildArtificialHorizon(Transform parent)
    {
        var mask = Rect("AH_Mask", parent, Vector2.zero, _ahSize);
        mask.gameObject.AddComponent<RectMask2D>();

        ahRoot  = Rect("AH_Root",  mask,   Vector2.zero, _ahSize);
        ahSlide = Rect("AH_Slide", ahRoot, Vector2.zero, new Vector2(_ahSize.x, 3200f));

        const float GAP = 110f;
        float half = _ahSize.x * 0.5f + 300f;           // 롤 시에도 끝이 보이지 않게 마스크보다 길게
        float seg  = half - GAP * 0.6f;
        Img(Rect("HL_L", ahSlide, new Vector2(-(GAP * 0.6f + seg * 0.5f), 0f), new Vector2(seg, 2.5f)), new Color(HG.r, HG.g, HG.b, 0.70f));
        Img(Rect("HL_R", ahSlide, new Vector2( (GAP * 0.6f + seg * 0.5f), 0f), new Vector2(seg, 2.5f)), new Color(HG.r, HG.g, HG.b, 0.70f));

        var ladder = Rect("Ladder", ahSlide, Vector2.zero, new Vector2(_ahSize.x, 3200f));
        foreach (int deg in PitchMarks)
        {
            float y    = deg * PX_PER_DEG;
            bool major = Mathf.Abs(deg) % 10 == 0;
            bool up    = deg > 0;
            float arm  = major ? 120f : 64f;
            Color col  = major ? new Color(HG.r, HG.g, HG.b, 0.80f) : new Color(HG.r, HG.g, HG.b, 0.45f);
            for (int side = -1; side <= 1; side += 2)
            {
                float cx = side * (GAP + arm * 0.5f);
                var bar = Rect($"P{deg}_{side}", ladder, new Vector2(cx, y), new Vector2(arm, 2.5f));
                if (up) Img(bar, col);
                else    { var im = Spr(bar, HudSprites.Dash, col); im.type = Image.Type.Tiled; }
                // 끝 틱: 양의 피치는 아래로, 음의 피치는 위로 (수평선 방향)
                Img(Rect($"PT{deg}_{side}", ladder, new Vector2(side * GAP, y + (up ? -6f : 6f)), new Vector2(2.5f, 12f)), col);
                if (major)
                    AddTxt(ladder, $"{Mathf.Abs(deg)}", new Vector2(side * (GAP + arm + 10f), y), new Vector2(40f, 20f),
                           17, HGL, side < 0 ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, FontStyle.Normal, true);
            }
        }
    }

    // ── 보어사이트 (워터라인 "-^-", 기수 축 고정) ────────────────────────────
    void BuildBoresight(Transform parent)
    {
        var wl = Rect("Waterline", parent, Vector2.zero, new Vector2(96f, 30f));
        wl.pivot = new Vector2(0.5f, 0.45f);
        Spr(wl, HudSprites.Waterline, new Color(HG.r, HG.g, HG.b, 0.85f));
    }

    // ── FPV ──────────────────────────────────────────────────────────────────
    void BuildFPV(Transform parent)
    {
        fpvRoot = Rect("FPV", parent, Vector2.zero, new Vector2(80f, 40f));
        fpvRoot.pivot = new Vector2(0.5f, 0.406f);   // 스프라이트 원 중심
        Spr(fpvRoot, HudSprites.Fpv, FPVC);
    }

    // ── 뱅크각 호 + 포인터 ──────────────────────────────────────────────────
    void BuildBankAngleArc(Transform parent)
    {
        int[] marks={-60,-45,-30,-20,-10,0,10,20,30,45,60};
        float r=230f;
        foreach(int deg in marks)
        {
            float rad=(90-deg)*Mathf.Deg2Rad;
            float x=r*Mathf.Cos(rad),y=r*Mathf.Sin(rad);
            float h=deg%30==0?18f:10f;
            var tick=Rect($"BA{deg}",parent,new Vector2(x,y),new Vector2(2.5f,h));
            tick.localRotation=Quaternion.Euler(0f,0f,-deg);
            Img(tick,deg==0?HG:HGD);   // 숫자 없이 눈금만 (피치 래더 숫자와 혼동 방지)
        }
        bankPivot=Rect("BankPivot",parent,Vector2.zero,Vector2.zero);
        var ptr=Rect("BPtr",bankPivot,new Vector2(0f,r-18f),new Vector2(14f,18f));
        ptr.localRotation=Quaternion.Euler(0f,0f,90f);
        Spr(ptr,HudSprites.Caret,HG);
    }

    // ── 속도 테이프 (좌) ─────────────────────────────────────────────────────
    void BuildSpeedTape(Transform parent)
    {
        float px = _spdX, h = SPD_LINES * SPD_PX;
        var panel = Rect("SpdPanel", parent, new Vector2(px, 0f), new Vector2(130f, h));
        Img(Rect("Spine", panel, new Vector2(56f, 0f), new Vector2(2f, h)), HGX);

        var msk = Rect("SM", panel, Vector2.zero, new Vector2(130f, h));
        msk.gameObject.AddComponent<RectMask2D>();
        var scr = Rect("SS", msk, Vector2.zero, new Vector2(130f, h));
        for (int i = 0; i < SPD_LINES; i++)
        {
            float y = (SPD_LINES / 2 - i) * SPD_PX;
            spdLabels[i]   = AddTxt(scr, "0", new Vector2(36f, y), new Vector2(90f, SPD_PX - 4f), 19, HGL, TextAnchor.MiddleRight, FontStyle.Normal, true);
            spdLabelRTs[i] = spdLabels[i].rectTransform;
            Img(Rect($"ST{i}", spdLabels[i].transform, new Vector2(59f, 0f), new Vector2(12f, 2f)), HGD);   // 스파인 쪽 눈금 (라벨과 함께 스크롤)
        }
        spdCurrent = Readout(panel, new Vector2(-2f, 0f), new Vector2(122f, 40f), false, TextAnchor.MiddleRight);
        AddTxt(panel, "KPH", new Vector2(0f, h * 0.5f + 22f), new Vector2(110f, 22f), 17, HGL, TextAnchor.MiddleCenter, FontStyle.Bold, true);
        machText = AddTxt(parent, "M 0.000", new Vector2(px, -(h * 0.5f + 26f)), new Vector2(130f, 24f), 18, HGL, TextAnchor.MiddleCenter, FontStyle.Normal, true);
        BuildThrottleBar(parent, px - 82f, h);
    }

    void BuildThrottleBar(Transform parent, float px, float h)
    {
        var p = Rect("TP", parent, new Vector2(px, 0f), new Vector2(4f, h));
        Img(p, new Color(HG.r, HG.g, HG.b, 0.14f));
        throttleBar = Rect("TF", p, new Vector2(0f, -h * 0.5f), new Vector2(4f, 0f));
        throttleBar.pivot = new Vector2(0.5f, 0f);
        Img(throttleBar, HG);
        AddTxt(parent, "THR", new Vector2(px, -(h * 0.5f + 26f)), new Vector2(40f, 20f), 13, HGD, TextAnchor.MiddleCenter, FontStyle.Normal, true);
    }

    // ── 고도 테이프 (우) ─────────────────────────────────────────────────────
    void BuildAltTape(Transform parent)
    {
        float px = _altX, h = ALT_LINES * ALT_PX;
        var panel = Rect("AltPanel", parent, new Vector2(px, 0f), new Vector2(130f, h));
        Img(Rect("Spine", panel, new Vector2(-56f, 0f), new Vector2(2f, h)), HGX);

        var msk = Rect("AM", panel, Vector2.zero, new Vector2(130f, h));
        msk.gameObject.AddComponent<RectMask2D>();
        var scr = Rect("AS", msk, Vector2.zero, new Vector2(130f, h));
        for (int i = 0; i < ALT_LINES; i++)
        {
            float y = (ALT_LINES / 2 - i) * ALT_PX;
            altLabels[i]   = AddTxt(scr, "0", new Vector2(-36f, y), new Vector2(90f, ALT_PX - 4f), 19, HGL, TextAnchor.MiddleLeft, FontStyle.Normal, true);
            altLabelRTs[i] = altLabels[i].rectTransform;
            Img(Rect($"AT{i}", altLabels[i].transform, new Vector2(-59f, 0f), new Vector2(12f, 2f)), HGD);
        }
        altCurrent = Readout(panel, new Vector2(2f, 0f), new Vector2(122f, 40f), true, TextAnchor.MiddleLeft);
        AddTxt(panel, "ALT  M", new Vector2(0f, h * 0.5f + 22f), new Vector2(110f, 22f), 17, HGL, TextAnchor.MiddleCenter, FontStyle.Bold, true);
        vsiText  = AddTxt(parent, "VVI  +0",  new Vector2(px, -(h * 0.5f + 26f)), new Vector2(150f, 24f), 18, HGL, TextAnchor.MiddleCenter, FontStyle.Normal, true);
        raltText = AddTxt(parent, "RALT ---", new Vector2(px, -(h * 0.5f + 52f)), new Vector2(150f, 22f), 17, HGL, TextAnchor.MiddleCenter, FontStyle.Normal, true);
    }

    // ── 헤딩 스트립 (상단) ───────────────────────────────────────────────────
    void BuildHeadingStrip(Transform parent)
    {
        float w = HDG_LINES * HDG_PX;
        var panel = Rect("HdgPanel", parent, new Vector2(0f, _hdgY), new Vector2(w, 50f));
        var msk = Rect("HM", panel, Vector2.zero, new Vector2(w, 50f));
        msk.gameObject.AddComponent<RectMask2D>();
        var scr = Rect("HS", msk, Vector2.zero, new Vector2(w, 50f));
        for (int i = 0; i < HDG_LINES; i++)
        {
            float x = (i - HDG_LINES / 2) * HDG_PX;
            hdgLabels[i]   = AddTxt(scr, "N", new Vector2(x, 8f), new Vector2(HDG_PX - 2f, 20f), 18, HGL, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            hdgLabelRTs[i] = hdgLabels[i].rectTransform;
            Img(Rect($"HT{i}", hdgLabels[i].transform, new Vector2(0f, -20f), new Vector2(2f, 9f)), HGD);
        }
        Spr(Rect("Base", panel, new Vector2(0f, -17f), new Vector2(w, 3f)), HudSprites.GlowLine, HGX);
        var ptr = Rect("HPtr", panel, new Vector2(0f, -33f), new Vector2(12f, 16f));
        ptr.localRotation = Quaternion.Euler(0f, 0f, 90f);
        Spr(ptr, HudSprites.Caret, HG);

        var box = Rect("HCB", panel, new Vector2(0f, 46f), new Vector2(84f, 30f));
        Spr(box, HudSprites.Pill, new Color(0.01f, 0.05f, 0.03f, 0.80f), Image.Type.Sliced);
        Spr(Stretch("Line", box), HudSprites.PillLine, HG, Image.Type.Sliced);
        hdgCurrent = AddTxt(box, "000°", Vector2.zero, new Vector2(84f, 30f), 19, INK, TextAnchor.MiddleCenter, FontStyle.Bold);
    }

    // ── 상태 스트립 (하단: AOA / G) + 비행 경고 배너 ─────────────────────────
    void BuildStatusStrip(Transform parent)
    {
        Spr(Rect("StatusLine", parent, new Vector2(0f, _statusY + 22f), new Vector2(420f, 2f)), HudSprites.GlowLine, HGX);
        aoaText = AddTxt(parent, "AOA +0.0°", new Vector2(-150f, _statusY), new Vector2(170f, 30f), 19, HGL, TextAnchor.MiddleCenter, FontStyle.Normal, true);
        gText   = AddTxt(parent, "G  1.0",    new Vector2(   0f, _statusY), new Vector2(120f, 30f), 25, HG,  TextAnchor.MiddleCenter, FontStyle.Bold,   true);

        warnText = AddTxt(parent, "", new Vector2(0f, _warnY), new Vector2(520f, 34f), 23, WARN, TextAnchor.MiddleCenter, FontStyle.Bold, true);
    }

    // ═══════════════════════════════════════════════════════════════════════
    void UpdateHUD()
    {
        Transform t=localPlayer.transform;
        float dt=Time.deltaTime;
        // 콕핏 HUD 는 콕핏 시점에서만 갱신되므로, 갱신이 끊겼다 재개되면 이전 위치가 낡아 속도/G 가 튄다 → 재동기화
        bool resync=!prevPosSet||Time.frameCount-_lastHudFrame>1;
        _lastHudFrame=Time.frameCount;
        if(resync){prevPos=t.position;prevPosSet=true;_gSettle=2;}

        Vector3 euler=t.eulerAngles;
        float pitch=euler.x>180f?euler.x-360f:euler.x;
        float roll =euler.z>180f?euler.z-360f:euler.z;
        float yaw  =euler.y;

        Vector3 vel=(t.position-prevPos)/dt;
        float kph=vel.magnitude*3.6f, mach=vel.magnitude/343f;

        Vector3 accel=dt>0f?(vel-prevVel)/dt:Vector3.zero;
        float g=(accel-Physics.gravity).magnitude/9.81f;
        if(_gSettle>0) _gSettle--;
        else smoothG=Mathf.Lerp(smoothG,g,dt*4f);

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

        UpdateTape(spdLabels,spdLabelRTs,kph,SPD_STEP,SPD_PX,SPD_LINES);
        if(spdCurrent!=null) spdCurrent.text=$"{kph:F0}";
        if(machText  !=null) machText.text  =$"M  {mach:F3}";
        float thr=Mathf.Clamp01(localPlayer.CurrentSpeed/80f);
        if(throttleBar!=null) throttleBar.sizeDelta=new Vector2(4f,thr*SPD_LINES*SPD_PX);

        UpdateTape(altLabels,altLabelRTs,t.position.y,ALT_STEP,ALT_PX,ALT_LINES);
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

        // 비행 상태 플래그 — PlayerController가 실제 fuel/stall/damage 상태 소유
        bool stallWarn    = localPlayer.StallWarning;
        bool stallActive  = localPlayer.IsStalling;
        bool abActive     = localPlayer.AfterburnerActive;
        bool fuelLow      = localPlayer.IsFuelLow;
        bool fuelEmpty    = localPlayer.IsFuelEmpty;
        bool engineDmg    = localPlayer.IsEngineDamaged;
        bool critDmg      = localPlayer.IsCriticalDamage;
        bool gWarn        = smoothG > 7f;

        // 통합 경고 메시지 — 우선순위: 자동기동 > destroyed > critical > ...
        if (warnText != null)
        {
            string msg = ""; Color col = WARN;
            if      (localPlayer.IsBarrelRolling)        { msg = "BARREL ROLL + FLARE (Q/E)"; col = ExhGlow; }
            else if (localPlayer.IsImmelmannRollover)    { msg = "IMMELMANN FLIP";      col = ExhGlow; }
            else if (localPlayer.IsCobraRising)          { msg = "ZOOM UP   [B TO FLIP]"; col = ExhGlow; }
            else if (localPlayer.IsDestroyed) { msg = "DESTROYED";       col = CRIT; }
            else if (critDmg)                 { msg = "CRITICAL DAMAGE"; col = CRIT; }
            else if (gWarn)                   { msg = "G-LOCK WARNING";  col = CRIT; }
            else if (stallActive)             { msg = "STALL";           col = CRIT; }
            else if (fuelEmpty)               { msg = "FUEL EMPTY";      col = CRIT; }
            else if (stallWarn)               { msg = "STALL WARN";      col = WARN; }
            else if (fuelLow)                 { msg = "BINGO FUEL";      col = WARN; }
            else if (engineDmg)               { msg = "ENGINE DAMAGE";   col = WARN; }
            else if (abActive)                { msg = "AFTERBURNER";     col = ExhGlow; }
            warnText.text  = msg;
            warnText.color = col;
        }

        missionTime += dt;
        // PlayerController가 실제 연료 소유 — HUD는 비율만 표시
        fuelLevel = localPlayer.MaxFuel > 0.01f
            ? (localPlayer.CurrentFuel / localPlayer.MaxFuel) * 100f
            : 0f;

        // N1 (엔진 RPM): AB 활성 시 100%+, 손상 시 90% 캡
        float n1 = 28f + thr * 72f;
        if (abActive) n1 = Mathf.Max(n1, 102f);
        if (engineDmg) n1 = Mathf.Min(n1, 85f);

        Color n1Col   = abActive ? ExhGlow : engineDmg ? WARN : (n1>95f?WARN:HG);
        Color fuelCol = fuelEmpty ? CRIT : fuelLow ? WARN : HG;
        if(n1Text  !=null){ n1Text.text  =$"{n1:F0}%";        n1Text.color   = n1Col; }
        if(n1Bar   !=null){ n1Bar.fillAmount = Mathf.Min(n1,100f)/100f; n1Bar.color = n1Col; }
        if(fuelText!=null){ fuelText.text=$"{fuelLevel:F1}%"; fuelText.color = fuelCol; }
        if(fuelBar !=null){ fuelBar.fillAmount = fuelLevel/100f;        fuelBar.color = fuelCol; }
        int ts=(int)missionTime;
        if(clockText!=null) clockText.text=$"{ts/3600:D2}:{ts/60%60:D2}:{ts%60:D2}";

        UpdateTargetingHUD();
    }

    void UpdateTape(Text[] labels, RectTransform[] rts, float val,
                    float step, float px, int count)
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
            hdgLabels[i].color    =card?HG:HGL;
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
        BuildEnemyMarkers();
    }

    void BuildEnemyMarkers()
    {
        for (int i = 0; i < MAX_ENEMY_MARKERS; i++)
        {
            var root = Rect($"EM_{i}", hudCanvas.transform, Vector2.zero, new Vector2(60f, 60f));
            root.gameObject.SetActive(false);
            _emNicks[i] = AddTxt(root, "", new Vector2(0f, 30f), new Vector2(200f, 18f), 12, HGD, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            _emIcons[i] = Spr(Rect("Icon", root, Vector2.zero, new Vector2(30f, 30f)), HudSprites.MarkerBox, HGD);
            _emRoots[i] = root;
        }
    }

    void BuildThreatWarningUI()
    {
        if (threatWarningText != null) return;
        var wp = Rect("ThreatWarn", hudCanvas.transform, new Vector2(0f, _threatBannerY), new Vector2(420f, 40f));
        _threatPlate     = Spr(wp, HudSprites.Pill, Color.clear, Image.Type.Sliced);
        _threatPlateLine = Spr(Stretch("Line", wp), HudSprites.PillLine, Color.clear, Image.Type.Sliced);
        threatWarningText = AddTxt(wp, "", Vector2.zero, new Vector2(420f, 40f), 19, CRIT, TextAnchor.MiddleCenter, FontStyle.Bold);
        _threatPlate.enabled = _threatPlateLine.enabled = false;
    }

    void BuildTargetBox()
    {
        if (tdbRoot != null)
        {
            // tdbRoot 가 씬에 베이크돼 있으면 런타임 참조만 복구
            for (int i = 0; i < 4; i++)
                if (tdbCorners[i] == null)
                    tdbCorners[i] = tdbRoot.Find($"TDB_C{i}")?.GetComponent<Image>();
            return;
        }
        tdbRoot = Rect("TDB_Root", hudCanvas.transform, Vector2.zero, Vector2.zero);
        tdbRoot.gameObject.SetActive(false);

        for (int i = 0; i < 4; i++)
        {
            var c = Rect($"TDB_C{i}", tdbRoot, Vector2.zero, new Vector2(24f, 24f));
            c.pivot = new Vector2(0f, 1f);
            c.localRotation = Quaternion.Euler(0f, 0f, CornerRot[i]);
            tdbCorners[i] = Spr(c, HudSprites.Corner, HG);
        }

        tdbName    = AddTxt(tdbRoot, "", new Vector2(0f,  80f), new Vector2(220f, 18f), 13, HGD, TextAnchor.MiddleCenter, FontStyle.Bold,   true);
        tdbLabel   = AddTxt(tdbRoot, "", new Vector2(0f, -82f), new Vector2(180f, 20f), 15, HG,  TextAnchor.MiddleCenter, FontStyle.Bold,   true);
        tdbRange   = AddTxt(tdbRoot, "", new Vector2(0f,-101f), new Vector2(180f, 18f), 14, INK, TextAnchor.MiddleCenter, FontStyle.Normal, true);
        tdbClosure = AddTxt(tdbRoot, "", new Vector2(0f,-118f), new Vector2(180f, 16f), 12, HGD, TextAnchor.MiddleCenter, FontStyle.Normal, true);
        tdbAspect  = AddTxt(tdbRoot, "", new Vector2(0f,-134f), new Vector2(180f, 16f), 12, HGD, TextAnchor.MiddleCenter, FontStyle.Normal, true);
    }

    void BuildOffScreenIndicator()
    {
        if (offScreenRoot != null) return;
        offScreenRoot = Rect("OSI_Root", hudCanvas.transform, Vector2.zero, new Vector2(32f, 32f));
        offScreenRoot.gameObject.SetActive(false);
        Spr(Rect("Arrow", offScreenRoot, Vector2.zero, new Vector2(30f, 22f)), HudSprites.Chevron, HG);
        offScreenDist = AddTxt(offScreenRoot, "", new Vector2(0f, -26f), new Vector2(90f, 18f), 12, HGD, TextAnchor.MiddleCenter, FontStyle.Normal, true);
    }

    // 코너 브래킷 4개를 ±halfSize 사각형 모서리에, 텍스트는 박스 위/아래로
    void LayoutTDB(float h)
    {
        Vector2[] pos = { new Vector2(-h, h), new Vector2(h, h), new Vector2(h, -h), new Vector2(-h, -h) };
        for (int i = 0; i < 4; i++)
            if (tdbCorners[i] != null) tdbCorners[i].rectTransform.anchoredPosition = pos[i];
        if (tdbName    != null) tdbName.rectTransform.anchoredPosition    = new Vector2(0f,  h + 16f);
        if (tdbLabel   != null) tdbLabel.rectTransform.anchoredPosition   = new Vector2(0f, -h - 18f);
        if (tdbRange   != null) tdbRange.rectTransform.anchoredPosition   = new Vector2(0f, -h - 37f);
        if (tdbClosure != null) tdbClosure.rectTransform.anchoredPosition = new Vector2(0f, -h - 54f);
        if (tdbAspect  != null) tdbAspect.rectTransform.anchoredPosition  = new Vector2(0f, -h - 70f);
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
            LayoutTDB(halfSize);

            // 락온 타겟 종류에 따라 브래킷 색 구별
            bool  targetIsEnemy = AIManager.IsEnemyBot(targeting.Target.nickname);
            Color lockedCol     = targetIsEnemy ? MarkerEnemyColor : MarkerHumanColor;

            Color armCol;
            if (inFireZone)
                armCol = blink ? WARN : new Color(WARN.r, WARN.g, WARN.b, 0.5f);
            else if (locked)
                armCol = lockedCol;
            else
                armCol = blink ? new Color(lockedCol.r, lockedCol.g, lockedCol.b, 0.6f) : HGX;

            foreach (var c in tdbCorners)
                if (c != null) c.color = armCol;

            if (inFireZone)
            {
                tdbLabel.text  = "SHOOT";
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
            tdbName.color   = new Color(lockedCol.r, lockedCol.g, lockedCol.b, 0.85f);

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

        UpdateEnemyMarkers();

        if (aim120CountText != null && launcher != null)
        {
            aim120CountText.text  = $"{launcher.MissileCount}";
            aim120CountText.color = launcher.MissileCount == 0 ? CRIT : INK;
        }

        if (cmsys != null)
        {
            if (flareCountText != null)
            {
                flareCountText.text  = $"{cmsys.FlareRemaining:D2}";
                flareCountText.color = cmsys.FlareRemaining <= 5 ? WARN : INK;
            }
            if (chaffCountText != null)
            {
                chaffCountText.text  = $"{cmsys.ChaffRemaining:D2}";
                chaffCountText.color = cmsys.ChaffRemaining <= 5 ? WARN : INK;
            }
        }

        UpdateThreatWarningUI();
    }

    // AI 적기: 빨간 코너 박스  /  인간 플레이어 비행: 파란 △
    static readonly Color MarkerEnemyColor = new Color(1.00f, 0.32f, 0.30f, 0.95f);
    static readonly Color MarkerHumanColor = new Color(0.28f, 0.84f, 0.95f, 0.92f);   // #46D7F2

    void UpdateEnemyMarkers()
    {
        for (int i = 0; i < MAX_ENEMY_MARKERS; i++)
            if (_emRoots[i] != null) _emRoots[i].gameObject.SetActive(false);

        if (targeting == null) return;

        var lockedTarget = targeting.Target;
        int mi = 0;

        foreach (var pc in PlayerController.All)
        {
            if (mi >= MAX_ENEMY_MARKERS) break;
            if (pc == null || pc.isLocalPlayer || !pc.IsFlying) continue;
            // 에스코트 봇(아군)은 마커 제외
            if (AIManager.IsEscortBot(pc.nickname)) continue;
            // 락온 타겟은 TDB 브래킷이 담당 — 소형 마커 중복 제외
            if (pc == lockedTarget) continue;

            Vector2 cp = targeting.WorldToCanvasPos(pc.transform.position, out bool onScreen);
            if (!onScreen) continue;

            bool  isEnemy = AIManager.IsEnemyBot(pc.nickname);
            Color col     = isEnemy ? MarkerEnemyColor : MarkerHumanColor;

            _emRoots[mi].anchoredPosition = cp;
            _emNicks[mi].text  = pc.nickname;
            _emNicks[mi].color = new Color(col.r, col.g, col.b, 0.75f);
            _emIcons[mi].sprite = HudSprites.Get(isEnemy ? HudSprites.MarkerBox : HudSprites.MarkerTri);
            _emIcons[mi].color  = col;
            _emRoots[mi].gameObject.SetActive(true);
            mi++;
        }
    }

    void UpdateThreatWarningUI()
    {
        if (threatWarn == null || threatWarningText == null) return;

        bool blink = tdbFlash % 0.35f < 0.175f;

        switch (threatWarn.Threat)
        {
            case ThreatWarningSystem.ThreatLevel.MissileActive:
                threatWarningText.text  = "SEEKER ACTIVE";
                threatWarningText.color = blink ? CRIT : INK;
                break;
            case ThreatWarningSystem.ThreatLevel.MissileFired:
                threatWarningText.text  = "MISSILE INBOUND";
                threatWarningText.color = blink ? CRIT : INK;
                break;
            case ThreatWarningSystem.ThreatLevel.Locked:
                threatWarningText.text  = blink ? "LOCK WARNING" : "";
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

        // 배너 판: 위협 단계 색으로 (메시지가 비면 같이 숨김)
        if (_threatPlate != null)
        {
            bool on = threatWarningText.text.Length > 0;
            Color lv = threatWarn.Threat >= ThreatWarningSystem.ThreatLevel.Locked ? CRIT
                     : threatWarn.Threat == ThreatWarningSystem.ThreatLevel.Tracked ? WARN : HG;
            _threatPlate.enabled = on;
            _threatPlate.color   = new Color(lv.r * 0.25f, lv.g * 0.25f, lv.b * 0.25f, 0.70f);
            if (_threatPlateLine != null)
            {
                _threatPlateLine.enabled = on;
                _threatPlateLine.color   = new Color(lv.r, lv.g, lv.b, 0.85f);
            }
        }

        if (rwrNeedle != null)
        {
            bool showRWR = threatWarn.Threat > ThreatWarningSystem.ThreatLevel.None;
            rwrNeedle.gameObject.SetActive(showRWR);
            if (showRWR)
            {
                rwrNeedle.localRotation = Quaternion.Euler(0f, 0f, -threatWarn.ThreatBearing);
                Color c = threatWarn.MissileIncoming ? CRIT : WARN;
                if (_rwrBlip == null)
                {
                    _rwrBlip = rwrNeedle.Find("Blip")?.GetComponent<Image>();
                    _rwrLine = rwrNeedle.Find("Line")?.GetComponent<Image>();
                }
                if (_rwrBlip != null) _rwrBlip.color = c;
                if (_rwrLine != null) _rwrLine.color = new Color(c.r, c.g, c.b, 0.45f);
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

    // ═══════════════════════════════════════════════════════════════════════
    // ── 빌드 헬퍼 ──────────────────────────────────────────────────────────
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

    RectTransform Stretch(string name, Transform parent) => MakeStretch(name, parent);

    Image Img(RectTransform rt,Color c)
    {
        var img=rt.gameObject.AddComponent<Image>();
        img.color=c; img.raycastTarget=false;
        return img;
    }

    Image Spr(RectTransform rt, string sprite, Color c, Image.Type type = Image.Type.Simple)
    {
        var img = Img(rt, c);
        img.sprite = HudSprites.Get(sprite);
        img.type   = type;
        return img;
    }

    // 다크 글래스 카드: 챔퍼 면 + 헤어라인 테두리 + 대각 코너 액센트 + 상단 광택
    RectTransform GlassCard(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var rt = Rect(name, parent, pos, size);
        Spr(rt, HudSprites.Panel, GLASS, Image.Type.Sliced);
        var sheen = Stretch("Sheen", rt);
        sheen.offsetMin = new Vector2(2f, size.y * 0.45f); sheen.offsetMax = new Vector2(-2f, -2f);
        Spr(sheen, HudSprites.Sheen, new Color(HG.r, HG.g, HG.b, 0.05f));
        Spr(Stretch("Line", rt), HudSprites.PanelLine, EDGE, Image.Type.Sliced);
        Spr(Stretch("Accent", rt), HudSprites.PanelAccent, new Color(HG.r, HG.g, HG.b, 0.75f), Image.Type.Sliced);
        return rt;
    }

    void CardTitle(Transform card, string label, float hw, float y)
    {
        Img(Rect("TitleBar", card, new Vector2(-hw + 1.5f, y), new Vector2(3f, 14f)), HG);
        AddTxt(card, label, new Vector2(-hw + 10f, y), new Vector2(140f, 20f), 13, HG, TextAnchor.MiddleLeft, FontStyle.Bold);
    }

    void Divider(Transform card, float y, float w)
        => Spr(Rect("Div", card, new Vector2(0f, y), new Vector2(w, 2f)), HudSprites.GlowLine, EDGE);

    // 상태 캡슐 (테두리 + 옅은 면) — 텍스트 색은 코드가 상태에 따라 바꾼다
    Text Pill(Transform card, string s, Vector2 pos, Vector2 size, Color c)
    {
        var rt = Rect("Pill", card, pos, size);
        Spr(rt, HudSprites.Pill, new Color(c.r, c.g, c.b, 0.12f), Image.Type.Sliced);
        Spr(Stretch("Line", rt), HudSprites.PillLine, new Color(c.r, c.g, c.b, 0.60f), Image.Type.Sliced);
        return AddTxt(rt, s, Vector2.zero, size, 12, c, TextAnchor.MiddleCenter, FontStyle.Bold);
    }

    // 라벨(좌) + 값(우) 한 줄. 값 Text 반환
    Text Row(Transform card, string label, string value, float hw, float y)
    {
        AddTxt(card, label, new Vector2(-hw, y), new Vector2(110f, 20f), 12, HGD, TextAnchor.MiddleLeft);
        return AddTxt(card, value, new Vector2(hw, y), new Vector2(110f, 22f), 16, INK, TextAnchor.MiddleRight, FontStyle.Bold);
    }

    Text MissileRow(Transform card, string label, string value, float hw, float y)
    {
        Spr(Rect("Msl", card, new Vector2(-hw + 4f, y), new Vector2(6f, 22f)), HudSprites.Missile, HGD);
        AddTxt(card, label, new Vector2(-hw + 16f, y), new Vector2(110f, 20f), 12, HGD, TextAnchor.MiddleLeft);
        return AddTxt(card, value, new Vector2(hw, y), new Vector2(60f, 22f), 16, INK, TextAnchor.MiddleRight, FontStyle.Bold);
    }

    // 20칸 세그먼트 게이지 (트랙 + Filled 채움)
    Image SegBar(Transform card, string name, float y, float w)
    {
        var track = Rect(name + "Track", card, new Vector2(0f, y), new Vector2(w, 9f));
        Spr(track, HudSprites.SegBar, new Color(HG.r, HG.g, HG.b, 0.12f));
        var fill = Spr(Stretch(name, track), HudSprites.SegBar, HG, Image.Type.Filled);
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 0.3f;
        return fill;
    }

    // 테이프 현재값 박스: 다크 면 + 노치 테두리. pointLeft 면 노치가 왼쪽(고도 테이프)
    Text Readout(Transform panel, Vector2 pos, Vector2 size, bool pointLeft, TextAnchor align)
    {
        var box = Rect("Readout", panel, pos, size);
        var back  = Spr(Stretch("Back", box), HudSprites.ReadoutFill, new Color(0.01f, 0.05f, 0.03f, 0.80f), Image.Type.Sliced);
        var front = Spr(Stretch("Line", box), HudSprites.Readout, HG, Image.Type.Sliced);
        if (pointLeft) { back.rectTransform.localScale = front.rectTransform.localScale = new Vector3(-1f, 1f, 1f); }
        float pad = 14f, notch = 14f;
        var txPos = new Vector2(pointLeft ? -size.x * 0.5f + notch + pad : size.x * 0.5f - notch - pad, 0f);
        return AddTxt(box, "0", txPos, new Vector2(size.x - 24f, size.y), 26, INK, align, FontStyle.Bold);
    }

    // 정렬에 맞춰 피벗을 잡는다 (MiddleLeft → pos 가 왼쪽 끝, MiddleRight → 오른쪽 끝)
    Text AddTxt(Transform parent,string content,Vector2 pos,Vector2 size,
                int fs,Color color,TextAnchor anchor,FontStyle style=FontStyle.Normal,bool halo=false)
    {
        var go=new GameObject("T"); go.transform.SetParent(parent,false);
        var rt=go.AddComponent<RectTransform>();
        rt.anchorMin=rt.anchorMax=new Vector2(.5f,.5f);
        if(anchor==TextAnchor.MiddleLeft)  rt.pivot=new Vector2(0f,.5f);
        if(anchor==TextAnchor.MiddleRight) rt.pivot=new Vector2(1f,.5f);
        rt.anchoredPosition=pos; rt.sizeDelta=size;
        var txt=go.AddComponent<Text>();
        txt.text=content; txt.font=fnt;
        txt.fontSize=fs; txt.color=color; txt.fontStyle=style;
        txt.alignment=anchor; txt.raycastTarget=false;
        txt.horizontalOverflow=HorizontalWrapMode.Overflow;
        txt.verticalOverflow=VerticalWrapMode.Overflow;
        if(halo)
        {
            var o=go.AddComponent<Outline>();
            o.effectColor=HALO; o.effectDistance=new Vector2(1.2f,-1.2f);
        }
        return txt;
    }
}

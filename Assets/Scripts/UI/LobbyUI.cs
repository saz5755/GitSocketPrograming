using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 로비 씬 UI — Premium Military Flight Simulator 디자인.
/// UITheme 기반 통일된 색상/스타일 적용.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    Font uiFont;
    Transform roomListContent;
    Text statusText;
    Text pilotLabel;
    Text _avatarText;
    Text _countText;
    Text _emptyText;
    GameObject _createModal;
    InputField _roomNameInput;
    string     _createMode = RoomModeSO.Combat;   // 방 생성 모달에서 고른 모드

    void Awake()
    {
        EnsureManagers();
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        UIStartup.SetCameraBackground(Camera.main);
    }

    void Start()
    {
        BuildUI();
        SubscribeEvents();
        StartCoroutine(RequestRoomList());
    }

    void OnDestroy()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;
        sc.OnRoomList         -= HandleRoomList;
        sc.OnCreateRoomResult -= HandleCreateRoomResult;
    }

    void EnsureManagers()
    {
        if (GameManager.Instance == null)
            new GameObject("[GameManager]").AddComponent<GameManager>();
        if (NetworkManager.Instance == null)
            new GameObject("[NetworkManager]").AddComponent<NetworkManager>();
        if (UnityMainThreadDispatcher.Instance == null)
            new GameObject("[Dispatcher]").AddComponent<UnityMainThreadDispatcher>();
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    void SubscribeEvents()
    {
        var sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;
        sc.OnRoomList         += HandleRoomList;
        sc.OnCreateRoomResult += HandleCreateRoomResult;
    }

    IEnumerator RequestRoomList()
    {
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsConnected())
        {
            SetStatus("서버 미연결 — 로그인으로 복귀", true);
            yield return new WaitForSeconds(1.5f);
            SceneManager.LoadScene("LoginScene");
            yield break;
        }
        SetStatus("전투 구역 데이터 수신 중...", false);
        NetworkManager.Instance.socketClient.RequestRoomList();
    }

    // ── UI 빌드 ──────────────────────────────────────────────────────────
    const float SideMargin = 110f;   // 좌우 여백 (1920 기준)
    const float PanelW     = 820f;   // 존 목록 패널 폭
    const float PanelPad   = 32f;

    void BuildUI()
    {
        var t = UITheme.Instance;

        var canvasGO = new GameObject("LobbyCanvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        var root = canvasGO.transform;

        BuildBackground(root, t);
        BuildHeader(root, t);
        BuildZonePanel(root, t);
        BuildTagline(root, t);
    }

    // ── 1. 배경: 디지털 터널 속 홀로그램 KF-21 (HoloStage) + 가독성 그라데이션 ──
    void BuildBackground(Transform root, UITheme t)
    {
        var hero = MakeRect("Hero", root, Vector2.zero, Vector2.zero);
        hero.anchorMin = Vector2.zero; hero.anchorMax = Vector2.one;
        hero.offsetMin = hero.offsetMax = Vector2.zero;
        var baseImg = AddImg(hero, t.primaryBG);
        baseImg.sprite = CreateVerticalGradient(HexC("#02060D"), HexC("#071526"), HexC("#0B2036"));

        HoloStage.Create(hero);

        // 좌측 패널 가독성
        var side = UIBackdropFit.MakeGradient(hero, "SideFade", true, new Color(0.01f, 0.03f, 0.07f), 0.80f, 0f);
        side.anchorMin = Vector2.zero; side.anchorMax = new Vector2(0.58f, 1f);
        side.offsetMin = side.offsetMax = Vector2.zero;
        var top = UIBackdropFit.MakeGradient(hero, "TopFade", false, new Color(0.01f, 0.03f, 0.06f), 0f, 0.55f);
        top.anchorMin = new Vector2(0f, 0.84f); top.anchorMax = Vector2.one;
        top.offsetMin = top.offsetMax = Vector2.zero;
        var bot = UIBackdropFit.MakeGradient(hero, "BottomFade", false, new Color(0.01f, 0.03f, 0.06f), 0.65f, 0f);
        bot.anchorMin = Vector2.zero; bot.anchorMax = new Vector2(1f, 0.14f);
        bot.offsetMin = bot.offsetMax = Vector2.zero;

        hero.gameObject.AddComponent<UIFadeIn>().Configure(0f, 0.35f, 0f);
    }

    // ── 2. 헤더: 좌측 로고 / 우측 파일럿 + 연결 해제 ───────────────────────
    void BuildHeader(Transform root, UITheme t)
    {
        var bar = MakeRect("Header", root, Vector2.zero, Vector2.zero);
        bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1);
        bar.pivot = new Vector2(0.5f, 1);
        bar.offsetMin = new Vector2(0, -110); bar.offsetMax = Vector2.zero;

        // 로고
        var logo = MakeRect("Logo", bar, new Vector2(SideMargin, -26), new Vector2(360, 60));
        logo.anchorMin = logo.anchorMax = new Vector2(0, 1); logo.pivot = new Vector2(0, 1);
        var logoTx = AddTextFill(logo, "KF-21", 46, Color.white, FontStyle.BoldAndItalic);
        logoTx.alignment = TextAnchor.MiddleLeft;
        UIType.Gradient(logoTx, UIType.Ice, UIType.Steel, new Color(0.42f, 0.60f, 1f));
        UIType.Glow(logoTx, UIType.Cyan, 2f, 0.20f);
        UIType.Swoosh(logo, new Vector2(-14f, -46f), new Vector2(240f, 16f), new Color(0.30f, 0.72f, 1f, 0.5f));
        var sub = MakeRect("LogoSub", bar, new Vector2(SideMargin + 4, -80), new Vector2(360, 18));
        sub.anchorMin = sub.anchorMax = new Vector2(0, 1); sub.pivot = new Vector2(0, 1);
        var bTx = AddTextFill(sub, UIType.Track("BORAMAE", 2), t.textXS + 1, Color.white, FontStyle.Italic);
        bTx.alignment = TextAnchor.MiddleLeft;
        UIType.Gradient(bTx, UIType.Ice, UIType.Steel, new Color(0.55f, 0.68f, 0.95f));
        UIType.AccentLine(sub, new Vector2(0f, -22f), 120f, 2f, t.accentCyan, 0.9f, 0f);

        // 연결 해제 (우측 끝)
        BuildThemedBtn(bar, "연결 해제", new Vector2(-SideMargin, 0), new Vector2(120, 40),
            new Vector2(1, 0.5f), new Vector2(1, 0.5f), UIThemedButton.Style.Ghost, OnLogoutClick);
        // 피벗을 우측 끝으로
        ((RectTransform)bar.GetChild(bar.childCount - 1)).pivot = new Vector2(1, 0.5f);

        // 파일럿 칩
        var chip = MakeRect("PilotChip", bar, new Vector2(-SideMargin - 140, 0), new Vector2(260, 40));
        chip.anchorMin = chip.anchorMax = new Vector2(1, 0.5f); chip.pivot = new Vector2(1, 0.5f);
        UIShapes.Round(AddImg(chip, new Color(t.panelBG.r, t.panelBG.g, t.panelBG.b, 0.75f)), 12);
        UIShapes.AddBorder(chip, 12, new Color(t.border.r, t.border.g, t.border.b, 0.9f));

        var av = MakeRect("Avatar", chip, new Vector2(7f, 0f), new Vector2(26f, 26f));
        av.anchorMin = av.anchorMax = new Vector2(0, 0.5f); av.pivot = new Vector2(0, 0.5f);
        UIShapes.Round(AddImg(av, new Color(t.primaryBlue.r, t.primaryBlue.g, t.primaryBlue.b, 0.35f)), 13);
        UIShapes.AddBorder(av, 13, new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, 0.7f));
        _avatarText = AddTextFill(av, "P", t.textS, t.textPrimary, FontStyle.Bold);
        var dot = MakeRect("Online", chip, new Vector2(30f, -8f), new Vector2(9f, 9f));
        dot.anchorMin = dot.anchorMax = new Vector2(0, 0.5f); dot.pivot = new Vector2(0, 0.5f);
        UIShapes.Round(AddImg(dot, t.success), 4);
        UIShapes.AddBorder(dot, 4, new Color(t.panelBG.r, t.panelBG.g, t.panelBG.b, 1f));

        var pil = MakeRect("PilotText", chip, Vector2.zero, Vector2.zero);
        pil.anchorMin = Vector2.zero; pil.anchorMax = Vector2.one;
        pil.offsetMin = new Vector2(46, 0); pil.offsetMax = new Vector2(-12, 0);
        pilotLabel = AddTextFill(pil, "PILOT", t.textS, t.textPrimary, FontStyle.Bold);
        pilotLabel.alignment = TextAnchor.MiddleLeft;

        if (GameManager.Instance != null && !string.IsNullOrEmpty(GameManager.Instance.myNickname))
        {
            pilotLabel.text = GameManager.Instance.myNickname.ToUpper();
            _avatarText.text = GameManager.Instance.myNickname.Substring(0, 1).ToUpper();
        }

        bar.gameObject.AddComponent<UIFadeIn>().Configure(0.05f, t.panelTransition, 8f);
    }

    // ── 3. 좌측 글래스 패널: 전투 구역 목록 ─────────────────────────────────
    void BuildZonePanel(Transform root, UITheme t)
    {
        var panel = MakeRect("ZonePanel", root, Vector2.zero, Vector2.zero);
        panel.anchorMin = new Vector2(0, 0); panel.anchorMax = new Vector2(0, 1);
        panel.pivot = new Vector2(0, 0.5f);
        panel.anchoredPosition = new Vector2(SideMargin, -10f);
        panel.sizeDelta = new Vector2(PanelW, -240f);   // 위 130 / 아래 110 여백
        // 그림자 + 유리 패널 + 상단 하이라이트 (외곽선 없음 — 홀로그램 배경과 겹쳐 튀어 보여 제거)
        UIShapes.AddPanelSkin(panel, 18,
            new Color(t.panelBG.r * 0.85f, t.panelBG.g * 0.85f, t.panelBG.b * 0.9f, 0.94f),
            Color.clear);
        var gloss = UIBackdropFit.MakeGradient(panel, "Gloss", false, t.primaryBlue, 0f, 0.04f);
        gloss.anchorMin = new Vector2(0.01f, 0.72f); gloss.anchorMax = new Vector2(0.99f, 0.99f);
        gloss.offsetMin = gloss.offsetMax = Vector2.zero;

        // 헤더 영역
        var hdr = MakeRect("PanelHdr", panel, Vector2.zero, Vector2.zero);
        hdr.anchorMin = new Vector2(0, 1); hdr.anchorMax = new Vector2(1, 1);
        hdr.pivot = new Vector2(0.5f, 1);
        hdr.offsetMin = new Vector2(0, -92); hdr.offsetMax = Vector2.zero;

        var title = MakeRect("Title", hdr, Vector2.zero, Vector2.zero);
        title.anchorMin = new Vector2(0, 0.5f); title.anchorMax = new Vector2(1, 1);
        title.offsetMin = new Vector2(PanelPad, 0); title.offsetMax = new Vector2(-PanelPad, -8);
        var titleTx = AddTextFill(title, "전투 구역 선택", t.titleM - 2, Color.white, FontStyle.Bold);
        titleTx.alignment = TextAnchor.LowerLeft;
        UIType.Gradient(titleTx, UIType.Ice, new Color(0.9f, 0.95f, 1f), new Color(0.68f, 0.8f, 1f));
        var subT = MakeRect("SubTitle", hdr, Vector2.zero, Vector2.zero);
        subT.anchorMin = new Vector2(0, 0); subT.anchorMax = new Vector2(1, 0.5f);
        subT.offsetMin = new Vector2(PanelPad, 16); subT.offsetMax = new Vector2(-PanelPad, 0);
        AddTextFill(subT, UIType.Track("COMBAT ZONES", 1), t.textXS, t.accentCyan, FontStyle.Bold)
            .alignment = TextAnchor.UpperLeft;

        var cnt = MakeRect("CountChip", hdr, new Vector2(-PanelPad, 0), new Vector2(110, 30));
        cnt.anchorMin = cnt.anchorMax = new Vector2(1, 0.5f); cnt.pivot = new Vector2(1, 0.5f);
        UIShapes.Round(AddImg(cnt, new Color(t.primaryBlue.r, t.primaryBlue.g, t.primaryBlue.b, 0.14f)), 15);
        UIShapes.AddBorder(cnt, 15, new Color(t.primaryBlue.r, t.primaryBlue.g, t.primaryBlue.b, 0.45f));
        _countText = AddTextFill(cnt, "— ZONES", t.textXS + 1, t.accentCyan, FontStyle.Bold);

        // 테이블 헤더
        var tblHdr = MakeRect("TblHdr", panel, Vector2.zero, Vector2.zero);
        tblHdr.anchorMin = new Vector2(0, 1); tblHdr.anchorMax = new Vector2(1, 1);
        tblHdr.pivot = new Vector2(0.5f, 1);
        tblHdr.offsetMin = new Vector2(PanelPad, -132); tblHdr.offsetMax = new Vector2(-PanelPad, -92);
        var tDiv = MakeRect("TblDiv", tblHdr, Vector2.zero, Vector2.zero);
        tDiv.anchorMin = new Vector2(0, 0); tDiv.anchorMax = new Vector2(1, 0);
        tDiv.offsetMin = Vector2.zero; tDiv.offsetMax = new Vector2(0, 1);
        AddImg(tDiv, new Color(t.border.r, t.border.g, t.border.b, 0.7f));
        BuildTableHeader(tblHdr, t);

        // 스크롤 뷰
        var scrollGO = new GameObject("ScrollView");
        scrollGO.transform.SetParent(panel, false);
        var scrollRT = scrollGO.AddComponent<RectTransform>();
        scrollRT.anchorMin = new Vector2(0, 0); scrollRT.anchorMax = new Vector2(1, 1);
        scrollRT.offsetMin = new Vector2(PanelPad, 100); scrollRT.offsetMax = new Vector2(-PanelPad, -138);
        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.scrollSensitivity = 30f;

        var viewportGO = new GameObject("Viewport");
        viewportGO.transform.SetParent(scrollGO.transform, false);
        var vpRT = viewportGO.AddComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = vpRT.offsetMax = Vector2.zero;
        viewportGO.AddComponent<RectMask2D>();   // 알파 0 Image + Mask 는 내용이 전부 잘려 보이지 않는다

        var contentGO = new GameObject("Content");
        contentGO.transform.SetParent(viewportGO.transform, false);
        var contentRT = contentGO.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1); contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.offsetMin = contentRT.offsetMax = Vector2.zero;
        var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var csf = contentGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = vpRT;
        scroll.content  = contentRT;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        roomListContent = contentGO.transform;

        // 슬림 스크롤바
        var sbGO = new GameObject("Scrollbar");
        sbGO.transform.SetParent(scrollGO.transform, false);
        var sbRT = sbGO.AddComponent<RectTransform>();
        sbRT.anchorMin = new Vector2(1, 0); sbRT.anchorMax = new Vector2(1, 1);
        sbRT.pivot = new Vector2(1, 0.5f);
        sbRT.offsetMin = new Vector2(-6, 4); sbRT.offsetMax = new Vector2(0, -4);
        var track = sbGO.AddComponent<Image>();
        UIShapes.Round(track, 3);
        track.color = new Color(1f, 1f, 1f, 0.05f);
        var handleGO = new GameObject("Handle");
        handleGO.transform.SetParent(sbGO.transform, false);
        var hRT = handleGO.AddComponent<RectTransform>();
        hRT.anchorMin = Vector2.zero; hRT.anchorMax = Vector2.one;
        hRT.offsetMin = hRT.offsetMax = Vector2.zero;
        var hImg = handleGO.AddComponent<Image>();
        UIShapes.Round(hImg, 3);
        hImg.color = new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, 0.55f);
        var sb = sbGO.AddComponent<Scrollbar>();
        sb.handleRect = hRT; sb.targetGraphic = hImg;
        sb.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = sb;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        vpRT.offsetMax = new Vector2(-12, 0);

        // 빈 목록 안내
        var empty = MakeRect("EmptyState", scrollGO.transform, Vector2.zero, Vector2.zero);
        empty.anchorMin = Vector2.zero; empty.anchorMax = Vector2.one;
        empty.offsetMin = empty.offsetMax = Vector2.zero;
        _emptyText = AddTextFill(empty, "생성된 전투 구역이 없습니다\n'구역 생성'으로 새 구역을 만들어 출격하세요",
                                 t.textM, t.textSecondary, FontStyle.Normal);
        _emptyText.lineSpacing = 1.4f;
        _emptyText.gameObject.SetActive(false);

        // 푸터: 상태 텍스트 + 액션 버튼
        var foot = MakeRect("PanelFooter", panel, Vector2.zero, Vector2.zero);
        foot.anchorMin = new Vector2(0, 0); foot.anchorMax = new Vector2(1, 0);
        foot.pivot = new Vector2(0.5f, 0);
        foot.offsetMin = Vector2.zero; foot.offsetMax = new Vector2(0, 88);
        var fdiv = MakeRect("FootDiv", foot, Vector2.zero, Vector2.zero);
        fdiv.anchorMin = new Vector2(0, 1); fdiv.anchorMax = new Vector2(1, 1);
        fdiv.offsetMin = new Vector2(PanelPad, 0); fdiv.offsetMax = new Vector2(-PanelPad, 1);
        AddImg(fdiv, new Color(t.border.r, t.border.g, t.border.b, 0.8f));

        var statGO = MakeRect("Status", foot, Vector2.zero, Vector2.zero);
        statGO.anchorMin = new Vector2(0, 0); statGO.anchorMax = new Vector2(0.5f, 1);
        statGO.offsetMin = new Vector2(PanelPad, 0); statGO.offsetMax = Vector2.zero;
        statusText = AddTextFill(statGO, "로딩 중...", t.textS, t.textSecondary, FontStyle.Normal);
        statusText.alignment = TextAnchor.MiddleLeft;

        BuildThemedBtn(foot, "새로고침", new Vector2(-PanelPad - 210, 0), new Vector2(150, 46),
            new Vector2(1, 0.5f), new Vector2(1, 0.5f), UIThemedButton.Style.Outline,
            () =>
            {
                SetStatus("구역 데이터 새로고침...", false);
                NetworkManager.Instance?.socketClient.RequestRoomList();
            });
        BuildThemedBtn(foot, "구역 생성", new Vector2(-PanelPad, 0), new Vector2(190, 46),
            new Vector2(1, 0.5f), new Vector2(1, 0.5f), UIThemedButton.Style.Primary, OnCreateRoomClick);
        // 두 버튼 피벗을 우측 기준으로
        ((RectTransform)foot.GetChild(foot.childCount - 1)).pivot = new Vector2(1, 0.5f);
        ((RectTransform)foot.GetChild(foot.childCount - 2)).pivot = new Vector2(1, 0.5f);

        panel.gameObject.AddComponent<UIFadeIn>().Configure(0.08f, t.panelTransition, 12f);
    }

    void BuildTagline(Transform root, UITheme t)
    {
        var go = MakeRect("Tagline", root, new Vector2(-SideMargin, 40), new Vector2(500, 20));
        go.anchorMin = go.anchorMax = new Vector2(1, 0); go.pivot = new Vector2(1, 0);
        var tx = AddTextFill(go, "더 멀리, 더 높이  ·  KF-21과 함께", t.textXS,
            new Color(t.textPrimary.r, t.textPrimary.g, t.textPrimary.b, 0.55f), FontStyle.Italic);
        tx.alignment = TextAnchor.MiddleRight;
        go.gameObject.AddComponent<UIFadeIn>().Configure(0.25f, t.panelTransition, 0f);
    }

    void BuildTableHeader(RectTransform parent, UITheme t)
    {
        (string label, float xs, float xe)[] cols =
        {
            ("ZONE",        0.00f, 0.10f),
            ("DESIGNATION", 0.10f, 0.45f),
            ("STATUS",      0.45f, 0.62f),
            ("PILOTS",      0.62f, 0.78f),
            ("ACTION",      0.78f, 1.00f),
        };
        Color hdrColor = new Color(t.textSecondary.r, t.textSecondary.g, t.textSecondary.b, 0.85f);
        foreach (var (label, xs, xe) in cols)
        {
            if (string.IsNullOrEmpty(label)) continue;
            var go = new GameObject($"H_{label}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(xs, 0); rt.anchorMax = new Vector2(xe, 1);
            rt.offsetMin = new Vector2(16, 0); rt.offsetMax = Vector2.zero;
            var txt = go.AddComponent<Text>();
            txt.text = label; txt.font = uiFont; txt.fontSize = t.textXS;
            txt.fontStyle = FontStyle.Bold; txt.color = hdrColor;
            txt.alignment = TextAnchor.MiddleLeft; txt.raycastTarget = false;
        }
    }

    // ── 룸 목록 갱신 ───────────────────────────────────────────────────────
    void HandleRoomList(List<RoomInfo> rooms)
    {
        foreach (Transform child in roomListContent)
            Destroy(child.gameObject);

        int n = rooms?.Count ?? 0;
        if (_countText != null) _countText.text = $"{n} ZONES";
        if (_emptyText != null) _emptyText.gameObject.SetActive(n == 0);

        if (n == 0)
        { SetStatus("전투 구역 없음", false); return; }

        int i = 0;
        foreach (var room in rooms)
        {
            var row = BuildRoomRow(room);
            // 행 순차 등장 (알파만 — 위치는 레이아웃 그룹이 관리)
            row.AddComponent<UIFadeIn>().Configure(0.04f * i++, 0.28f, 0f);
        }
        SetStatus($"{n}개 구역 사용 가능  ·  구역을 선택하여 출격", false);
    }

    GameObject BuildRoomRow(RoomInfo room)
    {
        var t = UITheme.Instance;
        bool active = room.playerCount > 0;
        bool isFull = room.playerCount >= room.maxPlayers;

        var row = new GameObject($"Row_{room.roomId}");
        row.transform.SetParent(roomListContent, false);
        row.AddComponent<LayoutElement>().preferredHeight = 68;   // (RectTransform 은 UI 컴포넌트 추가 시 자동 생성)
        var rowImg = row.AddComponent<Image>();
        UIShapes.Round(rowImg, 12);
        Color baseColor  = new Color(1f, 1f, 1f, 0.04f);
        Color hoverColor = new Color(t.primaryBlue.r, t.primaryBlue.g, t.primaryBlue.b, 0.16f);
        rowImg.color = baseColor;
        var rowBorder = UIShapes.AddBorder(row.transform, 12, new Color(t.border.r, t.border.g, t.border.b, 0.45f));
        Color borderBase  = rowBorder.color;
        Color borderHover = new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, 0.55f);

        // ZONE 번호
        var zGO = MakeRect("Zone", row.transform, Vector2.zero, Vector2.zero);
        zGO.anchorMin = new Vector2(0.00f, 0); zGO.anchorMax = new Vector2(0.11f, 1);
        zGO.offsetMin = new Vector2(8, 0); zGO.offsetMax = Vector2.zero;
        var zTop = AddTextFill(zGO, "ZONE", 10, t.textSecondary, FontStyle.Bold);
        zTop.alignment = TextAnchor.LowerCenter; zTop.rectTransform.offsetMin = new Vector2(0, 34);
        var zNum = AddTextFill(zGO, $"{room.roomId:D2}", t.textL + 2, t.accentCyan, FontStyle.Bold);
        zNum.alignment = TextAnchor.UpperCenter; zNum.rectTransform.offsetMax = new Vector2(0, -32);

        // 룸 이름
        var nameGO = MakeRect("Name", row.transform, Vector2.zero, Vector2.zero);
        nameGO.anchorMin = new Vector2(0.11f, 0f); nameGO.anchorMax = new Vector2(0.38f, 1);
        nameGO.offsetMin = new Vector2(12, 0); nameGO.offsetMax = new Vector2(-8, 0);
        AddTextFill(nameGO, room.roomName.ToUpper(), t.textM, t.textPrimary, FontStyle.Bold)
            .alignment = TextAnchor.MiddleLeft;

        // 상태 칩
        var statArea = MakeRect("Stat", row.transform, Vector2.zero, Vector2.zero);
        statArea.anchorMin = new Vector2(0.38f, 0); statArea.anchorMax = new Vector2(0.64f, 1);
        statArea.offsetMin = new Vector2(8, 0); statArea.offsetMax = Vector2.zero;
        var chip = MakeRect("Chip", statArea, Vector2.zero, new Vector2(104, 26));
        chip.anchorMin = chip.anchorMax = new Vector2(0, 0.5f); chip.pivot = new Vector2(0, 0.5f);
        Color chipCol = active ? t.success : t.textSecondary;
        UIShapes.Round(AddImg(chip, new Color(chipCol.r, chipCol.g, chipCol.b, active ? 0.14f : 0.10f)), 13);
        UIShapes.AddBorder(chip, 13, new Color(chipCol.r, chipCol.g, chipCol.b, active ? 0.45f : 0.30f));
        AddTextFill(chip, active ? "ACTIVE" : "STANDBY", 11, chipCol, FontStyle.Bold);

        // 모드 배지
        bool airshow = room.mode == RoomModeSO.Airshow;
        var badge = MakeRect("Mode", statArea, Vector2.zero, new Vector2(92, 26));
        badge.anchorMin = badge.anchorMax = new Vector2(0, 0.5f); badge.pivot = new Vector2(0, 0.5f);
        badge.anchoredPosition = new Vector2(112, 0);
        Color modeCol = airshow ? t.accentCyan : t.warning;
        UIShapes.Round(AddImg(badge, new Color(modeCol.r, modeCol.g, modeCol.b, 0.12f)), 13);
        UIShapes.AddBorder(badge, 13, new Color(modeCol.r, modeCol.g, modeCol.b, 0.45f));
        AddTextFill(badge, airshow ? "AIRSHOW" : "COMBAT", 11, modeCol, FontStyle.Bold);

        // 파일럿 수
        var pilotGO = MakeRect("Pilots", row.transform, Vector2.zero, Vector2.zero);
        pilotGO.anchorMin = new Vector2(0.64f, 0); pilotGO.anchorMax = new Vector2(0.79f, 1);
        pilotGO.offsetMin = new Vector2(8, 0); pilotGO.offsetMax = Vector2.zero;
        Color pilotColor = isFull ? t.warning : t.textPrimary;
        AddTextFill(pilotGO, $"{room.playerCount} / {room.maxPlayers}", t.textM, pilotColor, FontStyle.Bold)
            .alignment = TextAnchor.MiddleLeft;

        // 입장 버튼
        var btnArea = MakeRect("BtnArea", row.transform, Vector2.zero, Vector2.zero);
        btnArea.anchorMin = new Vector2(0.79f, 0.5f); btnArea.anchorMax = new Vector2(1.00f, 0.5f);
        btnArea.offsetMin = new Vector2(0, -20); btnArea.offsetMax = new Vector2(-16, 20);

        if (!isFull)
        {
            var btnGO = new GameObject("JoinBtn");
            btnGO.transform.SetParent(btnArea, false);
            var brt = btnGO.AddComponent<RectTransform>();
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            btnGO.AddComponent<Image>();
            AddTextFill(brt, "입장", t.textS, t.textPrimary, FontStyle.Bold);
            int rid = room.roomId;
            var themed = btnGO.AddComponent<UIThemedButton>();
            themed.ButtonStyle = UIThemedButton.Style.Primary;
            themed.OnClick = () => OnJoinRoom(rid);
        }
        else
        {
            var fullGO = MakeRect("Full", btnArea, Vector2.zero, Vector2.zero);
            fullGO.anchorMin = Vector2.zero; fullGO.anchorMax = Vector2.one;
            fullGO.offsetMin = fullGO.offsetMax = Vector2.zero;
            AddTextFill(fullGO, "FULL", t.textS, t.textDisabled, FontStyle.Bold);
        }

        // 호버
        var trigger = row.AddComponent<EventTrigger>();
        AddEvt(trigger, EventTriggerType.PointerEnter, _ => { rowImg.color = hoverColor; rowBorder.color = borderHover; });
        AddEvt(trigger, EventTriggerType.PointerExit,  _ => { rowImg.color = baseColor;  rowBorder.color = borderBase; });
        return row;
    }

    // ── CREATE ZONE 모달 ──────────────────────────────────────────────────
    void OnCreateRoomClick()
    {
        if (_createModal != null) return;
        var t = UITheme.Instance;

        var canvasGO = GameObject.Find("LobbyCanvas");
        var overlay  = new GameObject("CreateRoomModal");
        overlay.transform.SetParent(canvasGO?.transform ?? transform, false);
        var ort = overlay.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;
        overlay.AddComponent<Image>().color = new Color(0.01f, 0.03f, 0.07f, 0.72f);
        _createModal = overlay;

        var panel = MakeRect("Panel", overlay.transform, Vector2.zero, new Vector2(540, 420));
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        UIShapes.AddPanelSkin(panel, 16,
            new Color(t.panelBG.r * 0.9f, t.panelBG.g * 0.9f, t.panelBG.b * 0.95f, 0.98f),
            new Color(t.border.r + 0.04f, t.border.g + 0.06f, t.border.b + 0.1f, 0.95f));

        // 타이틀
        var titleGO = MakeRect("Title", panel, new Vector2(0, -32), new Vector2(500, 36));
        titleGO.anchorMin = titleGO.anchorMax = new Vector2(0.5f, 1);
        AddTextFill(titleGO, "전투 구역 생성", t.textL + 4, t.textPrimary, FontStyle.Bold);

        // 레이블
        var lblGO = MakeRect("Lbl", panel, new Vector2(-230, 98), new Vector2(220, 20));
        lblGO.anchorMin = lblGO.anchorMax = new Vector2(0.5f, 0.5f);
        lblGO.pivot = new Vector2(0, 0.5f);
        AddTextFill(lblGO, "구역 이름", t.textXS, t.textSecondary, FontStyle.Bold).alignment = TextAnchor.MiddleLeft;

        // InputField
        var inputGO = new GameObject("RoomNameInput");
        inputGO.transform.SetParent(panel, false);
        var inputRT = inputGO.AddComponent<RectTransform>();
        inputRT.anchorMin = inputRT.anchorMax = new Vector2(0.5f, 0.5f);
        inputRT.anchoredPosition = new Vector2(0, 64);
        inputRT.sizeDelta        = new Vector2(460, 46);
        UIShapes.Round(AddImg(inputRT, new Color(0f, 0f, 0f, 0.35f)), 10);
        UIShapes.AddBorder(inputRT, 10, new Color(t.border.r, t.border.g, t.border.b, 0.9f));

        var txtGO = new GameObject("Text");
        txtGO.transform.SetParent(inputGO.transform, false);
        var trt = txtGO.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(12, 0); trt.offsetMax = new Vector2(-12, 0);
        var tc = txtGO.AddComponent<Text>();
        tc.font = uiFont; tc.fontSize = t.textM; tc.color = t.textPrimary;
        tc.alignment = TextAnchor.MiddleLeft; tc.supportRichText = false;

        var phGO = new GameObject("Placeholder");
        phGO.transform.SetParent(inputGO.transform, false);
        var prt = phGO.AddComponent<RectTransform>();
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(12, 0); prt.offsetMax = new Vector2(-12, 0);
        var ph = phGO.AddComponent<Text>();
        ph.font = uiFont; ph.fontSize = t.textM; ph.color = t.textDisabled;
        ph.fontStyle = FontStyle.Italic; ph.alignment = TextAnchor.MiddleLeft;
        ph.text = "구역 이름 입력...";

        _roomNameInput = inputGO.AddComponent<InputField>();
        _roomNameInput.textComponent  = tc;
        _roomNameInput.placeholder    = ph;
        _roomNameInput.characterLimit = 24;

        // 방 모드 — 입장 후에는 바꿀 수 없다
        var modeLbl = MakeRect("ModeLbl", panel, new Vector2(-230, 18), new Vector2(220, 20));
        modeLbl.anchorMin = modeLbl.anchorMax = new Vector2(0.5f, 0.5f);
        modeLbl.pivot = new Vector2(0, 0.5f);
        AddTextFill(modeLbl, "비행 모드", t.textXS, t.textSecondary, FontStyle.Bold).alignment = TextAnchor.MiddleLeft;

        var descGO = MakeRect("ModeDesc", panel, new Vector2(0, -78), new Vector2(460, 36));
        descGO.anchorMin = descGO.anchorMax = new Vector2(0.5f, 0.5f);
        var desc = AddTextFill(descGO, "", t.textXS, t.textSecondary, FontStyle.Normal);
        desc.alignment = TextAnchor.UpperCenter;

        UIThemedButton airBtn = null, cmbBtn = null;
        void SelectMode(string m)
        {
            _createMode = m;
            if (airBtn != null) airBtn.Selected = m == RoomModeSO.Airshow;
            if (cmbBtn != null) cmbBtn.Selected = m == RoomModeSO.Combat;
            desc.text = m == RoomModeSO.Airshow
                ? "에스코트 편대와 함께 비행 — 무장·점수 없음"
                : "적 기체와 교전 — 격추·재출격, 킬 피드·스코어보드";
        }
        airBtn = BuildModalBtn(panel, "AIRSHOW", new Vector2(-117, -24), new Vector2(226, 44),
            UIThemedButton.Style.Outline, () => SelectMode(RoomModeSO.Airshow));
        cmbBtn = BuildModalBtn(panel, "COMBAT", new Vector2(117, -24), new Vector2(226, 44),
            UIThemedButton.Style.Outline, () => SelectMode(RoomModeSO.Combat));
        SelectMode(_createMode);

        // 버튼 행
        BuildModalBtn(panel, "생성", new Vector2(-110, -150), new Vector2(200, 48),
            UIThemedButton.Style.Primary, () =>
            {
                string name = _roomNameInput?.text?.Trim() ?? "";
                if (string.IsNullOrEmpty(name))
                    name = $"{GameManager.Instance?.myNickname ?? "Unknown"}'s Zone";
                SetStatus("전투 구역 생성 중...", false);
                NetworkManager.Instance?.socketClient.SendCreateRoom(name, 8, _createMode);
                CloseCreateModal();
            });

        BuildModalBtn(panel, "취소", new Vector2(110, -150), new Vector2(200, 48),
            UIThemedButton.Style.Secondary, CloseCreateModal);

        overlay.AddComponent<UIFadeIn>().Configure(0f, t.panelTransition, 0f);
    }

    UIThemedButton BuildModalBtn(RectTransform parent, string label, Vector2 pos, Vector2 size,
                       UIThemedButton.Style style, System.Action onClick)
    {
        var t = UITheme.Instance;
        var go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        go.AddComponent<Image>();
        go.AddComponent<Outline>().effectDistance = new Vector2(1, 1);
        var lbl = new GameObject("L");
        lbl.transform.SetParent(go.transform, false);
        var lrt = lbl.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var ltx = lbl.AddComponent<Text>();
        ltx.text = label; ltx.font = uiFont; ltx.fontSize = t.textM;
        ltx.fontStyle = FontStyle.Bold; ltx.alignment = TextAnchor.MiddleCenter;
        ltx.color = t.textPrimary;
        var btn = go.AddComponent<UIThemedButton>();
        btn.ButtonStyle = style;
        btn.OnClick = onClick;
        return btn;
    }

    void CloseCreateModal()
    {
        if (_createModal != null) { Destroy(_createModal); _createModal = null; }
        _roomNameInput = null;
    }

    void HandleCreateRoomResult(CreateRoomResultPacket p)
    {
        if (p.success)
        {
            if (GameManager.Instance != null) GameManager.Instance.currentRoomId = p.roomId;
            SetStatus($"구역 생성 완료: {p.roomName}  —  출격 중...", false);
            StartLaunch();
        }
        else
        {
            SetStatus($"구역 생성 실패: {p.errorMessage}", true);
        }
    }

    // ── 액션 ─────────────────────────────────────────────────────────────
    void OnJoinRoom(int roomId)
    {
        if (GameManager.Instance != null) GameManager.Instance.currentRoomId = roomId;
        SetStatus($"Zone {roomId:D2} 출격 중...", false);
        StartLaunch();
    }

    bool _launching;

    // 출격 연출 후 GameScene 으로 이동 (연출 에셋이 없으면 즉시 이동)
    void StartLaunch()
    {
        if (_launching) return;
        _launching = true;
        var canvas = GameObject.Find("LobbyCanvas");
        if (canvas == null) { SceneManager.LoadScene("GameScene"); return; }
        LaunchSequence.Play(canvas.transform, "GameScene");
    }

    void OnLogoutClick()
    {
        var canvas = GameObject.Find("LobbyCanvas");
        if (canvas == null) { DoLogout(); return; }
        UIModal.Show(canvas.transform, "서버 연결을 해제하시겠습니까?",
                     "로그인 화면으로 돌아갑니다.", "확인", "취소", DoLogout);
    }

    void DoLogout()
    {
        NetworkManager.Instance?.socketClient.Disconnect();
        SetStatus("연결 해제 중...", false);
        StartCoroutine(ReturnToLogin());
    }

    IEnumerator ReturnToLogin()
    {
        yield return new WaitForSeconds(0.3f);
        SceneManager.LoadScene("LoginScene");
    }

    void SetStatus(string msg, bool warn)
    {
        if (statusText == null) return;
        var t = UITheme.Instance;
        statusText.text  = msg;
        statusText.color = warn ? t.error : t.textSecondary;
    }

    IEnumerator UpdateClock(Text txt)
    {
        while (txt != null)
        {
            txt.text = $"UTC {System.DateTime.UtcNow:HH:mm:ss}";
            yield return new WaitForSeconds(1f);
        }
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────
    RectTransform MakeRect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    Image AddImg(RectTransform rt, Color color) => AddImg(rt.gameObject, color);
    Image AddImg(GameObject go, Color color)
    {
        var img = go.AddComponent<Image>();
        img.color = color; img.raycastTarget = false;
        return img;
    }

    Text AddTextFill(RectTransform parent, string content, int sz, Color color, FontStyle style)
    {
        var go = new GameObject("T");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var tx = go.AddComponent<Text>();
        tx.font = uiFont; tx.fontSize = sz; tx.color = color;
        tx.fontStyle = style; tx.text = content;
        tx.alignment = TextAnchor.MiddleCenter;
        tx.raycastTarget = false;
        return tx;
    }

    void AddOutline(GameObject go, Color col, float w)
    {
        var o = go.AddComponent<Outline>();
        o.effectColor = col; o.effectDistance = new Vector2(w, w);
    }

    void BuildThemedBtn(RectTransform parent, string label, Vector2 pos, Vector2 size,
                        Vector2 ancMin, Vector2 ancMax,
                        UIThemedButton.Style style, System.Action onClick)
    {
        var t = UITheme.Instance;
        var go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = ancMin; rt.anchorMax = ancMax;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        go.AddComponent<Image>();
        go.AddComponent<Outline>().effectDistance = new Vector2(1, 1);
        var lbl = new GameObject("L");
        lbl.transform.SetParent(go.transform, false);
        var lrt = lbl.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var ltx = lbl.AddComponent<Text>();
        ltx.text = label; ltx.font = uiFont; ltx.fontSize = t.textS;
        ltx.fontStyle = FontStyle.Bold; ltx.alignment = TextAnchor.MiddleCenter;
        ltx.color = t.textPrimary;
        var btn = go.AddComponent<UIThemedButton>();
        btn.ButtonStyle = style;
        btn.OnClick = onClick;
    }

    static void AddEvt(EventTrigger et, EventTriggerType type,
                        UnityEngine.Events.UnityAction<BaseEventData> cb)
    {
        var e = new EventTrigger.Entry { eventID = type };
        e.callback.AddListener(cb);
        et.triggers.Add(e);
    }

    static Sprite _cachedGradient;
    Sprite CreateVerticalGradient(Color bottom, Color mid, Color top)
    {
        if (_cachedGradient != null) return _cachedGradient;
        const int H = 256;
        var tex = new Texture2D(2, H, TextureFormat.RGBA32, false, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < H; y++)
        {
            float u = y / (float)(H - 1);
            Color c;
            if (u < 0.5f)
            {
                float a = u / 0.5f; a = a * a * (3f - 2f * a);
                c = Color.Lerp(bottom, mid, a);
            }
            else
            {
                float a = (u - 0.5f) / 0.5f; a = a * a * (3f - 2f * a);
                c = Color.Lerp(mid, top, a);
            }
            tex.SetPixel(0, y, c); tex.SetPixel(1, y, c);
        }
        tex.Apply();
        _cachedGradient = Sprite.Create(tex, new Rect(0, 0, 2, H), new Vector2(0.5f, 0.5f));
        return _cachedGradient;
    }

    static Color HexC(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
        return Color.magenta;
    }
}

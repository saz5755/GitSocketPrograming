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
    GameObject _createModal;
    InputField _roomNameInput;

    void Awake()
    {
        EnsureManagers();
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
        BuildTopBar(root, t);
        BuildBottomBar(root, t);
        BuildMainPanel(root, t);
    }

    // ── 1. 그라데이션 배경 ────────────────────────────────────────────────
    void BuildBackground(Transform root, UITheme t)
    {
        var bg = MakeRect("BG", root, Vector2.zero, Vector2.zero);
        bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
        bg.offsetMin = bg.offsetMax = Vector2.zero;
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.sprite = CreateVerticalGradient(HexC("#030810"), HexC("#0A1B2D"), HexC("#0D2440"));
        bgImg.type = Image.Type.Simple;
        bgImg.preserveAspect = false;
        bgImg.raycastTarget = false;

        // 수평 그리드 라인
        for (int i = 0; i < 8; i++)
        {
            float y = -480f + i * 137f;
            var ln = MakeRect($"Grid{i}", root, new Vector2(0, y), new Vector2(0, 1));
            ln.anchorMin = new Vector2(0, 0.5f); ln.anchorMax = new Vector2(1, 0.5f);
            ln.offsetMin = ln.offsetMax = Vector2.zero;
            AddImg(ln, new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, 0.03f));
        }

        // 상단 강조선
        var topLine = MakeRect("TopLine", root, Vector2.zero, new Vector2(0, 2));
        topLine.anchorMin = new Vector2(0, 1); topLine.anchorMax = new Vector2(1, 1);
        topLine.pivot = new Vector2(0.5f, 1);
        topLine.offsetMin = new Vector2(0, -2); topLine.offsetMax = Vector2.zero;
        AddImg(topLine, new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, 0.5f));
    }

    // ── 2. 상단 헤더 바 ───────────────────────────────────────────────────
    void BuildTopBar(Transform root, UITheme t)
    {
        var bar = MakeRect("TopBar", root, Vector2.zero, Vector2.zero);
        bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1);
        bar.pivot = new Vector2(0.5f, 1);
        bar.offsetMin = new Vector2(0, -72); bar.offsetMax = Vector2.zero;
        AddImg(bar, new Color(t.panelBG.r, t.panelBG.g, t.panelBG.b, 0.95f));

        // 하단 경계선
        var divider = MakeRect("BarDivider", bar, Vector2.zero, Vector2.zero);
        divider.anchorMin = new Vector2(0, 0); divider.anchorMax = new Vector2(1, 0);
        divider.offsetMin = new Vector2(0, -1); divider.offsetMax = Vector2.zero;
        AddImg(divider, new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, 0.4f));

        // 좌측 뱃지
        var badge = MakeRect("Badge", bar, new Vector2(40, 0), new Vector2(220, 40));
        badge.anchorMin = new Vector2(0, 0.5f); badge.anchorMax = new Vector2(0, 0.5f);
        badge.pivot = new Vector2(0, 0.5f);
        AddImg(badge, new Color(t.primaryBG.r, t.primaryBG.g, t.primaryBG.b, 0.7f));
        AddOutline(badge.gameObject, t.borderAccent, 1f);
        AddTextFill(badge, "KF-21  •  OPERATIONS", t.textS, t.accentCyan, FontStyle.Bold);

        // 중앙 타이틀
        var titleGO = MakeRect("Title", bar, Vector2.zero, Vector2.zero);
        titleGO.anchorMin = new Vector2(0.5f, 0); titleGO.anchorMax = new Vector2(0.5f, 1);
        titleGO.offsetMin = new Vector2(-400, 0); titleGO.offsetMax = new Vector2(400, 0);
        AddTextFill(titleGO, "TACTICAL  COMBAT  CENTER", t.textL, t.textPrimary, FontStyle.Bold);

        // 파일럿 레이블 (중앙 아래)
        var pilotGO = MakeRect("PilotLabel", bar, new Vector2(0, -6), Vector2.zero);
        pilotGO.anchorMin = new Vector2(0.5f, 0); pilotGO.anchorMax = new Vector2(0.5f, 0);
        pilotGO.pivot = new Vector2(0.5f, 0);
        pilotGO.offsetMin = new Vector2(-200, 0); pilotGO.offsetMax = new Vector2(200, 20);
        pilotLabel = AddTextFill(pilotGO, "PILOT  —", t.textXS, t.textSecondary, FontStyle.Normal);

        // 우측 상태 칩
        var statusChip = MakeRect("StatusChip", bar, new Vector2(-40, 0), new Vector2(180, 32));
        statusChip.anchorMin = new Vector2(1, 0.5f); statusChip.anchorMax = new Vector2(1, 0.5f);
        statusChip.pivot = new Vector2(1, 0.5f);
        AddImg(statusChip, new Color(t.success.r, t.success.g, t.success.b, 0.12f));
        AddOutline(statusChip.gameObject, new Color(t.success.r, t.success.g, t.success.b, 0.5f), 1f);
        AddTextFill(statusChip, "◉  SECURE LINK", t.textXS, t.success, FontStyle.Bold);

        // 우측 UTC 시계
        var clockGO = MakeRect("Clock", bar, new Vector2(-240, 0), new Vector2(140, 0));
        clockGO.anchorMin = new Vector2(1, 0); clockGO.anchorMax = new Vector2(1, 1);
        clockGO.pivot = new Vector2(1, 0.5f);
        var clockTxt = AddTextFill(clockGO, "UTC 00:00:00", t.textXS,
            new Color(t.textSecondary.r, t.textSecondary.g, t.textSecondary.b, 0.7f), FontStyle.Normal);
        StartCoroutine(UpdateClock(clockTxt));

        // 파일럿 닉네임 설정
        if (GameManager.Instance != null && !string.IsNullOrEmpty(GameManager.Instance.myNickname))
            pilotLabel.text = $"PILOT  {GameManager.Instance.myNickname.ToUpper()}";

        bar.gameObject.AddComponent<UIFadeIn>().Configure(0f, t.panelTransition, 8f);
    }

    // ── 3. 하단 액션 바 ───────────────────────────────────────────────────
    void BuildBottomBar(Transform root, UITheme t)
    {
        var bar = MakeRect("BottomBar", root, Vector2.zero, Vector2.zero);
        bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(1, 0);
        bar.pivot = new Vector2(0.5f, 0);
        bar.offsetMin = Vector2.zero; bar.offsetMax = new Vector2(0, 68);
        AddImg(bar, new Color(t.panelBG.r, t.panelBG.g, t.panelBG.b, 0.95f));

        // 상단 경계선
        var divider = MakeRect("BarDivider", bar, Vector2.zero, Vector2.zero);
        divider.anchorMin = new Vector2(0, 1); divider.anchorMax = new Vector2(1, 1);
        divider.offsetMin = Vector2.zero; divider.offsetMax = new Vector2(0, 1);
        AddImg(divider, new Color(t.border.r, t.border.g, t.border.b, 0.8f));

        // REFRESH 버튼
        BuildThemedBtn(bar, "↻  REFRESH", new Vector2(108, 0), new Vector2(180, 44),
            new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            UIThemedButton.Style.Outline,
            () =>
            {
                SetStatus("구역 데이터 새로고침...", false);
                NetworkManager.Instance?.socketClient.RequestRoomList();
            });

        // 상태 텍스트
        var statGO = MakeRect("Status", bar, Vector2.zero, Vector2.zero);
        statGO.anchorMin = new Vector2(0.5f, 0); statGO.anchorMax = new Vector2(0.5f, 1);
        statGO.offsetMin = new Vector2(-350, 0); statGO.offsetMax = new Vector2(350, 0);
        statusText = AddTextFill(statGO, "로딩 중...", t.textXS, t.textSecondary, FontStyle.Normal);

        // CREATE ZONE 버튼 (오른쪽에서 두 번째)
        BuildThemedBtn(bar, "+  CREATE ZONE", new Vector2(-305, 0), new Vector2(190, 44),
            new Vector2(1, 0.5f), new Vector2(1, 0.5f),
            UIThemedButton.Style.Secondary,
            OnCreateRoomClick);

        // DISCONNECT 버튼 (오른쪽 끝, 15px 패딩)
        BuildThemedBtn(bar, "⬡  DISCONNECT", new Vector2(-105, 0), new Vector2(180, 44),
            new Vector2(1, 0.5f), new Vector2(1, 0.5f),
            UIThemedButton.Style.Ghost,
            OnLogoutClick);

        bar.gameObject.AddComponent<UIFadeIn>().Configure(0.1f, t.panelTransition, 0f);
    }

    // ── 4. 메인 패널 (룸 리스트) ──────────────────────────────────────────
    void BuildMainPanel(Transform root, UITheme t)
    {
        // 72 top + 68 bottom 제외한 영역
        var panel = MakeRect("MainPanel", root, Vector2.zero, Vector2.zero);
        panel.anchorMin = new Vector2(0, 0); panel.anchorMax = new Vector2(1, 1);
        panel.offsetMin = new Vector2(0, 68); panel.offsetMax = new Vector2(0, -72);

        // 섹션 헤더
        var sHdr = MakeRect("SectionHdr", panel, Vector2.zero, Vector2.zero);
        sHdr.anchorMin = new Vector2(0, 1); sHdr.anchorMax = new Vector2(1, 1);
        sHdr.pivot = new Vector2(0.5f, 1);
        sHdr.offsetMin = new Vector2(40, -56); sHdr.offsetMax = new Vector2(-40, 0);
        AddImg(sHdr, new Color(t.secondaryBG.r, t.secondaryBG.g, t.secondaryBG.b, 0.9f));
        AddOutline(sHdr.gameObject, new Color(t.border.r, t.border.g, t.border.b, 0.5f), 1f);

        // 섹션 왼쪽 강조 바
        var accent = MakeRect("Accent", sHdr, Vector2.zero, Vector2.zero);
        accent.anchorMin = new Vector2(0, 0); accent.anchorMax = new Vector2(0, 1);
        accent.offsetMin = Vector2.zero; accent.offsetMax = new Vector2(3, 0);
        AddImg(accent, t.accentCyan);

        AddTextFill(sHdr, "▸  COMBAT ZONE SELECTION",
            t.textM, t.accentCyan, FontStyle.Bold);

        // 테이블 헤더
        var tblHdr = MakeRect("TblHdr", panel, Vector2.zero, Vector2.zero);
        tblHdr.anchorMin = new Vector2(0, 1); tblHdr.anchorMax = new Vector2(1, 1);
        tblHdr.pivot = new Vector2(0.5f, 1);
        tblHdr.offsetMin = new Vector2(40, -92); tblHdr.offsetMax = new Vector2(-40, -56);
        AddImg(tblHdr, new Color(0, 0, 0, 0.35f));
        BuildTableHeader(tblHdr, t);

        // 구분선
        var div = MakeRect("Div", panel, Vector2.zero, Vector2.zero);
        div.anchorMin = new Vector2(0, 1); div.anchorMax = new Vector2(1, 1);
        div.pivot = new Vector2(0.5f, 1);
        div.offsetMin = new Vector2(40, -93); div.offsetMax = new Vector2(-40, -92);
        AddImg(div, new Color(t.border.r, t.border.g, t.border.b, 0.7f));

        // 스크롤 뷰 영역
        var scrollGO = new GameObject("ScrollView");
        scrollGO.transform.SetParent(panel, false);
        var scrollRT = scrollGO.AddComponent<RectTransform>();
        scrollRT.anchorMin = new Vector2(0, 0); scrollRT.anchorMax = new Vector2(1, 1);
        scrollRT.offsetMin = new Vector2(40, 0); scrollRT.offsetMax = new Vector2(-40, -93);
        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;

        var viewportGO = new GameObject("Viewport");
        viewportGO.transform.SetParent(scrollGO.transform, false);
        var vpRT = viewportGO.AddComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = vpRT.offsetMax = Vector2.zero;
        viewportGO.AddComponent<Image>().color = new Color(0, 0, 0, 0);
        viewportGO.AddComponent<Mask>().showMaskGraphic = false;

        var contentGO = new GameObject("Content");
        contentGO.transform.SetParent(viewportGO.transform, false);
        var contentRT = contentGO.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1); contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.offsetMin = contentRT.offsetMax = Vector2.zero;
        var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var csf = contentGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = vpRT;
        scroll.content  = contentRT;
        scroll.movementType = ScrollRect.MovementType.Elastic;

        roomListContent = contentGO.transform;

        panel.gameObject.AddComponent<UIFadeIn>().Configure(0.15f, t.panelTransition, 12f);
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
        Color hdrColor = new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, 0.55f);
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

        if (rooms == null || rooms.Count == 0)
        { SetStatus("전투 구역 없음.", false); return; }

        foreach (var room in rooms) BuildRoomRow(room);
        SetStatus($"{rooms.Count}개 구역 사용 가능  ·  구역을 선택하여 출격", false);
    }

    void BuildRoomRow(RoomInfo room)
    {
        var t = UITheme.Instance;
        bool active = room.playerCount > 0;
        bool isFull = room.playerCount >= room.maxPlayers;

        var row = new GameObject($"Row_{room.roomId}");
        row.transform.SetParent(roomListContent, false);
        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 68;
        row.AddComponent<RectTransform>();
        var rowImg = row.AddComponent<Image>();
        rowImg.color = new Color(t.secondaryBG.r, t.secondaryBG.g, t.secondaryBG.b, 0.85f);

        // 좌측 상태 바
        var bar = MakeRect("Bar", row.transform, Vector2.zero, Vector2.zero);
        bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(0, 1);
        bar.offsetMin = Vector2.zero; bar.offsetMax = new Vector2(4, 0);
        Color barColor = active ? t.success : t.textDisabled;
        AddImg(bar, barColor);

        // ZONE 번호
        var zGO = MakeRect("Zone", row.transform, Vector2.zero, Vector2.zero);
        zGO.anchorMin = new Vector2(0.00f, 0); zGO.anchorMax = new Vector2(0.10f, 1);
        zGO.offsetMin = zGO.offsetMax = Vector2.zero;
        var zTxt = AddTextFill(zGO, $"ZONE\n{room.roomId:D2}", t.textS, t.textPrimary, FontStyle.Bold);
        zTxt.alignment = TextAnchor.MiddleCenter; zTxt.lineSpacing = 0.85f;

        // 룸 이름
        var nameGO = MakeRect("Name", row.transform, new Vector2(16, 5), Vector2.zero);
        nameGO.anchorMin = new Vector2(0.10f, 0.4f); nameGO.anchorMax = new Vector2(0.45f, 1);
        nameGO.offsetMin = new Vector2(16, 0); nameGO.offsetMax = Vector2.zero;
        AddTextFill(nameGO, room.roomName.ToUpper(), t.textM, t.textPrimary, FontStyle.Bold)
            .alignment = TextAnchor.MiddleLeft;

        var coordGO = MakeRect("Coord", row.transform, Vector2.zero, Vector2.zero);
        coordGO.anchorMin = new Vector2(0.10f, 0); coordGO.anchorMax = new Vector2(0.45f, 0.4f);
        coordGO.offsetMin = new Vector2(16, 0); coordGO.offsetMax = Vector2.zero;
        AddTextFill(coordGO, $"GRID: {room.roomId * 17 + 41:D3}-{room.roomId * 31 + 77:D3}",
            t.textXS, t.textSecondary, FontStyle.Normal)
            .alignment = TextAnchor.MiddleLeft;

        // 상태
        var statGO = MakeRect("Stat", row.transform, Vector2.zero, Vector2.zero);
        statGO.anchorMin = new Vector2(0.45f, 0); statGO.anchorMax = new Vector2(0.62f, 1);
        statGO.offsetMin = new Vector2(16, 0); statGO.offsetMax = Vector2.zero;
        Color statColor = active ? t.success : t.textDisabled;
        AddTextFill(statGO, active ? "● ACTIVE" : "○ STANDBY", t.textS, statColor, FontStyle.Bold)
            .alignment = TextAnchor.MiddleLeft;

        // 파일럿 수
        var pilotGO = MakeRect("Pilots", row.transform, Vector2.zero, Vector2.zero);
        pilotGO.anchorMin = new Vector2(0.62f, 0); pilotGO.anchorMax = new Vector2(0.78f, 1);
        pilotGO.offsetMin = new Vector2(16, 0); pilotGO.offsetMax = Vector2.zero;
        Color pilotColor = isFull ? t.warning : t.textPrimary;
        AddTextFill(pilotGO, $"{room.playerCount} / {room.maxPlayers}", t.textM, pilotColor, FontStyle.Bold)
            .alignment = TextAnchor.MiddleLeft;

        // JOIN 버튼
        var btnArea = MakeRect("BtnArea", row.transform, Vector2.zero, Vector2.zero);
        btnArea.anchorMin = new Vector2(0.78f, 0.15f); btnArea.anchorMax = new Vector2(1.00f, 0.85f);
        btnArea.offsetMin = new Vector2(16, 0); btnArea.offsetMax = new Vector2(-16, 0);

        if (!isFull)
        {
            var btnGO = new GameObject("JoinBtn");
            btnGO.transform.SetParent(btnArea, false);
            var brt = btnGO.AddComponent<RectTransform>();
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            btnGO.AddComponent<Image>();
            btnGO.AddComponent<Outline>().effectDistance = new Vector2(1, 1);
            var lbl = new GameObject("L");
            lbl.transform.SetParent(btnGO.transform, false);
            var lrt = lbl.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            lbl.AddComponent<Text>().text = "ENTER  ►";
            lbl.GetComponent<Text>().font = uiFont;
            lbl.GetComponent<Text>().fontSize = t.textS;
            lbl.GetComponent<Text>().fontStyle = FontStyle.Bold;
            lbl.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;
            lbl.GetComponent<Text>().color = t.textPrimary;
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
            AddTextFill(fullGO, "FULL", t.textS, t.textDisabled, FontStyle.Bold)
                .alignment = TextAnchor.MiddleCenter;
        }

        // 행 하단 구분선
        var rowDiv = MakeRect("RowDiv", row.transform, Vector2.zero, Vector2.zero);
        rowDiv.anchorMin = new Vector2(0, 0); rowDiv.anchorMax = new Vector2(1, 0);
        rowDiv.offsetMin = Vector2.zero; rowDiv.offsetMax = new Vector2(0, 1);
        AddImg(rowDiv, new Color(t.border.r, t.border.g, t.border.b, 0.4f));

        // 호버
        var trigger = row.AddComponent<EventTrigger>();
        Color baseColor = rowImg.color;
        Color hoverColor = new Color(baseColor.r + 0.02f, baseColor.g + 0.04f, baseColor.b + 0.06f, baseColor.a);
        AddEvt(trigger, EventTriggerType.PointerEnter, _ => rowImg.color = hoverColor);
        AddEvt(trigger, EventTriggerType.PointerExit,  _ => rowImg.color = baseColor);
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
        overlay.AddComponent<Image>().color = new Color(0, 0, 0, 0.72f);
        _createModal = overlay;

        var panel = MakeRect("Panel", overlay.transform, Vector2.zero, new Vector2(540, 300));
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        AddImg(panel, new Color(t.panelBG.r, t.panelBG.g, t.panelBG.b, 0.97f));
        AddOutline(panel.gameObject, t.border, 1.5f);

        // 상단 cyan 바
        var accent = MakeRect("Accent", panel, Vector2.zero, Vector2.zero);
        accent.anchorMin = new Vector2(0, 1); accent.anchorMax = new Vector2(1, 1);
        accent.offsetMin = new Vector2(0, -3); accent.offsetMax = Vector2.zero;
        AddImg(accent, t.accentCyan);

        // 좌측 cyan 바
        var lbar = MakeRect("LBar", panel, Vector2.zero, Vector2.zero);
        lbar.anchorMin = new Vector2(0, 0); lbar.anchorMax = new Vector2(0, 1);
        lbar.offsetMin = Vector2.zero; lbar.offsetMax = new Vector2(3, 0);
        AddImg(lbar, new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, 0.6f));

        // 타이틀
        var titleGO = MakeRect("Title", panel, new Vector2(0, -32), new Vector2(500, 36));
        titleGO.anchorMin = titleGO.anchorMax = new Vector2(0.5f, 1);
        AddTextFill(titleGO, "CREATE COMBAT ZONE", t.titleM, t.accentCyan, FontStyle.Bold);

        // 레이블
        var lblGO = MakeRect("Lbl", panel, new Vector2(-180, 12), new Vector2(220, 20));
        lblGO.anchorMin = lblGO.anchorMax = new Vector2(0.5f, 0.5f);
        lblGO.pivot = new Vector2(0, 0.5f);
        AddTextFill(lblGO, "ZONE DESIGNATION", t.textXS, t.textSecondary, FontStyle.Bold);

        // InputField
        var inputGO = new GameObject("RoomNameInput");
        inputGO.transform.SetParent(panel, false);
        var inputRT = inputGO.AddComponent<RectTransform>();
        inputRT.anchorMin = inputRT.anchorMax = new Vector2(0.5f, 0.5f);
        inputRT.anchoredPosition = new Vector2(0, -22);
        inputRT.sizeDelta        = new Vector2(460, 46);
        AddImg(inputRT, new Color(0f, 0f, 0f, 0.5f));
        AddOutline(inputGO, t.border, 1f);

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

        // 버튼 행
        BuildModalBtn(panel, "DEPLOY  ►", new Vector2(-120, -104), new Vector2(190, 46),
            UIThemedButton.Style.Primary, () =>
            {
                string name = _roomNameInput?.text?.Trim() ?? "";
                if (string.IsNullOrEmpty(name))
                    name = $"{GameManager.Instance?.myNickname ?? "Unknown"}'s Zone";
                SetStatus("전투 구역 생성 중...", false);
                NetworkManager.Instance?.socketClient.SendCreateRoom(name, 8);
                CloseCreateModal();
            });

        BuildModalBtn(panel, "CANCEL", new Vector2(100, -104), new Vector2(150, 46),
            UIThemedButton.Style.Ghost, CloseCreateModal);

        overlay.AddComponent<UIFadeIn>().Configure(0f, t.panelTransition, 0f);
    }

    void BuildModalBtn(RectTransform parent, string label, Vector2 pos, Vector2 size,
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
            StartCoroutine(LoadGame());
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
        StartCoroutine(LoadGame());
    }

    IEnumerator LoadGame()
    {
        yield return new WaitForSeconds(0.3f);
        SceneManager.LoadScene("GameScene");
    }

    void OnLogoutClick()
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

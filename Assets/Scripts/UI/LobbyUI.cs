using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    // ── 색상 팔레트 ─────────────────────────────────────────────────────────
    static readonly Color C_BG       = new Color(0.031f, 0.043f, 0.063f);
    static readonly Color C_PANEL    = new Color(0.047f, 0.063f, 0.094f);
    static readonly Color C_PANEL2   = new Color(0.055f, 0.075f, 0.110f);
    static readonly Color C_ROW      = new Color(0.059f, 0.082f, 0.122f);
    static readonly Color C_ACCENT   = new Color(0.00f,  0.78f,  0.33f);
    static readonly Color C_ACCENT2  = new Color(0.00f,  0.36f,  0.15f);
    static readonly Color C_TEXT     = new Color(0.91f,  0.93f,  0.94f);
    static readonly Color C_DIM      = new Color(0.36f,  0.47f,  0.56f);
    static readonly Color C_WARN     = new Color(1.00f,  0.65f,  0.20f);
    static readonly Color C_DANGER   = new Color(0.80f,  0.22f,  0.18f);
    static readonly Color C_BORDER   = new Color(0.12f,  0.22f,  0.32f);
    static readonly Color C_ACTIVE   = new Color(0.20f,  0.90f,  0.35f);
    static readonly Color C_STANDBY  = new Color(0.55f,  0.60f,  0.65f);

    Font uiFont;
    Transform roomListContent;
    Text statusText;
    Text pilotLabel;
    Text onlineLabel;
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
        SocketClient sc = NetworkManager.Instance?.socketClient;
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
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    void SubscribeEvents()
    {
        SocketClient sc = NetworkManager.Instance?.socketClient;
        if (sc == null) return;
        sc.OnRoomList          += HandleRoomList;
        sc.OnCreateRoomResult  += HandleCreateRoomResult;
    }

    IEnumerator RequestRoomList()
    {
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsConnected())
        {
            SetStatus("Not connected — returning to login", true);
            yield return new WaitForSeconds(1.5f);
            SceneManager.LoadScene("LoginScene");
            yield break;
        }
        SetStatus("Requesting combat zone data...", false);
        NetworkManager.Instance.socketClient.RequestRoomList();
    }

    // ── UI 빌드 ──────────────────────────────────────────────────────────────
    void BuildUI()
    {
        // Canvas
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

        // ── 전체 배경 ─────────────────────────────────────────────────────────
        Fill(root, "BG", C_BG);

        // 배경 그리드 라인 (장식)
        for (int i = 0; i < 7; i++)
        {
            float y = -480f + i * 160f;
            var hLine = NewRect("GridH" + i, root, new Vector2(0, y), new Vector2(0, 1));
            SetAnchors(hLine, new Vector2(0, 0.5f), new Vector2(1, 0.5f));
            AddImg(hLine, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.035f));
        }

        // ── 상단 헤더 바 (전체 폭) ────────────────────────────────────────────
        var topBar = NewRect("TopBar", root, Vector2.zero, new Vector2(0, 72));
        SetAnchors(topBar, new Vector2(0, 1), new Vector2(1, 1));
        topBar.offsetMin = new Vector2(0, -72);
        topBar.offsetMax = Vector2.zero;
        AddImg(topBar, C_PANEL);

        // 상단 강조선
        var topAccent = NewRect("TopAccent", topBar, new Vector2(0, 0), new Vector2(0, 3));
        SetAnchors(topAccent, new Vector2(0, 1), new Vector2(1, 1));
        AddImg(topAccent, C_ACCENT);

        // 파일럿 정보
        pilotLabel = MakeText(topBar, "PILOT  —",
            new Vector2(208, 0), new Vector2(360, 0),
            14, FontStyle.Bold, C_ACCENT, TextAnchor.MiddleLeft,
            new Vector2(0, 0), new Vector2(0, 1));

        // 타이틀
        MakeText(topBar, "F · 22   TACTICAL  OPERATIONS  CENTER",
            new Vector2(0, 0), new Vector2(700, 0),
            18, FontStyle.Bold, C_TEXT, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0), new Vector2(0.5f, 1));

        // 연결 상태
        onlineLabel = MakeText(topBar, "◉  SECURE LINK ACTIVE",
            new Vector2(-178, 0), new Vector2(300, 0),
            12, FontStyle.Bold, C_ACTIVE, TextAnchor.MiddleRight,
            new Vector2(1, 0), new Vector2(1, 1));

        // ── 하단 액션 바 (전체 폭) ────────────────────────────────────────────
        var botBar = NewRect("BotBar", root, Vector2.zero, new Vector2(0, 68));
        SetAnchors(botBar, new Vector2(0, 0), new Vector2(1, 0));
        botBar.offsetMin = Vector2.zero;
        botBar.offsetMax = new Vector2(0, 68);
        AddImg(botBar, C_PANEL);

        // 하단 강조선
        var botAccent = NewRect("BotAccent", botBar, new Vector2(0, 0), new Vector2(0, 2));
        SetAnchors(botAccent, new Vector2(0, 1), new Vector2(1, 1));
        AddImg(botAccent, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.4f));

        // REFRESH 버튼
        var refreshBtn = MakeButton(botBar, "↻   REFRESH",
            new Vector2(28, 0), new Vector2(160, 44), C_ACCENT2,
            new Vector2(0, 0.5f), new Vector2(0, 0.5f));
        var rcb = refreshBtn.colors;
        rcb.normalColor = C_ACCENT2; rcb.highlightedColor = C_ACCENT;
        rcb.pressedColor = new Color(C_ACCENT.r*0.5f, C_ACCENT.g*0.5f, C_ACCENT.b*0.5f);
        refreshBtn.colors = rcb;
        SetBtnTextColor(refreshBtn, C_TEXT);
        refreshBtn.onClick.AddListener(() =>
        {
            SetStatus("Refreshing zone data...", false);
            NetworkManager.Instance?.socketClient.RequestRoomList();
        });

        // 상태 텍스트 (하단 중앙)
        statusText = MakeText(botBar, "Loading...",
            new Vector2(0, 0), new Vector2(600, 0),
            12, FontStyle.Normal, C_DIM, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0), new Vector2(0.5f, 1));

        // CREATE ROOM 버튼
        var createBtn = MakeButton(botBar, "+   CREATE ROOM",
            new Vector2(-240, 0), new Vector2(190, 44), new Color(0.10f, 0.28f, 0.55f),
            new Vector2(1, 0.5f), new Vector2(1, 0.5f));
        SetBtnTextColor(createBtn, C_TEXT);
        var cbc = createBtn.colors;
        cbc.highlightedColor = new Color(0.20f, 0.45f, 0.80f);
        cbc.pressedColor     = new Color(0.06f, 0.16f, 0.35f);
        createBtn.colors = cbc;
        createBtn.onClick.AddListener(OnCreateRoomClick);

        // DISCONNECT 버튼
        var logoutBtn = MakeButton(botBar, "⬡   DISCONNECT",
            new Vector2(-28, 0), new Vector2(190, 44), C_DANGER,
            new Vector2(1, 0.5f), new Vector2(1, 0.5f));
        SetBtnTextColor(logoutBtn, C_TEXT);
        logoutBtn.onClick.AddListener(OnLogoutClick);

        // ── 중앙 컨텐츠 패널 ──────────────────────────────────────────────────
        // 중앙 고정 크기 패널로 왼쪽 정렬 문제 해결
        var mainPanel = NewRect("MainPanel", root, new Vector2(0, -2), new Vector2(1440, 880));
        mainPanel.anchorMin = mainPanel.anchorMax = new Vector2(0.5f, 0.5f);
        mainPanel.pivot = new Vector2(0.5f, 0.5f);
        AddImg(mainPanel, new Color(0, 0, 0, 0));

        // 섹션 헤더
        var sectionHeader = NewRect("SectionHeader", mainPanel, new Vector2(0, 398), new Vector2(1440, 52));
        sectionHeader.anchorMin = sectionHeader.anchorMax = new Vector2(0.5f, 0.5f);
        var sHdrImg = sectionHeader.gameObject.AddComponent<Image>();
        sHdrImg.color = C_PANEL2;

        // 섹션 왼쪽 강조 바
        var sHdrAccent = NewRect("SHdrAccent", sectionHeader, new Vector2(0, 0), new Vector2(4, 0));
        SetAnchors(sHdrAccent, new Vector2(0, 0), new Vector2(0, 1));
        AddImg(sHdrAccent, C_ACCENT);

        MakeText(sectionHeader, "▸  COMBAT ZONE SELECTION",
            new Vector2(320, 0), new Vector2(600, 0),
            16, FontStyle.Bold, C_ACCENT, TextAnchor.MiddleLeft,
            new Vector2(0, 0), new Vector2(0, 1));

        var timeText = MakeText(sectionHeader, "UTC 00:00:00",
            new Vector2(-120, 0), new Vector2(200, 0),
            11, FontStyle.Normal, new Color(C_DIM.r, C_DIM.g, C_DIM.b, 0.7f),
            TextAnchor.MiddleRight, new Vector2(1, 0), new Vector2(1, 1));
        StartCoroutine(UpdateClock(timeText));

        // 테이블 헤더 행
        var tblHeader = NewRect("TblHeader", mainPanel, new Vector2(0, 358), new Vector2(1440, 36));
        tblHeader.anchorMin = tblHeader.anchorMax = new Vector2(0.5f, 0.5f);
        AddImg(tblHeader, new Color(0, 0, 0, 0.4f));
        BuildTableHeader(tblHeader);

        // 구분선
        var divider = NewRect("Divider", mainPanel, new Vector2(0, 339), new Vector2(1400, 1));
        divider.anchorMin = divider.anchorMax = new Vector2(0.5f, 0.5f);
        AddImg(divider, new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.8f));

        // 룸 리스트 컨테이너 (수직 배치)
        var listGO = new GameObject("RoomList");
        listGO.transform.SetParent(mainPanel, false);
        var listRT = listGO.AddComponent<RectTransform>();
        listRT.anchorMin = new Vector2(0, 0.5f);
        listRT.anchorMax = new Vector2(1, 0.5f);
        listRT.pivot     = new Vector2(0.5f, 1);
        listRT.anchoredPosition = new Vector2(0, 330);
        listRT.sizeDelta = new Vector2(0, 600);
        var vlg = listGO.AddComponent<VerticalLayoutGroup>();
        vlg.spacing              = 8;
        vlg.childAlignment       = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        var csf = listGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        roomListContent = listGO.transform;

        // 파일럿 정보 갱신
        if (GameManager.Instance != null && !string.IsNullOrEmpty(GameManager.Instance.myNickname))
            pilotLabel.text = $"PILOT  {GameManager.Instance.myNickname.ToUpper()}";
    }

    void BuildTableHeader(RectTransform parent)
    {
        (string label, float xStart, float xEnd)[] cols = {
            ("ZONE",        0.00f, 0.10f),
            ("DESIGNATION", 0.10f, 0.45f),
            ("STATUS",      0.45f, 0.65f),
            ("PILOTS",      0.65f, 0.80f),
            ("",            0.80f, 1.00f),
        };

        foreach (var (label, xs, xe) in cols)
        {
            if (string.IsNullOrEmpty(label)) continue;
            var go = new GameObject($"H_{label}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(xs, 0); rt.anchorMax = new Vector2(xe, 1);
            rt.offsetMin = new Vector2(16, 0); rt.offsetMax = Vector2.zero;
            var txt = go.AddComponent<Text>();
            txt.text = label; txt.font = uiFont; txt.fontSize = 10;
            txt.fontStyle = FontStyle.Bold;
            txt.color = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.6f);
            txt.alignment = TextAnchor.MiddleLeft;
        }
    }

    // ── 룸 목록 갱신 ──────────────────────────────────────────────────────────
    void HandleRoomList(List<RoomInfo> rooms)
    {
        foreach (Transform child in roomListContent)
            Destroy(child.gameObject);

        if (rooms == null || rooms.Count == 0)
        { SetStatus("No combat zones available.", false); return; }

        foreach (var room in rooms) BuildRoomRow(room);
        SetStatus($"{rooms.Count} zone(s) available  ·  select a zone to deploy", false);
    }

    void BuildRoomRow(RoomInfo room)
    {
        var row = new GameObject($"Row_{room.roomId}");
        row.transform.SetParent(roomListContent, false);
        var rowRT = row.AddComponent<LayoutElement>();
        rowRT.preferredHeight = 72;

        var rowRT2 = row.AddComponent<RectTransform>();
        var rowImg = row.AddComponent<Image>();
        rowImg.color = C_ROW;

        // 왼쪽 상태 색상 바
        bool active = room.playerCount > 0;
        var bar = NewRect("Bar", row.transform, Vector2.zero, new Vector2(5, 0));
        SetAnchors(bar, new Vector2(0, 0), new Vector2(0, 1));
        AddImg(bar, active ? C_ACTIVE : C_STANDBY);

        // ZONE 번호
        var zTxt = MakeText(row.transform, $"ZONE\n{room.roomId:D2}",
            Vector2.zero, Vector2.zero, 16, FontStyle.Bold, C_TEXT, TextAnchor.MiddleCenter,
            new Vector2(0.00f, 0), new Vector2(0.10f, 1));
        zTxt.lineSpacing = 0.9f;

        // 룸 이름
        MakeText(row.transform, room.roomName.ToUpper(),
            new Vector2(16, 4), Vector2.zero, 15, FontStyle.Bold, C_TEXT, TextAnchor.UpperLeft,
            new Vector2(0.10f, 0), new Vector2(0.45f, 1));

        string coordStr = $"GRID REF: {(room.roomId * 17 + 41):D3}-{(room.roomId * 31 + 77):D3}";
        MakeText(row.transform, coordStr,
            new Vector2(16, -6), Vector2.zero, 10, FontStyle.Normal,
            new Color(C_DIM.r, C_DIM.g, C_DIM.b, 0.7f), TextAnchor.LowerLeft,
            new Vector2(0.10f, 0), new Vector2(0.45f, 1));

        // 상태 표시
        Color statusColor = active ? C_ACTIVE : C_STANDBY;
        string statusStr  = active ? "● ACTIVE" : "○ STANDBY";
        MakeText(row.transform, statusStr,
            new Vector2(16, 0), Vector2.zero, 13, FontStyle.Bold, statusColor,
            TextAnchor.MiddleLeft, new Vector2(0.45f, 0), new Vector2(0.65f, 1));

        // 파일럿 수
        bool isFull = room.playerCount >= room.maxPlayers;
        Color pilotColor = isFull ? C_WARN : C_TEXT;
        MakeText(row.transform, $"{room.playerCount} / {room.maxPlayers}  PILOTS",
            new Vector2(16, 0), Vector2.zero, 13, FontStyle.Bold, pilotColor,
            TextAnchor.MiddleLeft, new Vector2(0.65f, 0), new Vector2(0.80f, 1));

        // JOIN 버튼
        int rid = room.roomId;
        var btnArea = NewRect("BtnArea", row.transform, Vector2.zero, new Vector2(-40, 0));
        SetAnchors(btnArea, new Vector2(0.80f, 0.15f), new Vector2(1.00f, 0.85f));
        var joinBtn = btnArea.gameObject.AddComponent<Button>();
        var btnImg  = btnArea.gameObject.AddComponent<Image>();
        Color btnColor = isFull ? C_STANDBY * 0.5f : C_ACCENT;
        btnImg.color = btnColor;
        joinBtn.targetGraphic = btnImg;
        var cb = joinBtn.colors;
        cb.normalColor      = btnColor;
        cb.highlightedColor = isFull ? btnColor : new Color(0.2f, 1f, 0.5f);
        cb.pressedColor     = btnColor * 0.6f;
        cb.colorMultiplier  = 1f;
        joinBtn.colors = cb;
        joinBtn.interactable = !isFull;

        var btnTxtGO = new GameObject("BtnTxt");
        btnTxtGO.transform.SetParent(btnArea, false);
        var btnTxt = btnTxtGO.AddComponent<Text>();
        btnTxt.text = isFull ? "FULL" : "ENTER  ►";
        btnTxt.font = uiFont; btnTxt.fontSize = 14;
        btnTxt.fontStyle = FontStyle.Bold;
        btnTxt.color = isFull ? C_DIM : new Color(0.02f, 0.06f, 0.02f);
        btnTxt.alignment = TextAnchor.MiddleCenter;
        var bRT = btnTxt.rectTransform;
        bRT.anchorMin = Vector2.zero; bRT.anchorMax = Vector2.one;
        bRT.offsetMin = bRT.offsetMax = Vector2.zero;
        joinBtn.onClick.AddListener(() => OnJoinRoom(rid));

        // 행 구분선
        var rowDiv = NewRect("RowDiv", row.transform, new Vector2(0, 0), new Vector2(0, 1));
        SetAnchors(rowDiv, new Vector2(0, 0), new Vector2(1, 0));
        AddImg(rowDiv, new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.5f));

        // 호버 효과
        var trigger = row.AddComponent<EventTrigger>();
        AddEvt(trigger, EventTriggerType.PointerEnter,
            _ => rowImg.color = new Color(C_ROW.r + 0.02f, C_ROW.g + 0.03f, C_ROW.b + 0.04f));
        AddEvt(trigger, EventTriggerType.PointerExit,
            _ => rowImg.color = C_ROW);
    }

    // ── CREATE ROOM 모달 ──────────────────────────────────────────────────────
    void OnCreateRoomClick()
    {
        if (_createModal != null) return;

        // 반투명 오버레이 (전체 화면)
        var canvasGO = GameObject.Find("LobbyCanvas");
        var overlay  = new GameObject("CreateRoomModal");
        overlay.transform.SetParent(canvasGO?.transform ?? transform, false);
        var ort = overlay.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;
        var obg = overlay.AddComponent<Image>();
        obg.color         = new Color(0, 0, 0, 0.70f);
        obg.raycastTarget = true;
        _createModal = overlay;

        // 중앙 패널
        var panel = NewRect("Panel", overlay.transform, Vector2.zero, new Vector2(520, 280));
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        var pImg = panel.gameObject.AddComponent<Image>();
        pImg.color = new Color(0.047f, 0.063f, 0.094f);

        // 상단 강조선
        var accent = NewRect("Accent", panel, new Vector2(0, 0), new Vector2(0, 3));
        SetAnchors(accent, new Vector2(0, 1), new Vector2(1, 1));
        AddImg(accent, C_ACCENT);

        // 타이틀
        MakeText(panel, "CREATE COMBAT ZONE",
            new Vector2(0, -24), new Vector2(500, 40),
            18, FontStyle.Bold, C_ACCENT, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 1), new Vector2(0.5f, 1));

        // 룸 이름 레이블
        MakeText(panel, "ZONE DESIGNATION",
            new Vector2(-180, 10), new Vector2(200, 28),
            11, FontStyle.Bold, C_DIM, TextAnchor.MiddleLeft,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));

        // 룸 이름 InputField
        var inputGO = new GameObject("RoomNameInput");
        inputGO.transform.SetParent(panel, false);
        var inputRT = inputGO.AddComponent<RectTransform>();
        inputRT.anchorMin = inputRT.anchorMax = new Vector2(0.5f, 0.5f);
        inputRT.anchoredPosition = new Vector2(0, -25);
        inputRT.sizeDelta        = new Vector2(440, 44);

        var inputBg = inputGO.AddComponent<Image>();
        inputBg.color = new Color(0.08f, 0.12f, 0.18f);

        // InputField 텍스트
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(inputGO.transform, false);
        var textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(10, 4); textRT.offsetMax = new Vector2(-10, -4);
        var textComp = textGO.AddComponent<Text>();
        textComp.font      = uiFont;
        textComp.fontSize  = 15;
        textComp.color     = C_TEXT;
        textComp.alignment = TextAnchor.MiddleLeft;

        // Placeholder
        var phGO = new GameObject("Placeholder");
        phGO.transform.SetParent(inputGO.transform, false);
        var phRT = phGO.AddComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
        phRT.offsetMin = new Vector2(10, 4); phRT.offsetMax = new Vector2(-10, -4);
        var phTxt = phGO.AddComponent<Text>();
        phTxt.text      = "Enter zone name...";
        phTxt.font      = uiFont;
        phTxt.fontSize  = 14;
        phTxt.color     = C_DIM;
        phTxt.fontStyle = FontStyle.Italic;
        phTxt.alignment = TextAnchor.MiddleLeft;

        _roomNameInput            = inputGO.AddComponent<InputField>();
        _roomNameInput.textComponent  = textComp;
        _roomNameInput.placeholder    = phTxt;
        _roomNameInput.characterLimit = 24;
        _roomNameInput.text           = "";

        // CONFIRM 버튼
        var confirmBtn = MakeButton(panel, "DEPLOY  ►",
            new Vector2(-120, -100), new Vector2(180, 44), C_ACCENT,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        SetBtnTextColor(confirmBtn, new Color(0.02f, 0.06f, 0.02f));
        confirmBtn.onClick.AddListener(() =>
        {
            string name = _roomNameInput?.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) name = $"{GameManager.Instance?.myNickname ?? "Unknown"}'s Zone";
            SetStatus("Creating combat zone...", false);
            NetworkManager.Instance?.socketClient.SendCreateRoom(name, 8);
            CloseCreateModal();
        });

        // CANCEL 버튼
        var cancelBtn = MakeButton(panel, "CANCEL",
            new Vector2(100, -100), new Vector2(140, 44), new Color(0.20f, 0.22f, 0.24f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        SetBtnTextColor(cancelBtn, C_DIM);
        cancelBtn.onClick.AddListener(CloseCreateModal);
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
            SetStatus($"Zone created: {p.roomName}  —  Deploying...", false);
            StartCoroutine(LoadGame());
        }
        else
        {
            SetStatus($"Failed to create zone: {p.errorMessage}", true);
        }
    }

    // ── 액션 ──────────────────────────────────────────────────────────────────
    void OnJoinRoom(int roomId)
    {
        if (GameManager.Instance != null) GameManager.Instance.currentRoomId = roomId;
        SetStatus($"Deploying to Zone {roomId:D2}...", false);
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
        SetStatus("Disconnecting...", false);
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
        statusText.text  = msg;
        statusText.color = warn ? C_WARN : C_DIM;
    }

    IEnumerator UpdateClock(Text t)
    {
        while (t != null)
        {
            System.DateTime utc = System.DateTime.UtcNow;
            t.text = $"UTC {utc:HH:mm:ss}";
            yield return new WaitForSeconds(1f);
        }
    }

    // ── UI 헬퍼 ───────────────────────────────────────────────────────────────
    void Fill(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }

    RectTransform NewRect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        return rt;
    }

    void SetAnchors(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    Image AddImg(RectTransform rt, Color color)
        => AddImg(rt.gameObject, color);

    Image AddImg(Transform t, Color color)
        => AddImg(t.gameObject, color);

    Image AddImg(GameObject go, Color color)
    {
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    Text MakeText(Transform parent, string content, Vector2 pos, Vector2 size,
                  int fs, FontStyle style, Color color, TextAnchor anchor,
                  Vector2 ancMin, Vector2 ancMax)
    {
        var go = new GameObject("T_" + content[..Mathf.Min(content.Length, 10)]);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin = ancMin; rt.anchorMax = ancMax;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var txt = go.AddComponent<Text>();
        txt.text = content; txt.font = uiFont; txt.fontSize = fs;
        txt.fontStyle = style; txt.color = color; txt.alignment = anchor;
        txt.raycastTarget = false;
        return txt;
    }

    Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, Color color,
                      Vector2 ancMin, Vector2 ancMax)
    {
        var go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin = ancMin; rt.anchorMax = ancMax;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.normalColor      = color;
        cb.highlightedColor = new Color(color.r * 1.3f, color.g * 1.4f, color.b * 1.3f);
        cb.pressedColor     = new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f);
        cb.colorMultiplier  = 1f;
        btn.colors = cb;
        var txtGO = new GameObject("L");
        txtGO.transform.SetParent(go.transform, false);
        var txt = txtGO.AddComponent<Text>();
        txt.text = label; txt.font = uiFont; txt.fontSize = 13;
        txt.fontStyle = FontStyle.Bold;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = C_TEXT;
        var tRT = txt.rectTransform;
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = tRT.offsetMax = Vector2.zero;
        return btn;
    }

    void SetBtnTextColor(Button btn, Color color)
    {
        var txt = btn.GetComponentInChildren<Text>();
        if (txt != null) txt.color = color;
    }

    static void AddEvt(EventTrigger et, EventTriggerType type,
                       UnityEngine.Events.UnityAction<BaseEventData> cb)
    {
        var e = new EventTrigger.Entry { eventID = type };
        e.callback.AddListener(cb);
        et.triggers.Add(e);
    }
}

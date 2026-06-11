using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 로그인 씬에 빈 GameObject 하나 만들고 이 컴포넌트를 붙이면 됩니다.
/// GameManager / NetworkManager / UnityMainThreadDispatcher 가 없으면 자동 생성합니다.
/// </summary>
public class LoginUI : MonoBehaviour
{
    // ── 색상 팔레트 (군용 비행 시뮬레이션 스타일) ─────────────────────────
    static readonly Color C_BG       = new Color(0.039f, 0.051f, 0.075f);   // #0A0D13
    static readonly Color C_PANEL    = new Color(0.059f, 0.082f, 0.122f);   // #0F1520
    static readonly Color C_ACCENT   = new Color(0.00f,  0.78f,  0.33f);    // #00C755
    static readonly Color C_ACCENT2  = new Color(0.00f,  0.45f,  0.20f);    // #007333
    static readonly Color C_TEXT     = new Color(0.91f,  0.93f,  0.94f);    // #E8ECEF
    static readonly Color C_DIM      = new Color(0.36f,  0.47f,  0.56f);    // #5C788F
    static readonly Color C_WARN     = new Color(1.00f,  0.32f,  0.26f);    // #FF5242
    static readonly Color C_INPUT_BG = new Color(0.024f, 0.035f, 0.055f);   // #06090E
    static readonly Color C_BORDER   = new Color(0.10f,  0.20f,  0.30f);    // #1A3348

    Font uiFont;

    // UI 레퍼런스
    InputField ipField;
    InputField idField;
    InputField pwField;
    Text       statusText;
    Button     loginBtn;

    // ── 초기화 ───────────────────────────────────────────────────────────
    void Awake()
    {
        EnsureManagers();
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    void Start()
    {
        BuildUI();
        SubscribeEvents();
    }

    void OnDestroy()
    {
        SocketClient sc = NetworkManager.Instance?.socketClient;
        if (sc != null) sc.OnLoginResult -= HandleLoginResult;
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
        if (sc != null) sc.OnLoginResult += HandleLoginResult;
    }

    // ── UI 빌드 ──────────────────────────────────────────────────────────
    void BuildUI()
    {
        // ── Canvas ───────────────────────────────────────────────────────
        var canvasGO = new GameObject("LoginCanvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // ── 전체화면 배경 ─────────────────────────────────────────────────
        var bg = MakeImage(canvasGO.transform, "BG", Vector2.zero, Vector2.zero, C_BG);
        bg.anchorMin = Vector2.zero;
        bg.anchorMax = Vector2.one;
        bg.offsetMin = bg.offsetMax = Vector2.zero;

        // ── 장식용 가로줄 ──────────────────────────────────────────────────
        for (int i = 0; i < 8; i++)
        {
            float y = -540f + i * 155f;
            var line = MakeImage(canvasGO.transform, $"GridH{i}", new Vector2(0, y), new Vector2(0, 1),
                new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.04f));
            line.anchorMin = new Vector2(0, 0.5f);
            line.anchorMax = new Vector2(1, 0.5f);
            line.anchoredPosition = new Vector2(0, y);
            line.sizeDelta        = new Vector2(0, 1);
        }

        // ── 로그인 패널 ───────────────────────────────────────────────────
        var panel = MakeImage(canvasGO.transform, "Panel",
            new Vector2(0, 10), new Vector2(440, 580), C_PANEL);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);

        // 패널 상단 초록선
        var topBar = MakeImage(panel.transform, "TopBar",
            new Vector2(0, 290), new Vector2(440, 3), C_ACCENT);
        topBar.anchorMin = topBar.anchorMax = new Vector2(0.5f, 0.5f);

        // 패널 왼쪽 수직 장식선
        var leftBar = MakeImage(panel.transform, "LeftBar",
            new Vector2(-218, 0), new Vector2(3, 580), C_ACCENT2);
        leftBar.anchorMin = leftBar.anchorMax = new Vector2(0.5f, 0.5f);

        // ── 타이틀 섹션 ───────────────────────────────────────────────────
        MakeText(panel.transform, "F·22  TACTICAL  SIMULATOR",
            new Vector2(10, 218), new Vector2(400, 40),
            26, FontStyle.Bold, C_ACCENT, TextAnchor.MiddleCenter);

        MakeText(panel.transform, "ONLINE OPERATIONS CENTER",
            new Vector2(10, 183), new Vector2(400, 28),
            13, FontStyle.Normal, C_DIM, TextAnchor.MiddleCenter);

        MakeText(panel.transform, "▌ TOP SECRET  //  LEVEL 5  //  SECURE CHANNEL ▐",
            new Vector2(10, 152), new Vector2(400, 22),
            10, FontStyle.Normal,
            new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.55f),
            TextAnchor.MiddleCenter);

        // 구분선
        MakeImage(panel.transform, "Div1",
            new Vector2(0, 128), new Vector2(380, 1),
            new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.8f))
            .anchorMin = new Vector2(0.5f, 0.5f);

        // ── 폼 레이블 + 입력 ──────────────────────────────────────────────
        MakeFieldLabel(panel.transform, "SERVER ADDRESS", new Vector2(0, 100));
        ipField = MakeInputField(panel.transform, "172.30.201.117",
            new Vector2(0, 72), new Vector2(380, 38));
        if (GameManager.Instance != null)
            ipField.text = GameManager.Instance.serverIP;

        MakeFieldLabel(panel.transform, "CALL SIGN (ID)", new Vector2(0, 36));
        idField = MakeInputField(panel.transform, "Enter pilot ID",
            new Vector2(0, 8), new Vector2(380, 38));

        MakeFieldLabel(panel.transform, "ACCESS CODE", new Vector2(0, -28));
        pwField = MakeInputField(panel.transform, "Enter access code",
            new Vector2(0, -56), new Vector2(380, 38), isPassword: true);

        // 패스워드 필드 Enter → 로그인
        pwField.onSubmit.AddListener(_ => OnLoginClick());
        idField.onSubmit.AddListener(_ => EventSystem.current?.SetSelectedGameObject(pwField.gameObject));

        // ── 로그인 버튼 ───────────────────────────────────────────────────
        loginBtn = MakeButton(panel.transform, "▶  AUTHENTICATE",
            new Vector2(0, -115), new Vector2(380, 44), C_ACCENT);
        loginBtn.onClick.AddListener(OnLoginClick);

        // ── 상태 메시지 ───────────────────────────────────────────────────
        statusText = MakeText(panel.transform, "Ready to connect...",
            new Vector2(0, -162), new Vector2(380, 28),
            12, FontStyle.Normal, C_DIM, TextAnchor.MiddleCenter);

        // 하단 구분선 + 빌드 정보
        MakeImage(panel.transform, "Div2",
            new Vector2(0, -202), new Vector2(380, 1),
            new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.5f))
            .anchorMin = new Vector2(0.5f, 0.5f);

        MakeText(panel.transform, "BUILD 2025.1  |  ENCRYPTED  |  © TACTICAL SYSTEMS",
            new Vector2(0, -220), new Vector2(380, 22),
            9, FontStyle.Normal,
            new Color(C_DIM.r, C_DIM.g, C_DIM.b, 0.5f),
            TextAnchor.MiddleCenter);
    }

    // ── 이벤트 처리 ──────────────────────────────────────────────────────
    void OnLoginClick()
    {
        string ip = ipField.text.Trim();
        string id = idField.text.Trim();
        string pw = pwField.text.Trim();

        if (string.IsNullOrEmpty(id)) { SetStatus("CALL SIGN required", true); return; }
        if (string.IsNullOrEmpty(pw)) { SetStatus("ACCESS CODE required", true); return; }

        if (!string.IsNullOrEmpty(ip) && GameManager.Instance != null)
            GameManager.Instance.serverIP = ip;

        loginBtn.interactable = false;
        SetStatus("Authenticating...", false);

        // 이미 연결 중이면 재연결을 위해 먼저 끊기
        if (NetworkManager.Instance?.socketClient?.IsConnected() == true)
            NetworkManager.Instance.socketClient.Disconnect();

        try
        {
            NetworkManager.Instance.socketClient.Login(id, pw);
        }
        catch (System.Exception e)
        {
            SetStatus($"Connection failed: {e.Message}", true);
            loginBtn.interactable = true;
        }
    }

    void HandleLoginResult(LoginResultPacket packet)
    {
        if (packet.success)
        {
            SetStatus("Authentication successful. Loading...", false);
            StartCoroutine(LoadLobby());
        }
        else
        {
            SetStatus($"ACCESS DENIED  — {packet.message}", true);
            loginBtn.interactable = true;
        }
    }

    IEnumerator LoadLobby()
    {
        yield return new WaitForSeconds(0.4f);
        SceneManager.LoadScene("LobbyScene");
    }

    void SetStatus(string msg, bool warn)
    {
        if (statusText == null) return;
        statusText.text  = msg;
        statusText.color = warn ? C_WARN : C_DIM;
    }

    // ── UI 헬퍼 ──────────────────────────────────────────────────────────
    RectTransform MakeImage(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        return rt;
    }

    Text MakeText(Transform parent, string content, Vector2 pos, Vector2 size,
                  int fontSize, FontStyle style, Color color, TextAnchor anchor)
    {
        var go = new GameObject("T_" + content[..Mathf.Min(content.Length, 12)]);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        var txt = go.AddComponent<Text>();
        txt.text      = content;
        txt.font      = uiFont;
        txt.fontSize  = fontSize;
        txt.fontStyle = style;
        txt.color     = color;
        txt.alignment = anchor;
        txt.raycastTarget = false;
        return txt;
    }

    void MakeFieldLabel(Transform parent, string label, Vector2 pos)
    {
        MakeText(parent, label, pos, new Vector2(380, 22),
            10, FontStyle.Bold, C_ACCENT, TextAnchor.MiddleLeft);
    }

    InputField MakeInputField(Transform parent, string placeholder, Vector2 pos, Vector2 size,
                               bool isPassword = false)
    {
        var go = new GameObject("Input_" + placeholder[..Mathf.Min(placeholder.Length, 8)]);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;

        var img = go.AddComponent<Image>();
        img.color = C_INPUT_BG;

        var ol = go.AddComponent<Outline>();
        ol.effectColor    = C_BORDER;
        ol.effectDistance = new Vector2(1, -1);

        var field = go.AddComponent<InputField>();
        if (isPassword) field.contentType = InputField.ContentType.Password;

        // Placeholder
        var phGO  = new GameObject("Placeholder");
        phGO.transform.SetParent(go.transform, false);
        var phTxt = phGO.AddComponent<Text>();
        phTxt.text      = placeholder;
        phTxt.font      = uiFont;
        phTxt.fontSize  = 14;
        phTxt.color     = new Color(C_DIM.r, C_DIM.g, C_DIM.b, 0.7f);
        phTxt.fontStyle = FontStyle.Italic;
        var phRT = phTxt.rectTransform;
        phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
        phRT.offsetMin = new Vector2(12, 2); phRT.offsetMax = new Vector2(-12, -2);

        // Text
        var txtGO  = new GameObject("Text");
        txtGO.transform.SetParent(go.transform, false);
        var txt = txtGO.AddComponent<Text>();
        txt.font      = uiFont;
        txt.fontSize  = 14;
        txt.color     = C_TEXT;
        var txtRT = txt.rectTransform;
        txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(12, 2); txtRT.offsetMax = new Vector2(-12, -2);

        field.textComponent = txt;
        field.placeholder   = phTxt;
        return field;
    }

    Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;

        var img = go.AddComponent<Image>();
        img.color = color;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.normalColor      = color;
        cb.highlightedColor = new Color(color.r * 1.25f, color.g * 1.25f, color.b * 1.25f);
        cb.pressedColor     = new Color(color.r * 0.65f, color.g * 0.65f, color.b * 0.65f);
        cb.colorMultiplier  = 1f;
        btn.colors = cb;

        var txtGO = new GameObject("Label");
        txtGO.transform.SetParent(go.transform, false);
        var txt = txtGO.AddComponent<Text>();
        txt.text      = label;
        txt.font      = uiFont;
        txt.fontSize  = 15;
        txt.fontStyle = FontStyle.Bold;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color     = new Color(0.04f, 0.08f, 0.04f);
        var tRT = txt.rectTransform;
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = tRT.offsetMax = Vector2.zero;

        return btn;
    }
}

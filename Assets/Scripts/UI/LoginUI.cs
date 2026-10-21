using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 로그인 씬 UI — Premium Military Flight Simulator 디자인.
/// 코드만으로 블루 그라데이션 하늘 + 글래스 로그인 패널 + 대형 KF-21 타이포 구현.
/// 모든 색상/간격은 UITheme (Resources/UITheme.asset)에서 중앙 관리.
/// </summary>
public class LoginUI : MonoBehaviour
{
    Font uiFont;

    InputField ipField;
    InputField idField;
    InputField pwField;
    Text       statusText;
    UIThemedButton loginBtn;

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
        var sc = NetworkManager.Instance?.socketClient;
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
        if (sc != null) sc.OnLoginResult += HandleLoginResult;
    }

    // ── UI 빌드 ──────────────────────────────────────────────────────────
    void BuildUI()
    {
        var t = UITheme.Instance;

        var canvasGO = new GameObject("LoginCanvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        BuildSkyBackground(canvasGO.transform, t);
        BuildBigTypography(canvasGO.transform, t);
        BuildLoginCard(canvasGO.transform, t);
        BuildTopBar(canvasGO.transform, t);
        BuildFooter(canvasGO.transform, t);
    }

    // ── 1. 블루 그라데이션 하늘 배경 (Procedural Texture) ─────────────────
    void BuildSkyBackground(Transform parent, UITheme t)
    {
        // 전체 그라데이션: 하단 진한 네이비 → 상단 밝은 블루
        var sky = MakeImage(parent, "SkyGradient", Vector2.zero, Vector2.zero, Color.white);
        sky.anchorMin = Vector2.zero; sky.anchorMax = Vector2.one;
        sky.offsetMin = sky.offsetMax = Vector2.zero;

        var skyImg = sky.GetComponent<Image>();
        skyImg.sprite = CreateVerticalGradient(
            bottom: HexC("#030810"),
            mid:    HexC("#0A1B2D"),
            top:    HexC("#1A3B5F"));
        skyImg.type = Image.Type.Simple;
        skyImg.preserveAspect = false;

        // 수평 블루 글로우 (수평선 느낌)
        var horizonGlow = MakeImage(parent, "HorizonGlow", new Vector2(0f, -40f), new Vector2(0f, 200f),
            new Color(t.primaryBlue.r, t.primaryBlue.g, t.primaryBlue.b, 0.15f));
        horizonGlow.anchorMin = new Vector2(0f, 0.4f);
        horizonGlow.anchorMax = new Vector2(1f, 0.4f);
        horizonGlow.pivot     = new Vector2(0.5f, 0.5f);

        // 상단 Accent 라인
        var topLine = MakeImage(parent, "TopLine", new Vector2(0f, -1f), new Vector2(0f, 1.5f),
            new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, 0.4f));
        topLine.anchorMin = new Vector2(0f, 1f);
        topLine.anchorMax = new Vector2(1f, 1f);
        topLine.pivot     = new Vector2(0.5f, 1f);

        // 미세 격자 — 큰 간격으로 (장식)
        for (int i = 0; i < 6; i++)
        {
            float y = -200f + i * 160f;
            var line = MakeImage(parent, $"GridH{i}", new Vector2(0f, y), new Vector2(0f, 1f),
                new Color(1f, 1f, 1f, 0.025f));
            line.anchorMin = new Vector2(0f, 0.5f);
            line.anchorMax = new Vector2(1f, 0.5f);
        }

        // 중앙 비네트 (중앙에서 바깥으로 어두워지는 느낌 흉내)
        var vignetteL = MakeImage(parent, "VignetteL", Vector2.zero, Vector2.zero,
            new Color(0f, 0f, 0f, 0.3f));
        vignetteL.anchorMin = new Vector2(0f, 0f);
        vignetteL.anchorMax = new Vector2(0.2f, 1f);
        vignetteL.offsetMin = vignetteL.offsetMax = Vector2.zero;

        var vignetteR = MakeImage(parent, "VignetteR", Vector2.zero, Vector2.zero,
            new Color(0f, 0f, 0f, 0.3f));
        vignetteR.anchorMin = new Vector2(0.8f, 0f);
        vignetteR.anchorMax = new Vector2(1f, 1f);
        vignetteR.offsetMin = vignetteR.offsetMax = Vector2.zero;

        // 상·하 비네트
        var topDark = MakeImage(parent, "TopDark", Vector2.zero, Vector2.zero,
            new Color(0f, 0f, 0f, 0.4f));
        topDark.anchorMin = new Vector2(0f, 0.85f);
        topDark.anchorMax = new Vector2(1f, 1f);
        topDark.offsetMin = topDark.offsetMax = Vector2.zero;

        var botDark = MakeImage(parent, "BotDark", Vector2.zero, Vector2.zero,
            new Color(0f, 0f, 0f, 0.5f));
        botDark.anchorMin = new Vector2(0f, 0f);
        botDark.anchorMax = new Vector2(1f, 0.18f);
        botDark.offsetMin = botDark.offsetMax = Vector2.zero;
    }

    // ── 2. 대형 KF-21 타이포 (중앙 배경) ─────────────────────────────────
    void BuildBigTypography(Transform parent, UITheme t)
    {
        // 매우 큰 "KF-21" 반투명 타이포 — 배경 장식
        var bigGO = new GameObject("BigTitle");
        bigGO.transform.SetParent(parent, false);
        var brt = bigGO.AddComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.anchoredPosition = new Vector2(220f, 100f);
        brt.sizeDelta = new Vector2(1400f, 400f);
        var bigTx = bigGO.AddComponent<Text>();
        bigTx.font = uiFont; bigTx.fontSize = 340;
        bigTx.alignment = TextAnchor.MiddleCenter;
        bigTx.fontStyle = FontStyle.Bold;
        bigTx.color = new Color(1f, 1f, 1f, 0.05f);
        bigTx.text = "KF-21";
        bigTx.raycastTarget = false;

        // 서브 타이포
        var subBigGO = new GameObject("BigSubtitle");
        subBigGO.transform.SetParent(parent, false);
        var srt = subBigGO.AddComponent<RectTransform>();
        srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
        srt.anchoredPosition = new Vector2(220f, -140f);
        srt.sizeDelta = new Vector2(1200f, 60f);
        var subTx = subBigGO.AddComponent<Text>();
        subTx.font = uiFont; subTx.fontSize = 42;
        subTx.alignment = TextAnchor.MiddleCenter;
        subTx.fontStyle = FontStyle.Normal;
        subTx.color = new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, 0.4f);
        subTx.text = "B O R A M A E";
        subTx.raycastTarget = false;

        // 양쪽 세로 라인 (큰 타이포 좌우 장식)
        var leftV = MakeImage(parent, "BigV_L", new Vector2(-180f, 100f), new Vector2(1.5f, 300f),
            new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, 0.3f));
        leftV.anchorMin = leftV.anchorMax = new Vector2(0.5f, 0.5f);

        var rightV = MakeImage(parent, "BigV_R", new Vector2(620f, 100f), new Vector2(1.5f, 300f),
            new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, 0.3f));
        rightV.anchorMin = rightV.anchorMax = new Vector2(0.5f, 0.5f);

        bigGO.AddComponent<UIFadeIn>().Configure(0.1f, t.heroTransition, 0f);
    }

    // ── 3. 중앙 로그인 카드 ───────────────────────────────────────────────
    void BuildLoginCard(Transform parent, UITheme t)
    {
        float cardW = 440f, cardH = 580f;
        var card = MakeImage(parent, "LoginCard", new Vector2(-460f, 0f), new Vector2(cardW, cardH),
            new Color(t.panelBG.r, t.panelBG.g, t.panelBG.b, 0.92f));
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        AddOutline(card.gameObject, new Color(t.border.r, t.border.g, t.border.b, 0.8f), 1.5f);

        // 상단 Accent bar
        var accentBar = MakeImage(card, "AccentBar",
            new Vector2(0f, cardH * 0.5f - 2f), new Vector2(cardW, 3f), t.accentCyan);
        accentBar.anchorMin = accentBar.anchorMax = new Vector2(0.5f, 0.5f);

        // 좌측 세로 Accent bar
        var leftBar = MakeImage(card, "LeftBar",
            new Vector2(-cardW * 0.5f + 2f, 0f), new Vector2(3f, cardH),
            new Color(t.accentCyan.r, t.accentCyan.g, t.accentCyan.b, 0.6f));
        leftBar.anchorMin = leftBar.anchorMax = new Vector2(0.5f, 0.5f);

        // 상단 라벨 (SIGN IN)
        AddTextCentered(card, "SIGN IN", new Vector2(0f, cardH * 0.5f - 44f),
            new Vector2(cardW - 60f, 20f), t.textXS, t.accentCyan, FontStyle.Bold);

        // 큰 타이틀
        AddTextCentered(card, "KF-21", new Vector2(0f, cardH * 0.5f - 100f),
            new Vector2(cardW - 60f, 60f), t.titleL, t.textPrimary, FontStyle.Bold);

        // 서브
        AddTextCentered(card, "BORAMAE  •  FLIGHT COMBAT SIMULATION",
            new Vector2(0f, cardH * 0.5f - 140f), new Vector2(cardW - 60f, 18f),
            t.textXS, t.textSecondary, FontStyle.Normal);

        // 구분선
        var divider = MakeImage(card, "Divider",
            new Vector2(0f, cardH * 0.5f - 174f), new Vector2(cardW - 60f, 1f),
            new Color(t.border.r, t.border.g, t.border.b, 0.6f));
        divider.anchorMin = divider.anchorMax = new Vector2(0.5f, 0.5f);

        // 입력 필드 3개
        float fieldY = cardH * 0.5f - 220f;
        ipField = BuildField(card, "SERVER IP",   "172.30.201.117", fieldY,        cardW - 60f, false, t);
        idField = BuildField(card, "CALL SIGN",   "",               fieldY -  80f, cardW - 60f, false, t);
        pwField = BuildField(card, "ACCESS CODE", "",               fieldY - 160f, cardW - 60f, true,  t);

        // 상태 메시지
        statusText = AddTextCentered(card, "", new Vector2(0f, fieldY - 220f),
            new Vector2(cardW - 60f, 22f), t.textS, t.textSecondary, FontStyle.Normal);

        // AUTHENTICATE 버튼
        loginBtn = BuildPrimaryButton(card, "AUTHENTICATE",
            new Vector2(0f, fieldY - 275f), new Vector2(cardW - 60f, t.buttonHeightL), t);
        loginBtn.OnClick = OnLoginClick;

        // 하단 안내
        AddTextCentered(card, "ENTER 키로 바로 로그인",
            new Vector2(0f, -cardH * 0.5f + 28f), new Vector2(cardW, 18f),
            t.textXS, t.textDisabled, FontStyle.Normal);

        card.gameObject.AddComponent<UIFadeIn>().Configure(0.2f, t.heroTransition, 24f);
    }

    void BuildTopBar(Transform parent, UITheme t)
    {
        var bar = MakeEmpty(parent, "TopBar", Vector2.zero, new Vector2(0f, 70f));
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot     = new Vector2(0.5f, 1f);
        bar.offsetMin = new Vector2(0f, -70f);
        bar.offsetMax = new Vector2(0f, 0f);

        // 좌측 뱃지
        var badge = MakeImage(bar, "Badge", new Vector2(40f, 0f), new Vector2(170f, 40f),
            new Color(t.panelBG.r, t.panelBG.g, t.panelBG.b, 0.7f));
        badge.anchorMin = new Vector2(0f, 0.5f);
        badge.anchorMax = new Vector2(0f, 0.5f);
        badge.pivot     = new Vector2(0f, 0.5f);
        AddOutline(badge.gameObject, t.borderAccent, 1f);
        AddTextFill(badge, "KF-21  •  AUTH", t.textS, t.accentCyan, FontStyle.Bold);

        // 우측 상태 표시
        var status = MakeImage(bar, "StatusChip", new Vector2(-40f, 0f), new Vector2(160f, 32f),
            new Color(t.success.r, t.success.g, t.success.b, 0.12f));
        status.anchorMin = new Vector2(1f, 0.5f);
        status.anchorMax = new Vector2(1f, 0.5f);
        status.pivot     = new Vector2(1f, 0.5f);
        AddOutline(status.gameObject, new Color(t.success.r, t.success.g, t.success.b, 0.6f), 1f);
        AddTextFill(status, "● SERVER ONLINE", t.textXS, t.success, FontStyle.Bold);

        bar.gameObject.AddComponent<UIFadeIn>().Configure(0.05f, t.panelTransition, 6f);
    }

    void BuildFooter(Transform parent, UITheme t)
    {
        var leftGO = new GameObject("FooterLeft");
        leftGO.transform.SetParent(parent, false);
        var lrt = leftGO.AddComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f, 0f);
        lrt.anchorMax = new Vector2(0f, 0f);
        lrt.pivot     = new Vector2(0f, 0f);
        lrt.anchoredPosition = new Vector2(40f, 24f);
        lrt.sizeDelta = new Vector2(400f, 18f);
        var lt = leftGO.AddComponent<Text>();
        lt.font = uiFont; lt.fontSize = t.textXS;
        lt.color = t.textDisabled; lt.alignment = TextAnchor.MiddleLeft;
        lt.text = "VERSION 0.9.0  •  BUILD 20261020";
        lt.raycastTarget = false;

        var rightGO = new GameObject("FooterRight");
        rightGO.transform.SetParent(parent, false);
        var rrt = rightGO.AddComponent<RectTransform>();
        rrt.anchorMin = new Vector2(1f, 0f);
        rrt.anchorMax = new Vector2(1f, 0f);
        rrt.pivot     = new Vector2(1f, 0f);
        rrt.anchoredPosition = new Vector2(-40f, 24f);
        rrt.sizeDelta = new Vector2(400f, 18f);
        var rt = rightGO.AddComponent<Text>();
        rt.font = uiFont; rt.fontSize = t.textXS;
        rt.color = t.textDisabled; rt.alignment = TextAnchor.MiddleRight;
        rt.text = "© 2026 KF-21 FLIGHT COMBAT SIMULATION";
        rt.raycastTarget = false;

        leftGO.AddComponent<UIFadeIn>().Configure(0.3f, t.panelTransition, 0f);
        rightGO.AddComponent<UIFadeIn>().Configure(0.3f, t.panelTransition, 0f);
    }

    // ── 공통 빌더 ───────────────────────────────────────────────────────
    InputField BuildField(RectTransform parent, string label, string initialValue,
                           float y, float width, bool password, UITheme t)
    {
        // 레이블
        var lblGO = new GameObject(label + "_Lbl");
        lblGO.transform.SetParent(parent, false);
        var lrt = lblGO.AddComponent<RectTransform>();
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
        lrt.anchoredPosition = new Vector2(-width * 0.5f, y + 24f);
        lrt.sizeDelta = new Vector2(width, 16f);
        lrt.pivot = new Vector2(0f, 0.5f);
        var lbl = lblGO.AddComponent<Text>();
        lbl.font = uiFont; lbl.fontSize = t.textXS;
        lbl.color = t.textSecondary; lbl.alignment = TextAnchor.MiddleLeft;
        lbl.fontStyle = FontStyle.Bold;
        lbl.text = label;
        lbl.raycastTarget = false;

        // 필드 배경
        var bg = MakeImage(parent, label + "_BG", new Vector2(0f, y - 4f), new Vector2(width, 44f),
            new Color(0f, 0f, 0f, 0.5f));
        bg.anchorMin = bg.anchorMax = new Vector2(0.5f, 0.5f);
        AddOutline(bg.gameObject, t.border, 1f);

        var fieldGO = new GameObject(label);
        fieldGO.transform.SetParent(bg.transform, false);
        var frt = fieldGO.AddComponent<RectTransform>();
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(14f, 0f); frt.offsetMax = new Vector2(-14f, 0f);

        var inputField = fieldGO.AddComponent<InputField>();

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(fieldGO.transform, false);
        var trt = textGO.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var txt = textGO.AddComponent<Text>();
        txt.font = uiFont; txt.fontSize = t.textM;
        txt.color = t.textPrimary; txt.alignment = TextAnchor.MiddleLeft;
        txt.supportRichText = false;

        var placeGO = new GameObject("Placeholder");
        placeGO.transform.SetParent(fieldGO.transform, false);
        var prt = placeGO.AddComponent<RectTransform>();
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        var ptxt = placeGO.AddComponent<Text>();
        ptxt.font = uiFont; ptxt.fontSize = t.textM;
        ptxt.color = t.textDisabled;
        ptxt.fontStyle = FontStyle.Italic;
        ptxt.alignment = TextAnchor.MiddleLeft;
        ptxt.text = password ? "••••••••" : (string.IsNullOrEmpty(initialValue) ? "입력하세요" : "");

        inputField.textComponent = txt;
        inputField.placeholder   = ptxt;
        inputField.text          = initialValue;
        inputField.contentType   = password ? InputField.ContentType.Password : InputField.ContentType.Standard;

        return inputField;
    }

    UIThemedButton BuildPrimaryButton(RectTransform parent, string label,
                                       Vector2 pos, Vector2 size, UITheme t)
    {
        var btnGO = new GameObject("Btn_" + label);
        btnGO.transform.SetParent(parent, false);
        var rt = btnGO.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;

        btnGO.AddComponent<Image>();
        var outline = btnGO.AddComponent<Outline>();
        outline.effectDistance = new Vector2(1.5f, 1.5f);

        AddTextFill(rt, label, t.textL, t.textPrimary, FontStyle.Bold);

        var btn = btnGO.AddComponent<UIThemedButton>();
        btn.ButtonStyle = UIThemedButton.Style.Primary;
        return btn;
    }

    // ── 로그인 처리 ──────────────────────────────────────────────────────
    void OnLoginClick()
    {
        string ip = ipField.text.Trim();
        string id = idField.text.Trim();
        string pw = pwField.text.Trim();

        if (string.IsNullOrEmpty(id)) { SetStatus("CALL SIGN이 필요합니다", true); return; }
        if (string.IsNullOrEmpty(pw)) { SetStatus("ACCESS CODE가 필요합니다", true); return; }

        if (!string.IsNullOrEmpty(ip) && GameManager.Instance != null)
            GameManager.Instance.serverIP = ip;

        loginBtn.Interactable = false;
        SetStatus("인증 중…", false);

        if (NetworkManager.Instance?.socketClient?.IsConnected() == true)
            NetworkManager.Instance.socketClient.Disconnect();

        try
        {
            NetworkManager.Instance.socketClient.Login(id, pw);
        }
        catch (System.Exception e)
        {
            SetStatus($"접속 실패: {e.Message}", true);
            loginBtn.Interactable = true;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return) && loginBtn != null && loginBtn.Interactable)
            OnLoginClick();
    }

    void HandleLoginResult(LoginResultPacket packet)
    {
        if (packet.success)
        {
            SetStatus("인증 성공 — 로비로 이동 중…", false);
            StartCoroutine(LoadLobby());
        }
        else
        {
            SetStatus($"접속 거부 — {packet.message}", true);
            loginBtn.Interactable = true;
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
        var t = UITheme.Instance;
        statusText.text  = msg;
        statusText.color = warn ? t.error : t.textSecondary;
    }

    // ── UI 헬퍼 ──────────────────────────────────────────────────────────
    RectTransform MakeImage(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = color; img.raycastTarget = false;
        return rt;
    }

    RectTransform MakeImage(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color)
        => MakeImage((Transform)parent, name, pos, size, color);

    RectTransform MakeEmpty(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    Text AddTextCentered(Transform parent, string content, Vector2 pos, Vector2 size,
                          int sz, Color color, FontStyle style)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var tx = go.AddComponent<Text>();
        tx.font = uiFont; tx.fontSize = sz;
        tx.color = color; tx.alignment = TextAnchor.MiddleCenter;
        tx.fontStyle = style; tx.text = content;
        tx.raycastTarget = false;
        return tx;
    }

    Text AddTextFill(Transform parent, string content, int sz, Color color, FontStyle style)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var tx = go.AddComponent<Text>();
        tx.font = uiFont; tx.fontSize = sz;
        tx.color = color; tx.alignment = TextAnchor.MiddleCenter;
        tx.fontStyle = style; tx.text = content;
        tx.raycastTarget = false;
        return tx;
    }

    void AddOutline(GameObject target, Color col, float width)
    {
        var o = target.AddComponent<Outline>();
        o.effectColor = col;
        o.effectDistance = new Vector2(width, width);
    }

    // ── Procedural Vertical Gradient (3단: 하단→중간→상단) ────────────────
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
                float a = u / 0.5f;
                a = a * a * (3f - 2f * a); // smoothstep
                c = Color.Lerp(bottom, mid, a);
            }
            else
            {
                float a = (u - 0.5f) / 0.5f;
                a = a * a * (3f - 2f * a);
                c = Color.Lerp(mid, top, a);
            }
            tex.SetPixel(0, y, c);
            tex.SetPixel(1, y, c);
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

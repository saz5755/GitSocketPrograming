using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// GameScene 의 빈 GameObject 에 붙입니다.
/// ESC 키로 미션 일시정지 메뉴를 열고 닫습니다.
/// </summary>
public class InGameMenuUI : MonoBehaviour
{
    static readonly Color C_OVERLAY  = new Color(0.02f, 0.04f, 0.08f, 0.82f);
    static readonly Color C_PANEL    = new Color(0.055f, 0.075f, 0.115f, 0.97f);
    static readonly Color C_ACCENT   = new Color(0.00f,  0.78f,  0.33f);
    static readonly Color C_TEXT     = new Color(0.91f,  0.93f,  0.94f);
    static readonly Color C_DIM      = new Color(0.36f,  0.47f,  0.56f);
    static readonly Color C_DANGER   = new Color(0.80f,  0.22f,  0.18f);
    static readonly Color C_BORDER   = new Color(0.10f,  0.20f,  0.30f);

    Font uiFont;
    GameObject overlayRoot;
    Text pilotText;
    Text zoneText;
    bool menuOpen;

    void Awake()
    {
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    void Start()
    {
        BuildUI();
        SetMenuVisible(false);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            SetMenuVisible(!menuOpen);
    }

    void SetMenuVisible(bool visible)
    {
        menuOpen = visible;
        if (overlayRoot != null) overlayRoot.SetActive(visible);

        // 메뉴 열리면 파일럿·존 정보 갱신
        if (visible) RefreshInfo();

        // 커서 표시/숨김
        Cursor.visible   = visible;
        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
    }

    void RefreshInfo()
    {
        string pilot = GameManager.Instance?.myNickname ?? "UNKNOWN";
        int    zone  = GameManager.Instance?.currentRoomId ?? 0;
        if (pilotText != null) pilotText.text = $"PILOT    {pilot.ToUpper()}";
        if (zoneText  != null) zoneText.text  = zone > 0 ? $"ZONE     {zone:D2}" : "ZONE     —";
    }

    // ── UI 빌드 ──────────────────────────────────────────────────────────
    void BuildUI()
    {
        // Canvas (기존 씬 Canvas 위에 별도로 띄움)
        var canvasGO = new GameObject("PauseCanvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        overlayRoot = canvasGO;

        // 반투명 전체 오버레이
        var overlay = MakeFill(canvasGO.transform, "Overlay", C_OVERLAY);
        overlay.raycastTarget = true; // 뒤쪽 클릭 차단

        // 중앙 패널 (360 × 440)
        var panel = MakeRectAt("Panel", canvasGO.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(360, 440));
        MakeImg(panel, C_PANEL);

        // 상단 액센트 바
        var topBar = MakeRect("TopBar", panel, new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -3), new Vector2(0, 0));
        MakeImg(topBar, C_ACCENT).raycastTarget = false;

        // 왼쪽 수직 바
        var leftBar = MakeRect("LeftBar", panel, new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(0, 0), new Vector2(3, 0));
        MakeImg(leftBar, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.4f)).raycastTarget = false;

        // 제목
        MakeText(panel, "MISSION PAUSED",
            new Vector2(0, -46), new Vector2(320, 36),
            22, FontStyle.Bold, C_ACCENT, TextAnchor.MiddleCenter);

        MakeText(panel, "▬▬▬  STATUS REPORT  ▬▬▬",
            new Vector2(0, -80), new Vector2(320, 22),
            11, FontStyle.Normal,
            new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.5f),
            TextAnchor.MiddleCenter);

        // 구분선
        var div1 = MakeRectAt("Div1", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 80), new Vector2(300, 1));
        MakeImg(div1, new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.8f)).raycastTarget = false;

        // 파일럿 / 존 정보 박스
        var infoBox = MakeRectAt("InfoBox", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 36), new Vector2(300, 70));
        MakeImg(infoBox, new Color(0, 0, 0, 0.3f)).raycastTarget = false;

        pilotText = MakeText(infoBox, "PILOT    —",
            new Vector2(0, 14), new Vector2(280, 24),
            14, FontStyle.Bold, C_TEXT, TextAnchor.MiddleLeft);
        zoneText = MakeText(infoBox, "ZONE     —",
            new Vector2(0, -12), new Vector2(280, 24),
            14, FontStyle.Bold, C_DIM, TextAnchor.MiddleLeft);

        // 구분선 2
        var div2 = MakeRectAt("Div2", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, -14), new Vector2(300, 1));
        MakeImg(div2, new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.6f)).raycastTarget = false;

        // CONTINUE MISSION 버튼
        var continueBtn = MakeButton(panel,
            "▶  CONTINUE MISSION",
            new Vector2(0, -70), new Vector2(300, 50), C_ACCENT);
        continueBtn.onClick.AddListener(() => SetMenuVisible(false));

        // RETURN TO BASE 버튼
        var returnBtn = MakeButton(panel,
            "⬡  RETURN TO BASE",
            new Vector2(0, -134), new Vector2(300, 50), C_DANGER);
        returnBtn.onClick.AddListener(OnReturnToBase);

        // 하단 안내
        MakeText(panel, "[ ESC ]  Toggle Menu",
            new Vector2(0, -196), new Vector2(300, 22),
            10, FontStyle.Normal,
            new Color(C_DIM.r, C_DIM.g, C_DIM.b, 0.5f),
            TextAnchor.MiddleCenter);
    }

    void OnReturnToBase()
    {
        Cursor.visible   = true;
        Cursor.lockState = CursorLockMode.None;
        NetworkManager.Instance?.socketClient.LeaveRoom();
        StartCoroutine(GoToLobby());
    }

    IEnumerator GoToLobby()
    {
        yield return new WaitForSeconds(0.2f);
        SceneManager.LoadScene("LobbyScene");
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────
    Image MakeFill(Transform parent, string name, Color color)
    {
        var go  = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    RectTransform MakeRect(string name, Transform parent,
        Vector2 ancMin, Vector2 ancMax, Vector2 offMin, Vector2 offMax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = ancMin; rt.anchorMax = ancMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
        return rt;
    }

    RectTransform MakeRectAt(string name, Transform parent,
        Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot     = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        return rt;
    }

    Image MakeImg(RectTransform rt, Color color)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    Text MakeText(RectTransform parent, string content, Vector2 pos, Vector2 size,
                  int fs, FontStyle style, Color color, TextAnchor anchor)
    {
        var go  = new GameObject("T");
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var txt = go.AddComponent<Text>();
        txt.text      = content; txt.font      = uiFont;
        txt.fontSize  = fs;     txt.fontStyle = style;
        txt.color     = color;  txt.alignment = anchor;
        txt.raycastTarget = false;
        return txt;
    }

    Button MakeButton(RectTransform parent, string label, Vector2 pos, Vector2 size, Color color)
    {
        var go  = new GameObject("Btn");
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.normalColor      = color;
        cb.highlightedColor = new Color(color.r * 1.3f, color.g * 1.3f, color.b * 1.3f);
        cb.pressedColor     = new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f);
        cb.colorMultiplier  = 1f;
        btn.colors = cb;
        var txtGO = new GameObject("L");
        txtGO.transform.SetParent(go.transform, false);
        var txt = txtGO.AddComponent<Text>();
        txt.text = label; txt.font = uiFont; txt.fontSize = 15;
        txt.fontStyle = FontStyle.Bold;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = C_TEXT;
        var tRT = txt.rectTransform;
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = tRT.offsetMax = Vector2.zero;
        return btn;
    }
}
